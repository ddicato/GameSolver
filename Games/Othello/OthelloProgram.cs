using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

using Solver;

namespace Othello {
    class OthelloProgram {
        static void Main(string[] args) {
            const bool verbose = false;
            const int games = 1000;

            List<OthelloNode> temp = new List<OthelloNode>();
            Player<OthelloNode> p0 = new AlphaBetaPlayer(2, OthelloNode.Eval0, verbose: false, randomness: true);
            Player<OthelloNode> p1 = new AlphaBetaPlayer(2, OthelloNode.Eval1, verbose: false, randomness: true);

            PlayGames(p0, p1, games, verbose);

            Console.WriteLine("Press Enter to exit.");
            Console.ReadLine();
        }

        private static void PlayGames(Player<OthelloNode> p0, Player<OthelloNode> p1, int games = 2, bool verbose = false) {
            int totalScore = 0;
            int p0Wins = 0;
            int p1Wins = 0;
            for (int i = 0; i < games; i++) {
                if (i > 0) {
                    Console.WriteLine();
                }
                Console.WriteLine("Game {0} of {1}", i + 1, games);

                int result;
                if ((i & 1) == 0) {
                    result = GameLoop(p0, "Player 1", p1, "Player 2", verbose);
                } else {
                    result = -GameLoop(p1, "Player 2", p0, "Player 1", verbose);
                }

                totalScore += result;
                if (result > 0) {
                    p0Wins++;
                } else if (result < 0) {
                    p1Wins++;
                }

                Console.WriteLine("Player 1 wins: {0:00.00}% Player 2 wins: {1:00.00}% Average score: {2:+0.000;-0.000}.",
                    100.0 * p0Wins / (i + 1),
                    100.0 * p1Wins / (i + 1),
                    (double)totalScore / (i + 1));
            }

            Console.WriteLine(
                "Out of {0} games: {1} Player 1 win(s), {2} Player 2 win(s), and {3} draw(s).",
                games,
                p0Wins,
                p1Wins,
                games - p0Wins - p1Wins);
            Console.WriteLine();
        }

        private static int GameLoop(Player<OthelloNode> black, string blackName, Player<OthelloNode> white, string whiteName, bool verbose = false) {
            OthelloNode board = new OthelloNode();
            List<OthelloNode> children;
            while (!board.IsGameOver) {
                if (verbose || board.Turn == OthelloNode.BLACK && black is HumanPlayer ||
                    board.Turn == OthelloNode.WHITE && white is HumanPlayer) {
                    Console.WriteLine("Current board:");
                    Console.WriteLine(board);
                }
                children = board.GetChildren();

                if (children.Count == 0) {
                    Console.WriteLine("ERROR: No legal moves, but game is not over.");
                    return 0;
                }

                int index;
                if (board.Turn == OthelloNode.BLACK) {
                    index = black.SelectNode(children);
                } else {
                    index = white.SelectNode(children);
                }

                string player = board.Turn == OthelloNode.BLACK ? blackName : whiteName;
                string otherPlayer = board.Turn == OthelloNode.BLACK ? whiteName : blackName;
                if (index < 0 || index >= children.Count) {
                    Console.WriteLine("{0} made an illegal move.", player);
                    Console.WriteLine("{0} wins!", otherPlayer);
                    return board.Turn == OthelloNode.BLACK ? -64 : 64;
                }

                board = children[index];
            }

            if (verbose) {
                Console.WriteLine("Final Board:");
                Console.WriteLine(board);
            }
            board.PrintScore(blackName, whiteName);

            return board.Score;
        }

        #region Test Code

        private static void RunTests() {
            TestBitCount();
            PrintInitialBoard();
            PrintInitialChildren();
            PerftTest();
        }

        private static void PrintInitialBoard() {
            Console.WriteLine("Initial Board:");
            Console.WriteLine(new OthelloNode());
        }

        private static void PrintInitialChildren() {
            List<OthelloNode> children = new OthelloNode().GetChildren();

            Console.WriteLine("Initial board has {0} children:", children.Count);
            Console.WriteLine(OthelloNode.PrintNodes(4, true, children.ToArray()));
        }

        // The Perft method for testing move generation functions: evaluate the entire game
        // tree to a certain depth, count the leaf nodes, and compare against known values.
        private static long[] PerftLeaves = new long[] {
            4,
            12,
            56,
            244,
            1396,
            8200,
            55092,
            390216,
            3005288,
            24571284,
            212258572,
            1939886052,
            18429634780,
            184042061172
        };

        // At depth 11, we start to see game-over situations 
        private static int[] PerftEarlyTerminations = new int[] {
            0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
            228, 584, 6938, 23340
        };

        private static long Perft(OthelloNode node, uint depth, ref int earlyTerminations) {
            if (depth == 0) {
                return 1;
            }

            List<OthelloNode> children = node.GetChildren();
            if (children.Count == 0) {
                earlyTerminations++;
                return 0;
            }

            if (depth == 1) {
                return children.Count;
            }

            long total = 0L;
            foreach (OthelloNode child in children) {
                total += Perft(child, depth - 1, ref earlyTerminations);
            }

            return total;
        }

        private static void PerftTest() {
            for (uint depth = 1; depth < PerftLeaves.Length; depth++) {
                Console.WriteLine("Perft test at depth {0}", depth);

                int earlyTerminations = 0;
                DateTime start = DateTime.Now;
                long leaves = Perft(new OthelloNode(), depth, ref earlyTerminations);
                TimeSpan elapsed = DateTime.Now - start;

                bool failed = false;
                if (PerftLeaves[depth - 1] != leaves) {
                    Console.WriteLine("\t{0} leaves should be {1}", leaves, PerftLeaves[depth - 1]);
                    failed = true;
                }
                if (PerftEarlyTerminations[depth - 1] != earlyTerminations) {
                    Console.WriteLine("\t{0} early terminations should be {1}", earlyTerminations, PerftEarlyTerminations[depth - 1]);
                    failed = true;
                }

                Console.WriteLine("...done in {0:0.000} sec ({1:0.00} leaves/ms)",
                    elapsed.TotalSeconds,
                    leaves / elapsed.TotalMilliseconds);
                Console.WriteLine();

                if (failed) {
                    Console.WriteLine("Test failed at depth {0}. Stopping test.", depth);
                    Console.WriteLine();
                    break;
                }

                const double timeLimit = 1.0; // in minutes
                if (elapsed.TotalMinutes > timeLimit) {
                    Console.WriteLine("Depth {0} took longer than {1} minute(s). Stopping test.", depth, timeLimit);
                    Console.WriteLine();
                    break;
                }
            }
        }

        private static int NaiveBitCount(ulong value) {
            int count = 0;
            while (value != 0) {
                while ((value & 1) == 0) {
                    value >>= 1;
                }
                value >>= 1;
                count++;
            }

            return count;
        }

        private static void TestBitCount() {
            const int iters = 1000000;
            Console.WriteLine("Testing bitCount on {0} values", iters);

            Random random = new Random();
            byte[] data = new byte[8];
            for (int i = 0; i < iters; i++) {
                random.NextBytes(data);
                ulong value = data[0] |
                    ((ulong)data[1] << 8) |
                    ((ulong)data[2] << 16) |
                    ((ulong)data[3] << 24) |
                    ((ulong)data[4] << 32) |
                    ((ulong)data[5] << 40) |
                    ((ulong)data[6] << 48) |
                    ((ulong)data[7] << 56);

                if (OthelloNode.BitCount(value) != NaiveBitCount(value)) {
                    Console.WriteLine("\tMismatch in {0}: {1} should be {2}",
                        value,
                        OthelloNode.BitCount(value),
                        NaiveBitCount(value));
                }
            }

            Console.WriteLine("...done");
            Console.WriteLine();
        }

        #endregion
    }
}
