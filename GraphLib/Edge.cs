using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace GraphLib
{
    public struct Edge
    {
        public Edge(Vertex source, Vertex sink)
        {
            this.Source = source;
            this.Sink = sink;
        }

        public Vertex Source
        {
            get;
            private set;
        }

        public Vertex Sink
        {
            get;
            private set;
        }
    }
}
