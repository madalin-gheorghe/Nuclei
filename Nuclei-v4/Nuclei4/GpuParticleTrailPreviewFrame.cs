using System;

using Rhino.Geometry;

namespace Nuclei4
{
    internal sealed class GpuParticleTrailPreviewFrame
    {
        public IntPtr SharedHandle;
        public int TextureWidth;
        public int TextureHeight;
        public int ParticleCount;
        public int TrailSize;
        public int ValidTrailCount;
        public int HeadIndex;
        public int ResX;
        public int ResY;
        public int ResZ;
        public float VoxelSize;
        public int GroupCount;
        public float[] GroupColorData;
        public long Version;

        public bool IsValid
        {
            get
            {
                return SharedHandle != IntPtr.Zero
                    && TextureWidth > 0
                    && TextureHeight > 0
                    && ParticleCount > 0
                    && TrailSize > 1
                    && ValidTrailCount > 1
                    && HeadIndex >= 0
                    && HeadIndex < TrailSize
                    && ResX > 0
                    && ResY > 0
                    && ResZ > 0
                    && VoxelSize > 0;
            }
        }

        public int SegmentCount
        {
            get { return IsValid ? ParticleCount * (ValidTrailCount - 1) : 0; }
        }

        public BoundingBox ClippingBox
        {
            get
            {
                if (!IsValid) return BoundingBox.Empty;

                double dimX = ResX * VoxelSize;
                double dimY = ResY * VoxelSize;
                double dimZ = ResZ * VoxelSize;
                BoundingBox box = new BoundingBox(new Point3d(0, 0, 0), new Point3d(dimX, dimY, dimZ));
                box.Inflate(Math.Max(VoxelSize, 1.0));
                return box;
            }
        }
    }
}
