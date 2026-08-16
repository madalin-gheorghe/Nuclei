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
        public readonly float[] Values;

        public VoxelScalarMap(double defaultValue, float[] values = null)
        {
            DefaultValue = defaultValue;
            Values = values;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public double Get(int flatIndex)
        {
            return Values != null ? Values[flatIndex] : DefaultValue;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public float GetSingle(int flatIndex)
        {
            return Values != null ? Values[flatIndex] : (float)DefaultValue;
        }

        public float[] ToDenseArray(int count)
        {
            float[] result = new float[count];
            if (Values != null)
            {
                Array.Copy(Values, result, Math.Min(Values.Length, result.Length));
                return result;
            }

            float defaultValue = (float)DefaultValue;
            if (defaultValue != 0)
            {
                for (int i = 0; i < result.Length; i++)
                {
                    result[i] = defaultValue;
                }
            }

            return result;
        }

        public double[] ToDenseDoubleArray(int count)
        {
            double[] result = new double[count];
            if (Values != null)
            {
                int copyCount = Math.Min(Values.Length, result.Length);
                for (int i = 0; i < copyCount; i++) result[i] = Values[i];
                return result;
            }

            if (DefaultValue != 0)
            {
                for (int i = 0; i < result.Length; i++) result[i] = DefaultValue;
            }

            return result;
        }
    }

    internal sealed class VoxelFrequencyMap
    {
        public readonly int DefaultValue;
        public readonly int[] Values;

        public VoxelFrequencyMap(int defaultValue, int[] values = null)
        {
            DefaultValue = Math.Max(1, defaultValue);
            Values = values;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int Get(int flatIndex)
        {
            return Values != null ? Math.Max(1, Values[flatIndex]) : DefaultValue;
        }

        public int[] ToDenseArray(int count)
        {
            int[] result = new int[count];
            if (Values != null)
            {
                Array.Copy(Values, result, Math.Min(Values.Length, result.Length));
                return result;
            }

            for (int i = 0; i < result.Length; i++) result[i] = DefaultValue;
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
        public readonly int ActiveCount;
        readonly int[] activeIndices;
        readonly int[] activeWords;
        readonly int[] activeWordPrefix;

        public VoxelScalarMap Density = new VoxelScalarMap(0);
        public VoxelScalarMap MinimumDensity = new VoxelScalarMap(-1);
        public VoxelScalarMap MaximumDensity = new VoxelScalarMap(-1);
        public VoxelScalarMap Speed = new VoxelScalarMap(-1);
        public VoxelScalarMap SensorDistance = new VoxelScalarMap(-1);
        public VoxelScalarMap SensorAngle = new VoxelScalarMap(-1);
        public VoxelScalarMap RotationAngle = new VoxelScalarMap(-1);
        public VoxelScalarMap Food = new VoxelScalarMap(-1);
        // Packed XYZ. Frequency is kept separately so a uniform frequency remains
        // one scalar instead of adding a fourth value to every voxel.
        public float[] VectorData;
        public float VectorDefaultX;
        public float VectorDefaultY;
        public float VectorDefaultZ;
        public VoxelFrequencyMap VectorFrequency = new VoxelFrequencyMap(3);
        bool hasContentSignature;
        long contentSignature;

        VoxelGridData(
            int resX,
            int resY,
            int resZ,
            double voxelSize,
            bool allVoxelsActive,
            int[] activeIndices,
            int[] activeWords,
            int[] activeWordPrefix,
            int activeCount)
        {
            int count = CheckedCellCount(resX, resY, resZ);
            ResX = resX;
            ResY = resY;
            ResZ = resZ;
            StrideY = resZ;
            StrideX = checked(resY * StrideY);
            Count = count;
            VoxelSize = voxelSize > 0 ? voxelSize : 1.0;
            AllVoxelsActive = allVoxelsActive;
            this.activeIndices = activeIndices;
            this.activeWords = activeWords;
            this.activeWordPrefix = activeWordPrefix;
            ActiveCount = activeCount;
        }

        public static VoxelGridData CreateFullDomain(int resX, int resY, int resZ, double voxelSize)
        {
            int count = CheckedCellCount(resX, resY, resZ);
            return new VoxelGridData(resX, resY, resZ, voxelSize, true, null, null, null, count);
        }

        public static VoxelGridData CreateEmptyDomain(int resX, int resY, int resZ, double voxelSize)
        {
            CheckedCellCount(resX, resY, resZ);
            return new VoxelGridData(resX, resY, resZ, voxelSize, false, Array.Empty<int>(), null, null, 0);
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
            int count = CheckedCellCount(resX, resY, resZ);
            double voxelSize = ResolveVoxelSize(voxels, fallbackVoxelSize);

            bool[] activeMask = new bool[count];
            int activeCount = 0;

            float[] density = null;
            float[] minimumDensity = null;
            float[] maximumDensity = null;
            float[] speed = null;
            float[] sensorDistance = null;
            float[] sensorAngle = null;
            float[] rotationAngle = null;
            float[] food = null;
            float[] vectorData = null;
            int[] vectorFrequencies = null;

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
                        activeCount++;
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
                            if (vectorData == null) vectorData = new float[checked(count * 3)];
                            int vectorOffset = flatIndex * 3;
                            vectorData[vectorOffset] = (float)voxel.voxelVector.X;
                            vectorData[vectorOffset + 1] = (float)voxel.voxelVector.Y;
                            vectorData[vectorOffset + 2] = (float)voxel.voxelVector.Z;
                        }
                        if (voxel.frequency != 3)
                        {
                            if (vectorFrequencies == null)
                            {
                                vectorFrequencies = new int[count];
                                for (int i = 0; i < count; i++) vectorFrequencies[i] = 3;
                            }
                            vectorFrequencies[flatIndex] = Math.Max(1, voxel.frequency);
                        }
                    }
                }
            }

            VoxelGridData data;
            if (activeCount == 0)
            {
                data = CreateEmptyDomain(resX, resY, resZ, voxelSize);
            }
            else if (activeCount == count)
            {
                data = CreateFullDomain(resX, resY, resZ, voxelSize);
            }
            else
            {
                data = CreatePartialDomain(resX, resY, resZ, voxelSize, activeMask, activeCount);
            }

            data.Density = new VoxelScalarMap(0, density);
            data.MinimumDensity = new VoxelScalarMap(-1, minimumDensity);
            data.MaximumDensity = new VoxelScalarMap(-1, maximumDensity);
            data.Speed = new VoxelScalarMap(-1, speed);
            data.SensorDistance = new VoxelScalarMap(-1, sensorDistance);
            data.SensorAngle = new VoxelScalarMap(-1, sensorAngle);
            data.RotationAngle = new VoxelScalarMap(-1, rotationAngle);
            data.Food = new VoxelScalarMap(-1, food);
            data.VectorData = vectorData;
            data.VectorFrequency = new VoxelFrequencyMap(3, vectorFrequencies);
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
            if (values.Count == Count)
            {
                float firstValue = (float)AdjustScalarValue(field, values[0]);
                bool uniform = true;
                for (int flatIndex = 1; flatIndex < Count; flatIndex++)
                {
                    if ((float)AdjustScalarValue(field, values[flatIndex]) != firstValue)
                    {
                        uniform = false;
                        break;
                    }
                }

                if (uniform)
                {
                    result.SetMap(field, new VoxelScalarMap(firstValue));
                    return result;
                }

                VoxelScalarMap inherited = GetMap(field);
                float[] mapValues = inherited.ToDenseArray(Count);

                for (int flatIndex = 0; flatIndex < Count; flatIndex++)
                {
                    mapValues[flatIndex] = (float)AdjustScalarValue(field, values[flatIndex]);
                }

                result.SetMap(field, new VoxelScalarMap(inherited.DefaultValue, mapValues));
            }
            else if (values.Count == ActiveCount)
            {
                VoxelScalarMap inherited = GetMap(field);
                float[] mapValues = inherited.ToDenseArray(Count);

                for (int ordinal = 0; ordinal < ActiveCount; ordinal++)
                {
                    int flatIndex = ActiveFlatIndexAt(ordinal);
                    mapValues[flatIndex] = (float)AdjustScalarValue(field, values[ordinal]);
                }

                result.SetMap(field, new VoxelScalarMap(inherited.DefaultValue, mapValues));
            }
            else if (values.Count == 1)
            {
                result.SetMap(field, new VoxelScalarMap(AdjustScalarValue(field, values[0])));
            }

            return result;
        }

        public VoxelGridData WithScalarMapValues(int fieldIndex, float[] values)
        {
            VoxelScalarField field = (VoxelScalarField)fieldIndex;
            VoxelScalarMap inherited = GetMap(field);
            VoxelGridData result = CloneSharedMaps();
            result.SetMap(field, new VoxelScalarMap(inherited.DefaultValue, values));
            return result;
        }

        public Voxel[,,] ToVoxelArray(bool materializeActiveVoxels)
        {
            Voxel[,,] result = new Voxel[ResX, ResY, ResZ];
            if (!materializeActiveVoxels)
            {
                return result;
            }

            Parallel.For(0, ActiveCount, ordinal =>
            {
                Voxel voxel = CreateVoxel(ActiveFlatIndexAt(ordinal), null);
                result[voxel.idX, voxel.idY, voxel.idZ] = voxel;
            });

            return result;
        }

        public VoxelGridData WithActiveMask(bool[] activeMask)
        {
            if (activeMask == null || activeMask.Length != Count)
            {
                return this;
            }

            int activeCount = 0;
            for (int i = 0; i < activeMask.Length; i++)
            {
                if (activeMask[i])
                {
                    activeCount++;
                }
            }

            VoxelGridData result = activeCount == Count
                ? CreateFullDomain(ResX, ResY, ResZ, VoxelSize)
                : CreatePartialDomain(ResX, ResY, ResZ, VoxelSize, activeMask, activeCount);

            result.Density = Density;
            result.MinimumDensity = MinimumDensity;
            result.MaximumDensity = MaximumDensity;
            result.Speed = Speed;
            result.SensorDistance = SensorDistance;
            result.SensorAngle = SensorAngle;
            result.RotationAngle = RotationAngle;
            result.Food = Food;
            result.VectorData = VectorData;
            result.VectorDefaultX = VectorDefaultX;
            result.VectorDefaultY = VectorDefaultY;
            result.VectorDefaultZ = VectorDefaultZ;
            result.VectorFrequency = VectorFrequency;
            return result;
        }

        public VoxelGridData WithActiveWords(int[] activeWords)
        {
            int expectedWordCount = (Count + 31) >> 5;
            if (activeWords == null || activeWords.Length != expectedWordCount)
            {
                return this;
            }

            if (activeWords.Length > 0 && (Count & 31) != 0)
            {
                uint validMask = (1u << (Count & 31)) - 1u;
                activeWords[activeWords.Length - 1] &= unchecked((int)validMask);
            }

            int activeCount = 0;
            for (int i = 0; i < activeWords.Length; i++)
            {
                activeCount += PopCount(unchecked((uint)activeWords[i]));
            }

            VoxelGridData result = activeCount == Count
                ? CreateFullDomain(ResX, ResY, ResZ, VoxelSize)
                : CreatePartialDomain(ResX, ResY, ResZ, VoxelSize, activeWords, activeCount);

            result.Density = Density;
            result.MinimumDensity = MinimumDensity;
            result.MaximumDensity = MaximumDensity;
            result.Speed = Speed;
            result.SensorDistance = SensorDistance;
            result.SensorAngle = SensorAngle;
            result.RotationAngle = RotationAngle;
            result.Food = Food;
            result.VectorData = VectorData;
            result.VectorDefaultX = VectorDefaultX;
            result.VectorDefaultY = VectorDefaultY;
            result.VectorDefaultZ = VectorDefaultZ;
            result.VectorFrequency = VectorFrequency;
            return result;
        }

        public VoxelGridData WithVectorValues(IList<Vector3d> vectors, IList<int> frequencies)
        {
            VoxelGridData result = CloneSharedMaps();
            if (Count == 0)
            {
                return result;
            }

            bool vectorPerVoxel = vectors != null && vectors.Count == ActiveCount;
            bool frequencyPerVoxel = frequencies != null && frequencies.Count == ActiveCount;
            Vector3d fallbackVector = vectors != null && vectors.Count > 0 ? vectors[0] : Vector3d.Zero;
            int fallbackFrequency = frequencies != null && frequencies.Count > 0 ? frequencies[0] : 1;
            if (fallbackFrequency < 1) fallbackFrequency = 1;

            Vector3d projectedFallback = ProjectVectorToGridPlane(fallbackVector);
            if (AllVoxelsActive && !vectorPerVoxel)
            {
                result.VectorData = null;
                result.VectorDefaultX = (float)projectedFallback.X;
                result.VectorDefaultY = (float)projectedFallback.Y;
                result.VectorDefaultZ = (float)projectedFallback.Z;
            }
            else
            {
                float[] vectorValues = ToDenseVectorArray();
                for (int ordinal = 0; ordinal < ActiveCount; ordinal++)
                {
                    int flatIndex = ActiveFlatIndexAt(ordinal);
                    Vector3d vector = vectorPerVoxel ? vectors[ordinal] : projectedFallback;
                    Vector3d projected = vectorPerVoxel ? ProjectVectorToGridPlane(vector) : vector;
                    int offset = flatIndex * 3;
                    vectorValues[offset] = (float)projected.X;
                    vectorValues[offset + 1] = (float)projected.Y;
                    vectorValues[offset + 2] = (float)projected.Z;
                }

                result.VectorData = vectorValues;
                result.VectorDefaultX = 0;
                result.VectorDefaultY = 0;
                result.VectorDefaultZ = 0;
            }

            result.VectorFrequency = BuildFrequencyMap(frequencies, frequencyPerVoxel, fallbackFrequency);
            return result;
        }

        public VoxelGridData WithVectorMapValues(Vector3d[] vectors)
        {
            VoxelGridData result = CloneSharedMaps();
            if (vectors == null)
            {
                result.VectorData = null;
                result.VectorDefaultX = 0;
                result.VectorDefaultY = 0;
                result.VectorDefaultZ = 0;
                return result;
            }

            float[] packed = new float[checked(Count * 3)];
            int count = Math.Min(Count, vectors.Length);
            for (int flatIndex = 0; flatIndex < count; flatIndex++)
            {
                int offset = flatIndex * 3;
                Vector3d vector = vectors[flatIndex];
                packed[offset] = (float)vector.X;
                packed[offset + 1] = (float)vector.Y;
                packed[offset + 2] = (float)vector.Z;
            }
            result.VectorData = packed;
            result.VectorDefaultX = 0;
            result.VectorDefaultY = 0;
            result.VectorDefaultZ = 0;
            return result;
        }

        public VoxelGridData WithPackedVectorMapValues(float[] vectorData)
        {
            VoxelGridData result = CloneSharedMaps();
            result.VectorData = vectorData;
            result.VectorDefaultX = 0;
            result.VectorDefaultY = 0;
            result.VectorDefaultZ = 0;
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

            voxel.voxelVector = GetVectorValue(flatIndex);
            voxel.vectorField = voxel.voxelVector.Length > 0;
            voxel.frequency = GetFrequencyValue(flatIndex);
            voxel.density = Density.Get(flatIndex);

            return voxel;
        }

        public Point3d CenterPoint(int flatIndex)
        {
            int x;
            int y;
            int z;
            CoordinatesFromFlatIndex(flatIndex, out x, out y, out z);
            return new Point3d(x * VoxelSize + VoxelSize / 2, y * VoxelSize + VoxelSize / 2, z * VoxelSize + VoxelSize / 2);
        }

        public double GetScalarValue(int fieldIndex, int flatIndex)
        {
            switch (fieldIndex)
            {
                case 0: return MinimumDensity.Get(flatIndex);
                case 1: return MaximumDensity.Get(flatIndex);
                case 2: return Speed.Get(flatIndex);
                case 3: return SensorDistance.Get(flatIndex);
                case 4: return SensorAngle.Get(flatIndex);
                case 5: return RotationAngle.Get(flatIndex);
                case 6: return Food.Get(flatIndex);
                case 7: return Density.Get(flatIndex);
                default: return 0;
            }
        }

        public Vector3d GetVectorValue(int flatIndex)
        {
            if (flatIndex < 0 || flatIndex >= Count) return Vector3d.Zero;
            if (VectorData == null) return new Vector3d(VectorDefaultX, VectorDefaultY, VectorDefaultZ);
            int offset = flatIndex * 3;
            return new Vector3d(VectorData[offset], VectorData[offset + 1], VectorData[offset + 2]);
        }

        public bool HasVectorValues
        {
            get { return VectorData != null || VectorDefaultX != 0 || VectorDefaultY != 0 || VectorDefaultZ != 0; }
        }

        public int GetFrequencyValue(int flatIndex)
        {
            if (flatIndex < 0 || flatIndex >= Count) return 3;
            return VectorFrequency != null ? VectorFrequency.Get(flatIndex) : 3;
        }

        public int ActiveFlatIndexAt(int ordinal)
        {
            if (ordinal < 0 || ordinal >= ActiveCount)
            {
                return -1;
            }

            if (AllVoxelsActive) return ordinal;
            if (activeIndices != null) return activeIndices[ordinal];

            int wordIndex = FindWordForOrdinal(ordinal);
            int bitOrdinal = ordinal - activeWordPrefix[wordIndex];
            int bit = SelectSetBit(unchecked((uint)activeWords[wordIndex]), bitOrdinal);
            return (wordIndex << 5) + bit;
        }

        public long ContentSignature()
        {
            if (hasContentSignature)
            {
                return contentSignature;
            }

            long hash = 1469598103934665603L;
            hash = HashInt(hash, ResX);
            hash = HashInt(hash, ResY);
            hash = HashInt(hash, ResZ);
            hash = HashDouble(hash, VoxelSize);
            hash = HashInt(hash, ActiveCount);
            hash = HashInt(hash, AllVoxelsActive ? 1 : 0);

            for (int ordinal = 0; ordinal < ActiveCount; ordinal++)
            {
                int flatIndex = ActiveFlatIndexAt(ordinal);
                if (flatIndex < 0 || flatIndex >= Count)
                {
                    continue;
                }

                hash = HashInt(hash, flatIndex);
                hash = HashDouble(hash, Density.Get(flatIndex));
                hash = HashDouble(hash, MinimumDensity.Get(flatIndex));
                hash = HashDouble(hash, MaximumDensity.Get(flatIndex));
                hash = HashDouble(hash, Speed.Get(flatIndex));
                hash = HashDouble(hash, SensorDistance.Get(flatIndex));
                hash = HashDouble(hash, SensorAngle.Get(flatIndex));
                hash = HashDouble(hash, RotationAngle.Get(flatIndex));
                hash = HashDouble(hash, Food.Get(flatIndex));

                Vector3d vector = GetVectorValue(flatIndex);
                hash = HashDouble(hash, vector.X);
                hash = HashDouble(hash, vector.Y);
                hash = HashDouble(hash, vector.Z);
                hash = HashInt(hash, GetFrequencyValue(flatIndex));
            }

            contentSignature = hash;
            hasContentSignature = true;
            return contentSignature;
        }

        static long HashInt(long hash, int value)
        {
            return HashLong(hash, value);
        }

        static long HashDouble(long hash, double value)
        {
            return HashLong(hash, BitConverter.DoubleToInt64Bits(value));
        }

        static long HashLong(long hash, long value)
        {
            unchecked
            {
                hash ^= value;
                hash *= 1099511628211L;
                return hash;
            }
        }

        public int[] BuildWalkableActiveFlatIndices()
        {
            if (ActiveCount <= 0)
            {
                return new int[0];
            }

            List<int> walkable = new List<int>(ActiveCount);
            for (int ordinal = 0; ordinal < ActiveCount; ordinal++)
            {
                int flatIndex = ActiveFlatIndexAt(ordinal);
                if (flatIndex >= 0 && IsWalkableFlatIndex(flatIndex))
                {
                    walkable.Add(flatIndex);
                }
            }

            return walkable.ToArray();
        }

        public bool MayContainBlockedMaxDensity()
        {
            return MaximumDensity.Values != null || VoxelOccupancy.IsBlockedMaxDensity(MaximumDensity.DefaultValue);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool IsWalkableFlatIndex(int flatIndex)
        {
            return flatIndex >= 0 &&
                   flatIndex < Count &&
                   IsActive(flatIndex) &&
                   !VoxelOccupancy.IsBlockedMaxDensity(MaximumDensity.Get(flatIndex));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int FlatIndex(int x, int y, int z)
        {
            return x * StrideX + y * StrideY + z;
        }

        public bool IsActive(int flatIndex)
        {
            return flatIndex >= 0 &&
                   flatIndex < Count &&
                   (AllVoxelsActive || IsPartialIndexActive(flatIndex));
        }

        internal void OrActiveSelection(int[] targetWords)
        {
            ValidateSelectionWords(targetWords);
            if (AllVoxelsActive)
            {
                FillSelectionWords(targetWords);
                return;
            }

            if (activeWords != null)
            {
                for (int i = 0; i < targetWords.Length; i++) targetWords[i] |= activeWords[i];
                return;
            }

            for (int i = 0; i < activeIndices.Length; i++)
            {
                int flatIndex = activeIndices[i];
                targetWords[flatIndex >> 5] |= unchecked((int)(1u << (flatIndex & 31)));
            }
        }

        internal void AndActiveSelection(int[] targetWords)
        {
            ValidateSelectionWords(targetWords);
            if (AllVoxelsActive) return;

            if (activeWords != null)
            {
                for (int i = 0; i < targetWords.Length; i++) targetWords[i] &= activeWords[i];
                return;
            }

            int[] filtered = new int[targetWords.Length];
            for (int i = 0; i < activeIndices.Length; i++)
            {
                int flatIndex = activeIndices[i];
                int mask = unchecked((int)(1u << (flatIndex & 31)));
                int wordIndex = flatIndex >> 5;
                if ((targetWords[wordIndex] & mask) != 0) filtered[wordIndex] |= mask;
            }

            Array.Copy(filtered, targetWords, filtered.Length);
        }

        internal void AndNotActiveSelection(int[] targetWords)
        {
            ValidateSelectionWords(targetWords);
            if (AllVoxelsActive)
            {
                Array.Clear(targetWords, 0, targetWords.Length);
                return;
            }

            if (activeWords != null)
            {
                for (int i = 0; i < targetWords.Length; i++) targetWords[i] &= ~activeWords[i];
                return;
            }

            for (int i = 0; i < activeIndices.Length; i++)
            {
                int flatIndex = activeIndices[i];
                targetWords[flatIndex >> 5] &= ~unchecked((int)(1u << (flatIndex & 31)));
            }
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

            if (activeIndices != null) return Array.BinarySearch(activeIndices, flatIndex);

            int wordIndex = flatIndex >> 5;
            int bit = flatIndex & 31;
            uint word = unchecked((uint)activeWords[wordIndex]);
            uint mask = 1u << bit;
            if ((word & mask) == 0) return -1;
            uint lowerBits = bit == 0 ? 0 : word & (mask - 1);
            return activeWordPrefix[wordIndex] + PopCount(lowerBits);
        }

        void ValidateSelectionWords(int[] words)
        {
            if (words == null || words.Length != ((Count + 31) >> 5))
            {
                throw new ArgumentException("Selection word count does not match the voxel field.", nameof(words));
            }
        }

        void FillSelectionWords(int[] words)
        {
            for (int i = 0; i < words.Length; i++) words[i] = -1;
            if (words.Length == 0 || (Count & 31) == 0) return;
            uint mask = (1u << (Count & 31)) - 1u;
            words[words.Length - 1] &= unchecked((int)mask);
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
            VoxelGridData result = new VoxelGridData(
                ResX,
                ResY,
                ResZ,
                VoxelSize,
                AllVoxelsActive,
                activeIndices,
                activeWords,
                activeWordPrefix,
                ActiveCount);
            result.Density = Density;
            result.MinimumDensity = MinimumDensity;
            result.MaximumDensity = MaximumDensity;
            result.Speed = Speed;
            result.SensorDistance = SensorDistance;
            result.SensorAngle = SensorAngle;
            result.RotationAngle = RotationAngle;
            result.Food = Food;
            result.VectorData = VectorData;
            result.VectorDefaultX = VectorDefaultX;
            result.VectorDefaultY = VectorDefaultY;
            result.VectorDefaultZ = VectorDefaultZ;
            result.VectorFrequency = VectorFrequency;
            return result;
        }

        float[] ToDenseVectorArray()
        {
            if (VectorData != null) return CopyVectorData(VectorData);

            float[] result = new float[checked(Count * 3)];
            if (VectorDefaultX == 0 && VectorDefaultY == 0 && VectorDefaultZ == 0) return result;
            for (int flatIndex = 0; flatIndex < Count; flatIndex++)
            {
                int offset = flatIndex * 3;
                result[offset] = VectorDefaultX;
                result[offset + 1] = VectorDefaultY;
                result[offset + 2] = VectorDefaultZ;
            }

            return result;
        }

        VoxelFrequencyMap BuildFrequencyMap(IList<int> frequencies, bool frequencyPerVoxel, int fallbackFrequency)
        {
            VoxelFrequencyMap inherited = VectorFrequency ?? new VoxelFrequencyMap(3);
            if (ActiveCount == 0) return inherited;

            bool uniform = !frequencyPerVoxel;
            int uniformValue = Math.Max(1, fallbackFrequency);
            if (frequencyPerVoxel)
            {
                uniformValue = Math.Max(1, frequencies[0]);
                uniform = true;
                for (int i = 1; i < ActiveCount; i++)
                {
                    if (Math.Max(1, frequencies[i]) != uniformValue)
                    {
                        uniform = false;
                        break;
                    }
                }
            }

            if (AllVoxelsActive && uniform) return new VoxelFrequencyMap(uniformValue);
            if (uniform && inherited.Values == null && inherited.DefaultValue == uniformValue) return inherited;

            int[] values = inherited.ToDenseArray(Count);
            for (int ordinal = 0; ordinal < ActiveCount; ordinal++)
            {
                int frequency = frequencyPerVoxel ? frequencies[ordinal] : fallbackFrequency;
                values[ActiveFlatIndexAt(ordinal)] = Math.Max(1, frequency);
            }
            return new VoxelFrequencyMap(inherited.DefaultValue, values);
        }

        Vector3d ProjectVectorToGridPlane(Vector3d vector)
        {
            if (ResZ == 1)
            {
                return new Vector3d(vector.X, vector.Y, 0);
            }

            if (ResY == 1)
            {
                return new Vector3d(vector.X, 0, vector.Z);
            }

            if (ResX == 1)
            {
                return new Vector3d(0, vector.Y, vector.Z);
            }

            return vector;
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

        static void CaptureNonDefault(ref float[] values, int count, int flatIndex, double value, double defaultValue)
        {
            if (value == defaultValue)
            {
                return;
            }

            if (values == null)
            {
                values = new float[count];
                if (defaultValue != 0)
                {
                    float defaultSingle = (float)defaultValue;
                    for (int i = 0; i < values.Length; i++)
                    {
                        values[i] = defaultSingle;
                    }
                }
            }

            values[flatIndex] = (float)value;
        }

        static VoxelGridData CreatePartialDomain(
            int resX,
            int resY,
            int resZ,
            double voxelSize,
            bool[] activeMask,
            int activeCount)
        {
            int count = CheckedCellCount(resX, resY, resZ);
            if (activeCount <= 0) return CreateEmptyDomain(resX, resY, resZ, voxelSize);
            if (activeCount >= count) return CreateFullDomain(resX, resY, resZ, voxelSize);

            int wordCount = (count + 31) >> 5;
            long sparseBytes = (long)activeCount * sizeof(int);
            long packedBytes = (long)wordCount * sizeof(int) + (long)(wordCount + 1) * sizeof(int);
            if (sparseBytes <= packedBytes)
            {
                int[] indices = new int[activeCount];
                int target = 0;
                for (int i = 0; i < activeMask.Length && target < activeCount; i++)
                {
                    if (activeMask[i]) indices[target++] = i;
                }

                return new VoxelGridData(resX, resY, resZ, voxelSize, false, indices, null, null, target);
            }

            int[] words = new int[wordCount];
            for (int i = 0; i < activeMask.Length; i++)
            {
                if (activeMask[i]) words[i >> 5] |= unchecked((int)(1u << (i & 31)));
            }

            int[] prefix = new int[wordCount + 1];
            for (int i = 0; i < wordCount; i++) prefix[i + 1] = prefix[i] + PopCount(unchecked((uint)words[i]));
            return new VoxelGridData(resX, resY, resZ, voxelSize, false, null, words, prefix, prefix[wordCount]);
        }

        static VoxelGridData CreatePartialDomain(
            int resX,
            int resY,
            int resZ,
            double voxelSize,
            int[] activeWords,
            int activeCount)
        {
            int count = CheckedCellCount(resX, resY, resZ);
            if (activeCount <= 0) return CreateEmptyDomain(resX, resY, resZ, voxelSize);
            if (activeCount >= count) return CreateFullDomain(resX, resY, resZ, voxelSize);

            int wordCount = (count + 31) >> 5;
            long sparseBytes = (long)activeCount * sizeof(int);
            long packedBytes = (long)wordCount * sizeof(int) + (long)(wordCount + 1) * sizeof(int);
            if (sparseBytes <= packedBytes)
            {
                int[] indices = new int[activeCount];
                int target = 0;
                for (int wordIndex = 0; wordIndex < activeWords.Length && target < activeCount; wordIndex++)
                {
                    uint pending = unchecked((uint)activeWords[wordIndex]);
                    while (pending != 0 && target < activeCount)
                    {
                        int bit = SelectSetBit(pending, 0);
                        int flatIndex = (wordIndex << 5) + bit;
                        if (flatIndex < count) indices[target++] = flatIndex;
                        pending &= pending - 1;
                    }
                }

                return new VoxelGridData(resX, resY, resZ, voxelSize, false, indices, null, null, target);
            }

            int[] prefix = new int[wordCount + 1];
            for (int i = 0; i < wordCount; i++)
            {
                prefix[i + 1] = prefix[i] + PopCount(unchecked((uint)activeWords[i]));
            }

            return new VoxelGridData(resX, resY, resZ, voxelSize, false, null, activeWords, prefix, prefix[wordCount]);
        }

        static int CheckedCellCount(int resX, int resY, int resZ)
        {
            if (resX < 0 || resY < 0 || resZ < 0)
            {
                throw new ArgumentOutOfRangeException("Voxel resolutions cannot be negative.");
            }

            long count = (long)resX * resY * resZ;
            if (count > int.MaxValue)
            {
                throw new ArgumentOutOfRangeException("Voxel resolution exceeds the supported field size.");
            }

            return (int)count;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        bool IsPartialIndexActive(int flatIndex)
        {
            if (activeIndices != null) return Array.BinarySearch(activeIndices, flatIndex) >= 0;
            return activeWords != null && (activeWords[flatIndex >> 5] & unchecked((int)(1u << (flatIndex & 31)))) != 0;
        }

        int FindWordForOrdinal(int ordinal)
        {
            int low = 0;
            int high = activeWords.Length - 1;
            while (low < high)
            {
                int middle = low + ((high - low) >> 1);
                if (activeWordPrefix[middle + 1] <= ordinal) low = middle + 1;
                else high = middle;
            }

            return low;
        }

        static int PopCount(uint value)
        {
            value -= (value >> 1) & 0x55555555u;
            value = (value & 0x33333333u) + ((value >> 2) & 0x33333333u);
            return (int)((((value + (value >> 4)) & 0x0F0F0F0Fu) * 0x01010101u) >> 24);
        }

        static int SelectSetBit(uint value, int ordinal)
        {
            while (ordinal-- > 0) value &= value - 1;
            int bit = 0;
            while ((value & 1u) == 0)
            {
                value >>= 1;
                bit++;
            }

            return bit;
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

        static float[] CopyVectorData(float[] source)
        {
            float[] result = new float[source.Length];
            Array.Copy(source, result, source.Length);
            return result;
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
