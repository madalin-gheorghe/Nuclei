using System;
using System.Diagnostics;
using Rhino.Geometry;

namespace Nuclei4
{
    // General scalar-field fallback; live slime keeps the existing GPU meshing path.
    internal static class VoxelScalarMesher
    {
        static readonly int[,] Corners = { {0,0,0}, {1,0,0}, {1,1,0}, {0,1,0}, {0,0,1}, {1,0,1}, {1,1,1}, {0,1,1} };
        static readonly int[,] Tetrahedra = { {0,5,1,6}, {0,1,2,6}, {0,2,3,6}, {0,3,7,6}, {0,7,4,6}, {0,4,5,6} };

        internal static double Value(VoxelField field, int type, int index)
        {
            type = VoxelPreviewField.SourceField(type);
            if (VoxelPreviewField.IsCombinedDynamicDensity(type))
            {
                // Combined previews overlay channels: take their union at the iso level.
                double value = Math.Max(field.GetScalarValue(VoxelPreviewField.AntFoodPheromones, index),
                    field.GetScalarValue(VoxelPreviewField.AntBasePheromones, index));
                return type == VoxelPreviewField.AntsAndSlime
                    ? Math.Max(value, field.GetScalarValue(VoxelPreviewField.SlimeChemoattractants, index)) : value;
            }
            return field.GetScalarValue(type, index);
        }

        internal static GpuVolumeMeshResult Create(VoxelField field, int type, double iso, int limit, int smoothing)
        {
            if (VoxelPreviewField.SourceField(type) == VoxelPreviewField.SlimeChemoattractants
                && field.GpuVolumeMeshProvider != null && iso >= 0.000001 && iso <= float.MaxValue)
                return field.GpuVolumeMeshProvider((float)iso, limit, smoothing);

            var result = new GpuVolumeMeshResult();
            var timer = Stopwatch.StartNew();
            Mesh mesh = null;
            try
            {
                if (VoxelPreviewField.IsDynamicDensity(type)) field.EnsureDynamicStateCurrent();
                var data = field.Data;
                // Exterior samples must remain below the iso level, including zero/negative levels.
                double outside = Math.Min(0, iso - Math.Max(1, Math.Abs(iso) * 0.01));
                double[] values = new double[data.Count];
                for (int i = 0; i < values.Length; i++)
                {
                    double value = data.IsActive(i) ? Value(field, type, i) : outside;
                    values[i] = double.IsNaN(value) || double.IsInfinity(value) ? outside : value;
                }
                for (int pass = 0; pass < smoothing; pass++)
                {
                    var next = new double[values.Length];
                    for (int z = 0; z < field.ResZ; z++)
                    for (int y = 0; y < field.ResY; y++)
                    for (int x = 0; x < field.ResX; x++)
                    {
                        int i = data.FlatIndex(x, y, z);
                        if (!data.IsActive(i)) { next[i] = outside; continue; }
                        double sum = 0;
                        for (int dz = -1; dz <= 1; dz++)
                        for (int dy = -1; dy <= 1; dy++)
                        for (int dx = -1; dx <= 1; dx++)
                            sum += Sample(field, values, x + dx, y + dy, z + dz, outside);
                        next[i] = sum / 27;
                    }
                    values = next;
                }

                mesh = new Mesh();
                var points = new Point3d[8];
                var samples = new double[8];
                var inside = new int[4];
                var outIndices = new int[4];
                int activeCells = 0;
                for (int z = -1; z < field.ResZ; z++)
                for (int y = -1; y < field.ResY; y++)
                for (int x = -1; x < field.ResX; x++)
                {
                    int above = 0;
                    for (int c = 0; c < 8; c++)
                    {
                        int cx = x + Corners[c,0], cy = y + Corners[c,1], cz = z + Corners[c,2];
                        points[c] = new Point3d((cx + 0.5) * field.VoxelSize, (cy + 0.5) * field.VoxelSize, (cz + 0.5) * field.VoxelSize);
                        samples[c] = Sample(field, values, cx, cy, cz, outside);
                        if (samples[c] >= iso) above++;
                    }
                    if (above == 0 || above == 8) continue;
                    activeCells++;
                    for (int t = 0; t < 6; t++)
                    {
                        int ni = 0, no = 0;
                        for (int c = 0; c < 4; c++)
                        {
                            int v = Tetrahedra[t,c];
                            if (samples[v] >= iso) inside[ni++] = v;
                            else outIndices[no++] = v;
                        }
                        if (ni == 0 || ni == 4) continue;
                        if (mesh.Faces.Count + (ni == 2 ? 2 : 1) > limit)
                        {
                            result.Error = "The isosurface exceeds the Maximum Elements limit of " + limit.ToString("N0") + ". Increase the limit or use a higher iso value.";
                            return result;
                        }
                        Vector3d outward = points[outIndices[0]] - points[inside[0]];
                        if (ni == 1 || ni == 3)
                        {
                            int a = ni == 1 ? inside[0] : outIndices[0];
                            int[] other = ni == 1 ? outIndices : inside;
                            Add(mesh, Interpolate(a, other[0], points, samples, iso),
                                Interpolate(a, other[1], points, samples, iso),
                                Interpolate(a, other[2], points, samples, iso), outward);
                        }
                        else
                        {
                            Point3d a = Interpolate(inside[0], outIndices[0], points, samples, iso);
                            Point3d b = Interpolate(inside[0], outIndices[1], points, samples, iso);
                            Point3d c = Interpolate(inside[1], outIndices[0], points, samples, iso);
                            Point3d d = Interpolate(inside[1], outIndices[1], points, samples, iso);
                            Add(mesh, a, b, c, outward);
                            Add(mesh, b, d, c, outward);
                        }
                    }
                }
                if (mesh.Faces.Count == 0) { result.Error = "No surface crosses the requested iso value."; return result; }
                mesh.Vertices.CombineIdentical(true, true);
                mesh.Faces.CullDegenerateFaces();
                mesh.Vertices.CullUnused();
                mesh.UnifyNormals();
                if (smoothing > 0) mesh.Smooth(0.3, smoothing, true, true, true, true, SmoothingCoordinateSystem.World, Plane.WorldXY);
                mesh.Normals.ComputeNormals();
                mesh.Weld(Math.PI);
                mesh.Compact();
                result.Success = true;
                result.Mesh = mesh;
                result.TriangleCount = mesh.Faces.Count;
                result.ActiveCellCount = activeCells;
                result.Milliseconds = timer.Elapsed.TotalMilliseconds;
                mesh = null;
            }
            catch (Exception ex) { result.Error = "Voxel volume meshing failed: " + ex.Message; }
            finally { if (mesh != null) mesh.Dispose(); }
            return result;
        }

        static double Sample(VoxelField field, double[] values, int x, int y, int z, double outside)
        {
            return x < 0 || y < 0 || z < 0 || x >= field.ResX || y >= field.ResY || z >= field.ResZ
                ? outside : values[field.Data.FlatIndex(x, y, z)];
        }

        static Point3d Interpolate(int a, int b, Point3d[] points, double[] values, double iso)
        {
            // Always interpolate a shared edge in the same direction so welding is exact.
            if (points[a].CompareTo(points[b]) > 0) { int temp = a; a = b; b = temp; }
            return points[a] + (points[b] - points[a]) * ((iso - values[a]) / (values[b] - values[a]));
        }

        static void Add(Mesh mesh, Point3d a, Point3d b, Point3d c, Vector3d outward)
        {
            int start = mesh.Vertices.Count;
            mesh.Vertices.Add(a); mesh.Vertices.Add(b); mesh.Vertices.Add(c);
            if (Vector3d.Multiply(Vector3d.CrossProduct(b - a, c - a), outward) < 0)
                mesh.Faces.AddFace(start, start + 2, start + 1);
            else mesh.Faces.AddFace(start, start + 1, start + 2);
        }
    }
}
