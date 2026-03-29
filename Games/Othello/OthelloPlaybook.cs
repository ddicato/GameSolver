using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;

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
            this.entries[OthelloNode.Canonicalize(entry.State)] = entry;

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
                    current = new Entry(this, history[i].Node, history[i].Score);
                    this.AddEntry(current);
                }

                current.AddParent(parent);
                parent.AddChild(current);

                parent = current;

                // The first game we encounter with a SolvedScore can be a leaf node, so
                // there is no need to continue adding children.
                if (current.SolvedScore != null) {
                    break;
                }

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
                        entry.SolvedScore = -entry.Children.Max(c => c.SolvedScore.Value);
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

                // Second pass - create playbook entries.
                int badSolvedScores = 0;
                Entry[] entryArray = new Entry[entryCount];
                for (int i = 0; i < entryCount; i++) {
                    if (i % (entryCount / 100) == 0) {
                        Console.SetCursorPosition(consoleLeft, consoleTop);
                        Console.Write("{0:#0}%", 100.0 / entryCount * i);
                    }

                    Entry entry;
                    if (!this.TryGetEntry(states[i], out entry)) {
                        entry = new Entry(this, states[i]);
                        this.AddEntry(entry);
                    }

                    int? solvedScore = null;
                    if (solvedScores.TryGetValue(i, out solvedScore)) {
                        Debug.Assert(solvedScore != null);
                        if (solvedScore.Value < -64 || solvedScore.Value > 64) {
                            badSolvedScores++;
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

                // Third pass - link parents and children.
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
                    Console.Write("WARNING: skipped {0} out-of-range SolvedScore(s). ", badSolvedScores);
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

        /// <summary>
        /// Check cache coherency.
        /// </summary>
        /// <param name="verbose"></param>
        /// <returns>True if the playbook is in a good state.</returns>
        private bool CheckCache(bool verbose) {
            var toCheck = new Queue<Entry>();

            toCheck.Enqueue(this.Root);
            while (toCheck.Count > 0) {
                Entry entry = toCheck.Dequeue();

                if (!entry.CheckCache()) {
                    return false;
                }

                foreach (Entry child in entry.Children) {
                    toCheck.Enqueue(child);
                }
            }

            return true;
        }

        /// <summary>
        /// Check for orphan subtrees.
        /// </summary>
        /// <param name="verbose"></param>
        /// <returns>True if the playbook is in a good state.</returns>
        private bool CheckTree(bool verbose) {
            if (this.Root == null) {
                return false;
            }

            return true; // TODO
        }

        /// <summary>
        /// Check for tree nodes that aren't present in the entry set.
        /// </summary>
        /// <param name="verbose"></param>
        /// <returns>True if the playbook is in a good state.</returns>
        private bool CheckEntries(bool verbose) {
            return true; // TODO
        }

        /// <summary>
        /// Check for a 1-1 mapping between entries and entriesByGameStage.
        /// </summary>
        /// <param name="verbose"></param>
        /// <returns>True if the playbook is in a good state.</returns>
        private bool CheckGameStageEntries(bool verbose) {
            return true; // TODO
        }

        /// <summary>
        /// Run a series of checks on the playbook.
        /// </summary>
        /// <param name="verbose"></param>
        /// <returns>True if the playbook is in a good state.</returns>
        public bool Check(bool verbose = false) {
            return this.CheckTree(verbose) &&
                this.CheckEntries(verbose) &&
                this.CheckGameStageEntries(verbose) &&
                this.CheckCache(verbose);
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

            public Entry(OthelloPlaybook playbook, OthelloNode state, int? solvedScore = null) {
                if (playbook == null || state == null) {
                    throw new ArgumentNullException();
                }

                this.playbook = playbook;
                this.State = state;
                this.canonicalState = OthelloNode.Canonicalize(state);
                this.cachedHashCode = this.canonicalState.GetHashCode();
            }

            public int Score {
                get {
                    if (this.SolvedScore != null) {
                        Debug.Assert(this.SolvedScore >= -64 && this.SolvedScore <= 64);

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
                            this.score = -this.Children.Max(entry => entry.Score);
                        }
                    }

                    return this.score.Value;
                }
            }

#if DEBUG
            private int? solvedScore;
            public int? SolvedScore {
                get {
                    return this.solvedScore;
                }

                internal set {
                    Debug.Assert(value >= -64 && value <= 64);
                    this.solvedScore = value;
                }
            }
#else
            public int? SolvedScore {
                get;
                internal set;
            }
#endif

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

            internal bool CheckCache() {
                if (this.score == null) {
                    return true;
                }

                if (this.SolvedScore != null) {
                    return this.score == this.SolvedScore;
                }

                int max = -int.MaxValue;
                foreach (Entry child in this.Children) {
                    if (child.score == null) {
                        return false;
                    }

                    if (child.score.Value > max) {
                        max = child.score.Value;
                    }
                }

                return this.score.Value == -max;
            }

            #endregion
        }
    }
}
