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

            //this.blackPlayer = new BruteForcePlayer(3, board => board.MonteCarlo(50));
            this.whitePlayer = new BruteForcePlayer(3, board => board.MonteCarlo(100));

            //this.blackPlayer = new RandomPlayer();
            //this.whitePlayer= new RandomPlayer();
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
        }

        private void GameLoop() {
            if (this.board == null) {
                return;
            }

            List<OthelloNode> children = new List<OthelloNode>();
            while (!this.board.IsGameOver) {
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
        }

        // TODO: make it possible to edit the board before starting a game
        private void EditLoop() {

        }

        private void StartButton_Click(object sender, RoutedEventArgs e) {
            this.StartButton.IsEnabled = false;

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

        private void Square_MouseDown(object sender, MouseButtonEventArgs e) {
            Shape shape = sender as Shape;
            if (shape == null) {
                return;
            }

            int row = Grid.GetRow(shape);
            int column = Grid.GetColumn(shape);
            if (row < 0 || column < 0 || row >= 8 || column >= 8) {
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
            this.Dispatcher.Invoke(new Action(delegate() {
                this.ClearPieces();
                if (this.board != null) {
                    this.UpdatePieces(BlackPieceBrush, this.board.BlackBoard);
                    this.UpdatePieces(WhitePieceBrush, this.board.WhiteBoard);

                    ulong moves = 0ul;
                    foreach (OthelloNode child in this.board.GetChildren()) {
                        moves |= child.Occupied;
                    }
                    moves ^= this.board.Occupied;
                    this.UpdatePieces(this.board.Turn == OthelloNode.BLACK ? BlackMoveBrush : WhiteMoveBrush, moves);
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
                        if (index >= 0)
                        {
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
