using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

using Solver;

namespace Othello {
    public class MtdFPlayer : Player<OthelloNode> {
        private readonly int depth;
        private readonly OthelloSearchParams searchParams;

        private Random random = new Random();

        public bool Verbose {
            get;
            set;
        }

        public bool SolveEndgame {
            get;
            set;
        }

        public bool MoveOrdering {
            get;
            set;
        }

        public bool Randomness {
            get;
            set;
        }

        public bool ExploringPatterns {
            get;
            set;
        }

        public bool ExploringPlaybook {
            get;
            set;
        }

        public MtdFPlayer(
            int depth,
            Func<OthelloNode, int> evaluator,
            bool verbose = false,
            bool moveOrdering = true,
            // TODO: replace with EndgameDepth
            bool solveEndgame = true,
            bool randomness = false,
            bool exploringPatterns = false,
            bool exploringPlaybook = false) {
            if (depth <= 0) {
                throw new ArgumentOutOfRangeException("must be positive");
            }

            if (evaluator == null) {
                throw new ArgumentNullException("evaluator");
            }

            this.depth = depth;
            this.searchParams = new OthelloSearchParams(evaluator);

            this.Verbose = verbose;
            this.MoveOrdering = moveOrdering;
            this.Randomness = randomness;
            this.SolveEndgame = solveEndgame;
            this.ExploringPatterns = exploringPatterns;
            this.ExploringPlaybook = exploringPlaybook;
        }

        private void Initialize() {
            this.searchParams.Initialize(this.depth);
        }

        private void Initialize(int nodeCacheSize) {
            this.searchParams.Initialize(nodeCacheSize);
        }

        private static void OrderMovesDescending(List<Tuple<int, int>> metadata) {
            // Reverse a and b to sort in descending order
            metadata.Sort((a, b) => b.Item2.CompareTo(a.Item2));
        }

        public int GetScore(OthelloNode node) {
            int bestScore;
            this.SelectNode(node.GetChildren(), out bestScore);
            return bestScore;
        }

        public int SelectNode(List<OthelloNode> nodes) {
            int bestScore;
            return this.SelectNode(nodes, out bestScore);
        }

        public int SelectNode(List<OthelloNode> nodes, out int bestScore) {
            if (nodes == null || nodes.Count == 0) {
                if (this.Verbose) {
                    Console.WriteLine("No legal moves. Passing.");
                    Console.WriteLine();
                }
                bestScore = int.MinValue;
                return -1;
            } else if (nodes.Count == 1) {
                if (this.Verbose) {
                    Console.WriteLine("One legal move. Skipping game tree search.");
                    Console.WriteLine();
                }
                bestScore = 0;
                return 0;
            }

            // Iterative deepening
            int best = 0;
            bestScore = int.MinValue;
            List<Tuple<int, int>> metadata = nodes.Select((node, index) => new Tuple<int, int>(index, 0)).ToList();
            for (int depth = 1; depth <= Math.Max(1, this.depth); depth++) {
                best = 0;
                bestScore = int.MinValue;
                this.Initialize();

                if (this.Verbose) {
                    Console.Write("Searching at depth {0}... ", depth);
                }

                // Move ordering by score
                if (this.MoveOrdering && depth > 1) {
                    OrderMovesDescending(metadata);
                }

                DateTime start = DateTime.Now;
                for (int i = 0; i < metadata.Count; i++) {
                    int index = metadata[i].Item1;
                    int score = metadata[i].Item2;

                    score = -SearchUtils.MtdF(this.searchParams, nodes[index], depth, metadata[i].Item2);
                    if (score > bestScore) {
                        best = index;
                        bestScore = score;
                    }

                    metadata[i] = new Tuple<int, int>(index, score);
                }
                TimeSpan elapsed = DateTime.Now - start;

                if (this.Verbose) {
                    Console.WriteLine("Score: {0}. Searched {1} nodes in {2:0.000} sec ({3:0.000} nodes/ms)",
                        bestScore,
                        this.searchParams.NodesEvaluated,
                        elapsed.TotalSeconds,
                        this.searchParams.NodesEvaluated / elapsed.TotalMilliseconds);
                }
            }

            // Solve endgame if we can
            int empties = nodes.Max(node => node.EmptySquareCount);
            if (this.SolveEndgame &&
                (empties <= SearchUtils.EndgameDepth || empties < this.depth + SearchUtils.EndgameDepthDiff))
            {
                if (this.Verbose) {
                    Console.Write("Solving endgame... ");
                }

                best = 0;
                bestScore = int.MinValue;
                this.Initialize((Math.Max(SearchUtils.EndgameDepth, this.depth + SearchUtils.EndgameDepthDiff) + 1) * 2);

                // Move ordering by score
                if (this.MoveOrdering) {
                    OrderMovesDescending(metadata);
                }

                DateTime start = DateTime.Now;
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
                }
            } else if (this.Randomness) {
                // Inject some randomness if we're not solving the endgame. Nodes are ordered by score and
                // have an exponentially decreasing probability of getting picked the worse they are ranked.
                // If we're attempting to explore less-researched nodes, the probability is skewed accordingly.
                OrderMovesDescending(metadata);

                // TODO: If every child is in the playbook, use it for evaluation.
                bool[] recorded = null;
                int recordedCount = 0;
                if (this.ExploringPlaybook) {
                    recorded = new bool[nodes.Count];
                    for (int i = 0; i < nodes.Count; i++) {
                        recorded[i] = OthelloNode.PlaybookContains(nodes[i]);
                        if (recorded[i]) {
                            recordedCount++;
                        }
                    }
                }

                for (int i = 0; i < metadata.Count; i++) {
                    best = metadata[i].Item1;
                    bestScore = metadata[i].Item2;

                    int probabilityReciprocal = 8;

                    if (this.ExploringPatterns) {
                        // Decrease likelihood of skipping this node if more of the features are unknown.
                        double known = OthelloNode.PatternClasses.Length - nodes[best].UnknownPatterns();
                        known *= known;
                        known /= OthelloNode.Features.Length * OthelloNode.Features.Length;
                        known = Math.Sqrt(known);

                        probabilityReciprocal += (int)(probabilityReciprocal * known);
                    }

                    if (this.ExploringPlaybook) {
                        double factor;
                        if (recorded[i]) {
                            // Increase likelihood of skipping this node if the board is in the playbook and
                            // few other child nodes are in the playbook.
                            factor = (recordedCount + nodes.Count) / (2.0 * nodes.Count);
                        } else {
                            // Decrease likelihood of skipping this node if the board is not in the playbook
                            // few other child nodes are not in the playbook.
                            factor = (2.0 * nodes.Count) / (nodes.Count * 2 - recordedCount);
                        }

                        probabilityReciprocal = (int)Math.Round(probabilityReciprocal * factor);
                    }
                    
                    if (this.random.Next(probabilityReciprocal) > 0) {
                        break;
                    }
                }

                if (this.Verbose) {
                    Console.WriteLine("Weighted random choice: score = {0}", bestScore);
                }
            }

            if (this.Verbose) {
                Console.WriteLine();
            }

            return best;
        }
    }
}
