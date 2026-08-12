using System;
using System.Collections.Generic;
using Rhino.Geometry;

namespace Nuclei3
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

        public static List<Particle> CreateRandomVoxelCenterParticles(int count, ParticleGroup group, VoxelGridData voxelData)
        {
            if (count <= 0 || voxelData == null || voxelData.ActiveCount <= 0 || voxelData.VoxelSize <= 0)
            {
                return new List<Particle>();
            }

            Particle[] particles = new Particle[count];
            int voxelCount = voxelData.ActiveCount;
            int offset = (int)(Hash((uint)voxelCount ^ 0x4A7C15D1u) % (uint)voxelCount);
            int stride = FindCoprimeStride(voxelCount, Hash((uint)count ^ 0xB5297A4Du));

            for (int i = 0; i < count; i++)
            {
                int ordinal = (int)(((long)offset + (long)(i % voxelCount) * stride) % voxelCount);
                int flatIndex = voxelData.ActiveFlatIndexAt(ordinal);
                uint seed = Hash((uint)i ^ (uint)flatIndex ^ 0x9E3779B9u);
                particles[i] = CreateParticle(voxelData.CenterPoint(flatIndex), group, seed);
            }

            return new List<Particle>(particles);
        }

        static int FindCoprimeStride(int modulus, uint seed)
        {
            if (modulus <= 1)
            {
                return 1;
            }

            int stride = (int)(seed % (uint)(modulus - 1)) + 1;
            while (GreatestCommonDivisor(stride, modulus) != 1)
            {
                stride++;
                if (stride >= modulus)
                {
                    stride = 1;
                }
            }

            return stride;
        }

        static int GreatestCommonDivisor(int a, int b)
        {
            a = Math.Abs(a);
            b = Math.Abs(b);

            while (b != 0)
            {
                int remainder = a % b;
                a = b;
                b = remainder;
            }

            return a;
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

        static double ToUnit(uint value)
        {
            return (value & 0x00FFFFFFu) / 16777216.0;
        }
    }
}
