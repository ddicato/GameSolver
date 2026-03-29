# Algorithmic Optimization Ideas

Reducing work per operation via better data structures, fewer lookups, and eliminating redundant computation. For allocation/GC improvements see [Memory.md](Memory.md); for concurrency see [Parallelism.md](Parallelism.md).

## Playbook Deserialization / Post-Processing

### Store a canonical board form per entry

`TryGetEntry` currently iterates all 8 symmetries via `GetSymmetries()`, performing up to 8 dictionary lookups per call. Storing one canonical form per entry would reduce this to a single lookup. The canonical form could be the lexicographically smallest symmetry.

### Cache `Entry.GetHashCode`

`GetHashCode()` computes all 8 symmetries and XORs them together on every call — and it's invoked on every dictionary operation. Since the board state is immutable after creation, the hash can be computed once and stored.

### Use `HashSet<Entry>` for Parents and Children

Both collections are `List<Entry>`. `AddParent` and `AddChild` use `.Any(entry.Equals)` for dedup — an O(n) linear scan. Switching to `HashSet<Entry>` makes dedup O(1). Parents is typically small (initialized with capacity 1), so Children is the bigger win here.

### ~~Skip `InvalidateCachedScore` during bulk deserialization~~ *(done)*

Added early-return guard (`if (this.score == null) return;`) to `InvalidateCachedScore()`, which short-circuits the recursive cascade when scores are already null. Minimal measured impact on deserialization because the private `score` field is never populated during loading — the cascade was already hitting null nodes. The fix still helps the merge case (loading a second playbook into a non-empty one) and runtime callers (`AddGame`, `ExtendLeaf`) where cached scores do exist.

## CalculateHeuristics / TrainSingle

### Use `CollectionsMarshal.GetValueRefOrAddDefault`

Available since .NET 6. In `TrainSingle`, each update does a `TryGetValue` followed by an indexed set — two hash lookups. `GetValueRefOrAddDefault` returns a ref to the value slot, allowing in-place update with a single lookup. Particularly impactful in the innermost dictionary where most lookups land.

### Compute playbook entry scores bottom-up before iterating

`Entry.Score` uses lazy recursive evaluation — each entry calls `-Children.Max(e => e.Score)` on demand. During `CalculateHeuristics`, the playbook is materialized to a list, but score evaluation still triggers recursive descent. A topological sort (leaves first) with an explicit bottom-up pass would avoid recursion depth issues and make evaluation predictable.

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
