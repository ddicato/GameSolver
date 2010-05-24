using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.IO;

namespace SlidingPuzzle {
    class PuzzleReader {
        public const bool TORUS = false;

        public static SlidingNode Read(string filename) {
            IEnumerable<string> boardStrings = File.ReadAllLines(filename).Where(
                s => !String.IsNullOrEmpty(s) && s[0] != '#'
            );

            int width = boardStrings.First().Split(',').Length;
            int height = boardStrings.Count();

            if (height < 1 || width < 1) {
                return null;
            }

            int i = 0;
            int j = 0;
            int[,] board = new int[width, height];
            foreach (string row in boardStrings) {
                foreach (string num in row.Split(',')) {
                    int val;
                    if (!Int32.TryParse(num, out val)) {
                        string lowerNum = num.ToLower();
                        if (num.Length != 1 || num[0] < 'a' || num[0] > 'z') {
                            throw new ArgumentException("Invalid tile value: " + num);
                        }
                        val = (int)lowerNum[0] - (int)'a' + 10;
                    }
                    board[i, j] = val;
                    i++;
                }
                i = 0;
                j++;
            }

            return new SlidingNode(board, TORUS);
        }
    }
}
