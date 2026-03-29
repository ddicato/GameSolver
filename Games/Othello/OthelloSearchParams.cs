using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

using Solver;

namespace Othello {
    public class OthelloSearchParams : SearchParams<OthelloNode> {
        private readonly Func<OthelloNode, int> evaluator;

        public const int MaxKillerSlots = 2;
        public ulong[,] KillerMoves; // [depth, slot] — square bitmask of moves that caused beta cutoffs

        public OthelloSearchParams(Func<OthelloNode, int> evaluator) : base() {
            if (evaluator == null) {
                throw new ArgumentNullException();
            }

            this.evaluator = evaluator;
        }

        public override void Initialize(int nodeCacheSize, bool persistTable = false) {
            base.Initialize(nodeCacheSize, persistTable);
            int maxDepth = nodeCacheSize + 1;
            if (KillerMoves == null || KillerMoves.GetLength(0) < maxDepth) {
                KillerMoves = new ulong[maxDepth, MaxKillerSlots];
            } else {
                Array.Clear(KillerMoves);
            }
        }

        public override int Evaluate(OthelloNode node) {
            return this.evaluator(node);
        }

        public override int EvaluateEndgame(OthelloNode node) {
            return node.PieceCountSpread();
        }
    }
}
