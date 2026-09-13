using System;
using System.Threading.Tasks;
#if NET7_0_OR_GREATER
using System.Numerics;
#endif

namespace Nuclei3
{
    public partial class Solver
    {
#if NET7_0_OR_GREATER
        double[] antSimdFood, antSimdHome, antSimdFoodScratch, antSimdHomeScratch;
#endif

        // Voxel fields remain the public source of truth. Import/export once per
        // step, rather than chasing voxel objects for every stencil and axis.
        bool tryDiffuseAntSimd()
        {
#if NET7_0_OR_GREATER
            if (!denseVoxelGrid || !densityLimitsDisabled || !wrapBoundaries ||
                voxelFlat.Length < 4096 || !Vector.IsHardwareAccelerated ||
                !(foodDiffuseRate > 0 || baseDiffuseRate > 0 || antDiffusionGradual < 1) ||
                !double.IsFinite(foodDiffuseRate) || !double.IsFinite(baseDiffuseRate) ||
                !double.IsFinite(foodDecayRate) || !double.IsFinite(baseDecayRate)) return false;
            int passes = getDiffusionAxisOrder(reusableDiffusionAxes);
            if (passes == 0) return false;
            int size = voxelFlat.Length;
            if (antSimdFood == null || antSimdFood.Length != size)
            {
                antSimdFood = new double[size]; antSimdHome = new double[size];
                antSimdFoodScratch = new double[size]; antSimdHomeScratch = new double[size];
            }
            const int batch = 4096;
            Parallel.For(0, (size + batch - 1) / batch, chunk =>
            {
                int end = Math.Min(size, (chunk + 1) * batch);
                for (int i = chunk * batch; i < end; i++)
                {
                    antSimdFood[i] = voxelFlat[i].towardsFoodPheromone;
                    antSimdHome[i] = voxelFlat[i].towardsBasePheromone;
                }
            });
            for (int pass = 0; pass < passes; pass++)
            {
                diffuseAntSimdAxis(reusableDiffusionAxes[pass], pass == passes - 1);
                var swap = antSimdFood; antSimdFood = antSimdFoodScratch; antSimdFoodScratch = swap;
                swap = antSimdHome; antSimdHome = antSimdHomeScratch; antSimdHomeScratch = swap;
            }
            Parallel.For(0, (size + batch - 1) / batch, chunk =>
            {
                int end = Math.Min(size, (chunk + 1) * batch);
                for (int i = chunk * batch; i < end; i++)
                {
                    voxelFlat[i].towardsFoodPheromone = antSimdFood[i];
                    voxelFlat[i].towardsBasePheromone = antSimdHome[i];
                }
            });
            return true;
#else
            return false;
#endif
        }

#if NET7_0_OR_GREATER
        void diffuseAntSimdAxis(int axis, bool last)
        {
            int length = axis == 0 ? resX : axis == 1 ? resY : resZ;
            int stride = axis == 0 ? voxelStrideX : axis == 1 ? voxelStrideY : 1;
            // Use the last non-singleton dimension, including XY planar grids.
            int rowLength = resZ > 1 ? resZ : resY > 1 ? resY : resX;
            int rows = voxelFlat.Length / rowLength, range = reusableAntWeights.Length / 2;
            double foodStrength = gradualDiffusionStrength(foodDiffuseRate, antDiffusionGradual);
            double homeStrength = gradualDiffusionStrength(baseDiffuseRate, antDiffusionGradual);
            double foodScale = last ? gradualDiffusionRetention(foodDiffuseRate, antDiffusionGradual) : 1;
            double homeScale = last ? gradualDiffusionRetention(baseDiffuseRate, antDiffusionGradual) : 1;
            const int batch = 32;
            Parallel.For(0, (rows + batch - 1) / batch, chunk =>
            {
                int width = Vector<double>.Count;
                var foodRetain = new Vector<double>((1 - foodStrength) * foodScale);
                var homeRetain = new Vector<double>((1 - homeStrength) * homeScale);
                var foodRate = new Vector<double>(foodStrength * foodScale);
                var homeRate = new Vector<double>(homeStrength * homeScale);
                var foodLoss = new Vector<double>(foodDecayRate);
                var homeLoss = new Vector<double>(baseDecayRate);
                int end = Math.Min(rows, (chunk + 1) * batch);
                for (int rowIndex = chunk * batch; rowIndex < end; rowIndex++)
                {
                    int start = rowIndex * rowLength;
                    int coordinate = start / stride % length;
                    int i = 0;
                    // Only the contiguous axis needs scalar wrapped ends.
                    int prefix = stride == 1 ? Math.Min(range, rowLength) : 0;
                    int stop = stride == 1 ? rowLength - range : rowLength;
                    for (; i < prefix; i++) antSimdCell(start + i, axis, last);
                    for (; i + width <= stop; i += width)
                    {
                        var sumFood = Vector<double>.Zero; var sumHome = Vector<double>.Zero;
                        for (int tap = 0; tap < reusableAntWeights.Length; tap++)
                        {
                            int offset = tap - range;
                            int sample = stride == 1 ? start + i + offset
                                : start + i + (wrapIndex(coordinate + offset, length) - coordinate) * stride;
                            var weight = new Vector<double>(reusableAntWeights[tap]);
                            sumFood += new Vector<double>(antSimdFood, sample) * weight;
                            sumHome += new Vector<double>(antSimdHome, sample) * weight;
                        }
                        var food = new Vector<double>(antSimdFood, start + i);
                        var home = new Vector<double>(antSimdHome, start + i);
                        if (foodDiffuseRate > 0 || antDiffusionGradual < 1) food = Vector.Min(Vector<double>.One, food * foodRetain + sumFood * foodRate);
                        if (baseDiffuseRate > 0 || antDiffusionGradual < 1) home = Vector.Min(Vector<double>.One, home * homeRetain + sumHome * homeRate);
                        if (last)
                        {
                            food = Vector.Max(Vector<double>.Zero, food - foodLoss);
                            home = Vector.Max(Vector<double>.Zero, home - homeLoss);
                        }
                        food.CopyTo(antSimdFoodScratch, start + i);
                        home.CopyTo(antSimdHomeScratch, start + i);
                    }
                    for (; i < rowLength; i++) antSimdCell(start + i, axis, last);
                }
            });
        }

        void antSimdCell(int index, int axis, bool last)
        {
            int length = axis == 0 ? resX : axis == 1 ? resY : resZ;
            int stride = axis == 0 ? voxelStrideX : axis == 1 ? voxelStrideY : 1;
            int coordinate = index / stride % length;
            double sumFood = 0, sumHome = 0;
            for (int tap = 0; tap < reusableAntWeights.Length; tap++)
            {
                int sample = index + (wrapIndex(coordinate + tap - reusableAntWeights.Length / 2, length) - coordinate) * stride;
                sumFood += antSimdFood[sample] * reusableAntWeights[tap];
                sumHome += antSimdHome[sample] * reusableAntWeights[tap];
            }
            double food = antSimdFood[index], home = antSimdHome[index];
            double foodStrength = gradualDiffusionStrength(foodDiffuseRate, antDiffusionGradual);
            double homeStrength = gradualDiffusionStrength(baseDiffuseRate, antDiffusionGradual);
            double foodScale = last ? gradualDiffusionRetention(foodDiffuseRate, antDiffusionGradual) : 1;
            double homeScale = last ? gradualDiffusionRetention(baseDiffuseRate, antDiffusionGradual) : 1;
            if (foodDiffuseRate > 0 || antDiffusionGradual < 1) food = Math.Min(1, food * ((1 - foodStrength) * foodScale) + sumFood * (foodStrength * foodScale));
            if (baseDiffuseRate > 0 || antDiffusionGradual < 1) home = Math.Min(1, home * ((1 - homeStrength) * homeScale) + sumHome * (homeStrength * homeScale));
            antSimdFoodScratch[index] = last ? Math.Max(0, food - foodDecayRate) : food;
            antSimdHomeScratch[index] = last ? Math.Max(0, home - baseDecayRate) : home;
        }
#endif
    }
}
