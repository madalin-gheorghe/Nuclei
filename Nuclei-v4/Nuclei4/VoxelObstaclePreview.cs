using System;
using System.Drawing;
using Rhino.Display;
using Rhino.Geometry;

namespace Nuclei4
{
    // Static authored obstacles can be displayed over a live GPU field without
    // synchronizing density or pheromones back to the CPU.
    internal sealed class VoxelObstaclePreview : IDisposable
    {
        const int MaximumPointCount = 300000;
        static readonly Color ObstacleColor = Color.FromArgb(29, 19, 53);

        Mesh planarMesh;
        DisplayMaterial planarMaterial;
        PointCloud pointCloud;

        internal VoxelObstaclePreview(VoxelGridData data, double planarOffset)
        {
            if (data == null || data.ActiveCount == 0 || !data.MayContainBlockedMaxDensity()) return;

            try
            {
                if (data.ResZ == 1 || data.ResY == 1 || data.ResX == 1)
                    BuildPlanar(data, planarOffset);
                else
                    BuildPointCloud(data);
            }
            catch
            {
                Dispose();
                throw;
            }
        }

        internal void Draw(DisplayPipeline display)
        {
            if (display == null) return;
            if (planarMesh != null && planarMesh.Faces.Count > 0)
                display.DrawMeshShaded(planarMesh, planarMaterial);
            if (pointCloud != null && pointCloud.Count > 0)
                display.DrawPointCloud(pointCloud, 3);
        }

        void BuildPlanar(VoxelGridData data, double offset)
        {
            bool xy = data.ResZ == 1;
            bool xz = !xy && data.ResY == 1;
            int width = xy || xz ? data.ResX : data.ResY;
            int height = xy ? data.ResY : data.ResZ;
            planarMesh = new Mesh();

            for (int v = 0; v < height; v++)
            {
                int runStart = -1;
                for (int u = 0; u <= width; u++)
                {
                    int flatIndex = u == width ? -1 : xy ? data.FlatIndex(u, v, 0)
                        : xz ? data.FlatIndex(u, 0, v) : data.FlatIndex(0, u, v);
                    bool blocked = flatIndex >= 0 && data.IsActive(flatIndex)
                        && VoxelOccupancy.IsBlockedMaxDensity(data.MaximumDensity.Get(flatIndex));
                    if (blocked)
                    {
                        if (runStart < 0) runStart = u;
                        continue;
                    }
                    if (runStart < 0) continue;

                    // Merge adjacent blocked voxels within each row. Only obstacle
                    // faces are emitted, so empty cells cannot cover the live field.
                    int start = planarMesh.Vertices.Count;
                    planarMesh.Vertices.Add(PlanarPoint(runStart, v, data.VoxelSize, offset, xy, xz));
                    planarMesh.Vertices.Add(PlanarPoint(u, v, data.VoxelSize, offset, xy, xz));
                    planarMesh.Vertices.Add(PlanarPoint(u, v + 1, data.VoxelSize, offset, xy, xz));
                    planarMesh.Vertices.Add(PlanarPoint(runStart, v + 1, data.VoxelSize, offset, xy, xz));
                    planarMesh.Faces.AddFace(start, start + 1, start + 2, start + 3);
                    runStart = -1;
                }
            }

            if (planarMesh.Faces.Count == 0) return;
            planarMesh.Normals.ComputeNormals();
            using (var material = new Rhino.DocObjects.Material())
            {
                material.DiffuseColor = ObstacleColor;
                material.DisableLighting = true;
                planarMaterial = new DisplayMaterial(material);
            }
            planarMaterial.IsTwoSided = true;
            planarMaterial.BackDiffuse = ObstacleColor;
        }

        static Point3d PlanarPoint(int u, int v, double size, double offset, bool xy, bool xz)
        {
            if (xy) return new Point3d(u * size, v * size, offset);
            if (xz) return new Point3d(u * size, offset, v * size);
            return new Point3d(offset, u * size, v * size);
        }

        void BuildPointCloud(VoxelGridData data)
        {
            int blockedCount = 0;
            for (int ordinal = 0; ordinal < data.ActiveCount; ordinal++)
            {
                int flatIndex = data.ActiveFlatIndexAt(ordinal);
                if (VoxelOccupancy.IsBlockedMaxDensity(data.MaximumDensity.Get(flatIndex))) blockedCount++;
            }
            if (blockedCount == 0) return;

            // Sample the obstacles themselves, not the whole voxel grid, to keep
            // sparse barriers visible even in a large simulation domain.
            int sampleCount = Math.Min(blockedCount, MaximumPointCount);
            long accumulator = blockedCount - sampleCount;
            pointCloud = new PointCloud();
            for (int ordinal = 0; ordinal < data.ActiveCount; ordinal++)
            {
                int flatIndex = data.ActiveFlatIndexAt(ordinal);
                if (!VoxelOccupancy.IsBlockedMaxDensity(data.MaximumDensity.Get(flatIndex))) continue;
                accumulator += sampleCount;
                if (accumulator < blockedCount) continue;
                accumulator -= blockedCount;
                pointCloud.Add(data.CenterPoint(flatIndex), ObstacleColor);
            }
        }

        public void Dispose()
        {
            planarMesh?.Dispose();
            planarMesh = null;
            planarMaterial?.Dispose();
            planarMaterial = null;
            pointCloud?.Dispose();
            pointCloud = null;
        }
    }
}
