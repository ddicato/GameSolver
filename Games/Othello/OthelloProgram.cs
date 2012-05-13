using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

using Solver;

namespace Othello {
    class OthelloProgram {
        const string ParamsPath = "params.txt";
        const string PlaybookPath = "playbook.txt";

        static void _Main(string[] args) {
            RunTests();

            Console.WriteLine("Press Enter to exit.");
            Console.ReadLine();
        }

        // TODO: verbosity levels: Board, Turn, Game, GameSet, Output, None
        static void Main(string[] args) {
            const bool verbose = false;
            const TrainingMode randomTraining = TrainingMode.All;
            const TrainingMode selfTraining = TrainingMode.All;
            const TrainingMode adversarialTraining = TrainingMode.All; // TODO: evaluate relative strength and adjust training mode accordingly
            const int randomGames = 250;
            const int selfGames = 250;
            const int adversarialGames = 500;
            const int depth = 3;

            Player<OthelloNode> p0;
            Player<OthelloNode> p1;

            // TODO: re-enable calling CalculatePatternScore() if this is changed from
            //       PatternScoreSlow() to PatternScore()
            // TODO: also, optimize CalculateWeights() and especially CalculatePatternScores()
            Func<OthelloNode, int> patternEval = node => node.PatternScoreSlow();

            int randomGamesPlayed = 0;
            int selfGamesPlayed = 0;
            int adversarialGamesPlayed = 0;

            OthelloNode.ReadPlaybook(PlaybookPath);
            OthelloNode.ReadHeuristics(ParamsPath);
            OthelloNode.PrintPlaybookStats();
            
            do {
                p0 = new MtdFPlayer(1, patternEval, verbose: verbose, randomness: false);
                p1 = new RandomPlayer();
                PlayGames(p0, p1, randomGames, ref randomGamesPlayed, verbose, training: randomTraining, p1Name: "RandomPlayer");
            } while (randomGames > 0 && selfGames <= 0 && adversarialGames <= 0);

            while (true) {
                p0 = new MtdFPlayer(depth, patternEval, verbose: verbose, randomness: true, exploring: true);
                p1 = new MtdFPlayer(depth, patternEval, verbose: verbose, randomness: true, exploring: true);
                PlayGames(p0, p1, selfGames, ref selfGamesPlayed, verbose, training: selfTraining, p1Name: "self");

                p0 = new MtdFPlayer(depth, patternEval, verbose: false, randomness: true, exploring: true);
                p1 = new MtdFPlayer(depth + 1, OthelloNode.Eval1, verbose: false, randomness: true, exploring: true);
                PlayGames(p0, p1, adversarialGames, ref adversarialGamesPlayed, verbose, training: adversarialTraining, p1Name: "Adversary+");
            }

            Console.WriteLine("Press Enter to exit.");
            Console.ReadLine();
        }

        // TODO: Parallelize this. We're wasting cycles. Or, remove the threads param.
        private static void PlayGames(
            Player<OthelloNode> p0,
            Player<OthelloNode> p1,
            int games,
            ref int gamesPlayed,
            bool verbose = false,
            TrainingMode training = TrainingMode.All, // evaluated from p0's perspective
            string p0Name = null,
            string p1Name = null,
            string outputLog = null,
            int threads = 0) {
            const double emaFactor = 0.96;

            int totalScore = 0;
            int p0Wins = 0;
            int p1Wins = 0;
            double p0WinsEma = 0.5;
            double p1WinsEma = 0.5;
            double scoreEma = 0.0;

            p0Name = p0Name ?? "Player 1";
            p1Name = p1Name ?? "Player 2";
            if (threads < 1) {
                threads = Environment.ProcessorCount;
            }

            StreamWriter writer = null;
            if (outputLog != null) {
                try {
                    writer = new StreamWriter(outputLog, false);
                    writer.WriteLine("P0WinsAvg P0WinsEma P1WinsAvg P1WinsEma ScoreAvg ScoreEma");
                } catch { }
            }

            for (int i = 0; i < games; i++) {
                if (i > 0) {
                    Console.WriteLine();
                }
                Console.WriteLine("Game {0} of {1}", i + 1, games);

                int playbookCount = OthelloNode.PlaybookCount;

                int result;
                List<OthelloNode> gameHistory = new List<OthelloNode>();
                if ((i & 1) == 0) {
                    result = GameLoop(p0, p0Name, p1, p1Name, gameHistory, verbose, training);
                    if (result > 0 && (training & TrainingMode.Win) != TrainingMode.None ||
                        result < 0 && (training & TrainingMode.Loss) != TrainingMode.None ||
                        result == 0 && (training & TrainingMode.Draw) != TrainingMode.None) {
                        OthelloNode.TrainPlaybook(gameHistory);
                    }
                } else {
                    result = GameLoop(p1, p1Name, p0, p0Name, gameHistory, verbose, training);
                    if (result > 0 && (training & TrainingMode.Loss) != TrainingMode.None ||
                        result < 0 && (training & TrainingMode.Win) != TrainingMode.None ||
                        result == 0 && (training & TrainingMode.Draw) != TrainingMode.None) {
                        OthelloNode.TrainPlaybook(gameHistory);
                    }

                    // We want to display the result from p0's point of view.
                    result = -result;
                }

                totalScore += result;
                scoreEma = scoreEma * emaFactor + result * (1.0 - emaFactor);
                if (result > 0) {
                    p0Wins++;
                    p0WinsEma = p0WinsEma * emaFactor + (1.0 - emaFactor);
                    p1WinsEma *= emaFactor;
                } else if (result < 0) {
                    p1Wins++;
                    p1WinsEma = p1WinsEma * emaFactor + (1.0 - emaFactor);
                    p0WinsEma *= emaFactor;
                } else {
                    p0WinsEma *= emaFactor;
                    p1WinsEma *= emaFactor;
                }

                Console.WriteLine(
                    "{3} wins: {0:00.00}% {4} wins: {1:00.00}% Average score: {2:+0.000;-0.000}",
                    100.0 * p0Wins / (i + 1),
                    100.0 * p1Wins / (i + 1),
                    (double)totalScore / (i + 1),
                    p0Name,
                    p1Name);
                Console.WriteLine(
                    "Exponential moving averages: {0:00.00}% to {1:00.00}% with score {2:+0.000;-0.000}",
                    100.0 * p0WinsEma,
                    100.0 * p1WinsEma,
                    scoreEma);
                Console.WriteLine(
                    "{0} new entries added to the game database.",
                    OthelloNode.PlaybookCount - playbookCount);

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

            Console.WriteLine();
            Console.WriteLine(
                "Out of {0} games: {1} {4} win(s), {2} {5} win(s), and {3} draw(s).",
                games,
                p0Wins,
                p1Wins,
                games - p0Wins - p1Wins,
                p0Name,
                p1Name);

            gamesPlayed += games;

            if (training != TrainingMode.None && games > 0) {
                OthelloNode.WritePlaybook(PlaybookPath);
                OthelloNode.CalculateHeuristics();
                OthelloNode.CalculateWeights();
                OthelloNode.WriteHeuristics(ParamsPath);

                Console.WriteLine();
            }

            Console.WriteLine("** Played {0} games against {1} **", gamesPlayed, p1Name);
            Console.WriteLine();
        }

        private static int GameLoop(
            Player<OthelloNode> black,
            string blackName,
            Player<OthelloNode> white,
            string whiteName,
            List<OthelloNode> gameHistory,
            bool verbose = false,
            TrainingMode training = TrainingMode.All) {
            OthelloNode board = new OthelloNode();
            List<OthelloNode> children;
            
            if (gameHistory == null) {
                training = TrainingMode.None;
            } else {
                gameHistory.Clear();
                gameHistory.Add(board);
            }

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
                if (training != TrainingMode.None) {
                    gameHistory.Add(board);
                }
            }

            if (verbose) {
                Console.WriteLine("Final Board:");
                Console.WriteLine(board);
            }
            board.PrintScore(blackName, whiteName);

            return board.Score;
        }

        [Flags]
        private enum TrainingMode {
            None = 0,
            Loss = 1,
            Draw = 2,
            Win = 4,

            WinLoss = Win | Loss,
            WinDraw = Win | Draw,
            LossDraw = Loss | Draw,

            All = Win | Loss | Draw
        }

        #region Test Code

        // TODO: A test that takes some pseudorandom boards and makes sure the evaluation function is
        //       identical for all board symmetries.

        private static void RunTests() {
            TestBitCount();
            PrintSymmetries();
            PrintPatterns();
            PrintInitialBoard();
            PrintInitialChildren();
            PrintRandomGame(); // run before loading params.txt
            PrintWeights(); // loads params.txt
            PrintRandomGame(); // run after loading params.txt
            TestPlaybook();
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

        private static void PrintPatterns() { // TODO: create PatternNames?
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

        private static void PrintSymmetries() {
            Random random = new Random(12345);
            OthelloNode node = new OthelloNode();
            for (int i = 0; i < 10; i++) {
                List<OthelloNode> children = node.GetChildren();
                node = children[random.Next(children.Count)];
            }

            Console.WriteLine("Displaying board symmetries:");
            foreach (OthelloNode permutation in OthelloNode.GetSymmetries(node)) {
                Console.WriteLine(permutation);
            }
        }

        private static void PrintWeights() {
            OthelloNode.ReadHeuristics(ParamsPath);
            OthelloNode.PrintWeights();
        }

        // TODO: generalize and factor elsewhere?
        private static List<OthelloNode> GenerateRandomGame(int? seed = null) {
            Random random;
            if (seed == null || !seed.HasValue) {
                random = new Random();
            } else {
                random = new Random(seed.Value);
            }

            List<OthelloNode> history = new List<OthelloNode>();
            OthelloNode node = new OthelloNode();
            while (!node.IsGameOver) {
                history.Add(node);
                List<OthelloNode> children = node.GetChildren();
                if (children == null || children.Count == 0) {
                    Console.WriteLine("Error: node.IsGameOver is false but GetChildren returns no children.");
                    Console.WriteLine(node);
                }

                node = children[random.Next(children.Count)];
            }

            history.Add(node);
            return history;
        }

        private static void PrintRandomGame() {
            const int groupSize = 5;
            List<OthelloNode> history = GenerateRandomGame(12345);

            Console.WriteLine("Random game history:");
            Console.WriteLine(OthelloNode.PrintNodes(groupSize, true, history.ToArray()));

            Console.WriteLine("Pattern scores:");
            PrintScoreHistory(groupSize, board => board.PatternScore(), history);

            Console.WriteLine("Pattern Scores (reference):");
            PrintScoreHistory(groupSize, board => board.PatternScoreSlow(), history);

            Console.WriteLine("Pattern Score percentages:");
            PrintScoreHistory(
                groupSize,
                board => (int)Math.Round(board.PatternScoreSlow() * 100.0 / board.PatternScore()),
                history);

            Console.WriteLine("Feature scores:");
            PrintScoreHistory(groupSize, board => board.FeatureScore(), history);

            Console.WriteLine("Eval1 scores:");
            PrintScoreHistory(groupSize, OthelloNode.Eval1, history);
        }

        private static void TestPlaybook() {
            const string path = "tempPlaybook.txt";

            List<OthelloNode> history = GenerateRandomGame(12345);

            OthelloNode.ClearPlaybook();
            OthelloNode.TrainPlaybook(history, verbose: true);
            OthelloNode.PrintPlaybookStats();

            OthelloNode.WritePlaybook(path);
            OthelloNode.ClearPlaybook();
            OthelloNode.ReadPlaybook(path);
            OthelloNode.PrintPlaybookStats();
        }

        private static void PrintScoreHistory(int groupSize, Func<OthelloNode, int> getScore, IList<OthelloNode> history) {
            groupSize = Math.Max(1, groupSize);

            for (int i = 0; i < history.Count; i++) {
                Console.Write("{0} \t", getScore(history[i]));
                if ((i % groupSize) == groupSize - 1) {
                    Console.WriteLine();
                }
            }
            if ((history.Count % groupSize) != 0) {
                Console.WriteLine();
            }
        }

        #endregion
    }
}
