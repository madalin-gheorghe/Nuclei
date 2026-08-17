using Rhino.Geometry;

namespace Nuclei4
{
    internal sealed class ParticlePreviewDisplayFrame
    {
        public GpuParticlePreviewFrame GpuFrame;
        public PointCloud SlimePointCloud;
        public PointCloud AntPointCloud1;
        public PointCloud AntPointCloud2;
        public BoundingBox ClippingBox;
        public double PointSize;
        public bool HasPoint;
    }
}
