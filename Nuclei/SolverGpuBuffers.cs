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
        public float[] ParticlePositionsXyz;
        public float[] ParticleDirectionsXyz;
        public int[] ParticleGroupIndices;
        public float[] VoxelDensity;
        public float[] VoxelVectorsXyz;

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
                return;
            }

            ResX = inputVoxels.GetLength(0);
            ResY = inputVoxels.GetLength(1);
            ResZ = inputVoxels.GetLength(2);

            int voxelCount = ResX * ResY * ResZ;
            double voxelSize = ResolveVoxelSize(inputVoxels);
            bool hasInputVoxels = ContainsAnyVoxel(inputVoxels);

            Voxels = new Voxel[ResX, ResY, ResZ];
            VoxelDensity = new float[voxelCount];
            VoxelVectorsXyz = new float[voxelCount * 3];

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
                        ActiveVoxelCount++;
                    }
                }
            }
        }

        void CaptureParticles(IList<ParticleGroup> particleGroups)
        {
            Particles = new ParticleList();

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
            ParticleGroupIndices = new int[ParticleCount];

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

                    ParticlePositionsXyz[particleIndex * 3] = (float)origin.X;
                    ParticlePositionsXyz[particleIndex * 3 + 1] = (float)origin.Y;
                    ParticlePositionsXyz[particleIndex * 3 + 2] = (float)origin.Z;
                    ParticleDirectionsXyz[particleIndex * 3] = (float)xAxis.X;
                    ParticleDirectionsXyz[particleIndex * 3 + 1] = (float)xAxis.Y;
                    ParticleDirectionsXyz[particleIndex * 3 + 2] = (float)xAxis.Z;
                    ParticleGroupIndices[particleIndex] = groupIndex;

                    particleIndex++;
                }
            }
        }

        int FlatIndex(int x, int y, int z)
        {
            return x * ResY * ResZ + y * ResZ + z;
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
