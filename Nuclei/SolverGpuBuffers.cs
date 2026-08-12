using System;
using System.Collections.Generic;

using Rhino.Geometry;

namespace Nuclei3
{
    internal sealed class SolverGpuInputSnapshot
    {
        public Voxel[,,] Voxels;
        public ParticleList Particles;
        public ParticleGroup[] ParticleGroups;
        public int ResX;
        public int ResY;
        public int ResZ;
        public int ActiveVoxelCount;
        public int ParticleCount;
        public float VoxelSize;
        public float[] ParticlePositionsXyz;
        public float[] ParticleDirectionsXyz;
        public float[] ParticleYAxesXyz;
        public float[] ParticleHomesXyz;
        public uint[] ParticleAntStates;
        public int[] ParticleGroupIndices;
        public int[] ParticleParentIndices;
        public float[] VoxelDensity;
        public float[] AntFoodPheromone;
        public float[] AntBasePheromone;
        public float[] VoxelBehaviorData;
        public float[] VoxelVectorData;
        public float[] VoxelDensityLimits;
        public float[][] StaticVoxelFields;
        public float[] StaticVoxelFieldMaximums;
        public float[] VoxelVectorsXyz;
        public uint[] VoxelFlags;
        public int GroupCount;
        public float[] GroupData0;
        public float[] GroupData1;
        public float[] GroupColorData;
        public bool HasAntParticles;
        public bool HasSlimeParticles;

        public static SolverGpuInputSnapshot Capture(Voxel[,,] inputVoxels, IList<ParticleGroup> particleGroups)
        {
            SolverGpuInputSnapshot snapshot = new SolverGpuInputSnapshot();

            snapshot.DetectPopulationKinds(particleGroups);
            snapshot.CaptureVoxels(inputVoxels);
            snapshot.CaptureParticles(particleGroups);

            return snapshot;
        }

        void CaptureVoxels(Voxel[,,] inputVoxels)
        {
            if (inputVoxels == null)
            {
                Voxels = new Voxel[0, 0, 0];
                VoxelDensity = HasSlimeParticles ? new float[0] : null;
                AntFoodPheromone = HasAntParticles ? new float[0] : null;
                AntBasePheromone = HasAntParticles ? new float[0] : null;
                VoxelBehaviorData = new float[0];
                VoxelVectorData = new float[0];
                VoxelDensityLimits = new float[0];
                StaticVoxelFields = null;
                StaticVoxelFieldMaximums = null;
                VoxelVectorsXyz = new float[0];
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
            VoxelDensity = HasSlimeParticles ? new float[voxelCount] : null;
            AntFoodPheromone = HasAntParticles ? new float[voxelCount] : null;
            AntBasePheromone = HasAntParticles ? new float[voxelCount] : null;
            VoxelBehaviorData = new float[voxelCount * 4];
            VoxelVectorData = new float[voxelCount * 4];
            VoxelDensityLimits = new float[voxelCount * 4];
            StaticVoxelFields = null;
            StaticVoxelFieldMaximums = null;
            VoxelVectorsXyz = new float[voxelCount * 3];
            VoxelFlags = new uint[voxelCount];

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

                        if (HasSlimeParticles)
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
                            VoxelFlags[flatIndex] = 1;
                        }

                        ActiveVoxelCount++;
                    }
                }
            }
        }

        void CaptureVoxelBehaviorFields(Voxel voxel, int flatIndex)
        {
            int offset = flatIndex * 4;

            VoxelBehaviorData[offset] = MultiplierOrDefault(voxel.speedMultiplier);
            VoxelBehaviorData[offset + 1] = MultiplierOrDefault(voxel.sensorDistanceMultiplier);
            VoxelBehaviorData[offset + 2] = MultiplierOrDefault(voxel.sensorAngleMultiplier);
            VoxelBehaviorData[offset + 3] = MultiplierOrDefault(voxel.rotationAngleMultiplier);

            Vector3d vector = voxel.vectorField && voxel.voxelVector.Length > 0
                ? voxel.voxelVector
                : Vector3d.Zero;
            VoxelVectorData[offset] = (float)vector.X;
            VoxelVectorData[offset + 1] = (float)vector.Y;
            VoxelVectorData[offset + 2] = (float)vector.Z;
            VoxelVectorData[offset + 3] = vector.Length > 0 ? Math.Max(1, voxel.frequency) : 0;

            VoxelDensityLimits[offset] = DensityLimitOrUnset(voxel.minDensity);
            VoxelDensityLimits[offset + 1] = DensityLimitOrUnset(voxel.maxDensity);
            VoxelDensityLimits[offset + 2] = PositiveValueOrZero(voxel.food);
            VoxelDensityLimits[offset + 3] = 0;
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

                        if (FlatIndexFromPosition(particle.pPlane.Origin) < 0)
                        {
                            continue;
                        }

                        ParticleGroup simulationGroup = groupIndex < ParticleGroups.Length
                            ? ParticleGroups[groupIndex]
                            : null;
                        Particle simulationParticle = new Particle(particle.pPlane);
                        simulationParticle.parentParticleGroup = simulationGroup;
                        simulationParticle.home = group.ant ? particle.pPlane : particle.home;
                        simulationParticle.foundFood = group.ant && particle.foundFood;
                        simulationParticle.age = Math.Max(0, particle.age);
                        Particles.Add(simulationParticle);
                        if (simulationGroup != null)
                        {
                            simulationGroup.particles.Add(simulationParticle);
                        }
                    }
                }
            }

            ParticleCount = Particles.Count;
            ParticlePositionsXyz = new float[ParticleCount * 3];
            ParticleDirectionsXyz = new float[ParticleCount * 3];
            ParticleYAxesXyz = new float[ParticleCount * 3];
            ParticleHomesXyz = new float[ParticleCount * 3];
            ParticleAntStates = new uint[ParticleCount];
            ParticleGroupIndices = new int[ParticleCount];
            ParticleParentIndices = new int[ParticleCount];

            int particleIndex = 0;
            if (particleGroups == null)
            {
                return;
            }

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

                    Plane plane = particle.pPlane;
                    Point3d origin = plane.Origin;
                    int parentFlatIndex = FlatIndexFromPosition(origin);
                    if (parentFlatIndex < 0)
                    {
                        continue;
                    }

                    Vector3d xAxis = plane.XAxis;
                    Vector3d yAxis = plane.YAxis;
                    NormalizeParticleAxes(ref xAxis, ref yAxis);

                    ParticlePositionsXyz[particleIndex * 3] = (float)origin.X;
                    ParticlePositionsXyz[particleIndex * 3 + 1] = (float)origin.Y;
                    ParticlePositionsXyz[particleIndex * 3 + 2] = (float)origin.Z;
                    ParticleDirectionsXyz[particleIndex * 3] = (float)xAxis.X;
                    ParticleDirectionsXyz[particleIndex * 3 + 1] = (float)xAxis.Y;
                    ParticleDirectionsXyz[particleIndex * 3 + 2] = (float)xAxis.Z;
                    ParticleYAxesXyz[particleIndex * 3] = (float)yAxis.X;
                    ParticleYAxesXyz[particleIndex * 3 + 1] = (float)yAxis.Y;
                    ParticleYAxesXyz[particleIndex * 3 + 2] = (float)yAxis.Z;
                    ParticleGroupIndices[particleIndex] = groupIndex;
                    ParticleParentIndices[particleIndex] = parentFlatIndex;
                    Point3d home = group.ant ? plane.Origin : particle.home.Origin;
                    ParticleHomesXyz[particleIndex * 3] = (float)home.X;
                    ParticleHomesXyz[particleIndex * 3 + 1] = (float)home.Y;
                    ParticleHomesXyz[particleIndex * 3 + 2] = (float)home.Z;
                    ParticleAntStates[particleIndex] = group.ant && particle.foundFood ? 1u : 0u;

                    particleIndex++;
                }
            }
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

                ParticleGroups[i] = new ParticleGroup(
                    source.speed,
                    source.sensorDistance,
                    source.sensorAngle,
                    source.rotationAngle,
                    source.depositValue,
                    source.wanderFrequency,
                    source.baseWanderFrequency,
                    source.color)
                {
                    ant = source.ant
                };
            }
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
            int groupCount = particleGroups != null ? particleGroups.Count : 0;
            groupData0 = new float[groupCount * 4];
            groupData1 = new float[groupCount * 4];
            hasAntParticles = false;
            hasSlimeParticles = false;

            if (particleGroups == null)
            {
                return;
            }

            for (int groupIndex = 0; groupIndex < particleGroups.Count; groupIndex++)
            {
                ParticleGroup group = particleGroups[groupIndex];
                if (group == null)
                {
                    continue;
                }

                if (group.ant)
                {
                    hasAntParticles = true;
                }
                else
                {
                    hasSlimeParticles = true;
                }

                double sensorAngle = Math.PI * group.sensorAngle / 180.0;
                double rotationAngle = Math.PI * group.rotationAngle / 180.0;
                int particleCount = group.particles != null ? group.particles.Count : 0;
                float wanderFrequency = group.ant
                    ? ComputeAntBaseWanderFrequency(group.baseWanderFrequency, particleCount)
                    : ComputeSlimeWanderFrequency(group.wanderFrequency, particleCount);

                int offset = groupIndex * 4;
                groupData0[offset] = (float)group.speed;
                groupData0[offset + 1] = (float)group.sensorDistance;
                groupData0[offset + 2] = (float)sensorAngle;
                groupData0[offset + 3] = (float)group.wanderFrequency;

                groupData1[offset] = (float)rotationAngle;
                groupData1[offset + 1] = group.ant ? 1 : 0;
                groupData1[offset + 2] = (float)group.depositValue;
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

        void NormalizeParticleAxes(ref Vector3d xAxis, ref Vector3d yAxis)
        {
            if (ResZ == 1)
            {
                xAxis = new Vector3d(xAxis.X, xAxis.Y, 0);
                if (!xAxis.Unitize())
                {
                    xAxis = new Vector3d(1, 0, 0);
                }

                yAxis = new Vector3d(-xAxis.Y, xAxis.X, 0);
                return;
            }

            if (ResY == 1)
            {
                xAxis = new Vector3d(xAxis.X, 0, xAxis.Z);
                if (!xAxis.Unitize())
                {
                    xAxis = new Vector3d(1, 0, 0);
                }

                yAxis = new Vector3d(xAxis.Z, 0, -xAxis.X);
                return;
            }

            if (ResX == 1)
            {
                xAxis = new Vector3d(0, xAxis.Y, xAxis.Z);
                if (!xAxis.Unitize())
                {
                    xAxis = new Vector3d(0, 1, 0);
                }

                yAxis = new Vector3d(0, -xAxis.Z, xAxis.Y);
                return;
            }

            if (!xAxis.Unitize())
            {
                xAxis = new Vector3d(1, 0, 0);
            }

            if (!yAxis.Unitize())
            {
                yAxis = Math.Abs(xAxis.Z) < 0.9
                    ? Vector3d.CrossProduct(Vector3d.ZAxis, xAxis)
                    : Vector3d.CrossProduct(Vector3d.YAxis, xAxis);
            }

            double dot = xAxis.X * yAxis.X + xAxis.Y * yAxis.Y + xAxis.Z * yAxis.Z;
            yAxis -= xAxis * dot;
            if (!yAxis.Unitize())
            {
                yAxis = Math.Abs(xAxis.Z) < 0.9
                    ? Vector3d.CrossProduct(Vector3d.ZAxis, xAxis)
                    : Vector3d.CrossProduct(Vector3d.YAxis, xAxis);
                yAxis.Unitize();
            }
        }

        int FlatIndexFromPosition(Point3d point)
        {
            if (VoxelSize <= 0 || ResX <= 0 || ResY <= 0 || ResZ <= 0)
            {
                return -1;
            }

            int x = ResX == 1 ? 0 : (int)(point.X / VoxelSize);
            int y = ResY == 1 ? 0 : (int)(point.Y / VoxelSize);
            int z = ResZ == 1 ? 0 : (int)(point.Z / VoxelSize);

            if (x < 0 || x >= ResX || y < 0 || y >= ResY || z < 0 || z >= ResZ)
            {
                return -1;
            }

            int flatIndex = FlatIndex(x, y, z);
            return IsWalkableFlatIndex(flatIndex) ? flatIndex : -1;
        }

        bool IsWalkableFlatIndex(int flatIndex)
        {
            return VoxelFlags != null &&
                   flatIndex >= 0 &&
                   flatIndex < VoxelFlags.Length &&
                   (VoxelFlags[flatIndex] & 1) != 0;
        }

        static float ComputeSlimeWanderFrequency(double wander, int particleCount)
        {
            if (wander < 0) wander = 0;
            if (wander > 1) wander = 1;

            wander = 1 - wander;
            double frequency = Math.Floor(Math.Pow(wander, 3) * particleCount / 40.0);
            if (frequency < 1) frequency = 1;
            return (float)frequency;
        }

        static float ComputeAntBaseWanderFrequency(double wander, int particleCount)
        {
            if (wander < 0) wander = 0;
            if (wander > 1) wander = 1;

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
