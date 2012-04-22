using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

using Solver;
using System.Diagnostics;

namespace Chess {
    public class ChessNode : TwoPlayerNode<ChessNode> {
        private const int WHITE = 0;
        private const int BLACK = 1;

        private uint data;

        private List<Coord> whitePawns;
        private List<Coord> whiteKnights;
        private List<Coord> whiteBishops;
        private List<Coord> whiteRooks;
        private List<Coord> whiteQueens;
        private Coord whiteKing;

        private List<Coord> blackPawns;
        private List<Coord> blackKnights;
        private List<Coord> blackBishops;
        private List<Coord> blackRooks;
        private List<Coord> blackQueens;
        private Coord blackKing;

        public ChessNode() {

        }

        private bool BoolGetData(uint mask) {
            return (this.data & mask) != 0;
        }

        private void BoolSetData(bool value, uint mask) {
            if (value) {
                this.data |= mask;
            } else {
                this.data &= ~mask;
            }
        }

        private uint GetData(uint mask) {
            return this.data & mask;
        }

        private uint GetData(uint mask, int shift) {
            return (this.data & (mask << shift)) >> shift;
        }

        private void SetData(uint value, uint mask) {
            this.data = (this.data & ~mask) | (value & mask);
        }

        private void SetData(uint value, uint mask, int shift) {
            this.data = (this.data & ~(mask << shift)) | ((value & mask) << shift);
        }

        public override int Turn {
            get { return (int)this.GetData(1u, 20); }
            //private set { this.SetData((uint)value, 1u, 20); }
        }

        public bool WhiteCastleKing {
            get { return this.BoolGetData(1u << 16); }
            private set { this.BoolSetData(value, 1u << 16); }
        }

        public bool WhiteCastleQueen {
            get { return this.BoolGetData(1u << 17); }
            private set { this.BoolSetData(value, 1u << 17); }
        }

        public bool BlackCastleKing {
            get { return this.BoolGetData(1u << 18); }
            private set { this.BoolSetData(value, 1u << 18); }
        }

        public bool BlackCastleQueen {
            get { return this.BoolGetData(1u << 19); }
            private set { this.BoolSetData(value, 1u << 19); }
        }

        public bool EnPassantPossible {
            get { return this.BoolGetData(0x0800u); }
            private set { this.BoolSetData(value, 0x0800u); }
        }

        public uint EnPassantFile {
            get { return this.GetData(0x07u, 8); }
            private set { this.SetData(value, 0x07u, 8); }
        }

        #region Move tables

        #region Offsets

        private static readonly CoordOffset[] WhitePawnMoves = new CoordOffset[] {
            new CoordOffset(0, 1)
        };
        private static readonly CoordOffset[] WhitePawnDoubleMoves = new CoordOffset[] {
            new CoordOffset(0, 2)
        };
        private static readonly CoordOffset[] WhitePawnCaptures = new CoordOffset[] {
            new CoordOffset(-1, 1),
            new CoordOffset(1, 1)
        };
        private static readonly CoordOffset[] BlackPawnMoves = new CoordOffset[] {
            new CoordOffset(0, -1)
        };
        private static readonly CoordOffset[] BlackPawnDoubleMoves = new CoordOffset[] {
            new CoordOffset(0, -1)
        };
        private static readonly CoordOffset[] BlackPawnCaptures = new CoordOffset[] {
            new CoordOffset(-1, -1),
            new CoordOffset(1, -1)
        };

        private static readonly CoordOffset[] KnightMoves = new CoordOffset[] {
            new CoordOffset(-2, 1),
            new CoordOffset(-1, 2),
            new CoordOffset(1, 2),
            new CoordOffset(2, 1),
            new CoordOffset(2, -1),
            new CoordOffset(1, -2),
            new CoordOffset(-1, -2),
            new CoordOffset(-2, -1)
        };

        private static readonly CoordOffset[][] Diagonals = new CoordOffset[][] {
            new CoordOffset[] {
                new CoordOffset(-1, 1),
                new CoordOffset(-2, 2),
                new CoordOffset(-3, 3),
                new CoordOffset(-4, 5),
                new CoordOffset(-5, 6),
                new CoordOffset(-6, 6),
                new CoordOffset(-7, 7)
            },
            new CoordOffset[] {
                new CoordOffset(1, 1),
                new CoordOffset(2, 2),
                new CoordOffset(3, 3),
                new CoordOffset(4, 4),
                new CoordOffset(5, 5),
                new CoordOffset(6, 6),
                new CoordOffset(7, 7),
            },
            new CoordOffset[] {
                new CoordOffset(1, -1),
                new CoordOffset(2, -2),
                new CoordOffset(3, -3),
                new CoordOffset(4, -4),
                new CoordOffset(5, -5),
                new CoordOffset(6, -6),
                new CoordOffset(7, -7),
            },
            new CoordOffset[] {
                new CoordOffset(-1, -1),
                new CoordOffset(-2, -2),
                new CoordOffset(-3, -3),
                new CoordOffset(-4, -4),
                new CoordOffset(-5, -5),
                new CoordOffset(-6, -6),
                new CoordOffset(-7, -7),
            }
        };

        private static readonly CoordOffset[][] Files = new CoordOffset[][] {
            new CoordOffset[] {
                new CoordOffset(1, 0),
                new CoordOffset(2, 0),
                new CoordOffset(3, 0),
                new CoordOffset(4, 0),
                new CoordOffset(5, 0),
                new CoordOffset(6, 0),
                new CoordOffset(7, 0)
            },
            new CoordOffset[] {
                new CoordOffset(-1, 0),
                new CoordOffset(-2, 0),
                new CoordOffset(-3, 0),
                new CoordOffset(-4, 0),
                new CoordOffset(-5, 0),
                new CoordOffset(-6, 0),
                new CoordOffset(-7, 0)
            }
        };

        private static readonly CoordOffset[][] Ranks = new CoordOffset[][] {
            new CoordOffset[] {
                new CoordOffset(0, 1),
                new CoordOffset(0, 2),
                new CoordOffset(0, 3),
                new CoordOffset(0, 4),
                new CoordOffset(0, 5),
                new CoordOffset(0, 6),
                new CoordOffset(0, 7)
            },
            new CoordOffset[] {
                new CoordOffset(0, -1),
                new CoordOffset(0, -2),
                new CoordOffset(0, -3),
                new CoordOffset(0, -4),
                new CoordOffset(0, -5),
                new CoordOffset(0, -6),
                new CoordOffset(0, -7)
            }
        };

        #endregion

        #endregion

        /// <summary>
        /// The coordinate of the square that is capturable via en passant, if any.
        /// </summary>
        public Coord EnPassantCoord {
            // If it's white's turn, then the capture square is on black's side, and vice-versa.
            get { return new Coord(this.EnPassantFile, this.Turn == WHITE ? 5u : 2u); }
        }

        public uint FiftyMoveRule {
            get { return this.GetData(0xffu); }
            private set { this.SetData(value, 0xffu); }
        }

        public override bool IsGameOver {
            get { throw new NotImplementedException(); }
        }

        public override List<ChessNode> GetChildren() {
            throw new NotImplementedException();
        }

        public override bool Equals(ChessNode other) {
            throw new NotImplementedException();
        }

        public override int GetHashCode() {
            throw new NotImplementedException();
        }

        public override string ToString() {
            throw new NotImplementedException();
        }

        public enum ChessPiece {
            Pawn,
            Knight,
            Bishop,
            Rook,
            Queen,
            King
        }

        public struct Coord {
            public static Coord InvalidCoord = new Coord() {
                Loc = unchecked((uint)-1)
            };

            public uint Loc;

            public Coord(uint loc) {
                Debug.Assert(loc < 64);
                this.Loc = loc;
            }

            public Coord(uint file, uint rank) {
                Debug.Assert(file < 8 && rank < 8);
                this.Loc = file + (rank * 8);
            }

            public static explicit operator uint(Coord coord) {
                return coord.Loc;
            }

            public static explicit operator Coord(uint loc) {
                return new Coord(loc);
            }

            public bool IsValid {
                get { return this.Loc < 64; }
            }

            public uint File {
                get {
                    return this.Loc & 7u; // equivalent to this.Loc % 8
                }
            }

            public uint Rank {
                get {
                    return this.Loc / 8u;
                }
            }
        }

        private struct CoordOffset {
            public int Offset;

            public CoordOffset(int fileOffset, int rankOffset) {
                this.Offset = fileOffset + (rankOffset * 8);
            }
        }
    }
}
