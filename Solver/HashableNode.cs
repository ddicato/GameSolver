using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Solver {
    // TODO: merge with node... we don't really need non-hashable nodes
    // TODO: IComparer<T> MoveOrderingHeuristic
    // TODO: abstract int HeuristicScore
    public abstract class HashableNode<T> : Node<T> where T : HashableNode<T> {
        public readonly IEqualityComparer<T> Comparator; // TODO: rename to Equator
        public abstract override List<T> GetChildren();

        // A move-ordering heuristic. Put this in a separate class so we can create a
        // move-ordering solver, use MPC, etc. but still retain a dumb brute-force solver
        public virtual int CompareMoves(T x, T y) {
            return 0;
        }
    }
}
