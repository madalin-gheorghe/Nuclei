using System;
using Rhino.Geometry;

namespace Nuclei4
{
    internal static class VoxelAttractorBand
    {
        public static double SearchMaximum(double minimum, double maximum, double voxelSize) =>
            NeedsPairs(minimum, maximum, voxelSize) ? maximum + voxelSize : maximum;

        public static bool NeedsPairs(double minimum, double maximum, double voxelSize) =>
            minimum > 0 && maximum - minimum < 2 * voxelSize;

        public static bool Contains(VoxelGridData data, int flatIndex, double distance,
            Func<Point3d, double> distanceTo, double minimum, double maximum)
        {
            if (!Finite(distance)) return false;
            if (distance >= minimum && distance <= maximum) return true;
            if (!NeedsPairs(minimum, maximum, data.VoxelSize)) return false;
            double middle = minimum + (maximum - minimum) / 2;
            // True geometry distance is 1-Lipschitz: no adjacent center can cross
            // the middle farther than one voxel away. Saves six geometry queries.
            if (Math.Abs(distance - middle) > data.VoxelSize * (1 + 1e-10)) return false;
            int x, y, z;
            data.CoordinatesFromFlatIndex(flatIndex, out x, out y, out z);
            return Crosses(data, x - 1, y, z, distance, middle, distanceTo) ||
                Crosses(data, x + 1, y, z, distance, middle, distanceTo) ||
                Crosses(data, x, y - 1, z, distance, middle, distanceTo) ||
                Crosses(data, x, y + 1, z, distance, middle, distanceTo) ||
                Crosses(data, x, y, z - 1, distance, middle, distanceTo) ||
                Crosses(data, x, y, z + 1, distance, middle, distanceTo);
        }
        static bool Crosses(VoxelGridData data, int x, int y, int z, double distance, double middle, Func<Point3d, double> distanceTo)
        {
            if (x < 0 || y < 0 || z < 0 || x >= data.ResX || y >= data.ResY || z >= data.ResZ) return false;
            int flat = data.FlatIndex(x, y, z);
            if (!data.IsActive(flat)) return false;
            double other = distanceTo(data.CenterPoint(flat));
            return Finite(other) && ((distance <= middle && other >= middle) || (distance >= middle && other <= middle));
        }
        static bool Finite(double value) => !double.IsNaN(value) && !double.IsInfinity(value) && value >= 0;
    }
}
