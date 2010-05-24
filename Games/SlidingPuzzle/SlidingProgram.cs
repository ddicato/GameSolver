using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Solver;

namespace SlidingPuzzle {
    class SlidingProgram {
        static void Main(string[] args) {

            var solver = new SinglePlayerSolver<SlidingNode>(SlidingNode.Comparator);
            DateTime progStart = DateTime.Now;

            SlidingNode game = PuzzleReader.Read(@"..\..\Games\SlidingPuzzle\data\sliding.puzzle");

            if (game == null) {
                Console.WriteLine("No game");
                return;
            }

            Console.WriteLine();
            List<SlidingNode> solution = solver.IterativeDeepening(game);
            if (solution == null) {
                Console.WriteLine(game);
                Console.WriteLine("Game is unsolvable.");
                Console.WriteLine();
            } else {
                foreach (SlidingNode sn in solution) {
                    Console.WriteLine(sn);
                }
            }

            Console.WriteLine("Total execution time: {0:0.000} seconds", (DateTime.Now - progStart).TotalSeconds);
            Console.WriteLine("Transposition table entries: {0}", solver.TableEntries);
            Console.WriteLine("Press Enter to exit.");
            Console.ReadLine();
        }
    }
}
