using System;

using Rhino.Geometry;

namespace Nuclei3
{
    internal sealed class GpuDensityFieldPreviewFrame
    {
        public IntPtr SharedHandle;
        public int Width;
        public int Height;
        public int ResX;
        public int ResY;
        public int ResZ;
        public int AxisMode;
        public int Slice;
        public int AtlasColumns = 1;
        public int AtlasRows = 1;
        public float VoxelSize;
        public long Version;
        public int ValueIndex = VoxelPreviewField.SlimeChemoattractants;
        public float MinimumThreshold = 0;
        public float MaximumThreshold = float.MaxValue;
        public float PreviewScale = 1.35f;
        public float VolumeOpacity = 1.5f;
        public float VolumeContrast = 1.5f;
        public int VolumeSampleCount = 0;
        public int VolumeRenderMode = 0;
        public float ColorR = 0;
        public float ColorG = 0;
        public float ColorB = 0;
        public float ColorA = 0;
        public bool UseCustomColor;

        public bool IsValid
        {
            get
            {
                return SharedHandle != IntPtr.Zero && Width > 0 && Height > 0 && ResX > 0 && ResY > 0 && ResZ > 0 && VoxelSize > 0;
            }
        }

        public BoundingBox ClippingBox
        {
            get
            {
                if (!IsValid) return BoundingBox.Empty;

                double dimX = ResX * VoxelSize;
                double dimY = ResY * VoxelSize;
                double dimZ = ResZ * VoxelSize;
                double thickness = Math.Max(VoxelSize * 0.5, 0.001);

                if (VolumeMode)
                {
                    return new BoundingBox(new Point3d(0, 0, 0), new Point3d(dimX, dimY, dimZ));
                }

                if (AxisMode == 1)
                {
                    double y = ResY > 1 ? (Slice + 0.5) * VoxelSize : 0;
                    return new BoundingBox(new Point3d(0, y - thickness, 0), new Point3d(dimX, y + thickness, dimZ));
                }

                if (AxisMode == 2)
                {
                    double x = ResX > 1 ? (Slice + 0.5) * VoxelSize : 0;
                    return new BoundingBox(new Point3d(x - thickness, 0, 0), new Point3d(x + thickness, dimY, dimZ));
                }

                double z = ResZ > 1 ? (Slice + 0.5) * VoxelSize : 0;
                return new BoundingBox(new Point3d(0, 0, z - thickness), new Point3d(dimX, dimY, z + thickness));
            }
        }

        public bool VolumeMode
        {
            get { return AxisMode == 3 && ResX > 1 && ResY > 1 && ResZ > 1 && AtlasColumns > 0 && AtlasRows > 0; }
        }

        public Point3d Origin
        {
            get
            {
                if (AxisMode == 1)
                {
                    double y = ResY > 1 ? (Slice + 0.5) * VoxelSize : 0;
                    return new Point3d(0, y, 0);
                }

                if (AxisMode == 2)
                {
                    double x = ResX > 1 ? (Slice + 0.5) * VoxelSize : 0;
                    return new Point3d(x, 0, 0);
                }

                double z = ResZ > 1 ? (Slice + 0.5) * VoxelSize : 0;
                return new Point3d(0, 0, z);
            }
        }

        public Vector3d AxisU
        {
            get
            {
                if (AxisMode == 2) return new Vector3d(0, ResY * VoxelSize, 0);
                return new Vector3d(ResX * VoxelSize, 0, 0);
            }
        }

        public Vector3d AxisV
        {
            get
            {
                if (AxisMode == 0) return new Vector3d(0, ResY * VoxelSize, 0);
                return new Vector3d(0, 0, ResZ * VoxelSize);
            }
        }
    }
}
