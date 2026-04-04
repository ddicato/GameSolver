using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Othello {
    // TODO: implement ICollection<OthelloNode>?
    public class OthelloPlaybook : IEnumerable<KeyValuePair<OthelloNode, int>> {
        public readonly Entry Root;

        public readonly MtdFPlayer Player = new MtdFPlayer(SearchUtils.EndgameDepth * 2, OthelloNode.Eval1);

        private readonly Dictionary<OthelloNode, Entry> entries = new Dictionary<OthelloNode, Entry>();
        
        // TODO: create type for generalized game stage
        private readonly Dictionary<int, HashSet<Entry>> entriesByGameStage = new Dictionary<int, HashSet<Entry>>();

        public OthelloPlaybook(OthelloNode root) {
            if (root == null) {
                throw new ArgumentNullException();
            }

            this.Root = new Entry(this, root);
            this.AddEntry(this.Root);
        }

        public int Count {
            get {
                return this.entries.Count;
            }
        }

        private void AddEntry(Entry entry) {
            this.AddEntry(OthelloNode.Canonicalize(entry.State), entry);
        }

        private void AddEntry(OthelloNode canonicalKey, Entry entry) {
            this.entries[canonicalKey] = entry;

            int gameStage = entry.State.OccupiedSquareCount;
            HashSet<Entry> set;
            if (!this.entriesByGameStage.TryGetValue(gameStage, out set)) {
                set = new HashSet<Entry>();
                this.entriesByGameStage[gameStage] = set;
            }
            set.Add(entry);
        }

        /// <summary>
        /// Adds the given game history to the playbook.
        /// </summary>
        /// <param name="history">
        /// A sequence of game states in order. The second element in the tuple is the solved endgame
        /// score, if one was calculated.
        /// </param>
        public void AddGame(IList<(OthelloNode Node, int? Score)> history) {
            if (history == null) {
                throw new ArgumentNullException();
            }

            if (history.Count == 0) {
                throw new ArgumentException();
            }

            if (!this.Root.State.Equals(history[0].Node)) {
                Console.WriteLine(
                    OthelloNode.PrintNodes(2, true, this.Root.State, history[0].Node, history[history.Count - 1].Node));
                throw new ArgumentException();
            }

            if (!history[history.Count - 1].Node.IsGameOver) {
                Console.WriteLine(
                    OthelloNode.PrintNodes(2, true, this.Root.State, history[history.Count - 1].Node));
                throw new ArgumentException();
            }

            Debug.Assert(this.Contains(history[0].Node));

            Entry parent = this.Root;
            for (int i = 1; i < history.Count; i++) {
                Entry current;
                if (!this.TryGetEntry(history[i].Node, out current)) {
                    current = new Entry(this, history[i].Node);
                    this.AddEntry(current);
                }

                current.AddParent(parent);
                parent.AddChild(current);

                // Link any legal children of current that are already in the playbook.
                // This maintains the invariant that if both A and B are in the playbook
                // and A→B is a legal move, then A.Children contains B — even when they
                // were added via different game paths that never played A→B directly.
                current.LinkExistingLegalChildren();

                parent = current;

                // Solve the endgame exactly if we're close enough.
                if (current.SolvedScore == null && current.State.EmptySquareCount <= SearchUtils.EndgameDepth) {
                    Console.Write("* ");
                    current.SolvedScore = this.Player.GetScore(current.State);
                }
            }
        }

        public bool Contains(OthelloNode state) {
            return this.entries.ContainsKey(OthelloNode.Canonicalize(state));
        }

        public bool TryGetEntry(OthelloNode state, out Entry value) {
            return this.entries.TryGetValue(OthelloNode.Canonicalize(state), out value);
        }

        private bool TryGetEntryByCanonicalKey(OthelloNode canonicalKey, out Entry value) {
            return this.entries.TryGetValue(canonicalKey, out value);
        }

        public void Clear() {
            this.entries.Clear();
            this.entriesByGameStage.Clear();

            this.Root.Unlink();
            this.AddEntry(this.Root);
        }

        /// <summary>
        /// Returns all leaf entries (no children) that are not game-over. These are candidates for further exploration,
        /// for example by having a solved endgame that hasn't made it into the playbook.
        /// </summary>
        public List<Entry> GetUnfinishedLeaves() {
            return this.entries.Values
                .Where(e => e.Children.Count == 0 && !e.State.IsGameOver)
                .ToList();
        }

        /// <summary>
        /// Clears SolvedScore on non-leaf entries (those with children) so that
        /// BackfillSolvedScores can recompute them. Leaf SolvedScores (from endgame
        /// search or game-over) are preserved. Returns the number of entries cleared.
        /// </summary>
        public int ClearBackfilledSolvedScores() {
            int cleared = 0;
            foreach (Entry entry in this.entries.Values) {
                if (entry.Children.Count > 0 && entry.SolvedScore != null) {
                    entry.SolvedScore = null;
                    entry.ClearCachedScore();
                    cleared++;
                }
            }
            return cleared;
        }

        /// <summary>
        /// Backfills SolvedScore on internal nodes whose children all have SolvedScores.
        /// Processes bottom-up by game stage (most pieces first) so that children are
        /// resolved before parents. Returns the number of entries backfilled.
        /// </summary>
        public int BackfillSolvedScores() {
            int backfilled = 0;

            // Process from deepest game stage (most pieces) to shallowest.
            // Only backfill within endgame range where the playbook contains
            // complete solved PVs. Earlier stages have selective (not exhaustive)
            // children, so "all children solved" doesn't mean the position is solved.
            int minPieces = 64 - SearchUtils.EndgameDepth - 2; // match maxSolveDepth
            List<int> stages = this.entriesByGameStage.Keys.ToList();
            stages.Sort();
            stages.Reverse();

            foreach (int stage in stages) {
                if (stage < minPieces) {
                    break;
                }

                foreach (Entry entry in this.entriesByGameStage[stage]) {
                    if (entry.SolvedScore != null || entry.Children.Count == 0) {
                        continue;
                    }

                    // All children must have SolvedScores for us to compute this one.
                    if (entry.Children.All(c => c.SolvedScore != null)) {
                        entry.SolvedScore = -entry.Children.Min(c => c.SolvedScore.Value);
                        backfilled++;
                    }
                }
            }

            return backfilled;
        }

        /// <summary>
        /// Extends a leaf entry by appending a sequence of game states (the solved endgame line)
        /// from the leaf down to game-over, linking each as parent-child.
        /// </summary>
        /// <param name="leaf">The existing leaf entry to extend from.</param>
        /// <param name="continuation">
        /// The sequence of board states following the leaf, ending at game-over.
        /// Does not include the leaf state itself.
        /// </param>
        public void ExtendLeaf(Entry leaf, IList<(OthelloNode Node, int? Score)> continuation) {
            if (leaf == null || continuation == null) {
                throw new ArgumentNullException();
            }

            Entry parent = leaf;
            for (int i = 0; i < continuation.Count; i++) {
                Entry current;
                if (!this.TryGetEntry(continuation[i].Node, out current)) {
                    current = new Entry(this, continuation[i].Node);
                    this.AddEntry(current);
                }

                current.AddParent(parent);
                parent.AddChild(current);

                if (continuation[i].Score != null && current.SolvedScore == null) {
                    current.SolvedScore = continuation[i].Score;
                }

                parent = current;
            }
        }

        #region IEnumerable<KeyValuePair<OthelloNode, int>> Members

        public IEnumerator<KeyValuePair<OthelloNode, int>> GetEnumerator() {
            return this.entries.Values.Select(entry => new KeyValuePair<OthelloNode, int>(entry.State, entry.Score)).GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator() {
            return this.GetEnumerator();
        }

        /// <summary>
        /// Materializes all playbook entries, caching Entry.Score values (which may trigger
        /// expensive negamax evaluation on first call). Prints progress to console.
        /// </summary>
        public List<KeyValuePair<OthelloNode, int>> ToList() {
            DateTime start = DateTime.Now;
            Console.Write("Materializing playbook entries...");
            var result = Enumerable.ToList(this);
            Console.WriteLine(" {0} entries. Time elapsed = {1:0.000} seconds.",
                result.Count, (DateTime.Now - start).TotalSeconds);
            return result;
        }

        #endregion

        #region Serialization

        private const char SerializationDelim = ':';
        private const char SerializationSubDelim = ',';

        // An index is first assigned to each entry. By convention, the root entry has index
        // zero. Entries are then written as follows:
        // <entry index>:(<parent index>(,<parent index>)+)?:(<child index>(,<child index>)+)?:<game state>:<solved score>?
        // The solved score is an integer from -64 to 64, or "null" if not calculated.

        // The file is then organized as follows:
        // N\n
        // 0::<child list>:<root state>:\n
        // 1:<parent list>:<child list>:<state>:<solved score>?\n
        // ...
        // N:<parent list>:<child list>:<state>:<solved score>?\n
        // <EOF>
        // where N is the number of entries in the playbook.

        public void Serialize(string path) {
            DateTime start = DateTime.Now;
            Console.Write("Saving game database...");
            StreamWriter writer = new StreamWriter(path, false);

            try {
                List<Entry> entryList = new List<Entry>(this.entries.Count);
                Dictionary<OthelloNode, int> entryIndices = new Dictionary<OthelloNode, int>(this.entries.Count);

                Stack<Entry> entryStack = new Stack<Entry>();
                entryStack.Push(this.Root);
                while (entryStack.Count > 0) {
                    Entry entry = entryStack.Pop();
                    entryList.Add(entry);
                    entryIndices[entry.State] = entryList.Count - 1;
                    foreach (Entry child in entry.Children) {
                        if (!entryIndices.ContainsKey(child.State)) {
                            entryStack.Push(child);
                        }
                    }
                }

                if (entryList.Count != entryIndices.Count ||
                    entryIndices.Count != this.entries.Count) {
                    Console.WriteLine();
                    Console.WriteLine(
                        "Discrepancy in count: {0},{1},{2} entries,entryList,entryIndices",
                        this.entries.Count,
                        entryList.Count,
                        entryIndices.Count);
                }

                writer.WriteLine(entryList.Count);

                for (int i = 0; i < entryList.Count; i++) {
                    Entry entry = entryList[i];
                    writer.Write(i);
                    writer.Write(SerializationDelim);

                    bool first = true;
                    foreach (Entry parent in entry.Parents) {
                        if (first) {
                            first = false;
                        } else {
                            writer.Write(SerializationSubDelim);
                        }
                        writer.Write(entryIndices[parent.State]);
                    }
                    writer.Write(SerializationDelim);

                    first = true;
                    foreach (Entry child in entry.Children) {
                        if (first) {
                            first = false;
                        } else {
                            writer.Write(SerializationSubDelim);
                        }
                        writer.Write(entryIndices[child.State]);
                    }
                    writer.Write(SerializationDelim);

                    writer.Write(entry.State.Serialize());
                    writer.Write(SerializationDelim);

                    if (entry.SolvedScore != null) {
                        writer.Write(entry.SolvedScore);
                    }
                    writer.WriteLine();
                }
            } catch (Exception ex) {
                Console.WriteLine("error.");
                Console.WriteLine(ex);
                return;
            } finally {
                writer.Close();
                GC.Collect();
            }

            TimeSpan elapsed = DateTime.Now - start;
            Console.WriteLine("done. Time elapsed = {0:0.000} seconds.", elapsed.TotalSeconds);
        }

        #endregion

        #region Deserialization

        private static List<int> ReadList(string linePart) {
            if (string.IsNullOrEmpty(linePart)) {
                return null;
            }

            return linePart.Split(new char[] { SerializationSubDelim }).Select(int.Parse).ToList();
        }

        public void Deserialize(string path) {
            DateTime start = DateTime.Now;

            StreamReader reader;
            try {
                if (!File.Exists(path)) {
                    Console.WriteLine("File not found: {0}", path);
                }

                reader = new StreamReader(path);
                Console.Write("Loading game database... ");
            } catch {
                Console.WriteLine("I/O Error.");
                return;
            }

            int consoleLeft = Console.CursorLeft;
            int consoleTop = Console.CursorTop;

            try {
                string line = reader.ReadLine().Trim();
                int entryCount = int.Parse(line);
                if (entryCount < 1) {
                    throw new InvalidDataException("Entry count must be a positive integer.");
                }

                // First pass - read all the data.
                OthelloNode[] states = new OthelloNode[entryCount];
                List<int>[] parents = new List<int>[entryCount];
                List<int>[] children = new List<int>[entryCount];
                Dictionary<int, int?> solvedScores = new Dictionary<int, int?>();
                for (int i = 0; i < entryCount; i++) {
                    if (i % (entryCount / 100) == 0) {
                        Console.SetCursorPosition(consoleLeft, consoleTop);
                        Console.Write("{0:#0}%", 100.0 / entryCount * i);
                    }

                    line = reader.ReadLine();
                    if (line == null) {
                        throw new InvalidDataException("Unexpected EOF.");
                    }
                    line = line.Trim();

                    string[] data = line.Split(new char[] { SerializationDelim });
                    if (data.Length != 5) {
                        throw new InvalidDataException(
                            string.Format("Expected 4 '{0}' on a line but got {1}.", SerializationDelim, data.Length - 1));
                    }

                    int checkIndex = int.Parse(data[0]);
                    if (checkIndex != i) {
                        throw new InvalidDataException(
                            string.Format("Expected index {0} but got {1}.", i, checkIndex));
                    }

                    parents[i] = ReadList(data[1]);
                    children[i] = ReadList(data[2]);
                    states[i] = OthelloNode.Deserialize(data[3]);
                    if (!string.IsNullOrEmpty(data[4])) {
                        solvedScores[i] = int.Parse(data[4]);
                    }

                    if (i == 0) {
                        if (!states[i].IsIsomorphic(this.Root.State) &&
                            !this.Contains(states[i])) {
                            throw new InvalidDataException("Playbook root is not a valid starting state and represents an unknown subtree.");
                        }

                        if (parents[i] != null && parents[i].Count > 0) {
                            throw new InvalidDataException("Root node lacks child nodes.");
                        }
                    } else {
                        if (parents[i] == null || parents[i].Count == 0) {
                            throw new InvalidDataException(
                                string.Format("Non-root node {0} lacks parent nodes.", i));
                        }
                    }
                }

                line = reader.ReadLine();
                if (line != null) {
                    throw new InvalidDataException(
                        string.Format("Expected EOF after {0} lines.", entryCount + 1));
                }

                Console.Write(" | ");
                consoleLeft = Console.CursorLeft;
                consoleTop = Console.CursorTop;

                // Second pass - precompute canonical forms (can be parallelized later).
                OthelloNode[] canonicalKeys = new OthelloNode[entryCount];
                // for (int i = 0; i < entryCount; i++) {
                //     if (i % (entryCount / 100) == 0) {
                //         Console.SetCursorPosition(consoleLeft, consoleTop);
                //         Console.Write("{0:#0}%", 100.0 / entryCount * i);
                //     }

                //     canonicalKeys[i] = OthelloNode.Canonicalize(states[i]);
                // }
                {
                    int canonicalized = 0;
                    Lock @lock = new();
                    Parallel.For(0, entryCount, new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount }, i => {
                        canonicalKeys[i] = OthelloNode.Canonicalize(states[i]);
                        int canonicalizedSnapshot = Interlocked.Increment(ref canonicalized);
                        if (canonicalizedSnapshot % (entryCount / 100) == 0) {
                            lock (@lock) {
                                Console.SetCursorPosition(consoleLeft, consoleTop);
                                Console.Write("{0:#0}%", 100.0 / entryCount * canonicalized);
                            }
                        }
                    });
                }

                Console.Write(" | ");
                consoleLeft = Console.CursorLeft;
                consoleTop = Console.CursorTop;

                // Third pass - create playbook entries.
                int badSolvedScores = 0;
                int firstBadIndex = -1;
                int? firstBadScore = null;
                Entry[] entryArray = new Entry[entryCount];
                for (int i = 0; i < entryCount; i++) {
                    if (i % (entryCount / 100) == 0) {
                        Console.SetCursorPosition(consoleLeft, consoleTop);
                        Console.Write("{0:#0}%", 100.0 / entryCount * i);
                    }

                    Entry entry;
                    if (!this.TryGetEntryByCanonicalKey(canonicalKeys[i], out entry)) {
                        entry = new Entry(this, states[i], canonicalKeys[i]);
                        this.AddEntry(canonicalKeys[i], entry);
                    }

                    int? solvedScore = null;
                    if (solvedScores.TryGetValue(i, out solvedScore)) {
                        Debug.Assert(solvedScore != null);
                        if (solvedScore.Value < -64 || solvedScore.Value > 64) {
                            badSolvedScores++;
                            if (firstBadIndex < 0) {
                                firstBadIndex = i;
                                firstBadScore = solvedScore.Value;
                            }
                        } else {
                            Debug.Assert(entry.SolvedScore == null || entry.SolvedScore.Value == solvedScore.Value);
                            entry.SolvedScore = solvedScore.Value;
                        }
                    }

                    entryArray[i] = entry;
                }

                Console.Write(" | ");
                consoleLeft = Console.CursorLeft;
                consoleTop = Console.CursorTop;

                // Fourth pass - link parents and children.
                for (int i = 0; i < entryCount; i++) {
                    if (i % (entryCount / 100) == 0) {
                        Console.SetCursorPosition(consoleLeft, consoleTop);
                        Console.Write("{0:#0}%", 100.0 / entryCount * i);
                    }

                    Entry entry = entryArray[i];

                    if (entry == null) {
                        continue;
                    }

                    if (parents[i] != null) {
                        foreach (int parentIndex in parents[i]) {
                            Debug.Assert(entryArray[parentIndex] != null);
                            entry.AddParent(entryArray[parentIndex]);
                        }
                    }

                    if (children[i] != null) {
                        foreach (int childIndex in children[i]) {
                            Entry child = entryArray[childIndex];
                            if (child != null) {
                                entry.AddChild(child);
                            }
                        }
                    }
                }

                Console.Write(" | ");

                if (badSolvedScores > 0) {
                    Console.WriteLine();
                    Console.WriteLine("WARNING: skipped {0} out-of-range SolvedScore(s).", badSolvedScores);

                    // Print diagnostic for the first offending entry.
                    if (firstBadIndex >= 0) {
                        Entry badEntry = entryArray[firstBadIndex];
                        Console.WriteLine();
                        Console.WriteLine("=== First out-of-range SolvedScore (index {0}, value {1}) ===", firstBadIndex, firstBadScore);
                        badEntry.PrintDiagnostic();
                        Console.WriteLine("=== End diagnostic ===");
                        Console.WriteLine();
                    }
                }
            } catch (Exception ex) {
                Console.WriteLine("error.");
                Console.WriteLine(ex);
                return;
            } finally {
                reader.Close();
                GC.Collect();
            }

            TimeSpan elapsed = DateTime.Now - start;
            Console.WriteLine(
                "done.");
            Console.WriteLine(
                "{0:n0} entries. Time elapsed = {1:0.000} seconds.",
                this.entries.Count,
                elapsed.TotalSeconds);
        }

        #endregion

        #region Printing Stats

        public void PrintStats() {
            Console.WriteLine(string.Format(
                CultureInfo.CurrentCulture,
                "Othello Playbook with {0:n0} entries.",
                this.entries.Count));
            Console.WriteLine(
                "Root node: {0} {1}",
                this.Root.Children.Count,
                this.Root.Children.Count == 1 ? "child" : "children");

            // TODO: make this iterative
            const int iters = 5;
            HashSet<Entry> children = new HashSet<Entry>() { this.Root };
            for (int i = 1; i <= iters; i++) {
                if (children.Count == 0) {
                    Console.WriteLine("No generation {0} children.", i);
                    break;
                } else {
                    Console.WriteLine(
                        "Generation {0}: {1} distinct {2}",
                        i,
                        children.Count,
                        children.Count == 1 ? "entry" : "entries");
                    Console.Write("Generation {0} scores: ", i);
                    PrintScores(children);
                }

                if (i == iters) {
                    break;
                }

                HashSet<Entry> grandchildren = new HashSet<Entry>();
                foreach (Entry child in children) {
                    grandchildren.UnionWith(child.Children);
                }
                children = grandchildren;
            }
            Console.WriteLine();

            Console.WriteLine("Number of entries by game stage:");
            List<int> gameStages = this.entriesByGameStage.Keys.ToList();
            gameStages.Sort();
            int totalCount = 0;
            int totalSolvedScores = 0;
            foreach (int gameStage in gameStages) {
                int count = this.entriesByGameStage[gameStage].Count;
                int solvedScores = this.entriesByGameStage[gameStage].Count(e => e.SolvedScore != null);
                totalCount += count;
                totalSolvedScores += solvedScores;
                Console.WriteLine(
                    "{0:##} {1:n0} \tSolvedScore: {2:n0} ({3:#0.00}%)",
                    gameStage,
                    count,
                    solvedScores,
                    100.0 / count * solvedScores);
            }
            Console.WriteLine(
                "Total = {0:n0} SolvedScore: {1:n0} ({2:#0.00}%)",
                totalCount,
                totalSolvedScores,
                100.0 / totalCount * totalSolvedScores);

            Console.WriteLine();
        }

        private static void PrintScores(IEnumerable<Entry> entries, int grouping = 15, int indent = 4) {
            grouping = Math.Max(grouping, 1);
            indent = Math.Max(indent, 0);

            int column = 0;
            foreach (Entry entry in entries) {
                if (column == grouping) {
                    column = 0;
                    Console.WriteLine();
                    Console.Write(new string(' ', indent));
                }

                Console.Write("{0} ", entry.Score);
                column++;
            }
            Console.WriteLine();
        }

        #endregion

        #region Test Methods

        private const int MaxPrintedFailures = 5;

        /// <summary>
        /// Check score cache coherency by verifying that cached scores are consistent
        /// with SolvedScore and negamax over children.
        /// </summary>
        private int CheckScoreCache(bool verbose) {
            var visited = new HashSet<Entry>();
            var toCheck = new Queue<Entry>();
            int failed = 0;

            toCheck.Enqueue(this.Root);
            visited.Add(this.Root);
            while (toCheck.Count > 0) {
                Entry entry = toCheck.Dequeue();

                string reason = entry.CheckScoreCacheVerbose();
                if (reason != null) {
                    failed++;
                    if (verbose && failed <= MaxPrintedFailures) {
                        Console.WriteLine("CheckScoreCache FAIL: {0}", reason);
                        entry.PrintDiagnostic();
                    }
                }

                foreach (Entry child in entry.Children) {
                    if (visited.Add(child)) {
                        toCheck.Enqueue(child);
                    }
                }
            }

            return failed;
        }

        /// <summary>
        /// Check that parent-child links are symmetric: if A has B as a child,
        /// B must have A as a parent, and vice versa.
        /// </summary>
        private int CheckParentChildSymmetry(bool verbose) {
            int failed = 0;

            foreach (Entry entry in this.entries.Values) {
                foreach (Entry child in entry.Children) {
                    if (!child.Parents.Contains(entry)) {
                        failed++;
                        if (verbose && failed <= MaxPrintedFailures) {
                            Console.WriteLine(
                                "CheckParentChildSymmetry FAIL: child does not list entry as parent");
                            Console.WriteLine("--- Entry ---");
                            entry.PrintDiagnostic();
                            Console.WriteLine("--- Child ---");
                            child.PrintDiagnostic();
                        }
                    }
                }

                foreach (Entry parent in entry.Parents) {
                    if (!parent.Children.Contains(entry)) {
                        failed++;
                        if (verbose && failed <= MaxPrintedFailures) {
                            Console.WriteLine(
                                "CheckParentChildSymmetry FAIL: parent does not list entry as child");
                            Console.WriteLine("--- Entry ---");
                            entry.PrintDiagnostic();
                            Console.WriteLine("--- Parent ---");
                            parent.PrintDiagnostic();
                        }
                    }
                }
            }

            return failed;
        }

        /// <summary>
        /// Detect entries where different Pass states may have been conflated due
        /// to OthelloNode.Equals ignoring the Pass field. Checks two conditions:
        /// 1. A pass entry whose parent doesn't actually produce a pass child
        ///    (indicates the pass entry was found via Equals when a non-pass lookup
        ///    should have created a separate entry).
        /// 2. A non-pass entry whose parent only produces a pass child (the reverse:
        ///    a pass lookup found an existing non-pass entry).
        /// </summary>
        private int CheckPassConflation(bool verbose) {
            int failed = 0;

            foreach (Entry entry in this.entries.Values) {
                foreach (Entry parent in entry.Parents) {
                    List<OthelloNode> legalChildren = parent.State.GetChildren();
                    bool parentProducesPass = legalChildren.Count == 1 && legalChildren[0].Pass;

                    if (entry.State.Pass && !parentProducesPass) {
                        failed++;
                        if (verbose && failed <= MaxPrintedFailures) {
                            Console.WriteLine(
                                "CheckPassConflation FAIL: pass entry ({0} pieces, turn={1}) " +
                                "has parent ({2} pieces, pass={3}, turn={4}) which does not produce a pass child",
                                entry.State.OccupiedSquareCount,
                                entry.State.Turn == OthelloNode.BLACK ? "Black" : "White",
                                parent.State.OccupiedSquareCount, parent.State.Pass,
                                parent.State.Turn == OthelloNode.BLACK ? "Black" : "White");
                            entry.PrintDiagnostic();
                        }
                    } else if (!entry.State.Pass && parentProducesPass) {
                        failed++;
                        if (verbose && failed <= MaxPrintedFailures) {
                            Console.WriteLine(
                                "CheckPassConflation FAIL: non-pass entry ({0} pieces, turn={1}) " +
                                "has parent ({2} pieces, pass={3}, turn={4}) which only produces a pass child",
                                entry.State.OccupiedSquareCount,
                                entry.State.Turn == OthelloNode.BLACK ? "Black" : "White",
                                parent.State.OccupiedSquareCount, parent.State.Pass,
                                parent.State.Turn == OthelloNode.BLACK ? "Black" : "White");
                            entry.PrintDiagnostic();
                        }
                    }
                }
            }

            return failed;
        }

        /// <summary>
        /// Check that every entry is keyed correctly in the dictionary: the
        /// canonical form of entry.State must map back to the same entry.
        /// Also checks that the entry's State is isomorphic to its canonical
        /// form (i.e. they represent the same board up to symmetry).
        /// </summary>
        private int CheckCanonicalForm(bool verbose) {
            int failed = 0;

            foreach (var kvp in this.entries) {
                OthelloNode key = kvp.Key;
                Entry entry = kvp.Value;
                OthelloNode canonical = OthelloNode.Canonicalize(entry.State);

                // The dictionary key must equal the canonical form of the entry's state.
                if (!key.Equals(canonical) || key.Pass != canonical.Pass) {
                    failed++;
                    if (verbose && failed <= MaxPrintedFailures) {
                        Console.WriteLine(
                            "CheckCanonicalForm FAIL: dictionary key does not match Canonicalize(entry.State)");
                        entry.PrintDiagnostic();
                    }
                }
            }

            return failed;
        }

        /// <summary>
        /// Check that no two entries are isomorphic (same board up to symmetry).
        /// The dictionary keys on canonical state via Equals, but since Equals
        /// ignores Pass, two entries differing only in Pass could coexist. This
        /// check catches that and any other source of duplicate entries.
        /// </summary>
        private int CheckDuplicates(bool verbose) {
            int failed = 0;

            // Build a lookup keyed on (canonical board, pass) to detect entries
            // that are Equals-equal but differ in Pass.
            var seen = new Dictionary<OthelloNode, List<Entry>>();
            foreach (Entry entry in this.entries.Values) {
                OthelloNode canonical = OthelloNode.Canonicalize(entry.State);
                if (!seen.TryGetValue(canonical, out List<Entry> list)) {
                    list = new List<Entry>(1);
                    seen[canonical] = list;
                }
                list.Add(entry);
            }

            foreach (var kvp in seen) {
                List<Entry> list = kvp.Value;
                if (list.Count <= 1) continue;

                for (int i = 0; i < list.Count; i++) {
                    for (int j = i + 1; j < list.Count; j++) {
                        bool sameByEquals = list[i].State.Equals(list[j].State);
                        bool passDiffers = list[i].State.Pass != list[j].State.Pass;
                        failed++;
                        if (verbose && failed <= MaxPrintedFailures) {
                            Console.WriteLine(
                                "CheckDuplicates FAIL: {0} entries share canonical state ({1} pieces). " +
                                "Equals={2}, PassDiffers={3}",
                                list.Count, list[0].State.OccupiedSquareCount,
                                sameByEquals, passDiffers);
                            Console.WriteLine("--- Entry A (pass={0}) ---", list[i].State.Pass);
                            list[i].PrintDiagnostic();
                            Console.WriteLine("--- Entry B (pass={0}) ---", list[j].State.Pass);
                            list[j].PrintDiagnostic();
                        }
                    }
                }
            }

            return failed;
        }

        /// <summary>
        /// Scans every entry, finds legal children that exist in the playbook but are
        /// not linked, and adds the missing parent→child and child→parent links.
        /// Returns the number of links added.
        /// </summary>
        public int RepairMissingChildLinks(bool verbose) {
            if (verbose) {
                Console.Write("Adding missing child links... ");
            }
            DateTime start = DateTime.Now;
            int consoleLeft = Console.CursorLeft;
            int consoleTop = Console.CursorTop;

            int visited = 0;
            int repaired = 0;

            // Read phase: collect missing (parent, child) pairs in parallel.
            // TryGetEntry and GetChildren are read-only on the entries dictionary,
            // so no locking is needed here.
            var missingLinks = new ConcurrentBag<(Entry Parent, Entry Child)>();
            Entry[] entryArray = this.entries.Values.ToArray();
            int entryCount = entryArray.Length;

            Parallel.ForEach(
                Partitioner.Create(0, entryCount),
                new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount },
                (range, _) => {
                    for (int i = range.Item1; i < range.Item2; i++) {
                        Entry entry = entryArray[i];
                        if (entry.State.IsGameOver) continue;
                        foreach (OthelloNode legalChild in entry.State.GetChildren()) {
                            if (this.TryGetEntry(legalChild, out Entry childEntry) &&
                                !entry.Children.Contains(childEntry)) {
                                missingLinks.Add((entry, childEntry));
                            }
                        }

                        if (verbose) {
                            int done = Interlocked.Increment(ref visited);
                            if (entryCount >= 100 && done % (entryCount / 100) == 0) {
                                Console.SetCursorPosition(consoleLeft, consoleTop);
                                Console.Write("{0:#0}% ", 100.0 / entryCount * done);
                            }
                        }
                    }
                });

            // Write phase: apply links serially to avoid concurrent HashSet mutations.
            // Re-check each pair since duplicates may appear in missingLinks when the
            // same (parent, child) pair was identified by more than one thread.
            foreach ((Entry parent, Entry child) in missingLinks) {
                if (!parent.Children.Contains(child)) {
                    parent.AddChild(child);
                    child.AddParent(parent);
                    repaired++;
                }
            }

            if (verbose) {
                Console.WriteLine(
                    "done. Visited {0} nodes and added {1} missing links in {2:0.0000} seconds.",
                    visited,
                    repaired,
                    (DateTime.Now - start).TotalSeconds);
            }

            return repaired;
        }

        /// <summary>
        /// Check that legal children present in the playbook are linked as children
        /// of their parent entry. Detects missing child links where a position's
        /// legal move leads to a board that exists in the playbook but isn't connected.
        /// </summary>
        private int CheckMissingChildLinks(bool verbose) {
            int failed = 0;

            foreach (Entry entry in this.entries.Values) {
                if (entry.State.IsGameOver) continue;

                List<OthelloNode> legalChildren = entry.State.GetChildren();
                foreach (OthelloNode child in legalChildren) {
                    if (this.TryGetEntry(child, out Entry childEntry) &&
                        !entry.Children.Contains(childEntry)) {
                        failed++;
                        if (verbose && failed <= MaxPrintedFailures) {
                            Console.WriteLine(
                                "CheckMissingChildLinks FAIL: legal child exists in playbook but is not linked");
                            Console.WriteLine("--- Parent ---");
                            entry.PrintDiagnostic();
                            Console.WriteLine("--- Unlinked child ---");
                            childEntry.PrintDiagnostic();
                        }
                    }
                }
            }

            return failed;
        }

        /// <summary>
        /// Tallies entries whose color-swapped form (turn flipped, self/other swapped)
        /// also exists in the playbook under a different canonical form. These represent
        /// the same physical board reachable from the other player's perspective.
        /// Returns count / 2 since each pair is discovered from both sides.
        /// </summary>
        private int CheckColorSwapDuplicates(bool verbose) {
            int found = 0;

            foreach (Entry entry in this.entries.Values) {
                OthelloNode swapped = OthelloNode.SwapColors(entry.State);
                OthelloNode swappedCanonical = OthelloNode.Canonicalize(swapped);
                OthelloNode originalCanonical = OthelloNode.Canonicalize(entry.State);

                // Only count cases where the canonical forms differ (otherwise it's
                // already handled by geometric symmetry).
                if (swappedCanonical.Equals(originalCanonical)) {
                    continue;
                }

                if (this.TryGetEntry(swapped, out Entry swappedEntry)) {
                    found++;
                    if (verbose && found <= MaxPrintedFailures) {
                        Console.WriteLine(
                            "CheckColorSwapDuplicates: entry ({0} pieces, turn={1}, pass={2}) " +
                            "has color-swapped duplicate ({3} pieces, turn={4}, pass={5})",
                            entry.State.OccupiedSquareCount,
                            entry.State.Turn == OthelloNode.BLACK ? "Black" : "White",
                            entry.State.Pass,
                            swappedEntry.State.OccupiedSquareCount,
                            swappedEntry.State.Turn == OthelloNode.BLACK ? "Black" : "White",
                            swappedEntry.State.Pass);
                        Console.WriteLine("--- Original ---");
                        entry.PrintDiagnostic();
                        Console.WriteLine("--- Color-swapped ---");
                        swappedEntry.PrintDiagnostic();
                    }
                }
            }

            // Color-symmetric duplicates are a bidirectional relation, meaning we're double-counting nodes. Correct
            // that here.
            return found / 2;
        }

        /// <summary>
        /// Check for orphan subtrees.
        /// </summary>
        private int CheckTree(bool verbose) {
            if (this.Root == null) {
                return 1;
            }

            return 0; // TODO
        }

        /// <summary>
        /// Check for tree nodes that aren't present in the entry set.
        /// </summary>
        private int CheckEntries(bool verbose) {
            return 0; // TODO
        }

        /// <summary>
        /// Check for a 1-1 mapping between entries and entriesByGameStage.
        /// </summary>
        private int CheckGameStageEntries(bool verbose) {
            return 0; // TODO
        }

        /// <summary>
        /// Run a series of checks on the playbook.
        /// </summary>
        /// <param name="verbose"></param>
        /// <returns>True if the playbook is in a good state.</returns>
        public bool Check(bool verbose = false) {
            var results = new (string Name, int Failures)[] {
                ("Tree structure", this.CheckTree(verbose)),
                ("Entry set", this.CheckEntries(verbose)),
                ("Game stage index", this.CheckGameStageEntries(verbose)),
                ("Canonical form", this.CheckCanonicalForm(verbose)),
                ("Duplicates", this.CheckDuplicates(verbose)),
                ("Parent-child symmetry", this.CheckParentChildSymmetry(verbose)),
                ("Pass conflation", this.CheckPassConflation(verbose)),
                ("Missing child links", this.CheckMissingChildLinks(verbose)),
                ("Score cache coherency", this.CheckScoreCache(verbose)),
                ("Color-swap duplicates", this.CheckColorSwapDuplicates(verbose)),
            };

            Console.WriteLine();
            Console.WriteLine("Integrity check results:");
            int totalFailures = 0;
            foreach (var (name, failures) in results) {
                Console.WriteLine("  {0,-25} {1}",
                    name,
                    failures == 0 ? "PASSED" : string.Format("FAILED ({0:n0})", failures));
                totalFailures += failures;
            }
            Console.WriteLine("  {0,-25} {1:n0}", "Total failures", totalFailures);

            return totalFailures == 0;
        }

        #endregion

        public class Entry {
            private readonly OthelloPlaybook playbook;

            public readonly HashSet<Entry> Parents = new HashSet<Entry>();
            public readonly HashSet<Entry> Children = new HashSet<Entry>();

            // TODO: add ExactScore to store solved endgames
            public readonly OthelloNode State;

            // TODO: add DescendantCount and maybe AncestorCount
            private int? score = null;
            private readonly OthelloNode canonicalState;
            private readonly int cachedHashCode;

            public Entry(OthelloPlaybook playbook, OthelloNode state)
                : this(playbook, state, OthelloNode.Canonicalize(state)) {
            }

            internal Entry(OthelloPlaybook playbook, OthelloNode state, OthelloNode canonicalState) {
                if (playbook == null || state == null) {
                    throw new ArgumentNullException();
                }

                this.playbook = playbook;
                this.State = state;
                this.canonicalState = canonicalState;
                this.cachedHashCode = this.canonicalState.GetHashCode();
            }

            internal void ClearCachedScore() {
                this.score = null;
            }

            public int Score {
                get {
                    if (this.SolvedScore != null) {
                        Debug.Assert(this.SolvedScore >= -64 && this.SolvedScore <= 64);

                        this.score = this.SolvedScore;
                        return this.SolvedScore.Value;
                    }

                    if (this.score == null) {
                        if (this.Children.Count == 0) {
                            if (this.State.IsGameOver) {
                                this.score = this.State.PieceCountSpread();
                                this.SolvedScore = this.score;
                            } else if (this.State.EmptySquareCount <= SearchUtils.EndgameDepth) {
                                // We can calculate the endgame score exactly.
                                // TODO: replace Playbook.Player with static calls to MtdF search. Then, get rid of
                                //       GetScore() and the out param
                                Console.Write("* ");
                                this.score = this.playbook.Player.GetScore(this.State);
                                this.SolvedScore = this.score;
                            } else {
                                throw new InvalidOperationException(
                                    string.Format("Unexpected childless non-endgame leaf with no SolvedScore ({0} pieces).\n{1}",
                                        this.State.OccupiedSquareCount, this.State));
                            }
                        } else {
                            // The best way to estimate the score of this board is to do a negamax search
                            // within the space of recorded game states.
                            this.score = -this.Children.Min(entry => entry.Score);
                        }
                    }

                    return this.score.Value;
                }
            }

            private int? solvedScore;
            public int? SolvedScore {
                get {
                    return this.solvedScore;
                }

                internal set {
                    if (value != null && (value < -64 || value > 64)) {
                        Console.WriteLine();
                        Console.WriteLine("WARNING: out-of-range SolvedScore {0} being set:", value);
                        this.PrintDiagnostic();
                        Console.WriteLine(new System.Diagnostics.StackTrace());
                    }
                    Debug.Assert(value == null || value >= -64 && value <= 64);
                    this.solvedScore = value;
                }
            }

            public int UnexploredChildren {
                get {
                    return this.State.GetChildren().Count(
                        child => OthelloNode.GetSymmetries(child).Any(sym => this.Children.Any(ent => ent.State.Equals(sym))));
                }
            }

            private void InvalidateCachedScore() {
                if (this.score == null) return;
                this.score = null;
                foreach (Entry entry in this.Parents) {
                    entry.InvalidateCachedScore();
                }
            }

            public void Unlink() {
                this.Parents.Clear();
                this.Children.Clear();
                this.InvalidateCachedScore();
            }

            /// <summary>
            /// Links any legal children of this entry that already exist in the playbook
            /// but are not yet connected. Returns the number of links added.
            /// </summary>
            internal int LinkExistingLegalChildren() {
                if (this.State.IsGameOver) return 0;

                int linked = 0;
                foreach (OthelloNode legalChild in this.State.GetChildren()) {
                    if (this.playbook.TryGetEntry(legalChild, out Entry childEntry) &&
                        !this.Children.Contains(childEntry)) {
                        this.AddChild(childEntry);
                        childEntry.AddParent(this);
                        linked++;
                    }
                }
                return linked;
            }

            public void AddParent(Entry parent) {
                Debug.Assert(this.playbook == parent.playbook);
#if DEBUG
                if (!parent.State.IsIsomorphicParent(this.State)) {
                    throw new ArgumentException(
                        string.Format("Parent ({0} pieces, pass={1}, gameOver={2}) is not an isomorphic parent of child ({3} pieces, pass={4}, gameOver={5}).\nParent:\n{6}\nChild:\n{7}",
                            parent.State.OccupiedSquareCount, parent.State.Pass, parent.State.IsGameOver,
                            this.State.OccupiedSquareCount, this.State.Pass, this.State.IsGameOver,
                            parent.State, this.State));
                }
#endif

                if (this.Parents.Add(parent)) {
                    parent.InvalidateCachedScore();
                }
            }

            // TODO: Search for all possible parents and link them up. This may prove prohibitive.
            public void AddChild(Entry child) {
                Debug.Assert(this.playbook == child.playbook);
#if DEBUG
                if (!this.State.IsIsomorphicParent(child.State)) {
                    throw new ArgumentException();
                }
#endif

                if (this.Children.Add(child)) {
                    this.InvalidateCachedScore();
                }
            }

            /// <summary>
            /// Prints board state, parents, and children with their SolvedScores for debugging.
            /// </summary>
            internal void PrintDiagnostic() {
                const int boardWidth = 15; // "X . X . X . X ." = 8 chars + 7 spaces
                const int groupSize = 6;

                // Print parents in groups.
                if (this.Parents.Count > 0) {
                    Console.WriteLine("Parents ({0}):", this.Parents.Count);
                    PrintEntryGroup(boardWidth, groupSize, [.. this.Parents]);
                }

                // Print current node.
                Console.WriteLine("Entry ({0} pieces, gameOver={1}):",
                    this.State.OccupiedSquareCount,
                    this.State.IsGameOver);
                Console.Write(OthelloNode.PrintNodes(1, true, this.State));
                PrintSolvedScoreRow(boardWidth, this);

                // Print children in groups.
                if (this.Children.Count > 0) {
                    Console.WriteLine("Children ({0}):", this.Children.Count);
                    PrintEntryGroup(boardWidth, groupSize, [.. this.Children]);
                }
            }

            private static void PrintEntryGroup(int boardWidth, int groupSize, Entry[] entries) {
                for (int i = 0; i < entries.Length; i += groupSize) {
                    int count = Math.Min(groupSize, entries.Length - i);
                    OthelloNode[] nodes = new OthelloNode[count];
                    Entry[] group = new Entry[count];
                    for (int j = 0; j < count; j++) {
                        nodes[j] = entries[i + j].State;
                        group[j] = entries[i + j];
                    }
                    Console.Write(OthelloNode.PrintNodes(groupSize, true, nodes));
                    PrintSolvedScoreRow(boardWidth, group);
                }
            }

            private static void PrintSolvedScoreRow(int boardWidth, params Entry[] entries) {
                for (int i = 0; i < entries.Length; i++) {
                    if (i > 0) {
                        Console.Write(" | ");
                    }
                    string label = string.Format("solved={0}",
                        entries[i].SolvedScore?.ToString() ?? "null");
                    Console.Write(label.PadRight(boardWidth));
                }
                Console.WriteLine();
            }

            #region Hashing and Equality

            public override bool Equals(object obj) {
                Entry entry = obj as Entry;

                return entry != null && this.canonicalState.Equals(entry.canonicalState);
            }

            public override int GetHashCode() {
                return this.cachedHashCode;
            }

            #endregion

            #region Test Methods

            internal string CheckScoreCacheVerbose() {
                if (this.score == null) {
                    return null;
                }

                if (this.SolvedScore != null) {
                    return this.score == this.SolvedScore ? null :
                        string.Format("score={0} != solvedScore={1}", this.score, this.SolvedScore);
                }

                int min = int.MaxValue;
                foreach (Entry child in this.Children) {
                    if (child.score == null) {
                        return string.Format("score={0} but child has null score ({1} pieces)",
                            this.score, child.State.OccupiedSquareCount);
                    }

                    if (child.score.Value < min) {
                        min = child.score.Value;
                    }
                }

                return this.score.Value == -min ? null :
                    string.Format("score={0} but -min(children)={1}", this.score, -min);
            }

            internal bool CheckScoreCache() {
                if (this.score == null) {
                    return true;
                }

                if (this.SolvedScore != null) {
                    return this.score == this.SolvedScore;
                }

                int min = int.MaxValue;
                foreach (Entry child in this.Children) {
                    if (child.score == null) {
                        return false;
                    }

                    if (child.score.Value < min) {
                        min = child.score.Value;
                    }
                }

                return this.score.Value == -min;
            }

            #endregion
        }
    }
}
