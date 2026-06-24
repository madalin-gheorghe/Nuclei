using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Rhino.Geometry;

namespace Nuclei3
{
    internal enum VoxelScalarField
    {
        MinimumDensity = 0,
        MaximumDensity = 1,
        Speed = 2,
        SensorDistance = 3,
        SensorAngle = 4,
        RotationAngle = 5,
        Food = 6
    }

    internal sealed class VoxelScalarMap
    {
        public readonly double DefaultValue;
        public readonly double[] Values;

        public VoxelScalarMap(double defaultValue, double[] values = null)
        {
            DefaultValue = defaultValue;
            Values = values;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public double Get(int flatIndex)
        {
            return Values != null ? Values[flatIndex] : DefaultValue;
        }

        public double[] ToDenseArray(int count)
        {
            double[] result = new double[count];
            if (Values != null)
            {
                Array.Copy(Values, result, Math.Min(Values.Length, result.Length));
                return result;
            }

            if (DefaultValue != 0)
            {
                for (int i = 0; i < result.Length; i++)
                {
                    result[i] = DefaultValue;
                }
            }

            return result;
        }
    }

    internal sealed class VoxelGridData
    {
        public readonly int ResX;
        public readonly int ResY;
        public readonly int ResZ;
        public readonly int StrideX;
        public readonly int StrideY;
        public readonly int Count;
        public readonly double VoxelSize;
        public readonly bool AllVoxelsActive;
        public readonly bool[] ActiveMask;
        public readonly int[] ActiveIndices;
        public readonly int ActiveCount;

        public VoxelScalarMap Density = new VoxelScalarMap(0);
        public VoxelScalarMap MinimumDensity = new VoxelScalarMap(-1);
        public VoxelScalarMap MaximumDensity = new VoxelScalarMap(-1);
        public VoxelScalarMap Speed = new VoxelScalarMap(-1);
        public VoxelScalarMap SensorDistance = new VoxelScalarMap(-1);
        public VoxelScalarMap SensorAngle = new VoxelScalarMap(-1);
        public VoxelScalarMap RotationAngle = new VoxelScalarMap(-1);
        public VoxelScalarMap Food = new VoxelScalarMap(-1);
        public Vector3d[] Vectors;
        public int[] Frequencies;

        VoxelGridData(int resX, int resY, int resZ, double voxelSize, bool allVoxelsActive, bool[] activeMask, int[] activeIndices, int activeCount)
        {
            ResX = resX;
            ResY = resY;
            ResZ = resZ;
            StrideY = resZ;
            StrideX = resY * StrideY;
            Count = resX * resY * resZ;
            VoxelSize = voxelSize > 0 ? voxelSize : 1.0;
            AllVoxelsActive = allVoxelsActive;
            ActiveMask = activeMask;
            ActiveIndices = activeIndices;
            ActiveCount = activeCount;
        }

        public static VoxelGridData CreateFullDomain(int resX, int resY, int resZ, double voxelSize)
        {
            return new VoxelGridData(resX, resY, resZ, voxelSize, true, null, null, resX * resY * resZ);
        }

        public static VoxelGridData Capture(Voxel[,,] voxels, double fallbackVoxelSize)
        {
            if (voxels == null)
            {
                return CreateFullDomain(0, 0, 0, fallbackVoxelSize);
            }

            int resX = voxels.GetLength(0);
            int resY = voxels.GetLength(1);
            int resZ = voxels.GetLength(2);
            int strideY = resZ;
            int strideX = resY * strideY;
            int count = resX * resY * resZ;
            double voxelSize = ResolveVoxelSize(voxels, fallbackVoxelSize);

            List<int> active = new List<int>();
            bool[] activeMask = new bool[count];

            double[] density = null;
            double[] minimumDensity = null;
            double[] maximumDensity = null;
            double[] speed = null;
            double[] sensorDistance = null;
            double[] sensorAngle = null;
            double[] rotationAngle = null;
            double[] food = null;
            Vector3d[] vectors = null;
            int[] frequencies = null;

            for (int x = 0; x < resX; x++)
            {
                for (int y = 0; y < resY; y++)
                {
                    for (int z = 0; z < resZ; z++)
                    {
                        Voxel voxel = voxels[x, y, z];
                        if (voxel == null)
                        {
                            continue;
                        }

                        int flatIndex = x * strideX + y * strideY + z;
                        active.Add(flatIndex);
                        activeMask[flatIndex] = true;

                        CaptureNonDefault(ref density, count, flatIndex, voxel.density, 0);
                        CaptureNonDefault(ref minimumDensity, count, flatIndex, voxel.minDensity, -1);
                        CaptureNonDefault(ref maximumDensity, count, flatIndex, voxel.maxDensity, -1);
                        CaptureNonDefault(ref speed, count, flatIndex, voxel.speedMultiplier, -1);
                        CaptureNonDefault(ref sensorDistance, count, flatIndex, voxel.sensorDistanceMultiplier, -1);
                        CaptureNonDefault(ref sensorAngle, count, flatIndex, voxel.sensorAngleMultiplier, -1);
                        CaptureNonDefault(ref rotationAngle, count, flatIndex, voxel.rotationAngleMultiplier, -1);
                        CaptureNonDefault(ref food, count, flatIndex, voxel.food, -1);

                        if (voxel.voxelVector.Length > 0)
                        {
                            if (vectors == null) vectors = new Vector3d[count];
                            vectors[flatIndex] = voxel.voxelVector;
                        }

                        if (voxel.frequency != 3)
                        {
                            if (frequencies == null)
                            {
                                frequencies = new int[count];
                                for (int i = 0; i < frequencies.Length; i++)
                                {
                                    frequencies[i] = 3;
                                }
                            }

                            frequencies[flatIndex] = voxel.frequency;
                        }
                    }
                }
            }

            VoxelGridData data;
            if (active.Count == 0)
            {
                data = CreateFullDomain(resX, resY, resZ, voxelSize);
            }
            else if (active.Count == count)
            {
                data = CreateFullDomain(resX, resY, resZ, voxelSize);
            }
            else
            {
                data = new VoxelGridData(resX, resY, resZ, voxelSize, false, activeMask, active.ToArray(), active.Count);
            }

            data.Density = new VoxelScalarMap(0, density);
            data.MinimumDensity = new VoxelScalarMap(-1, minimumDensity);
            data.MaximumDensity = new VoxelScalarMap(-1, maximumDensity);
            data.Speed = new VoxelScalarMap(-1, speed);
            data.SensorDistance = new VoxelScalarMap(-1, sensorDistance);
            data.SensorAngle = new VoxelScalarMap(-1, sensorAngle);
            data.RotationAngle = new VoxelScalarMap(-1, rotationAngle);
            data.Food = new VoxelScalarMap(-1, food);
            data.Vectors = vectors;
            data.Frequencies = frequencies;
            return data;
        }

        public VoxelGridData WithScalarValues(int fieldIndex, IList<double> values)
        {
            VoxelGridData result = CloneSharedMaps();
            if (values == null || values.Count == 0 || Count == 0)
            {
                return result;
            }

            VoxelScalarField field = (VoxelScalarField)fieldIndex;
            if (values.Count == ActiveCount)
            {
                VoxelScalarMap inherited = GetMap(field);
                double[] mapValues = inherited.ToDenseArray(Count);

                if (AllVoxelsActive)
                {
                    for (int flatIndex = 0; flatIndex < Count; flatIndex++)
                    {
                        mapValues[flatIndex] = AdjustScalarValue(field, values[flatIndex]);
                    }
                }
                else
                {
                    for (int i = 0; i < ActiveIndices.Length; i++)
                    {
                        mapValues[ActiveIndices[i]] = AdjustScalarValue(field, values[i]);
                    }
                }

                result.SetMap(field, new VoxelScalarMap(inherited.DefaultValue, mapValues));
            }
            else
            {
                result.SetMap(field, new VoxelScalarMap(AdjustScalarValue(field, values[0])));
            }

            return result;
        }

        public Voxel[,,] ToVoxelArray(bool materializeActiveVoxels)
        {
            Voxel[,,] result = new Voxel[ResX, ResY, ResZ];
            if (!materializeActiveVoxels)
            {
                return result;
            }

            if (AllVoxelsActive)
            {
                Parallel.For(0, Count, flatIndex =>
                {
                    Voxel voxel = CreateVoxel(flatIndex, null);
                    result[voxel.idX, voxel.idY, voxel.idZ] = voxel;
                });
            }
            else if (ActiveIndices != null)
            {
                Parallel.For(0, ActiveIndices.Length, i =>
                {
                    Voxel voxel = CreateVoxel(ActiveIndices[i], null);
                    result[voxel.idX, voxel.idY, voxel.idZ] = voxel;
                });
            }

            return result;
        }

        public Voxel CreateVoxel(int flatIndex, VoxelDensityStore densityStore)
        {
            int x;
            int y;
            int z;
            CoordinatesFromFlatIndex(flatIndex, out x, out y, out z);

            Voxel voxel = new Voxel(VoxelSize, x, y, z);
            voxel.flatIndex = flatIndex;
            voxel.densityStore = densityStore;

            voxel.minDensity = MinimumDensity.Get(flatIndex);
            voxel.maxDensity = MaximumDensity.Get(flatIndex);
            voxel.inputMinDensity = voxel.minDensity;
            voxel.inputMaxDensity = voxel.maxDensity;

            voxel.speedMultiplier = Speed.Get(flatIndex);
            voxel.sensorDistanceMultiplier = SensorDistance.Get(flatIndex);
            voxel.sensorAngleMultiplier = SensorAngle.Get(flatIndex);
            voxel.rotationAngleMultiplier = RotationAngle.Get(flatIndex);
            voxel.food = Food.Get(flatIndex);

            voxel.voxelVector = Vectors != null ? Vectors[flatIndex] : Vector3d.Zero;
            voxel.vectorField = voxel.voxelVector.Length > 0;
            voxel.frequency = Frequencies != null ? Frequencies[flatIndex] : 3;
            voxel.density = Density.Get(flatIndex);

            return voxel;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int FlatIndex(int x, int y, int z)
        {
            return x * StrideX + y * StrideY + z;
        }

        public bool IsActive(int flatIndex)
        {
            return AllVoxelsActive || (ActiveMask != null && flatIndex >= 0 && flatIndex < ActiveMask.Length && ActiveMask[flatIndex]);
        }

        public int ActiveOrdinalFromFlatIndex(int flatIndex)
        {
            if (flatIndex < 0 || flatIndex >= Count || !IsActive(flatIndex))
            {
                return -1;
            }

            if (AllVoxelsActive)
            {
                return flatIndex;
            }

            return ActiveIndices != null ? Array.BinarySearch(ActiveIndices, flatIndex) : -1;
        }

        public void CoordinatesFromFlatIndex(int flatIndex, out int x, out int y, out int z)
        {
            x = flatIndex / StrideX;
            int remainder = flatIndex - x * StrideX;
            y = remainder / StrideY;
            z = remainder - y * StrideY;
        }

        VoxelGridData CloneSharedMaps()
        {
            VoxelGridData result = new VoxelGridData(ResX, ResY, ResZ, VoxelSize, AllVoxelsActive, ActiveMask, ActiveIndices, ActiveCount);
            result.Density = Density;
            result.MinimumDensity = MinimumDensity;
            result.MaximumDensity = MaximumDensity;
            result.Speed = Speed;
            result.SensorDistance = SensorDistance;
            result.SensorAngle = SensorAngle;
            result.RotationAngle = RotationAngle;
            result.Food = Food;
            result.Vectors = Vectors;
            result.Frequencies = Frequencies;
            return result;
        }

        VoxelScalarMap GetMap(VoxelScalarField field)
        {
            switch (field)
            {
                case VoxelScalarField.MinimumDensity: return MinimumDensity;
                case VoxelScalarField.MaximumDensity: return MaximumDensity;
                case VoxelScalarField.Speed: return Speed;
                case VoxelScalarField.SensorDistance: return SensorDistance;
                case VoxelScalarField.SensorAngle: return SensorAngle;
                case VoxelScalarField.RotationAngle: return RotationAngle;
                case VoxelScalarField.Food: return Food;
                default: return Speed;
            }
        }

        void SetMap(VoxelScalarField field, VoxelScalarMap map)
        {
            switch (field)
            {
                case VoxelScalarField.MinimumDensity:
                    MinimumDensity = map;
                    break;
                case VoxelScalarField.MaximumDensity:
                    MaximumDensity = map;
                    break;
                case VoxelScalarField.Speed:
                    Speed = map;
                    break;
                case VoxelScalarField.SensorDistance:
                    SensorDistance = map;
                    break;
                case VoxelScalarField.SensorAngle:
                    SensorAngle = map;
                    break;
                case VoxelScalarField.RotationAngle:
                    RotationAngle = map;
                    break;
                case VoxelScalarField.Food:
                    Food = map;
                    break;
            }
        }

        static void CaptureNonDefault(ref double[] values, int count, int flatIndex, double value, double defaultValue)
        {
            if (value == defaultValue)
            {
                return;
            }

            if (values == null)
            {
                values = new double[count];
                if (defaultValue != 0)
                {
                    for (int i = 0; i < values.Length; i++)
                    {
                        values[i] = defaultValue;
                    }
                }
            }

            values[flatIndex] = value;
        }

        static double AdjustScalarValue(VoxelScalarField field, double value)
        {
            if (field == VoxelScalarField.MinimumDensity || field == VoxelScalarField.MaximumDensity)
            {
                if (value != -1)
                {
                    if (value < 0) value = 0;
                    if (value > 1) value = 1;
                }
            }

            return value;
        }

        static double ResolveVoxelSize(Voxel[,,] voxels, double fallbackVoxelSize)
        {
            int resX = voxels.GetLength(0);
            int resY = voxels.GetLength(1);
            int resZ = voxels.GetLength(2);

            for (int x = 0; x < resX; x++)
            {
                for (int y = 0; y < resY; y++)
                {
                    for (int z = 0; z < resZ; z++)
                    {
                        Voxel voxel = voxels[x, y, z];
                        if (voxel != null && voxel.voxelSize > 0)
                        {
                            return voxel.voxelSize;
                        }
                    }
                }
            }

            return fallbackVoxelSize > 0 ? fallbackVoxelSize : 1.0;
        }
    }

    internal static class VoxelGridRegistry
    {
        static readonly ConditionalWeakTable<Voxel[,,], VoxelGridData> Data = new ConditionalWeakTable<Voxel[,,], VoxelGridData>();
        static readonly object SyncRoot = new object();

        public static void Set(Voxel[,,] voxels, VoxelGridData data)
        {
            if (voxels == null || data == null)
            {
                return;
            }

            lock (SyncRoot)
            {
                Data.Remove(voxels);
                Data.Add(voxels, data);
            }
        }

        public static bool TryGet(Voxel[,,] voxels, out VoxelGridData data)
        {
            data = null;
            return voxels != null && Data.TryGetValue(voxels, out data);
        }

        public static VoxelGridData GetOrCapture(Voxel[,,] voxels, double fallbackVoxelSize)
        {
            VoxelGridData data;
            if (TryGet(voxels, out data))
            {
                return data;
            }

            data = VoxelGridData.Capture(voxels, fallbackVoxelSize);
            Set(voxels, data);
            return data;
        }
    }
}
