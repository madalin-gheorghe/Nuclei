using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;

using Rhino.Display;
using Rhino.Geometry;

namespace Nuclei3
{
    internal sealed class ParticlePreviewDisplayConduit : DisplayConduit
    {
        static readonly ParticlePreviewDisplayConduit instance = new ParticlePreviewDisplayConduit();

        readonly object syncRoot = new object();
        readonly Dictionary<Guid, Preview_Particle> previews = new Dictionary<Guid, Preview_Particle>();

        ParticlePreviewDisplayConduit()
        {
        }

        public static void Register(Preview_Particle preview)
        {
            if (preview == null) return;
            instance.RegisterInternal(preview);
        }

        public static void Unregister(Guid id)
        {
            instance.UnregisterInternal(id);
        }

        void RegisterInternal(Preview_Particle preview)
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
                Enabled = previews.Count > 0;
            }
        }

        protected override void CalculateBoundingBox(CalculateBoundingBoxEventArgs e)
        {
            Preview_Particle[] snapshot = Snapshot();
            for (int i = 0; i < snapshot.Length; i++)
            {
                ParticlePreviewDisplayFrame frame = snapshot[i].GetDisplayFrame(false);
                if (frame != null && frame.HasPoint)
                {
                    e.IncludeBoundingBox(frame.ClippingBox);
                }
            }
        }

        protected override void PostDrawObjects(DrawEventArgs e)
        {
            Preview_Particle[] snapshot = Snapshot();
            if (snapshot.Length == 0) return;

            for (int i = 0; i < snapshot.Length; i++)
            {
                Preview_Particle preview = snapshot[i];
                long drawStart = Stopwatch.GetTimestamp();
                bool drew = false;

                ParticlePreviewDisplayFrame frame = preview.GetDisplayFrame(true);
                if (frame == null)
                {
                    continue;
                }

                if (frame.SlimePointCloud != null && frame.SlimePointCloud.Count > 0)
                {
                    e.Display.DrawPointCloud(frame.SlimePointCloud, (float)frame.PointSize);
                    drew = true;
                }

                if (frame.AntPointCloud1 != null && frame.AntPointCloud1.Count > 0)
                {
                    e.Display.DrawPointCloud(frame.AntPointCloud1, (float)frame.PointSize);
                    drew = true;
                }

                if (frame.AntPointCloud2 != null && frame.AntPointCloud2.Count > 0)
                {
                    e.Display.DrawPointCloud(frame.AntPointCloud2, (float)(frame.PointSize * 1.5));
                    drew = true;
                }

                if (drew)
                {
                    preview.RecordConduitDrawTiming(Stopwatch.GetTimestamp() - drawStart);
                }
            }
        }

        Preview_Particle[] Snapshot()
        {
            lock (syncRoot)
            {
                Preview_Particle[] snapshot = new Preview_Particle[previews.Count];
                previews.Values.CopyTo(snapshot, 0);
                return snapshot;
            }
        }
    }

    internal sealed class ParticlePreviewDisplayFrame
    {
        public PointCloud SlimePointCloud;
        public PointCloud AntPointCloud1;
        public PointCloud AntPointCloud2;
        public BoundingBox ClippingBox;
        public double PointSize;
        public bool HasPoint;
    }
}
