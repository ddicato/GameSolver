using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Solver {
    public interface Player<Node> where Node : Node<Node> {
        int SelectNode(List<Node> nodes);
    }
}
