# Unsorted Backlog

## Bugs

- **Process name:** Shows as "Avalonia Application" instead of the actual app name.
- **Empty pattern conflation:** Scores for pattern valuations with all zeros are shared among patterns. If one of the ulongs used for lookup is 0, we can't tell which pattern we're looking up a score for, which also conflates these patterns during training.
- **Redundant endgame solve** when adding to the playbook.
- **Evaluator score gap:** High-depth searches show 0 score but endgame solving hasn't kicked in yet. Need to use the endgame solver to extend the depth of the playbook, then re-generate params from it.

## Features

- Show how long each game took in OthelloProgram.
- Each player can load a different params and playbook file, for comparison and possibly coevolutionary learning.
- Read config params (e.g. randomGames/selfGames/adversarialGames, memoize/explore) from a file and/or command line. Might also want to change during program execution.
- Build a playbook visualizer.
- **RepairMissingChildLinks placement:** Decide systematically when to call `RepairMissingChildLinks`. Currently called in `ReadPlaybook` (after deserialize, before score materialization) and exposed via the UI "Repair Playbook" button. Other candidates: before `WritePlaybook` (ensure saved state is consistent), after each batch of `AddGame` calls, after `ExtendLeaf`. Takes ~8–9s on a 24-core machine.

## Refactoring

- Refactor iterative deepening into a static class.
- Add leaf node construct, or node types: `Node`, `ExploredLeaf`, `UnexploredLeaf`.
- `TrainingMode`: ~~make a flags type~~ *(done — `[Flags]` enum with Win/Loss/Draw)*, or split into two enums — add a second enum for playbook/patterns/heuristics/others to control which training outputs are updated.
- Check pattern shapes:
  - Why do we have downLeft but not downRight? — **Answered:** downRight is redundant; `FlipDiag1` maps every downLeft diagonal onto the corresponding downRight. `GetSymmetries()` applies all 8 D₄ transforms during training and evaluation, so downRight is already captured.
  - Row and Col are both listed in `patternSets`, but the dedup loop (lines 264–278) collapses them: `FlipDiag0(Row[k]) == Column[k]`, so all Column entries are skipped and they share a single pattern class and weight. Same for HorizEdge/VertEdge. The redundant `patternSets` entries are harmless (one-time static init) but could be removed.
  - Symmetry transforms apply in `TrainSingle` and `PatternScoreSlow`, which iterate all 8 board symmetries before masking with each pattern.
- Remove TODOs from code and add to backlog documents.

## Testing

- Add endgame test suite.
- Migrate to best-practice test suite.

## Reference: Player Search Strategies

**TrainingPlayer:** Below a certain depth, searches based on number of games played.

**PlaybookPlayer** search priority:
1. If a positive score exists, choose the node with the highest score.
   - On ties, choose the least-explored.
2. If there are any unexplored nodes, choose the one with the highest heuristic score.
3. Among negative-or-zero nodes, choose the least-explored node below a certain exploration threshold.
4. If no nodes are below the exploration threshold, choose the one with the highest heuristic score.