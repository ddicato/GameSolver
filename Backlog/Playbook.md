# Playbook Data Quality

Improving the quality and coverage of playbook training data. Two main problems: (1) low pattern coverage for several classes, and (2) many positions explored only by weak players.

## Current State

Pattern coverage as of 2026-04-05 (4.3M entries):
- Classes 0, 1, 7 are at/near 100% coverage
- Classes 2, 4, 8, 9 are at 40-62% coverage
- Classes 3, 5, 6 are at 55-95% but have thousands of configs seen only 1-5 times

Current exploration mechanisms (`exploringPatterns`, `exploringPlaybook` in MtdFPlayer) only perturb root-level move selection during self-play. Effect is mild and indirect.

## 1. Targeted Pattern Gap-Filling

Directly identify unseen configurations in low-coverage classes (2, 4, 8, 9). For each unseen config, construct or search for board positions containing it, then evaluate or solve those positions. This treats coverage as a data generation problem rather than a game-playing problem.

## 2. Playbook Frontier Exploration

Walk the playbook tree to find leaf nodes (positions whose children aren't in the playbook). Rank leaves by priority:
- Number of unseen pattern configurations they'd expose
- Proximity to endgame (cheaper to solve exactly)
- Evaluation uncertainty (high variance among sibling evaluations)

Extend from these positions directly — generate children, evaluate or endgame-solve them, add to playbook. No need to play full games. This separates data generation from game-playing and lets us target gaps precisely.

## 3. Retrograde / Neighborhood Exploration

For pattern configs seen only 1-5 times, find the playbook entries containing them. Explore sibling positions (alternative moves from the parent) to build up data density in those neighborhoods. Concentrates effort in data-poor regions of the game tree.

## 4. Player Strength Tagging and Re-evaluation

- Tag playbook entries with the strength/depth of the player that generated them
- Identify positions explored only by weak players (RandomPlayer, depth-1)
- Re-evaluate those positions with the current best player (Eval1 at high depth) or endgame solver
- Optionally weight training data by player strength or solve depth during pattern weight fitting

## 5. Bandit-Based Exploration (UCB vs Thompson Sampling)

Treat the playbook as a tree where each node tracks visit count and value estimates. Use a bandit algorithm to decide which branches to extend next, balancing exploitation (deepening strong/important lines) vs exploration (visiting sparse branches).

### UCB (Upper Confidence Bound)

Select the child maximizing `value + C * sqrt(ln(parent_visits) / child_visits)`. Deterministic given the same state — always picks the node with the highest upper confidence bound.

**Pros:**
- Simple to implement and tune (single parameter C)
- Deterministic: reproducible, easy to debug
- Well-understood theoretical regret bounds
- Natural fit for tree search (this is what MCTS uses)

**Cons:**
- Can be slow to adapt when value estimates are noisy or non-stationary (e.g., pattern weights change after retraining)
- The confidence term only considers visit counts, not the shape of the value distribution
- Tends to explore uniformly across all under-visited nodes regardless of how informative they are

### Thompson Sampling

Maintain a posterior distribution over each node's value (e.g., Beta distribution for win rate, or Gaussian for continuous eval). To select a child, sample from each child's posterior and pick the child with the highest sample.

**Pros:**
- Naturally probability-matches: explores in proportion to the probability that a node is optimal, so effort concentrates on genuinely uncertain nodes rather than all under-visited ones
- Handles non-stationary values gracefully — the posterior adapts as new data arrives
- Better empirical performance than UCB in many settings, especially with structured or correlated rewards (which the playbook has — nearby positions have correlated values)
- No explicit exploration parameter to tune

**Cons:**
- Stochastic: harder to reproduce and debug
- Requires choosing a prior distribution and maintaining per-node distribution parameters (more state than a visit counter)
- Posterior updates for complex value models (e.g., pattern-based eval) may not have clean closed-form solutions — may need approximations

### Recommendation

Thompson sampling is likely the better fit here because:
1. Pattern weight retraining makes value estimates non-stationary — Thompson sampling adapts naturally while UCB's confidence bounds assume fixed rewards
2. Game tree positions have correlated values (siblings share most of the board), so probability-matching explores more efficiently than UCB's uniform confidence inflation
3. The playbook already stores win/loss/draw counts per entry, which map directly to a Beta distribution posterior — minimal new state needed

UCB is the simpler starting point if you want to prototype quickly. Thompson sampling is worth the extra complexity if the exploration budget is limited and you need to be efficient about which branches to extend.
