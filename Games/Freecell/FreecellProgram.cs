using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

using Solver;

namespace Freecell {
    using Card = FreecellNode.Card;
    using Column = FreecellNode.Column;

    class FreecellProgram {

        private static string[][] data = {
            new string[] {"0D","QC","AH","AS","4H","8S","3D"},
            new string[] {"3H","KD","0H","2D","8H","0S","2S"},
            new string[] {"6H","3S","KH","7C","QH","AD","QD"},
            new string[] {"5S","4C","9H","6S","0C","JC","QS"},
            new string[] {"2H","7H","5D","8C","7D","6C"},
            new string[] {"KC","JD","KS","JH","9S","3C"},
            new string[] {"8D","4S","7S","4D","5C","9C"},
            new string[] {"AC","5H","JS","9D","6D","2C"},
        };

        // unsolvable with 4 columns and 3 cells
        private static string[][] unsolvable = {
            new string[] {"AC","5C","4C","3D","2C"},
            new string[] {"AS","5S","4S","3H","2S"},
            new string[] {"AH","5H","4H","3S","2H"},
            new string[] {"AD","5D","4D","3C","2D"},
        };

        static void Main(string[] args) {
            /*
            // TODO: remove hard-coded state in favor of reading files
            var home = new int[] { 10, 12, 10, 12 };
            var cells = new SortedSet<Card>(Card.Comparer.Instance);
            var columns = new SortedSet<Column>(Column.Comparer.Instance);
            columns.Add(new Column(Card.Make(11, 0)).Push(Card.Make(12, 0)));
            columns.Add(new Column(Card.Make(11, 2)).Push(Card.Make(12, 2)));

            var game = new FreecellNode(columns, cells, home);
             */
            
            //var game = FreecellNode.Read(data);
            //var game = FreecellNode.Read(unsolvable);

            var game = new FreecellNode(); // random deal
            var solver = new SinglePlayerSolver<FreecellNode>();
            DateTime progStart = DateTime.Now;

            List<FreecellNode> solution = solver.IterativeDeepening(game);
            if (solution == null) {
                Console.WriteLine(game);
                Console.WriteLine("Board is unsolvable.");
            } else {
                foreach (FreecellNode node in solution) {
                    Console.WriteLine(node);
                }
            }

            Console.WriteLine("Total execution time: {0:0.000} seconds", (DateTime.Now - progStart).TotalSeconds);
            Console.WriteLine("Transposition table entries: {0}", solver.TableEntries);
#if DEBUG
            Console.WriteLine("Press Enter to exit.");
            Console.ReadLine();
#endif
        }
    }
}
