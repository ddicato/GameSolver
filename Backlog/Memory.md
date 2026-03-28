# Memory Usage Analysis

Allocation hotspots and GC pressure — reducing object churn and unbounded growth. For algorithmic/CPU improvements see [Algorithms.md](Algorithms.md); for concurrency see [Parallelism.md](Parallelism.md).

## Unbounded Growth

1. **Playbook entries** (`OthelloPlaybook.cs:17-20`)
   Two dictionaries (`entries` + `entriesByGameStage`) grow with every unique board state. No cap or eviction.

2. **Heuristic dictionaries** (`OthelloNode.cs:1063-1076`)
   Three static arrays of 3-level nested dictionaries (`PatternClassScores`, `PatternScores`, `FeatureScores`).
   Grow with every new pattern/piece-count combination seen during training. Cleared and rebuilt each
   training cycle via `ClearHeuristics`, so bounded by playbook size, but still large.

3. **Transposition table** (`TranspositionTable2.cs:8`)
   Plain `Dictionary<T, TableEntry>`, no size cap. Grows per search. Cleared between depths in iterative
   deepening unless `PersistTable=true`.

4. **Deserialization temporaries** (`OthelloPlaybook.cs:362-365`)
   Three full-size arrays (`OthelloNode[]`, `List<int>[]`, `List<int>[]`) plus a `Dictionary<int, int?>`
   held in memory for the entire deserialization, then GC'd.

## Allocation Hotspots

5. **`GetSymmetries` yield return** (`OthelloNode.cs:454-457`)
   Allocates an iterator state machine + 8 `KeyValuePair<ulong, ulong>` per call. Called in
   `PatternScoreSlow`, `UnknownPatterns`, and `TrainSingle` -- the hottest paths in the program.

6. **`Tuple<int, int>` in MtdFPlayer** (`MtdFPlayer.cs:129,155`)
   Heap-allocated `Tuple` created per move per depth in iterative deepening. Small but frequent.

7. **`Tuple<OthelloNode, int?>` in game history** (`OthelloProgram.cs`)
   Allocated per move per game during training.

8. **`TrainSingle` inner dictionary creation** (`OthelloNode.cs:1506-1530`)
   `new Dictionary<>()` allocated whenever a new piece-count or pattern key is first seen.
   Many small dictionaries, none pre-sized.

9. **`Playbook.ToList()` in CalculateHeuristics** (`OthelloNode.cs:1393`)
   Materializes entire playbook into a `List<KeyValuePair>`. Full copy in memory alongside the
   playbook itself.

10. **Playbook enumerator** (`OthelloPlaybook.cs:213`)
    `Select` + `new KeyValuePair` per entry on every enumeration.

## Existing Mitigations

- **NodeCache reuse** (`SearchParams.cs:9-37`, `SearchUtils.cs:71-75`)
  Pre-allocated `List<List<Node>>` reused across search depths via `GetChildren(list)`.
  Effectiveness: **High** -- avoids millions of list allocations during search.

- **Entry.Parents initial capacity 1** (`OthelloPlaybook.cs:664`)
  Effectiveness: **Low** -- saves one reallocation per entry.

- **TranspositionTable cleared between depths** (`MtdFPlayer.cs:133` via `Initialize`)
  Effectiveness: **Medium** -- prevents unbounded growth within a game.

- **GC.Collect after serialization/deserialization** (`OthelloPlaybook.cs:316,489`)
  Effectiveness: **Low-negative** -- forces full collection, pauses runtime.

- **MemoPlayer** caching playbook lookups
  Effectiveness: **Medium** -- avoids redundant search for known positions.

## Proposed Improvements

### High Impact

- **Replace `GetSymmetries` yield-return with fixed-size array or Span.**
  Return 8 pairs in a stack-allocated buffer instead of an iterator. Eliminates state machine +
  heap KVP allocations in the hottest loop. Called billions of times during training/eval.
  Effort: Low.

- **Flatten 3-level PatternClassScores to single dictionary with composite key struct.**
  `(int pieceCount, ulong self, ulong other)` as key. Cuts 3 lookups to 1, eliminates
  intermediate dictionary objects. Better cache locality.
  Effort: Medium.

### Medium Impact

- **Pre-size inner heuristic dictionaries.**
  Use `3^patternBitCount` for pattern dicts, 65 for piece-count dicts. Eliminates rehashing
  during training.
  Effort: Low.

- **Cap or use LRU eviction for TranspositionTable.**
  Especially for endgame solving where the table can grow very large. Prevents OOM on deep
  endgame solves.
  Effort: Medium.

- **Replace `Tuple<int,int>` with ValueTuple in MtdFPlayer.**
  `(int index, int score)` is stack-allocated, no GC. Per move per depth.
  Effort: Trivial.

### Low Impact

- **Replace `Tuple<OthelloNode, int?>` with ValueTuple in game history.**
  Effort: Trivial.

- **Remove forced `GC.Collect()` calls.**
  Let runtime manage collection timing. Avoids forced Gen2 pauses.
  Effort: Trivial.

- **Avoid `Playbook.ToList()` materialization.**
  Use index-based access or partition on the keys array directly. Saves one full copy of the
  playbook.
  Effort: Low.

- **Pool `TrainAccumulator` dictionaries across training rounds.**
  Reuse and clear instead of allocating new ones each `CalculateHeuristics` call.
  Effort: Low.

- **Zero out `PatternClassScores` values in place instead of `ClearHeuristics()` + rebuild.**
  `ClearHeuristics()` calls `.Clear()` on every dictionary, discarding all allocated buckets.
  The next training round re-creates the same hierarchy from scratch. Since the key set is
  stable (playbook only grows), zeroing values in place avoids all the reallocation and rehashing.
  Effort: Medium.
