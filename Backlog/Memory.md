# Memory Usage Analysis

Allocation hotspots and GC pressure — reducing object churn and unbounded growth. For algorithmic/CPU improvements see [Algorithms.md](Algorithms.md); for concurrency see [Parallelism.md](Parallelism.md).

## Unbounded Growth

1. **Playbook entries** (`OthelloPlaybook.cs:17-20`)
   Two dictionaries (`entries` + `entriesByGameStage`) grow with every unique board state. No cap or eviction.

2. **Heuristic dictionaries** (`OthelloNode.cs`)
   Static arrays of flat dictionaries (`PatternClassScores`, `PatternScores`) keyed by `PatternClassKey`
   struct, plus `FeatureScores` (2-level nested). Grow with every new pattern/piece-count combination
   seen during training. Cleared and rebuilt each training cycle via `ClearHeuristics`, so bounded by
   playbook size, but still large.

3. **Transposition table** (`TranspositionTable2.cs:8`)
   Plain `Dictionary<T, TableEntry>`, no size cap. Grows per search. Cleared between depths in iterative
   deepening unless `PersistTable=true`.

4. **Deserialization temporaries** (`OthelloPlaybook.cs:362-365`)
   Three full-size arrays (`OthelloNode[]`, `List<int>[]`, `List<int>[]`) plus a `Dictionary<int, int?>`
   held in memory for the entire deserialization, then GC'd.

## Allocation Hotspots

5. **`Playbook.ToList()` in CalculateHeuristics** (`OthelloNode.cs:1393`)
   Materializes entire playbook into a `List<KeyValuePair>`. Full copy in memory alongside the
   playbook itself.

6. **Playbook enumerator** (`OthelloPlaybook.cs:213`)
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

- **`BoardSymmetries` struct** (`OthelloNode.cs`)
  Replaces `yield return` iterator + heap `KeyValuePair` allocations with a fixed-size
  stack-allocated struct in hot paths (`PatternScoreSlow`, `TrainSingle`, `UnknownPatterns`)
  and warm paths (`IsIsomorphic`, `IsIsomorphicParent`, static init).
  Effectiveness: **High** -- eliminates iterator state machine and 8 heap allocations per call
  in the most frequently executed code paths.

- **`PatternClassKey` flat dictionary** (`OthelloNode.cs`)
  Replaces 3-level nested `Dictionary<int, Dictionary<ulong, Dictionary<ulong, HeuristicData>>>`
  with single `Dictionary<PatternClassKey, HeuristicData>` using a 20-byte readonly struct key.
  Applied to `PatternClassScores`, `PatternScores`, and `FeatureScores` arrays.
  Effectiveness: **High** -- reduces 3 hash lookups to 1 in hot paths (`PatternScoreSlow`,
  `PatternScore`, `TrainSingle`), eliminates hundreds of intermediate dictionary objects,
  and improves cache locality.

- **ValueTuple replacements** (`MtdFPlayer.cs`, `OthelloProgram.cs`, `OthelloPlaybook.cs`, `OthelloWindow.axaml.cs`)
  `Tuple<int, int>` → `(int Index, int Score)` in MtdFPlayer iterative deepening;
  `Tuple<OthelloNode, int?>` → `(OthelloNode Node, int? Score)` in game history.
  Effectiveness: **Medium** (MtdFPlayer, per move per depth) / **Low** (game history, per move per game)
  -- eliminates heap allocations for small, frequent tuples; named fields improve readability.

## Proposed Improvements

### Medium Impact

- **Pre-size heuristic dictionaries.**
  Use estimated entry count (e.g. based on playbook size) to pre-size the flat
  `PatternClassKey`/`FeatureKey` dictionaries. Eliminates rehashing during training.
  Effort: Low.

- **Cap or use LRU eviction for TranspositionTable.**
  Especially for endgame solving where the table can grow very large. Prevents OOM on deep
  endgame solves.
  Effort: Medium.

### Low Impact

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

- **Zero out heuristic values in place instead of `ClearHeuristics()` + rebuild.**
  `ClearHeuristics()` calls `.Clear()` on every dictionary, discarding allocated buckets.
  Originally high overhead when rebuilding the 3-level nested hierarchy; now that dictionaries
  are flat, the only savings is preserving hash table bucket allocations for a once-per-cycle
  operation. Impact dropped to **Negligible** after `PatternClassKey`/`FeatureKey` flattening.
  Effort: Medium.
