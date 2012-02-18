using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

using Solver;

namespace Othello {
    class OthelloProgram {
        static void _Main(string[] args) {
            RunTests();

            Console.WriteLine("Press Enter to exit.");
            Console.ReadLine();
        }

        // TODO: verbosity levels: Board, Turn, Game, GameSet, Output, None
        static void Main(string[] args) {
            const bool verbose = false;
            const int randomTrainingGames = 500;
            const int selfTrainingGames = 500;
            const int games = 500;

            Player<OthelloNode> p0 = new AlphaBetaPlayer(1, node => node.PatternScore(), verbose: verbose, randomness: false, exploring: false);
            Player<OthelloNode> p1 = new RandomPlayer();

            PlayGames(p0, p1, randomTrainingGames, verbose, training: true, outputLog: "0_RandomPhase.txt");

            p0 = new AlphaBetaPlayer(1, node => node.PatternScore(), verbose: verbose, randomness: true, exploring: false);
            p1 = new AlphaBetaPlayer(2, node => node.PatternScore(), verbose: verbose, randomness: true, exploring: false);

            PlayGames(p0, p1, selfTrainingGames, verbose, training: true, outputLog: "1_SelfPhase.txt");

            p0 = new AlphaBetaPlayer(1, node => node.PatternScore(), verbose: false, randomness: true, exploring: false);
            p1 = new AlphaBetaPlayer(1, OthelloNode.Eval1, verbose: false, randomness: true, exploring: false);
            
            PlayGames(p0, p1, games, verbose, training: true, outputLog: "2_AdversarialPhase.txt");

            //StreamWriter writer = new StreamWriter("params.txt", false);
            //OthelloNode.WriteHeuristics(writer);
            //writer.Close();

            Console.WriteLine("Press Enter to exit.");
            Console.ReadLine();
        }

        // TODO: Parallelize this. We're wasting cycles.
        private static void PlayGames(
            Player<OthelloNode> p0,
            Player<OthelloNode> p1,
            int games = 2,
            bool verbose = false,
            bool training = true,
            string outputLog = null) {
            const double emaFactor = 0.99;

            int totalScore = 0;
            int p0Wins = 0;
            int p1Wins = 0;
            double p0WinsEma = 0.0;
            double p1WinsEma = 0.0;
            double scoreEma = 0.0;
            StreamWriter writer = null;
            for (int i = 0; i < games; i++) {
                if (i > 0) {
                    Console.WriteLine();
                }
                Console.WriteLine("Game {0} of {1}", i + 1, games);

                int result;
                if ((i & 1) == 0) {
                    result = GameLoop(p0, "Player 1", p1, "Player 2", verbose, training);
                    if (training) {
                        OthelloNode.Train(result);
                    }
                } else {
                    result = -GameLoop(p1, "Player 2", p0, "Player 1", verbose, training);
                    if (training) {
                        OthelloNode.Train(-result);
                    }
                }

                totalScore += result;
                scoreEma = scoreEma * emaFactor + result * (1.0 - emaFactor);
                if (result > 0) {
                    p0Wins++;
                    if (i == 0) {
                        p0WinsEma = 1.0;
                    } else {
                        p0WinsEma = p0WinsEma * emaFactor + (1.0 - emaFactor);
                        p1WinsEma *= emaFactor;
                    }
                } else if (result < 0) {
                    p1Wins++;
                    if (i == 0) {
                        p1WinsEma = 1.0;
                    } else {
                        p1WinsEma = p1WinsEma * emaFactor + (1.0 - emaFactor);
                        p0WinsEma *= emaFactor;
                    }
                } else {
                    if (i > 0) {
                        p0WinsEma *= emaFactor;
                        p1WinsEma *= emaFactor;
                    }
                }

                Console.WriteLine(
                    "Player 1 wins: {0:00.00}% Player 2 wins: {1:00.00}% Average score: {2:+0.000;-0.000}",
                    100.0 * p0Wins / (i + 1),
                    100.0 * p1Wins / (i + 1),
                    (double)totalScore / (i + 1));
                Console.WriteLine(
                    "Exponential moving averages: {0:00.00}% to {1:00.00}% with score {2:+0.000;-0.000}",
                    100.0 * p0WinsEma,
                    100.0 * p1WinsEma,
                    scoreEma);

                if (i == 0 && outputLog != null) {
                    try {
                        writer = new StreamWriter(outputLog, false);
                        writer.WriteLine("P0WinsAvg P0WinsEma P1WinsAvg P1WinsEma ScoreAvg ScoreEma");
                    } catch { }
                }
                if (writer != null) {
                    writer.WriteLine(
                        "{0:0.0000} {1:0.0000} {2:0.0000} {3:0.0000} {4:+00.00;-00.00} {5:+00.00;-00.00}",
                        (double)p0Wins / (i + 1),
                        p0WinsEma,
                        (double)p1Wins / (i + 1),
                        p1WinsEma,
                        (double)totalScore / (i + 1),
                        scoreEma);
                }
            }

            if (writer != null) {
                writer.Close();
            }

            Console.WriteLine(
                "Out of {0} games: {1} Player 1 win(s), {2} Player 2 win(s), and {3} draw(s).",
                games,
                p0Wins,
                p1Wins,
                games - p0Wins - p1Wins);
            Console.WriteLine();
        }

        private static int GameLoop(
            Player<OthelloNode> black,
            string blackName,
            Player<OthelloNode> white,
            string whiteName,
            bool verbose = false,
            bool training = true) {
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
                if (training) {
                    OthelloNode.AddIntermediateState(board);
                }
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
            PrintPatterns();
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

        private static void PrintPatterns() {
            PrintPattern("Row", OthelloNode.Row);
            PrintPattern("Column", OthelloNode.Column);
            PrintPattern("HorizEdge", OthelloNode.HorizEdge);
            PrintPattern("VertEdge", OthelloNode.VertEdge);
            PrintPattern("DownLeft", OthelloNode.DownLeft);
            PrintPattern("DownRight", OthelloNode.DownRight);
            PrintPattern("Corner33", OthelloNode.Corner33);
            PrintPattern("Corner52Cw", OthelloNode.Corner52Cw);
            PrintPattern("Corner52Ccw", OthelloNode.Corner52Ccw);
        }

        private static void PrintPattern(string name, ulong[] patternSet) {
            Console.WriteLine("{0} Patterns:", name);
            foreach (ulong pattern in patternSet) {
                Console.WriteLine(OthelloNode.PrintUlong(pattern));
            }
        }

        #endregion
    }
}
