using System;
using System.Collections.Generic;
using Rhino.Geometry;

namespace Nuclei3
{
    internal enum VoxelGridMergeMode
    {
        Average,
        Minimum,
        Maximum
    }

    internal static class VoxelGridCombiner
    {
        public static VoxelGridData Union(IList<VoxelGridData> inputs, VoxelGridMergeMode mode)
        {
            return Combine(inputs, mode, true);
        }

        public static VoxelGridData Intersection(IList<VoxelGridData> inputs, VoxelGridMergeMode mode)
        {
            return Combine(inputs, mode, false);
        }

        static VoxelGridData Combine(IList<VoxelGridData> inputs, VoxelGridMergeMode mode, bool union)
        {
            if (inputs == null || inputs.Count == 0)
            {
                return VoxelGridData.CreateFullDomain(0, 0, 0, Globals.voxelSize);
            }

            VoxelGridData first = inputs[0];
            bool[] activeMask = new bool[first.Count];

            for (int flatIndex = 0; flatIndex < first.Count; flatIndex++)
            {
                int activeCount = 0;
                for (int i = 0; i < inputs.Count; i++)
                {
                    VoxelGridData data = inputs[i];
                    if (data.Count != first.Count)
                    {
                        continue;
                    }

                    if (data.IsActive(flatIndex))
                    {
                        activeCount++;
                    }
                }

                activeMask[flatIndex] = union ? activeCount > 0 : activeCount == inputs.Count;
            }

            VoxelGridData result = first.WithActiveMask(activeMask);
            result.MinimumDensity = BuildScalarMap(result, inputs, mode, 0, -1);
            result.MaximumDensity = BuildScalarMap(result, inputs, mode, 1, -1);
            result.Speed = BuildScalarMap(result, inputs, mode, 2, -1);
            result.SensorDistance = BuildScalarMap(result, inputs, mode, 3, -1);
            result.SensorAngle = BuildScalarMap(result, inputs, mode, 4, -1);
            result.RotationAngle = BuildScalarMap(result, inputs, mode, 5, -1);
            result.Food = BuildScalarMap(result, inputs, mode, 6, -1);
            result.Vectors = BuildVectorMap(result, inputs);
            result.Frequencies = BuildFrequencyMap(result, inputs, mode);
            return result;
        }

        static VoxelScalarMap BuildScalarMap(VoxelGridData result, IList<VoxelGridData> inputs, VoxelGridMergeMode mode, int fieldIndex, double defaultValue)
        {
            double[] values = null;

            for (int ordinal = 0; ordinal < result.ActiveCount; ordinal++)
            {
                int flatIndex = result.ActiveFlatIndexAt(ordinal);
                int count = 0;
                double sum = 0;
                double min = double.MaxValue;
                double max = double.MinValue;

                for (int inputIndex = 0; inputIndex < inputs.Count; inputIndex++)
                {
                    VoxelGridData input = inputs[inputIndex];
                    if (flatIndex >= input.Count || !input.IsActive(flatIndex))
                    {
                        continue;
                    }

                    double value = input.GetScalarValue(fieldIndex, flatIndex);
                    if (value == defaultValue)
                    {
                        continue;
                    }

                    count++;
                    sum += value;
                    if (value < min) min = value;
                    if (value > max) max = value;
                }

                double merged = defaultValue;
                if (count > 0)
                {
                    if (mode == VoxelGridMergeMode.Minimum) merged = min;
                    else if (mode == VoxelGridMergeMode.Maximum) merged = max;
                    else merged = sum / count;
                }

                if (merged != defaultValue)
                {
                    if (values == null)
                    {
                        values = CreateFilledArray(result.Count, defaultValue);
                    }

                    values[flatIndex] = merged;
                }
            }

            return new VoxelScalarMap(defaultValue, values);
        }

        static Vector3d[] BuildVectorMap(VoxelGridData result, IList<VoxelGridData> inputs)
        {
            Vector3d[] values = null;

            for (int ordinal = 0; ordinal < result.ActiveCount; ordinal++)
            {
                int flatIndex = result.ActiveFlatIndexAt(ordinal);
                Vector3d vector = Vector3d.Zero;

                for (int inputIndex = 0; inputIndex < inputs.Count; inputIndex++)
                {
                    VoxelGridData input = inputs[inputIndex];
                    if (flatIndex >= input.Count || !input.IsActive(flatIndex))
                    {
                        continue;
                    }

                    vector += input.GetVectorValue(flatIndex);
                }

                if (vector.Length > 0)
                {
                    vector.Unitize();
                    if (values == null)
                    {
                        values = new Vector3d[result.Count];
                    }

                    values[flatIndex] = vector;
                }
            }

            return values;
        }

        static int[] BuildFrequencyMap(VoxelGridData result, IList<VoxelGridData> inputs, VoxelGridMergeMode mode)
        {
            int[] values = null;

            for (int ordinal = 0; ordinal < result.ActiveCount; ordinal++)
            {
                int flatIndex = result.ActiveFlatIndexAt(ordinal);
                int count = 0;
                int sum = 0;
                int min = int.MaxValue;
                int max = int.MinValue;

                for (int inputIndex = 0; inputIndex < inputs.Count; inputIndex++)
                {
                    VoxelGridData input = inputs[inputIndex];
                    if (flatIndex >= input.Count || !input.IsActive(flatIndex))
                    {
                        continue;
                    }

                    int value = input.GetFrequencyValue(flatIndex);
                    if (value < 1) value = 1;
                    count++;
                    sum += value;
                    if (value < min) min = value;
                    if (value > max) max = value;
                }

                int merged = 3;
                if (count > 0)
                {
                    if (mode == VoxelGridMergeMode.Minimum) merged = min;
                    else if (mode == VoxelGridMergeMode.Maximum) merged = max;
                    else merged = Math.Max(1, Convert.ToInt32(sum / (double)count));
                }

                if (merged != 3)
                {
                    if (values == null)
                    {
                        values = CreateFilledIntArray(result.Count, 3);
                    }

                    values[flatIndex] = merged;
                }
            }

            return values;
        }

        static double[] CreateFilledArray(int count, double value)
        {
            double[] values = new double[count];
            if (value != 0)
            {
                for (int i = 0; i < values.Length; i++)
                {
                    values[i] = value;
                }
            }

            return values;
        }

        static int[] CreateFilledIntArray(int count, int value)
        {
            int[] values = new int[count];
            for (int i = 0; i < values.Length; i++)
            {
                values[i] = value;
            }

            return values;
        }
    }
}
