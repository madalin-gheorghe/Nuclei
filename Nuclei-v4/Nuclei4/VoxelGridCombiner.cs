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
            VoxelSelectionBuilder activeSelection = new VoxelSelectionBuilder(first.Count);

            if (union)
            {
                for (int inputIndex = 0; inputIndex < inputs.Count; inputIndex++)
                {
                    VoxelGridData data = inputs[inputIndex];
                    if (!SameGrid(first, data))
                    {
                        continue;
                    }

                    activeSelection.UnionWith(data);
                }
            }
            else
            {
                activeSelection.Fill();
                for (int inputIndex = 0; inputIndex < inputs.Count; inputIndex++)
                {
                    VoxelGridData data = inputs[inputIndex];
                    if (!SameGrid(first, data))
                    {
                        activeSelection.Clear();
                        break;
                    }

                    activeSelection.IntersectWith(data);
                }
            }

            VoxelGridData result = activeSelection.ApplyTo(first);
            result.MinimumDensity = BuildScalarMap(result, inputs, mode, 0, -1, union);
            result.MaximumDensity = BuildScalarMap(result, inputs, mode, 1, -1, union);
            result.Speed = BuildScalarMap(result, inputs, mode, 2, -1, union);
            result.SensorDistance = BuildScalarMap(result, inputs, mode, 3, -1, union);
            result.SensorAngle = BuildScalarMap(result, inputs, mode, 4, -1, union);
            result.RotationAngle = BuildScalarMap(result, inputs, mode, 5, -1, union);
            result.Food = BuildScalarMap(result, inputs, mode, 6, -1, union);
            VoxelFrequencyMap vectorFrequency;
            Vector3d vectorDefault;
            result.VectorData = BuildVectorData(result, inputs, mode, union, out vectorFrequency, out vectorDefault);
            result.VectorDefaultX = (float)vectorDefault.X;
            result.VectorDefaultY = (float)vectorDefault.Y;
            result.VectorDefaultZ = (float)vectorDefault.Z;
            result.VectorFrequency = vectorFrequency;
            return result;
        }

        static bool SameGrid(VoxelGridData first, VoxelGridData other)
        {
            return other != null &&
                   other.ResX == first.ResX &&
                   other.ResY == first.ResY &&
                   other.ResZ == first.ResZ &&
                   other.Count == first.Count;
        }

        static VoxelScalarMap BuildScalarMap(VoxelGridData result, IList<VoxelGridData> inputs, VoxelGridMergeMode mode, int fieldIndex, double defaultValue, bool union)
        {
            VoxelScalarMap uniformMap;
            if (TryBuildUniformScalarMap(result, inputs, mode, fieldIndex, defaultValue, union, out uniformMap))
            {
                return uniformMap;
            }

            float[] values = null;

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
                    if (!SameGrid(result, input) || !input.IsActive(flatIndex))
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

                    values[flatIndex] = (float)merged;
                }
            }

            return new VoxelScalarMap(defaultValue, values);
        }

        static bool TryBuildUniformScalarMap(
            VoxelGridData result,
            IList<VoxelGridData> inputs,
            VoxelGridMergeMode mode,
            int fieldIndex,
            double unsetValue,
            bool union,
            out VoxelScalarMap map)
        {
            map = new VoxelScalarMap(unsetValue);
            if (result.ActiveCount == 0) return true;

            bool hasInput = false;
            bool allInputsFull = true;
            bool allValuesEqual = true;
            double firstValue = unsetValue;
            int contributingCount = 0;
            double sum = 0;
            double min = double.MaxValue;
            double max = double.MinValue;

            for (int inputIndex = 0; inputIndex < inputs.Count; inputIndex++)
            {
                VoxelGridData input = inputs[inputIndex];
                if (!SameGrid(result, input)) continue;

                VoxelScalarMap inputMap = ScalarMap(input, fieldIndex);
                if (inputMap.Values != null) return false;

                double value = inputMap.DefaultValue;
                if (!hasInput) firstValue = value;
                else if (value != firstValue) allValuesEqual = false;
                hasInput = true;
                allInputsFull &= input.AllVoxelsActive;

                if (value == unsetValue) continue;
                contributingCount++;
                sum += value;
                if (value < min) min = value;
                if (value > max) max = value;
            }

            if (!hasInput) return true;
            if (union && !allInputsFull && !allValuesEqual) return false;
            if (contributingCount == 0) return true;

            double merged = mode == VoxelGridMergeMode.Minimum
                ? min
                : mode == VoxelGridMergeMode.Maximum
                    ? max
                    : sum / contributingCount;
            map = new VoxelScalarMap(merged);
            return true;
        }

        static VoxelScalarMap ScalarMap(VoxelGridData data, int fieldIndex)
        {
            switch (fieldIndex)
            {
                case 0: return data.MinimumDensity;
                case 1: return data.MaximumDensity;
                case 2: return data.Speed;
                case 3: return data.SensorDistance;
                case 4: return data.SensorAngle;
                case 5: return data.RotationAngle;
                case 6: return data.Food;
                default: throw new ArgumentOutOfRangeException(nameof(fieldIndex));
            }
        }

        static float[] BuildVectorData(
            VoxelGridData result,
            IList<VoxelGridData> inputs,
            VoxelGridMergeMode mode,
            bool union,
            out VoxelFrequencyMap frequencyMap,
            out Vector3d defaultVector)
        {
            if (TryBuildUniformVectorData(result, inputs, mode, union, out frequencyMap, out defaultVector))
            {
                return null;
            }

            float[] values = null;
            defaultVector = Vector3d.Zero;
            int[] frequencyValues = null;
            int firstFrequency = 3;
            bool hasFrequency = false;
            bool variableFrequency = false;

            for (int ordinal = 0; ordinal < result.ActiveCount; ordinal++)
            {
                int flatIndex = result.ActiveFlatIndexAt(ordinal);
                Vector3d vector = Vector3d.Zero;
                int count = 0;
                int sum = 0;
                int min = int.MaxValue;
                int max = int.MinValue;

                for (int inputIndex = 0; inputIndex < inputs.Count; inputIndex++)
                {
                    VoxelGridData input = inputs[inputIndex];
                    if (!SameGrid(result, input) || !input.IsActive(flatIndex))
                    {
                        continue;
                    }

                    vector += input.GetVectorValue(flatIndex);
                    int frequency = Math.Max(1, input.GetFrequencyValue(flatIndex));
                    count++;
                    sum += frequency;
                    if (frequency < min) min = frequency;
                    if (frequency > max) max = frequency;
                }

                int mergedFrequency = 3;
                if (count > 0)
                {
                    if (mode == VoxelGridMergeMode.Minimum) mergedFrequency = min;
                    else if (mode == VoxelGridMergeMode.Maximum) mergedFrequency = max;
                    else mergedFrequency = Math.Max(1, Convert.ToInt32(sum / (double)count));
                }

                if (vector.Length > 0)
                {
                    vector.Unitize();
                }
                if (vector.Length > 0 || mergedFrequency != 3)
                {
                    if (vector.Length > 0)
                    {
                        if (values == null) values = new float[checked(result.Count * 3)];

                        int offset = flatIndex * 3;
                        values[offset] = (float)vector.X;
                        values[offset + 1] = (float)vector.Y;
                        values[offset + 2] = (float)vector.Z;
                    }
                }

                if (!hasFrequency)
                {
                    firstFrequency = mergedFrequency;
                    hasFrequency = true;
                }
                else if (!variableFrequency && mergedFrequency != firstFrequency)
                {
                    variableFrequency = true;
                    frequencyValues = CreateFilledIntArray(result.Count, 3);
                    for (int previous = 0; previous < ordinal; previous++)
                    {
                        frequencyValues[result.ActiveFlatIndexAt(previous)] = firstFrequency;
                    }
                }

                if (variableFrequency)
                {
                    frequencyValues[flatIndex] = mergedFrequency;
                }
            }

            if (!hasFrequency)
            {
                frequencyMap = new VoxelFrequencyMap(3);
            }
            else if (!variableFrequency && result.AllVoxelsActive)
            {
                frequencyMap = new VoxelFrequencyMap(firstFrequency);
            }
            else if (!variableFrequency && firstFrequency == 3)
            {
                frequencyMap = new VoxelFrequencyMap(3);
            }
            else
            {
                if (frequencyValues == null) frequencyValues = CreateFilledIntArray(result.Count, 3);
                if (!variableFrequency)
                {
                    for (int ordinal = 0; ordinal < result.ActiveCount; ordinal++)
                    {
                        frequencyValues[result.ActiveFlatIndexAt(ordinal)] = firstFrequency;
                    }
                }
                frequencyMap = new VoxelFrequencyMap(3, frequencyValues);
            }

            if (result.AllVoxelsActive && values != null && result.Count > 0)
            {
                float firstX = values[0];
                float firstY = values[1];
                float firstZ = values[2];
                bool uniform = true;
                for (int flatIndex = 1; flatIndex < result.Count; flatIndex++)
                {
                    int offset = flatIndex * 3;
                    if (values[offset] != firstX || values[offset + 1] != firstY || values[offset + 2] != firstZ)
                    {
                        uniform = false;
                        break;
                    }
                }

                if (uniform)
                {
                    defaultVector = new Vector3d(firstX, firstY, firstZ);
                    values = null;
                }
            }

            return values;
        }

        static bool TryBuildUniformVectorData(
            VoxelGridData result,
            IList<VoxelGridData> inputs,
            VoxelGridMergeMode mode,
            bool union,
            out VoxelFrequencyMap frequencyMap,
            out Vector3d defaultVector)
        {
            frequencyMap = new VoxelFrequencyMap(3);
            defaultVector = Vector3d.Zero;
            if (result.ActiveCount == 0) return true;

            bool hasInput = false;
            bool allInputsFull = true;
            bool allValuesEqual = true;
            Vector3d firstVector = Vector3d.Zero;
            int firstFrequency = 3;
            int count = 0;
            int frequencySum = 0;
            int frequencyMin = int.MaxValue;
            int frequencyMax = int.MinValue;
            Vector3d vectorSum = Vector3d.Zero;

            for (int inputIndex = 0; inputIndex < inputs.Count; inputIndex++)
            {
                VoxelGridData input = inputs[inputIndex];
                if (!SameGrid(result, input)) continue;
                if (input.VectorData != null || (input.VectorFrequency != null && input.VectorFrequency.Values != null)) return false;

                Vector3d vector = new Vector3d(input.VectorDefaultX, input.VectorDefaultY, input.VectorDefaultZ);
                int frequency = input.VectorFrequency != null ? input.VectorFrequency.DefaultValue : 3;
                if (!hasInput)
                {
                    firstVector = vector;
                    firstFrequency = frequency;
                }
                else if (vector.X != firstVector.X || vector.Y != firstVector.Y || vector.Z != firstVector.Z || frequency != firstFrequency)
                {
                    allValuesEqual = false;
                }

                hasInput = true;
                allInputsFull &= input.AllVoxelsActive;
                vectorSum += vector;
                frequencySum += frequency;
                if (frequency < frequencyMin) frequencyMin = frequency;
                if (frequency > frequencyMax) frequencyMax = frequency;
                count++;
            }

            if (!hasInput) return true;
            if (union && !allInputsFull && !allValuesEqual) return false;

            if (vectorSum.Length > 0) vectorSum.Unitize();
            defaultVector = vectorSum;
            int mergedFrequency = mode == VoxelGridMergeMode.Minimum
                ? frequencyMin
                : mode == VoxelGridMergeMode.Maximum
                    ? frequencyMax
                    : Math.Max(1, Convert.ToInt32(frequencySum / (double)count));
            frequencyMap = new VoxelFrequencyMap(mergedFrequency);
            return true;
        }

        static int[] CreateFilledIntArray(int count, int value)
        {
            int[] values = new int[count];
            if (value != 0)
            {
                for (int i = 0; i < values.Length; i++) values[i] = value;
            }
            return values;
        }

        static float[] CreateFilledArray(int count, double value)
        {
            float[] values = new float[count];
            if (value != 0)
            {
                float singleValue = (float)value;
                for (int i = 0; i < values.Length; i++)
                {
                    values[i] = singleValue;
                }
            }

            return values;
        }

    }
}
