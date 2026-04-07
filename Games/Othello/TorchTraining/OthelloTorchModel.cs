using System;
using TorchSharp;
using TorchSharp.Modules;
using static TorchSharp.torch;
using static TorchSharp.torch.nn;

namespace Othello.TorchTraining {
    /// <summary>
    /// TorchSharp Module replicating the hand-rolled OthelloNeuralNetwork architecture.
    /// Architecture: EmbeddingAccumulator(256)+ReLU -> Dense(128)+ReLU -> Dense(1)+Sigmoid
    /// Used for training only; inference uses the hand-rolled OthelloNeuralNetwork.
    /// </summary>
    public class OthelloTorchModel : Module<Tensor, Tensor, Tensor> {
        private readonly ModuleList<Embedding> patternEmbeddings;
        private readonly Linear numericProjection; // [6] -> [256], no bias
        private readonly Parameter bias1;          // [256]
        private readonly Linear dense2;            // [256] -> [128]
        private readonly Linear output;            // [128] -> [1]

        private readonly int numPatternClasses;
        private readonly int numTransforms;
        private readonly int accumulatorSize;

        // Precomputed column indices for gathering per-class pattern indices from the flat input.
        // columnIndices[c] = [c, numClasses+c, 2*numClasses+c, ..., 7*numClasses+c]
        private readonly Tensor[] columnIndices;

        public OthelloTorchModel(int[] numConfigs, int numTransforms = 8,
                                  int accumulatorSize = OthelloNeuralNetwork.AccumulatorSize)
            : base("OthelloTorchModel") {
            this.numPatternClasses = numConfigs.Length;
            this.numTransforms = numTransforms;
            this.accumulatorSize = accumulatorSize;

            var embeddings = new Embedding[numPatternClasses];
            for (int c = 0; c < numPatternClasses; c++) {
                embeddings[c] = Embedding(numConfigs[c], accumulatorSize).to(DeviceType.CUDA);
            }
            patternEmbeddings = ModuleList(embeddings).to(DeviceType.CUDA);

            numericProjection = Linear(OthelloNeuralNetwork.NumNumericFeatures,
                                       accumulatorSize, hasBias: false).to(DeviceType.CUDA);
            bias1 = Parameter(zeros(accumulatorSize));
            dense2 = Linear(accumulatorSize, OthelloNeuralNetwork.Hidden2Size).to(DeviceType.CUDA);
            output = Linear(OthelloNeuralNetwork.Hidden2Size, 1).to(DeviceType.CUDA);

            // Precompute column indices for each pattern class
            columnIndices = new Tensor[numPatternClasses];
            for (int c = 0; c < numPatternClasses; c++) {
                long[] cols = new long[numTransforms];
                for (int s = 0; s < numTransforms; s++) {
                    cols[s] = s * numPatternClasses + c;
                }
                columnIndices[c] = tensor(cols, dtype: ScalarType.Int64).to(DeviceType.CUDA).to(DeviceType.CUDA);
            }

            RegisterComponents();
        }

        /// <summary>
        /// Forward pass.
        /// patternIndices: [batch, numTransforms * numPatternClasses] int64
        /// numericFeatures: [batch, NumNumericFeatures] float32
        /// Returns: [batch, 1] float32 (sigmoid output)
        /// </summary>
        public override Tensor forward(Tensor patternIndices, Tensor numericFeatures) {
            // Layer 1: Embedding accumulator
            // Sum embeddings across all 8 symmetries for each pattern class, then sum across classes
            Tensor acc = bias1.expand(patternIndices.shape[0], -1); // [batch, 256]

            for (int c = 0; c < numPatternClasses; c++) {
                // Gather the 8 symmetry indices for class c: [batch, 8]
                var classIdx = patternIndices.index_select(1, columnIndices[c]);
                // Lookup: [batch, 8, 256]
                var emb = patternEmbeddings[c].call(classIdx);
                // Sum over symmetries: [batch, 256]
                acc = acc + emb.sum(1);
            }

            // Add numeric feature contribution
            acc = acc + numericProjection.call(numericFeatures);

            // ReLU
            acc = functional.relu(acc);

            // Layer 2: Dense 256 -> 128 + ReLU
            var h2 = functional.relu(dense2.call(acc));

            // Output: Dense 128 -> 1 + Sigmoid
            return functional.sigmoid(output.call(h2));
        }

        /// <summary>
        /// Create a TorchSharp model and load weights from an existing hand-rolled network.
        /// </summary>
        public static OthelloTorchModel FromHandRolled(OthelloNeuralNetwork nn) {
            var model = new OthelloTorchModel(nn.NumConfigs);

            using (no_grad()) {
                // Embedding tables: both row-major [numConfigs, accSize], direct copy
                for (int c = 0; c < nn.NumPatternClasses; c++) {
                    var data = tensor(nn.PatternEmbeddings[c],
                        new long[] { nn.NumConfigs[c], OthelloNeuralNetwork.AccumulatorSize });
                    model.patternEmbeddings[c].weight.copy_(data);
                }

                // NumericWeights: hand-rolled [6 * 256] as [f * 256 + j] = weight for (feature f, accum j)
                // This is [6, 256] row-major. PyTorch Linear.weight is [out=256, in=6].
                // So transpose from [6, 256] to [256, 6].
                var nw = tensor(nn.NumericWeights,
                    new long[] { OthelloNeuralNetwork.NumNumericFeatures,
                                 OthelloNeuralNetwork.AccumulatorSize });
                model.numericProjection.weight.copy_(nw.t());

                // Bias1: direct copy [256]
                model.bias1.copy_(tensor(nn.Bias1));

                // W2: hand-rolled column-major [row + col * 256] where row in 0..255, col in 0..127
                // This is [256, 128] column-major = [128, 256] row-major transposed.
                // Read as [256, 128] column-major -> reshape -> transpose to get [128, 256] for PyTorch.
                // Actually: column-major [256 rows, 128 cols] means contiguous memory is column 0 (256 floats),
                // then column 1 (256 floats), etc. In row-major reading, that's [128, 256] transposed.
                // Simplest: read as [128, 256] where each "row" j is the j-th column of the original matrix,
                // then transpose to get [256, 128], then transpose again... Let's be precise:
                //
                // hand-rolled: W2[i + j * 256] = weight connecting accumulator[i] to hidden2[j]
                // PyTorch Linear(256, 128).weight: [128, 256] where weight[j, i] = same connection
                // So: hand-rolled flat -> reshape to [128, 256] reading column-by-column
                //
                // Flat layout: [col0_row0, col0_row1, ..., col0_row255, col1_row0, ...]
                // = [j=0 i=0..255, j=1 i=0..255, ...] = [128 groups of 256]
                // Reshape to [128, 256] gives weight[j, i] = W2[i + j*256]. Exactly what PyTorch wants.
                var w2 = tensor(nn.W2,
                    new long[] { OthelloNeuralNetwork.Hidden2Size, OthelloNeuralNetwork.AccumulatorSize });
                model.dense2.weight.copy_(w2);

                // b2: direct copy [128]
                model.dense2.bias.copy_(tensor(nn.b2));

                // W3: hand-rolled [128] = dot product weights, PyTorch [1, 128]
                model.output.weight.copy_(tensor(nn.W3).unsqueeze(0));

                // b3: scalar
                model.output.bias.copy_(tensor(new float[] { nn.b3 }));
            }

            return model;
        }

        /// <summary>
        /// Export trained weights to a hand-rolled OthelloNeuralNetwork for inference.
        /// </summary>
        public void ExportWeights(OthelloNeuralNetwork nn) {
            using (no_grad()) {
                // Embedding tables: direct copy
                for (int c = 0; c < nn.NumPatternClasses; c++) {
                    var embData = patternEmbeddings[c].weight.data<float>();
                    embData.CopyTo(nn.PatternEmbeddings[c]);
                }

                // NumericWeights: PyTorch [256, 6] -> transpose to [6, 256] -> flatten
                var nw = numericProjection.weight.t().contiguous().data<float>();
                nw.CopyTo(nn.NumericWeights);

                // Bias1
                bias1.data<float>().CopyTo(nn.Bias1);

                // W2: PyTorch [128, 256] -> hand-rolled column-major [256 * 128]
                // PyTorch weight[j, i] = hand-rolled W2[i + j * 256]
                // PyTorch flat (row-major): [j=0 i=0..255, j=1 i=0..255, ...] = column-major [256, 128]
                // Direct copy works!
                dense2.weight.contiguous().data<float>().CopyTo(nn.W2);

                // b2
                dense2.bias.data<float>().CopyTo(nn.b2);

                // W3: PyTorch [1, 128] -> flatten to [128]
                output.weight.contiguous().data<float>().CopyTo(nn.W3);

                // b3
                nn.b3 = output.bias.data<float>()[0];
            }
        }
    }
}
