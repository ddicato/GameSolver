using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;

using Solver;

//\\// TODO: Move ordering based on recently-vacated squares
namespace Gridlock {
    public class GridNode : Node<GridNode> {
        #region Configurable Parameters

        public const int MAIN_BLOCK_LEN = 2;
        public const int EXIT_LOC = 2;
        public const int EXIT_LOC_X = 6;
        public const int EXIT_LOC_Y = EXIT_LOC;
        public const int BOUND_X = 6;
        public const int BOUND_Y = 6;
        public const int BOUND_EXIT = BOUND_X;
        public const int WIN_X = BOUND_X - 1;
        public const int WIN_Y = EXIT_LOC;
        
        #endregion

        #region Helper Structs

        private struct Block {
            private readonly int _data;

            public Block(int x, int y, int dx, int dy) {
                Debug.Assert(x >= 0);
                Debug.Assert(y >= 0);
                Debug.Assert(dx >= 0);
                Debug.Assert(dy >= 0);
                Debug.Assert(x <= SByte.MaxValue); // Arithmetic shift will clobber value
                Debug.Assert(y <= Byte.MaxValue);
                Debug.Assert(dx <= Byte.MaxValue);
                Debug.Assert(dy <= Byte.MaxValue);
                _data = (x << 24) | (y << 16) | (dx << 8) | dy;
            }

            public int x {
                get { return _data >> 24; }
            }

            public int y {
                get { return (_data >> 16) & 0xFF; }
            }

            public int dx {
                get { return (_data >> 8) & 0xFF; }
            }

            public int dy {
                get { return _data & 0xFF; }
            }

            public Block MoveTo(int x, int y) {
                return new Block(x, y, dx, dy);
            }

            public static bool operator ==(Block self, Block other) {
                return self._data == other._data;
            }

            public static bool operator !=(Block self, Block other) {
                return self._data != other._data;
            }
            
            public override bool Equals(object obj) {
                return obj is Block && this == (Block)obj;
            }

            public override int GetHashCode() {
                return _data.GetHashCode();
            }
        }

        private class Board {
            private int[,] _data;

            public Board() {
                _data = new int[BOUND_X, BOUND_Y];
            }

            public Board Copy() {
                Board res = new Board();
                Array.Copy(_data, res._data, _data.Length);
                return res;
            }

            public bool IsClear(Block b) {
                for (int x = b.x; x < b.x + b.dx; x++) {
                    for (int y = b.y; y < b.y + b.dy; y++) {
                        if (_data[x, y] != 0) {
                            return false;
                        }
                    }
                }
                return true;
            }

            public int this[int x, int y] {
                get { return _data[x, y]; }
            }

            public int this[Block b] {
                get { return _data[b.x, b.y]; }
                set {
                    for (int x = b.x; x < b.x + b.dx; x++) {
                        for (int y = b.y; y < b.y + b.dy; y++) {
                            // Ensure no overlaps, unless clearing a block
                            Debug.Assert(value == 0 || _data[x, y] == 0);
                            _data[x, y] = value;
                        }
                    }
                }
            }
        }

        #endregion

        private Board _board;
        private Block[] _blocks;

        public GridNode(int[] state) {
            _CheckState(state);

            _board = new Board();
            List<Block> blocks = new List<Block>();

            // Populate board with main block
            int id = 0;
            Block mainBlock = new Block(
                EXIT_LOC_X == BOUND_X ? state[0] : EXIT_LOC,
                EXIT_LOC_Y == BOUND_Y ? state[0] : EXIT_LOC,
                EXIT_LOC_X == BOUND_X ? MAIN_BLOCK_LEN : 1,
                EXIT_LOC_Y == BOUND_Y ? MAIN_BLOCK_LEN : 1
            );
            _board[mainBlock] = id + 1;
            blocks.Add(mainBlock);

            // Populate board with the rest of the blocks
            for (int i = 1; i < state.Length; i += 4) {
                id++;
                Block block = new Block(state[i], state[i + 1], state[i + 2], state[i + 3]);
                if (!_board.IsClear(block)) {
                    throw new ArgumentException("Block " + (i / 4 + 1) + " overlaps a previous block");
                }
                _board[block] = id + 1;
                blocks.Add(block);
            }

            _blocks = blocks.ToArray();
        }

        public GridNode(GridNode toCopy) {
            _board = toCopy._board.Copy();
            _blocks = new Block[toCopy._blocks.Length];
            Array.Copy(toCopy._blocks, _blocks, toCopy._blocks.Length);
        }

        /// <summary>
        /// Basic sanity-check for a given board state
        /// </summary>
        private void _CheckState(int[] state) {
            if (state.Length % 4 != 1)
                throw new ArgumentException("Invalid number of entries in game state");
            if (state[0] < 0 || state[0] > BOUND_EXIT - MAIN_BLOCK_LEN)
                throw new ArgumentException("Block 0 (main block) is out of bounds");
            
            for (int i = 1; i < state.Length; i += 4) {
                int block = i / 4 + 1;
                int x = state[i];
                int y = state[i + 1];
                int dx = state[i + 2];
                int dy = state[i + 3];

                if (dx < 1 || dy < 1)
                    throw new ArgumentException("Block " + block + " size must be non-zero");
                if (!((dx == 1) ^ (dy == 1)))
                    throw new ArgumentException("Block " + block + " size must be one in exactly one dimension");
                if (x < 0 || x + dx > BOUND_X || y < 0 || y + dy > BOUND_Y)
                    throw new ArgumentException("Block " + block + " is out of bounds");
            }
        }

        private char _PrintID(int id) {
            if (id == 0) {
                return '.';
            } else if (id == 1) {
                return '*';
            } else {
                return (char)((int)'A' + id - 2);
            }
        }

        private GridNode _MoveBlockTo(Block b, int x, int y) {
            GridNode res = new GridNode(this);

            // Clear old entry
            int id = res._board[b];
            res._board[b] = 0;

            // Place new entry
            b = b.MoveTo(x, y);
            res._board[b] = id;
            res._blocks[id - 1] = b;

            return res;
        }

        #region Hashing and Comparison

        private int? _hashCache = null;
        public override int GetHashCode() {
            if (_hashCache == null) {
                int hash = 0; // TODO: start with prime
                foreach (Block b in _blocks) {
                    hash = (hash << 9) ^ (hash >> 23) ^ b.GetHashCode();
                }
                _hashCache = hash;
            }
            return _hashCache.Value;
        }

        // Not strictly correct - for performance, we discount the possibility of
        // isomorphic boards with differing block ID assignments
        // This is correct within a single search tree, since no two blocks can ever
        // switch position through the course of a game
        public override bool Equals(GridNode/*!*/ other) {
            Debug.Assert(other != null);
            Debug.Assert(_blocks.Length == other._blocks.Length);

            for (int i = 0; i < _blocks.Length; i++) {
                if (_blocks[i] != other._blocks[i]) {
                    return false;
                }
            }
            return true;
        }

        #endregion

        #region Node Members

        public override void GetChildren(List<GridNode> children) {
            children.Clear();

            if (IsGameOver) {
                return;
            }

            // Find the possible moves of each block
            foreach (Block b in _blocks) {
                if (b.dx == 1) { // Vertical block
                    // Search up
                    for (int y = b.y - 1; y >= 0 && _board[b.x, y] == 0; y--) {
                        children.Add(_MoveBlockTo(b, b.x, y));
                    }
                    // Search down
                    for (int y = b.y + b.dy; y < BOUND_Y && _board[b.x, y] == 0; y++) {
                        children.Add(_MoveBlockTo(b, b.x, y - b.dy + 1));
                    }
                } else { // Horizontal block
                    // Search left
                    for (int x = b.x - 1; x >= 0 && _board[x, b.y] == 0; x--) {
                        children.Add(_MoveBlockTo(b, x, b.y));
                    }
                    // Search right
                    for (int x = b.x + b.dx; x < BOUND_X && _board[x, b.y] == 0; x++) {
                        children.Add(_MoveBlockTo(b, x - b.dx + 1, b.y));
                    }
                }
            }

            return;
        }
        
        public override bool IsGameOver {
            get {
                return _board[WIN_X, WIN_Y] == 1;
            }
        }

        public override string ToString() {
            StringBuilder res = new StringBuilder(80);
            for (int y = 0; y < BOUND_Y; y++) {
                for (int x = 0; x < BOUND_X; x++) {
                    res.Append(_PrintID(_board[x, y]));
                    res.Append(' ');
                }
                if (y == EXIT_LOC_Y) {
                    res.Append("->");
                }
                res.AppendLine();
            }
            if (EXIT_LOC_Y == BOUND_Y) {
                res.Append(' ', 2 * EXIT_LOC_X);
                res.Append('|');
                res.AppendLine();
                res.Append('v');
                res.AppendLine();
            }
            return res.ToString();
        }

        #endregion
    }
}
