using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;

using Solver;

namespace Freecell {
    public class FreecellNode : Node<FreecellNode>, IEquatable<FreecellNode> {
        #region Configurable Parameters

        public const string RANKS = "A23456";
        public const string SUITS = "CSHD"; // convention: 1st half is black, 2nd is red
        // TODO: make const
        public static readonly int DECK_SIZE = RANKS.Length * SUITS.Length;

        public const int CELLS = 4;
        public const int COLUMNS = 8;

        public const int SEED = 123456;

        #endregion

        #region Helper Structs

        internal struct Card {
            // TODO: store a single int, have Rank/Suit properties
            private readonly int _data;
            private static readonly Random _rnd = new Random(SEED);
            // TODO: make const
            private static readonly int RANK_SHIFT =
                (int)Math.Ceiling(Math.Log(SUITS.Length, 2));
            private static readonly int SUIT_MASK =
                (int)Math.Pow(RANK_SHIFT, 2) - 1;

            public Card(int rank, int suit) {
                if (rank < 0 || rank > RANKS.Length) {
                    throw new ArgumentException("Invalid rank index: " + rank);
                }
                if (suit < 0 || suit > SUITS.Length) {
                    throw new ArgumentException("Invalid suit index: " + suit);
                }
                _data = suit | (rank << RANK_SHIFT);
            }

            public int Rank {
                get {
                    return _data >> RANK_SHIFT;
                }
            }

            public int Suit {
                get {
                    return _data & SUIT_MASK;
                }
            }

            public static List<Card> MakeDeck() {
                List<Card> res = new List<Card>(DECK_SIZE);
                for (int s = 0; s < SUITS.Length; s++) {
                    for (int r = 0; r < RANKS.Length; r++) {
                        res.Add(new Card(r, s));
                    }
                }
                return res;
            }

            public static void Shuffle(List<Card> cards) {
                List<Card> copy = new List<Card>(cards);

                for (int i = 0; i < DECK_SIZE; i++) {
                    for (int j = 0; j < copy.Count; j++) {
                        if (_rnd.NextDouble() <= 1.0 / (copy.Count - j)) {
                            cards[i] = copy[j];
                            copy.RemoveAt(j);
                            break;
                        }
                        Debug.Assert(j != copy.Count - 1);
                    }
                }
            }

            public override string ToString() {
                return "" + RANKS[Rank] + SUITS[Suit];
            }

            #region Operators

            public static bool operator ==(Card x, Card y) {
                return x._data == y._data;
            }

            public static bool operator !=(Card x, Card y) {
                return x._data != y._data;
            }

            // TODO: remove?
            public static bool operator <(Card x, Card y) {
                return x._data < y._data;
            }

            public static bool operator >(Card x, Card y) {
                return x._data > y._data;
            }

            public static bool operator <=(Card x, Card y) {
                return x._data <= y._data;
            }

            public static bool operator >=(Card x, Card y) {
                return x._data >= y._data;
            }

            #endregion

            #region Comparison and Hashing

            public override bool Equals(object obj) {
                if (obj is Card) {
                    return _data == ((Card)obj)._data;
                }
                return false;
            }

            public override int GetHashCode() {
                return Rank * SUITS.Length + Suit;
            }

            public class Equator : IEqualityComparer<Card> {
                private Equator() { }
                public static readonly Equator Instance = new Equator();

                #region IEqualityComparer<Card> Members

                public bool Equals(Card x, Card y) {
                    return x._data == y._data;
                }

                public int GetHashCode(Card obj) {
                    return obj._data;
                }

                #endregion
            }

            // TODO: remove?
            public class Comparer : IComparer<Card> {
                private Comparer() { }
                public static readonly Comparer Instance = new Comparer();

                #region IComparer<Card> Members

                public int Compare(Card x, Card y) {
                    return x._data < y._data ? -1 : x._data > y._data ? 1 : 0;
                }

                #endregion
            }

            #endregion
        }

        internal class Column : IEnumerable<Card> {
            private readonly Card[] _cards;

            public static readonly Column Empty = new Column();

            private Column() {
                _cards = new Card[0];
            }

            // TODO: singleton?
            public Column(Card card) {
                _cards = new Card[] { card };
            }

            internal Column(Card[] cards) {
                if (cards == null) {
                    throw new ArgumentNullException();
                }
                _cards = cards;
            }

            internal Column(List<Card> cards, int index, int length) {
                Debug.Assert(index >= 0);
                Debug.Assert(length >= 0);

                _cards = new Card[length];
                cards.CopyTo(index, _cards, 0, length);
            }

            public int Count {
                get {
                    return _cards.Length;
                }
            }

            public Column Push(Card card) {
                Card[] cards = new Card[_cards.Length + 1];
                cards[0] = card; 
                Array.Copy(_cards, 0, cards, 1, _cards.Length);

                return new Column(cards);
            }

            public Column Pop() {
                if (_cards.Length == 0) {
                    throw new InvalidOperationException("Pop from empty column");
                } else if (_cards.Length == 1) {
                    return Empty;
                } else {
                    Card[] cards = new Card[_cards.Length - 1];
                    Array.Copy(_cards, 1, cards, 0, _cards.Length - 1);
                    return new Column(cards);
                }
            }

            public Card Pop(ref Column column) {
                column = Pop();
                return _cards[0];
            }

            public Card this[int index] {
                get {
                    return _cards[index];
                }
            }

            #region Comparison and Hashing

            public override bool Equals(object obj) {
                Column column = obj as Column;
                if (obj == null || column._cards.Length != _cards.Length) {
                    return false;
                }

                int i = 0;
                foreach (Card card in column._cards) {
                    if (card != _cards[i++]) {
                        return false;
                    }
                }

                return true;
            }

            private int? _hashCache;

            private static void Combine(ref int hash, int value) {
                hash = (hash << 17) ^ (hash << 15) ^ value;
            }

            public override int GetHashCode() {
                if (_hashCache == null) {
                    // order-dependent hash
                    /*
                    int hash = 123456719 + 2000000033 * _cards.Length;
                    foreach (Card card in _cards) {
                        hash = (hash << 17) ^ (hash << 15) ^ card.GetHashCode();
                    }
                     */

                    int hash0 = 123456719;
                    int hash1 = 2000000033;
                    int hash2 = 42042059;

                    for (int i = 0; i < _cards.Length; i++) {
                        int hash = _cards[i].GetHashCode();
                        hash0 ^= _cards[i].GetHashCode();
                        hash1 += _cards[i].GetHashCode();
                        hash2 *= _cards[i].GetHashCode();
                    }

                    Combine(ref hash0, hash1);
                    Combine(ref hash0, hash2);
                    Combine(ref hash0, _cards.Length);
                    _hashCache = hash0;
                }

                return _hashCache.Value;
            }

            public class Equator : IEqualityComparer<Column> {
                private Equator() { }
                public static readonly Equator Instance = new Equator();

                #region IEqualityComparer<Column> Members

                public bool Equals(Column x, Column y) {
                    return x.Equals(y);
                }

                public int GetHashCode(Column obj) {
                    return obj.GetHashCode();
                }

                #endregion
            }

            // TODO: remove?
            public class Comparer : IComparer<Column> {
                private Comparer() { }

                public static readonly Comparer Instance = new Comparer();

                #region IComparer<Column> Members

                public int Compare(Column x, Column y) {
                    if (object.ReferenceEquals(x, y)) {
                        return 0;
                    }
                    if (x == null) {
                        return -1;
                    }
                    if (y == null) {
                        return 1;
                    }

                    if (x.Count != y.Count) {
                        return x.Count < y.Count ? -1 : 1;
                    }

                    int xHash = x.GetHashCode();
                    int yHash = y.GetHashCode();
                    if (x != y) {
                        return xHash < yHash ? -1 : 1;
                    }

                    for (int i = 0; i < x.Count; i++) {
                        if (x._cards[i] != y._cards[i]) {
                            return x._cards[i] < y._cards[i] ? -1 : 1;
                        }
                    }

                    return 0;
                }

                #endregion
            }

            #endregion

            #region IEnumerable<Card> Members

            public IEnumerator<Card> GetEnumerator() {
                foreach (Card card in _cards) {
                    yield return card;
                }
            }

            #endregion

            #region IEnumerable Members

            System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() {
                return _cards.GetEnumerator();
            }

            #endregion
        }

        #endregion

        private readonly int[] _home; // array of rank indices, -1 if slot is empty
        private readonly HashSet<Card> _cells;
        private readonly HashSet<Column> _columns;

        public FreecellNode() {
            List<Card> deck = Card.MakeDeck();
            Card.Shuffle(deck);

            int nCols = Math.Min(COLUMNS, DECK_SIZE);
            Column[] columns = new Column[nCols];
            int index = 0;
            for (int i = 0; i < nCols; i++) {
                int length = DECK_SIZE / nCols;
                if (i < DECK_SIZE % nCols) {
                    length++;
                }
                columns[i] = new Column(deck, index, length);
                index += length;
            }

            _columns = new HashSet<Column>(columns, Column.Equator.Instance);
            _cells = new HashSet<Card>(Card.Equator.Instance);
            _home = MakeHome();
        }

        internal FreecellNode(HashSet<Column> columns) :
            this(columns, new HashSet<Card>(Card.Equator.Instance), MakeHome()) { }

        internal FreecellNode(HashSet<Column> columns, HashSet<Card> cells) :
            this(columns, cells, MakeHome()) { }

        internal FreecellNode(HashSet<Column> columns, HashSet<Card> cells, int[] home) {
            // Sanity checks
            if (columns == null || columns.Count > COLUMNS) {
                throw new ArgumentException("Invalid columns");
            } else if (cells == null || cells.Count > CELLS) {
                throw new ArgumentException("Invalid cells");
            } else if (home == null || home.Length != SUITS.Length) {
                throw new ArgumentException("Invalid home array");
            } else if (columns.Contains(null) || columns.Contains(Column.Empty)) {
                throw new ArgumentException("Null or empty c in set");
            }
            for (int i = 0; i < SUITS.Length; i++) {
                if (home[i] < -1 || home[i] >= RANKS.Length) {
                    throw new ArgumentException("Invalid rank in home: " + home[i]);
                }
            }

            // Make sure each card appears exactly once
            int count = 0;
            HashSet<Card> cards = new HashSet<Card>(Card.Equator.Instance);
            for (int suit = 0; suit < SUITS.Length; suit++) {
                int rank = home[suit];
                for (int r = 0; r <= rank; r++) {
                    cards.Add(new Card(r, suit));
                    count++;
                }
            }
            foreach (Card card in cells) {
                cards.Add(card);
                count++;
            }
            foreach (Column column in columns) {
                foreach (Card card in column) {
                    cards.Add(card);
                    count++;
                }
            }
            if (count > DECK_SIZE) {
                throw new ArgumentException("Invalid columns/cells/home: too many cards");
            } else if (count < DECK_SIZE) {
                throw new ArgumentException("Invalid columns/cells/home: not enough cards");
            } else if (cards.Count < DECK_SIZE) {
                throw new ArgumentException("Invalid columns/cells/home: duplicate cards");
            }

            // Initialize
            _columns = new HashSet<Column>(columns, Column.Equator.Instance);
            _cells = new HashSet<Card>(cells, Card.Equator.Instance);
            _home = new int[SUITS.Length];
            Array.Copy(home, _home, SUITS.Length);
        }

        private FreecellNode(FreecellNode node) {
            _columns = new HashSet<Column>(node._columns, Column.Equator.Instance);
            _cells = new HashSet<Card>(node._cells, Card.Equator.Instance);
            _home = new int[SUITS.Length];
            Array.Copy(node._home, _home, SUITS.Length);
        }

        public FreecellNode Copy() {
            return new FreecellNode(this);
        }

        private static int[] MakeHome() {
            int[] res = new int[SUITS.Length];
            for (int i = 0; i < SUITS.Length; i++) {
                res[i] = -1;
            }
            return res;
        }

        public static FreecellNode Read(string[][] cols) {
            var columns = new HashSet<Column>(Column.Equator.Instance);
            foreach (string[] col in cols) {
                Card[] cards = new Card[col.Length];
                for (int i = col.Length - 1; i >= 0; i--) {
                    if (col[i].Length != 2) {
                        throw new ArgumentException("Invalid card spec: " + col[i]);
                    }
                    cards[col.Length - i - 1] = new Card(
                        RANKS.IndexOf(col[i].ToUpper()[0]),
                        SUITS.IndexOf(col[i].ToUpper()[1])
                    );
                }
                columns.Add(new Column(cards));
            }

            return new FreecellNode(columns);
        }

        #region Hashing and Comparison

        private int? _hashCache = null;

        private static void Combine(ref int hash, int value) {
            hash = value ^ (hash << 9) ^ (hash >> 23);
        }

        public override int GetHashCode() {
            if (_hashCache == null) {
                int hash0 = 123456719;
                int hash1 = 2000000033;
                int hash2 = 42042059;

                foreach (Column c in _columns) {
                    int hash = c.GetHashCode();
                    hash0 ^= c.GetHashCode();
                    hash1 += c.GetHashCode();
                    hash2 *= c.GetHashCode();
                }
                Combine(ref hash0, _columns.Count);
                Combine(ref hash1, _cells.Count);
                Combine(ref hash2, (_cells.Count << 16) | _columns.Count);
                foreach (Card c in _cells) {
                    int hash = c.GetHashCode();
                    hash0 ^= c.GetHashCode();
                    hash1 += c.GetHashCode();
                    hash2 *= c.GetHashCode();
                }
                Combine(ref hash0, hash1);
                Combine(ref hash0, hash2);
                for (int i = 0; i < SUITS.Length; i++) {
                    Combine(ref hash0, _home[i]);
                }

                _hashCache = hash0;

                // order-dependent hash
                /*
                int hash = 42042059;

                foreach (Column c in _columns) {
                    Combine(ref hash, c.GetHashCode());
                }
                Combine(ref hash, _columns.Count);

                foreach (Card card in _cells) {
                    Combine(ref hash, card.GetHashCode());
                }
                Combine(ref hash, _cells.Count);

                for (int i = 0; i < SUITS.Length; i++) {
                    Combine(ref hash, _home[i]);
                }

                _hashCache = hash;
                 */
            }
            return _hashCache.Value;
        }

        public override bool Equals(FreecellNode/*!*/ other) {
            Debug.Assert(other != null);

            if (object.ReferenceEquals(this, other)) {
                return true;
            }

            for (int i = 0; i < SUITS.Length; i++) {
                if (_home[i] != other._home[i]) {
                    return false;
                }
            }

            return _cells.SetEquals(other._cells) && _columns.SetEquals(other._columns);
        }

        #endregion

        #region Node Members

        public override List<FreecellNode> GetChildren() {
            var res = new List<FreecellNode>();

            // Always legal: c -> empty c
            if (_columns.Count < COLUMNS) {
                foreach (Column column in _columns) {
                    Column c = column;
                    Debug.Assert(c.Count > 0);
                    if (c.Count == 1) {
                        // moving a single card to a new c is a no-op
                        continue;
                    }

                    FreecellNode node = Copy();
                    node._columns.Remove(c);
                    node._columns.Add(new Column(c.Pop(ref c)));
                    node._columns.Add(c);
                    node.Cleanup();
                    res.Add(node);
                }
            }

            // Always legal: c -> empty cell
            if (_cells.Count < CELLS) {
                foreach (Column column in _columns) {
                    Column c = column;
                    Debug.Assert(c.Count > 0);

                    FreecellNode node = Copy();
                    node._columns.Remove(c);
                    node._cells.Add(c.Pop(ref c));
                    if (c.Count > 0) { // don't re-add an empty c
                        node._columns.Add(c);
                    }
                    node.Cleanup();
                    res.Add(node);
                }
            }

            // Always legal: cell -> empty c
            if (_columns.Count < COLUMNS) {
                foreach (Card card in _cells) {
                    FreecellNode node = Copy();
                    node._cells.Remove(card);
                    node._columns.Add(new Column(card));
                    node.Cleanup();
                    res.Add(node);
                }
            }

            // c -> home
            foreach (Column column in _columns) {
                Card card = column[0];
                Debug.Assert(_home[card.Suit] < card.Rank);

                if (_home[card.Suit] == card.Rank - 1) {
                    FreecellNode node = Copy();
                    node._columns.Remove(column);
                    if (column.Count > 1) {
                        node._columns.Add(column.Pop());
                    }
                    node._home[card.Suit]++;
                    node.Cleanup();
                    res.Add(node);
                }
            }

            // cell -> home
            foreach (Card card in _cells) {
                Debug.Assert(_home[card.Suit] < card.Rank);

                if (_home[card.Suit] == card.Rank - 1) {
                    FreecellNode node = Copy();
                    node._cells.Remove(card);
                    node._home[card.Suit]++;
                    node.Cleanup();
                    res.Add(node);
                }
            }

            // TODO: move entire stacks
            // c -> c
            foreach (Column from in _columns) {
                foreach (Column to in _columns) {
                    if (object.ReferenceEquals(from, to) ||
                        from[0].Rank != to[0].Rank - 1 ||
                        (from[0].Suit < SUITS.Length / 2 ^
                         to[0].Suit < SUITS.Length / 2)) {
                             continue;
                    }

                    FreecellNode node = Copy();
                    node._columns.Remove(from);
                    node._columns.Remove(to);
                    if (from.Count > 1) {
                        node._columns.Add(from.Pop());
                    }
                    node._columns.Add(to.Push(from[0]));
                    node.Cleanup();
                    res.Add(node);
                }
            }

            // cell -> c
            foreach (Card card in _cells) {
                foreach (Column column in _columns) {
                    if (card.Rank != column[0].Rank - 1 ||
                        (card.Suit < SUITS.Length / 2 ^
                         column[0].Suit < SUITS.Length / 2)) {
                             continue;
                    }

                    FreecellNode node = Copy();
                    node._cells.Remove(card);
                    node._columns.Remove(column);
                    node._columns.Add(column.Push(card));
                    node.Cleanup();
                    res.Add(node);
                }
            }

#if DEBUG
            /*
            Console.WriteLine("The board:");
            Console.WriteLine(this);
            Console.WriteLine("The " + res.Count + " children:");
            int count = 0;
            foreach (FreecellNode node in res) {
                Console.WriteLine(count + "---");
                Console.WriteLine(node);
                count++;
            }
            Console.WriteLine("Press Enter to continue");
            Console.ReadLine();
            */
#endif

            // TODO: heuristic ideas
            //  - proving unsolvability: find min number of moves required in order to
            //    send a card home
            //  - remove cards; if reduced board is unsolvable, whole thing is
            //  - evaluate a board and come up with missions, e.g. "uncover this ace" or
            //    "free up 2 more cells"

            return res;
        }

        /// <summary>
        /// Send appropriate cards to home cells
        /// </summary>
        private void Cleanup() {
            for (; ; ) {
                bool found = false;
                int minRank = _home[0];
                for (int i = 1; i < SUITS.Length; i++) {
                    if (_home[i] < minRank) {
                        minRank = _home[i];
                    }
                }
                // cards up to 2 larger than the lowest-ranked are moved to
                // home cells
                minRank += 2;

                // cell -> home
                foreach (Card card in _cells) {
                    if (card.Rank <= minRank && _home[card.Suit] == card.Rank - 1) {
                        found = true;
                        _cells.Remove(card);
                        _home[card.Suit]++;
                        break;
                    }
                }
                if (found) {
                    continue;
                }

                // c -> home
                foreach (Column column in _columns) {
                    Card card = column[0];
                    if (card.Rank <= minRank && _home[card.Suit] == card.Rank - 1) {
                        found = true;
                        _columns.Remove(column);
                        if (column.Count > 1) {
                            _columns.Add(column.Pop());
                        }
                        _home[card.Suit]++;
                        break;
                    }
                }
                if (!found) {
                    break;
                }
            }

            Debug.Assert(_hashCache == null);
        }

        public override bool IsWinning {
            get {
                for (int i = 0; i < SUITS.Length; i++) {
                    Debug.Assert(_home[i] < RANKS.Length);
                    if (_home[i] != RANKS.Length - 1) {
                        return false;
                    }
                }
                return true;
            }
        }

        //\\// TODO: conflict
        /*
        public override int CompareMoves(FreecellNode x, FreecellNode y) {
            // TODO: average depth of crucial cards (e.g. aces)?
            // TODO: being covered by high cards should be worse than being covered by low cards
            int xSum = 0, ySum = 0;
            for (int s = 0; s < SUITS.Length; s++) {
                xSum += x._home[s];
                ySum += y._home[s];
            }
            // keep sums nonnegative
            xSum += SUITS.Length;
            ySum += SUITS.Length;

            xSum += CELLS - (x._cells.Count) * 10 + (COLUMNS - x._columns.Count) * 13;
            ySum += CELLS - (y._cells.Count) * 10 + (COLUMNS - y._columns.Count) * 13;

            return xSum < ySum ? -1 : xSum > ySum ? 1 : 0;
        }
        */

        public override string ToString() {
            StringBuilder res = new StringBuilder();

            res.Append("Home:");
            for (int i = 0; i < SUITS.Length; i++) {
                res.Append(' ');
                if (_home[i] < 0) {
                    res.Append('_');
                } else {
                    res.Append(RANKS[_home[i]]);
                }
                res.Append(SUITS[i]);
            }
            res.AppendLine();

            res.Append("Free:");
            int j = 0;
            foreach (Card card in _cells) {
                res.Append(' ');
                res.Append(card);
                j++;
            }
            while (j < CELLS) {
                res.Append(" __");
                j++;
            }
            res.AppendLine();

            res.AppendLine("Columns:");
            foreach (Column column in _columns) {
                res.Append(' ');
                for (int i = column.Count - 1; i >= 0; i--) {
                    res.Append(' ');
                    res.Append(column[i]);
                }
                res.AppendLine();
            }

            return res.ToString();
        }

        #endregion
    }
}
