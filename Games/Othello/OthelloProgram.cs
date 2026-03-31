using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using Solver;

namespace Othello {
    class OthelloProgram {
        const string ParamsPath = "params.txt";
        const string PlaybookPath = "playbook.txt";

        static void Main(string[] args) {
            var options = new (string Name, Action<string[]> Action)[] {
                ("Training (self-play loop)", TrainingMain),
                ("Benchmark (vs Adversary)", BenchmarkMain),
                ("Solve playbook leaves", SolvePlaybookLeavesMain),
                ("Repair SolvedScores", RepairSolvedScoresMain),
                ("Run tests", TestMain),
            };

            while (true) {
                Console.WriteLine();
                Console.WriteLine("Select a mode:");
                for (int i = 0; i < options.Length; i++) {
                    Console.WriteLine("  {0}. {1}", i + 1, options[i].Name);
                }
                Console.WriteLine("  {0}. Exit", options.Length + 1);
                Console.Write("> ");

                string input = Console.ReadLine();
                if (int.TryParse(input, out int choice) && choice >= 1 && choice <= options.Length + 1) {
                    if (choice == options.Length + 1) {
                        return;
                    }
                    Console.WriteLine();
                    options[choice - 1].Action(args);
                }
            }
        }

        /// <summary>
        /// Repairs playbook SolvedScores that were computed with the wrong negamax
        /// formula (-max instead of -min). Clears backfilled (non-leaf) SolvedScores,
        /// recomputes them with the corrected formula, and re-saves.
        /// </summary>
        static void RepairSolvedScoresMain(string[] args) {
            OthelloNode.ReadPlaybook(PlaybookPath);
            OthelloNode.PrintPlaybookStats();

            int cleared = OthelloNode.Playbook.ClearBackfilledSolvedScores();
            Console.WriteLine("Cleared {0} backfilled SolvedScore(s).", cleared);

            OthelloNode.PrintPlaybookStats();

            int backfilled = OthelloNode.Playbook.BackfillSolvedScores();
            Console.WriteLine("Re-backfilled {0} SolvedScore(s) with corrected formula.", backfilled);

            OthelloNode.PrintPlaybookStats();
            OthelloNode.WritePlaybook(PlaybookPath);

            Console.WriteLine("Done. Press Enter to exit.");
            Console.ReadLine();
        }

        static void TestMain(string[] args) {
            RunTests();

            Console.WriteLine("Press Enter to exit.");
            Console.ReadLine();
        }

        static void SolvePlaybookLeavesMain(string[] args) {
            SolvePlaybookLeaves();

            Console.WriteLine("Press Enter to exit.");
            Console.ReadLine();
        }

        /// <summary>
        /// Loads the playbook, finds all non-endgame leaf nodes, solves the endgame
        /// from each leaf, and adds the solved game line to the playbook.
        /// </summary>
        static void SolvePlaybookLeaves() {
            OthelloNode.ReadPlaybook(PlaybookPath);
            OthelloNode.PrintPlaybookStats();

            var leaves = OthelloNode.Playbook.GetUnfinishedLeaves();
            Console.WriteLine("{0} unfinished leaf nodes found.", leaves.Count);

            if (leaves.Any()) {
                int maxSolveDepth = SearchUtils.EndgameDepth + 2;
                int solved = 0;
                int processed = 0;
                int backfilled;
                DateTime start = DateTime.Now;

                // Filter leaves into solvable and non-solvable upfront.
                var solvable = new List<OthelloPlaybook.Entry>();
                int skipped = 0;
                foreach (var leaf in leaves) {
                    if (leaf.State.EmptySquareCount > maxSolveDepth) {
                        skipped++;
                    } else {
                        solvable.Add(leaf);
                    }
                }

                Console.WriteLine("{0} solvable, {1} skipped (too deep).", solvable.Count, skipped);

                // Process solvable leaves in parallel batches.
                const int batchSize = 1000;
                for (int batchStart = 0; batchStart < solvable.Count; batchStart += batchSize) {
                    int batchEnd = Math.Min(batchStart + batchSize, solvable.Count);
                    var batch = solvable.GetRange(batchStart, batchEnd - batchStart);

                    // Solve endgames in parallel. Each thread gets its own player.
                    var results = new List<(OthelloNode Node, int? Score)>[batch.Count];
                    Parallel.For(0, batch.Count, new ParallelOptions { MaxDegreeOfParallelism = 8 }, j => {
                        var leaf = batch[j];
                        if (leaf.Children.Count != 0) {
                            // Gained children from another thread's ExtendLeaf (shared position).
                            results[j] = null;
                        } else {
                            // Each thread creates its own player to avoid sharing the transposition table.
                            // depth=2 for a couple iterations of move ordering before the endgame solve.
                            var player = new MtdFPlayer(2, OthelloNode.Eval1, solveEndgame: true);
                            results[j] = PlayOutEndgame(player, leaf.State);
                        }
                    });

                    // Apply results to the playbook sequentially.
                    for (int j = 0; j < batch.Count; j++) {
                        if (results[j] != null && batch[j].Children.Count == 0) {
                            OthelloNode.Playbook.ExtendLeaf(batch[j], results[j]);
                            solved++;
                        }
                    }

                    // Backfill SolvedScores on intermediate PV nodes.
                    OthelloNode.Playbook.BackfillSolvedScores();

                    processed += batch.Count;
                    TimeSpan elapsed = DateTime.Now - start;
                    Console.WriteLine(
                        "Solved {0} of {1}; remaining {2}. Elapsed: {3:0.0}s => {4:0.0} solved/s",
                        solved, solvable.Count, solvable.Count - processed,
                        elapsed.TotalSeconds, solved / elapsed.TotalSeconds
                    );

                    if (processed % (batchSize * 10) == 0) {
                        // Backfill SolvedScores on intermediate nodes of existing PVs.
                        backfilled = OthelloNode.Playbook.BackfillSolvedScores();
                        Console.WriteLine("Backfilled SolvedScore on {0} intermediate nodes.", backfilled);
                    }
                    if (processed % batchSize == 0) {
                        OthelloNode.PrintPlaybookStats();
                        OthelloNode.WritePlaybook(PlaybookPath);
                    }
                    if (processed % (batchSize * 10) == 0) {
                        OthelloNode.CalculateHeuristics();
                        OthelloNode.WriteHeuristics(ParamsPath);
                    }
                }

                // Backfill SolvedScores on intermediate nodes of existing PVs.
                backfilled = OthelloNode.Playbook.BackfillSolvedScores();
                Console.WriteLine("Backfilled SolvedScore on {0} intermediate nodes.", backfilled);

                // Final save.
                OthelloNode.PrintPlaybookStats();
                OthelloNode.WritePlaybook(PlaybookPath);
                OthelloNode.CalculateHeuristics();
                OthelloNode.WriteHeuristics(ParamsPath);
            }
        }

        /// <summary>
        /// Plays out the endgame from the given position, using the player to select
        /// the best move at each step. Returns the sequence of board states from the
        /// first move after <paramref name="start"/> through game-over. The player
        /// should have PersistTable=true so the transposition table is reused between
        /// moves.
        /// </summary>
        private static List<(OthelloNode Node, int? Score)> PlayOutEndgame(MtdFPlayer player, OthelloNode start) {
            var continuation = new List<(OthelloNode Node, int? Score)>();
            OthelloNode current = start;

            player.PersistTable = false;
            while (!current.IsGameOver) {
                var children = current.GetChildren();
                if (children.Count == 0) {
                    break;
                }

                int bestScore;
                int index = player.SelectNode(children, out bestScore);

                // After initialize is called once (ensured by the above player.persistTable = false), we want to
                // persist the table for subsequent calls to SelectNode.
                player.PersistTable = true;

                current = children[index];

                // A pass node that ends the game (double pass) doesn't represent a new
                // board position — skip it. Set the solved score on the last real entry.
                if (current.Pass && current.IsGameOver) {
                    if (continuation.Count > 0) {
                        var last = continuation[continuation.Count - 1];
                        if (last.Score == null) {
                            // Score from the last real node's perspective.
                            continuation[continuation.Count - 1] =
                                (last.Node, last.Node.PieceCountSpread());
                        }
                    }
                    break;
                }

                // Only set SolvedScore on terminal nodes. Intermediate nodes will be
                // backfilled bottom-up by BackfillSolvedScores().
                int? solvedScore = current.IsGameOver
                    ? current.PieceCountSpread()
                    : (int?)null;
                continuation.Add((current, solvedScore));
            }

            return continuation;
        }

        // TODO: verbosity levels: Board, Turn, Game, GameSet, Output, None
        static void TrainingMain(string[] args) {
            const bool verbose = false;
            const bool memoize = true;
            const TrainingMode randomTraining = TrainingMode.LossDraw;
            const TrainingMode selfTraining = TrainingMode.All;
            const TrainingMode adversarialTraining = TrainingMode.All;
            const bool continueRandomGames = false;
            const int randomGames = 100;
            const int selfGames = 50;
            const int adversarialGames = 50;
            const int depth = 5;

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
            OthelloNode.PrintPlaybookStats();
            OthelloNode.ReadHeuristics(ParamsPath);
            OthelloNode.CalculateHeuristics();

            do {
                p0 = new MtdFPlayer(1, patternEval, verbose: verbose, randomness: false);
                p1 = new RandomPlayer() { Verbose = verbose };
                if (memoize) {
                    p0 = new MemoPlayer(OthelloNode.Playbook, p0) { Verbose = verbose };
                    p1 = new MemoPlayer(OthelloNode.Playbook, p1) { Verbose = verbose };
                }
                PlayGames(p0, p1, randomGames, ref randomGamesPlayed, verbose, training: randomTraining, p1Name: "RandomPlayer");
            } while (randomGames > 0 && selfGames <= 0 && adversarialGames <= 0);

            while (true) {
                p0 = new MtdFPlayer(depth, patternEval, verbose: verbose, randomness: !memoize, exploringPatterns: true, exploringPlaybook: true);
                p1 = new MtdFPlayer(depth, patternEval, verbose: verbose, randomness: !memoize, exploringPatterns: true, exploringPlaybook: true);
                if (memoize) {
                    p0 = new MemoPlayer(OthelloNode.Playbook, p0) { Verbose = verbose };
                    p1 = new MemoPlayer(OthelloNode.Playbook, p1) { Verbose = verbose };
                }
                PlayGames(p0, p1, selfGames, ref selfGamesPlayed, verbose: verbose, training: selfTraining, p1Name: "self");

                p0 = new MtdFPlayer(depth, patternEval, verbose: false, randomness: !memoize, exploringPatterns: true, exploringPlaybook: true);
                p1 = new MtdFPlayer(depth + 1, OthelloNode.Eval1, verbose: false, randomness: !memoize, exploringPatterns: true, exploringPlaybook: true);
                if (memoize) {
                    p0 = new MemoPlayer(OthelloNode.Playbook, p0) { Verbose = verbose };
                    p1 = new MemoPlayer(OthelloNode.Playbook, p1) { Verbose = verbose };
                }
                PlayGames(p0, p1, adversarialGames, ref adversarialGamesPlayed, verbose: verbose, training: adversarialTraining, p1Name: "Adversary+");

                if (continueRandomGames) {
                    p0 = new MtdFPlayer(1, patternEval, verbose: verbose, randomness: false);
                    p1 = new RandomPlayer() { Verbose = verbose };
                    if (memoize) {
                        p0 = new MemoPlayer(OthelloNode.Playbook, p0) { Verbose = verbose };
                        p1 = new MemoPlayer(OthelloNode.Playbook, p1) { Verbose = verbose };
                    }
                    PlayGames(p0, p1, randomGames, ref randomGamesPlayed, verbose, training: randomTraining, p1Name: "RandomPlayer");
                }
            }

            Console.WriteLine("Press Enter to exit.");
            Console.ReadLine();
        }

        /// <summary>
        /// Benchmark PatternScoreSlow against Adversary (Eval1 at depth) and Adversary+
        /// (Eval1 at depth+1). Alternates black/white for fairness and tracks per-color stats.
        /// </summary>
        static void BenchmarkMain(string[] args) {
            const int games = 100;
            const int depth = 5;

            OthelloNode.ReadPlaybook(PlaybookPath);
            OthelloNode.ReadHeuristics(ParamsPath);
            OthelloNode.CalculateHeuristics();

            Func<OthelloNode, int> patternEval = node => node.PatternScoreSlow();

            var trials = new (string Name, Func<OthelloNode, int> Eval, int Depth)[] {
                ("Adversary (Eval1, depth " + depth + ")", OthelloNode.Eval1, depth),
                ("Adversary+ (Eval1, depth " + (depth + 1) + ")", OthelloNode.Eval1, depth + 1),
            };

            var results = new List<(string Label, int Wins, int Losses, int Draws, int TotalScore,
                int BlackWins, int BlackLosses, int BlackDraws, int WhiteWins, int WhiteLosses, int WhiteDraws)>();

            foreach (var (name, eval, oppDepth) in trials) {
                Console.WriteLine();
                Console.WriteLine("========================================");
                Console.WriteLine("  Player 1 (PatternScoreSlow, depth {0}) vs {1}", depth, name);
                Console.WriteLine("========================================");

                int wins = 0, losses = 0, draws = 0, totalScore = 0;
                int bWins = 0, bLosses = 0, bDraws = 0;
                int wWins = 0, wLosses = 0, wDraws = 0;

                for (int i = 0; i < games; i++) {
                    Console.Write("Game {0}/{1}: ", i + 1, games);

                    var p0 = new MtdFPlayer(depth, patternEval, randomness: true);
                    var p1 = new MtdFPlayer(oppDepth, eval, randomness: true);

                    int result;
                    bool p0IsBlack = (i & 1) == 0;
                    if (p0IsBlack) {
                        result = GameLoop(p0, "Player 1", p1, name, null);
                    } else {
                        result = GameLoop(p1, name, p0, "Player 1", null);
                        result = -result;
                    }

                    totalScore += result;
                    if (result > 0) { wins++; if (p0IsBlack) bWins++; else wWins++; }
                    else if (result < 0) { losses++; if (p0IsBlack) bLosses++; else wLosses++; }
                    else { draws++; if (p0IsBlack) bDraws++; else wDraws++; }

                    Console.WriteLine(
                        "  score={0:+0;-0;0} (P1 as {1}) running: {2}W {3}L {4}D avg={5:+0.0;-0.0;0.0}",
                        result,
                        p0IsBlack ? "black" : "white",
                        wins, losses, draws,
                        (double)totalScore / (i + 1));
                }

                results.Add((name, wins, losses, draws, totalScore,
                    bWins, bLosses, bDraws, wWins, wLosses, wDraws));
            }

            // Print comparison.
            Console.WriteLine();
            Console.WriteLine("========================================");
            Console.WriteLine("  Results: Player 1 (PatternScoreSlow, depth {0})", depth);
            Console.WriteLine("  ({0} games each, alternating black/white)", games);
            Console.WriteLine("========================================");
            Console.WriteLine();
            Console.WriteLine("{0,-35} {1,6} {2,6} {3,6} {4,10} {5,10}",
                "Opponent", "Wins", "Losses", "Draws", "Avg Score", "Win Rate");
            Console.WriteLine(new string('-', 79));

            foreach (var r in results) {
                Console.WriteLine("{0,-35} {1,6} {2,6} {3,6} {4,10:+0.00;-0.00;0.00} {5,9:0.0}%",
                    r.Label, r.Wins, r.Losses, r.Draws,
                    (double)r.TotalScore / games,
                    100.0 * r.Wins / games);
                Console.WriteLine("  as black: {0,3}W {1,3}L {2,3}D    as white: {3,3}W {4,3}L {5,3}D",
                    r.BlackWins, r.BlackLosses, r.BlackDraws,
                    r.WhiteWins, r.WhiteLosses, r.WhiteDraws);
            }

            Console.WriteLine();
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
                bool trained = false;

                int result;
                var gameHistory = new List<(OthelloNode Node, int? Score)>();
                if ((i & 1) == 0) {
                    result = GameLoop(p0, p0Name, p1, p1Name, gameHistory, verbose, training);
                    if (result > 0 && (training & TrainingMode.Win) != TrainingMode.None ||
                        result < 0 && (training & TrainingMode.Loss) != TrainingMode.None ||
                        result == 0 && (training & TrainingMode.Draw) != TrainingMode.None) {
                            OthelloNode.TrainPlaybook(gameHistory);
                            trained = true;
                    }
                } else {
                    result = GameLoop(p1, p1Name, p0, p0Name, gameHistory, verbose, training);
                    if (result > 0 && (training & TrainingMode.Loss) != TrainingMode.None ||
                        result < 0 && (training & TrainingMode.Win) != TrainingMode.None ||
                        result == 0 && (training & TrainingMode.Draw) != TrainingMode.None) {
                            OthelloNode.TrainPlaybook(gameHistory);
                            trained = true;
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
                if (trained) {
                    Console.WriteLine(
                        "{0} new entries added to the game database.",
                        OthelloNode.PlaybookCount - playbookCount);
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
                OthelloNode.PrintPlaybookStats();
                OthelloNode.WritePlaybook(PlaybookPath);
                OthelloNode.CalculateHeuristics();
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
            List<(OthelloNode Node, int? Score)> gameHistory,
            bool verbose = false,
            TrainingMode training = TrainingMode.All) {
            OthelloNode board = new OthelloNode();
            List<OthelloNode> children;
            
            if (gameHistory == null) {
                training = TrainingMode.None;
            } else {
                gameHistory.Clear();
                gameHistory.Add((board, null));
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
                    // TODO: add solved endgame score
                    gameHistory.Add((board, null));
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
            /*TestBitCount();
            PrintSymmetries();
            PrintPatterns();
            PrintInitialBoard();
            PrintInitialChildren();
            PrintRandomGame(); // run before loading params.txt
            PrintWeights(); // loads params.txt
            PrintRandomGame(); // run after loading params.txt
            TestPlaybook();*/

            OthelloNode.ReadPlaybook(PlaybookPath);
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

        private static long Perft(OthelloNode node, uint depth, ref int earlyTerminations, out long totalBookCount) {
            if (depth == 0) {
                totalBookCount = OthelloNode.PlaybookContains(node) ? 1 : 0;
                return 1;
            }

            totalBookCount = 0L;

            List<OthelloNode> children = node.GetChildren();
            if (children.Count == 0) {
                earlyTerminations++;
                return 0L;
            }

            long total = 0L;
            foreach (OthelloNode child in children) {
                long bookCount;
                total += Perft(child, depth - 1, ref earlyTerminations, out bookCount);
                totalBookCount += bookCount;
            }

            return total;
        }

        private static void PerftTest() {
            for (uint depth = 1; depth < PerftLeaves.Length; depth++) {
                Console.Write("Perft test at depth {0}...", depth);

                int earlyTerminations = 0;
                DateTime start = DateTime.Now;
                long bookLeaves;
                long leaves = Perft(new OthelloNode(), depth, ref earlyTerminations, out bookLeaves);
                TimeSpan elapsed = DateTime.Now - start;

                bool failed = false;
                if (PerftLeaves[depth - 1] != leaves) {
                    Console.WriteLine();
                    Console.WriteLine("\t{0} leaves should be {1}", leaves, PerftLeaves[depth - 1]);
                    failed = true;
                }
                if (PerftEarlyTerminations[depth - 1] != earlyTerminations) {
                    Console.WriteLine();
                    Console.WriteLine("\t{0} early terminations should be {1}", earlyTerminations, PerftEarlyTerminations[depth - 1]);
                    failed = true;
                }

                Console.WriteLine(
                    "done in {0:0.000} sec ({1:0.00} leaves/ms)",
                    elapsed.TotalSeconds,
                    leaves / elapsed.TotalMilliseconds);
                Console.WriteLine(
                    "{0:n0} of {1:n0} entries in playbook ({2:p1})",
                    bookLeaves,
                    leaves,
                    (double)bookLeaves / leaves);
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
        private static List<(OthelloNode Node, int? Score)> GenerateRandomGame(int? seed = null) {
            Random random;
            if (seed == null || !seed.HasValue) {
                random = new Random();
            } else {
                random = new Random(seed.Value);
            }

            var history = new List<(OthelloNode Node, int? Score)>();
            OthelloNode node = new OthelloNode();
            while (!node.IsGameOver) {
                history.Add((node, null));
                List<OthelloNode> children = node.GetChildren();
                if (children == null || children.Count == 0) {
                    Console.WriteLine("Error: node.IsGameOver is false but GetChildren returns no children.");
                    Console.WriteLine(node);
                }

                node = children[random.Next(children.Count)];
            }

            history.Add((node, null));
            return history;
        }

        private static void PrintRandomGame() {
            const int groupSize = 5;
            var history = GenerateRandomGame(12345);
            OthelloNode[] stateHistory = history.Select(t => t.Node).ToArray();

            Console.WriteLine("Random game history:");
            Console.WriteLine(OthelloNode.PrintNodes(groupSize, true, stateHistory));

            Console.WriteLine("Pattern scores:");
            PrintScoreHistory(groupSize, board => board.PatternScore(), stateHistory);

            Console.WriteLine("Pattern Scores (reference):");
            PrintScoreHistory(groupSize, board => board.PatternScoreSlow(), stateHistory);

            Console.WriteLine("Pattern Score percentages:");
            PrintScoreHistory(
                groupSize,
                board => (int)Math.Round(board.PatternScoreSlow() * 100.0 / board.PatternScore()),
                stateHistory);

            Console.WriteLine("Eval1 scores:");
            PrintScoreHistory(groupSize, OthelloNode.Eval1, stateHistory);
        }

        private static void TestPlaybook() {
            const string path = "tempPlaybook.txt";

            var history = GenerateRandomGame(12345);

            OthelloNode.ClearPlaybook();
            OthelloNode.TrainPlaybook(history, verbose: true);
            OthelloNode.PrintPlaybookStats();

            OthelloNode.WritePlaybook(path);
            OthelloNode.ClearPlaybook();
            OthelloNode.ReadPlaybook(path);
            OthelloNode.PrintPlaybookStats();

            File.Delete(path);
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
