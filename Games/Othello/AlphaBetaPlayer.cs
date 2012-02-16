using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

using Solver;

namespace Othello
{
    public class AlphaBetaPlayer : Player<OthelloNode>
    {
        private readonly int depth;
        private readonly Func<OthelloNode, int> evaluator;

        private int nodesEvaluated = 0;

        private readonly List<List<OthelloNode>> nodeCache = new List<List<OthelloNode>>();

        public bool Verbose
        {
            get;
            set;
        }

        public AlphaBetaPlayer(int depth, Func<OthelloNode, int> evaluator, bool verbose = false)
        {
            if (depth <= 0)
            {
                throw new ArgumentOutOfRangeException("must be positive");
            }

            if (evaluator == null)
            {
                throw new ArgumentNullException("evaluator");
            }

            this.depth = depth;
            this.evaluator = evaluator;

            this.Verbose = verbose;
        }

        private void Initialize()
        {
            this.nodesEvaluated = 0;
            while (this.nodeCache.Count < this.depth)
            {
                this.nodeCache.Add(new List<OthelloNode>());
            }
        }

        private int AlphaBeta(OthelloNode node, int depth, int alpha, int beta)
        {
            if (depth <= 0)
            {
                this.nodesEvaluated++;
                return this.evaluator(node);
            }

            var children = this.nodeCache[depth - 1];
            node.GetChildren(children);
            if (children.Count == 0)
            {
                this.nodesEvaluated++;
                return node.PieceCount() << 16;
            }

            foreach (OthelloNode child in children)
            {
                int score = -this.AlphaBeta(child, depth - 1, -beta, -alpha);
                if (score >= beta)
                {
                    return beta;
                }
                if (score > alpha)
                {
                    alpha = score;
                }
            }

            return alpha;
        }

        public int SelectNode(List<OthelloNode> nodes)
        {
            if (nodes == null || nodes.Count == 0)
            {
                return -1;
            }
            else if (nodes.Count == 1)
            {
                return 0;
            }

            this.Initialize();
            
            int best = 0;
            int bestScore = int.MinValue;
            List<Tuple<int, int>> metadata = nodes.Select((node, index) => new Tuple<int, int>(index, 0)).ToList();
            for (int depth = 1; depth <= Math.Max(1, this.depth); depth++)
            {
                best = 0;
                bestScore = int.MinValue;

                if (this.Verbose)
                {
                    Console.Write("Searching at depth {0}... ", depth);
                }

                // Move ordering by score
                if (depth > 1)
                {
                    metadata.Sort((a, b) => a.Item2.CompareTo(b.Item2));
                }

                // Iterative deepening
                DateTime start = DateTime.Now;
                for (int i = 0; i < metadata.Count; i++)
                {
                    int index = metadata[i].Item1;

                    // Using -int.MaxValue because negating int.MinValue doesn't work
                    int score = -this.AlphaBeta(nodes[index], depth, -int.MaxValue, int.MaxValue);
                    if (score > bestScore)
                    {
                        best = i;
                        bestScore = score;
                    }

                    metadata[i] = new Tuple<int, int>(index, score);
                }
                TimeSpan elapsed = DateTime.Now - start;

                if (this.Verbose)
                {
                    Console.WriteLine("Score: {0}. Searched {1} nodes in {2:0.000} sec ({3:0.000} nodes/ms)",
                        bestScore,
                        nodesEvaluated,
                        elapsed.TotalSeconds,
                        nodesEvaluated / elapsed.TotalMilliseconds);
                }
            }

            if (this.Verbose)
            {
                Console.WriteLine();
            }

            return best;
        }
    }
}
