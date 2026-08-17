using System.Drawing;

using Rhino.Geometry;

namespace Nuclei4
{
    internal sealed class ParticleTrailPreviewDisplayFrame
    {
        public GpuParticleTrailPreviewFrame GpuFrame;
        public CpuParticleTrailPreviewBatch[] CpuBatches;
        public Color FreshColor;
        public Color OldColor;
        public Color[] FreshColors;
        public Color[] OldColors;
        public double Alpha;
        public double FadePower;
        public double DepthFocus;
        public BoundingBox ClippingBox;
        public bool HasPoint;
    }

    internal sealed class CpuParticleTrailPreviewFrame
    {
        public CpuParticleTrailPreviewBatch[] Batches;
        public BoundingBox ClippingBox;
        public int SegmentCount;

        public bool IsValid
        {
            get
            {
                return Batches != null
                    && Batches.Length > 0
                    && SegmentCount > 0
                    && ClippingBox.IsValid;
            }
        }
    }

    internal sealed class CpuParticleTrailPreviewBatch
    {
        public Line[] Lines;
        public Color Color;
    }
}
