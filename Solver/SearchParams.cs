using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Solver {
    public abstract class SearchParams<Node> where Node : TwoPlayerNode<Node> {
        protected SearchParams() {
            this.NodeCache = new List<List<Node>>();
            this.Table = new TranspositionTable2<Node>();
            this.Initialize(12);
        }

        /// <summary>
        /// A cache of child node lists to reuse during deep searches, avoiding repeated allocations.
        /// </summary>
        public List<List<Node>> NodeCache { get; private set; }

        public TranspositionTable2<Node> Table { get; private set; }

        public int NodesEvaluated { get; set; }

        public virtual void Initialize(int nodeCacheSize, bool persistTable = false) {
            if (nodeCacheSize < 0) {
                throw new ArgumentOutOfRangeException();
            }

            this.NodesEvaluated = 0;

            if (!persistTable) {
                this.NodeCache.Clear();
                this.Table.Clear();
            }

            while (this.NodeCache.Count < nodeCacheSize) {
                this.NodeCache.Add(new List<Node>());
            }
        }

        public abstract int Evaluate(Node node);
        
        public abstract int EvaluateEndgame(Node node);
    }
}
