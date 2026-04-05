# Training / Learning Improvements

## Current Approach (Frequency-Based Averaging)

Games are played. After each batch, `CalculateHeuristics()` iterates every playbook entry in parallel (`Parallel.ForEach` with thread-local accumulators, merged under lock), calling `TrainSingle()` which associates the game's final piece-count score with every board pattern/feature observed during that game.

- **Score** = `TotalScore / Count` (arithmetic mean of all final scores seen when a pattern appeared). No discounting for how early/late in the game the pattern was observed.
- **Weighting** (`CalculateWeights`): each pattern class gets a weight per piece-count based on average Confidence (currently hardcoded to 1.0) scaled by coverage of the pattern space, then sigmoid-squashed and normalized. Full rebuild from the entire playbook every training cycle.

## Weaknesses of Current Approach

- **No temporal credit assignment:** a pattern seen on move 5 gets the same final score as one on move 55. Early patterns have very weak correlation with the outcome.
- **Confidence is disabled** (returns 1.0), so all pattern classes are weighted equally regardless of sample quality.
- **Full rebuild each round:** heuristics are cleared and recomputed from the entire playbook every training cycle.
- **Score = simple average:** doesn't account for recency, opponent strength, or how decisive the pattern was.
- **No regularization:** low-count patterns (seen 1–2 times) get the same treatment as patterns seen thousands of times, making the eval noisy for rare positions.

## CalculateScore Improvements

These improve how `HeuristicData.CalculateScore()` converts the per-pattern stats (Count, WinCount, LossCount, TotalWinScore, TotalLossScore, TotalScore) into a single score value. They are drop-in replacements that don't require changes to training or the playbook.

### Probability-Weighted Expected Margin (recommended first step)

Separate the two questions the current simple average conflates: "how often does this pattern win?" and "by how much?" Combine a Bayesian win probability estimate with the average margin conditioned on outcome:

```
draws = Count - WinCount - LossCount
adjWins = WinCount + draws / 2
winProb = (adjWins + k/2) / (Count + k)         // Bayesian, shrinks toward 0.5
avgWinMargin = TotalWinScore / Max(WinCount, 1)
avgLossMargin = TotalLossScore / Max(LossCount, 1)  // negative
score = winProb * avgWinMargin + (1 - winProb) * avgLossMargin
```

The pseudocount `k` (~ 5–10, tune empirically) regularizes low-count patterns toward 0. This uses all the data already tracked in `HeuristicData` and naturally handles the asymmetry where losses tend to have larger magnitude than wins.

### Win Probability Model

Predict probability of winning rather than raw piece-count spread. This is what strong Othello engines (Edax, Zebra) do, and the commented-out code in `CalculateScore()` was heading toward.

```
draws = Count - WinCount - LossCount
effectiveWins = WinCount + draws / 2
winProbability = effectiveWins / Count
score = winProbability * ScoreMultiplier    // map [0,1] → integer range
```

Raw spread averaging is noisy — a +20 blowout and a +2 squeaker both count as "good" but the blowout distorts the mean. Win probability treats them equally, better reflecting what the search cares about (find the move that wins most often).

### Bayesian Regularization

Add a prior that pulls low-count estimates toward zero:

```
score = TotalScore / (Count + pseudoCount)
```

A pattern seen 2 times with scores +10, +12 gets Score=11 under simple averaging, which looks great but is unreliable. With pseudoCount ~ 5–10, this shrinks toward neutral. One line of code. Composes with any of the other scoring approaches.

### Wilson Score Lower Bound

For the win-probability variant, use the lower bound of a confidence interval instead of the raw win rate:

```
p = effectiveWins / Count
z = 1.0  // ~68% confidence, tune this
wilsonLower = (p + z²/(2n) - z * sqrt(p(1-p)/n + z²/(4n²))) / (1 + z²/n)
```

Naturally penalizes low-sample patterns without needing an explicit pseudocount. Well-known technique from ranking systems (Reddit, Yelp, etc.).

## PatternScoreSlow Combination

`PatternScoreSlow` combines per-pattern scores via weighted sum: `Σ data.Score * weight / 8`. With log-odds scoring (see above), this sum is a logistic regression — evidence from independent patterns accumulates correctly.

### Two-Track Expected Margin Estimation (tried, rejected for performance)

Instead of returning raw log-odds, compute an expected piece-count margin by running two parallel tracks during the `PatternScoreSlow` loop:

1. **Win/loss log-odds tracks**: For each pattern, compute per-pattern win and loss probabilities (Dirichlet prior with k/3 per W/D/L outcome), convert to log-odds, and accumulate weighted sums independently.
2. **Margin track**: Weighted average of per-pattern `TotalWinScore/WinCount` and `TotalLossScore/LossCount`.

After the loop, convert the two log-odds sums to probabilities via logistic sigmoid, normalize so W+D+L=1, then combine: `winProb * avgWinMargin + lossProb * avgLossMargin`.

This gives an expected margin in piece-count-spread units with principled probability combination. However, it was **over 50% slower** than the simple log-odds sum due to the extra per-pattern `Math.Log` calls (two instead of zero — `data.Score` already contains log-odds), the `Math.Clamp` calls, and the additional running sums. Since `PatternScoreSlow` is the innermost function in the search, this is a significant cost. The pure log-odds approach is preferred unless margin information proves necessary for search quality.

## CalculateWeights / Confidence Improvements

### Re-enable and Fix Confidence

The disabled `Confidence` property (returns 1.0) was trying to weight patterns by how decisive they are. The old implementation had issues: it was unsigned (didn't account for direction) and used an unusual double-sigmoid. A cleaner version:

```
signedWinRate = (WinCount - LossCount) / Count    // [-1, +1]
sampleStrength = 1 - 1 / (1 + Count / k)          // [0, 1), k ~ 10–20
confidence = signedWinRate * sampleStrength
```

High confidence = pattern consistently predicts one side winning AND has enough samples. Feeds directly into `CalculateWeights` to down-weight noisy pattern classes.

### Serialize PatternClassWeights

`ReadHeuristics` loads `FeatureCoefficients` and `PatternClassScores` but resets `PatternClassWeights` to uniform (`1.0 / numClasses`). The feature coefficients were trained jointly with specific pattern weights in `CalculateWeights`, so after a load-only path (no `CalculateHeuristics`), `PatternScoreSlow` uses mismatched weights. Serializing `PatternClassWeights` alongside `FeatureCoefficients` would make the params file self-contained, removing the need to rerun logistic regression after loading.

## Training-Time Improvements

These require changes to `TrainSingle`, the game loop, or `CalculateWeights`. Larger scope than the scoring changes above.

### Temporal-Difference Learning (TD)

Instead of attributing the final score uniformly to all positions, update each position's eval toward the next position's eval. TD(λ) with λ near 1 would be a natural fit — the existing playbook already stores the full game tree, so backward updates along game paths are straightforward.

### Gradient Descent on Eval Parameters

Treat the pattern weights as parameters, define a loss function (e.g., MSE between eval and final score), and do SGD/batch gradient descent. This directly optimizes the weights rather than using the indirect confidence/sigmoid heuristic.

### Logistic Regression on Win Probability

Model the probability of winning as a logistic function of the evaluation, train via maximum likelihood. This is what strong Othello engines (Edax, Zebra) do.

### Decay / Recency Weighting

Weight recent games more heavily than old ones. The current system treats game #1 and game #10,000 identically.

### Stage-Specific Training

Train separate eval functions for opening/midgame/endgame rather than one eval keyed by piece count. The transition between tactical and positional play is sharp in Othello.

## Reinforcement Learning Formalization

### Current Approach as RL

The current system — playing games to terminal states, backpropagating solved endgame scores, then training a logistic regression eval — is structurally a form of **batch Monte Carlo value estimation** with a linear (logistic) function approximator, where episodes are generated by self-play under the current policy (alpha-beta + current eval).

| RL concept | Current system |
|---|---|
| State | `OthelloNode` (board position) |
| Action | Legal move (child node) |
| Policy | Alpha-beta search + eval function |
| Value function | Logistic regression over pattern features |
| Reward | +1/0/-1 from solved endgame |
| Training signal | Solved playbook scores |

What makes it rudimentary rather than full RL:

1. **No bootstrapping** — doesn't use the eval to generate training targets for intermediate positions (as TD learning would). Relies entirely on solved endgame scores.
2. **No policy optimization** — alpha-beta is the policy; only the heuristic it uses is trained, not the policy itself.
3. **No exploration/exploitation tradeoff** — training data comes from whatever the playbook covers, not from an exploration strategy.

### Recommended Incremental Path

1. **Now**: Frame current training as batch MC, run the self-play → retrain loop explicitly and iteratively.
2. **Next**: Switch training targets from terminal outcomes to deep-search scores (search-bootstrapped training — see below).
3. **Then**: Add TD(λ) updates for positions far from solved endgames (see TD section above).
4. **Later**: Try a small neural net over pattern features if the linear model hits a ceiling (see below).

### Search-Bootstrapped Training Targets (NNUE-Style)

Instead of pure MC (terminal outcome) or pure TD (one-step), use alpha-beta search itself to generate "expert" labels:

- For each training position, run a deep search (deeper than what would be used in play)
- Use that search score as the training target
- This is "knowledge distillation from search"

This is how Stockfish NNUE is trained and is practical here — the search infrastructure already exists. It produces much denser, less noisy training signal than terminal outcomes alone, and can label positions far from endgame that the playbook doesn't cover.

### Self-Play with Iterative Retraining

The existing system is already close to an iterative self-play loop. Formalizing it:

1. Play games using current eval + search
2. Collect (position, outcome) pairs — or (position, search score) pairs
3. Retrain eval
4. Repeat

The key upgrade: use **search results as training targets** rather than only terminal outcomes. When alpha-beta at depth D returns a score for position P, that's a better training target than raw win/loss.

### Neural Network Function Approximator

Logistic regression is linear in the features. A shallow neural network (1-2 hidden layers) over the same pattern features can capture **interactions between patterns** that a linear model cannot. This is what moved computer Othello forward historically (Buro's IAGO → neural evals).

#### Target Architecture

- Input: [sparse pattern features, numeric features]
- Layer 1: 256 units, ReLU activation
- Layer 2: 128 units, ReLU activation
- Output: 1 unit, sigmoid activation (analogous to current logistic regression sigmoid)
- ~33K FLOPs per forward pass

#### Framework Evaluation (2026-04)

| Framework | Train in C# | Inference (est.) | Native Deps | .NET 10 + ARM64 Mac |
|---|---|---|---|---|
| TorchSharp | Yes | ~2-10 μs | ~57 MB | Yes |
| ONNX Runtime | No (Python needed) | ~1-5 μs | ~119 MB | Yes |
| TensorFlow.NET | Yes | ~2-10 μs | ~100+ MB | Uncertain ARM64 |
| ML.NET | No custom architectures | N/A | — | — |
| Hand-rolled | Yes | ~0.1-0.5 μs | 0 MB | Yes |

**Critical constraint:** eval is the innermost function in alpha-beta search, called millions of times. For a ~33K FLOP network, actual math takes <0.5μs, but framework native interop overhead (tensor allocation, P/Invoke marshaling, disposal) adds 2-10μs — the overhead dominates the computation.

**Decision: hand-rolled implementation.** This is exactly the NNUE (Efficiently Updatable Neural Networks) approach used by Stockfish:
- Zero interop overhead — pure managed code
- Zero allocation — reuse pre-allocated `float[]` / `Span<float>` buffers
- SIMD via `System.Runtime.Intrinsics.Arm.AdvSimd` or `System.Numerics.Vector<float>`
- 10-100x faster than any framework with native interop
- Later: quantize to int8/int16 for further speedup (NNUE-style)
- Forward pass + backprop + optimizer: ~200-400 lines of code for this fixed architecture

**NNUE-style optimization for Othello:** since input is sparse (board patterns), the first layer accumulator can be incrementally updated when a move is made — only recomputing changed features. Drops per-node cost from ~33K FLOPs to potentially hundreds.

**Hybrid option:** use TorchSharp for training experimentation, then port trained weights to hand-rolled inference. But for a fixed architecture, writing the training loop directly is straightforward and avoids the 57MB dependency.

## Neural Network Training Performance

The NN training loop (`NeuralNetwork.Train`) is fully single-threaded and scalar. Several optimizations are available, roughly in order of effort vs. impact.

### SIMD in Training Inner Loops

The inference path uses `Vector<float>` SIMD but the training forward/backward pass is scalar. All inner loops over `AccumulatorSize` (256) and `Hidden2Size` (128) are simple `a[i] += b[i]` or `a[i] += scalar * b[i]` patterns — trivial to vectorize. Applies to embedding accumulation, Layer 2 matmul, gradient accumulation, and delta backprop. Expected **4-8x** speedup on those loops depending on AVX2/512 availability.

### Learning Rate Schedule

Currently the global learning rate is fixed; only Adam's per-parameter moments adapt. Adding a schedule improves convergence:

- **Cosine annealing**: `lr = lr_base * 0.5 * (1 + cos(π * epoch / maxEpochs))`. Decays toward zero, avoids overshooting in late training.
- **Warmup**: Start at `lr/10` for first 5-10 epochs, ramp linearly. Stabilizes early training when Adam moments are poorly estimated.
- **Reduce on plateau**: Halve LR if loss stalls for N epochs. Simple but effective.

Cosine annealing with warmup is the standard recommendation — ~5 lines of code at the top of the epoch loop.

### Data-Parallel Batches

Each sample's forward+backward pass within a batch is independent. Give each thread its own gradient accumulators, run `Parallel.For` over samples, then reduce (sum) per-thread gradients before the Adam step. The embedding gradient accumulators are large but sparsely touched, so per-thread copies are manageable. With 8+ cores this could give close to linear speedup on per-sample work.

### Replace HashSet with Flat List + Dirty Flag Array

`touchedRows` uses `HashSet<int>` per pattern class for sparse gradient clearing. Replace with `bool[]` dirty flags and a `List<int>` for iteration — avoids hashing overhead. Small win but trivial to implement.

### Larger Batch Size

Default is 64. Larger batches (256, 512) amortize the Adam update cost and are more parallelism-friendly. May need to scale learning rate (linear scaling rule: double batch → double LR, with warmup).

## MCTS / AlphaZero-Style (Longer-Term)

The existing `MonteCarlo` method is a basic random playout. A full **Monte Carlo Tree Search (MCTS)** with UCB selection would be a significant improvement over random playouts. The further step — training a neural network policy/value head and using it to guide MCTS (AlphaZero-style) — represents the state of the art for game AI.

This is a fundamentally different direction from the current alpha-beta approach, so it would be a parallel experiment rather than an incremental improvement. Key considerations:

- **MCTS alone** (no neural net) is a moderate project: UCB1 selection, expansion, random rollout, backpropagation. Can reuse the existing board infrastructure.
- **AlphaZero-style** is a much larger project: requires a neural network (e.g., via TorchSharp or ONNX), self-play data generation pipeline, and training loop. The payoff is that the learned policy/value functions can surpass hand-crafted evaluation and pattern-based approaches.
- The current pattern-based evaluation and playbook infrastructure could serve as a strong baseline for comparison, and the playbook data could potentially bootstrap neural network training.

## Numeric Features Treated as Categorical

The `Features` array in `OthelloNode.cs` contains five numeric features alongside the pattern-based features:

- `PieceCountSpread` (range ~[-64, +64])
- `CornerSpread` (range [-4, +4])
- `StablePieceSpread` (variable range)
- `FrontierSpread` (variable range)
- `PotentialMobilitySpread` (variable range)

These are all continuous numeric quantities, but the current system treats them identically to pattern features: each distinct integer output is mapped via `FeatureKey(pieceCount, value)` to an independent `HeuristicData` bucket in the dictionary. This means:

- A `CornerSpread` of +3 has no relationship to +2 or +4 — each is a completely independent category.
- High-range features like `PieceCountSpread` create sparse buckets (up to 129 distinct values × ~60 piece counts = ~7,700 buckets), many with few samples.
- The logistic regression learns independent log-odds per bucket, ignoring the natural ordering and monotonicity of these features.

### Proposed Approach: Direct Numeric Features in Logistic Regression

Instead of looking up pre-bucketed log-odds for these features, include them as direct numeric inputs to the logistic regression alongside the pattern-based log-odds:

```
logit = Σ (pattern log-odds × pattern weight) + Σ (β_i × feature_i)
```

where `β_i` are learned coefficients (one per numeric feature per game stage), and `feature_i` is the raw numeric value (e.g., `CornerSpread = +3`).

**Implementation steps:**

1. **Separate numeric features from pattern features** in `PatternScoreSlow`. Pattern features continue through the existing `HeuristicData` lookup → log-odds → weighted sum path. Numeric features are evaluated directly.
2. **Learn coefficients per stage** in `CalculateWeights` (or a new training step). For each piece-count stage, fit `β_i` via logistic regression on the playbook data, using the numeric feature values as inputs alongside the pattern log-odds sum.
3. **Add the numeric term** to `PatternScoreSlow`: after the pattern loop, add `Σ β_i × feature_i` to the accumulated log-odds sum.

This eliminates thousands of sparse dictionary entries, gives the model monotonicity for free (a positive `β` for `CornerSpread` means more corners is always better, proportionally), and reduces overfitting on rare feature values. The pattern features remain categorical since board patterns genuinely are categorical — there's no meaningful interpolation between two different edge configurations.
