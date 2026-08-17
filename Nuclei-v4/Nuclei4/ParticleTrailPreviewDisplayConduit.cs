using System;
using System.Collections.Generic;
using System.Drawing;

using Rhino.Display;
using Rhino.Geometry;

namespace Nuclei4
{
    internal sealed class ParticleTrailPreviewDisplayConduit : DisplayConduit
    {
        static readonly ParticleTrailPreviewDisplayConduit instance = new ParticleTrailPreviewDisplayConduit();

        readonly object syncRoot = new object();
        readonly Dictionary<Guid, Preview_Particle_Trails_GPU> previews = new Dictionary<Guid, Preview_Particle_Trails_GPU>();

        ParticleTrailPreviewDisplayConduit()
        {
        }

        public static void Register(Preview_Particle_Trails_GPU preview)
        {
            if (preview == null) return;
            instance.RegisterInternal(preview);
        }

        public static void Unregister(Guid id)
        {
            instance.UnregisterInternal(id);
        }

        void RegisterInternal(Preview_Particle_Trails_GPU preview)
        {
            lock (syncRoot)
            {
                previews[preview.InstanceGuid] = preview;
                Enabled = previews.Count > 0;
            }
        }

        void UnregisterInternal(Guid id)
        {
            lock (syncRoot)
            {
                previews.Remove(id);
                ParticleTrailD3DRenderer.Unregister(id);
                Enabled = previews.Count > 0;
            }
        }

        protected override void CalculateBoundingBox(CalculateBoundingBoxEventArgs e)
        {
            Preview_Particle_Trails_GPU[] snapshot = Snapshot();
            for (int i = 0; i < snapshot.Length; i++)
            {
                ParticleTrailPreviewDisplayFrame frame = snapshot[i].GetDisplayFrame();
                if (frame != null && frame.HasPoint)
                {
                    e.IncludeBoundingBox(frame.ClippingBox);
                }
            }
        }

        protected override void PostDrawObjects(DrawEventArgs e)
        {
            RhinoWipD3DPreviewProbe.TryWriteProbe(e.Display, e.Viewport);

            Preview_Particle_Trails_GPU[] snapshot = Snapshot();
            if (snapshot.Length == 0) return;

            bool drewBackground = false;
            for (int i = 0; i < snapshot.Length; i++)
            {
                ParticleTrailPreviewDisplayFrame frame = snapshot[i].GetDisplayFrame();
                if (frame == null || !frame.HasPoint)
                {
                    continue;
                }

                if (!Globals.tridimensional
                    && !drewBackground
                    && !NucleiGpuDisplayManager.HasActivePlanarVoxelDensityPreview())
                {
                    e.Display.DrawPolygon(Globals.bgPolygon, Color.Black, true);
                    drewBackground = true;
                }

                if (!ParticleTrailD3DRenderer.TryDraw(snapshot[i].InstanceGuid, e, frame)
                    && frame.CpuBatches != null)
                {
                    for (int batchIndex = 0; batchIndex < frame.CpuBatches.Length; batchIndex++)
                    {
                        CpuParticleTrailPreviewBatch batch = frame.CpuBatches[batchIndex];
                        if (batch != null && batch.Lines != null && batch.Lines.Length > 0)
                        {
                            e.Display.DrawLines(batch.Lines, batch.Color, 1);
                        }
                    }
                }
            }
        }

        Preview_Particle_Trails_GPU[] Snapshot()
        {
            lock (syncRoot)
            {
                Preview_Particle_Trails_GPU[] snapshot = new Preview_Particle_Trails_GPU[previews.Count];
                previews.Values.CopyTo(snapshot, 0);
                return snapshot;
            }
        }
    }

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
