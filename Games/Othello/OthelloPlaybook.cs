using System;
using System.Collections.Generic;
using System.Globalization;
using System.Diagnostics;
using System.Linq;
using System.Text;

namespace Othello {
    // TODO: implement ICollection<OthelloNode>?
    public class OthelloPlaybook {
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

        #region Printing Stats

        public void PrintStats() {
            Console.WriteLine(string.Format(
                CultureInfo.CurrentCulture,
                "Othello Playbook with {0:n} entries.",
                this.entries.Count));
            Console.WriteLine(
                "Root node: {0} {1}.",
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

            public readonly List<Entry> Parents = new List<Entry>();
            public readonly List<Entry> Children = new List<Entry>();

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

            public int? SolvedScore {
                get;
                private set;
            }

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

            public void AddParent(Entry parent) {
                Debug.Assert(this.playbook == parent.playbook);

                if (!parent.State.IsIsomorphicParent(this.State)) {
                    throw new ArgumentException();
                }

                if (!this.Parents.Any(parent.Equals)) {
                    this.Parents.Add(parent);
                    parent.InvalidateCachedScore();
                }
            }

            // TODO: Search for all possible parents and link them up. This may prove prohibitive.
            public void AddChild(Entry child) {
                Debug.Assert(this.playbook == child.playbook);

                if (!this.State.IsIsomorphicParent(child.State)) {
                    throw new ArgumentException();
                }

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
