using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;

using Solver;
using Othello;

namespace OthelloAvalonia {
    public partial class OthelloWindow : Window, Player<OthelloNode>, INotifyPropertyChanged {
        private static IBrush EmptyPieceBrush = new SolidColorBrush(Colors.Transparent);
        private static IBrush BlackPieceBrush = new SolidColorBrush(Colors.Black);
        private static IBrush WhitePieceBrush = new SolidColorBrush(Colors.White);
        private static IBrush BlackMoveBrush = new SolidColorBrush(Color.FromArgb(72, 0, 0, 0));
        private static IBrush WhiteMoveBrush = new SolidColorBrush(Color.FromArgb(84, 255, 255, 255));

        private Rectangle[,] squares = new Rectangle[8, 8];
        private Ellipse[,] pieces = new Ellipse[8, 8];

        private volatile bool waitingForMove = true;
        private volatile int moveI = 0;
        private volatile int moveJ = 0;

        private Player<OthelloNode> blackPlayer = null;
        private Player<OthelloNode> whitePlayer = null;
        private volatile OthelloNode board;
        private CancellationTokenSource computationCts;

        public const string ParamsPath = "params.txt";
        public const string PlaybookPath = "playbook.txt";

        public new event PropertyChangedEventHandler PropertyChanged;

        private void OnPropertyChanged(string name) {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }

        public OthelloWindow() {
            InitializeComponent();

            this.SearchDepth = (int)this.SearchDepthSlider.Minimum;

            for (int j = 0; j < 8; j++) {
                for (int i = 0; i < 8; i++) {
                    this.squares[i, j] = this.FindControl<Rectangle>("Square" + i + j);
                    this.pieces[i, j] = this.FindControl<Ellipse>("Ellipse" + i + j);
                }
            }

            // Wire up pointer events (replaces WPF EventSetters)
            for (int j = 0; j < 8; j++) {
                for (int i = 0; i < 8; i++) {
                    this.squares[i, j].PointerPressed += Shape_PointerPressed;
                    this.pieces[i, j].PointerPressed += Piece_PointerPressed;
                }
            }

            OthelloNode.ReadPlaybook(PlaybookPath);
            OthelloNode.ReadHeuristics(ParamsPath);
            OthelloNode.PrintPlaybookStats();
        }

        #region Bindable Properties

        private bool _randomness;
        public bool Randomness {
            get => _randomness;
            set { _randomness = value; OnPropertyChanged(nameof(Randomness)); }
        }

        private bool _training;
        public bool Training {
            get => _training;
            set { _training = value; OnPropertyChanged(nameof(Training)); }
        }

        private int _searchDepth;
        public int SearchDepth {
            get => _searchDepth;
            set { _searchDepth = value; OnPropertyChanged(nameof(SearchDepth)); }
        }

        private bool _humanBlack;
        public bool HumanBlack {
            get => _humanBlack;
            set { _humanBlack = value; OnPropertyChanged(nameof(HumanBlack)); }
        }

        private bool _humanWhite;
        public bool HumanWhite {
            get => _humanWhite;
            set { _humanWhite = value; OnPropertyChanged(nameof(HumanWhite)); }
        }

        private bool _aiSwap;
        public bool AiSwap {
            get => _aiSwap;
            set { _aiSwap = value; OnPropertyChanged(nameof(AiSwap)); }
        }

        private bool _memo;
        public bool Memo {
            get => _memo;
            set { _memo = value; OnPropertyChanged(nameof(Memo)); }
        }

        #endregion

        private static int Eval0(OthelloNode node) {
            return 2 * node.PotentialMobilitySpread() - node.FrontierSpread() + 8 * node.CornerSpread();
        }

        private static int Eval1(OthelloNode node) {
            return 2 * node.PotentialMobilitySpread() - node.FrontierSpread() + 8 * node.CornerSpread();
        }

        private void InitGame() {
            this.AbortComputation();

            this.waitingForMove = false;
            this.board = new OthelloNode();
            this.UpdateBoard();

            this.StartButton.IsEnabled = true;
        }

        private void AbortComputation() {
            if (this.computationCts != null) {
                this.computationCts.Cancel();
                this.computationCts = null;
            }
        }

        private void InitPlayers() {
            const bool verbose = true;

            Player<OthelloNode> black =
                new MtdFPlayer(this.SearchDepth, node => node.PatternScoreSlow(), verbose: verbose, solveEndgame: true, randomness: this.Randomness);
            Player<OthelloNode> white =
                new MtdFPlayer(this.SearchDepth, OthelloNode.Eval1, verbose: verbose, solveEndgame: true, randomness: this.Randomness);

            if (this.AiSwap) {
                this.blackPlayer = white;
                this.whitePlayer = black;
            } else {
                this.blackPlayer = black;
                this.whitePlayer = white;
            }

            if (this.Memo) {
                this.blackPlayer = new MemoPlayer(OthelloNode.Playbook, this.blackPlayer) { Verbose = verbose };
                this.whitePlayer = new MemoPlayer(OthelloNode.Playbook, this.whitePlayer) { Verbose = verbose };
            }

            if (this.HumanBlack) {
                this.blackPlayer = null;
            }
            if (this.HumanWhite) {
                this.whitePlayer = null;
            }
        }

        private void GameLoop(CancellationToken ct) {
            Dispatcher.UIThread.InvokeAsync(this.InitPlayers).Wait();

            if (this.board != null) {
                int playbookCount = OthelloNode.Playbook.Count;
                var history = new List<(OthelloNode Node, int? Score)>();
                List<OthelloNode> children = new List<OthelloNode>();
                while (!this.board.IsGameOver && !ct.IsCancellationRequested) {
                    history.Add((this.board, null));

                    const int monteCarloIters = 0;
                    if (monteCarloIters > 0) {
                        Console.Write("Monte-carlo score: ");
                        int monteCarloScore = board.MonteCarlo(monteCarloIters);
                        Console.WriteLine("{0:0.000}", monteCarloScore / (double)monteCarloIters);
                    }

                    this.board.GetChildren(children);
                    if (children.Count == 0) {
                        return;
                    }

                    int index = ((this.board.Turn == OthelloNode.BLACK ?
                        this.blackPlayer :
                        this.whitePlayer) ??
                        this).SelectNode(children);

                    if (ct.IsCancellationRequested) return;

                    if (index < 0 || index >= children.Count) {
                        return;
                    }

                    this.board = children[index];
                    this.UpdateBoard();
                }

                if (ct.IsCancellationRequested) return;

                history.Add((this.board, null));
                this.board.PrintScore();
                Console.WriteLine();

                Dispatcher.UIThread.InvokeAsync(() => {
                    if (this.Training) {
                        Console.Write("Training playbook... ");
                        OthelloNode.TrainPlaybook(history);
                        Console.WriteLine("{0} entries added.", OthelloNode.Playbook.Count - playbookCount);
                    }
                });
            }
        }

        private void StartButton_Click(object sender, RoutedEventArgs e) {
            this.StartButton.IsEnabled = false;

            this.AbortComputation();
            var cts = new CancellationTokenSource();
            this.computationCts = cts;
            Task.Run(() => this.GameLoop(cts.Token));
        }

        private void ClearButton_Click(object sender, RoutedEventArgs e) {
            Console.WriteLine();
            Console.WriteLine("Initializing new game...");

            this.InitGame();

            Console.WriteLine("done.");
            Console.WriteLine();
        }

        private static bool TryGetCoordinates(object sender, out int column, out int row) {
            column = row = 0;
            if (sender is not Control control) {
                return false;
            }

            row = Grid.GetRow(control);
            column = Grid.GetColumn(control);
            return row >= 0 && column >= 0 && row < 8 && column < 8;
        }

        private void Shape_PointerPressed(object sender, PointerPressedEventArgs e) {
            var props = e.GetCurrentPoint(null).Properties;
            if (!props.IsLeftButtonPressed) return;

            int column, row;
            if (!TryGetCoordinates(sender, out column, out row)) {
                return;
            }

            lock (this) {
                if (this.waitingForMove) {
                    this.moveI = column;
                    this.moveJ = row;
                    this.waitingForMove = false;
                }
            }
        }

        private void Piece_PointerPressed(object sender, PointerPressedEventArgs e) {
            var props = e.GetCurrentPoint(null).Properties;

            if (props.IsLeftButtonPressed) {
                // Left click — same as square click
                int column, row;
                if (!TryGetCoordinates(sender, out column, out row)) {
                    return;
                }

                lock (this) {
                    if (this.waitingForMove) {
                        this.moveI = column;
                        this.moveJ = row;
                        this.waitingForMove = false;
                    }
                }
            } else if (props.IsRightButtonPressed) {
                // Right click — preview move
                int i, j;
                if (!TryGetCoordinates(sender, out i, out j) ||
                    (GetLegalMoves(this.board) & OthelloNode.Square[i, j]) == 0) {
                    return;
                }

                this.UpdateBoard(this.board.GetChildren().Find(child =>
                    (child.OtherBoard & OthelloNode.Square[i, j]) != 0));
            }
        }

        protected override void OnPointerReleased(PointerReleasedEventArgs e) {
            base.OnPointerReleased(e);
            // Restore board display after right-click preview
            if (e.InitialPressMouseButton == MouseButton.Right) {
                this.UpdateBoard();
            }
        }

        private static ulong GetLegalMoves(OthelloNode board) {
            ulong moves = 0ul;
            foreach (OthelloNode child in board.GetChildren()) {
                moves |= child.Occupied;
            }

            return moves == 0ul ? 0ul : moves ^ board.Occupied;
        }

        private void ClearPieces() {
            this.UpdatePieces(EmptyPieceBrush, 0xfffffffffffffffful);
        }

        private void UpdatePieces(IBrush brush, ulong mask) {
            for (int j = 0; j < 8; j++) {
                for (int i = 0; i < 8; i++) {
                    if ((OthelloNode.Square[i, j] & mask) != 0) {
                        this.pieces[i, j].Fill = brush;
                    }
                }
            }
        }

        private void UpdateBoard() {
            this.UpdateBoard(this.board);
        }

        private void UpdateBoard(OthelloNode board) {
            Dispatcher.UIThread.InvokeAsync(() => {
                this.ClearPieces();
                if (board != null) {
                    this.UpdatePieces(BlackPieceBrush, board.BlackBoard);
                    this.UpdatePieces(WhitePieceBrush, board.WhiteBoard);

                    this.UpdatePieces(
                        board.Turn == OthelloNode.BLACK ? BlackMoveBrush : WhiteMoveBrush,
                        GetLegalMoves(board));
                }
            });
        }

        public int SelectNode(List<OthelloNode> children) {
            if (children == null || children.Count == 0) {
                return -1;
            }

            if (children.Count == 1 && children[0].Pass) {
                return 0;
            }

            lock (this) {
                this.waitingForMove = true;
            }

            int i = 0;
            int j = 0;

            while (true) {
                lock (this) {
                    if (!this.waitingForMove) {
                        i = this.moveI;
                        j = this.moveJ;

                        ulong square = OthelloNode.Square[i, j];
                        int index = children.FindIndex(child =>
                            (square & this.board.Occupied) == 0 &&
                            (square & child.Occupied) != 0);
                        if (index >= 0) {
                            return index;
                        }

                        this.waitingForMove = true;
                    }
                }

                Thread.Sleep(50);
            }
        }

        private void RefreshParamsButton_Click(object sender, RoutedEventArgs e) {
            OthelloNode.CalculateHeuristics();
            OthelloNode.CalculateWeights();
        }

        private void SaveParamsButton_Click(object sender, RoutedEventArgs e) {
            OthelloNode.WriteHeuristics(ParamsPath);
        }

        private void SavePlaybookButton_Click(object sender, RoutedEventArgs e) {
            OthelloNode.PrintPlaybookStats();
            OthelloNode.WritePlaybook(PlaybookPath);
        }
    }
}
