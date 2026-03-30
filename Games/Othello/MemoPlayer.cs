using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;

using Solver;

namespace Othello {
    /// <summary>
    /// A game player whose purpose is to explore the opening book.
    /// </summary>
    public class MemoPlayer : Player<OthelloNode> {
        public MemoPlayer(OthelloPlaybook playbook, Player<OthelloNode> heuristic) {
            ArgumentNullException.ThrowIfNull(playbook, nameof(playbook));
            ArgumentNullException.ThrowIfNull(heuristic, nameof(heuristic));

            this.Playbook = playbook;
            this.Heuristic = heuristic;
        }

        public OthelloPlaybook Playbook {
            get;
            private set;
        }

        public Player<OthelloNode> Heuristic {
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
        ///    level of explored-ness. TODO: explore nonzero thresholds, and/or use explored-ness
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
            ArgumentNullException.ThrowIfNull(nodes, nameof(nodes));

            if (nodes.Count <= 0) {
                throw new ArgumentException(nameof(nodes) + " must not be empty");
            }

            Dictionary<int, OthelloPlaybook.Entry> explored = [];
            List<int> unexplored = [];
            int winning = 0;
            int drawing = 0;
            int losing = 0;

            int bestScore = int.MinValue;
            int best = -1;
            int worstScore = int.MaxValue;
            int worst = -1;
            for (int i = 0; i < nodes.Count; i++) {
                OthelloNode node = nodes[i];

                if (this.Playbook.TryGetEntry(node, out OthelloPlaybook.Entry entry)) {
                    int score = -entry.Score; // keep scores from the point of view of the parent node

                    explored.Add(i, entry);
                    if (score > 0) {
                        winning++;
                    } else if (score == 0) {
                        drawing++;
                    } else {
                        losing++;
                    }

                    if (score > bestScore) {
                        bestScore = score;
                        best = i;
                    }
                    if (score < worstScore) {
                        worstScore = score;
                        worst = i;
                    }
                } else {
                    unexplored.Add(i);
                }
            }

            if (this.Verbose) {
                Console.Write(
                    "Of {0} nodes: {1}(loss, draw, win) = ({2}, {3}, {4}); {5} unexplored; ",
                    nodes.Count,
                    best >= 0 ? "(worst, best) = (" + worstScore + ", " + bestScore + "); " : "",
                    losing, drawing, winning, unexplored.Count);
            }

            if (bestScore > 0) {
                if (this.Verbose) {
                    Console.WriteLine("choosing the winner.");
                    Console.WriteLine();
                }
                return best;
            }

            if (nodes.Count == 1) {
                if (this.Verbose) {
                    Console.WriteLine("choosing the only node.");
                    Console.WriteLine();
                }
                return 0;
            }

            if (unexplored.Count == 1) {
                if (this.Verbose) {
                    Console.WriteLine("choosing the only unexplored node.");
                    Console.WriteLine();
                }

                return unexplored[0];
            } else if (unexplored.Count > 0) {
                if (this.Verbose) {
                    Console.WriteLine("evaluating unexplored nodes...");
                }

                List<OthelloNode> unexploredNodes = [.. unexplored.Select(i => nodes[i])];
                return unexplored[this.Heuristic.SelectNode(unexploredNodes)];
            }

            if (this.Verbose) {
                Console.WriteLine("evaluating losing/drawing nodes...");
            }
            int selected = this.Heuristic.SelectNode(nodes);
            if (this.Verbose) {
                int score = -explored[selected].Score;
                Console.WriteLine("Chose node with playbook score {0}.", score);
                Console.WriteLine();
            }
            return selected;
        }
    }
}
