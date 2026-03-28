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

## Better Strategies

### Temporal-Difference Learning (TD)

Instead of attributing the final score uniformly to all positions, update each position's eval toward the next position's eval. TD(λ) with λ near 1 would be a natural fit — the existing playbook already stores the full game tree, so backward updates along game paths are straightforward.

### Gradient Descent on Eval Parameters

Treat the pattern weights as parameters, define a loss function (e.g., MSE between eval and final score), and do SGD/batch gradient descent. This directly optimizes the weights rather than using the indirect confidence/sigmoid heuristic.

### Logistic Regression on Win Probability

Model the probability of winning as a logistic function of the evaluation, train via maximum likelihood. This is what strong Othello engines (Edax, Zebra) do. (The commented-out code in `CalculateScore()` around line 1144 hints at this direction.)

### Decay / Recency Weighting

Weight recent games more heavily than old ones. The current system treats game #1 and game #10,000 identically.

### Stage-Specific Training

Train separate eval functions for opening/midgame/endgame rather than one eval keyed by piece count. The transition between tactical and positional play is sharp in Othello.

### Re-enable and Fix Confidence

The disabled `Confidence` property was trying to weight patterns by how decisive they are (win/loss spread). Fixing and re-enabling it would improve weight quality significantly.
