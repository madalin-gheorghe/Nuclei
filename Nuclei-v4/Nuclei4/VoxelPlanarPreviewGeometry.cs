using System.Collections.Generic;

using Rhino.Geometry;

namespace Nuclei4
{
    internal static class VoxelPlanarPreviewGeometry
    {
        public static Point3d[] CreateBackgroundPolygon(int resX, int resY, int resZ, double voxelSize)
        {
            List<Point3d> points = new List<Point3d>(12);
            double dimX = resX * voxelSize;
            double dimY = resY * voxelSize;
            double dimZ = resZ * voxelSize;

            // Match the constructor's existing plane selection and point order,
            // including its behavior when more than one dimension is collapsed.
            if (resZ == 1)
            {
                points.Add(new Point3d(0, 0, 0));
                points.Add(new Point3d(dimX, 0, 0));
                points.Add(new Point3d(dimX, dimY, 0));
                points.Add(new Point3d(0, dimY, 0));
            }
            if (resY == 1)
            {
                points.Add(new Point3d(0, voxelSize, 0));
                points.Add(new Point3d(dimX, voxelSize, 0));
                points.Add(new Point3d(dimX, voxelSize, dimZ));
                points.Add(new Point3d(0, voxelSize, dimZ));
            }
            if (resX == 1)
            {
                points.Add(new Point3d(0, 0, 0));
                points.Add(new Point3d(0, dimY, 0));
                points.Add(new Point3d(0, dimY, dimZ));
                points.Add(new Point3d(0, 0, dimZ));
            }

            return points.ToArray();
        }
    }
}
