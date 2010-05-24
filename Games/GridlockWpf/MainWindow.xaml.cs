using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using System.Windows.Threading;

using Solver;
using Gridlock;

namespace GridlockWpf {
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window {
        private SinglePlayerSolver<GridNode> _solver = new SinglePlayerSolver<GridNode>(GridNode.Comparator);
        private GridNode _level;
        private List<GridNode> _solution;

        private DateTime _solverStart;
        private DateTime _solverFinish;
        private DispatcherTimer _statusTimer;
        
        private int _solutionIndex;
        private DispatcherTimer _animationTimer;

        public MainWindow() {
            InitializeComponent();

            _statusTimer = new DispatcherTimer();
            _statusTimer.Interval = TimeSpan.FromMilliseconds(100);
            _statusTimer.Tick += new EventHandler(_statusTimer_Tick);

            _animationTimer = new DispatcherTimer();
            _animationTimer.Interval = TimeSpan.FromMilliseconds(500);
            _animationTimer.Tick += new EventHandler(_animationTimer_Tick);
        }

        private void _statusTimer_Tick(object sender, EventArgs e) {
            int nodes = _solver.TotalNodes;
            double rate = nodes / Math.Max(0.001, (DateTime.Now - _solverStart).TotalMilliseconds);
            statusBox.Text = String.Format(
                "Depth = {0}\n\n{1}\nnodes searched...\n{2:0.000} nodes/ms",
                _solver.CurrentPly,
                nodes,
                rate
            );
        }

        private void levelButton_Click(object sender, RoutedEventArgs e) {
            int index;
            GridNode level;
            string text = levelBox.Text;

            if (int.TryParse(text, out index) && (level = GridLevels.Get(index)) != null) {
                _level = level;
                _solution = null;
                solveButton.IsEnabled = true;
                mainBox.Text = _level.ToString();
                statusBox.Text = "Level " + text + " loaded.";
            } else {
                statusBox.Text = "Invalid Level '" + text + "'";
            }
        }

        private void solveButton_Click(object sender, RoutedEventArgs e) {
            levelButton.IsEnabled = false;
            solveButton.IsEnabled = false;

            BackgroundWorker worker = new BackgroundWorker();
            worker.DoWork += new DoWorkEventHandler(worker_DoWork);
            worker.RunWorkerCompleted += new RunWorkerCompletedEventHandler(worker_RunWorkerCompleted);

            _statusTimer.Start();
            worker.RunWorkerAsync();
        }

        private void worker_DoWork(object sender, DoWorkEventArgs e) {
            Debug.Assert(_level != null);

            _solverStart = DateTime.Now;
            _solution = _solver.IterativeDeepening(_level);
            _solverFinish = DateTime.Now;
        }

        private void worker_RunWorkerCompleted(object sender, RunWorkerCompletedEventArgs e) {
            _statusTimer.Stop();
            _solutionIndex = 0;

            statusBox.Dispatcher.BeginInvoke(
                new Action(
                    delegate() {
                        if (_solution != null) {
                            statusBox.Text = String.Format(
                                "Solution found!\nDepth = {0}\n\n{1}\nnodes searched.\n{2:0.000} nodes/ms",
                                _solver.CurrentPly,
                                _solver.TotalNodes,
                                _solver.TotalNodes / Math.Max(0.001, (_solverFinish - _solverStart).TotalMilliseconds)
                            );

                            UpdateDisplay();
                        } else {
                            statusBox.Text = String.Format(
                                "Unsolvable!\n\nDepth = {0}\n{1}\nnodes searched.\n{2:0.000} nodes/ms",
                                _solver.CurrentPly,
                                _solver.TotalNodes,
                                _solver.TotalNodes / Math.Max(0.001, (_solverFinish - _solverStart).TotalMilliseconds)
                            );

                            UpdateDisplay();
                        }

                        levelButton.IsEnabled = true;
                    }
                )
            );
        }

        // Update the solution display and navigation buttons
        private void UpdateDisplay() {
            bool forwards, backwards;

            if (_solution == null) {
                forwards = backwards = false;
            } else {
                forwards = _solutionIndex < _solution.Count - 1;
                backwards = _solutionIndex > 0;
                mainBox.Text = _solution[_solutionIndex].ToString();
            }

            firstButton.IsEnabled = backwards;
            prevButton.IsEnabled = backwards;
            playButton.IsEnabled = forwards;
            nextButton.IsEnabled = forwards;
            lastButton.IsEnabled = forwards;
        }

        private void firstButton_Click(object sender, RoutedEventArgs e) {
            Debug.Assert(_solution != null);
            Debug.Assert(_solutionIndex > 0);

            StopAnimation();
            _solutionIndex = 0;
            UpdateDisplay();
        }

        private void prevButton_Click(object sender, RoutedEventArgs e) {
            Debug.Assert(_solution != null);
            Debug.Assert(_solutionIndex > 0);

            StopAnimation();
            _solutionIndex--;
            UpdateDisplay();
        }

        private void nextButton_Click(object sender, RoutedEventArgs e) {
            Debug.Assert(_solution != null);
            Debug.Assert(_solutionIndex < _solution.Count - 1);

            StopAnimation();
            _solutionIndex++;
            UpdateDisplay();
        }

        private void lastButton_Click(object sender, RoutedEventArgs e) {
            Debug.Assert(_solution != null);
            Debug.Assert(_solutionIndex < _solution.Count - 1);

            StopAnimation();
            _solutionIndex = _solution.Count - 1;
            UpdateDisplay();
        }

        private void playButton_Click(object sender, RoutedEventArgs e) {
            Debug.Assert(_solution != null);
            Debug.Assert(_solutionIndex < _solution.Count - 1);

            if ((string)playButton.Content == ">>") {
                StartAnimation();
            } else {
                StopAnimation();
            }
        }

        private void StartAnimation() {
            playButton.Content = "| |";
            _animationTimer.Start();
        }

        private void StopAnimation() {
            _animationTimer.Stop();
            playButton.Content = ">>";
        }

        private void _animationTimer_Tick(object sender, EventArgs e) {
            if (nextButton.IsEnabled) {
                _solutionIndex++;
                UpdateDisplay();
            } else {
                StopAnimation();
            }
        }
    }
}
