using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

using Solver;

namespace Othello {
    public class HumanPlayer : Player<OthelloNode> {
        public int SelectNode(IList<OthelloNode> nodes) {
            if (nodes == null || nodes.Count == 0) {
                Console.WriteLine("No legal moves! Passing.");
                return -1;
            }

            Console.WriteLine(OthelloNode.PrintNodes(4, false, nodes.ToArray()));
            Console.Write("Select one: ");

            int result;
            while (!int.TryParse(Console.ReadLine(), out result) ||
                result < 0 ||
                result >= nodes.Count) {
                Console.Write("Invalid index! Please enter a number between 0 and {0}:", nodes.Count - 1);
            }

            return result;
        }
    }
}
