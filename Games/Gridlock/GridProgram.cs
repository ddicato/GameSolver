using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

using Solver;

namespace Gridlock {
    class GridProgram {
        static void Main(string[] args) {
            bool verbose = !args.Contains("-q");

            var solver = new SinglePlayerSolver<GridNode>(GridNode.Comparator);
            DateTime progStart = DateTime.Now;

            foreach (string arg in args) {
                int index;
                if (!int.TryParse(arg, out index)) {
                    continue;
                }

                GridNode game = GridLevels.Get(index);

                if (game == null) {
                    if (verbose) {
                        Console.WriteLine("Skipping nonexistent level " + index);
                    }
                    continue;
                }

                Console.WriteLine();
                List<GridNode> solution = solver.IterativeDeepening(game);
                if (verbose) {
                    if (solution != null) {
                        foreach (GridNode node in solution) {
                            Console.WriteLine(node);
                        }
                    } else {
                        Console.WriteLine(game);
                    }
                }
            }

            Console.WriteLine("Total execution time: {0:0.000} seconds", (DateTime.Now - progStart).TotalSeconds);
            Console.WriteLine("Transposition table entries: {0}", solver.TableEntries);
            Console.WriteLine("Press Enter to exit.");
            Console.ReadLine();
        }
    }
}
