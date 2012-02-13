using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using Solver;

namespace SlidingPuzzle {
    class SlidingNode : Node<SlidingNode> {

        private int[,] board;

        private const int BLANK = 0;

        private int width;
        private int height;
        private bool torus;

        private int blankX;
        private int blankY;

        private SlidingNode Slide(int x, int y) {
            int newX = blankX + x;
            int newY = blankY + y;
            if (torus) {
                newX = mod(newX, width);
                newY = mod(newY, height);
            } else if ((newX < 0 || newX >= width ||
                        newY < 0 || newY >= height)) {
                return null;
            }
            int[,] boardCopy = (int[,])board.Clone();

            boardCopy[newX, newY] = BLANK;
            boardCopy[blankX, blankY] = board[newX, newY];

            return new SlidingNode(boardCopy, torus);
        }

        // An implementation of mod that correctly handles negative dividends
        private static int mod(int x, int y) {
            Debug.Assert(y > 0);
            if (x >= 0) {
                return x % y;
            }
            return y + x % y;
        }

        public SlidingNode Left {
            get {
                return Slide(-1, 0);
            }
        }

        public SlidingNode Right {
            get {
                return Slide(1, 0);
            }
        }

        public SlidingNode Up {
            get {
                return Slide(0, -1);
            }
        }

        public SlidingNode Down {
            get {
                return Slide(0, 1);
            }
        }

        public SlidingNode(int[,] board, bool torus) {
            this.board = board;
            this.width = board.GetLength(0);
            this.height = board.GetLength(1);
            this.torus = torus;

            //check validity
            for (int i = 1; i < width * height - 1; i++) {
                bool ok = false;
                foreach (int num in board) {
                    if (num == i) {
                        ok = true;
                        break;
                    }
                }
                if (!ok) { throw new InvalidBoardException("No number " + i); }
            }

            //find blank space
            for (int i = 0; i < width; i++) {
                for (int j = 0; j < height; j++) {
                    if (board[i, j] == BLANK) {
                        this.blankX = i;
                        this.blankY = j;
                        return;
                    }
                }
            }

            throw new InvalidBoardException("No blank space");
        }

        public SlidingNode(int width, int height, bool torus) {
            throw new NotImplementedException();
        }

        public override List<SlidingNode> GetChildren() {
            List<SlidingNode> children = new List<SlidingNode>(4) { Left, Right, Up, Down };
/*/#if DEBUG
            Console.WriteLine("==============NODE==============");



            Console.WriteLine("-----------CHILDREN-------------");
            
            foreach(SlidingNode child in children) {
                Console.WriteLine(child);
            }
            Console.WriteLine("==============FIN===============");

//#endif*/
            children.RemoveAll(board => board == null);
            return children;
        }

        public override bool IsGameOver {
            get {
                if (board[width - 1, height - 1] != BLANK) {
                    return false;
                }

                for (int i = 0; i < width; i++) {
                    for (int j = 0; j < height; j++) {
                        if (i == width - 1 && j == height - 1) {
                            return true;
                        } else if (board[i, j] != (j * width) + i + 1) {
                            return false;
                        }
                    }
                }

                throw new InvalidOperationException("unreachable code");
            }
        }

        public override string ToString() {
            StringBuilder output = new StringBuilder();

            output.Append('=', width + 2);
            output.AppendLine();

            for (int j = 0; j < height; j++) {
                output.Append('|');
                for (int i = 0; i < width; i++) {
                    if (board[i, j] == 0) {
                        output.Append(' ');
                    } else if (board[i, j] < 10) {
                        output.Append((char)((int)'0' + board[i, j]));
                    } else {
                        output.Append((char)((int)'A' + board[i, j] - 10));
                    }
                }
                output.AppendLine("|");
            }
            output.Append('=', width + 2);
            output.AppendLine();
//#if DEBUG
            /*
            output.Append("\nIsWinning = ");
            output.AppendLine(IsWinning.ToString());
            */
//#endif

            return output.ToString();
        }

        public override bool Equals(SlidingNode obj) {
            if (this.width != obj.width || this.height != obj.height) {
//#if DEBUG
                throw new Exception("the fechk?");
//#else
//                return false;
//#endif
            }

            for (int i = 0; i < width; i++) {
                for (int j = 0; j < height; j++) {
                    if (board[i, j] != obj.board[i, j]) {
#if DEBUG
                        /*
                        Console.WriteLine("False");
                        Console.WriteLine(this.ToString());
                        Console.WriteLine(obj.ToString());
                        Console.ReadLine();
                        */
#endif
                        return false;
                    }
                }
            }

#if DEBUG
            /*
            Console.WriteLine("True");
            Console.WriteLine(this.ToString());
            Console.WriteLine(obj.ToString());
            Console.ReadLine();
            */
#endif
            return true;
        }

        public override int GetHashCode() {
            int hash = 464001913;
            for (int x = 0; x < width; x++) {
                for (int y = 0; y < width; y++) {
                    hash = (hash << 13) ^ (hash >> 19) ^ board[x, y].GetHashCode();
                }
            }
            return hash;
        }
    }

    public class InvalidBoardException : Exception {
        public InvalidBoardException(string message) : base(message) { }
    }
}
