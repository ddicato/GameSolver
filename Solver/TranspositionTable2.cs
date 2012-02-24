using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Solver {
    public class TranspositionTable2<T> {
        private readonly Dictionary<T, TableEntry> dict = new Dictionary<T, TableEntry>();

        private struct TableEntry {
            public int MinScore;
            public int MaxScore;
        }

        public bool TryGetValue(T node, out int minScore, out int maxScore) {
            TableEntry entry;
            if (!this.dict.TryGetValue(node, out entry)) {
                minScore = -int.MaxValue; // used because int.MinValue isn't negatable
                maxScore = int.MaxValue;
                return false;
            }

            minScore = entry.MinScore;
            maxScore = entry.MaxScore;
            return true;
        }

        public void SetValue(T node, int minScore, int maxScore) {
            this.dict[node] = new TableEntry() {
                MinScore = minScore,
                MaxScore = maxScore
            };
        }

        public void Clear() {
            this.dict.Clear();
        }

        public int Count {
            get {
                return this.dict.Count;
            }
        }
    }
}
