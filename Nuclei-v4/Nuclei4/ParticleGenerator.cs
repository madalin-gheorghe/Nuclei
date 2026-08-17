using System;
using System.Collections.Generic;
using Rhino.Geometry;

namespace Nuclei4
{
    internal static class ParticleGenerator
    {
        const double TwoPi = Math.PI * 2.0;

        public static List<Particle> CreateFromPoints(IList<Point3d> points, ParticleGroup group)
        {
            int count = points != null ? points.Count : 0;
            Particle[] particles = new Particle[count];

            for (int i = 0; i < count; i++)
            {
                particles[i] = CreateParticle(points[i], group, (uint)i);
            }

            return new List<Particle>(particles);
        }

        public static List<Particle> CreateScatteredParticles(int count, ParticleGroup group, VoxelGridData voxelData)
        {
            if (count <= 0 || voxelData == null || voxelData.ActiveCount <= 0 || voxelData.VoxelSize <= 0)
            {
                return new List<Particle>();
            }

            WalkableOrdinalIndex walkableIndices = voxelData.MayContainBlockedMaxDensity()
                ? WalkableOrdinalIndex.Create(voxelData)
                : null;
            int voxelCount = walkableIndices != null ? walkableIndices.Count : voxelData.ActiveCount;
            if (voxelCount <= 0)
            {
                return new List<Particle>();
            }

            Particle[] particles = new Particle[count];
            uint sequenceSeed = Hash((uint)count ^ (uint)voxelCount ^ 0x4A7C15D1u);

            for (int i = 0; i < count; i++)
            {
                uint sampleSeed = Hash((uint)i ^ sequenceSeed ^ 0xB5297A4Du);
                int ordinal = (int)(sampleSeed % (uint)voxelCount);
                int flatIndex = walkableIndices != null ? walkableIndices.FlatIndexAt(ordinal) : voxelData.ActiveFlatIndexAt(ordinal);
                Point3d point = ScatteredPointInVoxel(voxelData, flatIndex, sampleSeed);
                uint directionSeed = Hash(sampleSeed ^ (uint)flatIndex ^ 0x9E3779B9u);
                particles[i] = CreateParticle(point, group, directionSeed);
            }

            return new List<Particle>(particles);
        }

        static Point3d ScatteredPointInVoxel(VoxelGridData voxelData, int flatIndex, uint seed)
        {
            Point3d center = voxelData.CenterPoint(flatIndex);
            double radius = voxelData.VoxelSize * 0.45;
            if (voxelData.ResX > 1) center.X += (ToUnit(Hash(seed ^ 0x68BC21EBu)) * 2.0 - 1.0) * radius;
            if (voxelData.ResY > 1) center.Y += (ToUnit(Hash(seed ^ 0x02E5BE93u)) * 2.0 - 1.0) * radius;
            if (voxelData.ResZ > 1) center.Z += (ToUnit(Hash(seed ^ 0x967A889Bu)) * 2.0 - 1.0) * radius;
            return center;
        }

        static Particle CreateParticle(Point3d point, ParticleGroup group, uint seed)
        {
            Plane plane = CreateRandomPlane(point, seed);
            Particle particle = new Particle(plane);
            particle.parentParticleGroup = group;
            return particle;
        }

        static Plane CreateRandomPlane(Point3d point, uint seed)
        {
            double z = ToUnit(Hash(seed ^ 0xA511E9B3u)) * 2.0 - 1.0;
            double angle = ToUnit(Hash(seed ^ 0x63D83595u)) * TwoPi;
            double radius = Math.Sqrt(Math.Max(0, 1.0 - z * z));
            Vector3d xAxis = new Vector3d(Math.Cos(angle) * radius, Math.Sin(angle) * radius, z);
            if (!xAxis.Unitize())
            {
                xAxis = Vector3d.XAxis;
            }

            Vector3d helper = Math.Abs(xAxis.Z) < 0.9 ? Vector3d.ZAxis : Vector3d.YAxis;
            Vector3d yAxis = Vector3d.CrossProduct(helper, xAxis);
            if (!yAxis.Unitize())
            {
                yAxis = Vector3d.YAxis;
            }

            return new Plane(point, xAxis, yAxis);
        }

        static uint Hash(uint x)
        {
            x ^= x >> 16;
            x *= 0x7feb352du;
            x ^= x >> 15;
            x *= 0x846ca68bu;
            x ^= x >> 16;
            return x;
        }

        sealed class WalkableOrdinalIndex
        {
            readonly VoxelGridData data;
            readonly ulong[] words;
            readonly int[] prefixCounts;

            WalkableOrdinalIndex(VoxelGridData data, ulong[] words, int[] prefixCounts, int count)
            {
                this.data = data;
                this.words = words;
                this.prefixCounts = prefixCounts;
                Count = count;
            }

            public int Count { get; private set; }

            public static WalkableOrdinalIndex Create(VoxelGridData data)
            {
                int activeCount = data != null ? data.ActiveCount : 0;
                ulong[] words = new ulong[(activeCount + 63) / 64];
                for (int ordinal = 0; ordinal < activeCount; ordinal++)
                {
                    int flatIndex = data.ActiveFlatIndexAt(ordinal);
                    if (data.IsWalkableFlatIndex(flatIndex))
                    {
                        words[ordinal >> 6] |= 1UL << (ordinal & 63);
                    }
                }

                int[] prefixCounts = new int[words.Length + 1];
                for (int wordIndex = 0; wordIndex < words.Length; wordIndex++)
                {
                    prefixCounts[wordIndex + 1] = prefixCounts[wordIndex] + PopCount(words[wordIndex]);
                }

                return new WalkableOrdinalIndex(data, words, prefixCounts, prefixCounts[prefixCounts.Length - 1]);
            }

            public int FlatIndexAt(int walkableOrdinal)
            {
                if (walkableOrdinal < 0 || walkableOrdinal >= Count)
                {
                    return -1;
                }

                int low = 0;
                int high = words.Length - 1;
                while (low < high)
                {
                    int middle = low + ((high - low) >> 1);
                    if (prefixCounts[middle + 1] <= walkableOrdinal) low = middle + 1;
                    else high = middle;
                }

                int bitOrdinal = walkableOrdinal - prefixCounts[low];
                ulong word = words[low];
                int bit = SelectSetBit(word, bitOrdinal);
                return data.ActiveFlatIndexAt((low << 6) + bit);
            }

            static int PopCount(ulong value)
            {
                value -= (value >> 1) & 0x5555555555555555UL;
                value = (value & 0x3333333333333333UL) + ((value >> 2) & 0x3333333333333333UL);
                return (int)((((value + (value >> 4)) & 0x0F0F0F0F0F0F0F0FUL) * 0x0101010101010101UL) >> 56);
            }

            static int SelectSetBit(ulong value, int ordinal)
            {
                for (int bit = 0; bit < 64; bit++)
                {
                    if ((value & (1UL << bit)) == 0) continue;
                    if (ordinal == 0) return bit;
                    ordinal--;
                }

                return 0;
            }
        }

        static double ToUnit(uint value)
        {
            return (value & 0x00FFFFFFu) / 16777216.0;
        }
    }
}
