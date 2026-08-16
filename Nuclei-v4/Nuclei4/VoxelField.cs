using System;
using System.Runtime.CompilerServices;
using System.Threading;

using Grasshopper.Kernel;
using Grasshopper.Kernel.Types;

namespace Nuclei3
{
    /// <summary>
    /// Lightweight runtime payload for a voxel field. Static maps are immutable and
    /// structurally shared; solver-owned dynamic fields are updated only on readback.
    /// </summary>
    public sealed class VoxelField
    {
        static long nextId;

        internal VoxelField(VoxelGridData data, VoxelDynamicData dynamicData = null, Voxel[,,] legacyVoxels = null)
        {
            Data = data ?? VoxelGridData.CreateFullDomain(0, 0, 0, 1.0);
            Dynamic = dynamicData;
            LegacyVoxels = legacyVoxels;
            RuntimeId = Interlocked.Increment(ref nextId);
        }

        internal VoxelGridData Data { get; private set; }
        internal VoxelDynamicData Dynamic { get; private set; }
        internal Voxel[,,] LegacyVoxels { get; private set; }
        internal long RuntimeId { get; private set; }
        internal Func<float, int, int, GpuVolumeMeshResult> GpuVolumeMeshProvider;
        internal Action DynamicStateSynchronizer;

        public int ResX { get { return Data.ResX; } }
        public int ResY { get { return Data.ResY; } }
        public int ResZ { get { return Data.ResZ; } }
        public int Count { get { return Data.Count; } }
        public int ActiveCount { get { return Data.ActiveCount; } }
        public double VoxelSize { get { return Data.VoxelSize; } }

        internal int DynamicVersion
        {
            get { return Dynamic != null ? Dynamic.Version : 0; }
        }

        internal VoxelField WithData(VoxelGridData data)
        {
            return new VoxelField(data, Dynamic);
        }

        internal VoxelField ForkRuntimeState()
        {
            VoxelDynamicData dynamicCopy = null;
            if (Dynamic != null)
            {
                dynamicCopy = new VoxelDynamicData
                {
                    Density = Dynamic.Density,
                    AntFoodPheromone = Dynamic.AntFoodPheromone,
                    AntBasePheromone = Dynamic.AntBasePheromone,
                    RemainingFood = Dynamic.RemainingFood
                };
            }

            return new VoxelField(Data, dynamicCopy, LegacyVoxels);
        }

        internal VoxelField ForkResetState()
        {
            // Solver-generated density, pheromones, and remaining food must never
            // become the source of a later reset.
            return new VoxelField(Data);
        }

        internal void ReplaceDynamicData(VoxelDynamicData dynamicData)
        {
            Dynamic = dynamicData;
        }

        internal void EnsureDynamicStateCurrent()
        {
            if (DynamicStateSynchronizer != null)
            {
                DynamicStateSynchronizer();
            }
        }

        internal void UpdateDynamicFields(float[] density, float[] antFood, float[] antBase, float[] remainingFood)
        {
            if (Dynamic == null)
            {
                Dynamic = new VoxelDynamicData();
            }

            Dynamic.Density = density;
            Dynamic.AntFoodPheromone = antFood;
            Dynamic.AntBasePheromone = antBase;
            Dynamic.RemainingFood = remainingFood;
            Dynamic.IncrementVersion();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal double GetScalarValue(int fieldIndex, int flatIndex)
        {
            if (flatIndex < 0 || flatIndex >= Data.Count)
            {
                return 0;
            }

            if (fieldIndex == VoxelPreviewField.Food && Dynamic != null && Dynamic.RemainingFood != null)
            {
                return Dynamic.RemainingFood[flatIndex];
            }

            if (fieldIndex == VoxelPreviewField.SlimeChemoattractants)
            {
                if (Dynamic != null && Dynamic.Density != null) return Dynamic.Density[flatIndex];
                return LegacyDynamicValue(flatIndex, fieldIndex, Data.Density.Get(flatIndex));
            }

            if (fieldIndex == VoxelPreviewField.AntFoodPheromones)
            {
                if (Dynamic != null && Dynamic.AntFoodPheromone != null) return Dynamic.AntFoodPheromone[flatIndex];
                return LegacyDynamicValue(flatIndex, fieldIndex, 0);
            }

            if (fieldIndex == VoxelPreviewField.AntBasePheromones)
            {
                if (Dynamic != null && Dynamic.AntBasePheromone != null) return Dynamic.AntBasePheromone[flatIndex];
                return LegacyDynamicValue(flatIndex, fieldIndex, 0);
            }

            return Data.GetScalarValue(fieldIndex, flatIndex);
        }

        internal Voxel CreateVoxel(int flatIndex)
        {
            if (!Data.IsActive(flatIndex)) return null;

            Voxel voxel = Data.CreateVoxel(flatIndex, null);
            voxel.density = GetScalarValue(VoxelPreviewField.SlimeChemoattractants, flatIndex);
            voxel.towardsFoodPheromone = GetScalarValue(VoxelPreviewField.AntFoodPheromones, flatIndex);
            voxel.towardsBasePheromone = GetScalarValue(VoxelPreviewField.AntBasePheromones, flatIndex);
            voxel.food = GetScalarValue(VoxelPreviewField.Food, flatIndex);
            return voxel;
        }

        internal Voxel[,,] MaterializeLegacyArray()
        {
            if (LegacyVoxels != null)
            {
                return LegacyVoxels;
            }

            Voxel[,,] result = new Voxel[ResX, ResY, ResZ];
            for (int ordinal = 0; ordinal < Data.ActiveCount; ordinal++)
            {
                int flatIndex = Data.ActiveFlatIndexAt(ordinal);
                Voxel voxel = CreateVoxel(flatIndex);
                result[voxel.idX, voxel.idY, voxel.idZ] = voxel;
            }

            VoxelGridRegistry.Set(result, Data);
            return result;
        }

        double LegacyDynamicValue(int flatIndex, int fieldIndex, double fallback)
        {
            if (LegacyVoxels == null) return fallback;

            int x;
            int y;
            int z;
            Data.CoordinatesFromFlatIndex(flatIndex, out x, out y, out z);
            Voxel voxel = LegacyVoxels[x, y, z];
            if (voxel == null) return fallback;

            switch (fieldIndex)
            {
                case VoxelPreviewField.SlimeChemoattractants: return voxel.density;
                case VoxelPreviewField.AntFoodPheromones: return voxel.towardsFoodPheromone;
                case VoxelPreviewField.AntBasePheromones: return voxel.towardsBasePheromone;
                default: return fallback;
            }
        }

        public override string ToString()
        {
            return "Voxel Field " + ResX + " x " + ResY + " x " + ResZ + " (" + ActiveCount + " active)";
        }
    }

    internal sealed class VoxelDynamicData
    {
        public float[] Density;
        public float[] AntFoodPheromone;
        public float[] AntBasePheromone;
        public float[] RemainingFood;
        int version;

        public int Version { get { return Volatile.Read(ref version); } }

        public void IncrementVersion()
        {
            Interlocked.Increment(ref version);
        }
    }

    internal static class VoxelFieldAccess
    {
        static readonly ConditionalWeakTable<Voxel[,,], VoxelField> LegacyFields = new ConditionalWeakTable<Voxel[,,], VoxelField>();
        static readonly object SyncRoot = new object();

        public static bool TryGet(IGH_DataAccess dataAccess, int index, double fallbackVoxelSize, out VoxelField field)
        {
            object value = null;
            field = null;
            return dataAccess != null && dataAccess.GetData(index, ref value) && TryResolve(value, fallbackVoxelSize, out field);
        }

        public static bool TryGet(IGH_DataAccess dataAccess, string name, double fallbackVoxelSize, out VoxelField field)
        {
            object value = null;
            field = null;
            return dataAccess != null && dataAccess.GetData(name, ref value) && TryResolve(value, fallbackVoxelSize, out field);
        }

        public static bool TryResolve(object value, double fallbackVoxelSize, out VoxelField field)
        {
            field = value as VoxelField;
            if (field != null) return true;

            GH_ObjectWrapper wrapper = value as GH_ObjectWrapper;
            if (wrapper != null && !ReferenceEquals(wrapper.Value, value))
            {
                return TryResolve(wrapper.Value, fallbackVoxelSize, out field);
            }

            IGH_Goo goo = value as IGH_Goo;
            if (goo != null)
            {
                object scriptValue = goo.ScriptVariable();
                if (scriptValue != null && !ReferenceEquals(scriptValue, value))
                {
                    return TryResolve(scriptValue, fallbackVoxelSize, out field);
                }
            }

            Voxel[,,] legacy = value as Voxel[,,];
            if (legacy == null) return false;

            lock (SyncRoot)
            {
                if (!LegacyFields.TryGetValue(legacy, out field))
                {
                    field = new VoxelField(VoxelGridRegistry.GetOrCapture(legacy, fallbackVoxelSize), null, legacy);
                    LegacyFields.Add(legacy, field);
                }
            }

            return true;
        }
    }
}
