using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Solver {
    public abstract class Node<Self> : IEquatable<Self> where Self : Node<Self> {
        public abstract bool IsGameOver { get; }

        public List<Self> GetChildren() {
            var children = new List<Self>();
            this.GetChildren(children);

            return children;
        }

        public abstract void GetChildren(List<Self> children);

        public abstract bool Equals(Self other);
        public abstract override int GetHashCode();

        public abstract override String ToString();
    }

    public abstract class TwoPlayerNode<Self> : Node<Self> where Self : TwoPlayerNode<Self> {
        public abstract int Turn { get; }
    }
}
