using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

using Solver;

namespace Othello {
    public class OthelloSearchParams : SearchParams<OthelloNode> {
        private readonly Func<OthelloNode, int> evaluator;

        public OthelloSearchParams(Func<OthelloNode, int> evaluator, int nodeCacheSize = 128)
            : base(nodeCacheSize) {
            if (evaluator == null) {
                throw new ArgumentNullException();
            }

            this.evaluator = evaluator;
        }

        public override int Evaluate(OthelloNode node) {
            return this.evaluator(node);
        }

        public override int EvaluateEndgame(OthelloNode node) {
            return node.PieceCountSpread();
        }
    }
}
