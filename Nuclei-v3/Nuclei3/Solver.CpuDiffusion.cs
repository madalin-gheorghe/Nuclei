using System;
using System.Threading.Tasks;
using System.Runtime.CompilerServices;

namespace Nuclei3
{
    public partial class Solver
    {
        // Keep contiguous field storage for particle sensors. This specialization
        // preserves the three separable passes and fuses decay into the last one.
        bool tryDiffuseScalarSimd()
        {
#if NET7_0_OR_GREATER
            if (!slimeParticles || antParticles || !denseVoxelGrid || !densityLimitsDisabled ||
                scalarVoxelDensity == null || scalarVoxelScratch == null ||
                !wrapBoundaries || !tridimensional || resX != resY || resY != resZ || resX < 16 ||
                diffuseRange != 1 || diffusionGradual != 1 || diffuse <= 0 || diffuse > 1 ||
                double.IsNaN(diffuse) || double.IsInfinity(diffuse) || decay < 0 ||
                double.IsNaN(decay) || double.IsInfinity(decay) || !System.Numerics.Vector.IsHardwareAccelerated)
                return false;
            int count = getDiffusionAxisOrder(reusableDiffusionAxes);
            if (count != 3) return false;
            // Solver voxels share this store: their density setters already write
            // the current array. Food-aware deposits can mark it non-authoritative
            // even though no copy is needed. Keep synchronization for unbound data.
            if (scalarDensityStore != null && scalarDensityStore.Values == scalarVoxelDensity)
                scalarVoxelDensityAuthoritative = true;
            else
                ensureScalarDensityAuthoritative();
            for (int pass = 0; pass < count; pass++)
            {
                diffuseScalarSimdAxis(scalarVoxelDensity, scalarVoxelScratch, resX,
                    reusableDiffusionAxes[pass], reusableWeights, diffuse, decay, pass == count - 1);
                swapScalarDensityBuffers();
            }
            scalarVoxelDensityDirtyForOutput = true;
            return true;
#else
            return false;
#endif
        }

#if NET7_0_OR_GREATER
        static void diffuseScalarSimdAxis(double[] src, double[] dst, int n, int axis,
            double[] weights, double rate, double decay, bool last)
        {
            const int batch = 32;
            int rows = n * n;
            Parallel.For(0, (rows + batch - 1) / batch, chunk =>
            {
                int width = System.Numerics.Vector<double>.Count;
                var w0 = new System.Numerics.Vector<double>(weights[0]);
                var w1 = new System.Numerics.Vector<double>(weights[1]);
                var w2 = new System.Numerics.Vector<double>(weights[2]);
                var retain = new System.Numerics.Vector<double>(1 - rate);
                var amount = new System.Numerics.Vector<double>(rate);
                var loss = new System.Numerics.Vector<double>(decay);
                int end = Math.Min(rows, (chunk + 1) * batch);
                for (int rowIndex = chunk * batch; rowIndex < end; rowIndex++)
                {
                    int row = rowIndex * n, x = rowIndex / n, y = rowIndex % n;
                    int left = axis == 0 ? ((x + n - 1) % n * n + y) * n : (x * n + (y + n - 1) % n) * n;
                    int right = axis == 0 ? ((x + 1) % n * n + y) * n : (x * n + (y + 1) % n) * n;
                    int z = 0;
                    if (axis == 2)
                    {
                        dst[row] = evaluateScalarSimd(src[row + n - 1], src[row], src[row + 1], weights, rate, decay, last);
                        z = 1;
                    }
                    int stop = axis == 2 ? n - 1 : n;
                    for (; z + width <= stop; z += width)
                    {
                        var center = new System.Numerics.Vector<double>(src, row + z);
                        var sum = new System.Numerics.Vector<double>(src, axis == 2 ? row + z - 1 : left + z) * w0;
                        sum += center * w1;
                        sum += new System.Numerics.Vector<double>(src, axis == 2 ? row + z + 1 : right + z) * w2;
                        var value = System.Numerics.Vector.Min(System.Numerics.Vector<double>.One, center * retain + amount * sum);
                        if (last) value = System.Numerics.Vector.Max(System.Numerics.Vector<double>.Zero, value - loss);
                        value.CopyTo(dst, row + z);
                    }
                    for (; z < stop; z++)
                        dst[row + z] = evaluateScalarSimd(src[axis == 2 ? row + z - 1 : left + z], src[row + z],
                            src[axis == 2 ? row + z + 1 : right + z], weights, rate, decay, last);
                    if (axis == 2)
                        dst[row + n - 1] = evaluateScalarSimd(src[row + n - 2], src[row + n - 1], src[row], weights, rate, decay, last);
                }
            });
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static double evaluateScalarSimd(double left, double center, double right, double[] weights, double rate, double decay, bool last)
        {
            double sum = left * weights[0]; sum += center * weights[1]; sum += right * weights[2];
            double value = Math.Min(1, center * (1 - rate) + rate * sum);
            return last ? Math.Max(0, value - decay) : value;
        }
#endif
    }
}
