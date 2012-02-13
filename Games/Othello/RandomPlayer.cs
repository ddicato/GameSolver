using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

using Solver;

namespace Othello {
    public class RandomPlayer : Player<OthelloNode> {
        private static readonly Random Random = new Random();

        public int SelectNode(IList<OthelloNode> nodes) {
            if (nodes == null || nodes.Count == 0) {
                return -1;
            }

            return Random.Next(nodes.Count);
        }
    }
}
