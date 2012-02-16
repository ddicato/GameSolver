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

        private readonly List<List<OthelloNode>> nodeCache = new List<List<OthelloNode>>();

        public bool Verbose {
            get;
            set;
        }

        public BruteForcePlayer(int depth, Func<OthelloNode, int> evaluator, bool verbose = false) {
            if (depth <= 0) {
                throw new ArgumentOutOfRangeException("must be positive");
            }

            if (evaluator == null) {
                throw new ArgumentNullException("evaluator");
            }

            this.depth = depth;
            this.evaluator = evaluator;

            this.Verbose = verbose;
        }

        private void Initialize() {
            this.nodesEvaluated = 0;
            while (this.nodeCache.Count < this.depth) {
                this.nodeCache.Add(new List<OthelloNode>());
            }
        }

        private int Evaluate(OthelloNode node, int depth) {
            if (depth <= 0) {
                this.nodesEvaluated++;
                return this.evaluator(node);
            }

            var children = this.nodeCache[depth - 1];
            node.GetChildren(children);
            if (children.Count == 0) {
                this.nodesEvaluated++;
                return node.PieceCount() << 16;
            }

            if (depth == 1) {
                this.nodesEvaluated += children.Count;
                return -children.Min(child => this.evaluator(child));
            }

            return -children.Min(child => this.Evaluate(child, depth - 1));
        }

        public int SelectNode(List<OthelloNode> nodes) {
            if (nodes == null || nodes.Count == 0) {
                if (this.Verbose) {
                    Console.WriteLine("No legal moves. Passing.");
                    Console.WriteLine();
                }
                return -1;
            } else if (nodes.Count == 1) {
                if (this.Verbose) {
                    Console.WriteLine("One legal move. Skipping game tree search.");
                    Console.WriteLine();
                }
                return 0;
            }

            this.Initialize();

            int best = 0;
            int bestScore = int.MinValue;

            if (this.Verbose) {
                Console.Write("Searching at depth {0}... ", this.depth);
            }

            DateTime start = DateTime.Now;
            for (int i = 0; i < nodes.Count; i++) {
                int score = -this.Evaluate(nodes[i], this.depth);
                if (score > bestScore) {
                    best = i;
                    bestScore = score;
                }
            }
            TimeSpan elapsed = DateTime.Now - start;

            if (this.Verbose) {
                Console.WriteLine("Score: {0}. Searched {1} nodes in {2:0.000} sec ({3:0.000} nodes/ms)",
                    bestScore,
                    nodesEvaluated,
                    elapsed.TotalSeconds,
                    nodesEvaluated / elapsed.TotalMilliseconds);
                Console.WriteLine();
            }

            return best;
        }
    }
}
