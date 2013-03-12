using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

using Solver;

namespace Othello {
    // TODO: Generalize this and move into GameSolver
    public static class SearchUtils {
        // TODO: Move these, as they are Othello-specific. The latter is only used in iterative deepening.
        public const int EndgameDepth = 11;
        public const int EndgameDepthDiff = 6;

        public static int MtdF<Node>(SearchParams<Node> searchParams, Node node, int depth, int firstGuess)
            where Node : TwoPlayerNode<Node> {
            int guess = firstGuess;
            int maxScore = int.MaxValue;
            int minScore = -int.MaxValue; // used instead of int.MinValue, which isn't negatable

            while (minScore < maxScore) {
                int beta = guess == minScore ? guess + 1 : guess;
                guess = AlphaBeta(searchParams, node, depth, beta - 1, beta);
                if (guess < beta) {
                    maxScore = guess;
                } else {
                    minScore = guess;
                }
            }

            return guess;
        }

        public static int MtdFEndgame<Node>(SearchParams<Node> searchParams, Node node, int firstGuess)
            where Node : TwoPlayerNode<Node> {
            int guess = firstGuess;
            int maxScore = int.MaxValue;
            int minScore = -int.MaxValue; // used instead of int.MinValue, which isn't negatable

            while (minScore < maxScore) {
                int beta = guess == minScore ? guess + 1 : guess;
                guess = AlphaBetaEndgame(searchParams, node, 0, beta - 1, beta);
                if (guess < beta) {
                    maxScore = guess;
                } else {
                    minScore = guess;
                }
            }

            return guess;
        }

        public static int AlphaBeta<Node>(SearchParams<Node> searchParams, Node node, int depth, int alpha, int beta)
            where Node : TwoPlayerNode<Node> {
            int minScore, maxScore;
            if (searchParams.Table.TryGetValue(node, out minScore, out maxScore)) {
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
                searchParams.NodesEvaluated++;
                gamma = searchParams.Evaluate(node);
            } else {
                var children = searchParams.NodeCache[depth - 1];
                node.GetChildren(children);
                if (children.Count == 0) {
                    searchParams.NodesEvaluated++;
                    gamma = searchParams.EvaluateEndgame(node);
                } else {
                    gamma = -int.MaxValue; // used instead of int.MinValue, which isn't negatable
                    int a = alpha;
                    foreach (Node child in children) {
                        gamma = Math.Max(gamma, -AlphaBeta(searchParams, child, depth - 1, -beta, -a));
                        if (gamma >= beta) {
                            break;
                        }
                        a = Math.Max(a, gamma);
                    }
                }
            }

            if (gamma <= alpha) {
                // Failing low gives us an upper bound
                searchParams.Table.SetValue(node, minScore, gamma);
            } else if (gamma >= beta) {
                // Failing high gives us a lower bound
                searchParams.Table.SetValue(node, gamma, maxScore);
            } else {
                // We have an accurate value (impossible if called with zero window)
                searchParams.Table.SetValue(node, gamma, gamma);
            }

            return gamma;
        }

        public static int AlphaBetaEndgame<Node>(SearchParams<Node> searchParams, Node node, int currentDepth, int alpha, int beta)
            where Node : TwoPlayerNode<Node> {
            int minScore, maxScore;
            if (searchParams.Table.TryGetValue(node, out minScore, out maxScore)) {
                if (minScore >= beta) {
                    return minScore;
                } else if (maxScore <= alpha) {
                    return maxScore;
                }
                alpha = Math.Max(alpha, minScore);
                beta = Math.Min(beta, maxScore);
            }

            int gamma;
            var children = searchParams.NodeCache[currentDepth];
            node.GetChildren(children);
            if (children.Count == 0) {
                searchParams.NodesEvaluated++;
                // TODO: this was PieceCountSpread(), but the default evaluator returns PieceCountSpread() << 16.
                gamma = searchParams.EvaluateEndgame(node);
            } else {
                gamma = -int.MaxValue; // used instead of int.MinValue, which isn't negatable
                int a = alpha;
                foreach (Node child in children) {
                    gamma = Math.Max(gamma, -AlphaBetaEndgame(searchParams, child, currentDepth + 1, -beta, -a));
                    if (gamma >= beta) {
                        break;
                    }
                    a = Math.Max(a, gamma);
                }
            }

            if (gamma <= alpha) {
                // Failing low gives us an upper bound
                searchParams.Table.SetValue(node, minScore, gamma);
            } else if (gamma >= beta) {
                // Failing high gives us a lower bound
                searchParams.Table.SetValue(node, gamma, maxScore);
            } else {
                // We have an accurate value (impossible if called with zero window)
                searchParams.Table.SetValue(node, gamma, gamma);
            }

            return gamma;
        }
    }
}
