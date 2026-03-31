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
