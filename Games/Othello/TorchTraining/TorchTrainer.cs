using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using TorchSharp;
using static TorchSharp.torch;

namespace Othello.TorchTraining {
    public static class TorchTrainer {
        private static bool nativeLoaded;

        /// <summary>
        /// Ensure the native libtorch libraries are loaded.
        /// Works around TorchSharp not finding native libs in the runtimes/ subdirectory
        /// when running via 'dotnet run' rather than 'dotnet publish'.
        /// </summary>
        public static void EnsureNativeLoaded() {
            if (nativeLoaded) return;
            nativeLoaded = true;

            torch.InitializeDeviceType(DeviceType.CUDA);
            Console.WriteLine("CUDA is available: {0}", torch.cuda_is_available());

            // NativeLibrary.Load("/Users/ddicato/Downloads/libtorch/lib/libtorch.dylib");

            /*
            // Check if TorchSharp can find the natives on its own first
            string torchSharpDir = Path.GetDirectoryName(typeof(torch).Assembly.Location);
            string libTorchSharp = Path.Combine(torchSharpDir, "libLibTorchSharp.dylib");
            if (File.Exists(libTorchSharp)) return;

            // Look in runtimes/<rid>/native/
            string rid = RuntimeInformation.RuntimeIdentifier;
            string nativeDir = Path.Combine(torchSharpDir, "runtimes", rid, "native");
            if (!Directory.Exists(nativeDir)) {
                // Try well-known RID
                if (RuntimeInformation.ProcessArchitecture == Architecture.Arm64 &&
                    RuntimeInformation.IsOSPlatform(OSPlatform.OSX)) {
                    nativeDir = Path.Combine(torchSharpDir, "runtimes", "osx-arm64", "native");
                }
            }

            if (Directory.Exists(nativeDir)) {
                // Load libtorch first (dependency), then LibTorchSharp
                string torchCpu = Path.Combine(nativeDir, "libtorch_cpu.dylib");
                if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
                    torchCpu = Path.Combine(nativeDir, "libtorch_cpu.so");
                else if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                    torchCpu = Path.Combine(nativeDir, "torch_cpu.dll");

                if (File.Exists(torchCpu)) {
                    NativeLibrary.Load(torchCpu);
                }

                string libTS = Path.Combine(nativeDir, "libLibTorchSharp.dylib");
                if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
                    libTS = Path.Combine(nativeDir, "libLibTorchSharp.so");
                else if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                    libTS = Path.Combine(nativeDir, "LibTorchSharp.dll");

                if (File.Exists(libTS)) {
                    NativeLibrary.Load(libTS);
                }
            }
            */
        }

        /// <summary>
        /// Train the neural network using TorchSharp with Adam optimizer and BCE loss.
        /// Exports weights to the hand-rolled OthelloNeuralNetwork after each epoch.
        /// </summary>
        public static float Train(OthelloNeuralNetwork targetNN,
                                   List<OthelloNeuralNetwork.TrainingSample> data,
                                   TrainingConfig config,
                                   string savePath = null) {
            EnsureNativeLoaded();

            int n = data.Count;
            if (n == 0) {
                Console.WriteLine("  No training data.");
                return float.NaN;
            }

            // Convert training data to tensors
            Console.Write("  Converting {0} samples to tensors...", n);
            var sw = Stopwatch.StartNew();

            int lookupCount = 8 * targetNN.NumPatternClasses; // NumTransforms * NumPatternClasses
            long[] patternData = new long[n * lookupCount];
            float[] numericData = new float[n * OthelloNeuralNetwork.NumNumericFeatures];
            float[] labelData = new float[n];

            for (int i = 0; i < n; i++) {
                var sample = data[i];

                // Pattern indices (int -> long for TorchSharp Embedding)
                for (int j = 0; j < lookupCount; j++) {
                    patternData[i * lookupCount + j] = sample.PatternIndices[j];
                }

                // Numeric features
                for (int j = 0; j < OthelloNeuralNetwork.NumNumericFeatures; j++) {
                    numericData[i * OthelloNeuralNetwork.NumNumericFeatures + j] =
                        sample.NumericFeatures[j];
                }

                labelData[i] = sample.Label;
            }

            var allPatternIndices = tensor(patternData, new long[] { n, lookupCount }, dtype: ScalarType.Int64).to(DeviceType.CUDA);
            var allNumericFeatures = tensor(numericData,
                new long[] { n, OthelloNeuralNetwork.NumNumericFeatures }).to(DeviceType.CUDA);
            var allLabels = tensor(labelData, new long[] { n, 1 }).to(DeviceType.CUDA);

            Console.WriteLine("done ({0:0.000}s)", sw.Elapsed.TotalSeconds);

            // Build model (warm-start from existing weights)
            sw.Restart();
            Console.Write("  Building TorchSharp model...");
            var model = OthelloTorchModel.FromHandRolled(targetNN).to(DeviceType.CUDA);
            Console.WriteLine("done ({0:0.000}s)", sw.Elapsed.TotalSeconds);

            // Optimizer
            var optimizer = optim.Adam(model.parameters(),
                lr: config.LearningRate,
                weight_decay: config.WeightDecay,
                beta1: config.Beta1,
                beta2: config.Beta2,
                eps: config.Epsilon);

            float lastEpochLoss = float.NaN;
            var trainingStopwatch = Stopwatch.StartNew();

            for (int epoch = 0; epoch < config.MaxEpochs; epoch++) {
                var epochStopwatch = Stopwatch.StartNew();
                model.train();

                // Forward + backward on full dataset
                optimizer.zero_grad();
                using var predictions = model.call(allPatternIndices, allNumericFeatures);
                using var loss = nn.functional.binary_cross_entropy(predictions, allLabels);
                loss.backward();
                optimizer.step();

                // Accuracy
                int epochCorrect = 0;
                using (no_grad()) {
                    var preds = predictions.data<float>();
                    var labs = allLabels.data<float>();
                    for (int i = 0; i < n; i++) {
                        float pred = preds[i];
                        float label = labs[i];
                        bool correct;
                        if (label > 0.5f)
                            correct = pred > 0.5f;
                        else if (label < 0.5f)
                            correct = pred < 0.5f;
                        else
                            correct = pred >= 0.4f && pred <= 0.6f;
                        if (correct) epochCorrect++;
                    }
                }

                epochStopwatch.Stop();
                lastEpochLoss = loss.item<float>();
                float accuracy = (float)epochCorrect / n * 100f;
                double epochSeconds = epochStopwatch.Elapsed.TotalSeconds;
                int epochsRemaining = config.MaxEpochs - epoch - 1;
                double etaSeconds = epochSeconds * epochsRemaining;

                Console.Write("\r    Epoch {0,4}: loss = {1:0.000000}  acc = {2:0.0}%  " +
                    "({3:0.0}s, ETA {4})\n",
                    epoch, lastEpochLoss, accuracy,
                    epochSeconds, FormatEta(etaSeconds));

                // Export weights and save checkpoint
                if (savePath != null) {
                    var saveSw = Stopwatch.StartNew();
                    model.ExportWeights(targetNN);
                    targetNN.Save(savePath, config, n, lastEpochLoss);
                    Console.WriteLine("    Saved {0} ({1:0.000}s)", savePath, saveSw.Elapsed.TotalSeconds);
                }
            }

            trainingStopwatch.Stop();
            Console.WriteLine("    Total training time: {0:0.0}s",
                trainingStopwatch.Elapsed.TotalSeconds);

            // Final export
            model.ExportWeights(targetNN);
            return lastEpochLoss;
        }

        private static string FormatEta(double seconds) {
            if (seconds < 60) return $"{seconds:0}s";
            if (seconds < 3600) return $"{seconds / 60:0}m {seconds % 60:0}s";
            return $"{seconds / 3600:0}h {(seconds % 3600) / 60:0}m";
        }
    }
}
