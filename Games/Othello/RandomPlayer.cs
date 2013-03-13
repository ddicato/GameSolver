using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

using Solver;

namespace Othello {
    public class RandomPlayer : Player<OthelloNode> {
        private static readonly Random Random = new Random();

        private readonly OthelloSearchParams searchParams = new OthelloSearchParams(
            node => node.PieceCountSpread());

        public bool Verbose { get; set; }

        public int SelectNode(List<OthelloNode> nodes) {
            if (nodes == null || nodes.Count == 0) {
                return -1;
            }

            int empties = nodes.Max(node => node.EmptySquareCount);
            if (empties <= SearchUtils.EndgameDepth) {
                if (this.Verbose) {
                    Console.Write("Solving endgame... ");
                }

                int best = 0;
                int bestScore = int.MinValue;
                this.searchParams.Initialize(this.searchParams.NodeCache.Count);

                DateTime start = DateTime.Now;

                // TODO: This search may be slower because there is no move ordering.
                List<Tuple<int, int>> metadata = nodes.Select((node, index) => new Tuple<int, int>(index, 0)).ToList();
                foreach (Tuple<int, int> tuple in metadata) {
                    int index = tuple.Item1;

                    // TODO: use a smarter first guess than 0
                    int score = -SearchUtils.MtdFEndgame(this.searchParams, nodes[index], 0);
                    if (score > bestScore) {
                        best = index;
                        bestScore = score;
                    }
                }
                TimeSpan elapsed = DateTime.Now - start;

                if (this.Verbose) {
                    Console.WriteLine("Score: {0}. Searched {1} nodes in {2:0.000} sec ({3:0.000} nodes/ms)",
                        bestScore,
                        this.searchParams.NodesEvaluated,
                        elapsed.TotalSeconds,
                        this.searchParams.NodesEvaluated / elapsed.TotalMilliseconds);
                    Console.WriteLine();
                }

                return best;
            }

            return Random.Next(nodes.Count);
        }
    }
}
