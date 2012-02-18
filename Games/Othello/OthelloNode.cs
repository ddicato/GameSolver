using System;
using System.Collections.Generic;
using System.IO;
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

        public const ulong Corners = (1ul << 63) | (1ul << 56) | (1ul << 7) | 1ul;

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

            Row = new ulong[6];
            Column = new ulong[6];
            for (int i = 1; i < 7; i++) {
                ulong row = 0ul;
                ulong column = 0ul;
                for (int j = 0; j < 8; j++) {
                    row |= Square[j, i];
                    column |= Square[i, j];
                }

                Row[i - 1] = row;
                Column[i - 1] = column;
            }

            HorizEdge = new ulong[2];
            VertEdge = new ulong[2];
            for (int i = 0; i < 8; i++) {
                HorizEdge[0] |= Square[i, 0];
                HorizEdge[1] |= Square[i, 7];
                VertEdge[0] |= Square[0, i];
                VertEdge[1] |= Square[7, i];
            }
            HorizEdge[0] |= Square[1, 1] | Square[6, 1];
            HorizEdge[1] |= Square[1, 6] | Square[6, 6];
            VertEdge[0] |= Square[1, 1] | Square[1, 6];
            VertEdge[1] |= Square[6, 1] | Square[6, 6];

            DownLeft = new ulong[9];
            DownRight = new ulong[9];
            for (int i = 3; i < 7; i++) {
                DownLeft[i - 3] = GenerateDownLeftAt(i, 0);
                DownRight[i - 3] = GenerateDownRightAt(7 - i, 0);
                DownLeft[i + 2] = GenerateDownLeftAt(7, i - 2);
                DownRight[i + 2] = GenerateDownRightAt(0, i - 2);
            }
            DownLeft[4] = GenerateDownLeftAt(7, 0);
            DownRight[4] = GenerateDownRightAt(0, 0);

            Corner33 = new ulong[4];
            for (int i = 0; i < 3; i++) {
                for (int j = 0; j < 3; j++) {
                    Corner33[0] |= Square[i, j];
                    Corner33[1] |= Square[7 - i, j];
                    Corner33[2] |= Square[i, 7 - j];
                    Corner33[3] |= Square[7 - i, 7 - j];
                }
            }

            Corner52Cw = new ulong[4];
            Corner52Ccw = new ulong[4];
            for (int i = 0; i < 5; i++) {
                for (int j = 0; j < 2; j++) {
                    Corner52Cw[0] |= Square[i, j];
                    Corner52Ccw[0] |= Square[j, i];

                    Corner52Ccw[1] |= Square[i, j + 6];
                    Corner52Cw[1] |= Square[j + 6, i];

                    Corner52Cw[2] |= Square[i + 3, j + 6];
                    Corner52Ccw[2] |= Square[j + 6, i + 3];

                    Corner52Ccw[3] |= Square[i + 3, j];
                    Corner52Cw[3] |= Square[j, i + 3];
                }
            }

            PatternSets = new ulong[][] {
                Row,
                Column,
                HorizEdge,
                VertEdge,
                DownLeft,
                DownRight,
                Corner33,
                Corner52Cw,
                Corner52Ccw
            };

            PatternScores = new Dictionary<int, Dictionary<ulong, Dictionary<ulong, HeuristicData>>>[PatternSets.Length];
            for (int i = 0; i < PatternScores.Length; i++) {
                PatternScores[i] = new Dictionary<int, Dictionary<ulong, Dictionary<ulong, HeuristicData>>>();
            }

            // TODO: these are equally weighted. Measure how well they correlate with victory and tune
            //       weights accordingly. Also find a weight with respect to Pattern evaluation
            Features = new Func<OthelloNode, int>[] {
                node => node.PieceCountSpread(),
                node => node.CornerSpread(),
                node => node.StablePieceSpread(),
                node => node.FrontierSpread(),
                node => node.PotentialMobilitySpread()
            };
            FeatureNames = new string[] {
                "Piece Count",
                "Corner Squares",
                "Stable Pieces",
                "Frontier Squares",
                "Potential Mobility"
            };

            // TODO: add interpolation to training function
            FeatureScores = new Dictionary<int, Dictionary<int, HeuristicData>>[Features.Length];
            for (int i = 0; i < Features.Length; i++) {
                FeatureScores[i] = new Dictionary<int, Dictionary<int, HeuristicData>>();
            }
        }

        private static ulong GenerateDownLeftAt(int iStart, int jStart) {
            ulong result = 0ul;
            for (int i = iStart, j = jStart; i >= 0 && j < 8; i--, j++) {
                result |= Square[i, j];
            }

            return result;
        }

        private static ulong GenerateDownRightAt(int iStart, int jStart) {
            ulong result = 0ul;
            for (int i = iStart, j = jStart; i < 8 && j < 8; i++, j++) {
                result |= Square[i, j];
            }

            return result;
        }

        #endregion

        #region Board Transforms

        public static IEnumerable<OthelloNode> GetSymmetries(OthelloNode node) {
            yield return node;
            yield return new OthelloNode(node.Turn, RotateRight(node.PlayerBoard), RotateRight(node.OtherBoard), node.Pass);
            yield return new OthelloNode(node.Turn, Rotate180(node.PlayerBoard), Rotate180(node.OtherBoard), node.Pass);
            yield return new OthelloNode(node.Turn, RotateLeft(node.PlayerBoard), RotateLeft(node.OtherBoard), node.Pass);
            yield return new OthelloNode(node.Turn, FlipHoriz(node.PlayerBoard), FlipHoriz(node.OtherBoard), node.Pass);
            yield return new OthelloNode(node.Turn, FlipVert(node.PlayerBoard), FlipVert(node.OtherBoard), node.Pass);
            yield return new OthelloNode(node.Turn, FlipDiag0(node.PlayerBoard), FlipDiag0(node.OtherBoard), node.Pass);
            yield return new OthelloNode(node.Turn, FlipDiag1(node.PlayerBoard), FlipDiag1(node.OtherBoard), node.Pass);
        }

        public static ulong RotateRight(ulong board) {
            ulong result = 0ul;
            for (int i = 0; i < 8; i++) {
                for (int j = 0; j < 8; j++) {
                    if ((board & Square[i, j]) != 0) {
                        result |= Square[7 - j, i];
                    }
                }
            }
            return result;
        }

        public static ulong RotateLeft(ulong board) {
            ulong result = 0ul;
            for (int i = 0; i < 8; i++) {
                for (int j = 0; j < 8; j++) {
                    if ((board & Square[i, j]) != 0) {
                        result |= Square[j, 7 - i];
                    }
                }
            }
            return result;
        }

        public static ulong Rotate180(ulong board) {
            ulong result = 0ul;
            for (int i = 0; i < 8; i++) {
                for (int j = 0; j < 8; j++) {
                    if ((board & Square[i, j]) != 0) {
                        result |= Square[7 - i, 7 - j];
                    }// 1,7 --> 6,0
                }
            }
            return result;
        }

        public static ulong FlipHoriz(ulong board) {
            ulong result = 0ul;
            for (int i = 0; i < 8; i++) {
                for (int j = 0; j < 8; j++) {
                    if ((board & Square[i, j]) != 0) {
                        result |= Square[7 - i, j];
                    }
                }
            }
            return result;
        }

        public static ulong FlipVert(ulong board) {
            ulong result = 0ul;
            for (int i = 0; i < 8; i++) {
                for (int j = 0; j < 8; j++) {
                    if ((board & Square[i, j]) != 0) {
                        result |= Square[i, 7 - j];
                    }
                }
            }
            return result;
        }

        public static ulong FlipDiag0(ulong board) {
            ulong result = 0ul;
            for (int i = 0; i < 8; i++) {
                for (int j = 0; j < 8; j++) {
                    if ((board & Square[i, j]) != 0) {
                        result |= Square[j, i];
                    }
                }
            }
            return result;
        }

        public static ulong FlipDiag1(ulong board) {
            ulong result = 0ul;
            for (int i = 0; i < 8; i++) {
                for (int j = 0; j < 8; j++) {
                    if ((board & Square[i, j]) != 0) {
                        result |= Square[7 - j, 7 - i];
                    }
                }
            }
            return result;
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

        #region Pattern-based Evaluation
        
        public static readonly ulong[] Row;
        public static readonly ulong[] Column;
        public static readonly ulong[] HorizEdge;
        public static readonly ulong[] VertEdge;
        public static readonly ulong[] DownLeft;
        public static readonly ulong[] DownRight;
        public static readonly ulong[] Corner33;
        public static readonly ulong[] Corner52Cw;
        public static readonly ulong[] Corner52Ccw;

        private static readonly ulong[][] PatternSets;

        private static readonly Dictionary<int, Dictionary<ulong, Dictionary<ulong, HeuristicData>>>[] PatternScores;
        private static readonly List<OthelloNode> GameHistory = new List<OthelloNode>(); // TODO: make thread-safe?

        public static readonly Func<OthelloNode, int>[] Features;
        public static readonly string[] FeatureNames;
        private static readonly Dictionary<int, Dictionary<int, HeuristicData>>[] FeatureScores;

        private struct HeuristicData {
            public int Score;
            public int Count;
            public double TotalScore;

            public HeuristicData(int score) {
                this.Score = score;
                this.Count = 1;
                this.TotalScore = (double)score;
            }

            public HeuristicData(HeuristicData data, int newScore) {
                this.Count = data.Count + 1;
                this.TotalScore = data.TotalScore + newScore;
                this.Score = (int)(this.TotalScore / this.Count);
            }
        }

        public int HeuristicScore() {
            ulong self = this.PlayerBoard;
            ulong other = this.OtherBoard;
            int pieceCount = BitCount(this.Occupied);
            int result = 0;

            for (int i = 0; i < PatternSets.Length; i++) {
                foreach (ulong mask in PatternSets[i]) {
                    Dictionary<ulong, Dictionary<ulong, HeuristicData>> mid;
                    Dictionary<ulong, HeuristicData> inner;
                    HeuristicData data;
                    if (PatternScores[i].TryGetValue(pieceCount, out mid) &&
                        mid.TryGetValue(self & mask, out inner) &&
                        inner.TryGetValue(other & mask, out data)) {
                        result += data.Score;
                    }
                }
            }

            for (int i = 0; i < Features.Length; i++) {
                Dictionary<int, HeuristicData> inner;
                HeuristicData data;
                if (FeatureScores[i].TryGetValue(pieceCount, out inner) &&
                    inner.TryGetValue(Features[i](this), out data)) {
                    result += data.Score;
                }
            }

            return result;
        }

        public int PatternScore() {
            ulong self = this.PlayerBoard;
            ulong other = this.OtherBoard;
            int pieceCount = BitCount(this.Occupied);
            int result = 0;

            for (int i = 0; i < PatternSets.Length; i++) {
                foreach (ulong mask in PatternSets[i]) {
                    Dictionary<ulong, Dictionary<ulong, HeuristicData>> mid;
                    Dictionary<ulong, HeuristicData> inner;
                    HeuristicData data;
                    if (PatternScores[i].TryGetValue(pieceCount, out mid) &&
                        mid.TryGetValue(self & mask, out inner) &&
                        inner.TryGetValue(other & mask, out data)) {
                        result += data.Score;
                    }
                }
            }

            return result;
        }

        public int FeatureScore() {
            int pieceCount = BitCount(this.Occupied);
            int result = 0;

            for (int i = 0; i < Features.Length; i++) {
                Dictionary<int, HeuristicData> inner;
                HeuristicData data;
                if (FeatureScores[i].TryGetValue(pieceCount, out inner) &&
                    inner.TryGetValue(Features[i](this), out data)) {
                    result += data.Score;
                }
            }

            return result;
        }

        public int UnknownPatterns() {
            ulong self = this.PlayerBoard;
            ulong other = this.OtherBoard;
            int pieceCount = BitCount(this.Occupied);
            int result = 0;

            for (int i = 0; i < PatternSets.Length; i++) {
                foreach (ulong mask in PatternSets[i]) {
                    Dictionary<ulong, Dictionary<ulong, HeuristicData>> mid;
                    Dictionary<ulong, HeuristicData> inner;
                    if (!PatternScores[i].TryGetValue(pieceCount, out mid) ||
                        !mid.TryGetValue(self & mask, out inner) ||
                        !inner.ContainsKey(other & mask)) {
                        result++;
                    }
                }
            }

            return result;
        }

        public int UnknownFeatures() {
            int pieceCount = BitCount(this.Occupied);
            int result = 0;

            for (int i = 0; i < Features.Length; i++) {
                Dictionary<int, HeuristicData> inner;
                if (!FeatureScores[i].TryGetValue(pieceCount, out inner) ||
                    !inner.ContainsKey(Features[i](this))) {
                    result++;
                }
            }

            return result;
        }

        #endregion

        #region Learning

        public static void AddIntermediateState(OthelloNode node) {
            GameHistory.Add(node);
        }

        // finalScore is black's piece count minus white's
        public static void Train(int finalScore, bool includeSymmetries = true) {
            // TODO: interpolate between turns when averaging. For features, also interpolate between feature values
            // TODO: more and better learning algorithms: Gradient descent, Temporal-difference
            // Score will be stored as an int, so we multiply to get more significant digits.
            finalScore *= 1000;

            if (includeSymmetries) {
                foreach (OthelloNode node in GameHistory) {
                    foreach (OthelloNode permutation in GetSymmetries(node)) {
                        TrainSingle(permutation, finalScore);
                    }
                }
            } else foreach (OthelloNode node in GameHistory) {
                TrainSingle(node, finalScore);
            }

            GameHistory.Clear();
        }

        private static void TrainSingle(OthelloNode node, int finalScore) {
            ulong self = node.PlayerBoard;
            ulong other = node.OtherBoard;
            int pieceCount = BitCount(node.Occupied);
            int relativeScore = node.Turn == BLACK ? finalScore : -finalScore;

            for (int i = 0; i < PatternSets.Length; i++) {
                foreach (ulong mask in PatternSets[i]) {
                    Dictionary<ulong, Dictionary<ulong, HeuristicData>> mid;
                    if (!PatternScores[i].TryGetValue(pieceCount, out mid)) {
                        mid = new Dictionary<ulong, Dictionary<ulong, HeuristicData>>();
                        PatternScores[i][pieceCount] = mid;
                    }

                    Dictionary<ulong, HeuristicData> inner;
                    if (!mid.TryGetValue(self & mask, out inner)) {
                        inner = new Dictionary<ulong, HeuristicData>();
                        mid[self & mask] = inner;
                    }

                    HeuristicData data;
                    if (inner.TryGetValue(other & mask, out data)) {
                        inner[other & mask] = new HeuristicData(data, relativeScore);
                    } else {
                        inner[other & mask] = new HeuristicData(relativeScore);
                    }
                }
            }

            for (int i = 0; i < Features.Length; i++) {
                int score = Features[i](node);

                Dictionary<int, HeuristicData> inner;
                if (!FeatureScores[i].TryGetValue(pieceCount, out inner)) {
                    inner = new Dictionary<int, HeuristicData>();
                    FeatureScores[i][pieceCount] = inner;
                }

                HeuristicData data;
                if (inner.TryGetValue(score, out data)) {
                    inner[score] = new HeuristicData(data, relativeScore);
                } else {
                    inner[score] = new HeuristicData(relativeScore);
                }
            }
        }

        #endregion

        #region Serialization

        private static void WriteComment(StreamWriter writer, string format, params object[] args) {
            WriteComment(writer, 0, format, args);
        }

        private static void WriteComment(StreamWriter writer, int indentLevel, string format, params object[] args) {
            const string indent = "    ";
            const string comment = "#   ";

            writer.Write(comment);
            for (int i = 0; i < indentLevel; i++) {
                writer.Write(indent);
            }
            writer.WriteLine(format, args);
        }

        public static void WriteHeuristics(string path) {// TODO: take file name instead of writer
            const string indent = "    ";
            const string comment = "#   ";

            Console.Write("Saving evaluation parameters...");
            StreamWriter writer = new StreamWriter(path, false);

            try {
                for (int i = 0; i < Features.Length; i++) {
                    WriteComment(writer, "Feature {0}: {1}", i, FeatureNames[i]);
                    writer.WriteLine("Feature");

                    List<int> pieceCounts = FeatureScores[i].Keys.ToList();
                    pieceCounts.Sort();

                    WriteComment(writer, "Data for {0} piece counts", pieceCounts.Count);
                    foreach (int pieceCount in pieceCounts) {
                        writer.WriteLine("PieceCount {0}", pieceCount);

                        List<int> keys = FeatureScores[i][pieceCount].Keys.ToList();
                        keys.Sort();

                        WriteComment(writer, 1, "{0} Entries", keys.Count);
                        foreach (int key in keys) {
                            HeuristicData data = FeatureScores[i][pieceCount][key];
                            writer.Write("{0},{1},{2},{3} ", key, data.Score, data.Count, data.TotalScore);
                        }
                        writer.WriteLine();
                    }
                }

                for (int i = 0; i < PatternSets.Length; i++) {
                    WriteComment(writer, "PatternSet {0}", i);
                    writer.WriteLine("PatternSet");

                    ulong[] masks = PatternSets[i];
                    for (int j = 0; j < masks.Length; j++) {
                        ulong mask = masks[j];
                        WriteComment(writer, 1, "Pattern {0}", j);
                        writer.Write(PrintUlong(mask, comment + indent)); // PrintUlong includes its own newline
                    }

                    List<int> pieceCounts = PatternScores[i].Keys.ToList();
                    pieceCounts.Sort();

                    WriteComment(writer, "Data for {0} piece counts", pieceCounts.Count);
                    foreach (int pieceCount in pieceCounts) {
                        writer.WriteLine("PieceCount {0}", pieceCount);

                        Dictionary<ulong, Dictionary<ulong, HeuristicData>> selfBoards = PatternScores[i][pieceCount];
                        WriteComment(writer, 1, "{0} Entries", selfBoards.Count);
                        foreach (ulong self in selfBoards.Keys) {
                            Dictionary<ulong, HeuristicData> otherBoards = selfBoards[self];
                            WriteComment(writer, 2, "{0} Sub-Entries", otherBoards.Count);

                            writer.WriteLine(self);
                            foreach (ulong other in otherBoards.Keys) {
                                HeuristicData data = otherBoards[other];
                                writer.Write("{0},{1},{2},{3} ", other, data.Score, data.Count, data.TotalScore);
                            }
                            writer.WriteLine();
                        }
                    }
                }
            } catch {
                Console.WriteLine("error.");
            } finally {
                writer.Close();
            }

            Console.WriteLine("done.");
        }

        #endregion

        #region Deserialization

        public static void ClearHeuristics() {
            foreach (Dictionary<int, Dictionary<int, HeuristicData>> dict in FeatureScores) {
                dict.Clear();
            }
            foreach (Dictionary<int, Dictionary<ulong, Dictionary<ulong, HeuristicData>>> dict in PatternScores) {
                dict.Clear();
            }

            GC.Collect();
        }

        private static void CheckEOF(string line) {
            if (line == null) {
                throw new InvalidDataException("Unexpected EOF");
            }
        }

        // Read and trim the next line, skipping comments and blank lines
        private static string NextLine(StreamReader reader, string remainder = null) {
            const string comment = "#"; // TODO: cleanup consts

            string line = remainder == null ? reader.ReadLine() : remainder;
            if (line != null) {
                line = line.Trim();
            }

            while (line != null &&
                (string.IsNullOrWhiteSpace(line) ||
                line.StartsWith(comment))) {
                line = reader.ReadLine().Trim();
            }

            return line;
        }

        private static void EatLine(StreamReader reader, string match, string remainder = null) {
            string line = NextLine(reader, remainder);
            if (line != match) {
                throw new InvalidDataException(string.Format(
                    "Expected {0}, got {1}",
                    match == null ? "<EOF>" : '"' + match + '"',
                    line == null ? "<EOF>" : '"' + line + '"'));
            }
        }

        private static bool TryEatPrefix(StreamReader reader, string match, out string line, string remainder = null) {
            line = NextLine(reader, remainder);
            if (!line.StartsWith(match)) {
                return false;
            }

            line = line.Substring(match.Length).Trim();
            return true;
        }

        private static HeuristicData ParseHeuristicData<T>(string input, Func<string, T> parse, out T key) {
            CheckEOF(input);
            input = input.Trim();
            string[] data = input.Split(new char[] { ',' }, 4);
            if (data == null || data.Length < 4) {
                throw new FormatException(string.Format(
                    "Cannot parse \"{0}\" as {1}",
                    input,
                    typeof(HeuristicData).Name));
            }

            key = parse(data[0]);
            return new HeuristicData() {
                Score = int.Parse(data[1]),
                Count = int.Parse(data[2]),
                TotalScore = double.Parse(data[3])
            };
        }

        public static void ReadHeuristics(string path) {
            try {
                if (!File.Exists(path)) {
                    return;
                }
            } catch {
                return;
            }

            Console.Write("Loading evaluation parameters...");

            ClearHeuristics();
            StreamReader reader = new StreamReader(path);

            try {
                string line = null;
                for (int i = 0; i < Features.Length; i++) {
                    EatLine(reader, "Feature", line);

                    while (TryEatPrefix(reader, "PieceCount", out line)) {
                        int pieceCount = int.Parse(line);
                        Dictionary<int, HeuristicData> inner;
                        if (!FeatureScores[i].TryGetValue(pieceCount, out inner)) {
                            inner = new Dictionary<int, HeuristicData>();
                            FeatureScores[i][pieceCount] = inner;
                        }

                        line = NextLine(reader);
                        CheckEOF(line);
                        foreach (string entry in line.Split(new char[0], StringSplitOptions.RemoveEmptyEntries)) {
                            int key;
                            HeuristicData data = ParseHeuristicData(entry, int.Parse, out key);
                            inner[key] = data;
                        }
                    }
                }

                for (int i = 0; i < PatternSets.Length; i++) {
                    EatLine(reader, "PatternSet", line);

                    line = null;
                    int pieceCount;
                    while (TryEatPrefix(reader, "PieceCount", out line, line) &&
                        int.TryParse(line, out pieceCount)) {
                        Dictionary<ulong, Dictionary<ulong, HeuristicData>> mid;
                        if (!PatternScores[i].TryGetValue(pieceCount, out mid)) {
                            mid = new Dictionary<ulong, Dictionary<ulong, HeuristicData>>();
                            PatternScores[i][pieceCount] = mid;
                        }

                        ulong self;
                        line = NextLine(reader);
                        CheckEOF(line);
                        while (line != null && ulong.TryParse(line, out self)) {
                            Dictionary<ulong, HeuristicData> inner;
                            if (!mid.TryGetValue(self, out inner)) {
                                inner = new Dictionary<ulong, HeuristicData>();
                                mid[self] = inner;
                            }

                            line = NextLine(reader);
                            CheckEOF(line);
                            foreach (string entry in line.Split(new char[0], StringSplitOptions.RemoveEmptyEntries)) {
                                ulong other;
                                HeuristicData data = ParseHeuristicData(entry, ulong.Parse, out other);
                                inner[other] = data;
                            }

                            line = NextLine(reader);
                        }

                        // EOF
                        if (line == null) {
                            break;
                        }
                    }
                }

                // Make sure there's nothing left at end of file.
                EatLine(reader, null);
            } catch {
                Console.WriteLine("error.");
                ClearHeuristics();
            } finally {
                reader.Close();
            }

            Console.WriteLine("done.");
        }

        #endregion

        #region Pretty-printing

        public static string PrintUlong(ulong value, string prefix = null) {
            const char empty = '.';
            const char occupied = '+';

            StringBuilder sb = new StringBuilder();
            for (int j = 0; j < 8; j++) {
                for (int i = 0; i < 8; i++) {
                    if (i > 0) {
                        sb.Append(' ');
                    } else if (!string.IsNullOrEmpty(prefix)) {
                        sb.Append(prefix);
                    }
                    sb.Append((value & Square[i, j]) == 0 ? empty : occupied);
                }
                sb.AppendLine();
            }

            return sb.ToString();
        }

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
