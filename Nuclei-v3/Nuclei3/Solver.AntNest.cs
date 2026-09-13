using System;
using System.Threading;
using Rhino.Geometry;

namespace Nuclei3
{
    public partial class Solver
    {
        Point3d antNestPosition(Particle particle)
        {
            Point3d home = particle.home.Origin;
            if (planarXY) home.Z = dimZ / 2;
            if (planarXZ) home.Y = dimY / 2;
            if (planarYZ) home.X = dimX / 2;
            return home;
        }

        bool antSharesNestVoxel(Particle particle, Point3d position)
        {
            ParticleGroup group = particle.parentParticleGroup;
            if (group == null || !group.ant) return false;
            double radius = Math.Max(0, group.sensorDistance * 2);
            return radius > 0 && position.DistanceToSquared(antNestPosition(particle)) < radius * radius;
        }

        Vector3d blendAntNestApproach(Particle particle, Vector3d movement)
        {
            double radius = Math.Max(0, particle.parentParticleGroup.sensorDistance * 2);
            Vector3d home = antNestPosition(particle) - particle.pPlane.Origin;
            double distance = home.Length;
            if (!(radius > 0) || distance >= radius || !home.Unitize()) return movement;
            movement.Unitize();
            double takeover = Math.Max(0, Math.Min(1, 1 - distance / radius));
            Vector3d blended = movement * (1 - takeover) + home * takeover;
            return blended.Unitize() ? blended : home;
        }

        bool tryTransferAntNestOwnership(int[] owners, Particle particle, Voxel target, Point3d targetPosition, int token)
        {
            Voxel source = particle.parentVoxel;
            int from = source.flatIndex, to = target.flatIndex;
            bool owned = Volatile.Read(ref owners[from]) == token;
            bool shared = antSharesNestVoxel(particle, targetPosition);
            if (!shared && (!owned || from != to) && Interlocked.CompareExchange(ref owners[to], token, 0) != 0) return false;
            if (from != to)
            {
                Interlocked.Increment(ref target.particleCount);
                Interlocked.Decrement(ref source.particleCount);
            }
            if (owned && (shared || from != to)) Interlocked.CompareExchange(ref owners[from], 0, token);
            return true;
        }

        Vector3d antNestDeparture(Particle particle, Vector3d movement)
        {
            double radius = Math.Max(2 * Math.Max(0, particle.parentParticleGroup.sensorDistance),
                                     2 * Math.Abs(particle.parentParticleGroup.speed));
            Vector3d outward = particle.pPlane.Origin - antNestPosition(particle);
            if (particle.foundFood || radius <= 0 || outward.Length >= radius)
            {
                particle.antDepartingNest = false;
                return movement;
            }
            // Delivery reverses the incoming heading, giving a direction at home itself.
            if (!outward.Unitize()) outward = particle.pPlane.XAxis;
            if (planarXY) outward.Z = 0;
            if (planarXZ) outward.Y = 0;
            if (planarYZ) outward.X = 0;
            return outward.Unitize() ? outward : movement;
        }

        static void addAntPheromone(ref double field, double amount)
        {
            double before, observed = Volatile.Read(ref field);
            do { before = observed; observed = Interlocked.CompareExchange(ref field, before + amount, before); }
            while (!observed.Equals(before));
        }
    }
}
