using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Othello {
    class OthelloProgram {
        static void Main(string[] args) {
            RunTests();

            Console.WriteLine("Press Enter to exit.");
            Console.ReadLine();
        }

        #region Test Code

        private static void RunTests() {
            TestBitCount();
            PrintInitialBoard();
            PrintInitialChildren();
            PerftTest();
        }

        private static void PrintInitialBoard() {
            Console.WriteLine("Initial Board:");
            Console.WriteLine(new OthelloNode());
        }

        private static void PrintInitialChildren() {
            List<OthelloNode> children = new OthelloNode().GetChildren();

            Console.WriteLine("Initial board has {0} children:", children.Count);
            children.ForEach(Console.Write);
            Console.WriteLine();
        }

        // The Perft method for testing move generation functions: evaluate the entire game
        // tree to a certain depth, count the leaf nodes, and compare against known values.
        private static long[] PerftLeaves = new long[] {
            4,
            12,
            56,
            244,
            1396,
            8200,
            55092,
            390216,
            3005288,
            24571284,
            212258572,
            1939886052,
            18429634780,
            184042061172
        };

        // At depth 11, we start to see game-over situations 
        private static int[] PerftEarlyTerminations = new int[] {
            0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
            228, 584, 6938, 23340
        };

        private static long Perft(OthelloNode node, uint depth, ref int earlyTerminations) {
            if (depth == 0) {
                return 1;
            }

            List<OthelloNode> children = node.GetChildren();
            if (children.Count == 0) {
                earlyTerminations++;
                return 0;
            }

            if (depth == 1) {
                return children.Count;
            }

            long total = 0L;
            foreach (OthelloNode child in children) {
                total += Perft(child, depth - 1, ref earlyTerminations);
            }

            return total;
        }

        private static void PerftTest() {
            for (uint depth = 1; depth < PerftLeaves.Length; depth++) {
                Console.WriteLine("Perft test at depth {0}", depth);
                
                int earlyTerminations = 0;
                DateTime start = DateTime.Now;
                long leaves = Perft(new OthelloNode(), depth, ref earlyTerminations);
                TimeSpan elapsed = DateTime.Now - start;

                bool failed = false;
                if (PerftLeaves[depth - 1] != leaves) {
                    Console.WriteLine("\t{0} leaves should be {1}", leaves, PerftLeaves[depth - 1]);
                    failed = true;
                }
                if (PerftEarlyTerminations[depth - 1] != earlyTerminations) {
                    Console.WriteLine("\t{0} early terminations should be {1}", earlyTerminations, PerftEarlyTerminations[depth - 1]);
                    failed = true;
                }

                Console.WriteLine("...done in {0:0.000} sec ({1:0.00} leaves/ms)",
                    elapsed.TotalSeconds,
                    leaves / elapsed.TotalMilliseconds);
                Console.WriteLine();

                if (failed) {
                    Console.WriteLine("Test failed at depth {0}. Stopping test.", depth);
                    Console.WriteLine();
                    break;
                }

                const double timeLimit = 1.0; // in minutes
                if (elapsed.TotalMinutes > timeLimit) {
                    Console.WriteLine("Depth {0} took longer than {1} minute(s). Stopping test.", depth, timeLimit);
                    Console.WriteLine();
                    break;
                }
            }
        }

        private static int NaiveBitCount(ulong value) {
            int count = 0;
            while (value != 0) {
                while ((value & 1) == 0) {
                    value >>= 1;
                }
                value >>= 1;
                count++;
            }

            return count;
        }

        private static void TestBitCount() {
            const int iters = 1000000;
            Console.WriteLine("Testing bitCount on {0} values", iters);

            Random random = new Random();
            byte[] data = new byte[8];
            for (int i = 0; i < iters; i++) {
                random.NextBytes(data);
                ulong value = data[0] |
                    ((ulong)data[1] << 8) |
                    ((ulong)data[2] << 16) |
                    ((ulong)data[3] << 24) |
                    ((ulong)data[4] << 32) |
                    ((ulong)data[5] << 40) |
                    ((ulong)data[6] << 48) |
                    ((ulong)data[7] << 56);

                if (OthelloNode.BitCount(value) != NaiveBitCount(value)) {
                    Console.WriteLine("\tMismatch in {0}: {1} should be {2}",
                        value,
                        OthelloNode.BitCount(value),
                        NaiveBitCount(value));
                }
            }

            Console.WriteLine("...done");
            Console.WriteLine();
        }

        #endregion
    }
}
