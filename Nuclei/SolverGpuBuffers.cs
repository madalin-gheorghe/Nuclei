using System;
using System.Collections.Generic;

using Rhino.Geometry;

namespace Nuclei3
{
    internal sealed class SolverGpuInputSnapshot
    {
        public Voxel[,,] Voxels;
        public ParticleList Particles;
        public int ResX;
        public int ResY;
        public int ResZ;
        public int ActiveVoxelCount;
        public int ParticleCount;
        public float VoxelSize;
        public float[] ParticlePositionsXyz;
        public float[] ParticleDirectionsXyz;
        public float[] ParticleYAxesXyz;
        public int[] ParticleGroupIndices;
        public int[] ParticleParentIndices;
        public float[] VoxelDensity;
        public float[] VoxelVectorsXyz;
        public uint[] VoxelFlags;
        public int GroupCount;
        public float[] GroupData0;
        public float[] GroupData1;
        public bool HasAntParticles;

        public static SolverGpuInputSnapshot Capture(Voxel[,,] inputVoxels, IList<ParticleGroup> particleGroups)
        {
            SolverGpuInputSnapshot snapshot = new SolverGpuInputSnapshot();

            snapshot.CaptureVoxels(inputVoxels);
            snapshot.CaptureParticles(particleGroups);

            return snapshot;
        }

        void CaptureVoxels(Voxel[,,] inputVoxels)
        {
            if (inputVoxels == null)
            {
                Voxels = new Voxel[0, 0, 0];
                VoxelDensity = new float[0];
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
            VoxelDensity = new float[voxelCount];
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

                        VoxelDensity[flatIndex] = (float)voxel.density;
                        VoxelVectorsXyz[flatIndex * 3] = (float)voxel.voxelVector.X;
                        VoxelVectorsXyz[flatIndex * 3 + 1] = (float)voxel.voxelVector.Y;
                        VoxelVectorsXyz[flatIndex * 3 + 2] = (float)voxel.voxelVector.Z;
                        if (voxel.maxDensity != 0)
                        {
                            VoxelFlags[flatIndex] = 1;
                        }

                        ActiveVoxelCount++;
                    }
                }
            }
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

                        if (particle.parentParticleGroup == null)
                        {
                            particle.parentParticleGroup = group;
                        }

                        Particles.Add(particle);
                    }
                }
            }

            ParticleCount = Particles.Count;
            ParticlePositionsXyz = new float[ParticleCount * 3];
            ParticleDirectionsXyz = new float[ParticleCount * 3];
            ParticleYAxesXyz = new float[ParticleCount * 3];
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
                    ParticleParentIndices[particleIndex] = FlatIndexFromPosition(origin);

                    particleIndex++;
                }
            }
        }

        void CaptureParticleGroups(IList<ParticleGroup> particleGroups)
        {
            GroupCount = particleGroups != null ? particleGroups.Count : 0;
            GroupData0 = new float[GroupCount * 4];
            GroupData1 = new float[GroupCount * 4];

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
                    HasAntParticles = true;
                }

                double sensorAngle = Math.PI * group.sensorAngle / 180.0;
                double rotationAngle = Math.PI * group.rotationAngle / 180.0;
                int particleCount = group.particles != null ? group.particles.Count : 0;
                float wanderFrequency = group.ant
                    ? ComputeAntBaseWanderFrequency(group.baseWanderFrequency, particleCount)
                    : ComputeSlimeWanderFrequency(group.wanderFrequency, particleCount);

                int offset = groupIndex * 4;
                GroupData0[offset] = (float)group.speed;
                GroupData0[offset + 1] = (float)group.sensorDistance;
                GroupData0[offset + 2] = (float)Math.Cos(sensorAngle);
                GroupData0[offset + 3] = (float)Math.Sin(sensorAngle);

                GroupData1[offset] = (float)Math.Cos(rotationAngle);
                GroupData1[offset + 1] = (float)Math.Sin(rotationAngle);
                GroupData1[offset + 2] = (float)group.depositValue;
                GroupData1[offset + 3] = wanderFrequency;
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

            return FlatIndex(x, y, z);
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
