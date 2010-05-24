using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;

namespace Solver {
    public class TranspositionTable<T> where T : HashableNode<T> {
        private readonly Dictionary<T, TableEntry> _dict;
        
        public TranspositionTable(IEqualityComparer<T> Comparator) {
            _dict = new Dictionary<T, TableEntry>(Comparator);
        }

        private struct TableEntry {
            private readonly int _data;

            public int Ply {
                get {
                    return (_data & 0x7fffffff) >> 16;
                }
            }

            public int MaxPly {
                get {
                    return _data & 0x0000ffff;
                }
            }

            public bool Pending {
                get {
                    return (_data & ~0x7fffffff) != 0;
                }
            }

            public TableEntry(int maxPly, int ply, bool pending) {
                Debug.Assert(maxPly > 0);
                Debug.Assert(ply > 0);
                int data = pending ? ~0x7fffffff : 0x00000000;
                data |= (ply << 16) | maxPly;
                _data = data;
            }
        }

        /// <summary>
        /// Inserts the specified node into the transposition table if its
        /// depth is lower than the current entry (if any). Returns true
        /// if the table was updated.
        /// </summary>
        public bool Insert(T node, int maxPly, int ply, bool pending) {
            if (pending) {
                // node is partially searched - override entries that exist lower
                // in the tree
                TableEntry entry;
                // TODO: if (!_dict.Try() && (entry.Pending && (>, ==)))
                if (_dict.TryGetValue(node, out entry)) {
                    // read-only case is lock-free
                    if (entry.Pending) {
                        if (entry.Ply > ply ||
                            entry.Ply == ply && entry.MaxPly < maxPly) {
                            lock (_dict) {
                                _dict[node] = new TableEntry(maxPly, ply, pending);
                                return true;
                            }
                        }
                        // TODO: remove
                        // semantically, the entry is replaced, but there's no
                        // need to do so explicitly because the contents are
                        // identical
                        //return entry.Key == ply;
                    }
                } else {
                    lock (_dict) {
                        _dict[node] = new TableEntry(maxPly, ply, pending);
                        return true;
                    }
                }
            } else {
                // node is proven unsolvable - override any pending entries
                TableEntry entry;
                // TODO: if (!_dict.Try() && (entry.Pending && (>, ==)))
                if (_dict.TryGetValue(node, out entry)) {
                    // read-only case is lock-free
                    if (entry.Ply > ply ||
                        entry.Ply == ply && ply == entry.MaxPly && entry.MaxPly < maxPly) {
                        lock (_dict) {
                            _dict[node] = new TableEntry(maxPly, ply, pending);
                            return true;
                        }
                    }
                    // TODO: remove
                    // semantically, the entry is replaced, but there's no
                    // need to do so explicitly because the contents are
                    // identical
                    //return entry.Key == ply;
                } else {
                    lock (_dict) {
                        _dict[node] = new TableEntry(maxPly, ply, pending);
                        return true;
                    }
                }
            }

            return false;
        }

        /// <summary>
        /// Removes all entries from the transposition table.
        /// </summary>
        public void Clear() {
            lock (_dict) {
                _dict.Clear();
            }
        }

        /// <summary>
        /// Gets the number of nodes currently in the table.
        /// </summary>
        public int Count {
            get {
                return _dict.Count;
            }
        }
    }
}
