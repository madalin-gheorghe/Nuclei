using System;
using System.Collections.Generic;
using System.Drawing;

using Grasshopper;
using Grasshopper.GUI.Canvas;
using Grasshopper.Kernel;
using Rhino.Display;

namespace Nuclei4
{
    internal sealed class ParticleTrailPreviewDisplayConduit : DisplayConduit
    {
        static readonly ParticleTrailPreviewDisplayConduit instance = new ParticleTrailPreviewDisplayConduit();

        readonly object syncRoot = new object();
        readonly Dictionary<Guid, Preview_Particle_Trails_GPU> previews = new Dictionary<Guid, Preview_Particle_Trails_GPU>();
        GH_Canvas subscribedCanvas;

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
                EnsureCanvasSubscription();
                GH_Document activeDocument = Instances.ActiveCanvas?.Document;
                List<Preview_Particle_Trails_GPU> activePreviews = new List<Preview_Particle_Trails_GPU>(previews.Count);
                List<Guid> invalidIds = null;

                foreach (KeyValuePair<Guid, Preview_Particle_Trails_GPU> entry in previews)
                {
                    Preview_Particle_Trails_GPU preview = entry.Value;
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
                        ParticleTrailD3DRenderer.Unregister(id);
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
                    foreach (KeyValuePair<Guid, Preview_Particle_Trails_GPU> entry in previews)
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
                            ParticleTrailD3DRenderer.Unregister(entry.Key);
                        }
                    }
                }

                Enabled = previews.Count > 0;
            }

            Rhino.RhinoDoc.ActiveDoc?.Views.Redraw();
        }
    }
}
