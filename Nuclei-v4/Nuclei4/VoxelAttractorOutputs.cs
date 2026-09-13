using System;
using Grasshopper;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Data;
using Rhino.Geometry;

namespace Nuclei4
{
    internal static class VoxelAttractorOutputs
    {
        // Sum output fields across Grasshopper iterations without enumerating voxels.
        internal static void WriteCount(GH_Component component, ref long total, int activeCount)
        {
            total += activeCount;
            component.Message = "Voxels: " + total.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        // Distances needed for selection or branch ownership are independent of
        // the optional distance output. Inverted point/index outputs need no queries.
        public static void Write(IGH_DataAccess da, VoxelField field, int attractorCount,
            Func<int, Point3d, double> distanceTo, double minimum, double maximum, bool invert,
            bool useMinimum, bool useMaximum, bool useAverage,
            DataTree<Point3d> positions, DataTree<double> distances, DataTree<int> indices)
        {
            da.SetData(0, field);
            if (positions == null && distances == null && indices == null) return;
            var data = field.Data;
            for (int ordinal = 0; ordinal < data.ActiveCount; ordinal++)
            {
                Point3d center = Point3d.Unset;
                if (positions != null || distances != null || !invert)
                    center = data.CenterPoint(data.ActiveFlatIndexAt(ordinal));
                int owner = invert ? 0 : -1;
                double closest = double.PositiveInfinity, farthest = 0, sum = 0;
                int count = 0;
                if (!invert || distances != null)
                    for (int a = 0; a < attractorCount; a++)
                    {
                        double distance = distanceTo(a, center);
                        if (double.IsNaN(distance) || double.IsInfinity(distance) || distance < 0) continue;
                        if (!invert && !VoxelAttractorBand.Contains(data, data.ActiveFlatIndexAt(ordinal), distance,
                            point => distanceTo(a, point), minimum, maximum)) continue;
                        if (distance < closest) { closest = distance; if (!invert) owner = a; }
                        if (distances != null)
                        {
                            if (useMaximum && distance > farthest) farthest = distance;
                            if (useAverage) sum += distance;
                            count++;
                        }
                    }
                if (owner < 0) continue;
                var path = new GH_Path(owner);
                positions?.Add(center, path);
                indices?.Add(ordinal, path);
                if (distances != null && count > 0)
                {
                    if (useMinimum) distances.Add(closest, path);
                    if (useMaximum) distances.Add(farthest, path);
                    if (useAverage) distances.Add(sum / count, path);
                }
            }
            if (positions != null) da.SetDataTree(1, positions);
            if (distances != null) da.SetDataTree(2, distances);
            if (indices != null) da.SetDataTree(3, indices);
        }
    }
}
