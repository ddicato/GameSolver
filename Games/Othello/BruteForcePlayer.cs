using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

using Solver;

namespace Othello {
    public class BruteForcePlayer : Player<OthelloNode> {
        private readonly int depth;
        private readonly Func<OthelloNode, int> evaluator;

        private int nodesEvaluated = 0;

        public bool Verbose {
            get;
            set;
        }

        public BruteForcePlayer(int depth, Func<OthelloNode, int> evaluator) {
            if (depth < 0) {
                throw new ArgumentOutOfRangeException("depth cannot be negative");
            }

            if (evaluator == null) {
                throw new ArgumentNullException("evaluator");
            }

            this.depth = depth;
            this.evaluator = evaluator;
        }

        private int Evaluate(OthelloNode node, int depth) {
            if (depth <= 0) {
                this.nodesEvaluated++;
                return this.evaluator(node);
            }

            var children = node.GetChildren();
            if (children.Count == 0) {
                this.nodesEvaluated++;
                return this.evaluator(node);
            }

            if (depth == 1) {
                this.nodesEvaluated += children.Count;
                return children.Max(this.evaluator);
            }
            
            return children.Max(child => this.Evaluate(child, depth - 1));
        }

        public int SelectNode(IList<OthelloNode> nodes) {
            int best = -1;
            int bestScore = int.MinValue;
            for (int i = 0; i < nodes.Count; i++) {
                int score = this.Evaluate(nodes[i], this.depth);
                if (bestScore == int.MinValue || score > bestScore) {
                    best = i;
                    bestScore = score;
                }
            }

            return best;
        }
    }
}
