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
            this.entries[entry.State] = entry;

            int gameStage = entry.State.OccupiedSquareCount;
            HashSet<Entry> set;
            if (!this.entriesByGameStage.TryGetValue(gameStage, out set)) {
                set = new HashSet<Entry>();
                this.entriesByGameStage[gameStage] = set;
            }
            set.Add(entry);
        }

        public void AddGame(IList<OthelloNode> history) {
            if (history == null) {
                throw new ArgumentNullException();
            }

            if (history.Count == 0 ||
                !this.Root.State.Equals(history[0]) ||
                !history[history.Count - 1].IsGameOver) {
                    throw new ArgumentException();
            }

            Debug.Assert(this.Contains(history[0]));

            Entry parent = this.Root;
            for (int i = 1; i < history.Count; i++) {
                Entry current;
                if (!this.TryGetEntry(history[i], out current)) {
                    current = new Entry(this, history[i]);
                    this.AddEntry(current);
                }

                current.AddParent(parent);
                parent.AddChild(current);

                parent = current;
            }
        }

        public bool Contains(OthelloNode state) {
            return OthelloNode.GetSymmetries(state).Any(this.entries.ContainsKey);
        }

        public bool TryGetEntry(OthelloNode state, out Entry value) {
            foreach (OthelloNode key in OthelloNode.GetSymmetries(state)) {
                if (this.entries.TryGetValue(key, out value)) {
                    return true;
                }
            }

            value = null;
            return false;
        }

        public void Clear() {
            this.entries.Clear();
            this.entriesByGameStage.Clear();

            this.Root.Unlink();
            this.AddEntry(this.Root);
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
        // <entry index>:(<parent index>(,<parent index>)+)?:(<child index>(,<child index>)+)?:<game state>

        // The file is then organized as follows:
        // N\n
        // 0::<child list>:<root state>\n
        // 1:<parent list>:<child list>:<state>\n
        // ...
        // N:<parent list>:<child list>:<state>\n
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

                    writer.WriteLine(entry.State.Serialize());
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
                Console.Write("Loading game database...");
            } catch {
                Console.WriteLine("I/O Error.");
                return;
            }

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
                for (int i = 0; i < entryCount; i++) {
                    line = reader.ReadLine();
                    if (line == null) {
                        throw new InvalidDataException("Unexpected EOF.");
                    }
                    line = line.Trim();

                    string[] data = line.Split(new char[] { SerializationDelim });
                    if (data.Length != 4) {
                        throw new InvalidDataException(
                            string.Format("Expected 3 '{0}' on a line but got {1}.", SerializationDelim, data.Length - 1));
                    }

                    int checkIndex = int.Parse(data[0]);
                    if (checkIndex != i) {
                        throw new InvalidDataException(
                            string.Format("Expected index {0} but got {1}.", i, checkIndex));
                    }

                    parents[i] = ReadList(data[1]);
                    children[i] = ReadList(data[2]);
                    states[i] = OthelloNode.Deserialize(data[3]);

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

                Console.Write(" .");

                // Second pass - create playbook entries.
                Entry[] entryArray = new Entry[entryCount];
                for (int i = 0; i < entryCount; i++) {
                    Entry entry;
                    if (!this.TryGetEntry(states[i], out entry)) {
                        entry = new Entry(this, states[i]);
                        this.AddEntry(entry);
                    }

                    entryArray[i] = entry;
                }

                Console.Write(" .");

                // Third pass - link parents and children.
                for (int i = 0; i < entryCount; i++) {
                    Entry entry = entryArray[i];

                    if (parents[i] != null) {
                        foreach (int parentIndex in parents[i]) {
                            entry.AddParent(entryArray[parentIndex]);
                        }
                    }

                    if (children[i] != null) {
                        foreach (int childIndex in children[i]) {
                            entry.AddChild(entryArray[childIndex]);
                        }
                    }
                }

                Console.Write(" .");
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
                "done. {0:n0} entries. Time elapsed = {1:0.000} seconds.",
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

            if (this.Root.Children.Count > 0) {
                Console.Write("Child scores: ");
                PrintScores(this.Root.Children);

                HashSet<Entry> grandchildren = new HashSet<Entry>();
                foreach (Entry child in this.Root.Children) {
                    grandchildren.UnionWith(child.Children);
                }

                Console.WriteLine(
                    "Generation {0}: {1} distinct {2}",
                    2,
                    grandchildren.Count,
                    grandchildren.Count == 1 ? "entry" : "entries");
                if (grandchildren.Count > 0) {
                    Console.Write("Generation {0} scores: ", 2);
                    PrintScores(grandchildren);
                }
            }

            Console.WriteLine("Number of entries by game stage:");
            List<int> gameStages = this.entriesByGameStage.Keys.ToList();
            gameStages.Sort();
            int totalCount = 0;
            foreach (int gameStage in gameStages) {
                int count = this.entriesByGameStage[gameStage].Count;
                totalCount += count;
                Console.WriteLine("{0:##} {1:n0}", gameStage, count);
            }
            Console.WriteLine("Total = {0:n0}", totalCount);

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

        public class Entry {
            private readonly OthelloPlaybook playbook;

            public readonly List<Entry> Parents = new List<Entry>(1);
            public readonly List<Entry> Children = new List<Entry>(); // TODO: add a capacity?

            // TODO: add ExactScore to store solved endgames
            public readonly OthelloNode State;

            // TODO: add DescendantCount and maybe AncestorCount
            private int? score = null;

            public Entry(OthelloPlaybook playbook, OthelloNode state) {
                if (playbook == null || state == null) {
                    throw new ArgumentNullException();
                }

                this.playbook = playbook;
                this.State = state;
            }

            public int Score {
                get {
                    if (this.score == null) {
                        if (this.Children.Count == 0) {
                            // TODO: If game isn't over, perform some kind of evaluation. Or, discount this
                            //       node completely by returning a bogus value
                            Debug.Assert(this.State.IsGameOver);

                            this.score = this.State.PieceCountSpread();
                        } else {
                            // The best way to estimate the score of this board is to do a negamax search
                            // within the space of recorded game states.
                            this.score = this.Children.Max(entry => -entry.Score);
                        }
                    }

                    return this.score.Value;
                }
            }

            // TODO
            /*public int? SolvedScore {
                get;
                private set;
            }*/

            public int UnexploredChildren {
                get {
                    int result = this.State.GetChildren().Count - this.Children.Count;
                    Debug.Assert(result >= 0);

                    return result;
                }
            }

            private void InvalidateCachedScore() {
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
                    throw new ArgumentException();
                }
#endif

                if (!this.Parents.Any(parent.Equals)) {
                    this.Parents.Add(parent);
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

                if (!this.Children.Any(child.Equals)) {
                    this.Children.Add(child);
                    this.InvalidateCachedScore();
                }
            }

            #region Hashing and Equality

            public override bool Equals(object obj) {
                Entry entry = obj as Entry;

                return entry != null && this.State.Equals(entry.State);
            }

            public override int GetHashCode() {
                return this.State.GetHashCode();
            }

            #endregion
        }
    }
}
