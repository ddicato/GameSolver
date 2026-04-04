# Algorithmic Optimization Ideas

Reducing work per operation via better data structures, fewer lookups, and eliminating redundant computation. For allocation/GC improvements see [Memory.md](Memory.md); for concurrency see [Parallelism.md](Parallelism.md).

## Playbook Deserialization / Post-Processing

### ~~Store a canonical board form per entry~~ *(done)*

Added `OthelloNode.Canonicalize()` which picks the lexicographic minimum of the 8 symmetries. The `entries` dictionary is now keyed by canonical form, reducing `TryGetEntry` and `Contains` from up to 8 dictionary lookups to 1. Each `Entry` caches its canonical state and uses it for `Equals` and `GetHashCode`, replacing the previous 8-symmetry iteration/XOR approach. Subsumes the earlier `Entry.GetHashCode` caching.

### ~~Use `HashSet<Entry>` for Parents and Children~~ *(done)*

Replaced `List<Entry>` with `HashSet<Entry>` for both collections. `AddParent` and `AddChild` now use `HashSet.Add()` which returns false on duplicate, replacing the O(n) `.Any(entry.Equals)` linear scan with O(1) hash lookup. Leverages the cached canonical hash and canonical `Equals` from the canonical board form change.

### ~~Skip `InvalidateCachedScore` during bulk deserialization~~ *(done)*

Added early-return guard (`if (this.score == null) return;`) to `InvalidateCachedScore()`, which short-circuits the recursive cascade when scores are already null. Minimal measured impact on deserialization because the private `score` field is never populated during loading — the cascade was already hitting null nodes. The fix still helps the merge case (loading a second playbook into a non-empty one) and runtime callers (`AddGame`, `ExtendLeaf`) where cached scores do exist.

## CalculateHeuristics / TrainSingle

### Use `CollectionsMarshal.GetValueRefOrAddDefault`

Available since .NET 6. In `TrainSingle`, each update does a `TryGetValue` followed by an indexed set — two hash lookups. `GetValueRefOrAddDefault` returns a ref to the value slot, allowing in-place update with a single lookup. Particularly impactful in the innermost dictionary where most lookups land.

### Compute playbook entry scores bottom-up before iterating

`Entry.Score` uses lazy recursive evaluation — each entry calls `-Children.Max(e => e.Score)` on demand. During `CalculateHeuristics`, the playbook is materialized to a list, but score evaluation still triggers recursive descent. A topological sort (leaves first) with an explicit bottom-up pass would avoid recursion depth issues and make evaluation predictable.

## Bitboard Move Generation

`GetChildren` iterates all 64 squares with `Square[i,j]` lookups in 8-direction loops. The competitive approach for Othello is **Kogge-Stone** or **Dumb7Fill** shift-based move generation — compute all legal moves as a single `ulong` mask using parallel prefix shifts, then iterate only the set bits. This is typically 3–5x faster than square-by-square scanning, which directly translates to deeper search at the same wall-clock time.

Same applies to `PotentialMobilitySpread` and `FrontierSpread` — these can be computed with a few shift-and-mask operations instead of 64-square loops.

Unlocks fastest-first endgame ordering (see below), since computing child mobility becomes cheap.

## Aspiration Windows in Iterative Deepening

`AlphaBetaPlayer.SelectNode` searches each depth with a full `(-MaxValue, MaxValue)` window. Using **aspiration windows** — starting with a narrow window around the previous depth's score and re-searching if it fails — dramatically reduces the tree size at each iteration. `MtdFPlayer` effectively does this already, but `AlphaBetaPlayer` (which is the one used with `SolveEndgame`) doesn't.

## Principal Variation Search (PVS) / NegaScout

Uses zero-window searches **at every internal node**, not just the root. After searching the first child (the predicted best move) with a full window, all subsequent children get a null-window probe `(-alpha-1, -alpha)`. If the probe fails high, re-search that child with the full window. Complementary to MTD(f) — PVS can be used as the search algorithm inside each MTD(f) iteration.

**PVS vs MTD(f) tradeoffs:**

- MTD(f) is sensitive to evaluation granularity — coarse evaluations converge in fewer passes, but fine-grained ones (like `ScoreMultiplier = 10000` log-odds) can cause many re-searches. Also sensitive to TT memory pressure; if entries get evicted between passes, redundant work occurs.
- PVS typically finds the answer in a single pass, so less dependent on TT hit rate. Naturally integrates with iterative deepening (previous iteration's PV gives move ordering for the first child). Re-search on fail-high adds some overhead, but with good move ordering the first move is usually best.
- For Othello with high-resolution evaluation scores, PVS with aspiration windows at the root tends to be preferred over MTD(f).
- Note: TT best move was tested and not effective with MTD(f) (see below), but may be worth revisiting with PVS since wide-window search benefits more from PV move ordering.

## Multi-Prob Cut (MPC)

A forward-pruning technique: do a shallow search to estimate whether a full-depth search would fall outside the alpha-beta window. If a depth-D search at a node estimates the score well outside the window (with statistical confidence calibrated from the evaluation function's error distribution), prune the subtree without a full search. Used by most top Othello programs (Edax, Zebra). Provides substantial speedup in midgame positions.

Requires calibration data: the standard deviation of (shallow eval - deep eval) at various depths, which can be gathered from self-play or playbook analysis.

## Move Ordering in AlphaBeta / AlphaBetaEndgame

Currently, move ordering only exists at the root level of `MtdFPlayer.SelectNode()` — iterative deepening scores from the previous depth guide the next iteration's ordering via `OrderMovesDescending`. Inside the tree (`AlphaBeta` in `SearchUtils.cs:82` and `AlphaBetaEndgame` in `SearchUtils.cs:133`), children are iterated in fixed spatial board order with no ordering.

### ~~Store best move in transposition table~~ — *Tested, not effective with MTD(f)*

Implemented and benchmarked: added `BestMove` field to `TranspositionTable2.TableEntry`, probed in `AlphaBeta` and `AlphaBetaEndgame` to try the PV move first. **Result:** node counts reduced < 2%, but per-node throughput dropped 12–21% due to overhead (Equals check per child to skip duplicate, larger TT entries hurting cache). Net negative on wall-clock time.

**Why it doesn't help MTD(f):** MTD(f) uses zero-window alpha-beta calls. The TT bounds already provide most of the pruning — the best move from one zero-window call isn't necessarily best for the next call with a different bound. TT best move ordering is most effective with wide-window alpha-beta, which MTD(f) avoids by design.

May be worth revisiting if the search framework changes to use wider windows (e.g., PVS/NegaScout instead of MTD(f)). Note: only benchmarked during iterative deepening, not the endgame solver (`MtdFEndgame` / `AlphaBetaEndgame`) — deeper endgame trees with more TT reuse could behave differently.

### ~~Killer moves~~ *(done — positive results)*

Two killer move slots per depth, stored as `ulong` square bitmasks in `OthelloSearchParams.KillerMoves[depth, slot]`. On beta cutoff, the move square is recorded; on entry, children matching a killer are searched first. Killers persist across MTD(f) zero-window iterations within the same iterative deepening depth, then cleared.

**Results:** 9–24% node reduction in early-game searches, with negligible throughput overhead. Effect grows as the game progresses — opening positions see little benefit, but by move 5–6 searches see up to 24% fewer nodes. Wall-clock improvement of ~18% on the largest early-game searches. Unlike TT best move, killer moves depend on cutoff frequency rather than score values, making them well-suited to MTD(f)'s zero-window architecture. These results were measured during iterative deepening only; endgame solving with deeper trees and more positional repetition may see even larger gains.

### History heuristic — *Medium impact*

Maintain a table of `(from, to) → cumulative depth²` across the entire search, incremented when a move causes a cutoff. Sort non-TT, non-killer moves by history score. Improves ordering of "quiet" moves that don't show up in the TT or killer slots. Can be decayed between iterative deepening iterations.

### Quick static ordering — *Low impact*

Pre-sort children by a cheap heuristic before searching: corners first, then edges, then mobility count. Simplest to implement but least effective compared to search-informed techniques. Mostly useful as a tiebreaker when other signals are absent.

### Depth cutoff for ordering overhead

Move ordering has overhead (sorting, lookups). At shallow remaining depths the search is fast enough that overhead isn't worth it:

- **TT best move:** Not effective with MTD(f) zero-window searches (see above).
- **Killer moves:** Useful at depth >= 2. At depth 1, children are about to be evaluated directly.
- **Full sorting** (history, static): Useful at depth >= 3–4. Below that, Othello's average branching factor (~8–10) means sorting a small list for minimal savings.
- **Endgame:** Move ordering matters *more* here because trees are deeper. TT best move and killer moves are especially valuable in `AlphaBetaEndgame`.

## PatternScoreSlow

`PatternScoreSlow` is the evaluation function used during search. It iterates all 8 symmetries, performs 3 nested dictionary lookups per pattern class per symmetry, and divides by `Transforms.Length` (8) at query time.

Note: the faster `PatternScore` already exists — it uses pre-expanded `PatternScores[]` with a single canonical board, avoiding the symmetry loop entirely. The ideas below apply to `PatternScoreSlow` specifically, which is used when the pre-expanded scores aren't available.

### Precompute a canonical board form

Instead of evaluating all 8 symmetries and averaging, pick a canonical symmetry (e.g., lexicographic minimum) and look up once. This is an 8x reduction in dictionary lookups.

### Use flat arrays for pattern lookup

For patterns with a small number of squares (e.g., 8), the key space is 3^8 = 6,561. A flat array indexed by a ternary encoding of the pattern would replace dictionary hashing with a direct array index. This is the approach used by strong Othello engines like Edax and Zebra.

### Incremental pattern index updates

Currently, pattern masks are applied from scratch each call (`board & mask`). Engines like Edax maintain pattern indices incrementally — when a move is made, only the affected patterns are updated. This avoids recomputing all pattern indices at every node in the search tree.

### Pre-bake the weight division

`PatternScoreSlow` computes `data.Score * PatternClassWeights[i, pieceCount] / Transforms.Length` at query time. The `/ Transforms.Length` factor is constant and could be folded into the weight during `CalculateWeights`, eliminating a division per pattern per symmetry per evaluation.

## Endgame Solver Enhancements

### Fastest-First Move Ordering

Order children by **fewest opponent legal moves** (fastest-first). Nodes where the opponent has fewer responses are more likely to cause cutoffs. Computing each child's mobility is cheap with bitboard move generation (see above). Well-known Othello optimization — often 10x+ reduction in nodes searched during endgame solving.

### Parity-Based Move Ordering

In the endgame, moves into **odd-parity regions** (regions with an odd number of empty squares) tend to be better because the current player gets the last move in that region. Simple and effective ordering heuristic specific to Othello. Can be combined with fastest-first as a tiebreaker.

### Enhanced Transposition Cutoffs (ETC)

Before expanding a node's children, probe the TT for each child. If any child returns a value that causes a cutoff at the parent, skip the entire subtree without recursing. Cheap when a TT already exists — just additional probes before the main search loop.

### Stability-Based Pruning

If a player can be proven to have enough stable discs to guarantee a win regardless of remaining play, cut the search short. Leverages the existing `GetStablePieces` calculation. Most useful in late endgame positions where large stable regions exist.
