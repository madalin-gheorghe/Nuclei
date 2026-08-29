using System;
using System.Collections.Generic;

using Rhino.Geometry;

namespace Nuclei4
{
    /// <summary>
    /// GH1-owned materializer for host-neutral GPU readbacks. This keeps Rhino and
    /// legacy Nuclei domain objects out of compute backends while preserving the
    /// existing object identity, population, trail, and preview-cache behavior.
    /// </summary>
    internal sealed class Gh1GpuSolverOutputSink : IGpuSolverOutputSink
    {
        VoxelField voxelField;
        ParticleList particles;
        ParticleGroup[] particleGroups;
        Particle[] particleSlots;
        int[] particleSlotGenerations;

        public Gh1GpuSolverOutputSink(SolverGpuInputSnapshot snapshot, int particleCapacity)
        {
            Bind(snapshot, particleCapacity);
        }

        public int ParticleCount { get; private set; }

        public void Bind(SolverGpuInputSnapshot snapshot, int particleCapacity)
        {
            if (snapshot == null)
            {
                throw new ArgumentNullException(nameof(snapshot));
            }

            voxelField = snapshot.Field;
            particles = snapshot.Particles ?? new ParticleList();
            particleGroups = snapshot.ParticleGroups ?? new ParticleGroup[0];

            int initialParticleCount = Math.Max(0, snapshot.ParticleCount);
            int initialCapacity = Math.Max(initialParticleCount, Math.Max(0, particleCapacity));
            if (particleSlots == null || particleSlots.Length < initialCapacity)
            {
                particleSlots = new Particle[initialCapacity];
                particleSlotGenerations = new int[initialCapacity];
            }
            else
            {
                Array.Clear(particleSlots, 0, particleSlots.Length);
                Array.Clear(particleSlotGenerations, 0, particleSlotGenerations.Length);
            }

            int snapshotParticleCount = Math.Min(initialParticleCount, particles.Count);
            for (int i = 0; i < snapshotParticleCount; i++)
            {
                particleSlots[i] = particles[i];
            }

            ParticleCount = initialParticleCount;
        }

        public void UpdateVoxelField(VoxelField field)
        {
            voxelField = field;
        }

        public void ApplyVoxelFields(GpuVoxelReadbackView view)
        {
            if (voxelField == null)
            {
                return;
            }

            VoxelDynamicData existing = voxelField.Dynamic;
            voxelField.UpdateDynamicFields(
                view.HasSlime ? view.Density : existing != null ? existing.Density : null,
                view.HasAnt ? view.AntFood : existing != null ? existing.AntFoodPheromone : null,
                view.HasAnt ? view.AntBase : existing != null ? existing.AntBasePheromone : null,
                view.HasAnt ? view.RemainingFood : existing != null ? existing.RemainingFood : null);
        }

        public bool ApplyParticles(
            GpuParticleReadbackView view,
            SolverGpuSettings settings,
            int iteration,
            bool buildPreviewCache)
        {
            if (particles == null || view.Capacity <= 0)
            {
                return false;
            }

            EnsureParticleCapacity(view.Capacity);
            // One voxel refresh per step; see VoxelFromFlatIndex.
            voxelCacheGeneration++;

            ParticlePreviewCache previewCache = buildPreviewCache ? particles.PreviewCache : null;
            ParticlePreviewBuildCache previewBuildCache = previewCache != null
                ? new ParticlePreviewBuildCache(view.Count)
                : null;
            if (previewCache != null)
            {
                previewCache.BeginBuild(view.Count);
            }

            particles.Clear();
            for (int groupIndex = 0; groupIndex < particleGroups.Length; groupIndex++)
            {
                ParticleGroup group = particleGroups[groupIndex];
                if (group != null && group.particles != null)
                {
                    group.particles.Clear();
                }
            }

            float[] positions = view.Positions;
            float[] directions = view.Directions;
            float[] yAxes = view.YAxes;
            int[] auxiliary = view.Auxiliary;
            int activeCount = 0;
            for (int i = 0; i < view.Capacity; i++)
            {
                int offset = i * 4;
                int groupIndex = (int)Math.Round(positions[offset + 3]);
                if (groupIndex < 0 || groupIndex >= view.GroupCount)
                {
                    Particle deadParticle = particleSlots[i];
                    if (deadParticle != null && deadParticle.trails != null)
                    {
                        deadParticle.trails.Clear();
                    }
                    particleSlots[i] = null;
                    continue;
                }

                Particle particle = particleSlots[i];
                int generation = auxiliary[view.Capacity * 3 + i];
                if (particle == null)
                {
                    particle = new Particle();
                    particleSlots[i] = particle;
                }
                else if (particleSlotGenerations[i] != generation && particle.trails != null)
                {
                    particle.trails.Clear();
                }
                particleSlotGenerations[i] = generation;
                particle.parentParticleGroup = groupIndex < particleGroups.Length
                    ? particleGroups[groupIndex]
                    : null;
                particle.age = auxiliary[i];
                particle.foundFood = particle.parentParticleGroup != null
                    && particle.parentParticleGroup.ant
                    && auxiliary[view.Capacity * 4 + i] != 0;
                particle.highDeposit = auxiliary.Length >= view.Capacity * 6
                    && auxiliary[view.Capacity * 5 + i] != 0;
                particle.antLaunchBoundaryHit = particle.parentParticleGroup != null
                    && particle.parentParticleGroup.ant
                    && auxiliary.Length >= view.Capacity * 7
                    && auxiliary[view.Capacity * 6 + i] != 0;

                Point3d origin = new Point3d(
                    positions[offset],
                    positions[offset + 1],
                    positions[offset + 2]);

                Vector3d xAxis = new Vector3d(
                    directions[offset],
                    directions[offset + 1],
                    directions[offset + 2]);

                Vector3d yAxis = new Vector3d(
                    yAxes[offset],
                    yAxes[offset + 1],
                    yAxes[offset + 2]);

                if (!TryUnitize(ref xAxis))
                {
                    xAxis = new Vector3d(1, 0, 0);
                }

                yAxis = OrthonormalYAxis(xAxis, yAxis);
                particle.pPlane = PlaneFromOrthonormalAxes(origin, xAxis, yAxis);

                if (particle.parentParticleGroup != null
                    && particle.parentParticleGroup.ant
                    && view.Homes != null
                    && view.Homes.Length >= offset + 3
                    && view.HomeAxes != null
                    && view.HomeAxes.Length >= view.Capacity * 6)
                {
                    Point3d homeOrigin = new Point3d(
                        view.Homes[offset],
                        view.Homes[offset + 1],
                        view.Homes[offset + 2]);
                    Vector3d homeYAxis = new Vector3d(
                        view.HomeAxes[i],
                        view.HomeAxes[view.Capacity + i],
                        view.HomeAxes[view.Capacity * 2 + i]);
                    Vector3d homeXAxis = new Vector3d(
                        view.HomeAxes[view.Capacity * 3 + i],
                        view.HomeAxes[view.Capacity * 4 + i],
                        view.HomeAxes[view.Capacity * 5 + i]);

                    if (TryUnitize(ref homeXAxis) && TryUnitize(ref homeYAxis))
                    {
                        homeYAxis = OrthonormalYAxis(homeXAxis, homeYAxis);
                        particle.home = PlaneFromOrthonormalAxes(homeOrigin, homeXAxis, homeYAxis);
                    }
                    else
                    {
                        // V3 normal division leaves an ant child's home plane at
                        // its default value; only random division inherits it.
                        particle.home = new Plane();
                    }
                }
                else if (particle.parentParticleGroup == null || !particle.parentParticleGroup.ant)
                {
                    // A dead GPU slot can be claimed by a different species. V3
                    // constructs a fresh slime Particle whose home is the default
                    // plane; do not leak the previous ant occupant's home state.
                    particle.home = new Plane();
                }

                int parentIndex = (int)Math.Round(directions[offset + 3]);
                particle.parentVoxel = VoxelFromFlatIndex(parentIndex);
                particle.age = auxiliary[i];
                particle.neighbourCount_Die = auxiliary[view.Capacity + i];
                particle.neighbourCount_Div = auxiliary[view.Capacity * 2 + i];

                if (yAxes[offset + 3] > 0.5f)
                {
                    particle.trails.Clear();
                }

                if (previewBuildCache != null)
                {
                    previewBuildCache.AddParticle(particle);
                }

                particles.Add(particle);
                if (particle.parentParticleGroup != null && particle.parentParticleGroup.particles != null)
                {
                    particle.parentParticleGroup.particles.Add(particle);
                }
                activeCount++;
            }

            ParticleCount = activeCount;
            RecordTrails(settings, iteration);

            if (previewCache != null)
            {
                previewCache.Merge(previewBuildCache);
                previewCache.CompleteBuild();
                return true;
            }

            particles.PreviewCache.Invalidate(activeCount);
            return false;
        }

        public bool ApplyPreviewPositions(GpuParticlePreviewReadbackView view)
        {
            if (particles == null || view.Capacity <= 0)
            {
                return false;
            }

            EnsureParticleCapacity(view.Capacity);
            voxelCacheGeneration++;

            ParticlePreviewCache previewCache = particles.PreviewCache;
            ParticlePreviewBuildCache previewBuildCache = new ParticlePreviewBuildCache(view.Count);
            previewCache.BeginBuild(view.Count);

            int activeCount = 0;
            for (int i = 0; i < view.Capacity; i++)
            {
                int offset = i * 4;
                double previewGroupTag = view.Positions[offset + 3];
                int groupIndex = (int)Math.Round(previewGroupTag);
                if (groupIndex < 0 || groupIndex >= view.GroupCount)
                {
                    continue;
                }

                Particle particle = particleSlots[i];
                if (particle == null)
                {
                    particle = new Particle();
                    particleSlots[i] = particle;
                }

                // Dynamic population can reuse a dead slot for another group.
                // Refresh classification on every lightweight preview readback;
                // the fractional GPU tag carries the ant found-food state.
                particle.parentParticleGroup = groupIndex < particleGroups.Length
                    ? particleGroups[groupIndex]
                    : null;
                particle.foundFood = particle.parentParticleGroup != null
                    && particle.parentParticleGroup.ant
                    && Math.Abs(previewGroupTag - groupIndex) > 0.1;

                Point3d origin = new Point3d(
                    view.Positions[offset],
                    view.Positions[offset + 1],
                    view.Positions[offset + 2]);

                previewBuildCache.AddParticlePoint(particle, origin);
                activeCount++;
            }

            previewCache.Merge(previewBuildCache);
            previewCache.CompleteBuild();
            previewCache.ParticleCount = activeCount;
            return true;
        }

        void EnsureParticleCapacity(int capacity)
        {
            if (particleSlots != null && particleSlots.Length >= capacity)
            {
                return;
            }

            Particle[] expandedSlots = new Particle[capacity];
            int[] expandedGenerations = new int[capacity];
            if (particleSlots != null)
            {
                Array.Copy(particleSlots, expandedSlots, particleSlots.Length);
                Array.Copy(particleSlotGenerations, expandedGenerations, particleSlotGenerations.Length);
            }

            particleSlots = expandedSlots;
            particleSlotGenerations = expandedGenerations;
        }

        struct VoxelCacheEntry
        {
            public Voxel Voxel;
            public int Generation;
        }

        readonly Dictionary<int, VoxelCacheEntry> voxelCache = new Dictionary<int, VoxelCacheEntry>();
        VoxelField voxelCacheField;
        int voxelCacheGeneration;

        /// <summary>
        /// Voxel instances are reused across particles and across steps, refreshed at
        /// most once per step each. Allocating one per particle per step dominated the
        /// solver component cost once dynamic population let the population grow. The
        /// cache is keyed by flat index, so it is bounded by the number of occupied
        /// voxels rather than by the grid size.
        /// </summary>
        Voxel VoxelFromFlatIndex(int index)
        {
            if (voxelField == null || index < 0 || index >= voxelField.Count)
            {
                return null;
            }

            if (!ReferenceEquals(voxelCacheField, voxelField))
            {
                voxelCache.Clear();
                voxelCacheField = voxelField;
            }

            VoxelCacheEntry entry;
            if (voxelCache.TryGetValue(index, out entry) && entry.Voxel != null)
            {
                if (entry.Generation != voxelCacheGeneration)
                {
                    voxelField.RefreshDynamicValues(entry.Voxel, index);
                    entry.Generation = voxelCacheGeneration;
                    voxelCache[index] = entry;
                }

                return entry.Voxel;
            }

            Voxel created = voxelField.CreateVoxel(index);
            if (created == null)
            {
                return null;
            }

            voxelCache[index] = new VoxelCacheEntry { Voxel = created, Generation = voxelCacheGeneration };
            return created;
        }

        void RecordTrails(SolverGpuSettings settings, int iteration)
        {
            if (particles == null)
            {
                return;
            }

            bool sampleTrail = settings.TrailFreq <= 1 || iteration % settings.TrailFreq == 0;
            for (int i = 0; i < particles.Count; i++)
            {
                Particle particle = particles[i];
                if (particle == null || particle.parentVoxel == null)
                {
                    continue;
                }

                if (settings.TrailSize <= 1)
                {
                    if (particle.trails.Count > 0)
                    {
                        particle.trails.Clear();
                    }

                    continue;
                }

                if (particle.trails.Capacity < settings.TrailSize)
                {
                    particle.trails.Capacity = settings.TrailSize;
                }

                Point3d origin = particle.pPlane.Origin;
                if (sampleTrail)
                {
                    if (particle.trails.Count > 0)
                    {
                        particle.trails.Insert(0, origin);
                    }
                    else
                    {
                        particle.trails.Add(origin);
                    }

                    if (particle.trails.Count > settings.TrailSize)
                    {
                        particle.trails.RemoveAt(particle.trails.Count - 1);
                    }
                }
                else if (particle.trails.Count > 0)
                {
                    particle.trails[0] = origin;
                }
                else
                {
                    particle.trails.Add(origin);
                }
            }
        }

        /// <summary>
        /// Managed normalization. Vector3d.Unitize is a native call, and this runs three
        /// times per particle per step; at a large population the transitions alone cost
        /// more than the arithmetic.
        /// </summary>
        static bool TryUnitize(ref Vector3d vector)
        {
            double length = Math.Sqrt(vector.X * vector.X + vector.Y * vector.Y + vector.Z * vector.Z);
            if (!(length > 1e-12)) return false;

            double inverse = 1.0 / length;
            vector = new Vector3d(vector.X * inverse, vector.Y * inverse, vector.Z * inverse);
            return true;
        }

        static Vector3d Cross(Vector3d a, Vector3d b)
        {
            return new Vector3d(
                a.Y * b.Z - a.Z * b.Y,
                a.Z * b.X - a.X * b.Z,
                a.X * b.Y - a.Y * b.X);
        }

        /// <summary>
        /// Builds the plane from axes that are already unit length and orthogonal, so the
        /// native Plane constructor's own orthonormalization is redundant work.
        /// </summary>
        static Plane PlaneFromOrthonormalAxes(Point3d origin, Vector3d xAxis, Vector3d yAxis)
        {
            Plane plane = new Plane();
            plane.Origin = origin;
            plane.XAxis = xAxis;
            plane.YAxis = yAxis;
            plane.ZAxis = Cross(xAxis, yAxis);
            return plane;
        }

        static Vector3d OrthonormalYAxis(Vector3d xAxis, Vector3d yAxis)
        {
            if (!TryUnitize(ref yAxis))
            {
                yAxis = Math.Abs(xAxis.Z) < 0.9
                    ? Cross(Vector3d.ZAxis, xAxis)
                    : Cross(Vector3d.YAxis, xAxis);
            }

            double dot = xAxis.X * yAxis.X + xAxis.Y * yAxis.Y + xAxis.Z * yAxis.Z;
            yAxis -= xAxis * dot;

            if (!TryUnitize(ref yAxis))
            {
                yAxis = Math.Abs(xAxis.Z) < 0.9
                    ? Cross(Vector3d.ZAxis, xAxis)
                    : Cross(Vector3d.YAxis, xAxis);
                TryUnitize(ref yAxis);
            }

            return yAxis;
        }
    }
}
