using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading;

namespace Solver {
    public struct SolverResult<T> where T : Node<T>, IEquatable<T> {
        public readonly bool Winning;
        public readonly List<T> Solution;
        public readonly int Depth;
        public readonly int Threads;

        public SolverResult(bool winning, List<T> solution, int depth, int threads) {
            Winning = winning;
            Solution = solution;
            Depth = depth;
            Threads = threads;
        }
    }

    public class SinglePlayerSolver<T> where T : Node<T>, IEquatable<T> {
        private static readonly int ProcCount = 1;//\\//Environment.ProcessorCount; TODO: multi-threaded is slower
        private static readonly List<T> Unsolvable = null;
        private static readonly List<T> EmptySolution = new List<T>();
        
        // Statistical information
        private int _nodes; // single search
        private int _totalNodes; // iteratively-deepened search
        private int _hits;
        private int _totalHits;
        private int _currentPly;

        private readonly TranspositionTable<T> _table;

        public SinglePlayerSolver() {
            _table = new TranspositionTable<T>();
            ResetStats();
        }

        private void ResetStats() {
            _nodes = 0;
            _hits = 0;
        }

        public int Nodes {
            get { return _nodes; }
        }

        public int TotalNodes {
            get { return _totalNodes + _nodes; }
        }

        public int Hits {
            get { return _hits; }
        }

        public int TotalHits {
            get { return _totalHits + _hits; }
        }

        public int TableEntries {
            get { return _table.Count; }
        }

        public int CurrentPly {
            get { return _currentPly; }
        }

        public SolverResult<T> Solve(T node) {
            return Solve(node, int.MaxValue, 0, ProcCount);
        }

        public SolverResult<T> Solve(T node, int maxPly) {
            return Solve(node, maxPly, 0, ProcCount);
        }

        private SolverResult<T> Solve(T node, int maxPly, int ply, int threads) {
            // Base case - node has already been searched
            if (ply > 0 && !_table.Insert(node, maxPly, ply, true)) {
                _hits++;
                return new SolverResult<T>(false, Unsolvable, ply, threads);
            }
            
            bool winning = node.IsWinning;
            _nodes++;

            // Base case - winning board
            if (winning) {
                List<T> solution = new List<T>();
                solution.Add(node);
                return new SolverResult<T>(true, solution, ply, threads);
            }

            // Base case - depth limit reached
            if (ply >= maxPly) {
                return new SolverResult<T>(false, EmptySolution, ply, threads);
            }

            // Prepare initial search quantities
            bool bestResult = false;
            List<T> bestSolution = Unsolvable;
            int bestDepth = maxPly;
            int searchDepth = maxPly;

            // Find which child nodes to recurse on
            List<T> children = node.GetChildren();

            // Base case - no new nodes to search
            if (children.Count == 0) {
#if DEBUG
                if (ProcCount > 1 && !_table.Insert(node, maxPly, ply, false)) {
                    Debug.Assert(false);
                }
#else
                _table.Insert(node, maxPly, ply, false);
#endif
                return new SolverResult<T>(false, Unsolvable, ply, threads);
            }

            // Move ordering
            //\\// TODO: conflict
            /*
            if (ply < maxPly - 2 && node is OrderedNode<T>) { // TODO: tune param
                if (children.Count > 1) {
                    children.Sort(((OrderedNode<T>)(object)node).CompareMoves);
                }
            }
            */
            /*
            if (ply < maxPly - 2) { // TODO: tune param
                if (children.Count > 1) {
                    children.Sort(node.CompareMoves);
                }
            }
            */

            // Recurse on each child
            if (threads > 1 && maxPly > 8) { // TODO: leave as high as 8??
                // Multi-threaded case - we're near the root of a large tree
                var threadQueue = new Queue<KeyValuePair<Thread, int>>(threads);
                var results = new Queue<SolverResult<T>>();
                int excessThreads = Math.Max(0, threads - children.Count);
                for (int i = 0; i < children.Count; i++) {
                    T c = children[i];
                    KeyValuePair<Thread, int> t;
                    if (threadQueue.Count >= threads) {
                        t = threadQueue.Dequeue();
                        while (!t.Key.Join(50)) {
                            threadQueue.Enqueue(t);
                            t = threadQueue.Dequeue();
                        }
                        excessThreads += t.Value;
                    }

                    // TODO: This is voodoo math which attempts to assign larger numbers of
                    //       sub-threads towards the end of the thread queue. Behavior should
                    //       be defined slightly more rigorously.
                    int childThreads = excessThreads / (children.Count - i);
                    excessThreads -= childThreads;
                    Thread thread = new Thread(
                        new ParameterizedThreadStart(
                            delegate(object child) {
                                var result = Solve((T)child, searchDepth, ply + 1, childThreads + 1);
                                lock (results) {
                                    results.Enqueue(result);
                                }
                            }
                        )
                    );
                    thread.Start(c);
                    threadQueue.Enqueue(new KeyValuePair<Thread, int>(thread, childThreads));
                }

                while (threadQueue.Count > 0) {
                    threadQueue.Dequeue().Key.Join();
                }

                while (results.Count > 0) {
                    var result = results.Dequeue();
                    if (result.Winning && (!bestResult || result.Depth < bestDepth)) {
                        bestResult = true;
                        bestSolution = result.Solution;
                        bestDepth = result.Depth;
                    } else if (!bestResult && result.Solution == EmptySolution) {
                        bestSolution = EmptySolution;
                        bestDepth = result.Depth;
                    }
                }
            } else {
                // Single-threaded case
                foreach (T c in children) {
                    var result = Solve(c, searchDepth, ply + 1, 1);
                    if (result.Winning && (!bestResult || result.Depth < bestDepth)) {
                        // Optimization for iterative deepening, when we know
                        // the current solution is shortest
                        //result.Solution.Insert(0, node);
                        //return result;
                        bestResult = true;
                        bestSolution = result.Solution;
                        bestDepth = result.Depth;
                        // We now only need to search for shorter solutions
                        searchDepth = result.Depth - 1;
                    } else if (!bestResult && result.Solution == EmptySolution) {
                        bestSolution = EmptySolution;
                        bestDepth = result.Depth;
                    }
                }
            }

            if (bestSolution == Unsolvable) {
#if DEBUG
                if (ProcCount > 1 && !_table.Insert(node, maxPly, ply, false)) {
                    Debug.Assert(false);
                }
#else
                _table.Insert(node, maxPly, ply, false);
#endif
            } else if (bestSolution != EmptySolution) {
                bestSolution.Insert(0, node);
            }
            return new SolverResult<T>(bestResult, bestSolution, bestDepth, threads);
        }


        public List<T> IterativeDeepening(T node) {
            double totalTime = 0;
            _totalNodes = 0;
            _totalHits = 0;

            SolverResult<T> result;
            for (_currentPly = 0; true; _currentPly++) {
                Console.Write("Searching at {0} ply...", _currentPly);

                DateTime start = DateTime.Now;
                result = Solve(node, _currentPly);
                TimeSpan elapsed = DateTime.Now - start;

                Console.WriteLine(
                    "{0} nodes in {1:0.000} seconds ({2:0.000} nodes/ms). {3} hits.",
                    _nodes,
                    Math.Max(elapsed.TotalSeconds, 1e-6),
                    _nodes / Math.Max(elapsed.TotalMilliseconds, 1e-3),
                    _hits
                );

                totalTime = Math.Max(totalTime + elapsed.TotalMilliseconds, 1e-3);
                _totalNodes += _nodes;
                _totalHits += _hits;
                ResetStats();

                if (result.Solution == Unsolvable) {
                    Console.WriteLine("Board is unsolvable.");
                    break;
                } else if (result.Winning) {
                    Console.WriteLine(
                        "Winning board found at depth {0}",
                        result.Depth
                    );
                    break;
                }
            }

            Console.WriteLine(
                "Total: {0} nodes searched in {1:0.000} seconds ({2:0.000} nodes/ms). {3} hits.",
                _totalNodes, totalTime / 1000, _totalNodes / totalTime, _totalHits
            );

            return result.Solution;
        }
    }
}
