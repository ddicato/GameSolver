# Parallelism

Concurrency improvements — doing more work simultaneously. For algorithmic/CPU improvements see [Algorithms.md](Algorithms.md); for allocation/GC improvements see [Memory.md](Memory.md).

## Parallelize Training/Tournament Games (Easy)

Run multiple games concurrently with independent board states. Each game is self-contained, so the main constraint is merging results into the shared playbook afterward.

**Possible approaches:**
- Use a thread-local playbook per game batch and merge at the end of each round
- Use a `ConcurrentDictionary`

The existing `threads` param in `PlayGames` is unused — this would fulfill that intent.

## Parallelize a Single Game Tree Search (Harder)

Split the search tree across threads using one of:

- **Young Brothers Wait** — search the first child sequentially, then search remaining siblings in parallel.
- **Lazy SMP** — run identical searches at slightly different depths on each thread, sharing a lock-free transposition table. Simpler to implement and a natural fit for MtdF's iterative re-searching.

Root children can be searched in parallel with a shared transposition table.

**Main challenges:**
- Shared playbook/memo access
- Thread safety of the evaluation function (`PatternScoreSlow`) — currently reads shared static dictionaries, which is safe for reads but not if training happens concurrently
