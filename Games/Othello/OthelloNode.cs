//#define VERBOSE_PARAM_SERIALIZATION

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using Solver;

namespace Othello {
    public class OthelloNode : TwoPlayerNode<OthelloNode> {
        // TODO: change to SELF/OTHER and remove turn? change board[] to 2 ulong fields?
        // TODO: Remove Turn and PlayerCount?
        // TODO: replace with enum
        public const int BLACK = 0;
        public const int WHITE = 1;

        private ulong[] board = new ulong[2];
        private int turn;

        // True if the previous player's move was a pass.
        public bool Pass {
            get;
            private set;
        }

        public override int Turn {
            get { return this.turn; }
        }

        public ulong BlackBoard {
            get { return this.board[BLACK]; }
        }

        public ulong WhiteBoard {
            get { return this.board[WHITE]; }
        }

        public ulong PlayerBoard {
            get { return this.board[this.turn]; }
        }

        public ulong OtherBoard {
            get { return this.board[(this.turn + 1) & 1]; }
        }

        public ulong Occupied {
            get { return this.board[BLACK] | this.board[WHITE]; }
        }

        private OthelloNode(int turn, ulong self, ulong other, bool pass = false) {
            this.turn = turn;
            this.board[turn] = self;
            this.board[(turn + 1) & 1] = other;
            this.Pass = pass;
        }

        public OthelloNode()
            : this(
                BLACK,
                Square[4, 3] | Square[3, 4],
                Square[3, 3] | Square[4, 4]) {
        }

        // TODO: might need to expand API to tell whose point of view this is from, not
        // to mention an evaluation function that maps to a number, not just a bool.
        public override bool IsGameOver {
            get {
                // TODO: make this more efficient with caching - add public property ChildCount
                return this.OccupiedSquareCount == 64 || (this.Pass && this.GetChildren().Count == 0);
            }
        }

        // The number of pieces Black is winning by.
        public int Score {
            get {
                return BitCount(this.board[BLACK]) - BitCount(this.board[WHITE]);
            }
        }

        public int OccupiedSquareCount {
            get {
                return BitCount(this.Occupied);
            }
        }

        public int EmptySquareCount {
            get {
                return BitCount(~this.Occupied);
            }
        }

        #region Bitmasks

        // The board is arranged like so:
        //
        // \i---->
        // j 00 01 02 03 04 05 06 07
        // | 08 09 10 11 12 13 14 15
        // | 16 17 18 19 20 21 22 23
        // v 24 25 26 27 28 29 30 31
        //   32 33 34 35 36 37 38 39
        //   40 41 42 43 44 45 46 47
        //   48 49 50 51 52 53 54 55
        //   56 57 58 59 60 61 62 63
        //
        // The square's index is equivalent to a one-bit's distance from the least-significant
        // position in a 64-bit int. e.g. square 57, or (1,7), is occupied by white if:
        //     board[1] & (1ul << 57) != 0

        public static readonly ulong[,] Square;
        public static readonly ulong[,] Adjacent;

        public const ulong Corners = (1ul << 63) | (1ul << 56) | (1ul << 7) | 1ul;

        static OthelloNode() {
            #region Initialize basic masks

            Square = new ulong[8, 8];
            for (int i = 0; i < 8; i++) {
                for (int j = 0; j < 8; j++) {
                    Square[i, j] = 1ul << (j * 8 + i);
                }
            }

            Adjacent = new ulong[8, 8];
            for (int i = 0; i < 8; i++) {
                for (int j = 0; j < 8; j++) {
                    ulong value = 0ul;
                    if (i > 0) { // left
                        value |= Square[i - 1, j];
                        if (j > 0) { // up-left
                            value |= Square[i - 1, j - 1];
                        }
                        if (j < 7) { // down-left
                            value |= Square[i - 1, j + 1];
                        }
                    }
                    if (i < 7) { // right
                        value |= Square[i + 1, j];
                        if (j > 0) { // up-right
                            value |= Square[i + 1, j - 1];
                        }
                        if (j < 7) { // down-right
                            value |= Square[i + 1, j + 1];
                        }
                    }
                    if (j > 0) { // up
                        value |= Square[i, j - 1];
                    }
                    if (j < 7) { // down
                        value |= Square[i, j + 1];
                    }

                    Adjacent[i, j] = value;
                }
            }

            #endregion

            #region Initialize patern masks

            Row = new ulong[6];
            Column = new ulong[6];
            for (int i = 1; i < 7; i++) {
                ulong row = 0ul;
                ulong column = 0ul;
                for (int j = 0; j < 8; j++) {
                    row |= Square[j, i];
                    column |= Square[i, j];
                }

                Row[i - 1] = row;
                Column[i - 1] = column;
            }

            HorizEdge = new ulong[2];
            VertEdge = new ulong[2];
            for (int i = 0; i < 8; i++) {
                HorizEdge[0] |= Square[i, 0];
                HorizEdge[1] |= Square[i, 7];
                VertEdge[0] |= Square[0, i];
                VertEdge[1] |= Square[7, i];
            }
            HorizEdge[0] |= Square[1, 1] | Square[6, 1];
            HorizEdge[1] |= Square[1, 6] | Square[6, 6];
            VertEdge[0] |= Square[1, 1] | Square[1, 6];
            VertEdge[1] |= Square[6, 1] | Square[6, 6];

            DownLeft = new ulong[9];
            DownRight = new ulong[9];
            for (int i = 3; i < 7; i++) {
                DownLeft[i - 3] = GenerateDownLeftAt(i, 0);
                DownRight[i - 3] = GenerateDownRightAt(7 - i, 0);
                DownLeft[i + 2] = GenerateDownLeftAt(7, i - 2);
                DownRight[i + 2] = GenerateDownRightAt(0, i - 2);
            }
            DownLeft[4] = GenerateDownLeftAt(7, 0);
            DownRight[4] = GenerateDownRightAt(0, 0);

            Corner33 = new ulong[4];
            for (int i = 0; i < 3; i++) {
                for (int j = 0; j < 3; j++) {
                    Corner33[0] |= Square[i, j];
                    Corner33[1] |= Square[7 - i, j];
                    Corner33[2] |= Square[i, 7 - j];
                    Corner33[3] |= Square[7 - i, 7 - j];
                }
            }

            Corner52Cw = new ulong[4];
            Corner52Ccw = new ulong[4];
            for (int i = 0; i < 5; i++) {
                for (int j = 0; j < 2; j++) {
                    Corner52Cw[0] |= Square[i, j];
                    Corner52Ccw[0] |= Square[j, i];

                    Corner52Ccw[1] |= Square[i, j + 6];
                    Corner52Cw[1] |= Square[j + 6, i];

                    Corner52Cw[2] |= Square[i + 3, j + 6];
                    Corner52Ccw[2] |= Square[j + 6, i + 3];

                    Corner52Ccw[3] |= Square[i + 3, j];
                    Corner52Cw[3] |= Square[j, i + 3];
                }
            }

            #endregion

            // TODO: Get rid of above patterns and hard-code just a few pattern classes here.
            // TODO: update comment
            // Store non-overlapping groups of patterns. Used for fast board evaluation.
            // TODO: flaw: Scores for pattern valuations with all zeros are shared among patterns. So
            //       if one of the ulongs we use for lookup is 0, we can't tell which pattern we're
            //       looking up a score for, which also conflates these patterns during training.
            //\\//PatternSets = new ulong[][] {
            ulong[][] patternSets = new ulong[][] {
                Row,
                Column,
                HorizEdge,
                VertEdge,
                //DownLeft,
                //DownRight,
                Corner33,
                Corner52Cw,
                Corner52Ccw,
                new ulong[] {
                    DownLeft[0] | DownLeft[1],
                    DownLeft[2],
                    DownLeft[3],
                    DownLeft[4]
                }
            };

            // Store classes of patterns that are equal modulo board symmetries. Used for training
            // and serialization.
            List<ulong> patternClassGenerators = new List<ulong>();
            foreach (ulong[] patternSet in patternSets) {
                foreach (ulong pattern in patternSet) {
                    bool found = false;
                    BoardSymmetries sym = GetBoardSymmetries(pattern, 0);
                    for (int s = 0; s < Transforms.Length; s++) {
                        sym.GetPair(s, out ulong self, out _);
                        if (patternClassGenerators.Contains(self)) {
                            found = true;
                            break;
                        }
                    }

                    if (!found) {
                        patternClassGenerators.Add(pattern);
                    }
                }
            }

            PatternClasses = new ulong[patternClassGenerators.Count][];
            for (int i = 0; i < PatternClasses.Length; i++) {
                List<ulong> patternClass = new List<ulong>(8);
                BoardSymmetries sym = GetBoardSymmetries(patternClassGenerators[i], 0);
                for (int s = 0; s < Transforms.Length; s++) {
                    sym.GetPair(s, out ulong self, out _);
                    if (!patternClass.Contains(self)) {
                        patternClass.Add(self);
                    }
                }

                PatternClasses[i] = patternClass.ToArray();
            }

            PatternClassScores = new Dictionary<PatternClassKey, HeuristicData>[PatternClasses.Length];
            for (int i = 0; i < PatternClassScores.Length; i++) {
                PatternClassScores[i] = new Dictionary<PatternClassKey, HeuristicData>();
            }
            PatternClassWeights = new double[PatternClasses.Length, NumGameStages];

            // Store all the pattern masks and associated scores. Used for fast board evaluation.
            List<ulong> patterns = new List<ulong>();
            List<int> patternClassIndices = new List<int>();
            List<int> patternTransformIndices = new List<int>();
            for (int i = 0; i < PatternClasses.Length; i++) {
                foreach (ulong pattern in PatternClasses[i]) {
                    int transformIndex;
                    if (!IsIsomorphic(PatternClasses[i][0], pattern, out transformIndex)) {
                        throw new InvalidDataException();
                    }

                    patterns.Add(pattern);
                    patternClassIndices.Add(i);
                    patternTransformIndices.Add(transformIndex);
                }
            }
            Patterns = patterns.ToArray();
            PatternClassIndices = patternClassIndices.ToArray();
            PatternTransformIndices = patternTransformIndices.ToArray();

            PatternScores = new Dictionary<PatternClassKey, HeuristicData>[Patterns.Length];
            for (int i = 0; i < PatternScores.Length; i++) {
                PatternScores[i] = new Dictionary<PatternClassKey, HeuristicData>();
            }

            // TODO: these are equally weighted. Measure how well they correlate with victory and tune
            //       weights accordingly. Also find a weight with respect to Pattern evaluation
            Features = new Func<OthelloNode, int>[] {
                node => node.PieceCountSpread(),
                node => node.CornerSpread(),
                node => node.StablePieceSpread(),
                node => node.FrontierSpread(),
                node => node.PotentialMobilitySpread()
            };
            FeatureNames = new string[] {
                "Piece Count",
                "Corner Squares",
                "Stable Pieces",
                "Frontier Squares",
                "Potential Mobility"
            };

            // TODO: add interpolation to training function
            FeatureScores = new Dictionary<FeatureKey, HeuristicData>[Features.Length];
            for (int i = 0; i < Features.Length; i++) {
                FeatureScores[i] = new Dictionary<FeatureKey, HeuristicData>();
            }

            // Initialize all the pattern and feature weights to 1, calculating Patterns and PatternScores accordingly.
            InitializeWeights();

            // Create the initial playbook.
            Playbook = new OthelloPlaybook(new OthelloNode());
        }

        private static ulong GenerateDownLeftAt(int iStart, int jStart) {
            ulong result = 0ul;
            for (int i = iStart, j = jStart; i >= 0 && j < 8; i--, j++) {
                result |= Square[i, j];
            }

            return result;
        }

        private static ulong GenerateDownRightAt(int iStart, int jStart) {
            ulong result = 0ul;
            for (int i = iStart, j = jStart; i < 8 && j < 8; i++, j++) {
                result |= Square[i, j];
            }

            return result;
        }

        // TODO: Remove PatternClassWeights and encode weight via HeuristicData's score
        private static void InitializeWeights() {
            for (int i = 0; i < PatternClasses.Length; i++) {
                for (int stage = 0; stage < NumGameStages; stage++) {
                    PatternClassWeights[i, stage] = 1.0;
                }
            }

            // Recalculate the pattern scores to incorporate the new weights.
            CalculatePatternScores();
        }

        private static void CalculatePatternScores() {
            // TODO: temporarily unused
        }

        private static void _CalculatePatternScores() {
            for (int i = 0; i < Patterns.Length; i++) {
                int patternClass = PatternClassIndices[i];
                Dictionary<PatternClassKey, HeuristicData> source = PatternClassScores[patternClass];
                Dictionary<PatternClassKey, HeuristicData> dest = PatternScores[i];
                Func<ulong, ulong> transform = Transforms[PatternTransformIndices[i]];

                dest.Clear();
                // Because our board evaluator adds one score for each pattern in the class, we must divide
                // by the cardinality of the class in order to un-bias the weights.
                foreach (var kvp in source) {
                    double weight = PatternClassWeights[patternClass, GameStage(kvp.Key.PieceCount)] / PatternClasses[patternClass].Length;
                    HeuristicData data = kvp.Value;
                    data.Score = (int)Math.Round(data.CalculateScore() * weight);
                    dest[new PatternClassKey(kvp.Key.PieceCount, transform(kvp.Key.Self), transform(kvp.Key.Other))] = data;
                }
            }

            GC.Collect();
        }

        #endregion

        #region Board Transforms

        //\\// TODO: replace '8' with Transforms.Length
        public static readonly Func<ulong, ulong>[] Transforms = new Func<ulong, ulong>[] {
            x => x,
            RotateRight,
            Rotate180,
            RotateLeft,
            FlipHoriz,
            FlipVert,
            FlipDiag0,
            FlipDiag1
        };

        public static readonly Func<ulong, ulong>[] InverseTransforms = new Func<ulong, ulong>[] {
            x => x,
            RotateLeft,
            Rotate180,
            RotateRight,
            FlipHoriz,
            FlipVert,
            FlipDiag0,
            FlipDiag1
        };

        public static IEnumerable<ulong> GetSymmetries(ulong board) {
            for (int i = 0; i < Transforms.Length; i++) {
                yield return Transforms[i](board);
            }
        }

        public static IEnumerable<KeyValuePair<ulong, ulong>> GetSymmetries(ulong selfBoard, ulong otherBoard) {
            for (int i = 0; i < Transforms.Length; i++) {
                yield return new KeyValuePair<ulong, ulong>(Transforms[i](selfBoard), Transforms[i](otherBoard));
            }
        }

        /// <summary>
        /// Fixed-size buffer holding all 8 board symmetry pairs. Stack-allocated to avoid
        /// iterator/heap overhead in hot paths (PatternScoreSlow, TrainSingle, UnknownPatterns).
        /// </summary>
        public struct BoardSymmetries {
            public ulong Self0, Other0;
            public ulong Self1, Other1;
            public ulong Self2, Other2;
            public ulong Self3, Other3;
            public ulong Self4, Other4;
            public ulong Self5, Other5;
            public ulong Self6, Other6;
            public ulong Self7, Other7;

            public void GetPair(int index, out ulong self, out ulong other) {
                switch (index) {
                    case 0: self = Self0; other = Other0; break;
                    case 1: self = Self1; other = Other1; break;
                    case 2: self = Self2; other = Other2; break;
                    case 3: self = Self3; other = Other3; break;
                    case 4: self = Self4; other = Other4; break;
                    case 5: self = Self5; other = Other5; break;
                    case 6: self = Self6; other = Other6; break;
                    case 7: self = Self7; other = Other7; break;
                    default: throw new ArgumentOutOfRangeException(nameof(index));
                }
            }
        }

        public static BoardSymmetries GetBoardSymmetries(ulong selfBoard, ulong otherBoard) {
            BoardSymmetries result;
            result.Self0 = Transforms[0](selfBoard); result.Other0 = Transforms[0](otherBoard);
            result.Self1 = Transforms[1](selfBoard); result.Other1 = Transforms[1](otherBoard);
            result.Self2 = Transforms[2](selfBoard); result.Other2 = Transforms[2](otherBoard);
            result.Self3 = Transforms[3](selfBoard); result.Other3 = Transforms[3](otherBoard);
            result.Self4 = Transforms[4](selfBoard); result.Other4 = Transforms[4](otherBoard);
            result.Self5 = Transforms[5](selfBoard); result.Other5 = Transforms[5](otherBoard);
            result.Self6 = Transforms[6](selfBoard); result.Other6 = Transforms[6](otherBoard);
            result.Self7 = Transforms[7](selfBoard); result.Other7 = Transforms[7](otherBoard);
            return result;
        }

        public static OthelloNode Canonicalize(OthelloNode node) {
            BoardSymmetries sym = GetBoardSymmetries(node.PlayerBoard, node.OtherBoard);
            ulong minSelf = node.PlayerBoard, minOther = node.OtherBoard;
            for (int s = 1; s < Transforms.Length; s++) {
                sym.GetPair(s, out ulong self, out ulong other);
                if (self < minSelf || (self == minSelf && other < minOther)) {
                    minSelf = self;
                    minOther = other;
                }
            }
            return new OthelloNode(node.Turn, minSelf, minOther, node.Pass);
        }

        public static IEnumerable<OthelloNode> GetSymmetries(OthelloNode node) {
            BoardSymmetries sym = GetBoardSymmetries(node.PlayerBoard, node.OtherBoard);
            for (int s = 0; s < Transforms.Length; s++) {
                sym.GetPair(s, out ulong self, out ulong other);
                yield return new OthelloNode(node.Turn, self, other, node.Pass);
            }
        }

        public static bool IsIsomorphic(ulong self, ulong other, out int transformIndex) {
            BoardSymmetries sym = GetBoardSymmetries(self, 0);
            for (int i = 0; i < Transforms.Length; i++) {
                sym.GetPair(i, out ulong transformed, out _);
                if (transformed == other) {
                    transformIndex = i;
                    return true;
                }
            }

            transformIndex = 0;
            return false;
        }

        public bool IsIsomorphic(OthelloNode node) {
            BoardSymmetries sym = GetBoardSymmetries(this.PlayerBoard, this.OtherBoard);
            for (int s = 0; s < Transforms.Length; s++) {
                sym.GetPair(s, out ulong self, out ulong other);
                if (self == node.PlayerBoard && other == node.OtherBoard) {
                    return true;
                }
            }
            return false;
        }

        public bool IsIsomorphicParent(OthelloNode child) {
            if (this.IsGameOver) return false;
            foreach (OthelloNode sibling in this.GetChildren()) {
                BoardSymmetries sym = GetBoardSymmetries(sibling.PlayerBoard, sibling.OtherBoard);
                for (int s = 0; s < Transforms.Length; s++) {
                    sym.GetPair(s, out ulong self, out ulong other);
                    if (self == child.PlayerBoard && other == child.OtherBoard) {
                        return true;
                    }
                }
            }
            return false;
        }

        private static ulong RotateRight(ulong board) {
            ulong result = 0ul;
            for (int i = 0; i < 8; i++) {
                for (int j = 0; j < 8; j++) {
                    if ((board & Square[i, j]) != 0) {
                        result |= Square[7 - j, i];
                    }
                }
            }
            return result;
        }

        private static ulong RotateLeft(ulong board) {
            ulong result = 0ul;
            for (int i = 0; i < 8; i++) {
                for (int j = 0; j < 8; j++) {
                    if ((board & Square[i, j]) != 0) {
                        result |= Square[j, 7 - i];
                    }
                }
            }
            return result;
        }

        private static ulong Rotate180(ulong board) {
            ulong result = 0ul;
            for (int i = 0; i < 8; i++) {
                for (int j = 0; j < 8; j++) {
                    if ((board & Square[i, j]) != 0) {
                        result |= Square[7 - i, 7 - j];
                    }// 1,7 --> 6,0
                }
            }
            return result;
        }

        private static ulong FlipHoriz(ulong board) {
            ulong result = 0ul;
            for (int i = 0; i < 8; i++) {
                for (int j = 0; j < 8; j++) {
                    if ((board & Square[i, j]) != 0) {
                        result |= Square[7 - i, j];
                    }
                }
            }
            return result;
        }

        private static ulong FlipVert(ulong board) {
            ulong result = 0ul;
            for (int i = 0; i < 8; i++) {
                for (int j = 0; j < 8; j++) {
                    if ((board & Square[i, j]) != 0) {
                        result |= Square[i, 7 - j];
                    }
                }
            }
            return result;
        }

        private static ulong FlipDiag0(ulong board) {
            ulong result = 0ul;
            for (int i = 0; i < 8; i++) {
                for (int j = 0; j < 8; j++) {
                    if ((board & Square[i, j]) != 0) {
                        result |= Square[j, i];
                    }
                }
            }
            return result;
        }

        private static ulong FlipDiag1(ulong board) {
            ulong result = 0ul;
            for (int i = 0; i < 8; i++) {
                for (int j = 0; j < 8; j++) {
                    if ((board & Square[i, j]) != 0) {
                        result |= Square[7 - j, 7 - i];
                    }
                }
            }
            return result;
        }

        #endregion

        #region Hashing and Equality

        public override bool Equals(OthelloNode other) {
            return this.turn == other.turn &&
                this.board[BLACK] == other.board[BLACK] &&
                this.board[WHITE] == other.board[WHITE];
        }

        /*
        private static readonly ulong[] HashSeed = new ulong[] {
            5678901257ul * 1234567811ul,
            4567890121ul * 987654337ul
        };

        // Note: we only need the rightmost 32 bits produced by this function.
        private static ulong HashULong(ulong key) {
            unchecked {
                key = (~key) + (key << 18); // key = (key << 18) - key - 1;
                key = key ^ (key >> 31);
                key = key * 21; // key = (key + (key << 2)) + (key << 4);
                key = key ^ (key >> 11);
                key = key + (key << 6);
                key = key ^ (key >> 22);
            }

            return key;
        }

        public override int GetHashCode() {
            return unchecked((int)HashULong(
                (HashULong(HashSeed[this.turn] ^ this.board[BLACK]) << 32) |
                (HashULong(this.board[WHITE]) & 0x00000000fffffffful) ));
        }
        */

        private static readonly uint[] HashSeed = new uint[] {
            1234567811u,
            3456789023u
        };

        private static uint Mix(uint a, uint b, uint c) {
            unchecked {
                a -= b; a -= c; a ^= (c >> 13);
                b -= c; b -= a; b ^= (a << 8);
                c -= a; c -= b; c ^= (b >> 13);
                a -= b; a -= c; a ^= (c >> 12);
                b -= c; b -= a; b ^= (a << 16);
                c -= a; c -= b; c ^= (b >> 5);
                a -= b; a -= c; a ^= (c >> 3);
                b -= c; b -= a; b ^= (a << 10);
                c -= a; c -= b; c ^= (b >> 15);
            }
            return c;
        }

        private static uint HashULong(ulong key) {
            unchecked {
                key = (~key) + (key << 18); // key = (key << 18) - key - 1;
                key = key ^ (key >> 31);
                key = key * 21; // key = (key + (key << 2)) + (key << 4);
                key = key ^ (key >> 11);
                key = key + (key << 6);
                key = key ^ (key >> 22);
            }

            return (uint)key;
        }

        public override int GetHashCode() {
            return unchecked((int)Mix(
                HashSeed[this.turn],
                HashULong(this.board[BLACK]),
                HashULong(this.board[WHITE])));
        }

        #endregion

        #region Move Calculations

        public static int BitCount(ulong value) {
            ulong count = value -
                ((value >> 1) & 0x7777777777777777ul) -
                ((value >> 2) & 0x3333333333333333ul) -
                ((value >> 3) & 0x1111111111111111ul);

            count += count >> 4;
            count &= 0x0f0f0f0f0f0f0f0f;
            count %= 255;
            return unchecked((int)count);
        }

        public override void GetChildren(List<OthelloNode> children) {
            children.Clear();

            ulong self = this.board[this.turn];
            ulong other = this.board[(this.turn + 1) & 1];
            ulong occupied = self | other;

            for (int jStart = 0; jStart < 8; jStart++) {
                for (int iStart = 0; iStart < 8; iStart++) {
                    // Make sure the current square is unoccupied and at least one adjacent square is
                    // occupied by the opponent.
                    if ((Square[iStart, jStart] & occupied) != 0 ||
                        (Adjacent[iStart, jStart] & other) == 0) {
                        continue;
                    }

                    // Search left
                    int i, j;
                    ulong square;
                    ulong allTraces = 0ul;
                    ulong trace = 0ul;
                    for (i = iStart - 1, j = jStart;
                        i >= 0 && (other & (square = Square[i, j])) != 0;
                        i--) {
                        trace |= square;
                    }
                    if (trace != 0 && i >= 0 && (self & (square = Square[i, j])) != 0) {
                        allTraces |= trace | square;
                    }

                    // Search up-left
                    trace = 0ul;
                    for (i = iStart - 1, j = jStart - 1;
                        i >= 0 && j >= 0 && (other & (square = Square[i, j])) != 0;
                        i--, j--) {
                        trace |= square;
                    }
                    if (trace != 0 && i >= 0 && j >= 0 && (self & (square = Square[i, j])) != 0) {
                        allTraces |= trace | square;
                    }

                    // Search down-left
                    trace = 0ul;
                    for (i = iStart - 1, j = jStart + 1;
                        i >= 0 && j < 8 && (other & (square = Square[i, j])) != 0;
                        i--, j++) {
                        trace |= square;
                    }
                    if (trace != 0 && i >= 0 && j < 8 && (self & (square = Square[i, j])) != 0) {
                        allTraces |= trace | square;
                    }

                    // Search right
                    trace = 0ul;
                    for (i = iStart + 1, j = jStart;
                        i < 8 && (other & (square = Square[i, j])) != 0;
                        i++) {
                        trace |= square;
                    }
                    if (trace != 0 && i < 8 && (self & (square = Square[i, j])) != 0) {
                        allTraces |= trace | square;
                    }

                    // Search up-right
                    trace = 0ul;
                    for (i = iStart + 1, j = jStart - 1;
                        i < 8 && j >= 0 && (other & (square = Square[i, j])) != 0;
                        i++, j--) {
                        trace |= square;
                    }
                    if (trace != 0 && i < 8 && j >= 0 && (self & (square = Square[i, j])) != 0) {
                        allTraces |= trace | square;
                    }

                    // Search down-right
                    trace = 0ul;
                    for (i = iStart + 1, j = jStart + 1;
                        i < 8 && j < 8 && (other & (square = Square[i, j])) != 0;
                        i++, j++) {
                        trace |= square;
                    }
                    if (trace != 0 && i < 8 && j < 8 && (self & (square = Square[i, j])) != 0) {
                        allTraces |= trace | square;
                    }

                    // Search up
                    trace = 0ul;
                    for (i = iStart, j = jStart - 1;
                        j >= 0 && (other & (square = Square[i, j])) != 0;
                        j--) {
                        trace |= square;
                    }
                    if (trace != 0 && j >= 0 && (self & (square = Square[i, j])) != 0) {
                        allTraces |= trace | square;
                    }

                    // Search down
                    trace = 0ul;
                    for (i = iStart, j = jStart + 1;
                        j < 8 && (other & (square = Square[i, j])) != 0;
                        j++) {
                        trace |= square;
                    }
                    if (trace != 0 && j < 8 && (self & (square = Square[i, j])) != 0) {
                        allTraces |= trace | square;
                    }

                    // If we've flipped any pieces, we have a legal move.
                    if (allTraces != 0) {
                        allTraces |= Square[iStart, jStart];
                        children.Add(
                            new OthelloNode(
                                (this.turn + 1) & 1,
                                other & ~allTraces,
                                self | allTraces));
                    }
                }
            }

            // If there are no legal moves, we're forced to pass. If the previous
            // turn was a pass, then the game is over.
            if (!this.Pass && children.Count == 0) {
                children.Add(
                    new OthelloNode(
                        (this.turn + 1) & 1,
                        this.board[(this.turn + 1) & 1],
                        this.board[this.turn],
                        pass: true));
            }
        }

        #endregion

        #region Heuristics

        public static int Eval0(OthelloNode node) {
            return 2 * node.PotentialMobilitySpread()
                - node.FrontierSpread()
                + 6 * node.CornerSpread()
                + 4 * node.StablePieceSpread();
        }

        public static int Eval1(OthelloNode node) {
            return 2 * node.PotentialMobilitySpread()
                - node.FrontierSpread()
                + 6 * node.CornerSpread()
                + 4 * node.StablePieceSpread();
        }

        public int PieceCountSpread() {
            return BitCount(this.board[this.turn]) - BitCount(this.board[(this.turn + 1) & 1]);
        }

        public int CornerSpread() {
            ulong self = this.board[this.turn];
            ulong other = this.board[(this.turn + 1) & 1];

            return BitCount(self & Corners) - BitCount(other & Corners);
        }

        // TODO: merge some of these functions so that we only iterate once
        public int Frontiers() {
            ulong self = this.board[this.turn];
            ulong occupied = self | this.board[(this.turn + 1) & 1];

            int total = 0;

            for (int j = 0; j < 8; j++) {
                for (int i = 0; i < 8; i++) {
                    ulong square = Square[i, j];
                    ulong adjacent = Adjacent[i, j];
                    if ((self & square) != 0 &&
                        (adjacent & occupied) != adjacent) {
                        total++;
                    }
                }
            }

            return total;
        }

        public int FrontierSpread() {
            ulong self = this.board[this.turn];
            ulong other = this.board[(this.turn + 1) & 1];
            ulong occupied = self | other;

            int total = 0;

            for (int j = 0; j < 8; j++) {
                for (int i = 0; i < 8; i++) {
                    ulong square = Square[i, j];
                    ulong adjacent = Adjacent[i, j];
                    if ((adjacent & occupied) != adjacent) {
                        if ((self & square) != 0) {
                            total++;
                        } else if ((other & square) != 0) {
                            total--;
                        }
                    }
                }
            }

            return total;
        }

        public static int GetStablePieces(ulong self) {
            int i, j;
            ulong stable = self & Corners;

            // Search left-to-right, top-to-bottom
            for (i = 0; i < 8; i++) {
                ulong square = Square[i, 0];
                if ((square & self) == 0) {
                    break;
                }
                stable |= square;
            }
            if (i > 0) {
                for (j = 1; j < 8; j++) {
                    for (i = 0; i < 7; i++) {
                        ulong square;
                        if ((Square[i + 1, j - 1] & self) == 0 ||
                            ((square = Square[i, j]) & self) == 0) {
                            break;
                        }
                        stable |= square;
                    }
                    if (i == 0) {
                        break;
                    }
                }
            }

            // Search right-to-left, top-to-bottom
            for (i = 7; i >= 0; i--) {
                ulong square = Square[i, 0];
                if ((square & self) == 0) {
                    break;
                }
                stable &= square;
            }
            if (i < 7) {
                for (j = 1; j < 8; j++) {
                    for (i = 7; i > 0; i--) {
                        ulong square;
                        if ((Square[i - 1, j - 1] & self) == 0 ||
                            ((square = Square[i, j]) & self) == 0) {
                            break;
                        }
                        stable |= square;
                    }
                    if (i == 7) {
                        break;
                    }
                }
            }

            // Search left-to-right, bottom-to-top
            for (i = 0; i < 8; i++) {
                ulong square = Square[i, 7];
                if ((square & self) == 0) {
                    break;
                }
                stable |= square;
            }
            if (i > 0) {
                for (j = 6; j >= 0; j--) {
                    for (i = 0; i < 7; i++) {
                        ulong square;
                        if ((Square[i + 1, j + 1] & self) == 0 ||
                            ((square = Square[i, j]) & self) == 0) {
                            break;
                        }
                        stable |= square;
                    }
                    if (i == 0) {
                        break;
                    }
                }
            }

            // Search right-to-left, bottom-to-top
            for (i = 7; i >= 0; i--) {
                ulong square = Square[i, 0];
                if ((square & self) == 0) {
                    break;
                }
                stable &= square;
            }
            if (i < 7) {
                for (j = 1; j < 8; j++) {
                    for (i = 7; i > 0; i--) {
                        ulong square;
                        if ((Square[i - 1, j - 1] & self) == 0 ||
                            ((square = Square[i, j]) & self) == 0) {
                            break;
                        }
                        stable |= square;
                    }
                    if (i == 7) {
                        break;
                    }
                }
            }

            return BitCount(stable);
        }

        public int StablePieceSpread() {
            return GetStablePieces(this.PlayerBoard) - GetStablePieces(this.OtherBoard);
        }

        // Potential mobility is the number of empty squares next to an opponent's piece. It provides an
        // approximation for mobility, but at a smaller performance cost.
        public int PotentialMobility() {
            ulong other = this.board[(this.turn + 1) & 1]; // TODO: replace with property accessors everywhere
            ulong occupied = other | this.board[this.turn];

            int total = 0;
            for (int j = 0; j < 8; j++) {
                for (int i = 0; i < 8; i++) {
                    if ((Square[i, j] & occupied) == 0 &&
                        (other & Adjacent[i, j]) != 0) {
                        total++;
                    }
                }
            }

            return total;
        }

        // Potential mobility is the number of empty squares next to an opponent's piece. It provides an
        // approximation for mobility, but at a smaller performance cost.
        public int PotentialMobilitySpread() {
            ulong self = this.board[this.turn];
            ulong other = this.board[(this.turn + 1) & 1];
            ulong occupied = self | other;

            int total = 0;
            for (int j = 0; j < 8; j++) {
                for (int i = 0; i < 8; i++) {
                    if ((Square[i, j] & occupied) == 0) {
                        ulong adjacent = Adjacent[i, j];
                        if ((other & adjacent) != 0) {
                            total++;
                        }
                        if ((self & adjacent) != 0) {
                            total--;
                        }
                    }
                }
            }

            return total;
        }

        public int MonteCarlo(int iters) {
            Random random = new Random();
            int totalScore = 0;
            List<OthelloNode> children = this.GetChildren();
            List<OthelloNode> currentChildren = new List<OthelloNode>();
            for (int i = 0; i < iters; i++) {
                OthelloNode current = this;
                // TODO: not needed once we cache result of GetChildren
                currentChildren.Clear();
                foreach (OthelloNode child in children) {
                    currentChildren.Add(child);
                }

                while (currentChildren.Count > 0) {
                    current = currentChildren[random.Next(currentChildren.Count)];
                    current.GetChildren(currentChildren);
                }

                totalScore += current.Score;
            }

            return this.turn == BLACK ? totalScore : -totalScore;
        }

        #endregion

        #region Pattern-based Evaluation
        
        public static readonly ulong[] Row;
        public static readonly ulong[] Column;
        public static readonly ulong[] HorizEdge;
        public static readonly ulong[] VertEdge;
        public static readonly ulong[] DownLeft;
        public static readonly ulong[] DownRight;
        public static readonly ulong[] Corner33;
        public static readonly ulong[] Corner52Cw;
        public static readonly ulong[] Corner52Ccw;

        // TODO: rename these? (also Patterns and PatternScores). Search for "PatternSets" and "pattern sets" and delete.
        public static readonly ulong[][] PatternClasses;
        private static readonly Dictionary<PatternClassKey, HeuristicData>[] PatternClassScores;
        private static readonly double[,] PatternClassWeights;
        private const int GameStageShift = 2;
        private const int NumGameStages = 64 >> GameStageShift;
        public static int GameStage(int pieceCount) => (pieceCount - 1) >> GameStageShift;
        public static int PieceCountMin(int stage) => (stage << GameStageShift) + 1;
        public static int PieceCountMax(int stage) => Math.Min(64, (stage + 1) << GameStageShift);

        private static readonly ulong[] Patterns;
        private static readonly Dictionary<PatternClassKey, HeuristicData>[] PatternScores;

        // Information for mapping between Patterns and PatternClasses
        private static readonly int[] PatternClassIndices;
        private static readonly int[] PatternTransformIndices;

        // Additional features
        public static readonly Func<OthelloNode, int>[] Features;
        public static readonly string[] FeatureNames;
        private static readonly Dictionary<FeatureKey, HeuristicData>[] FeatureScores;
        // TODO: add feature weights
        // TODO: abstract pattern-eval score as a feature? unify with pattern eval features?

        private readonly struct FeatureKey(int pieceCount, int value) : IEquatable<FeatureKey> {
            public readonly int PieceCount = pieceCount;
            public readonly int Value = value;

            public bool Equals(FeatureKey other) =>
                PieceCount == other.PieceCount && Value == other.Value;

            public override bool Equals(object obj) => obj is FeatureKey k && Equals(k);

            public override int GetHashCode() => HashCode.Combine(PieceCount, Value);
        }

        private readonly struct PatternClassKey(int pieceCount, ulong self, ulong other) : IEquatable<PatternClassKey> {
            public readonly int PieceCount = pieceCount;
            public readonly ulong Self = self;
            public readonly ulong Other = other;

            public bool Equals(PatternClassKey other) =>
                PieceCount == other.PieceCount && Self == other.Self && Other == other.Other;

            public override bool Equals(object obj) => obj is PatternClassKey k && Equals(k);

            public override int GetHashCode() => HashCode.Combine(PieceCount, Self, Other);
        }

        private struct HeuristicData {
            public int Score; // TODO: rename this? Could be confused with Total[,Win,Loss]Score
            public uint Count;
            public uint WinCount;
            public uint LossCount;
            public double TotalWinScore;
            public double TotalLossScore;
            public double TotalScore;

            public HeuristicData(int score) {
                this.Count = 1u;

                this.WinCount = this.LossCount = 0u;
                this.TotalWinScore = this.TotalLossScore = 0.0;
                if (score > 0) {
                    this.WinCount = 1u;
                    this.TotalWinScore = score;
                } else if (score < 0) {
                    this.LossCount = 1u;
                    this.TotalLossScore = score;
                }
                
                this.TotalScore = score;

                this.Score = 0;
                this.Score = this.CalculateScore();
            }

            public HeuristicData(HeuristicData data, int newScore) {
                this.Count = data.Count + 1;

                this.WinCount = data.WinCount;
                this.LossCount = data.LossCount;
                this.TotalWinScore = data.TotalWinScore;
                this.TotalLossScore = data.TotalLossScore;
                if (newScore > 0) {
                    this.WinCount += 1u;
                    this.TotalWinScore += newScore;
                } else if (newScore < 0) {
                    this.LossCount += 1u;
                    this.TotalLossScore += newScore;
                }

                this.TotalScore = data.TotalScore + newScore;

                this.Score = 0;
                this.Score = this.CalculateScore();
            }

            public HeuristicData(HeuristicData a, HeuristicData b) {
                this.Count = a.Count + b.Count;
                this.WinCount = a.WinCount + b.WinCount;
                this.LossCount = a.LossCount + b.LossCount;
                this.TotalWinScore = a.TotalWinScore + b.TotalWinScore;
                this.TotalLossScore = a.TotalLossScore + b.TotalLossScore;
                this.TotalScore = a.TotalScore + b.TotalScore;

                this.Score = 0;
                this.Score = this.CalculateScore();
            }

            public int CalculateScore() {
                const double pseudoCount = 8.0; // Bayesian prior — regularizes toward winProb=0.5 (logit=0)

                double sampleCount = this.Count;
                double draws = sampleCount - this.WinCount - this.LossCount;
                double adjustedWins = this.WinCount + draws / 2.0;
                double winProb = (adjustedWins + pseudoCount / 2.0) / (sampleCount + pseudoCount);

                // Cap extreme win probabilities to bound log-odds to approximately ±9.2
                winProb = Math.Clamp(winProb, 0.0001, 0.9999);

                // Log-odds: maps [0,1] to (-inf,+inf), with 0.5 -> 0.
                // The weighted sum in PatternScoreSlow becomes a logistic regression
                // combination, where evidence from independent patterns accumulates correctly.
                double logOdds = Math.Log(winProb / (1.0 - winProb));

                return (int)Math.Round(logOdds * ScoreMultiplier);
            }

            public static double Sigmoid(double x) {
                return 1.0 / (1.0 + Math.Exp(-x));
            }

            public static double CrossEntropyLoss(double predictedWinProb, double label) {
                const double epsilon = 1e-15; // to avoid log(0)
                predictedWinProb = Math.Clamp(predictedWinProb, epsilon, 1.0 - epsilon);
                return -(label * Math.Log(predictedWinProb) + (1.0 - label) * Math.Log(1.0 - predictedWinProb));
            }

            public double Confidence {
                get {
                    return 1.0;/*
                    // TODO: Take a more general measure and multiply by Sigmoid(this.Score / ScoreMultiplier)?
                    //       That would also correct the sign.

                    double winSpread = (double)this.WinCount - (double)this.LossCount;
                    double nonDrawCount = (double)this.WinCount + (double)this.LossCount;
                    if (this.Score > 0) {
                        return Sigmoid(winSpread) * Sigmoid(nonDrawCount + 1.0);
                    } else if (this.Score < 0) {
                        return Sigmoid(-winSpread) * Sigmoid(nonDrawCount + 1.0);
                    } else {
                        double drawCount = (double)this.Count - nonDrawCount;
                        return Sigmoid(drawCount) * Sigmoid(this.Count + 1.0);
                    }*/
                }
            }
        }

        //\\//
        /*public int HeuristicScore() {
            ulong self = this.PlayerBoard;
            ulong other = this.OtherBoard;
            int pieceCount = this.PieceCount;
            int result = 0;

            for (int i = 0; i < PatternSets.Length; i++) {
                foreach (ulong mask in PatternSets[i]) {
                    Dictionary<ulong, Dictionary<ulong, HeuristicData>> mid;
                    Dictionary<ulong, HeuristicData> inner;
                    HeuristicData data;
                    if (PatternScores[i].TryGetValue(pieceCount, out mid) &&
                        mid.TryGetValue(self & mask, out inner) &&
                        inner.TryGetValue(other & mask, out data)) {
                        result += data.Score;
                    }
                }
            }

            for (int i = 0; i < Features.Length; i++) {
                Dictionary<int, HeuristicData> inner;
                HeuristicData data;
                if (FeatureScores[i].TryGetValue(pieceCount, out inner) &&
                    inner.TryGetValue(Features[i](this), out data)) {
                    result += data.Score;
                }
            }

            return result;
        }*/

        public int PatternScore() {
            ulong self = this.PlayerBoard;
            ulong other = this.OtherBoard;
            int pieceCount = this.OccupiedSquareCount;
            int result = 0;

            for (int i = 0; i < Patterns.Length; i++) {
                ulong mask = Patterns[i];
                HeuristicData data;
                if (PatternScores[i].TryGetValue(new PatternClassKey(pieceCount, self & mask, other & mask), out data)) {
                    result += data.Score;
                }
            }

            return result;
        }

        // While slightly slower than PatternScore(), this evaluation function has the
        // advantage of benefiting immediately from training updates without requiring
        // a costly call to CalculateWeights() and CalculatePatternScores()
        public int PatternScoreSlow() {
            int pieceCount = this.OccupiedSquareCount;
            double result = 0.0;

            BoardSymmetries sym = GetBoardSymmetries(this.PlayerBoard, this.OtherBoard);
            for (int s = 0; s < Transforms.Length; s++) {
                sym.GetPair(s, out ulong self, out ulong other);
                for (int i = 0; i < PatternClasses.Length; i++) {
                    ulong mask = PatternClasses[i][0];
                    HeuristicData patternData;
                    if (PatternClassScores[i].TryGetValue(new PatternClassKey(pieceCount, self & mask, other & mask), out patternData)) {
                        result += patternData.Score * PatternClassWeights[i, GameStage(pieceCount)] / Transforms.Length;
                    }
                }
            }

            return (int)Math.Round(result);
        }

        public int FeatureScore() {
            int pieceCount = this.OccupiedSquareCount;
            int result = 0;

            for (int i = 0; i < Features.Length; i++) {
                HeuristicData data;
                if (FeatureScores[i].TryGetValue(new FeatureKey(pieceCount, Features[i](this)), out data)) {
                    result += data.Score;
                }
            }

            return result;
        }

        //\\//
        /*public int UnknownPatterns() {
            ulong self = this.PlayerBoard;
            ulong other = this.OtherBoard;
            int pieceCount = this.PieceCount;
            int result = 0;

            for (int i = 0; i < PatternSets.Length; i++) {
                foreach (ulong mask in PatternSets[i]) {
                    Dictionary<ulong, Dictionary<ulong, HeuristicData>> mid;
                    Dictionary<ulong, HeuristicData> inner;
                    if (!PatternScores[i].TryGetValue(pieceCount, out mid) ||
                        !mid.TryGetValue(self & mask, out inner) ||
                        !inner.ContainsKey(other & mask)) {
                        result++;
                    }
                }
            }

            return result;
        }*/

        public int UnknownPatterns() {
            int pieceCount = this.OccupiedSquareCount;
            int result = 0;

            BoardSymmetries sym = GetBoardSymmetries(this.PlayerBoard, this.OtherBoard);
            for (int s = 0; s < Transforms.Length; s++) {
                sym.GetPair(s, out ulong self, out ulong other);
                for (int i = 0; i < PatternClasses.Length; i++) {
                    ulong mask = PatternClasses[i][0];
                    if (!PatternClassScores[i].ContainsKey(new PatternClassKey(pieceCount, self & mask, other & mask))) {
                        result++;
                    }
                }
            }

            return result;
        }

        public int UnknownFeatures() {
            int pieceCount = this.OccupiedSquareCount;
            int result = 0;

            for (int i = 0; i < Features.Length; i++) {
                if (!FeatureScores[i].ContainsKey(new FeatureKey(pieceCount, Features[i](this)))) {
                    result++;
                }
            }

            return result;
        }

        #endregion

        #region Learning

        // TODO: interpolate between turns when averaging. For features, also interpolate between feature values
        // TODO: more and better learning algorithms: Gradient descent, Temporal-difference

        // Log-odds score (roughly in [-9.2,9.2]) will be stored as fixed-precision int, and as such, is multiplied by
        // this factor to preserve additional significant digits.
        private const int ScoreMultiplier = 10000; // TODO: serialize this so it is changeable

        public static OthelloPlaybook Playbook;

        /// <summary>
        /// Adds the given game history to the playbook.
        /// </summary>
        /// <param name="gameHistory">
        /// A list of board states and, if available, solved endgame scores that represents the game
        /// from beginning to end.
        /// </param>
        /// <param name="verbose">Whether to print status information.</param>
        public static void TrainPlaybook(List<(OthelloNode Node, int? Score)> gameHistory, bool verbose = false) {
            DateTime start = DateTime.Now;
            TimeSpan elapsed;

            if (verbose) {
                Console.Write("Adding game to playbook...");
            }

            Playbook.AddGame(gameHistory);

            if (verbose) {
                elapsed = DateTime.Now - start;
                Console.WriteLine("done. Time elapsed = {0:0.000} seconds.", elapsed.TotalSeconds);

                Console.Write("Negamaxing scores...");
                start = DateTime.Now;
            }

            int rootScore = Playbook.Root.Score;

            if (verbose) {
                elapsed = DateTime.Now - start;
                Console.WriteLine("done. Root score = {0}. Time elapsed = {1:0.000} seconds.", rootScore, elapsed.TotalSeconds);
            }
        }

        // TODO: rename *Heuristics to *Params
        public static void ClearHeuristics() {
            foreach (Dictionary<FeatureKey, HeuristicData> dict in FeatureScores) {
                dict.Clear();
            }
            foreach (Dictionary<PatternClassKey, HeuristicData> dict in PatternClassScores) {
                dict.Clear();
            }

            HeuristicDataMaxCount = 0u;
            InitializeWeights();
        }

        public static void CalculateHeuristics() {
            DateTime start = DateTime.Now;
            ClearHeuristics();

            // Entry.Score values are already cached from ReadPlaybook, so ToList() is cheap.
            var entries = Playbook.ToList();

            Console.Write("Calculating feature values ({0} entries)... ", entries.Count);
            int processed = 0;

            Parallel.ForEach(
                Partitioner.Create(0, entries.Count),
                new ParallelOptions { MaxDegreeOfParallelism = 8 },
                // Thread-local initializer: create empty accumulators.
                () => new TrainAccumulator(),
                // Body: accumulate into thread-local dictionaries.
                (range, loopState, local) => {
                    for (int i = range.Item1; i < range.Item2; i++) {
                        TrainSingle(entries[i].Key, entries[i].Value,
                            local.PatternClassScores, local.FeatureScores);
                    }

                    int done = Interlocked.Add(ref processed, range.Item2 - range.Item1);
                    if (entries.Count >= 200 && done % (entries.Count / 20) < (range.Item2 - range.Item1)) {
                        Console.Write("{0:#0}% ", 100.0 / entries.Count * done);
                    }

                    return local;
                },
                // Thread-local finalizer: merge into global dictionaries.
                (local) => {
                    lock (PatternClassScores) {
                        MergePatternClassScores(local.PatternClassScores);
                        MergeFeatureScores(local.FeatureScores);
                    }
                }
            );

            TimeSpan elapsed = DateTime.Now - start;
            Console.WriteLine("done. Time elapsed = {0:0.000} seconds.", elapsed.TotalSeconds);

            CalculateWeights();
        }

        private class TrainAccumulator {
            public readonly Dictionary<PatternClassKey, HeuristicData>[] PatternClassScores;
            public readonly Dictionary<FeatureKey, HeuristicData>[] FeatureScores;

            public TrainAccumulator() {
                this.PatternClassScores = new Dictionary<PatternClassKey, HeuristicData>[PatternClasses.Length];
                for (int i = 0; i < this.PatternClassScores.Length; i++) {
                    this.PatternClassScores[i] = new Dictionary<PatternClassKey, HeuristicData>();
                }

                this.FeatureScores = new Dictionary<FeatureKey, HeuristicData>[Features.Length];
                for (int i = 0; i < this.FeatureScores.Length; i++) {
                    this.FeatureScores[i] = new Dictionary<FeatureKey, HeuristicData>();
                }
            }
        }

        private static void MergePatternClassScores(
            Dictionary<PatternClassKey, HeuristicData>[] source) {
            for (int i = 0; i < PatternClasses.Length; i++) {
                foreach (var kvp in source[i]) {
                    HeuristicData existing;
                    if (PatternClassScores[i].TryGetValue(kvp.Key, out existing)) {
                        PatternClassScores[i][kvp.Key] = new HeuristicData(existing, kvp.Value);
                    } else {
                        PatternClassScores[i][kvp.Key] = kvp.Value;
                    }
                }
            }
        }

        private static void MergeFeatureScores(
            Dictionary<FeatureKey, HeuristicData>[] source) {
            for (int i = 0; i < Features.Length; i++) {
                foreach (var kvp in source[i]) {
                    HeuristicData existing;
                    if (FeatureScores[i].TryGetValue(kvp.Key, out existing)) {
                        FeatureScores[i][kvp.Key] = new HeuristicData(existing, kvp.Value);
                    } else {
                        FeatureScores[i][kvp.Key] = kvp.Value;
                    }
                }
            }
        }

        // TODO: rename this too
        private static void TrainSingle(
            OthelloNode node, int finalScore,
            Dictionary<PatternClassKey, HeuristicData>[] patternScores,
            Dictionary<FeatureKey, HeuristicData>[] featureScores) {
            int pieceCount = node.OccupiedSquareCount;

            BoardSymmetries sym = GetBoardSymmetries(node.PlayerBoard, node.OtherBoard);
            for (int s = 0; s < Transforms.Length; s++) {
                sym.GetPair(s, out ulong self, out ulong other);
                for (int i = 0; i < PatternClasses.Length; i++) {
                    ulong mask = PatternClasses[i][0];
                    var key = new PatternClassKey(pieceCount, self & mask, other & mask);

                    HeuristicData data;
                    if (patternScores[i].TryGetValue(key, out data)) {
                        patternScores[i][key] = new HeuristicData(data, finalScore);
                    } else {
                        patternScores[i][key] = new HeuristicData(finalScore);
                    }
                }
            }

            for (int i = 0; i < Features.Length; i++) {
                var key = new FeatureKey(pieceCount, Features[i](node));

                HeuristicData data;
                if (featureScores[i].TryGetValue(key, out data)) {
                    featureScores[i][key] = new HeuristicData(data, finalScore);
                } else {
                    featureScores[i][key] = new HeuristicData(finalScore);
                }
            }
        }

        public static void CalculateWeights(bool verbose = true) {
            var entries = Playbook?.ToList();

            DateTime start = DateTime.Now;
            if (verbose) {
                Console.WriteLine("Calculating feature weights...");
            }

            int numClasses = PatternClasses.Length;

            // The logistic regression requires playbook data to learn weights.
            // Fall back to uniform weights when the playbook is not loaded.
            if (entries == null || entries.Count == 0) {
                for (int i = 0; i < numClasses; i++) {
                    for (int stage = 0; stage < NumGameStages; stage++) {
                        PatternClassWeights[i, stage] = 1.0 / numClasses;
                    }
                }

                CalculatePatternScores();

                if (verbose) {
                    Console.WriteLine("done (no playbook, uniform weights). Time elapsed = {0:0.000} seconds.",
                        (DateTime.Now - start).TotalSeconds);
                }
                return;
            }

            // Step 1: Extract feature vectors and labels from the playbook, grouped by bucket.
            // Entry.Score is cached after CalculateHeuristics, so ToList() is cheap here.
            // Features are the per-pattern-class log-odds values (averaged over symmetries).
            // Labels are 1.0 (win), 0.0 (loss), or 0.5 (draw).
            Console.WriteLine("Extracting feature vectors and labels from playbook entries ({0} entries)...", entries.Count);
            var trainingData = new List<(double[] features, double label)>[NumGameStages];
            var validationData = new List<(double[] features, double label)>[NumGameStages];
            for (int stage = 0; stage < NumGameStages; stage++) {
                trainingData[stage] = [];
                validationData[stage] = [];
            }
            foreach (var entry in entries) {
                OthelloNode node = entry.Key;
                int score = entry.Value;
                int pieceCount = node.OccupiedSquareCount;
                int stage = GameStage(pieceCount);

                double label;
                // TODO: should these be close to 0/1 instead of exactly 0/1? To prevent saturating sigmoid?
                if (score > 0) label = 1.0;
                else if (score < 0) label = 0.0;
                else label = 0.5;

                double[] features = new double[numClasses];
                BoardSymmetries sym = GetBoardSymmetries(node.PlayerBoard, node.OtherBoard);

                for (int i = 0; i < numClasses; i++) {
                    ulong mask = PatternClasses[i][0];
                    double featureSum = 0.0;
                    int featureCount = 0;

                    for (int s = 0; s < Transforms.Length; s++) {
                        sym.GetPair(s, out ulong self, out ulong other);
                        if (PatternClassScores[i].TryGetValue(
                                new PatternClassKey(pieceCount, self & mask, other & mask), out HeuristicData data))
                        {
                            featureSum += data.Score / (double)ScoreMultiplier;
                            featureCount++;
                        }
                    }

                    features[i] = featureCount > 0 ? featureSum / featureCount : 0.0;
                }

                // train/validation split
                if (Random.Shared.NextDouble() < 0.2) {
                    validationData[stage].Add((features, label));
                } else {
                    trainingData[stage].Add((features, label));
                }
            }

            // Step 2: Learn weights via logistic regression per game stage.
            const double learningRate = 0.01;
            const double lambda = 1e-3; // L2 regularization
            const int maxIterations = 500;
            const int minExamples = 20;

            bool[] stageTrained = new bool[NumGameStages];

            for (int stage = 0; stage < NumGameStages; stage++) {
                var trainingDataStage = trainingData[stage];
                var validationDataStage = validationData[stage];

                if (trainingDataStage.Count < minExamples) {
                    if (verbose) {
                        Console.WriteLine("  Stage {0} (pieces {1}-{2}): only {3} training + {4} validation examples, skipping weight learning.",
                            stage, PieceCountMin(stage), PieceCountMax(stage), trainingDataStage.Count, validationDataStage.Count);
                    }
                    for (int i = 0; i < numClasses; i++) {
                        PatternClassWeights[i, stage] = 1.0;
                    }
                    continue;
                }

                double[] weights = new double[numClasses];

                if (verbose) {
                    Console.WriteLine("\n  Stage {0} (pieces {1}-{2}, {3} training + {4} validation examples):",
                        stage, PieceCountMin(stage), PieceCountMax(stage), trainingDataStage.Count, validationDataStage.Count);
                }

                for (int iter = 0; iter < maxIterations; iter++) {
                    double[] gradient = new double[numClasses];
                    double trainingLoss = 0.0;

                    foreach (var (features, label) in trainingDataStage) {
                        double logit = 0.0;
                        for (int i = 0; i < numClasses; i++) {
                            logit += weights[i] * features[i];
                        }

                        double predictedWinProb = HeuristicData.Sigmoid(logit);
                        trainingLoss += HeuristicData.CrossEntropyLoss(predictedWinProb, label);

                        double error = predictedWinProb - label;
                        for (int i = 0; i < numClasses; i++) {
                            gradient[i] += error * features[i];
                        }
                    }
                    trainingLoss /= trainingDataStage.Count;

                    double validationLoss = 0.0;
                    foreach (var (features, label) in validationDataStage) {
                        double logit = 0.0;
                        for (int i = 0; i < numClasses; i++) {
                            logit += weights[i] * features[i];
                        }

                        double predictedWinProb = HeuristicData.Sigmoid(logit);
                        validationLoss += HeuristicData.CrossEntropyLoss(predictedWinProb, label);
                    }
                    if (validationDataStage.Count > 0) {
                        validationLoss /= validationDataStage.Count;
                    } else {
                        validationLoss = double.NaN;
                    }

                    // L2 regularization contribution to average loss
                    double l2Penalty = 0.0;
                    for (int i = 0; i < numClasses; i++) {
                        l2Penalty += 0.5 * lambda * weights[i] * weights[i];
                    }
                    trainingLoss += l2Penalty;
                    validationLoss += l2Penalty;

                    if (verbose && (iter == 0 || iter == maxIterations - 1 || (iter + 1) % 50 == 0)) {
                        Console.WriteLine(
                            "    iter {0,3}: train loss = {1:0.000000} validation loss = {2:0.000000}",
                            iter, trainingLoss, validationLoss);
                    }

                    for (int i = 0; i < numClasses; i++) {
                        gradient[i] /= trainingDataStage.Count;
                        gradient[i] += lambda * weights[i];
                        weights[i] -= learningRate * gradient[i];
                    }
                }

                for (int i = 0; i < numClasses; i++) {
                    PatternClassWeights[i, stage] = weights[i];
                }
                stageTrained[stage] = true;
            }

            InterpolateUntrainedStages(stageTrained);

            // Recalculate the pattern scores to incorporate the new weights.
            CalculatePatternScores();

            TimeSpan elapsed = DateTime.Now - start;
            if (verbose) {
                Console.WriteLine("done ({0} examples, {1} stages trained). Time elapsed = {2:0.000} seconds.",
                    entries.Count,
                    stageTrained.Count(t => t),
                    elapsed.TotalSeconds);
            }
        }

        /// <summary>
        /// For untrained game stages, interpolate weights from the nearest trained neighbors.
        /// Stages beyond all trained stages are copied from the nearest trained stage.
        /// </summary>
        private static void InterpolateUntrainedStages(bool[] stageTrained) {
            int numClasses = PatternClasses.Length;

            for (int stage = 0; stage < NumGameStages; stage++) {
                if (stageTrained[stage]) continue;

                int lowerStage = stage - 1, upperStage = stage + 1;
                while (lowerStage >= 0 && !stageTrained[lowerStage]) lowerStage--;
                while (upperStage < NumGameStages && !stageTrained[upperStage]) upperStage++;

                for (int i = 0; i < numClasses; i++) {
                    if (lowerStage >= 0 && upperStage < NumGameStages) {
                        // t = interpolation factor: 0.0 at lowerStage, 1.0 at upperStage
                        double t = (double)(stage - lowerStage) / (upperStage - lowerStage);
                        PatternClassWeights[i, stage] =
                            double.Lerp(PatternClassWeights[i, lowerStage], PatternClassWeights[i, upperStage], t);
                    } else if (lowerStage >= 0) {
                        PatternClassWeights[i, stage] = PatternClassWeights[i, lowerStage];
                    } else if (upperStage < NumGameStages) {
                        PatternClassWeights[i, stage] = PatternClassWeights[i, upperStage];
                    }
                }
            }
        }

        #endregion

        #region Parameter Serialization

        private static void WriteComment(StreamWriter writer, string format, params object[] args) {
            WriteComment(writer, 0, format, args);
        }

        private static void WriteComment(StreamWriter writer, int indentLevel, string format, params object[] args) {
#if VERBOSE_PARAM_SERIALIZATION
            const string indent = "    ";
            const string comment = "#   ";

            writer.Write(comment);
            for (int i = 0; i < indentLevel; i++) {
                writer.Write(indent);
            }
            writer.WriteLine(format, args);
#endif
        }

        private static void WriteHeuristicData<T>(StreamWriter writer, T key, HeuristicData data) {
            writer.Write(
                "{0},{1},{2},{3},{4},{5} ",
                key,
                data.Count,
                data.WinCount,
                data.LossCount,
                data.TotalWinScore,
                data.TotalLossScore);
        }

        public static void WriteHeuristics(string path) {
#if VERBOSE_PARAM_SERIALIZATION
            const string indent = "    ";
            const string comment = "#   ";
#endif

            Console.Write("Saving evaluation parameters...");
            DateTime start = DateTime.Now;
            StreamWriter writer = new StreamWriter(path, false);

            try {
                for (int i = 0; i < Features.Length; i++) {
                    WriteComment(writer, "Feature {0}: {1}", i, FeatureNames[i]);
                    writer.WriteLine("Feature");

                    // Group flat dictionary entries by PieceCount to preserve file format.
                    var grouped = new SortedDictionary<int, List<KeyValuePair<int, HeuristicData>>>();
                    foreach (var kvp in FeatureScores[i]) {
                        if (!grouped.TryGetValue(kvp.Key.PieceCount, out var list)) {
                            list = new List<KeyValuePair<int, HeuristicData>>();
                            grouped[kvp.Key.PieceCount] = list;
                        }
                        list.Add(new KeyValuePair<int, HeuristicData>(kvp.Key.Value, kvp.Value));
                    }

                    WriteComment(writer, "Data for {0} piece counts", grouped.Count);
                    foreach (var pieceCountKvp in grouped) {
                        writer.WriteLine("PieceCount {0}", pieceCountKvp.Key);

                        pieceCountKvp.Value.Sort((a, b) => a.Key.CompareTo(b.Key));

                        WriteComment(writer, 1, "{0} Entries", pieceCountKvp.Value.Count);
                        foreach (var kvp in pieceCountKvp.Value) {
                            WriteHeuristicData(writer, kvp.Key, kvp.Value);
                        }
                        writer.WriteLine();
                    }
                }

                for (int i = 0; i < PatternClasses.Length; i++) {
                    WriteComment(writer, "PatternClass {0}", i);
                    writer.WriteLine("PatternClass");

                    ulong[] masks = PatternClasses[i];
                    for (int j = 0; j < masks.Length; j++) {
                        ulong mask = masks[j];
                        WriteComment(writer, 1, "Pattern {0}", j);
#if VERBOSE_SERIALIZATION
                        writer.Write(PrintUlong(mask, comment + indent)); // PrintUlong includes its own newline
#endif
                    }

                    // Group flat dictionary entries by PieceCount, then by Self, to preserve file format.
                    var grouped = new SortedDictionary<int, SortedDictionary<ulong, List<KeyValuePair<ulong, HeuristicData>>>>();
                    foreach (var kvp in PatternClassScores[i]) {
                        if (!grouped.TryGetValue(kvp.Key.PieceCount, out var selfGroup)) {
                            selfGroup = new SortedDictionary<ulong, List<KeyValuePair<ulong, HeuristicData>>>();
                            grouped[kvp.Key.PieceCount] = selfGroup;
                        }
                        if (!selfGroup.TryGetValue(kvp.Key.Self, out var otherList)) {
                            otherList = new List<KeyValuePair<ulong, HeuristicData>>();
                            selfGroup[kvp.Key.Self] = otherList;
                        }
                        otherList.Add(new KeyValuePair<ulong, HeuristicData>(kvp.Key.Other, kvp.Value));
                    }

                    WriteComment(writer, "Data for {0} piece counts", grouped.Count);
                    foreach (var pieceCountKvp in grouped) {
                        writer.WriteLine("PieceCount {0}", pieceCountKvp.Key);

                        WriteComment(writer, 1, "{0} Entries", pieceCountKvp.Value.Count);
                        foreach (var selfKvp in pieceCountKvp.Value) {
                            WriteComment(writer, 2, "{0} Sub-Entries", selfKvp.Value.Count);

                            writer.WriteLine(selfKvp.Key);
                            foreach (var otherKvp in selfKvp.Value) {
                                WriteHeuristicData(writer, otherKvp.Key, otherKvp.Value);
                            }
                            writer.WriteLine();
                        }
                    }
                }
            } catch (Exception ex) {
                Console.WriteLine("error.");
                Console.WriteLine(ex);
                return;
            } finally {
                writer.Close();
            }

            TimeSpan elapsed = DateTime.Now - start;
            Console.WriteLine("done. Time elapsed = {0:0.000} seconds.", elapsed.TotalSeconds);
        }

        #endregion

        #region Parameter Deserialization

        private static uint HeuristicDataMaxCount = 0u;

        private static void CheckEOF(string line) {
            if (line == null) {
                throw new InvalidDataException("Unexpected EOF");
            }
        }

        // Read and trim the next line, skipping comments and blank lines
        private static string NextLine(StreamReader reader, string remainder = null) {
            const string comment = "#"; // TODO: cleanup consts

            string line = remainder == null ? reader.ReadLine() : remainder;
            if (line != null) {
                line = line.Trim();
            }

            while (line != null &&
                (string.IsNullOrWhiteSpace(line) ||
                line.StartsWith(comment))) {
                line = reader.ReadLine().Trim();
            }

            return line;
        }

        private static void EatLine(StreamReader reader, string match, string remainder = null) {
            string line = NextLine(reader, remainder);
            if (line != match) {
                throw new InvalidDataException(string.Format(
                    "Expected {0}, got {1}",
                    match == null ? "<EOF>" : '"' + match + '"',
                    line == null ? "<EOF>" : '"' + line + '"'));
            }
        }

        private static bool TryEatPrefix(StreamReader reader, string match, out string line, string remainder = null) {
            line = NextLine(reader, remainder);
            if (!line.StartsWith(match)) {
                return false;
            }

            line = line.Substring(match.Length).Trim();
            return true;
        }

        private static HeuristicData ParseHeuristicData<T>(string input, Func<string, T> parse, out T key) {
            CheckEOF(input);
            input = input.Trim();
            string[] data = input.Split(new char[] { ',' }, 6);
            if (data == null || data.Length < 6) {
                throw new FormatException(string.Format(
                    "Cannot parse \"{0}\" as {1}",
                    input,
                    typeof(HeuristicData).Name));
            }

            key = parse(data[0]);
            uint count = uint.Parse(data[1]);
            if (count > HeuristicDataMaxCount) {
                HeuristicDataMaxCount = count;
            }
            double totalWinScore = double.Parse(data[4]);
            double totalLossScore = double.Parse(data[5]);
            HeuristicData result = new HeuristicData() {
                Score = 0,
                Count = count,
                WinCount = uint.Parse(data[2]),
                LossCount = uint.Parse(data[3]),
                TotalWinScore = totalWinScore,
                TotalLossScore = totalLossScore,
                TotalScore = totalWinScore + totalLossScore
            };
            result.Score = result.CalculateScore();

            return result;
        }

        public static void ReadHeuristics(string path) {
            DateTime start = DateTime.Now;

            StreamReader reader;
            try {
                if (!File.Exists(path)) {
                    Console.WriteLine("File not found: {0}", path);
                }

                reader = new StreamReader(path);
                Console.Write("Loading evaluation parameters...");
            } catch {
                Console.WriteLine("I/O Error.");
                return;
            }

            try {
                HeuristicDataMaxCount = 0;
                string line = null;
                for (int i = 0; i < Features.Length; i++) {
                    EatLine(reader, "Feature", line);

                    while (TryEatPrefix(reader, "PieceCount", out line)) {
                        int pieceCount = int.Parse(line);

                        line = NextLine(reader);
                        CheckEOF(line);
                        foreach (string entry in line.Split((char[])null, StringSplitOptions.RemoveEmptyEntries)) {
                            int value;
                            HeuristicData orig;
                            HeuristicData data = ParseHeuristicData(entry, int.Parse, out value);
                            var key = new FeatureKey(pieceCount, value);
                            if (FeatureScores[i].TryGetValue(key, out orig)) {
                                FeatureScores[i][key] = new HeuristicData(orig, data);
                            } else {
                                FeatureScores[i][key] = data;
                            }
                        }
                    }
                }

                for (int i = 0; i < PatternClasses.Length; i++) {
                    EatLine(reader, "PatternClass", line);

                    line = null;
                    int pieceCount;
                    while (TryEatPrefix(reader, "PieceCount", out line, line) &&
                        int.TryParse(line, out pieceCount)) {

                        ulong self;
                        line = NextLine(reader);
                        CheckEOF(line);
                        while (line != null && ulong.TryParse(line, out self)) {
                            line = NextLine(reader);
                            CheckEOF(line);
                            foreach (string entry in line.Split((char[])null, StringSplitOptions.RemoveEmptyEntries)) {
                                ulong other;
                                HeuristicData orig;
                                HeuristicData data = ParseHeuristicData(entry, ulong.Parse, out other);
                                var key = new PatternClassKey(pieceCount, self, other);
                                if (PatternClassScores[i].TryGetValue(key, out orig)) {
                                    PatternClassScores[i][key] = new HeuristicData(orig, data);
                                } else {
                                    PatternClassScores[i][key] = data;
                                }
                            }

                            line = NextLine(reader);
                        }

                        // EOF
                        if (line == null) {
                            break;
                        }
                    }
                }

                // Make sure there's nothing left at end of file.
                EatLine(reader, null);
            } catch (Exception ex) {
                Console.WriteLine("error.");
                Console.WriteLine(ex);
                ClearHeuristics();
                return;
            } finally {
                reader.Close();
            }

            // Use uniform weights when loading from disk. The logistic regression in
            // CalculateWeights requires PatternClassScores to be populated by CalculateHeuristics
            // first; at load time we only have the deserialized data.
            for (int i = 0; i < PatternClasses.Length; i++) {
                for (int stage = 0; stage < NumGameStages; stage++) {
                    PatternClassWeights[i, stage] = 1.0 / PatternClasses.Length;
                }
            }

            CalculatePatternScores();

            TimeSpan elapsed = DateTime.Now - start;
            Console.WriteLine("done. maxCount = {0:n0}. Time elapsed = {1:0.000} seconds.", HeuristicDataMaxCount, elapsed.TotalSeconds);
        }

        #endregion

        #region Playbook Serialization/Deserialization

        public static void ReadPlaybook(string path) {
            Playbook.Deserialize(path);

            // Materialize Entry.Score values so subsequent calls to Playbook.ToList()
            // (e.g., in CalculateWeights) don't trigger expensive negamax evaluation.
            Playbook.ToList();
        }

        public static void WritePlaybook(string path) {
            Playbook.Serialize(path);
        }

        public static void ClearPlaybook() {
            Playbook.Clear();
        }

        public static void PrintPlaybookStats() {
            Playbook.PrintStats();
        }

        public static int PlaybookCount {
            get {
                return Playbook.Count;
            }
        }

        public static bool PlaybookContains(OthelloNode node) {
            return Playbook.Contains(node);
        }

        #endregion

        #region Board Serialization

        public string Serialize() {
            int data = this.Pass ? 1 : 0;
            data |= this.Turn << 1;

            return string.Format(
                CultureInfo.InvariantCulture,
                "{0}{1:X16}{2:x16}",
                data,
                this.BlackBoard,
                this.WhiteBoard);
        }

        #endregion

        #region Board Deserialization

        public static OthelloNode Deserialize(string data) {
            if (data == null || data.Length != 33) {
                throw new FormatException();
            }

            int turn = int.Parse(data.Substring(0, 1));
            bool pass = (turn & 1) == 1;
            turn >>= 1;

            ulong black = ulong.Parse(data.Substring(1, 16), NumberStyles.HexNumber);
            ulong white = ulong.Parse(data.Substring(17, 16), NumberStyles.HexNumber);

            return new OthelloNode(
                turn,
                turn == BLACK ? black : white,
                turn == BLACK ? white : black,
                pass);
        }

        #endregion

        #region Pretty-printing

        public static string PrintUlong(ulong value, string prefix = null) {
            const char empty = '.';
            const char occupied = '+';

            StringBuilder sb = new StringBuilder();
            for (int j = 0; j < 8; j++) {
                for (int i = 0; i < 8; i++) {
                    if (i > 0) {
                        sb.Append(' ');
                    } else if (!string.IsNullOrEmpty(prefix)) {
                        sb.Append(prefix);
                    }
                    sb.Append((value & Square[i, j]) == 0 ? empty : occupied);
                }
                sb.AppendLine();
            }

            return sb.ToString();
        }

        private static void PrintHorizontalSeparator(StringBuilder sb, int groupSize, int offset, bool extra = false) {
            if (offset > 0) {
                for (int i = 0; i < groupSize; i++) {
                    if (i == 0) {
                        sb.Append("---------------");
                    } else {
                        sb.Append("-+----------------");
                    }
                }
                if (extra) {
                    sb.Append("-+");
                }
                sb.AppendLine();
            }
        }

        public static string PrintNodes(int groupSize, bool showTurn, params OthelloNode[] nodes) {
            if (groupSize <= 0) {
                return string.Empty;
            }

            StringBuilder sb = new StringBuilder();

            int offset = 0;
            OthelloNode[] buffer = new OthelloNode[groupSize];
            for (; offset + groupSize <= nodes.Length; offset += groupSize) {
                for (int i = 0; i < groupSize; i++) {
                    buffer[i] = nodes[offset + i];
                }

                PrintHorizontalSeparator(sb, groupSize, offset);
                PrintNodes(sb, showTurn, buffer);
            }

            groupSize = nodes.Length % groupSize;
            if (groupSize > 0) {
                buffer = new OthelloNode[groupSize];
                for (int i = 0; i < groupSize; i++) {
                    buffer[i] = nodes[offset + i];
                }

                PrintHorizontalSeparator(sb, groupSize, offset, extra: true);
                PrintNodes(sb, showTurn, buffer);
            }

            return sb.ToString();
        }

        private static void PrintNodes(StringBuilder sb, bool showTurn, params OthelloNode[] nodes) {
            const char black = 'X';
            const char white = 'O';
            const char blank = '.';
            const char invalid = '!';

            bool first = true;
            if (showTurn) {
                foreach (OthelloNode node in nodes) {
                    if (!first) {
                        sb.Append(" | ");
                    }
                    first = false;
                    sb.Append("Turn = ");
                    sb.Append(node.turn == BLACK ? black : white);
                    sb.Append(node.Pass ? '*' : ' ');
                    sb.Append("      ");
                }
                sb.AppendLine();
            }

            for (int j = 0; j < 8; j++) {
                first = true;
                foreach (OthelloNode node in nodes) {
                    if (!first) {
                        sb.Append(" | ");
                    }
                    first = false;

                    for (int i = 0; i < 8; i++) {
                        ulong square = Square[i, j];
                        if (i > 0) {
                            sb.Append(' ');
                        }
                        sb.Append(
                            (node.board[BLACK] & node.board[WHITE] & square) != 0 ?
                            invalid :
                            (node.board[BLACK] & square) != 0 ?
                            black :
                            (node.board[WHITE] & square) != 0 ?
                            white :
                            blank);
                    }
                }
                sb.AppendLine();
            }
        }

        public override string ToString() {
            return PrintNodes(1, true, this);
        }

        public void PrintScore(string blackName = "Black", string whiteName = "White") {
            if (!this.IsGameOver) {
                return;
            }

            Console.Write("Final Score: {0} ", this.Score);
            if (this.Score > 0) {
                Console.WriteLine("({0} beats {1})", blackName, whiteName);
            } else if (this.Score < 0) {
                Console.WriteLine("({0} beats {1})", whiteName, blackName);
            } else {
                Console.WriteLine("(The game is a draw)");
            }
        }

        public static void PrintWeights() {
            for (int i = 0; i < PatternClasses.Length; i++) {
                Console.WriteLine("Pattern {0}:", i);
                Console.WriteLine(PrintUlong(PatternClasses[i][0]));

                for (int stage = 0; stage < NumGameStages; stage++) {
                    Console.WriteLine("{0:00} \t{1:00.000}", stage, PatternClassWeights[i, stage]);
                }

                Console.WriteLine();
            }
        }

        #endregion
    }
}
