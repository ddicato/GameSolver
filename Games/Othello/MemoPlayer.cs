using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

using Solver;

namespace Othello {
    /// <summary>
    /// A game player whose purpose is to explore the opening book.
    /// </summary>
    public class MemoPlayer : Player<OthelloNode> {
        public MemoPlayer(OthelloPlaybook playbook, Player<OthelloNode> heuristic, int exploreDepth = 0) {
            if (playbook == null || heuristic == null) {
                throw new ArgumentNullException();
            }

            this.Playbook = playbook;
            this.Heuristic = heuristic;
            this.ExploreDepth = exploreDepth;
        }

        public OthelloPlaybook Playbook {
            get;
            private set;
        }

        public Player<OthelloNode> Heuristic {
            get;
            private set;
        }

        public int ExploreDepth {
            get;
            private set;
        }

        public bool Verbose {
            get;
            set;
        }

        /// <summary>
        /// Select a node based on the following:
        /// 1. If a positive score exists in the playbook, choose the highest, using explored-ness
        ///    as a tiebreaker.
        /// 2. If there are any unexplored nodes, choose the one with the highest heuristic score.
        /// 3. Among the negative-or-zero nodes, choose the least-explored node below a threshold
        ///    level of explored-ness.
        /// 4. If no nodes are below the exploration threshold, or if there is a tie, choose the
        ///    highest score.
        /// </summary>
        /// <param name="nodes">
        /// The list of nodes to select from.
        /// </param>
        /// <returns>
        /// The index of the selected node.
        /// </returns>
        public int SelectNode(List<OthelloNode> nodes) {
            if (nodes == null) {
                throw new ArgumentNullException();
            }

            if (nodes.Count <= 0) {
                throw new ArgumentException();
            }

            var explored = new List<Tuple<int, OthelloPlaybook.Entry>>();
            var unexplored = new List<int>();

            int bestScore = int.MinValue;
            int best = -1;
            for (int i = 0; i < nodes.Count; i++) {
                OthelloNode node = nodes[i];

                OthelloPlaybook.Entry entry;
                if (this.Playbook.TryGetEntry(node, out entry)) {
                    explored.Add(new Tuple<int, OthelloPlaybook.Entry>(i, entry));

                    if (entry.Score > 0 && entry.Score > bestScore) {
                        bestScore = entry.Score;
                        best = i;
                    }
                } else {
                    unexplored.Add(i);
                }
            }

            if (best > 0) {
                if (this.Verbose) {
                    Console.WriteLine(
                        "Choosing winning node with score {0} out of {1} winning nodes.",
                        bestScore,
                        explored.Count(t => t.Item2.Score > 0));
                }
                return best;
            }

            if (unexplored.Count == 1) {
                if (this.Verbose) {
                    Console.WriteLine("Choosing the one unepxlored node.");
                }

                return unexplored[0];
            } else if (unexplored.Count > 0) {
                if (this.Verbose) {
                    Console.WriteLine("Calculating heuristics of {0} unexplored nodes.", unexplored.Count);
                }

                List<OthelloNode> unexploredNodes = unexplored.Select(i => nodes[i]).ToList();
                return unexplored[this.Heuristic.SelectNode(unexploredNodes)];
            }

            if (this.Verbose) {
                Console.WriteLine("Calculating heuristics of {0} losing/drawing nodes.", explored.Count);
            }

            List<OthelloNode> exploredNodes = explored.Select(t => nodes[t.Item1]).ToList();
            return explored[this.Heuristic.SelectNode(exploredNodes)].Item1;
        }
    }
}
