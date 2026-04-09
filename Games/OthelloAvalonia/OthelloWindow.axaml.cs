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
    public enum PlayerType {
        Human,
        Random,
        Pattern,
        PatternSlow,
        NeuralNet,
        Eval0,
        Eval1,
    }

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
        public const string NNParamsPath = "nn-params.txt";
        private const int MinNNTrainingExamples = 500;

        private OthelloNeuralNetwork neuralNetwork;

        public new event PropertyChangedEventHandler PropertyChanged;

        private void OnPropertyChanged(string name) {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }

        public OthelloWindow() {
            InitializeComponent();

            this.BlackSearchDepth = (int)this.BlackSearchDepthSlider.Minimum;
            this.WhiteSearchDepth = (int)this.WhiteSearchDepthSlider.Minimum;

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

        public static PlayerType[] PlayerTypes => (PlayerType[])Enum.GetValues(typeof(PlayerType));

        // Black player settings
        private PlayerType _blackPlayerType = PlayerType.Pattern;
        public PlayerType BlackPlayerType {
            get => _blackPlayerType;
            set { _blackPlayerType = value; OnPropertyChanged(nameof(BlackPlayerType)); OnPropertyChanged(nameof(BlackIsAI)); OnPropertyChanged(nameof(BlackIsSearchPlayer)); }
        }
        public bool BlackIsAI => _blackPlayerType != PlayerType.Human;
        public bool BlackIsSearchPlayer => _blackPlayerType != PlayerType.Human && _blackPlayerType != PlayerType.Random;

        private bool _blackRandomness;
        public bool BlackRandomness {
            get => _blackRandomness;
            set { _blackRandomness = value; OnPropertyChanged(nameof(BlackRandomness)); }
        }

        private bool _blackMemo;
        public bool BlackMemo {
            get => _blackMemo;
            set { _blackMemo = value; OnPropertyChanged(nameof(BlackMemo)); }
        }

        private int _blackSearchDepth;
        public int BlackSearchDepth {
            get => _blackSearchDepth;
            set { _blackSearchDepth = value; OnPropertyChanged(nameof(BlackSearchDepth)); }
        }

        // White player settings
        private PlayerType _whitePlayerType = PlayerType.Pattern;
        public PlayerType WhitePlayerType {
            get => _whitePlayerType;
            set { _whitePlayerType = value; OnPropertyChanged(nameof(WhitePlayerType)); OnPropertyChanged(nameof(WhiteIsAI)); OnPropertyChanged(nameof(WhiteIsSearchPlayer)); }
        }
        public bool WhiteIsAI => _whitePlayerType != PlayerType.Human;
        public bool WhiteIsSearchPlayer => _whitePlayerType != PlayerType.Human && _whitePlayerType != PlayerType.Random;

        private bool _whiteRandomness;
        public bool WhiteRandomness {
            get => _whiteRandomness;
            set { _whiteRandomness = value; OnPropertyChanged(nameof(WhiteRandomness)); }
        }

        private bool _whiteMemo;
        public bool WhiteMemo {
            get => _whiteMemo;
            set { _whiteMemo = value; OnPropertyChanged(nameof(WhiteMemo)); }
        }

        private int _whiteSearchDepth;
        public int WhiteSearchDepth {
            get => _whiteSearchDepth;
            set { _whiteSearchDepth = value; OnPropertyChanged(nameof(WhiteSearchDepth)); }
        }

        // Global settings
        private bool _training = true;
        public bool Training {
            get => _training;
            set { _training = value; OnPropertyChanged(nameof(Training)); }
        }

        #endregion

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

        private Func<OthelloNode, int> GetEvaluator(PlayerType type) {
            if (type == PlayerType.NeuralNet) {
                if (neuralNetwork == null) {
                    throw new InvalidOperationException("Neural network not loaded. Load nn-params.txt first.");
                }
                return neuralNetwork.Evaluate;
            }
            return type switch {
                PlayerType.Pattern => node => node.PatternScore(),
                PlayerType.PatternSlow => node => node.PatternScoreSlow(),
                PlayerType.Eval0 => OthelloNode.Eval0,
                PlayerType.Eval1 => OthelloNode.Eval1,
                _ => throw new InvalidOperationException($"No evaluator for {type}"),
            };
        }

        private Player<OthelloNode> CreatePlayer(PlayerType type, int depth, bool randomness, bool memo) {
            if (type == PlayerType.Human) return null;

            const bool verbose = true;

            Player<OthelloNode> player = type switch {
                PlayerType.Random => new RandomPlayer { Verbose = verbose },
                _ => new MtdFPlayer(depth, GetEvaluator(type), verbose: verbose, solveEndgame: true, randomness: randomness)
            };
            if (memo) {
                player = new MemoPlayer(OthelloNode.Playbook, player) { Verbose = verbose };
            }
            return player;
        }

        private void InitPlayers() {
            this.blackPlayer = CreatePlayer(this.BlackPlayerType, this.BlackSearchDepth, this.BlackRandomness, this.BlackMemo);
            this.whitePlayer = CreatePlayer(this.WhitePlayerType, this.WhiteSearchDepth, this.WhiteRandomness, this.WhiteMemo);
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

        private void SwapButton_Click(object sender, RoutedEventArgs e) {
            (BlackPlayerType, WhitePlayerType) = (WhitePlayerType, BlackPlayerType);
            (BlackRandomness, WhiteRandomness) = (WhiteRandomness, BlackRandomness);
            (BlackMemo, WhiteMemo) = (WhiteMemo, BlackMemo);
            (BlackSearchDepth, WhiteSearchDepth) = (WhiteSearchDepth, BlackSearchDepth);
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
        }

        private void SaveParamsButton_Click(object sender, RoutedEventArgs e) {
            OthelloNode.WriteHeuristics(ParamsPath);
        }

        private void SavePlaybookButton_Click(object sender, RoutedEventArgs e) {
            OthelloNode.PrintPlaybookStats();
            OthelloNode.WritePlaybook(PlaybookPath);
        }

        private void RepairPlaybookButton_Click(object sender, RoutedEventArgs e) {
            int repaired = OthelloNode.Playbook.RepairMissingChildLinks(verbose: true);
            Console.WriteLine("Repaired {0} missing child link(s).", repaired);
        }

        private void CheckPlaybookButton_Click(object sender, RoutedEventArgs e) {
            bool result = OthelloNode.Playbook.Check(verbose: true);
            Console.WriteLine("Playbook integrity check: {0}", result ? "PASSED" : "FAILED");
        }

        private void LoadNNButton_Click(object sender, RoutedEventArgs e) {
            if (!System.IO.File.Exists(NNParamsPath)) {
                Console.WriteLine("Neural network file not found: {0}", NNParamsPath);
                return;
            }
            try {
                neuralNetwork = OthelloNeuralNetwork.Load(NNParamsPath);
            } catch (Exception ex) {
                Console.WriteLine("Failed to load neural network: {0}", ex.Message);
            }
        }

        private void TrainNNButton_Click(object sender, RoutedEventArgs e) {
            var playbookEntries = OthelloNode.Playbook?.ToList();
            int entryCount = playbookEntries?.Count ?? 0;
            if (entryCount < MinNNTrainingExamples) {
                Console.WriteLine("Not enough playbook entries for NN training ({0} < {1}). " +
                    "Play more games first.", entryCount, MinNNTrainingExamples);
                return;
            }

            TrainNNButton.IsEnabled = false;
            Task.Run(() => {
                try {
                    // Initialize or warm-start
                    OthelloNeuralNetwork nn;
                    if (neuralNetwork != null) {
                        nn = neuralNetwork;
                        Console.WriteLine("Warm-starting from current neural network.");
                    } else if (System.IO.File.Exists(NNParamsPath)) {
                        nn = OthelloNeuralNetwork.Load(NNParamsPath);
                        Console.WriteLine("Warm-starting from {0}.", NNParamsPath);
                    } else {
                        nn = new OthelloNeuralNetwork();
                        nn.InitializeWeights(Random.Shared);
                        Console.WriteLine("Initialized fresh neural network ({0} pattern classes, {1}x{2}x1).",
                            OthelloNode.PatternClasses.Length, OthelloNeuralNetwork.AccumulatorSize,
                            OthelloNeuralNetwork.Hidden2Size);
                    }

                    var data = nn.ExtractTrainingData();
                    if (data.Count < MinNNTrainingExamples) {
                        Console.WriteLine("Not enough training examples ({0} < {1}).",
                            data.Count, MinNNTrainingExamples);
                        return;
                    }

                    var config = new TrainingConfig {
                        LearningRate = 1e-3f,
                        WeightDecay = 1e-4f,
                        MaxEpochs = 200,
                    };

                    Console.WriteLine("Training neural network on {0} examples (TorchSharp)...", data.Count);
                    float finalLoss = Othello.TorchTraining.TorchTrainer.Train(nn, data, config, savePath: NNParamsPath);
                    Console.WriteLine("Training complete. Final loss = {0:0.000000}", finalLoss);

                    neuralNetwork = nn;
                } catch (Exception ex) {
                    Console.WriteLine("NN training failed: {0}", ex.Message);
                } finally {
                    Dispatcher.UIThread.InvokeAsync(() => TrainNNButton.IsEnabled = true);
                }
            });
        }
    }
}
