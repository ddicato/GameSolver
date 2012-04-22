using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Solver {
    public enum EntryType {
        NonLeaf,
        Win,
        Loss,
        Draw
    }

    public class PlaybookEntry<T> {
        public List<PlaybookEntry<T>> parents;

        // Paired with the transformation that makes the match valid.
        private List<KeyValuePair<PlaybookEntry<T>, int>> children;

        public Playbook<T> Playbook {
            get;
            private set;
        }

        public T State {
            get;
            private set;
        }

        public int ExpectedScore {
            get;
            private set;
        }

        public ulong GamesPlayed {
            get;
            private set;
        }

        private PlaybookEntry() {
            throw new InvalidOperationException();
        }

        public PlaybookEntry(Playbook<T> playbook, T state, int score) {
            this.Playbook = playbook;
            this.State = state;
            this.ExpectedScore = score;
            this.GamesPlayed = 1ul;
        }

        public int ParentCount {
            get {
                return this.parents == null ? 0 : this.parents.Count;
            }
        }

        public int ChildCount {
            get {
                return this.children == null ? 0 : this.children.Count;
            }
        }

        /* TODO: store hashset of entries instead of dict mapping state to entry
        public override bool Equals(object obj) {
            var other = obj as PlaybookEntry<T>;
            return other != null && this.State.Equals(other.State);
        }

        public override int GetHashCode() {
            return this.State.GetHashCode();
        }
        */
    }

    public class Playbook<T> {
        private readonly Tuple<Func<T, T>, Func<T, T>>[] Transforms;
        private readonly Dictionary<T, PlaybookEntry<T>> States;

        public Playbook(Tuple<Func<T, T>, Func<T, T>>[] transforms = null) {
            if (transforms == null || transforms.Length == 0) {
                this.Transforms = new Tuple<Func<T, T>, Func<T, T>>[] {
                    new Tuple<Func<T, T>, Func<T, T>>(state => state, state => state)
                };
            } else {
                this.Transforms = transforms;
            }

            this.States = new Dictionary<T, PlaybookEntry<T>>();
        }

        private bool IsEquivalent(T a, T b) {
            for (int i = 0; i < this.Transforms.Length; i++) {
                if (a.Equals(this.Transform(i, b))) {
                    return true;
                }
            }

            return false;
        }

        private bool IsEquivalent(T a, T b, out int transformIndex) {
            for (int i = 0; i < this.Transforms.Length; i++) {
                if (a.Equals(this.Transform(i, b))) {
                    transformIndex = i;
                    return true;
                }
            }

            transformIndex = -1;
            return false;
        }

        public T Transform(int index, T state) {
            return this.Transforms[index].Item1(state);
        }

        public T InverseTransform(int index, T state) {
            return this.Transforms[index].Item2(state);
        }

        public bool Contains(T state) {
            for (int i = 0; i < this.Transforms.Length; i++) {
                if (this.States.ContainsKey(this.Transform(i, state))) {
                    return true;
                }
            }

            return false;
        }

        public bool TryGetValue(T state, out PlaybookEntry<T> entry) {
            entry = null;
            for (int i = 0; i < this.Transforms.Length; i++) {
                if (this.States.TryGetValue(this.Transform(i, state), out entry)) {
                    return true;
                }
            }

            return false;
        }

        public bool Remove(T state) {
            bool removed = false;
            for (int i = 0; i < this.Transforms.Length; i++) {
                removed |= this.States.Remove(this.Transform(i, state));
            }

            return removed;
        }

        public PlaybookEntry<T> this[T state] {
            get {
                PlaybookEntry<T> entry;
                if (this.TryGetValue(state, out entry)) {
                    return entry;
                }

                return null;
            }
            set {
                if (value == null) {
                    this.Remove(state);
                } else {
                    PlaybookEntry<T> entry = this[state];
                    if (state != null) {
                        this.States[entry.State] = entry;
                    }
                }
            }
        }
    }
}
