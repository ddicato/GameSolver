using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Solver {
    public abstract class Node<Self> where Self : Node<Self> {
        // TODO: rename to Equator
        public abstract IEqualityComparer<Self> Comparator { get; }
        public abstract bool IsWinning { get; }
        public abstract List<Self> GetChildren();

        public abstract override String ToString();
    }
}
