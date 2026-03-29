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

        public static int MtdF(OthelloSearchParams searchParams, OthelloNode node, int depth, int firstGuess) {
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

        public static int MtdFEndgame(OthelloSearchParams searchParams, OthelloNode node, int firstGuess) {
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

        public static int AlphaBeta(OthelloSearchParams searchParams, OthelloNode node, int depth, int alpha, int beta) {
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
                while (searchParams.NodeCache.Count < depth) {
                    searchParams.NodeCache.Add(new List<OthelloNode>());
                }
                var children = searchParams.NodeCache[depth - 1];
                node.GetChildren(children);
                if (children.Count == 0) {
                    searchParams.NodesEvaluated++;
                    gamma = searchParams.EvaluateEndgame(node);
                } else {
                    gamma = -int.MaxValue; // used instead of int.MinValue, which isn't negatable
                    int a = alpha;
                    int cutoffIndex = -1;

                    // Find which children (if any) match the killer moves for this depth
                    int killerChildIndex0 = -1, killerChildIndex1 = -1;
                    ulong killer0 = searchParams.KillerMoves[depth, 0];
                    ulong killer1 = searchParams.KillerMoves[depth, 1];
                    if (killer0 != 0 || killer1 != 0) {
                        ulong nodeOccupied = node.Occupied;
                        for (int i = 0; i < children.Count; i++) {
                            ulong moveSquare = children[i].Occupied ^ nodeOccupied;
                            if (moveSquare == killer0 && killer0 != 0) killerChildIndex0 = i;
                            else if (moveSquare == killer1 && killer1 != 0) killerChildIndex1 = i;
                        }
                    }

                    // Search killer moves first
                    if (killerChildIndex0 >= 0) {
                        int score = -AlphaBeta(searchParams, children[killerChildIndex0], depth - 1, -beta, -a);
                        if (score > gamma) { gamma = score; cutoffIndex = killerChildIndex0; }
                        a = Math.Max(a, gamma);
                    }
                    if (gamma < beta && killerChildIndex1 >= 0) {
                        int score = -AlphaBeta(searchParams, children[killerChildIndex1], depth - 1, -beta, -a);
                        if (score > gamma) { gamma = score; cutoffIndex = killerChildIndex1; }
                        a = Math.Max(a, gamma);
                    }

                    // Search remaining children
                    if (gamma < beta) {
                        for (int i = 0; i < children.Count; i++) {
                            if (i == killerChildIndex0 || i == killerChildIndex1) continue;
                            int score = -AlphaBeta(searchParams, children[i], depth - 1, -beta, -a);
                            if (score > gamma) { gamma = score; cutoffIndex = i; }
                            if (gamma >= beta) break;
                            a = Math.Max(a, gamma);
                        }
                    }

                    // Record the cutoff move as a killer for this depth
                    if (gamma >= beta && cutoffIndex >= 0) {
                        ulong cutoffSquare = children[cutoffIndex].Occupied ^ node.Occupied;
                        if (cutoffSquare != 0 && cutoffSquare != searchParams.KillerMoves[depth, 0]) {
                            searchParams.KillerMoves[depth, 1] = searchParams.KillerMoves[depth, 0];
                            searchParams.KillerMoves[depth, 0] = cutoffSquare;
                        }
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

        public static int AlphaBetaEndgame(OthelloSearchParams searchParams, OthelloNode node, int currentDepth, int alpha, int beta) {
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

            while (searchParams.NodeCache.Count <= currentDepth) {
                searchParams.NodeCache.Add(new List<OthelloNode>());
            }
            var children = searchParams.NodeCache[currentDepth];

            int gamma;
            node.GetChildren(children);
            if (children.Count == 0) {
                searchParams.NodesEvaluated++;
                // TODO: this was PieceCountSpread(), but the default evaluator returns PieceCountSpread() << 16.
                gamma = searchParams.EvaluateEndgame(node);
            } else {
                gamma = -int.MaxValue; // used instead of int.MinValue, which isn't negatable
                int a = alpha;
                int cutoffIndex = -1;

                // Find which children (if any) match the killer moves for this depth
                int killerChildIndex0 = -1, killerChildIndex1 = -1;
                int killerDepth = currentDepth;
                if (killerDepth < searchParams.KillerMoves.GetLength(0)) {
                    ulong killer0 = searchParams.KillerMoves[killerDepth, 0];
                    ulong killer1 = searchParams.KillerMoves[killerDepth, 1];
                    if (killer0 != 0 || killer1 != 0) {
                        ulong nodeOccupied = node.Occupied;
                        for (int i = 0; i < children.Count; i++) {
                            ulong moveSquare = children[i].Occupied ^ nodeOccupied;
                            if (moveSquare == killer0 && killer0 != 0) killerChildIndex0 = i;
                            else if (moveSquare == killer1 && killer1 != 0) killerChildIndex1 = i;
                        }
                    }
                }

                // Search killer moves first
                if (killerChildIndex0 >= 0) {
                    int score = -AlphaBetaEndgame(searchParams, children[killerChildIndex0], currentDepth + 1, -beta, -a);
                    if (score > gamma) { gamma = score; cutoffIndex = killerChildIndex0; }
                    a = Math.Max(a, gamma);
                }
                if (gamma < beta && killerChildIndex1 >= 0) {
                    int score = -AlphaBetaEndgame(searchParams, children[killerChildIndex1], currentDepth + 1, -beta, -a);
                    if (score > gamma) { gamma = score; cutoffIndex = killerChildIndex1; }
                    a = Math.Max(a, gamma);
                }

                // Search remaining children
                if (gamma < beta) {
                    for (int i = 0; i < children.Count; i++) {
                        if (i == killerChildIndex0 || i == killerChildIndex1) continue;
                        int score = -AlphaBetaEndgame(searchParams, children[i], currentDepth + 1, -beta, -a);
                        if (score > gamma) { gamma = score; cutoffIndex = i; }
                        if (gamma >= beta) break;
                        a = Math.Max(a, gamma);
                    }
                }

                // Record the cutoff move as a killer for this depth
                if (gamma >= beta && cutoffIndex >= 0 && killerDepth < searchParams.KillerMoves.GetLength(0)) {
                    ulong cutoffSquare = children[cutoffIndex].Occupied ^ node.Occupied;
                    if (cutoffSquare != 0 && cutoffSquare != searchParams.KillerMoves[killerDepth, 0]) {
                        searchParams.KillerMoves[killerDepth, 1] = searchParams.KillerMoves[killerDepth, 0];
                        searchParams.KillerMoves[killerDepth, 0] = cutoffSquare;
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
    }
}
