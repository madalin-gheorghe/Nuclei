using System;
using System.Collections.Generic;
using System.Diagnostics;
using Grasshopper;
using Grasshopper.GUI.Canvas;
using Grasshopper.Kernel;
using Rhino.Display;

namespace Nuclei4
{
    internal sealed class ParticlePreviewDisplayConduit : DisplayConduit
    {
        static readonly ParticlePreviewDisplayConduit instance = new ParticlePreviewDisplayConduit();

        readonly object syncRoot = new object();
        readonly Dictionary<Guid, Preview_Particle> previews = new Dictionary<Guid, Preview_Particle>();
        GH_Canvas subscribedCanvas;

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
                EnsureCanvasSubscription();
                previews[preview.InstanceGuid] = preview;
                Enabled = previews.Count > 0;
            }
        }

        void UnregisterInternal(Guid id)
        {
            lock (syncRoot)
            {
                EnsureCanvasSubscription();
                previews.Remove(id);
                ParticlePreviewD3DRenderer.Unregister(id);
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
            if (NucleiGpuDisplayManager.HasActivePlanarVoxelDensityPreview())
            {
                return;
            }

            RhinoWipD3DPreviewProbe.TryWriteProbe(e.Display, e.Viewport);
            DrawRegisteredPreviews(e);
        }

        internal static void DrawRegisteredPreviews(DrawEventArgs e)
        {
            instance.DrawRegisteredPreviewsInternal(e);
        }

        void DrawRegisteredPreviewsInternal(DrawEventArgs e)
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

                if (ParticlePreviewD3DRenderer.TryDraw(preview.InstanceGuid, e, frame))
                {
                    drew = true;
                }
                else
                {
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
                EnsureCanvasSubscription();
                GH_Document activeDocument = Instances.ActiveCanvas?.Document;
                List<Preview_Particle> activePreviews = new List<Preview_Particle>(previews.Count);
                List<Guid> invalidIds = null;

                foreach (KeyValuePair<Guid, Preview_Particle> entry in previews)
                {
                    Preview_Particle preview = entry.Value;
                    GH_Document ownerDocument;
                    try
                    {
                        ownerDocument = preview?.OnPingDocument();
                    }
                    catch
                    {
                        ownerDocument = null;
                    }

                    if (ownerDocument == null)
                    {
                        if (invalidIds == null) invalidIds = new List<Guid>();
                        invalidIds.Add(entry.Key);
                        continue;
                    }

                    if (ownerDocument.Enabled && ReferenceEquals(ownerDocument, activeDocument))
                    {
                        activePreviews.Add(preview);
                    }
                }

                if (invalidIds != null)
                {
                    for (int i = 0; i < invalidIds.Count; i++)
                    {
                        Guid id = invalidIds[i];
                        previews.Remove(id);
                        ParticlePreviewD3DRenderer.Unregister(id);
                    }

                    Enabled = previews.Count > 0;
                }

                return activePreviews.ToArray();
            }
        }

        void EnsureCanvasSubscription()
        {
            GH_Canvas activeCanvas = Instances.ActiveCanvas;
            if (ReferenceEquals(activeCanvas, subscribedCanvas)) return;

            if (subscribedCanvas != null)
            {
                subscribedCanvas.DocumentChanged -= ActiveCanvasDocumentChanged;
            }

            subscribedCanvas = activeCanvas;
            if (subscribedCanvas != null)
            {
                subscribedCanvas.DocumentChanged += ActiveCanvasDocumentChanged;
            }
        }

        void ActiveCanvasDocumentChanged(GH_Canvas sender, GH_CanvasDocumentChangedEventArgs e)
        {
            lock (syncRoot)
            {
                GH_Document oldDocument = e?.OldDocument;
                if (oldDocument != null)
                {
                    foreach (KeyValuePair<Guid, Preview_Particle> entry in previews)
                    {
                        GH_Document ownerDocument;
                        try
                        {
                            ownerDocument = entry.Value?.OnPingDocument();
                        }
                        catch
                        {
                            ownerDocument = null;
                        }

                        if (ReferenceEquals(ownerDocument, oldDocument))
                        {
                            ParticlePreviewD3DRenderer.Unregister(entry.Key);
                        }
                    }
                }

                Enabled = previews.Count > 0;
            }

            Rhino.RhinoDoc.ActiveDoc?.Views.Redraw();
        }
    }
}
