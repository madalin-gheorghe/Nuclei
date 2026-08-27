using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Runtime.CompilerServices;

using Grasshopper.Kernel;
using Rhino.Geometry;
using Grasshopper;

namespace Nuclei4
{
    internal sealed class VoxelDensityStore
    {
        public double[] Values;
        public double[] MinDensity;
        public double[] MaxDensity;
        public double[] InputMinDensity;
        public double[] InputMaxDensity;
        public double[] TowardsFoodPheromone;
        public double[] TowardsBasePheromone;
        public double[] SpeedMultiplier;
        public double[] SensorAngleMultiplier;
        public double[] SensorDistanceMultiplier;
        public double[] RotationAngleMultiplier;
        public double[] Food;
        public double[] AntFood;
        public Vector3d[] Vectors;
        public int[] Frequencies;
        public bool[] VectorField;
        public bool[] Boundary;

        public VoxelDensityStore(double[] values)
        {
            Values = values;
        }

        public void AttachStaticMaps(VoxelGridData data)
        {
            if (data == null) return;

            MinDensity = CopyMap(data.MinimumDensity);
            MaxDensity = CopyMap(data.MaximumDensity);
            InputMinDensity = CopyValues(MinDensity);
            InputMaxDensity = CopyValues(MaxDensity);
            SpeedMultiplier = CopyMap(data.Speed);
            SensorDistanceMultiplier = CopyMap(data.SensorDistance);
            SensorAngleMultiplier = CopyMap(data.SensorAngle);
            RotationAngleMultiplier = CopyMap(data.RotationAngle);
            Food = CopyMap(data.Food);
            AntFood = CopyMap(data.AntFood);
            if (data.VectorData != null)
            {
                Vectors = new Vector3d[data.Count];
                Frequencies = new int[data.Count];
                for (int i = 0; i < data.Count; i++)
                {
                    Vectors[i] = data.GetVectorValue(i);
                    Frequencies[i] = data.GetFrequencyValue(i);
                }
            }
            else
            {
                Vectors = null;
                Frequencies = null;
            }

            int count = data.Count;
            Boundary = count > 0 ? new bool[count] : null;
            if (Vectors != null)
            {
                VectorField = new bool[count];
                for (int i = 0; i < Math.Min(Vectors.Length, count); i++)
                {
                    VectorField[i] = Vectors[i].Length > 0;
                }
            }
        }

        public void EnsureAntPheromoneArrays(int count)
        {
            if (count <= 0) return;
            if (TowardsFoodPheromone == null || TowardsFoodPheromone.Length != count)
            {
                TowardsFoodPheromone = new double[count];
            }

            if (TowardsBasePheromone == null || TowardsBasePheromone.Length != count)
            {
                TowardsBasePheromone = new double[count];
            }
        }

        static double[] CopyMap(VoxelScalarMap map)
        {
            return map != null && map.Values != null
                ? map.ToDenseDoubleArray(map.Values.Length)
                : null;
        }

        static double[] CopyValues(double[] source)
        {
            if (source == null) return null;
            double[] copy = new double[source.Length];
            Array.Copy(source, copy, source.Length);
            return copy;
        }

        static int[] CopyValues(int[] source)
        {
            if (source == null) return null;
            int[] copy = new int[source.Length];
            Array.Copy(source, copy, source.Length);
            return copy;
        }

        static Vector3d[] CopyValues(Vector3d[] source)
        {
            if (source == null) return null;
            Vector3d[] copy = new Vector3d[source.Length];
            Array.Copy(source, copy, source.Length);
            return copy;
        }
    }

    internal static class VoxelOccupancy
    {
        public const double BlockedMaxDensityThreshold = 0.01;
        public const float BlockedMaxDensityThresholdF = 0.01f;

        public static bool IsBlockedMaxDensity(double maxDensity)
        {
            return maxDensity >= 0 && maxDensity < BlockedMaxDensityThreshold;
        }

        public static bool IsWalkable(Voxel voxel)
        {
            return voxel != null && !IsBlockedMaxDensity(voxel.maxDensity);
        }
    }

    public class Voxel
    {
        public Point3d loc;
        public int idX;
        public int idY;
        public int idZ;
        public int flatIndex;
        public double voxelSize = 1;

        double minDensityValue = -1;
        double maxDensityValue = -1;
        double inputMinDensityValue = -1;
        double inputMaxDensityValue = -1;

        double densityValue = 0;
        internal VoxelDensityStore densityStore;

        public double density
        {
            get
            {
                VoxelDensityStore store = densityStore;
                double[] values = store != null ? store.Values : null;
                if (values != null && flatIndex >= 0 && flatIndex < values.Length)
                {
                    return values[flatIndex];
                }

                return densityValue;
            }

            set
            {
                densityValue = value;

                VoxelDensityStore store = densityStore;
                double[] values = store != null ? store.Values : null;
                if (values != null && flatIndex >= 0 && flatIndex < values.Length)
                {
                    values[flatIndex] = value;
                }
            }
        }

        double towardsFoodPheromoneValue = 0;
        double towardsBasePheromoneValue = 0;

        double speedMultiplierValue = -1;
        double sensorAngleMultiplierValue = -1;
        double sensorDistanceMultiplierValue = -1;
        double rotationAngleMultiplierValue = -1;

        double foodValue = -1;
        double antFoodValue = -1;

        Vector3d voxelVectorValue = new Vector3d(0,0,0);
        int frequencyValue = 3;
        bool vectorFieldValue = false;

        public int particleCount = 0;

        bool boundaryValue = false;

        public double minDensity
        {
            get { return GetStoreValue(densityStore != null ? densityStore.MinDensity : null, minDensityValue); }
            set { SetStoreValue(densityStore != null ? densityStore.MinDensity : null, ref minDensityValue, value); }
        }

        public double maxDensity
        {
            get { return GetStoreValue(densityStore != null ? densityStore.MaxDensity : null, maxDensityValue); }
            set { SetStoreValue(densityStore != null ? densityStore.MaxDensity : null, ref maxDensityValue, value); }
        }

        public double inputMinDensity
        {
            get { return GetStoreValue(densityStore != null ? densityStore.InputMinDensity : null, inputMinDensityValue); }
            set { SetStoreValue(densityStore != null ? densityStore.InputMinDensity : null, ref inputMinDensityValue, value); }
        }

        public double inputMaxDensity
        {
            get { return GetStoreValue(densityStore != null ? densityStore.InputMaxDensity : null, inputMaxDensityValue); }
            set { SetStoreValue(densityStore != null ? densityStore.InputMaxDensity : null, ref inputMaxDensityValue, value); }
        }

        public double towardsFoodPheromone
        {
            get { return GetStoreValue(densityStore != null ? densityStore.TowardsFoodPheromone : null, towardsFoodPheromoneValue); }
            set { SetStoreValue(densityStore != null ? densityStore.TowardsFoodPheromone : null, ref towardsFoodPheromoneValue, value); }
        }

        public double towardsBasePheromone
        {
            get { return GetStoreValue(densityStore != null ? densityStore.TowardsBasePheromone : null, towardsBasePheromoneValue); }
            set { SetStoreValue(densityStore != null ? densityStore.TowardsBasePheromone : null, ref towardsBasePheromoneValue, value); }
        }

        public double speedMultiplier
        {
            get { return GetStoreValue(densityStore != null ? densityStore.SpeedMultiplier : null, speedMultiplierValue); }
            set { SetStoreValue(densityStore != null ? densityStore.SpeedMultiplier : null, ref speedMultiplierValue, value); }
        }

        public double sensorAngleMultiplier
        {
            get { return GetStoreValue(densityStore != null ? densityStore.SensorAngleMultiplier : null, sensorAngleMultiplierValue); }
            set { SetStoreValue(densityStore != null ? densityStore.SensorAngleMultiplier : null, ref sensorAngleMultiplierValue, value); }
        }

        public double sensorDistanceMultiplier
        {
            get { return GetStoreValue(densityStore != null ? densityStore.SensorDistanceMultiplier : null, sensorDistanceMultiplierValue); }
            set { SetStoreValue(densityStore != null ? densityStore.SensorDistanceMultiplier : null, ref sensorDistanceMultiplierValue, value); }
        }

        public double rotationAngleMultiplier
        {
            get { return GetStoreValue(densityStore != null ? densityStore.RotationAngleMultiplier : null, rotationAngleMultiplierValue); }
            set { SetStoreValue(densityStore != null ? densityStore.RotationAngleMultiplier : null, ref rotationAngleMultiplierValue, value); }
        }

        public double food
        {
            get { return GetStoreValue(densityStore != null ? densityStore.Food : null, foodValue); }
            set { SetStoreValue(densityStore != null ? densityStore.Food : null, ref foodValue, value); }
        }

        public double antFood
        {
            get { return GetStoreValue(densityStore != null ? densityStore.AntFood : null, antFoodValue); }
            set { SetStoreValue(densityStore != null ? densityStore.AntFood : null, ref antFoodValue, value); }
        }

        public Vector3d voxelVector
        {
            get { return GetStoreValue(densityStore != null ? densityStore.Vectors : null, voxelVectorValue); }
            set { SetStoreValue(densityStore != null ? densityStore.Vectors : null, ref voxelVectorValue, value); }
        }

        public int frequency
        {
            get { return GetStoreValue(densityStore != null ? densityStore.Frequencies : null, frequencyValue); }
            set { SetStoreValue(densityStore != null ? densityStore.Frequencies : null, ref frequencyValue, value); }
        }

        public bool vectorField
        {
            get { return GetStoreValue(densityStore != null ? densityStore.VectorField : null, vectorFieldValue); }
            set { SetStoreValue(densityStore != null ? densityStore.VectorField : null, ref vectorFieldValue, value); }
        }

        public bool boundary
        {
            get { return GetStoreValue(densityStore != null ? densityStore.Boundary : null, boundaryValue); }
            set { SetStoreValue(densityStore != null ? densityStore.Boundary : null, ref boundaryValue, value); }
        }

        //-------------------------------------------------------------------

        public Voxel(double _voxelSize, int _idX, int _idY, int _idZ)
        {
            voxelSize = _voxelSize;

            idX = _idX;
            idY = _idY;
            idZ = _idZ;

            loc = new Point3d(idX * voxelSize + voxelSize / 2, idY * voxelSize + voxelSize / 2, idZ * voxelSize + voxelSize / 2);

            minDensity = -1;
            maxDensity = -1;
            density = 0;

            particleCount = 0;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        bool HasStoreValue<T>(T[] values)
        {
            return values != null && flatIndex >= 0 && flatIndex < values.Length;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        double GetStoreValue(double[] values, double fallback)
        {
            return HasStoreValue(values) ? values[flatIndex] : fallback;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        void SetStoreValue(double[] values, ref double fallback, double value)
        {
            fallback = value;
            if (HasStoreValue(values))
            {
                values[flatIndex] = value;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        Vector3d GetStoreValue(Vector3d[] values, Vector3d fallback)
        {
            return HasStoreValue(values) ? values[flatIndex] : fallback;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        void SetStoreValue(Vector3d[] values, ref Vector3d fallback, Vector3d value)
        {
            fallback = value;
            if (HasStoreValue(values))
            {
                values[flatIndex] = value;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        int GetStoreValue(int[] values, int fallback)
        {
            return HasStoreValue(values) ? values[flatIndex] : fallback;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        void SetStoreValue(int[] values, ref int fallback, int value)
        {
            fallback = value;
            if (HasStoreValue(values))
            {
                values[flatIndex] = value;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        bool GetStoreValue(bool[] values, bool fallback)
        {
            return HasStoreValue(values) ? values[flatIndex] : fallback;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        void SetStoreValue(bool[] values, ref bool fallback, bool value)
        {
            fallback = value;
            if (HasStoreValue(values))
            {
                values[flatIndex] = value;
            }
        }
    }
}
