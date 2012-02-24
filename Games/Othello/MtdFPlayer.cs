using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

using Solver;

namespace Othello {
    public class MtdFPlayer : Player<OthelloNode> {
        private readonly int depth;
        private readonly Func<OthelloNode, int> evaluator;

        private int nodesEvaluated = 0;

        private readonly List<List<OthelloNode>> nodeCache = new List<List<OthelloNode>>();
        private readonly TranspositionTable2<OthelloNode> table = new TranspositionTable2<OthelloNode>();

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

        public bool Exploring {
            get;
            set;
        }

        public MtdFPlayer(
            int depth,
            Func<OthelloNode, int> evaluator,
            bool verbose = false,
            bool moveOrdering = true,
            bool solveEndgame = true,
            bool randomness = false,
            bool exploring = false) {
            if (depth <= 0) {
                throw new ArgumentOutOfRangeException("must be positive");
            }

            if (evaluator == null) {
                throw new ArgumentNullException("evaluator");
            }

            this.depth = depth;
            this.evaluator = evaluator;

            this.Verbose = verbose;
            this.MoveOrdering = moveOrdering;
            this.Randomness = randomness;
            this.SolveEndgame = solveEndgame;
            this.Exploring = exploring;
        }

        private void Initialize() {
            this.Initialize(this.depth);
        }

        private void Initialize(int nodeCacheSize) {
            this.nodesEvaluated = 0;
            this.table.Clear();
            while (this.nodeCache.Count < nodeCacheSize) {
                this.nodeCache.Add(new List<OthelloNode>());
            }
        }

        private int MtdF(OthelloNode node, int depth, int firstGuess) {
            int guess = firstGuess;
            int maxScore = int.MaxValue;
            int minScore = -int.MaxValue; // used instead of int.MinValue, which isn't negatable

            while (minScore < maxScore) {
                int beta = guess == minScore ? guess + 1 : guess;
                guess = AlphaBeta(node, depth, beta - 1, beta);
                if (guess < beta) {
                    maxScore = guess;
                } else {
                    minScore = guess;
                }
            }

            return guess;
        }

        private int MtdFEndgame(OthelloNode node, int firstGuess) {
            int guess = firstGuess;
            // TODO: use +/- 64 for bounds?
            int maxScore = int.MaxValue;
            int minScore = -int.MaxValue; // used instead of int.MinValue, which isn't negatable

            while (minScore < maxScore) {
                int beta = guess == minScore ? guess + 1 : guess;
                guess = AlphaBetaEndgame(node, 0, beta - 1, beta);
                if (guess < beta) {
                    maxScore = guess;
                } else {
                    minScore = guess;
                }
            }

            return guess;
        }

        private int AlphaBeta(OthelloNode node, int depth, int alpha, int beta) {
            int minScore, maxScore;
            if (this.table.TryGetValue(node, out minScore, out maxScore)) {
                if (minScore >= beta) {
                    return minScore;
                } else if (maxScore <= alpha) {
                    return maxScore;
                }
                alpha = Math.Max(alpha, minScore);
                beta = Math.Min(beta, maxScore);
            }

            int gamma;
            if (depth <= 0) {
                this.nodesEvaluated++;
                gamma = this.evaluator(node);
            } else {
                var children = this.nodeCache[depth - 1];
                node.GetChildren(children);
                if (children.Count == 0) {
                    this.nodesEvaluated++;
                    gamma = node.PieceCountSpread() << 16;
                } else {
                    gamma = -int.MaxValue; // used instead of int.MinValue, which isn't negatable
                    int a = alpha;
                    foreach (OthelloNode child in children) {
                        gamma = Math.Max(gamma, -this.AlphaBeta(child, depth - 1, -beta, -a));
                        if (gamma >= beta) {
                            break;
                        }
                        a = Math.Max(a, gamma);
                    }
                }
            }

            if (gamma <= alpha) {
                // Failing low gives us an upper bound
                this.table.SetValue(node, minScore, gamma);
            } else if (gamma >= beta) {
                // Failing high gives us a lower bound
                this.table.SetValue(node, gamma, maxScore);
            } else {
                // We have an accurate value (impossible if called with zero window)
                this.table.SetValue(node, gamma, gamma);
            }

            return gamma;
        }

        private int AlphaBetaEndgame(OthelloNode node, int currentDepth, int alpha, int beta) {
            int minScore, maxScore;
            if (this.table.TryGetValue(node, out minScore, out maxScore)) {
                if (minScore >= beta) {
                    return minScore;
                } else if (maxScore <= alpha) {
                    return maxScore;
                }
                alpha = Math.Max(alpha, minScore);
                beta = Math.Min(beta, maxScore);
            }

            int gamma;
            var children = this.nodeCache[currentDepth];
            node.GetChildren(children);
            if (children.Count == 0) {
                this.nodesEvaluated++;
                gamma = node.PieceCountSpread();
            } else {
                gamma = -int.MaxValue; // used instead of int.MinValue, which isn't negatable
                int a = alpha;
                foreach (OthelloNode child in children) {
                    gamma = Math.Max(gamma, -this.AlphaBetaEndgame(child, currentDepth + 1, -beta, -a));
                    if (gamma >= beta) {
                        break;
                    }
                    a = Math.Max(a, gamma);
                }
            }

            if (gamma <= alpha) {
                // Failing low gives us an upper bound
                this.table.SetValue(node, minScore, gamma);
            } else if (gamma >= beta) {
                // Failing high gives us a lower bound
                this.table.SetValue(node, gamma, maxScore);
            } else {
                // We have an accurate value (impossible if called with zero window)
                this.table.SetValue(node, gamma, gamma);
            }

            return gamma;
        }

        private static void OrderMovesDescending(List<Tuple<int, int>> metadata) {
            // Reverse a and b to sort in descending order
            metadata.Sort((a, b) => b.Item2.CompareTo(a.Item2));
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

            // Iterative deepening
            int best = 0;
            int bestScore = int.MinValue;
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

                    score = -this.MtdF(nodes[index], depth, metadata[i].Item2);
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
                        this.nodesEvaluated,
                        elapsed.TotalSeconds,
                        this.nodesEvaluated / elapsed.TotalMilliseconds);
                }
            }

            // Solve endgame if we can
            const int endgameDepthDiff = 6;
            if (this.SolveEndgame && OthelloNode.BitCount(~nodes[0].Occupied) < this.depth + endgameDepthDiff) {
                if (this.Verbose) {
                    Console.Write("Solving endgame... ");
                }

                best = 0;
                bestScore = int.MinValue;
                this.Initialize((this.depth + endgameDepthDiff + 1) * 2);

                DateTime start = DateTime.Now;
                foreach (Tuple<int, int> tuple in metadata) {
                    int index = tuple.Item1;

                    // TODO: use a smarter first guess than 0
                    int score = -this.AlphaBetaEndgame(nodes[index], 0, -int.MaxValue, int.MaxValue);
                    if (score > bestScore) {
                        best = index;
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
                }
            } else if (this.Randomness) {
                // TODO: add a way to explore nodes that don't have a lot of known feature or pattern values
                // Inject some randomness if we're not solving the endgame. Nodes are ordered by score and
                // have an exponentially decreasing probability of getting picked the worse they are.
                // If we're attempting to explore less-researched nodes, the probability is skewed accordingly.
                OrderMovesDescending(metadata);
                for (int i = 0; i < metadata.Count; i++) {
                    best = metadata[i].Item1;
                    bestScore = metadata[i].Item2;

                    int probabilityReciprocal = 8;
                    if (this.Exploring) {
                        // Decrease likelihood of skipping this node if more of the features are unknown.
                        double known = OthelloNode.Features.Length - nodes[best].UnknownFeatures();
                        known *= known;
                        known /= OthelloNode.Features.Length * OthelloNode.Features.Length;
                        known = Math.Sqrt(known);

                        probabilityReciprocal += (int)(probabilityReciprocal * known);
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
