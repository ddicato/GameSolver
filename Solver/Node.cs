using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Solver {
    public abstract class Node<T> where T : Node<T> {
        public abstract bool IsWinning { get; }
        public abstract List<T> GetChildren();

        public abstract override String ToString();
    }
}
