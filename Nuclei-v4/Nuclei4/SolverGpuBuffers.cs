using System;
using System.Collections.Generic;

using Rhino.Geometry;

namespace Nuclei4
{
    internal sealed class SolverGpuInputSnapshot : GpuSolverInput
    {
        bool wrapBoundaries;

        public VoxelField Field;
        public Voxel[,,] Voxels;
        public ParticleList Particles;
        public ParticleGroup[] ParticleGroups;
        public int ActiveVoxelCount;
        public float[][] StaticVoxelFields;
        public float[] StaticVoxelFieldMaximums;
        public float[] VoxelVectorsXyz;

        public static SolverGpuInputSnapshot Capture(Voxel[,,] inputVoxels, IList<ParticleGroup> particleGroups)
        {
            SolverGpuInputSnapshot snapshot = new SolverGpuInputSnapshot();

            snapshot.DetectPopulationKinds(particleGroups);
            snapshot.CaptureVoxels(inputVoxels);
            snapshot.CaptureParticles(particleGroups);

            return snapshot;
        }

        public static SolverGpuInputSnapshot Capture(VoxelField inputField, IList<ParticleGroup> particleGroups, bool wrapBoundaries = false)
        {
            SolverGpuInputSnapshot snapshot = new SolverGpuInputSnapshot();
            snapshot.DetectPopulationKinds(particleGroups);
            snapshot.CaptureCompactVoxels(inputField, false, wrapBoundaries);
            snapshot.CaptureParticles(particleGroups);
            return snapshot;
        }

        public static SolverGpuInputSnapshot CaptureVoxelFields(VoxelField inputField, IList<ParticleGroup> particleGroups, bool wrapBoundaries = false)
        {
            SolverGpuInputSnapshot snapshot = new SolverGpuInputSnapshot();
            snapshot.DetectPopulationKinds(particleGroups);
            snapshot.CaptureCompactVoxels(inputField, true, wrapBoundaries);
            return snapshot;
        }

        void CaptureCompactVoxels(VoxelField inputField, bool includeDynamicState, bool wrapBoundaries)
        {
            this.wrapBoundaries = wrapBoundaries;
            VoxelField sourceField = inputField ?? new VoxelField(VoxelGridData.CreateFullDomain(0, 0, 0, 1.0));
            Field = includeDynamicState ? sourceField.ForkRuntimeState() : sourceField.ForkResetState();
            Field.ConfigureSolverBoundaries(wrapBoundaries);
            VoxelGridData data = Field.Data;
            ResX = data.ResX;
            ResY = data.ResY;
            ResZ = data.ResZ;
            ActiveVoxelCount = data.ActiveCount;
            VoxelSize = (float)(data.VoxelSize > 0 ? data.VoxelSize : 1.0);
            int voxelCount = data.Count;

            HasStaticPreviewInput = true;
            StaticMinimumDensityValues = data.MinimumDensity.Values;
            StaticMaximumDensityValues = data.MaximumDensity.Values;
            StaticSpeedValues = data.Speed.Values;
            StaticSensorDistanceValues = data.SensorDistance.Values;
            StaticSensorAngleValues = data.SensorAngle.Values;
            StaticRotationAngleValues = data.RotationAngle.Values;
            StaticMinimumDensityDefault = data.MinimumDensity.DefaultValue;
            StaticMaximumDensityDefault = data.MaximumDensity.DefaultValue;
            StaticSpeedDefault = data.Speed.DefaultValue;
            StaticSensorDistanceDefault = data.SensorDistance.DefaultValue;
            StaticSensorAngleDefault = data.SensorAngle.DefaultValue;
            StaticRotationAngleDefault = data.RotationAngle.DefaultValue;

            VoxelDensity = null;
            AntFoodPheromone = null;
            AntBasePheromone = null;

            SpeedDefault = MultiplierOrDefault(data.Speed.DefaultValue);
            SensorDistanceDefault = MultiplierOrDefault(data.SensorDistance.DefaultValue);
            SensorAngleDefault = MultiplierOrDefault(data.SensorAngle.DefaultValue);
            RotationAngleDefault = MultiplierOrDefault(data.RotationAngle.DefaultValue);
            int behaviorElementCount = 0;
            SpeedOffset = ReserveChannel(ref behaviorElementCount, voxelCount, HasDenseMap(data.Speed));
            SensorDistanceOffset = ReserveChannel(ref behaviorElementCount, voxelCount, HasDenseMap(data.SensorDistance));
            SensorAngleOffset = ReserveChannel(ref behaviorElementCount, voxelCount, HasDenseMap(data.SensorAngle));
            RotationAngleOffset = ReserveChannel(ref behaviorElementCount, voxelCount, HasDenseMap(data.RotationAngle));
            bool hasVectors = data.HasVectorValues;
            MinimumDensityDefault = DensityLimitOrUnset(data.MinimumDensity.DefaultValue);
            MaximumDensityDefault = DensityLimitOrUnset(data.MaximumDensity.DefaultValue);
            int densityLimitElementCount = 0;
            MinimumDensityOffset = ReserveChannel(ref densityLimitElementCount, voxelCount, HasDenseMap(data.MinimumDensity));
            MaximumDensityOffset = ReserveChannel(ref densityLimitElementCount, voxelCount, HasDenseMap(data.MaximumDensity));
            bool hasFlags = !data.AllVoxelsActive || data.MayContainBlockedMaxDensity();

            VoxelBehaviorData = behaviorElementCount > 0 ? new float[behaviorElementCount] : null;
            VoxelVectorData = hasVectors ? data.VectorData : null;
            VoxelVectorDefaultX = data.VectorDefaultX;
            VoxelVectorDefaultY = data.VectorDefaultY;
            VoxelVectorDefaultZ = data.VectorDefaultZ;
            VoxelVectorFrequencies = hasVectors && data.VectorFrequency != null ? data.VectorFrequency.Values : null;
            VoxelVectorDefaultFrequency = data.VectorFrequency != null ? data.VectorFrequency.DefaultValue : 3;
            VoxelDensityLimits = densityLimitElementCount > 0 ? new float[densityLimitElementCount] : null;
            InitialFood = null;
            InitialAntFood = null;
            ActiveVoxelFlags = data.AllVoxelsActive ? null : new uint[(voxelCount + 31) >> 5];
            VoxelFlags = hasFlags ? new uint[(voxelCount + 31) >> 5] : null;
            StaticVoxelFields = null;
            StaticVoxelFieldMaximums = null;
            VoxelVectorsXyz = null;

            bool hasDynamicDensity = includeDynamicState && Field.Dynamic != null && Field.Dynamic.Density != null;
            bool hasDynamicAntFood = includeDynamicState && Field.Dynamic != null && Field.Dynamic.AntFoodPheromone != null;
            bool hasDynamicAntBase = includeDynamicState && Field.Dynamic != null && Field.Dynamic.AntBasePheromone != null;
            bool hasDynamicFood = includeDynamicState && Field.Dynamic != null && Field.Dynamic.RemainingFood != null;
            bool mayHaveDensity = hasDynamicDensity || data.Density.Values != null || data.Density.DefaultValue != 0 || Field.LegacyVoxels != null;
            bool mayHaveAntFields = HasAntParticles && (hasDynamicAntFood || hasDynamicAntBase || Field.LegacyVoxels != null);
            // hasDynamicFood tracks the remaining-food readback, which is the ant map
            // after the food split. Both maps must be able to force the voxel scan;
            // omitting ant food here left InitialAntFood null whenever nothing else
            // required a scan, so ants saw no food while the preview still showed it.
            bool mayHaveFood = data.Food.Values != null || PositiveValueOrZero(data.Food.DefaultValue) > 0;
            bool mayHaveAntFood = hasDynamicFood || data.AntFood.Values != null
                || PositiveValueOrZero(data.AntFood.DefaultValue) > 0;
            bool needsFlagScan = hasFlags && (!data.AllVoxelsActive || data.MaximumDensity.Values != null);
            bool needsVoxelScan = mayHaveDensity || mayHaveAntFields || mayHaveFood || mayHaveAntFood ||
                VoxelBehaviorData != null || VoxelDensityLimits != null || needsFlagScan;

            if (!needsVoxelScan) return;

            for (int ordinal = 0; ordinal < data.ActiveCount; ordinal++)
            {
                int flatIndex = data.ActiveFlatIndexAt(ordinal);
                SetFlag(ActiveVoxelFlags, flatIndex);
                if (ProcessDensity)
                {
                    float density = (float)Field.GetScalarValue(VoxelPreviewField.SlimeChemoattractants, flatIndex);
                    if (density != 0)
                    {
                        if (VoxelDensity == null) VoxelDensity = new float[voxelCount];
                        VoxelDensity[flatIndex] = density;
                    }
                }
                if (HasAntParticles)
                {
                    float foodPheromone = (float)Math.Max(0, Field.GetScalarValue(VoxelPreviewField.AntFoodPheromones, flatIndex));
                    float basePheromone = (float)Math.Max(0, Field.GetScalarValue(VoxelPreviewField.AntBasePheromones, flatIndex));
                    if (foodPheromone != 0)
                    {
                        if (AntFoodPheromone == null) AntFoodPheromone = new float[voxelCount];
                        AntFoodPheromone[flatIndex] = foodPheromone;
                    }
                    if (basePheromone != 0)
                    {
                        if (AntBasePheromone == null) AntBasePheromone = new float[voxelCount];
                        AntBasePheromone[flatIndex] = basePheromone;
                    }
                }
                SetPackedValue(VoxelBehaviorData, SpeedOffset, flatIndex, MultiplierOrDefault(data.Speed.Get(flatIndex)));
                SetPackedValue(VoxelBehaviorData, SensorDistanceOffset, flatIndex, MultiplierOrDefault(data.SensorDistance.Get(flatIndex)));
                SetPackedValue(VoxelBehaviorData, SensorAngleOffset, flatIndex, MultiplierOrDefault(data.SensorAngle.Get(flatIndex)));
                SetPackedValue(VoxelBehaviorData, RotationAngleOffset, flatIndex, MultiplierOrDefault(data.RotationAngle.Get(flatIndex)));
                SetPackedValue(VoxelDensityLimits, MinimumDensityOffset, flatIndex, DensityLimitOrUnset(data.MinimumDensity.Get(flatIndex)));
                SetPackedValue(VoxelDensityLimits, MaximumDensityOffset, flatIndex, DensityLimitOrUnset(data.MaximumDensity.Get(flatIndex)));
                float food = PositiveValueOrZero(Field.GetScalarValue(VoxelPreviewField.Food, flatIndex));
                if (food > 0)
                {
                    if (InitialFood == null) InitialFood = new float[voxelCount];
                    InitialFood[flatIndex] = food;
                }
                float antFood = PositiveValueOrZero(Field.GetScalarValue(VoxelPreviewField.AntFood, flatIndex));
                if (antFood > 0)
                {
                    if (InitialAntFood == null) InitialAntFood = new float[voxelCount];
                    InitialAntFood[flatIndex] = antFood;
                }
                if (hasFlags && Field.IsSolverWalkableFlatIndex(flatIndex))
                {
                    SetFlag(VoxelFlags, flatIndex);
                }
            }
        }

        static int ReserveChannel(ref int elementCount, int channelLength, bool enabled)
        {
            if (!enabled) return -1;
            int offset = elementCount;
            elementCount = checked(elementCount + channelLength);
            return offset;
        }

        static void SetPackedValue(float[] values, int channelOffset, int flatIndex, float value)
        {
            if (values != null && channelOffset >= 0) values[channelOffset + flatIndex] = value;
        }

        static void SetFlag(uint[] words, int flatIndex)
        {
            if (words != null && flatIndex >= 0) words[flatIndex >> 5] |= 1u << (flatIndex & 31);
        }

        static bool HasDenseMap(VoxelScalarMap map)
        {
            return map != null && map.Values != null;
        }

        void CaptureVoxels(Voxel[,,] inputVoxels)
        {
            if (inputVoxels == null)
            {
                Voxels = new Voxel[0, 0, 0];
                VoxelDensity = ProcessDensity ? new float[0] : null;
                AntFoodPheromone = HasAntParticles ? new float[0] : null;
                AntBasePheromone = HasAntParticles ? new float[0] : null;
                VoxelBehaviorData = new float[0];
                SpeedOffset = SensorDistanceOffset = SensorAngleOffset = RotationAngleOffset = -1;
                SpeedDefault = SensorDistanceDefault = SensorAngleDefault = RotationAngleDefault = 1;
                VoxelVectorData = new float[0];
                VoxelVectorDefaultX = VoxelVectorDefaultY = VoxelVectorDefaultZ = 0;
                VoxelVectorFrequencies = null;
                VoxelVectorDefaultFrequency = 3;
                VoxelDensityLimits = new float[0];
                MinimumDensityOffset = MaximumDensityOffset = -1;
                MinimumDensityDefault = MaximumDensityDefault = -1;
                InitialFood = new float[0];
                InitialAntFood = new float[0];
                StaticVoxelFields = null;
                StaticVoxelFieldMaximums = null;
                VoxelVectorsXyz = new float[0];
                ActiveVoxelFlags = null;
                VoxelFlags = new uint[0];
                VoxelSize = 1;
                return;
            }

            ResX = inputVoxels.GetLength(0);
            ResY = inputVoxels.GetLength(1);
            ResZ = inputVoxels.GetLength(2);

            int voxelCount = ResX * ResY * ResZ;
            double voxelSize = ResolveVoxelSize(inputVoxels);
            VoxelSize = (float)voxelSize;
            bool hasInputVoxels = ContainsAnyVoxel(inputVoxels);

            Voxels = new Voxel[ResX, ResY, ResZ];
            VoxelDensity = ProcessDensity ? new float[voxelCount] : null;
            AntFoodPheromone = HasAntParticles ? new float[voxelCount] : null;
            AntBasePheromone = HasAntParticles ? new float[voxelCount] : null;
            SpeedOffset = 0;
            SensorDistanceOffset = voxelCount;
            SensorAngleOffset = voxelCount * 2;
            RotationAngleOffset = voxelCount * 3;
            SpeedDefault = SensorDistanceDefault = SensorAngleDefault = RotationAngleDefault = 1;
            VoxelBehaviorData = new float[voxelCount * 4];
            VoxelVectorData = new float[checked(voxelCount * 3)];
            VoxelVectorDefaultX = VoxelVectorDefaultY = VoxelVectorDefaultZ = 0;
            VoxelVectorFrequencies = new int[voxelCount];
            VoxelVectorDefaultFrequency = 3;
            MinimumDensityOffset = 0;
            MaximumDensityOffset = voxelCount;
            MinimumDensityDefault = MaximumDensityDefault = -1;
            VoxelDensityLimits = new float[voxelCount * 2];
            InitialFood = null;
            InitialAntFood = null;
            StaticVoxelFields = null;
            StaticVoxelFieldMaximums = null;
            VoxelVectorsXyz = new float[voxelCount * 3];
            ActiveVoxelFlags = hasInputVoxels ? new uint[(voxelCount + 31) >> 5] : null;
            VoxelFlags = new uint[(voxelCount + 31) >> 5];

            for (int x = 0; x < ResX; x++)
            {
                for (int y = 0; y < ResY; y++)
                {
                    for (int z = 0; z < ResZ; z++)
                    {
                        Voxel source = inputVoxels[x, y, z];
                        if (source == null && hasInputVoxels)
                        {
                            continue;
                        }

                        int flatIndex = FlatIndex(x, y, z);
                        Voxel voxel = source == null ? new Voxel(voxelSize, x, y, z) : CopyVoxel(source, voxelSize, x, y, z);
                        voxel.flatIndex = flatIndex;
                        Voxels[x, y, z] = voxel;
                        SetFlag(ActiveVoxelFlags, flatIndex);

                        if (ProcessDensity)
                        {
                            VoxelDensity[flatIndex] = (float)voxel.density;
                        }
                        if (HasAntParticles)
                        {
                            AntFoodPheromone[flatIndex] = (float)Math.Max(0, voxel.towardsFoodPheromone);
                            AntBasePheromone[flatIndex] = (float)Math.Max(0, voxel.towardsBasePheromone);
                        }
                        CaptureVoxelBehaviorFields(voxel, flatIndex);
                        VoxelVectorsXyz[flatIndex * 3] = (float)voxel.voxelVector.X;
                        VoxelVectorsXyz[flatIndex * 3 + 1] = (float)voxel.voxelVector.Y;
                        VoxelVectorsXyz[flatIndex * 3 + 2] = (float)voxel.voxelVector.Z;
                        if (!VoxelOccupancy.IsBlockedMaxDensity(voxel.maxDensity))
                        {
                            SetFlag(VoxelFlags, flatIndex);
                        }

                        ActiveVoxelCount++;
                    }
                }
            }

            if (ActiveVoxelCount == voxelCount)
            {
                ActiveVoxelFlags = null;
            }
        }

        void CaptureVoxelBehaviorFields(Voxel voxel, int flatIndex)
        {
            SetPackedValue(VoxelBehaviorData, SpeedOffset, flatIndex, MultiplierOrDefault(voxel.speedMultiplier));
            SetPackedValue(VoxelBehaviorData, SensorDistanceOffset, flatIndex, MultiplierOrDefault(voxel.sensorDistanceMultiplier));
            SetPackedValue(VoxelBehaviorData, SensorAngleOffset, flatIndex, MultiplierOrDefault(voxel.sensorAngleMultiplier));
            SetPackedValue(VoxelBehaviorData, RotationAngleOffset, flatIndex, MultiplierOrDefault(voxel.rotationAngleMultiplier));

            int offset = flatIndex * 3;
            Vector3d vector = voxel.vectorField && voxel.voxelVector.Length > 0
                ? voxel.voxelVector
                : Vector3d.Zero;
            VoxelVectorData[offset] = (float)vector.X;
            VoxelVectorData[offset + 1] = (float)vector.Y;
            VoxelVectorData[offset + 2] = (float)vector.Z;
            VoxelVectorFrequencies[flatIndex] = Math.Max(1, voxel.frequency);

            SetPackedValue(VoxelDensityLimits, MinimumDensityOffset, flatIndex, DensityLimitOrUnset(voxel.minDensity));
            SetPackedValue(VoxelDensityLimits, MaximumDensityOffset, flatIndex, DensityLimitOrUnset(voxel.maxDensity));
            float food = PositiveValueOrZero(voxel.food);
            if (food > 0)
            {
                if (InitialFood == null) InitialFood = new float[ResX * ResY * ResZ];
                InitialFood[flatIndex] = food;
            }
            float antFood = PositiveValueOrZero(voxel.antFood);
            if (antFood > 0)
            {
                if (InitialAntFood == null) InitialAntFood = new float[ResX * ResY * ResZ];
                InitialAntFood[flatIndex] = antFood;
            }
        }

        static float MultiplierOrDefault(double value)
        {
            if (double.IsNaN(value) || double.IsInfinity(value) || value < 0) return 1.0f;
            return (float)value;
        }

        static float DensityLimitOrUnset(double value)
        {
            if (double.IsNaN(value) || double.IsInfinity(value) || value < 0) return -1.0f;
            return (float)value;
        }

        static float PositiveValueOrZero(double value)
        {
            if (double.IsNaN(value) || double.IsInfinity(value) || value <= 0) return 0.0f;
            return (float)value;
        }

        static float[][] CreateStaticVoxelFieldArrays(int voxelCount)
        {
            float[][] fields = new float[VoxelPreviewField.StaticFieldCount][];
            for (int i = 0; i < fields.Length; i++)
            {
                fields[i] = new float[voxelCount];
            }

            return fields;
        }

        void CaptureStaticVoxelFields(Voxel voxel, int flatIndex)
        {
            if (StaticVoxelFields == null || voxel == null) return;

            SetStaticVoxelField(VoxelPreviewField.MinimumDensity, flatIndex, PreviewValue(voxel.minDensity, false));
            SetStaticVoxelField(VoxelPreviewField.MaximumDensity, flatIndex, PreviewValue(voxel.maxDensity, true));
            SetStaticVoxelField(VoxelPreviewField.Speed, flatIndex, PreviewValue(voxel.speedMultiplier, false));
            SetStaticVoxelField(VoxelPreviewField.SensorDistance, flatIndex, PreviewValue(voxel.sensorDistanceMultiplier, false));
            SetStaticVoxelField(VoxelPreviewField.SensorAngle, flatIndex, PreviewValue(voxel.sensorAngleMultiplier, false));
            SetStaticVoxelField(VoxelPreviewField.RotationAngle, flatIndex, PreviewValue(voxel.rotationAngleMultiplier, false));
            SetStaticVoxelField(VoxelPreviewField.Food, flatIndex, PreviewValue(voxel.food, false));
        }

        void SetStaticVoxelField(int fieldIndex, int flatIndex, float value)
        {
            if (fieldIndex < 0 || fieldIndex >= VoxelPreviewField.StaticFieldCount) return;

            StaticVoxelFields[fieldIndex][flatIndex] = value;
            if (StaticVoxelFieldMaximums != null && value > StaticVoxelFieldMaximums[fieldIndex])
            {
                StaticVoxelFieldMaximums[fieldIndex] = value;
            }
        }

        static float PreviewValue(double value, bool keepZero)
        {
            if (double.IsNaN(value) || double.IsInfinity(value)) return 0;
            if (value < 0) return 0;
            if (!keepZero && value <= 0.01) return 0;
            return (float)value;
        }

        void CaptureParticles(IList<ParticleGroup> particleGroups)
        {
            Particles = new ParticleList();
            CaptureParticleGroups(particleGroups);

            List<int> capturedGroupIndices = new List<int>();
            List<int> capturedParentIndices = new List<int>();
            Dictionary<int, Voxel> capturedParentVoxels = new Dictionary<int, Voxel>();
            HashSet<int> capturedParentIndicesSet = new HashSet<int>();
            bool capturedAntParticles = false;
            bool capturedSlimeParticles = false;

            if (particleGroups != null)
            {
                for (int groupIndex = 0; groupIndex < particleGroups.Count; groupIndex++)
                {
                    ParticleGroup group = particleGroups[groupIndex];
                    if (group == null || group.particles == null)
                    {
                        continue;
                    }

                    for (int i = 0; i < group.particles.Count; i++)
                    {
                        Particle particle = group.particles[i];
                        if (particle == null)
                        {
                            continue;
                        }

                        Plane preparedPlane;
                        int parentFlatIndex;
                        if (!TryPrepareResetParticle(particle, group, out preparedPlane, out parentFlatIndex))
                        {
                            continue;
                        }

                        // Match the GPU owner's lowest-slot winner before any reset
                        // output is published. Input order is stable across groups and
                        // particles, so the first particle for a voxel is retained.
                        if (!capturedParentIndicesSet.Add(parentFlatIndex))
                        {
                            continue;
                        }

                        ParticleGroup simulationGroup = groupIndex < ParticleGroups.Length
                            ? ParticleGroups[groupIndex]
                            : null;
                        bool particleIsAnt = particle.parentParticleGroup != null
                            ? particle.parentParticleGroup.ant
                            : group.ant;
                        if (particleIsAnt) capturedAntParticles = true;
                        else capturedSlimeParticles = true;
                        if (simulationGroup != null && particleIsAnt)
                        {
                            // V3 promotes the retained simulation group to ant only
                            // after at least one eligible ant particle survives reset.
                            simulationGroup.ant = true;
                        }
                        Particle simulationParticle = new Particle(preparedPlane);
                        simulationParticle.parentParticleGroup = simulationGroup;
                        simulationParticle.parentVoxel = CapturedParentVoxel(parentFlatIndex, capturedParentVoxels);
                        if (particleIsAnt)
                        {
                            simulationParticle.home = preparedPlane;
                        }
                        // V3 reset constructs a fresh particle. Runtime ant state and
                        // age are observable on readback, but are not continuation
                        // state when a solver reset consumes those particles again.
                        ResetCapturedParticleRuntimeState(simulationParticle);
                        Particles.Add(simulationParticle);
                        if (simulationGroup != null)
                        {
                            simulationGroup.particles.Add(simulationParticle);
                        }
                        capturedGroupIndices.Add(groupIndex);
                        capturedParentIndices.Add(parentFlatIndex);
                    }
                }
            }

            // V3 derives the solver frequencies from the retained population, not
            // from the raw input-group count. Pack those values before the CPU-visible
            // group metadata is converted from its raw controls to derived values.
            bool ignoredHasAntParticles;
            bool ignoredHasSlimeParticles;
            CaptureRuntimeGroupSettings(
                particleGroups,
                ParticleGroups,
                out GroupData0,
                out GroupData1,
                out ignoredHasAntParticles,
                out ignoredHasSlimeParticles);
            HasAntParticles = capturedAntParticles;
            HasSlimeParticles = capturedSlimeParticles;
            ApplyCapturedParticleGroupMetadata();

            ParticleCount = Particles.Count;
            ParticlePositionsXyz = new float[ParticleCount * 3];
            ParticleDirectionsXyz = new float[ParticleCount * 3];
            ParticleYAxesXyz = new float[ParticleCount * 3];
            ParticleHomesXyz = new float[ParticleCount * 3];
            ParticleAntStates = new uint[ParticleCount];
            ParticleAntLaunchBoundaryStates = new uint[ParticleCount];
            ParticleAges = new int[ParticleCount];
            ParticleGroupIndices = new int[ParticleCount];
            ParticleParentIndices = new int[ParticleCount];

            for (int particleIndex = 0; particleIndex < ParticleCount; particleIndex++)
            {
                Particle particle = Particles[particleIndex];
                Plane plane = particle.pPlane;
                Point3d origin = plane.Origin;
                Vector3d xAxis = plane.XAxis;
                Vector3d yAxis = plane.YAxis;

                ParticlePositionsXyz[particleIndex * 3] = (float)origin.X;
                ParticlePositionsXyz[particleIndex * 3 + 1] = (float)origin.Y;
                ParticlePositionsXyz[particleIndex * 3 + 2] = (float)origin.Z;
                ParticleDirectionsXyz[particleIndex * 3] = (float)xAxis.X;
                ParticleDirectionsXyz[particleIndex * 3 + 1] = (float)xAxis.Y;
                ParticleDirectionsXyz[particleIndex * 3 + 2] = (float)xAxis.Z;
                ParticleYAxesXyz[particleIndex * 3] = (float)yAxis.X;
                ParticleYAxesXyz[particleIndex * 3 + 1] = (float)yAxis.Y;
                ParticleYAxesXyz[particleIndex * 3 + 2] = (float)yAxis.Z;
                ParticleGroupIndices[particleIndex] = capturedGroupIndices[particleIndex];
                ParticleParentIndices[particleIndex] = capturedParentIndices[particleIndex];
                Point3d home = particle.home.Origin;
                ParticleHomesXyz[particleIndex * 3] = (float)home.X;
                ParticleHomesXyz[particleIndex * 3 + 1] = (float)home.Y;
                ParticleHomesXyz[particleIndex * 3 + 2] = (float)home.Z;
                ResetPackedParticleRuntimeState(
                    particleIndex,
                    ParticleAges,
                    ParticleAntStates,
                    ParticleAntLaunchBoundaryStates);
            }
        }

        static void ResetCapturedParticleRuntimeState(Particle particle)
        {
            particle.foundFood = false;
            particle.antLaunchBoundaryHit = false;
            // inheritParticleGroups is immediately followed by
            // particleCheckParentVoxel on a V3 reset, which advances age once.
            particle.age = 1;
        }

        static void ResetPackedParticleRuntimeState(
            int particleIndex,
            int[] ages,
            uint[] antStates,
            uint[] antLaunchBoundaryStates)
        {
            ages[particleIndex] = 0;
            antStates[particleIndex] = 0u;
            antLaunchBoundaryStates[particleIndex] = 0u;
        }

        void CaptureParticleGroups(IList<ParticleGroup> particleGroups)
        {
            CaptureGroupSettings(particleGroups, out GroupData0, out GroupData1, out HasAntParticles, out HasSlimeParticles);
            GroupColorData = CaptureGroupColors(particleGroups);
            GroupCount = particleGroups != null ? particleGroups.Count : 0;
            ParticleGroups = new ParticleGroup[GroupCount];
            for (int i = 0; i < GroupCount; i++)
            {
                ParticleGroup source = particleGroups[i];
                if (source == null)
                {
                    ParticleGroups[i] = new ParticleGroup();
                    continue;
                }

                ParticleGroup simulationGroup = new ParticleGroup(
                    source.speed,
                    source.sensorDistance,
                    source.sensorAngle,
                    source.rotationAngle,
                    source.depositValue,
                    source.wanderFrequency,
                    source.baseWanderFrequency,
                    source.color)
                {
                    // V3 leaves a new retained group non-ant until an eligible ant
                    // particle is actually copied into it.
                    ant = false,
                    connectedSteering = source.connectedSteering
                };

                if (simulationGroup.connectedSteering && !simulationGroup.ant)
                {
                    simulationGroup.clampConnectedExploration();
                }

                ParticleGroups[i] = simulationGroup;
            }
        }

        void ApplyCapturedParticleGroupMetadata()
        {
            if (ParticleGroups == null)
            {
                return;
            }

            for (int i = 0; i < ParticleGroups.Length; i++)
            {
                ApplyV3ParticleGroupMetadata(ParticleGroups[i]);
            }
        }

        internal static void ApplyV3ParticleGroupMetadata(ParticleGroup group)
        {
            int population = group != null && group.particles != null
                ? group.particles.Count
                : 0;
            ApplyV3ParticleGroupMetadata(group, population);
        }

        internal static void ApplyV3ParticleGroupMetadata(ParticleGroup group, int population)
        {
            if (group == null)
            {
                return;
            }

            population = Math.Max(0, population);
            if (group.ant)
            {
                group.baseWanderFrequency = ComputeAntBaseWanderFrequency(
                    group.baseWanderFrequency,
                    population);
                return;
            }

            if (group.connectedSteering) group.clampConnectedExploration();
            else group.wanderFrequency = ComputeSlimeWanderFrequency(
                group.wanderFrequency,
                population);
        }

        public static float[] CaptureGroupColors(IList<ParticleGroup> particleGroups)
        {
            int groupCount = particleGroups != null ? particleGroups.Count : 0;
            float[] groupColors = new float[groupCount * 4];

            if (particleGroups == null)
            {
                return groupColors;
            }

            for (int groupIndex = 0; groupIndex < particleGroups.Count; groupIndex++)
            {
                ParticleGroup group = particleGroups[groupIndex];
                int offset = groupIndex * 4;
                System.Drawing.Color color = group != null ? group.color : System.Drawing.Color.White;
                int alpha = color.A == 0 ? 255 : color.A;
                groupColors[offset] = color.R / 255.0f;
                groupColors[offset + 1] = color.G / 255.0f;
                groupColors[offset + 2] = color.B / 255.0f;
                groupColors[offset + 3] = alpha / 255.0f;
            }

            return groupColors;
        }

        public static void CaptureGroupSettings(
            IList<ParticleGroup> particleGroups,
            out float[] groupData0,
            out float[] groupData1,
            out bool hasAntParticles,
            out bool hasSlimeParticles)
        {
            CaptureRuntimeGroupSettings(
                particleGroups,
                particleGroups,
                out groupData0,
                out groupData1,
                out hasAntParticles,
                out hasSlimeParticles);
        }

        internal static void CaptureRuntimeGroupSettings(
            IList<ParticleGroup> sourceGroups,
            IList<ParticleGroup> runtimeGroups,
            out float[] groupData0,
            out float[] groupData1,
            out bool hasAntParticles,
            out bool hasSlimeParticles)
        {
            CaptureRuntimeGroupSettings(
                sourceGroups,
                runtimeGroups,
                null,
                out groupData0,
                out groupData1,
                out hasAntParticles,
                out hasSlimeParticles);
        }

        internal static void CaptureRuntimeGroupSettings(
            IList<ParticleGroup> sourceGroups,
            IList<ParticleGroup> runtimeGroups,
            int[] runtimePopulationCounts,
            out float[] groupData0,
            out float[] groupData1,
            out bool hasAntParticles,
            out bool hasSlimeParticles)
        {
            int groupCount = sourceGroups != null ? sourceGroups.Count : 0;
            groupData0 = new float[groupCount * 4];
            groupData1 = new float[groupCount * 4];
            hasAntParticles = false;
            hasSlimeParticles = false;

            if (sourceGroups == null)
            {
                return;
            }

            for (int groupIndex = 0; groupIndex < sourceGroups.Count; groupIndex++)
            {
                ParticleGroup source = sourceGroups[groupIndex];
                if (source == null)
                {
                    continue;
                }

                ParticleGroup runtime = runtimeGroups != null && groupIndex < runtimeGroups.Count
                    ? runtimeGroups[groupIndex]
                    : source;
                bool isAnt = runtime != null ? runtime.ant : source.ant;

                if (isAnt)
                {
                    hasAntParticles = true;
                }
                else
                {
                    hasSlimeParticles = true;
                }

                double sensorAngle = Math.PI * source.sensorAngle / 180.0;
                double rotationAngle = Math.PI * source.rotationAngle / 180.0;
                int particleCount = runtimePopulationCounts != null && groupIndex < runtimePopulationCounts.Length
                    ? Math.Max(0, runtimePopulationCounts[groupIndex])
                    : runtime != null && runtime.particles != null
                        ? runtime.particles.Count
                        : 0;
                bool connectedSteering = source.connectedSteering && !isAnt;
                double exploration = connectedSteering
                    ? NormalizeConnectedExploration(source.wanderFrequency)
                    : source.wanderFrequency;
                float wanderFrequency = isAnt
                    ? ComputeAntBaseWanderFrequency(source.baseWanderFrequency, particleCount)
                    : ComputeSlimeWanderFrequency(exploration, particleCount);
                // Dynamic populations recompute the derived interval from the GPU's
                // exact per-group count. Slime uses exploration; ants use their raw
                // base-wander control in the otherwise-unused fourth channel.
                double populationControl = isAnt
                    ? NormalizeAntBaseWander(source.baseWanderFrequency)
                    : exploration;

                int offset = groupIndex * 4;
                groupData0[offset] = (float)source.speed;
                groupData0[offset + 1] = (float)source.sensorDistance;
                groupData0[offset + 2] = (float)sensorAngle;
                groupData0[offset + 3] = (float)populationControl;

                groupData1[offset] = (float)rotationAngle;
                groupData1[offset + 1] = isAnt ? 1 : (connectedSteering ? -1 : 0);
                groupData1[offset + 2] = (float)source.depositValue;
                groupData1[offset + 3] = wanderFrequency;
            }
        }

        void DetectPopulationKinds(IList<ParticleGroup> particleGroups)
        {
            HasAntParticles = false;
            HasSlimeParticles = false;
            if (particleGroups == null)
            {
                return;
            }

            for (int i = 0; i < particleGroups.Count; i++)
            {
                ParticleGroup group = particleGroups[i];
                if (group == null)
                {
                    continue;
                }

                if (group.ant) HasAntParticles = true;
                else HasSlimeParticles = true;
            }
        }

        int FlatIndex(int x, int y, int z)
        {
            return x * ResY * ResZ + y * ResZ + z;
        }

        bool TryPrepareResetParticle(
            Particle particle,
            ParticleGroup group,
            out Plane preparedPlane,
            out int parentFlatIndex)
        {
            preparedPlane = new Plane();
            parentFlatIndex = -1;
            if (particle == null || group == null)
            {
                return false;
            }

            // V3 decides inclusion from the original input position. Boundary
            // wrapping/reflection happens only after this parent is retained.
            int originalParentFlatIndex = FlatIndexFromPosition(particle.pPlane.Origin);
            if (originalParentFlatIndex < 0)
            {
                return false;
            }

            double resetRotationAngle = particle.parentParticleGroup != null
                ? particle.parentParticleGroup.rotationAngle
                : group.rotationAngle;
            preparedPlane = PrepareResetPlane(particle.pPlane, group, resetRotationAngle);

            // The prepared position is the position uploaded to the GPU, so resolve
            // its effective parent after boundary handling. This keeps the CPU reset
            // list, packed parent indices, and GPU occupancy claims coherent.
            parentFlatIndex = FlatIndexFromPosition(preparedPlane.Origin);
            return parentFlatIndex >= 0;
        }

        Plane PrepareResetPlane(Plane inputPlane, ParticleGroup group)
        {
            return PrepareResetPlane(inputPlane, group, group != null ? group.rotationAngle : 0);
        }

        Plane PrepareResetPlane(Plane inputPlane, ParticleGroup group, double rotationAngle)
        {
            bool tridimensional = ResX > 1 && ResY > 1 && ResZ > 1;
            // V3 assigns these with three independent checks in X/Y/Z order,
            // so Z has final priority for degenerate line/point grids.
            bool planarXY = !tridimensional && ResZ == 1;
            bool planarXZ = !tridimensional && !planarXY && ResY == 1;
            bool planarYZ = !tridimensional && !planarXY && !planarXZ && ResX == 1;

            Plane plane = inputPlane;
            if (!tridimensional)
            {
                Point3d origin = inputPlane.Origin;
                Vector3d xAxis;
                Vector3d yAxis;
                if (planarXY)
                {
                    xAxis = new Vector3d(inputPlane.XAxis.X, inputPlane.XAxis.Y, 0);
                    xAxis.Unitize();
                    yAxis = new Vector3d(xAxis);
                    yAxis.Rotate(Math.PI / 2, Plane.WorldXY.ZAxis);
                    plane = new Plane(origin, xAxis, yAxis);
                }
                else if (planarXZ)
                {
                    xAxis = new Vector3d(inputPlane.XAxis.X, 0, inputPlane.XAxis.Z);
                    xAxis.Unitize();
                    yAxis = new Vector3d(xAxis);
                    yAxis.Rotate(Math.PI / 2, Plane.WorldXY.YAxis);
                    plane = new Plane(origin, xAxis, yAxis);
                }
                else if (planarYZ)
                {
                    xAxis = new Vector3d(0, inputPlane.XAxis.Y, inputPlane.XAxis.Z);
                    xAxis.Unitize();
                    yAxis = new Vector3d(xAxis);
                    yAxis.Rotate(Math.PI / 2, Plane.WorldXY.XAxis);
                    plane = new Plane(origin, xAxis, yAxis);
                }
            }

            double dimX = ResX * (double)VoxelSize;
            double dimY = ResY * (double)VoxelSize;
            double dimZ = ResZ * (double)VoxelSize;
            if (tridimensional
                && dimX > group.sensorDistance
                && dimY > group.sensorDistance
                && dimZ > group.sensorDistance)
            {
                plane.Rotate(
                    Math.PI * rotationAngle / 180.0,
                    plane.YAxis,
                    plane.Origin);
            }

            Point3d boundedOrigin = ApplyResetBoundaries(
                ref plane,
                plane.Origin,
                tridimensional,
                planarXY,
                planarXZ,
                planarYZ,
                dimX,
                dimY,
                dimZ);
            return new Plane(boundedOrigin, plane.XAxis, plane.YAxis);
        }

        Point3d ApplyResetBoundaries(
            ref Plane plane,
            Point3d point,
            bool tridimensional,
            bool planarXY,
            bool planarXZ,
            bool planarYZ,
            double dimX,
            double dimY,
            double dimZ)
        {
            Point3d next = point;
            if (!wrapBoundaries)
            {
                double boundaryDistance = VoxelSize;
                if ((planarYZ || (next.X > boundaryDistance && next.X < dimX - boundaryDistance))
                    && (planarXZ || (next.Y > boundaryDistance && next.Y < dimY - boundaryDistance))
                    && (planarXY || (next.Z > boundaryDistance && next.Z < dimZ - boundaryDistance)))
                {
                    return next;
                }

                if (!planarYZ)
                {
                    if (next.X <= boundaryDistance)
                    {
                        next.X = boundaryDistance;
                        ReflectResetPlane(ref plane, 0);
                    }
                    if (next.X >= dimX - boundaryDistance)
                    {
                        next.X = dimX - boundaryDistance;
                        ReflectResetPlane(ref plane, 0);
                    }
                }

                if (!planarXZ)
                {
                    if (next.Y <= boundaryDistance)
                    {
                        next.Y = boundaryDistance;
                        ReflectResetPlane(ref plane, 1);
                    }
                    if (next.Y >= dimY - boundaryDistance)
                    {
                        next.Y = dimY - boundaryDistance;
                        ReflectResetPlane(ref plane, 1);
                    }
                }

                if (!planarXY)
                {
                    if (next.Z <= boundaryDistance)
                    {
                        next.Z = boundaryDistance;
                        if (tridimensional) ReflectResetPlane(ref plane, 2);
                    }
                    if (next.Z >= dimZ - boundaryDistance)
                    {
                        next.Z = dimZ - boundaryDistance;
                        if (tridimensional) ReflectResetPlane(ref plane, 2);
                    }
                }

                return next;
            }

            const double wrapDistance = 0.01;
            if ((planarYZ || (next.X >= wrapDistance && next.X <= dimX - wrapDistance))
                && (planarXZ || (next.Y >= wrapDistance && next.Y <= dimY - wrapDistance))
                && (planarXY || (next.Z >= wrapDistance && next.Z <= dimZ - wrapDistance)))
            {
                return next;
            }

            if (!planarYZ)
            {
                if (next.X < wrapDistance) next.X = dimX - 0.1;
                if (next.X > dimX - wrapDistance) next.X = 0.1;
            }
            if (!planarXZ)
            {
                if (next.Y < wrapDistance) next.Y = dimY - 0.1;
                if (next.Y > dimY - wrapDistance) next.Y = 0.1;
            }
            if (!planarXY)
            {
                if (next.Z < wrapDistance) next.Z = dimZ - 0.1;
                if (next.Z > dimZ - wrapDistance) next.Z = 0.1;
            }
            return next;
        }

        static void ReflectResetPlane(ref Plane plane, int axis)
        {
            Vector3d direction = plane.XAxis;
            direction.Unitize();
            if (axis == 0) direction.X = -direction.X;
            else if (axis == 1) direction.Y = -direction.Y;
            else direction.Z = -direction.Z;

            Vector3d yAxis = new Vector3d(direction);
            yAxis.Rotate(Math.PI / 2, plane.ZAxis);
            plane = new Plane(plane.Origin, direction, yAxis);
        }

        Voxel CapturedParentVoxel(int flatIndex, Dictionary<int, Voxel> cache)
        {
            Voxel voxel = null;
            if (cache.TryGetValue(flatIndex, out voxel))
            {
                return voxel;
            }

            if (Field != null)
            {
                voxel = Field.CreateVoxel(flatIndex);
            }
            else if (Voxels != null)
            {
                int x = flatIndex / (ResY * ResZ);
                int remainder = flatIndex - x * ResY * ResZ;
                int y = remainder / ResZ;
                int z = remainder - y * ResZ;
                voxel = Voxels[x, y, z];
            }

            cache[flatIndex] = voxel;
            return voxel;
        }

        int FlatIndexFromPosition(Point3d point)
        {
            if (VoxelSize <= 0 || ResX <= 0 || ResY <= 0 || ResZ <= 0)
            {
                return -1;
            }

            int x = (int)(point.X / VoxelSize);
            int y = (int)(point.Y / VoxelSize);
            int z = (int)(point.Z / VoxelSize);

            if (x < 0 || x >= ResX || y < 0 || y >= ResY || z < 0 || z >= ResZ)
            {
                return -1;
            }

            int flatIndex = FlatIndex(x, y, z);
            return IsWalkableFlatIndex(flatIndex) ? flatIndex : -1;
        }

        bool IsWalkableFlatIndex(int flatIndex)
        {
            if (Field != null)
            {
                return Field.IsSolverWalkableFlatIndex(flatIndex);
            }

            return VoxelFlags != null &&
                   flatIndex >= 0 &&
                   flatIndex < ResX * ResY * ResZ &&
                   (VoxelFlags[flatIndex >> 5] & (1u << (flatIndex & 31))) != 0;
        }

        static float ComputeSlimeWanderFrequency(double wander, int particleCount)
        {
            if (wander <= 0) return 0;
            if (wander > 1) wander = 1;

            wander = 1 - wander;
            double frequency = Math.Floor(Math.Pow(wander, 3) * particleCount / 10.0);
            if (frequency < 1) frequency = 1;
            return (float)frequency;
        }

        static double NormalizeConnectedExploration(double exploration)
        {
            if (double.IsNaN(exploration) || exploration < 0) return 0;
            if (double.IsPositiveInfinity(exploration) || exploration > 1) return 1;
            return exploration;
        }

        static double NormalizeAntBaseWander(double wander)
        {
            if (double.IsNaN(wander) || wander < 0) return 0;
            if (double.IsPositiveInfinity(wander) || wander > 1) return 1;
            return wander;
        }

        static float ComputeAntBaseWanderFrequency(double wander, int particleCount)
        {
            wander = NormalizeAntBaseWander(wander);

            double frequency = Math.Floor(wander * particleCount / 40.0);
            if (frequency < 1) frequency = 1;
            return (float)frequency;
        }

        static bool ContainsAnyVoxel(Voxel[,,] inputVoxels)
        {
            int resX = inputVoxels.GetLength(0);
            int resY = inputVoxels.GetLength(1);
            int resZ = inputVoxels.GetLength(2);

            for (int x = 0; x < resX; x++)
            {
                for (int y = 0; y < resY; y++)
                {
                    for (int z = 0; z < resZ; z++)
                    {
                        if (inputVoxels[x, y, z] != null)
                        {
                            return true;
                        }
                    }
                }
            }

            return false;
        }

        static double ResolveVoxelSize(Voxel[,,] inputVoxels)
        {
            int resX = inputVoxels.GetLength(0);
            int resY = inputVoxels.GetLength(1);
            int resZ = inputVoxels.GetLength(2);

            for (int x = 0; x < resX; x++)
            {
                for (int y = 0; y < resY; y++)
                {
                    for (int z = 0; z < resZ; z++)
                    {
                        Voxel voxel = inputVoxels[x, y, z];
                        if (voxel != null && voxel.voxelSize > 0)
                        {
                            return voxel.voxelSize;
                        }
                    }
                }
            }

            return Globals.voxelSize > 0 ? Globals.voxelSize : 1.0;
        }

        static Voxel CopyVoxel(Voxel source, double voxelSize, int x, int y, int z)
        {
            Voxel voxel = new Voxel(voxelSize, x, y, z);

            voxel.minDensity = source.minDensity;
            voxel.maxDensity = source.maxDensity;
            voxel.inputMinDensity = source.inputMinDensity;
            voxel.inputMaxDensity = source.inputMaxDensity;
            voxel.density = source.density;
            voxel.towardsFoodPheromone = source.towardsFoodPheromone;
            voxel.towardsBasePheromone = source.towardsBasePheromone;
            voxel.speedMultiplier = source.speedMultiplier;
            voxel.sensorAngleMultiplier = source.sensorAngleMultiplier;
            voxel.sensorDistanceMultiplier = source.sensorDistanceMultiplier;
            voxel.rotationAngleMultiplier = source.rotationAngleMultiplier;
            voxel.food = source.food;
            voxel.voxelVector = source.voxelVector;
            voxel.frequency = source.frequency;
            voxel.vectorField = source.vectorField;
            voxel.particleCount = source.particleCount;
            voxel.boundary = source.boundary;

            return voxel;
        }
    }
}
