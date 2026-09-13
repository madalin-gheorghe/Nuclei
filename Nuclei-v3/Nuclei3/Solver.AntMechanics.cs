using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Nuclei3
{
    public partial class Solver
    {
        Voxel[] foodSourceVoxels;
        int[][] antActiveLines;
        static readonly ThreadLocal<double[]> antLineScratch = new ThreadLocal<double[]>(() => new double[0]);

        void cacheFoodSourceVoxels()
        {
            var sources = new List<Voxel>();
            foreach (Voxel voxel in activeVoxels)
                if (voxel.food > 0 || voxel.antFood > 0) sources.Add(voxel);
            foodSourceVoxels = sources.ToArray();
        }

        void projectFoodSources()
        {
            if (!slimeParticles && !antParticles) return;
            if (foodSourceVoxels == null) cacheFoodSourceVoxels();
            if (foodSourceVoxels.Length < 2048)
                for (int i = 0; i < foodSourceVoxels.Length; i++) projectFoodSource(foodSourceVoxels[i]);
            else
                Parallel.For(0, foodSourceVoxels.Length, i => projectFoodSource(foodSourceVoxels[i]));
        }

        void projectFoodSource(Voxel voxel)
        {
            if (slimeParticles && foodSourceValues[voxel.flatIndex] > 0)
                addWorkingDensity(voxel, foodSourceValues[voxel.flatIndex]);
            // Pickup runs first. Only food still present emits scent; the edible
            // quantity stays in its original voxel and is never diffused.
            if (antParticles && voxel.antFood > 0)
                voxel.towardsFoodPheromone += voxel.antFood;
        }

        int[] getAntActiveLines(int axis, int lineCount)
        {
            if (activeVoxels.Length == voxelFlat.Length) return null;
            if (antActiveLines == null) antActiveLines = new int[3][];
            if (antActiveLines[axis] != null) return antActiveLines[axis];
            var occupied = new bool[lineCount];
            foreach (Voxel voxel in activeVoxels)
            {
                int line = axis == 0 ? voxel.idY * resZ + voxel.idZ
                    : axis == 1 ? voxel.idX * resZ + voxel.idZ : voxel.idX * resY + voxel.idY;
                occupied[line] = true;
            }
            var lines = new List<int>();
            for (int i = 0; i < lineCount; i++) if (occupied[i]) lines.Add(i);
            return antActiveLines[axis] = lines.ToArray();
        }

        // A worker owns a whole axis line. Snapshot both channels before writing
        // any voxel in that line, so every tap reads the same iteration (Jacobi).
        // Independent lines cannot race. The interleaved halo shares neighbor
        // lookup, wrapping and blocked-voxel checks between the two fields.
        void diffuseAntAxis(int axis, double[] weights, bool finishStep)
        {
            int length = axis == 0 ? resX : axis == 1 ? resY : resZ;
            int stride = axis == 0 ? voxelStrideX : axis == 1 ? voxelStrideY : 1;
            int lineCount = voxelFlat.Length / length;
            int[] lines = getAntActiveLines(axis, lineCount);
            int range = weights.Length / 2;
            double foodRate = gradualDiffusionStrength(foodDiffuseRate, antDiffusionGradual);
            double baseRate = gradualDiffusionStrength(baseDiffuseRate, antDiffusionGradual);
            double foodScale = finishStep ? gradualDiffusionRetention(foodDiffuseRate, antDiffusionGradual) : 1;
            double baseScale = finishStep ? gradualDiffusionRetention(baseDiffuseRate, antDiffusionGradual) : 1;
            bool diffuseFood = foodRate > 0, diffuseBase = baseRate > 0;
            Parallel.For(0, lines == null ? lineCount : lines.Length, lineIndex =>
            {
                int line = lines == null ? lineIndex : lines[lineIndex];
                int start = axis == 0 ? line
                    : axis == 1 ? (line / resZ) * voxelStrideX + line % resZ : line * resZ;
                int paddedLength = length + range * 2;
                double[] scratch = antLineScratch.Value;
                if (scratch.Length < paddedLength * 2)
                    antLineScratch.Value = scratch = new double[paddedLength * 2];

                for (int i = 0; i < paddedLength; i++)
                {
                    int coordinate = i - range;
                    Voxel source = null;
                    if (wrapBoundaries)
                        source = voxelFlat[start + wrapIndex(coordinate, length) * stride];
                    else if (coordinate >= 0 && coordinate < length)
                        source = voxelFlat[start + coordinate * stride];
                    bool valid = VoxelOccupancy.IsWalkable(source);
                    scratch[i * 2] = valid ? source.towardsFoodPheromone : 0;
                    scratch[i * 2 + 1] = valid ? source.towardsBasePheromone : 0;
                }

                for (int coordinate = 0, index = start; coordinate < length; coordinate++, index += stride)
                {
                    Voxel voxel = voxelFlat[index];
                    if (voxel == null) continue;
                    double food = voxel.towardsFoodPheromone, home = voxel.towardsBasePheromone;
                    double sumFood = 0, sumBase = 0;
                    for (int tap = 0, sample = coordinate * 2; tap < weights.Length; tap++, sample += 2)
                    {
                        double weight = weights[tap];
                        sumFood += scratch[sample] * weight;
                        sumBase += scratch[sample + 1] * weight;
                    }
                    if (diffuseFood) food = clampAntField(food * ((1 - foodRate) * foodScale) + sumFood * (foodRate * foodScale), voxel);
                    if (diffuseBase) home = clampAntField(home * ((1 - baseRate) * baseScale) + sumBase * (baseRate * baseScale), voxel);
                    if (finishStep)
                    {
                        food = !wrapBoundaries && isAntFieldBoundary(voxel) ? 0 : Math.Max(0, food - foodDecayRate);
                        home = Math.Max(0, home - baseDecayRate);
                    }
                    voxel.towardsFoodPheromone = food;
                    voxel.towardsBasePheromone = home;
                }
            });
        }

        double clampAntField(double value, Voxel voxel)
        {
            value = Math.Min(1, value);
            if (voxel.maxDensity >= 0) value = Math.Min(value, voxel.maxDensity);
            if (voxel.minDensity >= 0) value = Math.Max(value, voxel.minDensity);
            return !wrapBoundaries && isAntFieldBoundary(voxel) ? 0 : value;
        }

        bool isAntFieldBoundary(Voxel voxel)
        {
            return (resX > 1 && (voxel.idX == 0 || voxel.idX == resX - 1))
                || (resY > 1 && (voxel.idY == 0 || voxel.idY == resY - 1))
                || (resZ > 1 && (voxel.idZ == 0 || voxel.idZ == resZ - 1));
        }

        void applyAntBoundaryAndDecay()
        {
            Parallel.For(0, activeVoxels.Length, i =>
            {
                Voxel voxel = activeVoxels[i];
                voxel.towardsFoodPheromone = !wrapBoundaries && isAntFieldBoundary(voxel)
                    ? 0 : Math.Max(0, voxel.towardsFoodPheromone - foodDecayRate);
                voxel.towardsBasePheromone = Math.Max(0, voxel.towardsBasePheromone - baseDecayRate);
            });
        }

        static bool hasFoodGradient(double food0, double food1, double food2, double food3, double food4)
        {
            double strongest = Math.Max(Math.Max(food0, food1), Math.Max(food2, Math.Max(food3, food4)));
            if (strongest <= 0.000001) return false;
            double weakest = strongest;
            if (food0 >= 0) weakest = Math.Min(weakest, food0);
            if (food1 >= 0) weakest = Math.Min(weakest, food1);
            if (food2 >= 0) weakest = Math.Min(weakest, food2);
            if (food3 >= 0) weakest = Math.Min(weakest, food3);
            if (food4 >= 0) weakest = Math.Min(weakest, food4);
            return strongest - weakest > Math.Max(0.000001, strongest * 0.01);
        }
    }
}
