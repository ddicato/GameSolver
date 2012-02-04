using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace GraphLib
{
    public abstract class Vertex : IComparable<Vertex>
    {
        public override int CompareTo(Vertex other)
        {
            if (object.ReferenceEquals(other, null))
            {
                return int.MaxValue;
            }
            if (this.Equals(other))
            {
                return 0;
            }
            return this.GetHashCode().CompareTo(other.GetHashCode());
        }

        public static bool operator >(Vertex a, Vertex b)
        {
            if (object.ReferenceEquals(a, null))
            {
                return !object.ReferenceEquals(b, null);
            }
            return a.CompareTo(b) > 0;
        }

        public static bool operator <(Vertex a, Vertex b)
        {
            if (object.ReferenceEquals(a, null))
            {
                return !object.ReferenceEquals(b, null);
            }
            return a.CompareTo(b) < 0;
        }

        public static bool operator >=(Vertex a, Vertex b)
        {
            return !(a < b);
        }

        public static bool operator <=(Vertex a, Vertex b)
        {
            return !(a > b);
        }

        public static bool operator ==(Vertex a, Vertex b)
        {
            if (object.ReferenceEquals(a, null))
            {
                return object.ReferenceEquals(b, null);
            }
            return a.Equals(b);
        }

        public static bool operator !=(Vertex a, Vertex b)
        {
            return !(a == b);
        }
    }
}
