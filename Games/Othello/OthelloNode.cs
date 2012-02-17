using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

using Solver;

namespace Othello {
    public class OthelloNode : TwoPlayerNode<OthelloNode> {
        // TODO: change to SELF/OTHER and remove turn? change board[] to 2 ulong fields?
        // TODO: Remove Turn and PlayerCount?
        // TODO: replace with enum
        public const int BLACK = 0;
        public const int WHITE = 1;

        private ulong[] board = new ulong[2];
        private int turn;

        // True if the previous player's move was a pass.
        public bool Pass {
            get;
            private set;
        }

        public override int Turn {
            get { return this.turn; }
        }

        public ulong BlackBoard {
            get { return this.board[BLACK]; }
        }

        public ulong WhiteBoard {
            get { return this.board[WHITE]; }
        }

        public ulong PlayerBoard {
            get { return this.board[this.turn]; }
        }

        public ulong OtherBoard {
            get { return this.board[(this.turn + 1) & 1]; }
        }

        public ulong Occupied {
            get { return this.board[BLACK] | this.board[WHITE]; }
        }

        private OthelloNode(int turn, ulong self, ulong other, bool pass = false) {
            this.turn = turn;
            this.board[turn] = self;
            this.board[(turn + 1) & 1] = other;
            this.Pass = pass;
        }

        public OthelloNode()
            : this(
                BLACK,
                Square[4, 3] | Square[3, 4],
                Square[3, 3] | Square[4, 4]) {
        }

        // TODO: might need to expand API to tell whose point of view this is from, not
        // to mention an evaluation function that maps to a number, not just a bool.
        public override bool IsGameOver {
            get {
                // TODO: make this more efficient with caching
                return BitCount(this.board[BLACK] | this.board[WHITE]) == 64 ||
                    (this.Pass && this.GetChildren().Count == 0);
            }
        }

        // The number of pieces Black is winning by.
        public int Score {
            get {
                return BitCount(this.board[BLACK]) - BitCount(this.board[WHITE]);
            }
        }

        #region Bitmasks

        // The board is arranged like so:
        //
        // \i---->
        // j 00 01 02 03 04 05 06 07
        // | 08 09 10 11 12 13 14 15
        // | 16 17 18 19 20 21 22 23
        // v 24 25 26 27 28 29 30 31
        //   32 33 34 35 36 37 38 39
        //   40 41 42 43 44 45 46 47
        //   48 49 50 51 52 53 54 55
        //   56 57 58 59 60 61 62 63
        //
        // The square's index is equivalent to a one-bit's distance from the least-significant
        // position in a 64-bit int. e.g. square 57, or (1,7), is occupied by white if:
        //     board[1] & (1ul << 57) != 0

        public static readonly ulong[,] Square;
        public static readonly ulong[,] Adjacent;

        private const ulong Corners = (1ul << 63) | (1ul << 56) | (1ul << 7) | 1ul;

        private static readonly ulong[] Row;
        private static readonly ulong[] Column;

        static OthelloNode() {
            Square = new ulong[8, 8];
            for (int i = 0; i < 8; i++) {
                for (int j = 0; j < 8; j++) {
                    Square[i, j] = 1ul << (j * 8 + i);
                }
            }

            Adjacent = new ulong[8, 8];
            for (int i = 0; i < 8; i++) {
                for (int j = 0; j < 8; j++) {
                    ulong value = 0ul;
                    if (i > 0) { // left
                        value |= Square[i - 1, j];
                        if (j > 0) { // up-left
                            value |= Square[i - 1, j - 1];
                        }
                        if (j < 7) { // down-left
                            value |= Square[i - 1, j + 1];
                        }
                    }
                    if (i < 7) { // right
                        value |= Square[i + 1, j];
                        if (j > 0) { // up-right
                            value |= Square[i + 1, j - 1];
                        }
                        if (j < 7) { // down-right
                            value |= Square[i + 1, j + 1];
                        }
                    }
                    if (j > 0) { // up
                        value |= Square[i, j - 1];
                    }
                    if (j < 7) { // down
                        value |= Square[i, j + 1];
                    }

                    Adjacent[i, j] = value;
                }
            }

            Row = new ulong[8];
            Column = new ulong[8];
            for (int i = 0; i < 8; i++) {
                ulong row = 0ul;
                ulong column = 0ul;
                for (int j = 0; j < 8; j++) {
                    row |= Square[j, i];
                    column |= Square[i, j];
                }

                Row[i] = row;
                Column[i] = column;
            }
        }

        #endregion

        #region Hashing and Equality

        public override bool Equals(OthelloNode other) {
            return this.turn == other.turn &&
                this.board[BLACK] == other.board[BLACK] &&
                this.board[WHITE] == other.board[WHITE];
        }

        /*
        private static readonly ulong[] HashSeed = new ulong[] {
            5678901257ul * 1234567811ul,
            4567890121ul * 987654337ul
        };

        // Note: we only need the rightmost 32 bits produced by this function.
        private static ulong HashULong(ulong key) {
            unchecked {
                key = (~key) + (key << 18); // key = (key << 18) - key - 1;
                key = key ^ (key >> 31);
                key = key * 21; // key = (key + (key << 2)) + (key << 4);
                key = key ^ (key >> 11);
                key = key + (key << 6);
                key = key ^ (key >> 22);
            }

            return key;
        }

        public override int GetHashCode() {
            return unchecked((int)HashULong(
                (HashULong(HashSeed[this.turn] ^ this.board[BLACK]) << 32) |
                (HashULong(this.board[WHITE]) & 0x00000000fffffffful) ));
        }
        */

        private static readonly uint[] HashSeed = new uint[] {
            1234567811u,
            3456789023u
        };

        private static uint Mix(uint a, uint b, uint c) {
            unchecked {
                a -= b; a -= c; a ^= (c >> 13);
                b -= c; b -= a; b ^= (a << 8);
                c -= a; c -= b; c ^= (b >> 13);
                a -= b; a -= c; a ^= (c >> 12);
                b -= c; b -= a; b ^= (a << 16);
                c -= a; c -= b; c ^= (b >> 5);
                a -= b; a -= c; a ^= (c >> 3);
                b -= c; b -= a; b ^= (a << 10);
                c -= a; c -= b; c ^= (b >> 15);
            }
            return c;
        }

        private static uint HashULong(ulong key) {
            unchecked {
                key = (~key) + (key << 18); // key = (key << 18) - key - 1;
                key = key ^ (key >> 31);
                key = key * 21; // key = (key + (key << 2)) + (key << 4);
                key = key ^ (key >> 11);
                key = key + (key << 6);
                key = key ^ (key >> 22);
            }

            return (uint)key;
        }

        public override int GetHashCode() {
            return unchecked((int)Mix(
                HashSeed[this.turn],
                HashULong(this.board[BLACK]),
                HashULong(this.board[WHITE])));
        }

        #endregion

        #region Move Calculations

        public static int BitCount(ulong value) {
            ulong count = value -
                ((value >> 1) & 0x7777777777777777ul) -
                ((value >> 2) & 0x3333333333333333ul) -
                ((value >> 3) & 0x1111111111111111ul);

            count += count >> 4;
            count &= 0x0f0f0f0f0f0f0f0f;
            count %= 255;
            return unchecked((int)count);
        }

        public override List<OthelloNode> GetChildren() {
            var children = new List<OthelloNode>();
            this.GetChildren(children);

            return children;
        }

        // TODO: make this part of node API, and implement GetChildren() in the abstract class
        public void GetChildren(List<OthelloNode> children) {
            children.Clear();

            ulong self = this.board[this.turn];
            ulong other = this.board[(this.turn + 1) & 1];
            ulong occupied = self | other;

            for (int jStart = 0; jStart < 8; jStart++) {
                for (int iStart = 0; iStart < 8; iStart++) {
                    // Make sure the current square is unoccupied and at least one adjacent square is
                    // occupied by the opponent.
                    if ((Square[iStart, jStart] & occupied) != 0 ||
                        (Adjacent[iStart, jStart] & other) == 0) {
                        continue;
                    }

                    // Search left
                    int i, j;
                    ulong square;
                    ulong allTraces = 0ul;
                    ulong trace = 0ul;
                    for (i = iStart - 1, j = jStart;
                        i >= 0 && (other & (square = Square[i, j])) != 0;
                        i--) {
                        trace |= square;
                    }
                    if (trace != 0 && i >= 0 && (self & (square = Square[i, j])) != 0) {
                        allTraces |= trace | square;
                    }

                    // Search up-left
                    trace = 0ul;
                    for (i = iStart - 1, j = jStart - 1;
                        i >= 0 && j >= 0 && (other & (square = Square[i, j])) != 0;
                        i--, j--) {
                        trace |= square;
                    }
                    if (trace != 0 && i >= 0 && j >= 0 && (self & (square = Square[i, j])) != 0) {
                        allTraces |= trace | square;
                    }

                    // Search down-left
                    trace = 0ul;
                    for (i = iStart - 1, j = jStart + 1;
                        i >= 0 && j < 8 && (other & (square = Square[i, j])) != 0;
                        i--, j++) {
                        trace |= square;
                    }
                    if (trace != 0 && i >= 0 && j < 8 && (self & (square = Square[i, j])) != 0) {
                        allTraces |= trace | square;
                    }

                    // Search right
                    trace = 0ul;
                    for (i = iStart + 1, j = jStart;
                        i < 8 && (other & (square = Square[i, j])) != 0;
                        i++) {
                        trace |= square;
                    }
                    if (trace != 0 && i < 8 && (self & (square = Square[i, j])) != 0) {
                        allTraces |= trace | square;
                    }

                    // Search up-right
                    trace = 0ul;
                    for (i = iStart + 1, j = jStart - 1;
                        i < 8 && j >= 0 && (other & (square = Square[i, j])) != 0;
                        i++, j--) {
                        trace |= square;
                    }
                    if (trace != 0 && i < 8 && j >= 0 && (self & (square = Square[i, j])) != 0) {
                        allTraces |= trace | square;
                    }

                    // Search down-right
                    trace = 0ul;
                    for (i = iStart + 1, j = jStart + 1;
                        i < 8 && j < 8 && (other & (square = Square[i, j])) != 0;
                        i++, j++) {
                        trace |= square;
                    }
                    if (trace != 0 && i < 8 && j < 8 && (self & (square = Square[i, j])) != 0) {
                        allTraces |= trace | square;
                    }

                    // Search up
                    trace = 0ul;
                    for (i = iStart, j = jStart - 1;
                        j >= 0 && (other & (square = Square[i, j])) != 0;
                        j--) {
                        trace |= square;
                    }
                    if (trace != 0 && j >= 0 && (self & (square = Square[i, j])) != 0) {
                        allTraces |= trace | square;
                    }

                    // Search down
                    trace = 0ul;
                    for (i = iStart, j = jStart + 1;
                        j < 8 && (other & (square = Square[i, j])) != 0;
                        j++) {
                        trace |= square;
                    }
                    if (trace != 0 && j < 8 && (self & (square = Square[i, j])) != 0) {
                        allTraces |= trace | square;
                    }

                    // If we've flipped any pieces, we have a legal move.
                    if (allTraces != 0) {
                        allTraces |= Square[iStart, jStart];
                        children.Add(
                            new OthelloNode(
                                (this.turn + 1) & 1,
                                other & ~allTraces,
                                self | allTraces));
                    }
                }
            }

            // If there are no legal moves, we're forced to pass. If the previous
            // turn was a pass, then the game is over.
            if (!this.Pass && children.Count == 0) {
                children.Add(
                    new OthelloNode(
                        (this.turn + 1) & 1,
                        this.board[(this.turn + 1) & 1],
                        this.board[this.turn],
                        pass: true));
            }
        }

        #endregion

        #region Heuristics

        public static int Eval0(OthelloNode node) {
            return 2 * node.PotentialMobilitySpread()
                - node.FrontierSpread()
                + 6 * node.CornerSpread()
                + 4 * node.StablePieceSpread();
        }

        public static int Eval1(OthelloNode node) {
            return 2 * node.PotentialMobilitySpread()
                - node.FrontierSpread()
                + 6 * node.CornerSpread()
                + 4 * node.StablePieceSpread();
        }

        public int PieceCountSpread() {
            return BitCount(this.board[this.turn]) - BitCount(this.board[(this.turn + 1) & 1]);
        }

        public int CornerSpread() {
            ulong self = this.board[this.turn];
            ulong other = this.board[(this.turn + 1) & 1];

            return BitCount(self & Corners) - BitCount(other & Corners);
        }

        // TODO: merge some of these functions so that we only iterate once
        public int Frontiers() {
            ulong self = this.board[this.turn];
            ulong occupied = self | this.board[(this.turn + 1) & 1];

            int total = 0;

            for (int j = 0; j < 8; j++) {
                for (int i = 0; i < 8; i++) {
                    ulong square = Square[i, j];
                    ulong adjacent = Adjacent[i, j];
                    if ((self & square) != 0 &&
                        (adjacent & occupied) != adjacent) {
                        total++;
                    }
                }
            }

            return total;
        }

        public int FrontierSpread() {
            ulong self = this.board[this.turn];
            ulong other = this.board[(this.turn + 1) & 1];
            ulong occupied = self | other;

            int total = 0;

            for (int j = 0; j < 8; j++) {
                for (int i = 0; i < 8; i++) {
                    ulong square = Square[i, j];
                    ulong adjacent = Adjacent[i, j];
                    if ((adjacent & occupied) != adjacent) {
                        if ((self & square) != 0) {
                            total++;
                        } else if ((other & square) != 0) {
                            total--;
                        }
                    }
                }
            }

            return total;
        }

        public static int GetStablePieces(ulong self) {
            int i, j;
            ulong stable = self & Corners;

            // Search left-to-right, top-to-bottom
            for (i = 0; i < 8; i++) {
                ulong square = Square[i, 0];
                if ((square & self) == 0) {
                    break;
                }
                stable |= square;
            }
            if (i > 0) {
                for (j = 1; j < 8; j++) {
                    for (i = 0; i < 7; i++) {
                        ulong square;
                        if ((Square[i + 1, j - 1] & self) == 0 ||
                            ((square = Square[i, j]) & self) == 0) {
                            break;
                        }
                        stable |= square;
                    }
                    if (i == 0) {
                        break;
                    }
                }
            }

            // Search right-to-left, top-to-bottom
            for (i = 7; i >= 0; i--) {
                ulong square = Square[i, 0];
                if ((square & self) == 0) {
                    break;
                }
                stable &= square;
            }
            if (i < 7) {
                for (j = 1; j < 8; j++) {
                    for (i = 7; i > 0; i--) {
                        ulong square;
                        if ((Square[i - 1, j - 1] & self) == 0 ||
                            ((square = Square[i, j]) & self) == 0) {
                            break;
                        }
                        stable |= square;
                    }
                    if (i == 7) {
                        break;
                    }
                }
            }

            // Search left-to-right, bottom-to-top
            for (i = 0; i < 8; i++) {
                ulong square = Square[i, 7];
                if ((square & self) == 0) {
                    break;
                }
                stable |= square;
            }
            if (i > 0) {
                for (j = 6; j >= 0; j--) {
                    for (i = 0; i < 7; i++) {
                        ulong square;
                        if ((Square[i + 1, j + 1] & self) == 0 ||
                            ((square = Square[i, j]) & self) == 0) {
                            break;
                        }
                        stable |= square;
                    }
                    if (i == 0) {
                        break;
                    }
                }
            }

            // Search right-to-left, bottom-to-top
            for (i = 7; i >= 0; i--) {
                ulong square = Square[i, 0];
                if ((square & self) == 0) {
                    break;
                }
                stable &= square;
            }
            if (i < 7) {
                for (j = 1; j < 8; j++) {
                    for (i = 7; i > 0; i--) {
                        ulong square;
                        if ((Square[i - 1, j - 1] & self) == 0 ||
                            ((square = Square[i, j]) & self) == 0) {
                            break;
                        }
                        stable |= square;
                    }
                    if (i == 7) {
                        break;
                    }
                }
            }

            return BitCount(stable);
        }

        public int StablePieceSpread() {
            return GetStablePieces(this.PlayerBoard) - GetStablePieces(this.OtherBoard);
        }

        // Potential mobility is the number of empty squares next to an opponent's piece. It provides an
        // approximation for mobility, but at a smaller performance cost.
        public int PotentialMobility() {
            ulong other = this.board[(this.turn + 1) & 1]; // TODO: replace with property accessors everywhere
            ulong occupied = other | this.board[this.turn];

            int total = 0;
            for (int j = 0; j < 8; j++) {
                for (int i = 0; i < 8; i++) {
                    if ((Square[i, j] & occupied) == 0 &&
                        (other & Adjacent[i, j]) != 0) {
                        total++;
                    }
                }
            }

            return total;
        }

        // Potential mobility is the number of empty squares next to an opponent's piece. It provides an
        // approximation for mobility, but at a smaller performance cost.
        public int PotentialMobilitySpread() {
            ulong self = this.board[this.turn];
            ulong other = this.board[(this.turn + 1) & 1];
            ulong occupied = self | other;

            int total = 0;
            for (int j = 0; j < 8; j++) {
                for (int i = 0; i < 8; i++) {
                    if ((Square[i, j] & occupied) == 0) {
                        ulong adjacent = Adjacent[i, j];
                        if ((other & adjacent) != 0) {
                            total++;
                        }
                        if ((self & adjacent) != 0) {
                            total--;
                        }
                    }
                }
            }

            return total;
        }

        public int MonteCarlo(int iters) {
            Random random = new Random();
            int totalScore = 0;
            List<OthelloNode> children = this.GetChildren();
            List<OthelloNode> currentChildren = new List<OthelloNode>();
            for (int i = 0; i < iters; i++) {
                OthelloNode current = this;
                // TODO: not needed once we cache result of GetChildren
                currentChildren.Clear();
                foreach (OthelloNode child in children) {
                    currentChildren.Add(child);
                }

                while (currentChildren.Count > 0) {
                    current = currentChildren[random.Next(currentChildren.Count)];
                    current.GetChildren(currentChildren);
                }

                totalScore += current.Score;
            }

            return this.turn == BLACK ? totalScore : -totalScore;
        }

        #endregion

        #region Pretty-printing

        private static void PrintHorizontalSeparator(StringBuilder sb, int groupSize, int offset, bool extra = false) {
            if (offset > 0) {
                for (int i = 0; i < groupSize; i++) {
                    if (i == 0) {
                        sb.Append("---------------");
                    } else {
                        sb.Append("-+----------------");
                    }
                }
                if (extra) {
                    sb.Append("-+");
                }
                sb.AppendLine();
            }
        }

        public static string PrintNodes(int groupSize, bool showTurn, params OthelloNode[] nodes) {
            if (groupSize <= 0) {
                return string.Empty;
            }

            StringBuilder sb = new StringBuilder();

            int offset = 0;
            OthelloNode[] buffer = new OthelloNode[groupSize];
            for (; offset + groupSize <= nodes.Length; offset += groupSize) {
                for (int i = 0; i < groupSize; i++) {
                    buffer[i] = nodes[offset + i];
                }

                PrintHorizontalSeparator(sb, groupSize, offset);
                PrintNodes(sb, showTurn, buffer);
            }

            groupSize = nodes.Length % groupSize;
            if (groupSize > 0) {
                buffer = new OthelloNode[groupSize];
                for (int i = 0; i < groupSize; i++) {
                    buffer[i] = nodes[offset + i];
                }

                PrintHorizontalSeparator(sb, groupSize, offset, extra: true);
                PrintNodes(sb, showTurn, buffer);
            }

            return sb.ToString();
        }

        private static void PrintNodes(StringBuilder sb, bool showTurn, params OthelloNode[] nodes) {
            const char black = 'X';
            const char white = 'O';
            const char blank = '.';
            const char invalid = '!';

            bool first = true;
            if (showTurn) {
                foreach (OthelloNode node in nodes) {
                    if (!first) {
                        sb.Append(" | ");
                    }
                    first = false;
                    sb.Append("Turn = ");
                    sb.Append(node.turn == BLACK ? black : white);
                    sb.Append(node.Pass ? '*' : ' ');
                    sb.Append("      ");
                }
                sb.AppendLine();
            }

            for (int j = 0; j < 8; j++) {
                first = true;
                foreach (OthelloNode node in nodes) {
                    if (!first) {
                        sb.Append(" | ");
                    }
                    first = false;

                    for (int i = 0; i < 8; i++) {
                        ulong square = Square[i, j];
                        if (i > 0) {
                            sb.Append(' ');
                        }
                        sb.Append(
                            (node.board[BLACK] & node.board[WHITE] & square) != 0 ?
                            invalid :
                            (node.board[BLACK] & square) != 0 ?
                            black :
                            (node.board[WHITE] & square) != 0 ?
                            white :
                            blank);
                    }
                }
                sb.AppendLine();
            }
        }

        public override string ToString() {
            return PrintNodes(1, true, this);
        }

        public void PrintScore(string blackName = "Black", string whiteName = "White") {
            if (!this.IsGameOver) {
                return;
            }

            Console.Write("Final Score: {0}", this.Score);
            if (this.Score > 0) {
                Console.WriteLine(" ({0} wins)", blackName);
            } else if (this.Score < 0) {
                Console.WriteLine(" ({0} wins)", whiteName);
            } else {
                Console.WriteLine(" (The game is a draw)");
            }
        }

        #endregion
    }
}
