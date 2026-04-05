using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Numerics;

namespace Othello {
    public class TrainingConfig {
        public float LearningRate = 1e-3f;
        public float WeightDecay = 1e-4f;
        public float Beta1 = 0.9f;
        public float Beta2 = 0.999f;
        public float Epsilon = 1e-8f;
        public int MaxEpochs = 200;
        public int BatchSize = 64;
        public int? Seed = null;
    }

    /// <summary>
    /// A 2-hidden-layer feedforward neural network for Othello position evaluation.
    /// Layer 1 uses sparse NNUE-style embedding lookups over one-hot pattern features.
    /// Architecture: EmbeddingAccumulator(256)+ReLU → Dense(128)+ReLU → Dense(1)+Sigmoid
    /// </summary>
    public sealed class OthelloNeuralNetwork {
        public const int AccumulatorSize = 256; // Hidden layer 1 width
        public const int Hidden2Size = 128;
        public const int NumNumericFeatures = 6; // 5 features + 1 normalized piece count

        // --- Layer 1: Sparse embedding accumulator ---
        // Per-pattern-class embedding tables. PatternEmbeddings[c] has size NumConfigs[c] * AccumulatorSize.
        // Row-major: embedding for config k starts at offset k * AccumulatorSize.
        public readonly int NumPatternClasses;
        public readonly int[] NumConfigs;   // 3^(bits in mask) per class
        public readonly int[] MaskBitCount; // number of squares per pattern class
        public float[][] PatternEmbeddings; // [numClasses][numConfigs * AccumulatorSize]

        // Numeric feature weights: NumericWeights[f * AccumulatorSize + j]
        public float[] NumericWeights; // [NumNumericFeatures * AccumulatorSize]
        public float[] Bias1;          // [AccumulatorSize]

        // --- Layer 2: Dense 256 → 128 ---
        // Column-major: W2[row + col * AccumulatorSize]
        public float[] W2, b2;

        // --- Output layer: Dense 128 → 1 ---
        public float[] W3; // [Hidden2Size]
        public float b3;

        private const int ScoreMultiplier = 10000;
        private const int NumTransforms = 8; // board symmetries

        // Cached pattern class masks (copied from OthelloNode.PatternClasses at construction)
        private readonly ulong[] ClassMasks;

        // Thread-local scratch buffers for zero-allocation inference
        [ThreadStatic] private static float[] t_acc;
        [ThreadStatic] private static float[] t_h2;

        /// <summary>
        /// Construct from the current OthelloNode.PatternClasses configuration.
        /// </summary>
        public OthelloNeuralNetwork() {
            NumPatternClasses = OthelloNode.PatternClasses.Length;
            ClassMasks = new ulong[NumPatternClasses];
            NumConfigs = new int[NumPatternClasses];
            MaskBitCount = new int[NumPatternClasses];

            long totalEmbeddingParams = 0;
            for (int c = 0; c < NumPatternClasses; c++) {
                ClassMasks[c] = OthelloNode.PatternClasses[c][0];
                int bits = BitOperations.PopCount(ClassMasks[c]);
                MaskBitCount[c] = bits;
                int configs = 1;
                for (int b = 0; b < bits; b++) configs *= 3;
                NumConfigs[c] = configs;
                totalEmbeddingParams += (long)configs * AccumulatorSize;
            }

            PatternEmbeddings = new float[NumPatternClasses][];
            for (int c = 0; c < NumPatternClasses; c++) {
                PatternEmbeddings[c] = new float[NumConfigs[c] * AccumulatorSize];
            }

            NumericWeights = new float[NumNumericFeatures * AccumulatorSize];
            Bias1 = new float[AccumulatorSize];

            W2 = new float[AccumulatorSize * Hidden2Size];
            b2 = new float[Hidden2Size];
            W3 = new float[Hidden2Size];
            b3 = 0f;

            Console.WriteLine("  NN architecture: {0} pattern classes, {1} total embedding params, " +
                "{2} dense params",
                NumPatternClasses,
                totalEmbeddingParams,
                NumericWeights.Length + Bias1.Length + W2.Length + b2.Length + W3.Length + 1);
            for (int c = 0; c < NumPatternClasses; c++) {
                Console.WriteLine("    Class {0}: {1} bits, {2} configs, {3} embedding floats ({4:0.0} MB)",
                    c, MaskBitCount[c], NumConfigs[c],
                    PatternEmbeddings[c].Length,
                    PatternEmbeddings[c].Length * 4.0 / 1024 / 1024);
            }
        }

        /// <summary>
        /// Construct with explicit configuration (for deserialization).
        /// </summary>
        private OthelloNeuralNetwork(int numClasses, int[] numConfigs, int[] maskBitCount, ulong[] classMasks) {
            NumPatternClasses = numClasses;
            ClassMasks = classMasks;
            NumConfigs = numConfigs;
            MaskBitCount = maskBitCount;

            PatternEmbeddings = new float[numClasses][];
            for (int c = 0; c < numClasses; c++) {
                PatternEmbeddings[c] = new float[numConfigs[c] * AccumulatorSize];
            }

            NumericWeights = new float[NumNumericFeatures * AccumulatorSize];
            Bias1 = new float[AccumulatorSize];

            W2 = new float[AccumulatorSize * Hidden2Size];
            b2 = new float[Hidden2Size];
            W3 = new float[Hidden2Size];
            b3 = 0f;
        }

        #region Weight Initialization

        public void InitializeWeights(Random rng) {
            // He initialization for the embedding layer.
            // Effective fan_in: number of active embeddings summed into the accumulator.
            // That's NumTransforms * NumPatternClasses (one lookup per symmetry per class).
            int effectiveFanIn = NumTransforms * NumPatternClasses + NumNumericFeatures;
            float scale1 = MathF.Sqrt(2.0f / effectiveFanIn);

            for (int c = 0; c < NumPatternClasses; c++) {
                float[] emb = PatternEmbeddings[c];
                for (int i = 0; i < emb.Length; i++) {
                    emb[i] = NextGaussian(rng) * scale1;
                }
            }

            for (int i = 0; i < NumericWeights.Length; i++) {
                NumericWeights[i] = NextGaussian(rng) * scale1;
            }
            Array.Clear(Bias1);

            float scale2 = MathF.Sqrt(2.0f / AccumulatorSize);
            for (int i = 0; i < W2.Length; i++) {
                W2[i] = NextGaussian(rng) * scale2;
            }
            Array.Clear(b2);

            // Xavier for sigmoid output layer
            float scale3 = MathF.Sqrt(1.0f / Hidden2Size);
            for (int i = 0; i < W3.Length; i++) {
                W3[i] = NextGaussian(rng) * scale3;
            }
            b3 = 0f;
        }

        private static float NextGaussian(Random rng) {
            double u1 = 1.0 - rng.NextDouble();
            double u2 = rng.NextDouble();
            return (float)(Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Cos(2.0 * Math.PI * u2));
        }

        #endregion

        #region Ternary Index

        /// <summary>
        /// Compute a ternary index for a pattern configuration.
        /// Each square in the mask contributes a ternary digit: 0=empty, 1=self, 2=other.
        /// Iterates bits LSB-first for consistent ordering.
        /// </summary>
        public static int TernaryIndex(ulong selfMasked, ulong otherMasked, ulong mask) {
            int index = 0;
            while (mask != 0) {
                ulong bit = mask & (ulong)(-(long)mask); // isolate lowest set bit
                index *= 3;
                if ((selfMasked & bit) != 0) index += 1;
                else if ((otherMasked & bit) != 0) index += 2;
                mask &= mask - 1; // clear lowest set bit
            }
            return index;
        }

        #endregion

        #region Forward Pass

        /// <summary>
        /// Evaluate an OthelloNode, returning an int score compatible with PatternScore().
        /// Zero-allocation on the hot path (uses thread-local buffers).
        /// </summary>
        public int Evaluate(OthelloNode node) {
            float[] acc = t_acc ??= new float[AccumulatorSize];
            float[] h2 = t_h2 ??= new float[Hidden2Size];

            float logit = ForwardLogit(node, acc, h2);
            return (int)Math.Round(logit * ScoreMultiplier);
        }

        /// <summary>
        /// Compute the raw logit (pre-sigmoid) for a board position.
        /// </summary>
        private float ForwardLogit(OthelloNode node, float[] acc, float[] h2) {
            // Layer 1: Embedding accumulator
            // Start with bias
            Array.Copy(Bias1, acc, AccumulatorSize);

            // Add pattern embeddings (8 symmetries × numClasses lookups)
            OthelloNode.BoardSymmetries sym = OthelloNode.GetBoardSymmetries(
                node.PlayerBoard, node.OtherBoard);
            for (int s = 0; s < NumTransforms; s++) {
                sym.GetPair(s, out ulong self, out ulong other);
                for (int c = 0; c < NumPatternClasses; c++) {
                    ulong mask = ClassMasks[c];
                    int idx = TernaryIndex(self & mask, other & mask, mask);
                    AddEmbedding(PatternEmbeddings[c], idx, acc);
                }
            }

            // Add numeric feature contributions
            int pieceCount = node.OccupiedSquareCount;
            float[] numericValues = stackalloc_workaround(node, pieceCount);
            for (int f = 0; f < NumNumericFeatures; f++) {
                AddScaled(NumericWeights, f * AccumulatorSize, numericValues[f], acc);
            }

            // ReLU
            for (int i = 0; i < AccumulatorSize; i++) {
                if (acc[i] < 0f) acc[i] = 0f;
            }

            // Layer 2: Dense 256 → 128 + ReLU
            MatVecAddRelu(W2, b2, acc, h2, AccumulatorSize, Hidden2Size);

            // Output: Dense 128 → 1
            return DotProduct(W3, h2, Hidden2Size) + b3;
        }

        /// <summary>
        /// Compute the 6 numeric feature values for a node.
        /// Returns a temporary array (not allocated per call in practice).
        /// </summary>
        [ThreadStatic] private static float[] t_numericBuf;
        private static float[] stackalloc_workaround(OthelloNode node, int pieceCount) {
            float[] buf = t_numericBuf ??= new float[NumNumericFeatures];
            for (int i = 0; i < OthelloNode.Features.Length; i++) {
                buf[i] = OthelloNode.Features[i](node);
            }
            buf[OthelloNode.Features.Length] = (pieceCount - 32f) / 32f;
            return buf;
        }

        /// <summary>
        /// acc[j] += embeddings[idx * AccumulatorSize + j] for j in 0..AccumulatorSize-1.
        /// </summary>
        private static void AddEmbedding(float[] embeddings, int idx, float[] acc) {
            int offset = idx * AccumulatorSize;
            int vecSize = Vector<float>.Count;
            int j = 0;
            for (; j + vecSize <= AccumulatorSize; j += vecSize) {
                var vA = new Vector<float>(acc, j);
                var vE = new Vector<float>(embeddings, offset + j);
                (vA + vE).CopyTo(acc, j);
            }
            for (; j < AccumulatorSize; j++) {
                acc[j] += embeddings[offset + j];
            }
        }

        /// <summary>
        /// acc[j] += weights[offset + j] * scalar for j in 0..AccumulatorSize-1.
        /// </summary>
        private static void AddScaled(float[] weights, int offset, float scalar, float[] acc) {
            int vecSize = Vector<float>.Count;
            var vS = new Vector<float>(scalar);
            int j = 0;
            for (; j + vecSize <= AccumulatorSize; j += vecSize) {
                var vA = new Vector<float>(acc, j);
                var vW = new Vector<float>(weights, offset + j);
                (vA + vW * vS).CopyTo(acc, j);
            }
            for (; j < AccumulatorSize; j++) {
                acc[j] += weights[offset + j] * scalar;
            }
        }

        private static void MatVecAddRelu(float[] matrix, float[] bias, float[] input,
                                          float[] output, int rows, int cols) {
            int vecSize = Vector<float>.Count;
            for (int j = 0; j < cols; j++) {
                int offset = j * rows;
                float sum = bias[j];
                int i = 0;
                for (; i + vecSize <= rows; i += vecSize) {
                    sum += Vector.Dot(new Vector<float>(matrix, offset + i),
                                      new Vector<float>(input, i));
                }
                for (; i < rows; i++) {
                    sum += matrix[offset + i] * input[i];
                }
                output[j] = sum > 0f ? sum : 0f;
            }
        }

        private static float DotProduct(float[] a, float[] b, int length) {
            int vecSize = Vector<float>.Count;
            float sum = 0f;
            int i = 0;
            for (; i + vecSize <= length; i += vecSize) {
                sum += Vector.Dot(new Vector<float>(a, i), new Vector<float>(b, i));
            }
            for (; i < length; i++) {
                sum += a[i] * b[i];
            }
            return sum;
        }

        private static float Sigmoid(float x) {
            return 1.0f / (1.0f + MathF.Exp(-x));
        }

        #endregion

        #region Training (Backpropagation + Adam)

        /// <summary>
        /// Pre-computed training sample: ternary indices for all (symmetry, class) pairs,
        /// numeric features, and label. Avoids redundant feature extraction during training.
        /// </summary>
        public struct TrainingSample {
            public int[] PatternIndices; // [NumTransforms * NumPatternClasses]
            public float[] NumericFeatures; // [NumNumericFeatures]
            public float Label;
        }

        /// <summary>
        /// Extract pre-computed training data from the playbook.
        /// </summary>
        public List<TrainingSample> ExtractTrainingData() {
            var entries = OthelloNode.Playbook?.ToList();
            if (entries == null || entries.Count == 0) {
                Console.WriteLine("  No playbook entries for NN training.");
                return [];
            }

            int lookupCount = NumTransforms * NumPatternClasses;
            var data = new List<TrainingSample>(entries.Count);

            // Coverage tracking: how many configs per class are seen in training data
            int[][] configFrequency = new int[NumPatternClasses][];
            for (int c = 0; c < NumPatternClasses; c++) {
                configFrequency[c] = new int[NumConfigs[c]];
            }

            Console.Write("  Extracting NN training data from {0} playbook entries...", entries.Count);
            DateTime start = DateTime.Now;

            foreach (var entry in entries) {
                OthelloNode node = entry.Key;
                int score = entry.Value;

                float label;
                if (score > 0) label = 0.99f;
                else if (score < 0) label = 0.01f;
                else label = 0.5f;

                int[] patternIndices = new int[lookupCount];
                OthelloNode.BoardSymmetries sym = OthelloNode.GetBoardSymmetries(
                    node.PlayerBoard, node.OtherBoard);

                for (int s = 0; s < NumTransforms; s++) {
                    sym.GetPair(s, out ulong self, out ulong other);
                    for (int c = 0; c < NumPatternClasses; c++) {
                        ulong mask = ClassMasks[c];
                        int idx = TernaryIndex(self & mask, other & mask, mask);
                        patternIndices[s * NumPatternClasses + c] = idx;
                        configFrequency[c][idx]++;
                    }
                }

                int pieceCount = node.OccupiedSquareCount;
                float[] numericFeatures = new float[NumNumericFeatures];
                for (int i = 0; i < OthelloNode.Features.Length; i++) {
                    numericFeatures[i] = OthelloNode.Features[i](node);
                }
                numericFeatures[OthelloNode.Features.Length] = (pieceCount - 32f) / 32f;

                data.Add(new TrainingSample {
                    PatternIndices = patternIndices,
                    NumericFeatures = numericFeatures,
                    Label = label,
                });
            }

            Console.WriteLine("done ({0:0.000}s)", (DateTime.Now - start).TotalSeconds);

            // Print pattern coverage report
            Console.WriteLine("  Pattern coverage ({0} positions × {1} symmetries = {2} lookups per class):",
                data.Count, NumTransforms, (long)data.Count * NumTransforms);
            for (int c = 0; c < NumPatternClasses; c++) {
                int[] freq = configFrequency[c];
                int seen = 0, seenOnce = 0, seenFewTimes = 0;
                int maxFreq = 0;
                for (int i = 0; i < freq.Length; i++) {
                    if (freq[i] > 0) {
                        seen++;
                        if (freq[i] == 1) seenOnce++;
                        else if (freq[i] <= 5) seenFewTimes++;
                        if (freq[i] > maxFreq) maxFreq = freq[i];
                    }
                }
                Console.WriteLine("    Class {0,2} ({1,2} bits, {2,6} configs): {3,6} seen ({4,5:0.0}%), " +
                    "{5} once, {6} 2-5x, max {7}",
                    c, MaskBitCount[c], NumConfigs[c], seen,
                    100.0 * seen / NumConfigs[c],
                    seenOnce, seenFewTimes, maxFreq);
            }

            return data;
        }

        /// <summary>
        /// Train the network using mini-batch Adam with cross-entropy loss.
        /// If savePath is provided, saves weights after every epoch for crash-safe checkpointing.
        /// Returns the final epoch loss.
        /// </summary>
        public float Train(List<TrainingSample> data, TrainingConfig config,
                           string savePath = null) {
            Random rng = config.Seed.HasValue ? new Random(config.Seed.Value) : Random.Shared;
            int n = data.Count;
            if (n == 0) {
                Console.WriteLine("  No training data.");
                return float.NaN;
            }

            int lookupCount = NumTransforms * NumPatternClasses;

            // Adam moment accumulators for embedding tables
            float[][] mEmb = new float[NumPatternClasses][];
            float[][] vEmb = new float[NumPatternClasses][];
            for (int c = 0; c < NumPatternClasses; c++) {
                mEmb[c] = new float[PatternEmbeddings[c].Length];
                vEmb[c] = new float[PatternEmbeddings[c].Length];
            }
            float[] mNW = new float[NumericWeights.Length], vNW = new float[NumericWeights.Length];
            float[] mB1 = new float[Bias1.Length], vB1 = new float[Bias1.Length];
            float[] mW2 = new float[W2.Length], vW2 = new float[W2.Length];
            float[] mb2 = new float[b2.Length], vb2 = new float[b2.Length];
            float[] mW3 = new float[W3.Length], vW3 = new float[W3.Length];
            float mb3 = 0f, vb3 = 0f;

            // Gradient accumulators for embedding tables (sparse — only touched entries)
            float[][] gEmb = new float[NumPatternClasses][];
            for (int c = 0; c < NumPatternClasses; c++) {
                gEmb[c] = new float[PatternEmbeddings[c].Length];
            }
            // Track which embedding rows were touched this batch for efficient clearing
            HashSet<int>[] touchedRows = new HashSet<int>[NumPatternClasses];
            for (int c = 0; c < NumPatternClasses; c++) {
                touchedRows[c] = new HashSet<int>();
            }

            float[] gNW = new float[NumericWeights.Length], gB1 = new float[Bias1.Length];
            float[] gW2 = new float[W2.Length], gb2 = new float[b2.Length];
            float[] gW3 = new float[W3.Length];
            float gb3;

            // Per-sample scratch buffers
            float[] z1 = new float[AccumulatorSize];  // pre-ReLU accumulator
            float[] a1 = new float[AccumulatorSize];  // post-ReLU
            float[] z2 = new float[Hidden2Size];
            float[] a2 = new float[Hidden2Size];
            float[] d2 = new float[Hidden2Size];
            float[] d1 = new float[AccumulatorSize];

            int[] indices = new int[n];
            for (int i = 0; i < n; i++) indices[i] = i;

            int globalStep = 0;
            float lastEpochLoss = float.NaN;
            int totalBatches = (n + config.BatchSize - 1) / config.BatchSize;
            var trainingStopwatch = Stopwatch.StartNew();

            for (int epoch = 0; epoch < config.MaxEpochs; epoch++) {
                var epochStopwatch = Stopwatch.StartNew();

                // Fisher-Yates shuffle
                for (int i = n - 1; i > 0; i--) {
                    int j = rng.Next(i + 1);
                    (indices[i], indices[j]) = (indices[j], indices[i]);
                }

                float epochLoss = 0f;
                int epochCorrect = 0;
                int batchIndex = 0;
                int lastProgressTenths = -1;

                for (int batchStart = 0; batchStart < n; batchStart += config.BatchSize) {
                    int batchEnd = Math.Min(batchStart + config.BatchSize, n);
                    int batchSize = batchEnd - batchStart;

                    // Intra-epoch progress (report every 0.1%)
                    int progressTenths = (int)((long)batchIndex * 1000 / totalBatches);
                    if (progressTenths > lastProgressTenths) {
                        Console.Write("\r    Epoch {0,4}: {1,5:0.0}%", epoch, progressTenths / 10.0);
                        lastProgressTenths = progressTenths;
                    }
                    batchIndex++;

                    // Zero gradients (sparse clear for embeddings)
                    for (int c = 0; c < NumPatternClasses; c++) {
                        foreach (int row in touchedRows[c]) {
                            Array.Clear(gEmb[c], row * AccumulatorSize, AccumulatorSize);
                        }
                        touchedRows[c].Clear();
                    }
                    Array.Clear(gNW); Array.Clear(gB1);
                    Array.Clear(gW2); Array.Clear(gb2);
                    Array.Clear(gW3);
                    gb3 = 0f;

                    for (int s = batchStart; s < batchEnd; s++) {
                        TrainingSample sample = data[indices[s]];

                        // === Forward pass ===

                        // Layer 1: Embedding accumulation
                        Array.Copy(Bias1, z1, AccumulatorSize);

                        for (int t = 0; t < NumTransforms; t++) {
                            for (int c = 0; c < NumPatternClasses; c++) {
                                int idx = sample.PatternIndices[t * NumPatternClasses + c];
                                int off = idx * AccumulatorSize;
                                float[] emb = PatternEmbeddings[c];
                                for (int j = 0; j < AccumulatorSize; j++) {
                                    z1[j] += emb[off + j];
                                }
                            }
                        }

                        for (int f = 0; f < NumNumericFeatures; f++) {
                            float val = sample.NumericFeatures[f];
                            int off = f * AccumulatorSize;
                            for (int j = 0; j < AccumulatorSize; j++) {
                                z1[j] += NumericWeights[off + j] * val;
                            }
                        }

                        // ReLU
                        for (int j = 0; j < AccumulatorSize; j++) {
                            a1[j] = z1[j] > 0f ? z1[j] : 0f;
                        }

                        // Layer 2
                        for (int j = 0; j < Hidden2Size; j++) {
                            int off = j * AccumulatorSize;
                            float sum = b2[j];
                            for (int i = 0; i < AccumulatorSize; i++) {
                                sum += W2[off + i] * a1[i];
                            }
                            z2[j] = sum;
                            a2[j] = sum > 0f ? sum : 0f;
                        }

                        // Output
                        float z3 = b3;
                        for (int i = 0; i < Hidden2Size; i++) {
                            z3 += W3[i] * a2[i];
                        }
                        float output = Sigmoid(z3);

                        // Loss
                        float clamped = Math.Clamp(output, 1e-7f, 1f - 1e-7f);
                        epochLoss += -(sample.Label * MathF.Log(clamped) +
                                      (1f - sample.Label) * MathF.Log(1f - clamped));

                        // Accuracy
                        bool correct;
                        if (sample.Label > 0.5f)
                            correct = output > 0.5f;
                        else if (sample.Label < 0.5f)
                            correct = output < 0.5f;
                        else
                            correct = output >= 0.4f && output <= 0.6f;
                        if (correct) epochCorrect++;

                        // === Backward pass ===

                        float dz3 = output - sample.Label;

                        // Output layer gradients
                        for (int i = 0; i < Hidden2Size; i++) {
                            gW3[i] += dz3 * a2[i];
                        }
                        gb3 += dz3;

                        // Layer 2 delta
                        for (int i = 0; i < Hidden2Size; i++) {
                            d2[i] = z2[i] > 0f ? dz3 * W3[i] : 0f;
                        }

                        // Layer 2 weight gradients
                        for (int j = 0; j < Hidden2Size; j++) {
                            int off = j * AccumulatorSize;
                            float dj = d2[j];
                            for (int i = 0; i < AccumulatorSize; i++) {
                                gW2[off + i] += dj * a1[i];
                            }
                            gb2[j] += dj;
                        }

                        // Layer 1 delta: d1 = W2 * d2, masked by ReLU'(z1)
                        Array.Clear(d1);
                        for (int j = 0; j < Hidden2Size; j++) {
                            int off = j * AccumulatorSize;
                            float dj = d2[j];
                            for (int i = 0; i < AccumulatorSize; i++) {
                                d1[i] += dj * W2[off + i];
                            }
                        }
                        for (int i = 0; i < AccumulatorSize; i++) {
                            if (z1[i] <= 0f) d1[i] = 0f;
                        }

                        // Embedding gradients (sparse: only the looked-up rows)
                        for (int t = 0; t < NumTransforms; t++) {
                            for (int c = 0; c < NumPatternClasses; c++) {
                                int idx = sample.PatternIndices[t * NumPatternClasses + c];
                                int off = idx * AccumulatorSize;
                                float[] ge = gEmb[c];
                                touchedRows[c].Add(idx);
                                for (int j = 0; j < AccumulatorSize; j++) {
                                    ge[off + j] += d1[j];
                                }
                            }
                        }

                        // Numeric weight gradients
                        for (int f = 0; f < NumNumericFeatures; f++) {
                            float val = sample.NumericFeatures[f];
                            int off = f * AccumulatorSize;
                            for (int j = 0; j < AccumulatorSize; j++) {
                                gNW[off + j] += d1[j] * val;
                            }
                        }

                        // Bias1 gradient
                        for (int j = 0; j < AccumulatorSize; j++) {
                            gB1[j] += d1[j];
                        }
                    }

                    // Adam updates
                    globalStep++;
                    float scale = 1.0f / batchSize;

                    // Sparse Adam for embeddings (only update touched rows)
                    for (int c = 0; c < NumPatternClasses; c++) {
                        foreach (int row in touchedRows[c]) {
                            AdamUpdateRange(PatternEmbeddings[c], gEmb[c], mEmb[c], vEmb[c],
                                row * AccumulatorSize, AccumulatorSize,
                                scale, globalStep, config);
                        }
                    }

                    AdamUpdate(NumericWeights, gNW, mNW, vNW, scale, globalStep, config);
                    AdamUpdate(Bias1, gB1, mB1, vB1, scale, globalStep, config);
                    AdamUpdate(W2, gW2, mW2, vW2, scale, globalStep, config);
                    AdamUpdate(b2, gb2, mb2, vb2, scale, globalStep, config);
                    AdamUpdate(W3, gW3, mW3, vW3, scale, globalStep, config);

                    // Scalar Adam for b3
                    {
                        float g = gb3 * scale + config.WeightDecay * b3;
                        mb3 = config.Beta1 * mb3 + (1f - config.Beta1) * g;
                        vb3 = config.Beta2 * vb3 + (1f - config.Beta2) * g * g;
                        float mHat = mb3 / (1f - MathF.Pow(config.Beta1, globalStep));
                        float vHat = vb3 / (1f - MathF.Pow(config.Beta2, globalStep));
                        b3 -= config.LearningRate * mHat / (MathF.Sqrt(vHat) + config.Epsilon);
                    }
                }

                epochStopwatch.Stop();
                lastEpochLoss = epochLoss / n;
                float accuracy = (float)epochCorrect / n * 100f;
                double epochSeconds = epochStopwatch.Elapsed.TotalSeconds;
                int epochsRemaining = config.MaxEpochs - epoch - 1;
                double etaSeconds = epochSeconds * epochsRemaining;

                Console.Write("\r    Epoch {0,4}: loss = {1:0.000000}  acc = {2:0.0}%  " +
                    "({3:0.0}s, ETA {4})\n",
                    epoch, lastEpochLoss, accuracy,
                    epochSeconds, FormatEta(etaSeconds));

                if (savePath != null) {
                    Save(savePath, config, n, lastEpochLoss);
                }
            }

            trainingStopwatch.Stop();
            Console.WriteLine("    Total training time: {0:0.0}s", trainingStopwatch.Elapsed.TotalSeconds);

            return lastEpochLoss;
        }

        private static void AdamUpdate(float[] weights, float[] gradients,
                                        float[] m, float[] v,
                                        float gradScale, int t, TrainingConfig config) {
            AdamUpdateRange(weights, gradients, m, v, 0, weights.Length, gradScale, t, config);
        }

        private static void AdamUpdateRange(float[] weights, float[] gradients,
                                             float[] m, float[] v,
                                             int start, int count,
                                             float gradScale, int t, TrainingConfig config) {
            float beta1 = config.Beta1, beta2 = config.Beta2;
            float lr = config.LearningRate;
            float eps = config.Epsilon;
            float wd = config.WeightDecay;
            float mCorrection = 1f / (1f - MathF.Pow(beta1, t));
            float vCorrection = 1f / (1f - MathF.Pow(beta2, t));

            int end = start + count;
            for (int i = start; i < end; i++) {
                float g = gradients[i] * gradScale + wd * weights[i];
                m[i] = beta1 * m[i] + (1f - beta1) * g;
                v[i] = beta2 * v[i] + (1f - beta2) * g * g;
                float mHat = m[i] * mCorrection;
                float vHat = v[i] * vCorrection;
                weights[i] -= lr * mHat / (MathF.Sqrt(vHat) + eps);
            }
        }

        private static string FormatEta(double seconds) {
            if (seconds < 60) return $"{seconds:0}s";
            if (seconds < 3600) return $"{seconds / 60:0}m {seconds % 60:0}s";
            return $"{seconds / 3600:0}h {(seconds % 3600) / 60:0}m";
        }

        #endregion

        #region Serialization

        public void Save(string path, TrainingConfig config, int trainingExamples, float finalLoss) {
            using var writer = new StreamWriter(path, false);

            writer.WriteLine("# Training Hyperparameters");
            writer.WriteLine("# LearningRate {0}", config.LearningRate);
            writer.WriteLine("# WeightDecay {0}", config.WeightDecay);
            writer.WriteLine("# Beta1 {0}", config.Beta1);
            writer.WriteLine("# Beta2 {0}", config.Beta2);
            writer.WriteLine("# Epsilon {0}", config.Epsilon);
            writer.WriteLine("# MaxEpochs {0}", config.MaxEpochs);
            writer.WriteLine("# BatchSize {0}", config.BatchSize);
            writer.WriteLine("# TrainingExamples {0}", trainingExamples);
            writer.WriteLine("# FinalLoss {0:R}", finalLoss);
            writer.WriteLine();

            writer.WriteLine("NeuralNetwork");
            writer.WriteLine("NumPatternClasses {0}", NumPatternClasses);
            writer.WriteLine("AccumulatorSize {0}", AccumulatorSize);
            writer.WriteLine("Hidden2Size {0}", Hidden2Size);
            writer.WriteLine("NumNumericFeatures {0}", NumNumericFeatures);

            // Pattern class metadata
            writer.Write("MaskBitCounts");
            for (int c = 0; c < NumPatternClasses; c++) {
                writer.Write(" {0}", MaskBitCount[c]);
            }
            writer.WriteLine();

            writer.Write("NumConfigs");
            for (int c = 0; c < NumPatternClasses; c++) {
                writer.Write(" {0}", NumConfigs[c]);
            }
            writer.WriteLine();

            // Embedding tables
            for (int c = 0; c < NumPatternClasses; c++) {
                WriteArray(writer, $"PatternEmbedding_{c}", PatternEmbeddings[c]);
            }

            WriteArray(writer, "NumericWeights", NumericWeights);
            WriteArray(writer, "Bias1", Bias1);
            WriteArray(writer, "W2", W2);
            WriteArray(writer, "b2", b2);
            WriteArray(writer, "W3", W3);
            writer.WriteLine("b3");
            writer.WriteLine("{0:R}", b3);
        }

        public static OthelloNeuralNetwork Load(string path) {
            using var reader = new StreamReader(path);

            string line;
            while ((line = reader.ReadLine()) != null) {
                line = line.Trim();
                if (line == "NeuralNetwork") break;
            }
            if (line != "NeuralNetwork") {
                throw new FormatException("Expected 'NeuralNetwork' header in " + path);
            }

            int numClasses = ReadInt(reader, "NumPatternClasses");
            int accSize = ReadInt(reader, "AccumulatorSize");
            int h2Size = ReadInt(reader, "Hidden2Size");
            int numNumeric = ReadInt(reader, "NumNumericFeatures");

            if (accSize != AccumulatorSize || h2Size != Hidden2Size) {
                throw new FormatException(
                    $"Architecture mismatch: file has acc={accSize}, h2={h2Size}; " +
                    $"code expects acc={AccumulatorSize}, h2={Hidden2Size}");
            }

            int[] maskBitCount = ReadIntArray(reader, "MaskBitCounts", numClasses);
            int[] numConfigs = ReadIntArray(reader, "NumConfigs", numClasses);

            // Reconstruct class masks from current OthelloNode.PatternClasses
            if (numClasses != OthelloNode.PatternClasses.Length) {
                throw new FormatException(
                    $"Pattern class count mismatch: file has {numClasses}, " +
                    $"current config has {OthelloNode.PatternClasses.Length}");
            }
            ulong[] classMasks = new ulong[numClasses];
            for (int c = 0; c < numClasses; c++) {
                classMasks[c] = OthelloNode.PatternClasses[c][0];
            }

            var nn = new OthelloNeuralNetwork(numClasses, numConfigs, maskBitCount, classMasks);

            for (int c = 0; c < numClasses; c++) {
                ReadArray(reader, $"PatternEmbedding_{c}", nn.PatternEmbeddings[c]);
            }

            ReadArray(reader, "NumericWeights", nn.NumericWeights);
            ReadArray(reader, "Bias1", nn.Bias1);
            ReadArray(reader, "W2", nn.W2);
            ReadArray(reader, "b2", nn.b2);
            ReadArray(reader, "W3", nn.W3);

            ExpectHeader(reader, "b3");
            nn.b3 = float.Parse(reader.ReadLine().Trim());

            long totalParams = nn.Bias1.Length + nn.NumericWeights.Length +
                nn.W2.Length + nn.b2.Length + nn.W3.Length + 1;
            for (int c = 0; c < numClasses; c++) {
                totalParams += nn.PatternEmbeddings[c].Length;
            }
            Console.WriteLine("Loaded neural network from {0} ({1} classes, {2} params, {3:0.0} MB)",
                path, numClasses, totalParams, totalParams * 4.0 / 1024 / 1024);

            return nn;
        }

        private static void WriteArray(StreamWriter writer, string name, float[] array) {
            writer.WriteLine(name);
            const int valuesPerLine = 16;
            for (int i = 0; i < array.Length; i += valuesPerLine) {
                int end = Math.Min(i + valuesPerLine, array.Length);
                for (int j = i; j < end; j++) {
                    if (j > i) writer.Write(' ');
                    writer.Write("{0:R}", array[j]);
                }
                writer.WriteLine();
            }
        }

        private static void ReadArray(StreamReader reader, string expectedName, float[] array) {
            ExpectHeader(reader, expectedName);
            int read = 0;
            while (read < array.Length) {
                string line = reader.ReadLine();
                if (line == null) throw new FormatException("Unexpected EOF reading " + expectedName);
                string[] parts = line.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
                for (int i = 0; i < parts.Length && read < array.Length; i++) {
                    array[read++] = float.Parse(parts[i]);
                }
            }
        }

        private static int ReadInt(StreamReader reader, string expectedPrefix) {
            string line = reader.ReadLine()?.Trim();
            if (line == null || !line.StartsWith(expectedPrefix)) {
                throw new FormatException("Expected '" + expectedPrefix + "', got: " + line);
            }
            return int.Parse(line.Substring(expectedPrefix.Length).Trim());
        }

        private static int[] ReadIntArray(StreamReader reader, string expectedPrefix, int count) {
            string line = reader.ReadLine()?.Trim();
            if (line == null || !line.StartsWith(expectedPrefix)) {
                throw new FormatException("Expected '" + expectedPrefix + "', got: " + line);
            }
            string[] parts = line.Substring(expectedPrefix.Length).Trim()
                .Split(' ', StringSplitOptions.RemoveEmptyEntries);
            int[] result = new int[count];
            for (int i = 0; i < count; i++) {
                result[i] = int.Parse(parts[i]);
            }
            return result;
        }

        private static void ExpectHeader(StreamReader reader, string expected) {
            string line = reader.ReadLine()?.Trim();
            if (line != expected) {
                throw new FormatException("Expected '" + expected + "', got: " + line);
            }
        }

        #endregion
    }
}
