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
            if (depth <= 0) {
                throw new ArgumentOutOfRangeException("must be positive");
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
                return node.PieceCount() << 16;
            }

            if (depth == 1) {
                this.nodesEvaluated += children.Count;
                return children.Max(this.evaluator);
            }

            return children.Max(child => this.Evaluate(child, depth - 1));
        }

        public int SelectNode(IList<OthelloNode> nodes) {
            if (nodes == null || nodes.Count == 0) {
                return -1;
            } else if (nodes.Count == 1) {
                return 0;
            }

            int best = 0;
            int bestScore = int.MaxValue;
            for (int i = 0; i < nodes.Count; i++) {
                int score = this.Evaluate(nodes[i], this.depth);
                if (bestScore == int.MaxValue || score < bestScore) {
                    best = i;
                    bestScore = score;
                }
            }

            return best;
        }
    }
}
