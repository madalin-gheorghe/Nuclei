using System;

using Rhino.Geometry;

namespace Nuclei4
{
    internal sealed class GpuDensityFieldPreviewFrame
    {
        public IntPtr SharedHandle;
        public IntPtr GradientSharedHandle;
        public int Width;
        public int Height;
        public int ResX;
        public int ResY;
        public int ResZ;
        public int SourceResX;
        public int SourceResY;
        public int SourceResZ;
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
        public float VolumeOpacity = 0.8f;
        public float VolumeContrast = 1.5f;
        public int VolumeSampleCount = 0;
        public int VolumeRendererVersion = 1;
        public bool FancyRender;
        public float ColorR = 0;
        public float ColorG = 0;
        public float ColorB = 0;
        public float ColorA = 0;
        public bool UseCustomColor;

        public bool ColorTexture
        {
            get { return VoxelPreviewField.IsDynamicDensity(ValueIndex); }
        }

        public bool HasGradientTexture
        {
            get { return GradientSharedHandle != IntPtr.Zero; }
        }

        public bool IsValid
        {
            get
            {
                return SharedHandle != IntPtr.Zero && Width > 0 && Height > 0 && ResX > 0 && ResY > 0 && ResZ > 0 && VoxelSize > 0;
            }
        }

        public int DomainResX
        {
            get { return SourceResX > 0 ? SourceResX : ResX; }
        }

        public int DomainResY
        {
            get { return SourceResY > 0 ? SourceResY : ResY; }
        }

        public int DomainResZ
        {
            get { return SourceResZ > 0 ? SourceResZ : ResZ; }
        }

        public BoundingBox ClippingBox
        {
            get
            {
                if (!IsValid) return BoundingBox.Empty;

                double dimX = DomainResX * VoxelSize;
                double dimY = DomainResY * VoxelSize;
                double dimZ = DomainResZ * VoxelSize;
                double thickness = Math.Max(VoxelSize * 0.5, 0.001);

                if (VolumeMode)
                {
                    return new BoundingBox(new Point3d(0, 0, 0), new Point3d(dimX, dimY, dimZ));
                }

                if (AxisMode == 1)
                {
                    double y = DomainResY > 1 ? (Slice + 0.5) * VoxelSize : 0;
                    return new BoundingBox(new Point3d(0, y - thickness, 0), new Point3d(dimX, y + thickness, dimZ));
                }

                if (AxisMode == 2)
                {
                    double x = DomainResX > 1 ? (Slice + 0.5) * VoxelSize : 0;
                    return new BoundingBox(new Point3d(x - thickness, 0, 0), new Point3d(x + thickness, dimY, dimZ));
                }

                double z = DomainResZ > 1 ? (Slice + 0.5) * VoxelSize : 0;
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
                    double y = DomainResY > 1 ? (Slice + 0.5) * VoxelSize : 0;
                    return new Point3d(0, y, 0);
                }

                if (AxisMode == 2)
                {
                    double x = DomainResX > 1 ? (Slice + 0.5) * VoxelSize : 0;
                    return new Point3d(x, 0, 0);
                }

                double z = DomainResZ > 1 ? (Slice + 0.5) * VoxelSize : 0;
                return new Point3d(0, 0, z);
            }
        }

        public Vector3d AxisU
        {
            get
            {
                if (AxisMode == 2) return new Vector3d(0, DomainResY * VoxelSize, 0);
                return new Vector3d(DomainResX * VoxelSize, 0, 0);
            }
        }

        public Vector3d AxisV
        {
            get
            {
                if (AxisMode == 0) return new Vector3d(0, DomainResY * VoxelSize, 0);
                return new Vector3d(0, 0, DomainResZ * VoxelSize);
            }
        }
    }
}
