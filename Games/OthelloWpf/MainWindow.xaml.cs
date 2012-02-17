using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;

using Solver;
using Othello;

namespace OthelloWpf {
    // TODO: fix braces

    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window, Player<OthelloNode> {
        private static Brush EmptyPieceBrush = new SolidColorBrush(Colors.Transparent);
        private static Brush BlackPieceBrush = new SolidColorBrush(Colors.Black);
        private static Brush WhitePieceBrush = new SolidColorBrush(Colors.White);
        private static Brush BlackMoveBrush = new SolidColorBrush(Color.FromArgb(84, 0, 0, 0));
        private static Brush WhiteMoveBrush = new SolidColorBrush(Color.FromArgb(96, 255, 255, 255));

        private Rectangle[,] squares = new Rectangle[8, 8];
        private Ellipse[,] pieces = new Ellipse[8, 8];

        private volatile bool waitingForMove = true;
        private volatile int moveI = 0;
        private volatile int moveJ = 0;

        private Player<OthelloNode> blackPlayer = null;
        private Player<OthelloNode> whitePlayer = null;
        private volatile OthelloNode board;
        private Thread computationThread;

        public MainWindow() {
            InitializeComponent();

            BindingFlags bindingFlags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
            for (int j = 0; j < 8; j++) {
                for (int i = 0; i < 8; i++) {
                    this.squares[i, j] = typeof(MainWindow).GetField("Square" + i + j, bindingFlags).GetValue(this) as Rectangle;
                    this.pieces[i, j] = typeof(MainWindow).GetField("Ellipse" + i + j, bindingFlags).GetValue(this) as Ellipse;
                }
            }

            this.blackPlayer = new AlphaBetaPlayer(5, OthelloNode.Eval0, verbose: true, randomness: true);
            this.whitePlayer = new AlphaBetaPlayer(5, OthelloNode.Eval1, verbose: true, randomness: true);

            //this.blackPlayer = new RandomPlayer();
            //this.whitePlayer= new RandomPlayer();
        }

        private static int Eval0(OthelloNode node) {
            return 2 * node.PotentialMobilitySpread() - node.FrontierSpread() + 8 * node.CornerSpread();
        }

        private static int Eval1(OthelloNode node) {
            return 2 * node.PotentialMobilitySpread() - node.FrontierSpread() + 8 * node.CornerSpread();
        }

        private void InitGame() {
            if (this.computationThread != null) {
                this.computationThread.Abort();
                this.computationThread = null;
            }

            this.waitingForMove = false;
            this.board = new OthelloNode();
            this.UpdateBoard();

            this.StartButton.IsEnabled = true;
            this.SwitchButton.IsEnabled = true;
        }

        private void GameLoop() {
            try {
                if (this.board != null) {
                    List<OthelloNode> children = new List<OthelloNode>();
                    while (!this.board.IsGameOver) {
                        const int monteCarloIters = 0;
                        if (monteCarloIters > 0) {
                            Console.Write("Monte-carlo score: ");
                            int monteCarloScore = board.MonteCarlo(monteCarloIters);
                            Console.WriteLine("{0:0.000}", monteCarloScore / (double)monteCarloIters);
                        }

                        this.board.GetChildren(children);
                        if (children.Count == 0) {
                            // TODO: error
                            return;
                        }

                        int index = ((this.board.Turn == OthelloNode.BLACK ?
                            this.blackPlayer :
                            this.whitePlayer) ??
                            this).SelectNode(children);

                        if (index < 0 || index >= children.Count) {
                            // TODO: disqualify player
                            return;
                        }

                        this.board = children[index];
                        this.UpdateBoard();
                    }

                    board.PrintScore();
                }
            } finally {
                this.Dispatcher.Invoke(new Action(delegate() {
                    this.SwitchButton.IsEnabled = true;
                }));
            }
        }

        // TODO: make it possible to edit the board before starting a game
        private void EditLoop() {

        }

        private void StartButton_Click(object sender, RoutedEventArgs e) {
            this.StartButton.IsEnabled = false;
            this.SwitchButton.IsEnabled = false;

            if (this.computationThread != null) {
                this.computationThread.Abort();
                this.computationThread = null;
            }
            Thread thread = new Thread(this.GameLoop);
            thread.Start();
            this.computationThread = thread;
        }

        private void ClearButton_Click(object sender, RoutedEventArgs e) {
            this.InitGame();
        }

        private void SwitchButton_Click(object sender, RoutedEventArgs e) {
            Player<OthelloNode> temp = this.blackPlayer;
            this.blackPlayer = this.whitePlayer;
            this.whitePlayer = temp;
        }

        private static bool TryGetCoordinates(object sender, out int column, out int row) {
            column = row = 0;
            Shape shape = sender as Shape;
            if (shape == null) {
                return false;
            }

            row = Grid.GetRow(shape);
            column = Grid.GetColumn(shape);
            return row >= 0 && column >= 0 && row < 8 && column < 8;
        }

        private void Shape_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) {
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

        private void Ellipse_MouseRightButtonDown(object sender, MouseButtonEventArgs e) {
            int i, j;
            if (!TryGetCoordinates(sender, out i, out j) ||
                (GetLegalMoves(this.board) & OthelloNode.Square[i, j]) == 0) {
                return;
            }

            this.UpdateBoard(this.board.GetChildren().Find(child =>
                (child.OtherBoard & OthelloNode.Square[i, j]) != 0));
        }

        private void Ellipse_MouseRightButtonUp(object sender, MouseButtonEventArgs e) {
            this.UpdateBoard();
        }

        private static ulong GetLegalMoves(OthelloNode board) {
            ulong moves = 0ul;
            foreach (OthelloNode child in board.GetChildren()) {
                moves |= child.Occupied;
            }

            return moves ^ board.Occupied;
        }

        private void ClearPieces() {
            this.UpdatePieces(EmptyPieceBrush, 0xfffffffffffffffful);
        }

        private void UpdatePieces(Brush brush, ulong mask) {
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
            this.Dispatcher.Invoke(new Action(delegate() {
                this.ClearPieces();
                if (board != null) {
                    this.UpdatePieces(BlackPieceBrush, board.BlackBoard);
                    this.UpdatePieces(WhitePieceBrush, board.WhiteBoard);

                    this.UpdatePieces(
                        board.Turn == OthelloNode.BLACK ? BlackMoveBrush : WhiteMoveBrush,
                        GetLegalMoves(board));
                }
            }));
        }

        // TODO: implement Player<OthelloNode> interface
        public int SelectNode(List<OthelloNode> children) {
            if (children == null || children.Count == 0) {
                // TODO: wait for 'pass' button to get clicked?
                return -1;
            }

            // TODO: implement a 'pass' button
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

                        // TODO: notify invalid move

                        this.waitingForMove = true;
                    }
                }

                Thread.Sleep(50);
            }
        }
    }
}
