using System;

using Rhino.Geometry;

namespace Nuclei4
{
    internal sealed class GpuParticlePreviewFrame
    {
        public IntPtr SharedHandle;
        public GpuTextureHandleDescriptor NativeTextureDescriptor;
        public int TextureWidth;
        public int TextureHeight;
        public int ParticleCount;
        public int ResX;
        public int ResY;
        public int ResZ;
        public float VoxelSize;
        public long Version;

        public GpuTextureHandleDescriptor TextureDescriptor
        {
            get
            {
                return NativeTextureDescriptor.IsValid
                    ? NativeTextureDescriptor
                    : GpuTextureHandleDescriptor.Direct3D11SharedTexture(SharedHandle);
            }
        }

        public bool IsValid
        {
            get
            {
                return TextureDescriptor.IsValid
                    && TextureWidth > 0
                    && TextureHeight > 1
                    && ParticleCount > 0
                    && ResX > 0
                    && ResY > 0
                    && ResZ > 0
                    && VoxelSize > 0;
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
                BoundingBox box = new BoundingBox(new Point3d(0, 0, 0), new Point3d(dimX, dimY, dimZ));
                box.Inflate(Math.Max(VoxelSize, 1.0));
                return box;
            }
        }
    }
}
