using System;
using System.Drawing;
using Rhino.Display;
using Rhino.Geometry;

namespace Nuclei4
{
    // Food sources are a final overlay, independent of signal colour and thresholds.
    internal sealed class VoxelFoodPreview : IDisposable
    {
        const int MaximumPointCount = 300000;
        Mesh planarMesh;
        DisplayMaterial planarMaterial;
        PointCloud pointCloud;

        internal VoxelFoodPreview(VoxelGridData data, double offset, Func<int, double> foodAt)
        {
            if (data == null || data.ActiveCount == 0) return;
            try
            {
                bool HasFood(int index) => data.IsActive(index) && foodAt(index) > 0;
                if (data.ResZ == 1 || data.ResY == 1 || data.ResX == 1)
                {
                    bool xy = data.ResZ == 1;
                    bool xz = !xy && data.ResY == 1;
                    int width = xy || xz ? data.ResX : data.ResY;
                    int height = xy ? data.ResY : data.ResZ;
                    Point3d Point(int u, int v) => xy ? new Point3d(u * data.VoxelSize, v * data.VoxelSize, offset)
                        : xz ? new Point3d(u * data.VoxelSize, offset, v * data.VoxelSize)
                        : new Point3d(offset, u * data.VoxelSize, v * data.VoxelSize);
                    planarMesh = new Mesh();
                    for (int v = 0; v < height; v++)
                    {
                        int start = -1;
                        for (int u = 0; u <= width; u++)
                        {
                            int index = u == width ? -1 : xy ? data.FlatIndex(u, v, 0)
                                : xz ? data.FlatIndex(u, 0, v) : data.FlatIndex(0, u, v);
                            if (index >= 0 && HasFood(index))
                            {
                                if (start < 0) start = u;
                                continue;
                            }
                            if (start < 0) continue;
                            int vertex = planarMesh.Vertices.Count;
                            planarMesh.Vertices.Add(Point(start, v));
                            planarMesh.Vertices.Add(Point(u, v));
                            planarMesh.Vertices.Add(Point(u, v + 1));
                            planarMesh.Vertices.Add(Point(start, v + 1));
                            planarMesh.Faces.AddFace(vertex, vertex + 1, vertex + 2, vertex + 3);
                            start = -1;
                        }
                    }
                    if (planarMesh.Faces.Count == 0) return;
                    planarMesh.Normals.ComputeNormals();
                    using (var material = new Rhino.DocObjects.Material())
                    {
                        material.DiffuseColor = Color.White;
                        material.DisableLighting = true;
                        planarMaterial = new DisplayMaterial(material);
                    }
                    planarMaterial.IsTwoSided = true;
                    planarMaterial.BackDiffuse = Color.White;
                }
                else
                {
                    int count = 0;
                    for (int ordinal = 0; ordinal < data.ActiveCount; ordinal++)
                        if (HasFood(data.ActiveFlatIndexAt(ordinal))) count++;
                    if (count == 0) return;
                    int samples = Math.Min(count, MaximumPointCount);
                    long accumulator = count - samples;
                    pointCloud = new PointCloud();
                    for (int ordinal = 0; ordinal < data.ActiveCount; ordinal++)
                    {
                        int index = data.ActiveFlatIndexAt(ordinal);
                        if (!HasFood(index)) continue;
                        accumulator += samples;
                        if (accumulator < count) continue;
                        accumulator -= count;
                        pointCloud.Add(data.CenterPoint(index), Color.White);
                    }
                }
            }
            catch { Dispose(); throw; }
        }

        internal void Draw(DisplayPipeline display)
        {
            if (display == null) return;
            display.PushDepthTesting(false);
            display.PushDepthWriting(false);
            try
            {
                if (planarMaterial != null) display.DrawMeshShaded(planarMesh, planarMaterial);
                if (pointCloud != null) display.DrawPointCloud(pointCloud, 3);
            }
            finally
            {
                display.PopDepthWriting();
                display.PopDepthTesting();
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
