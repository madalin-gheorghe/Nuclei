using System;
using System.Collections.Generic;
using System.Diagnostics;

using Rhino.Display;

namespace Nuclei4
{
    internal static class NucleiGpuDisplayManager
    {
        static readonly object syncRoot = new object();
        static readonly Dictionary<Guid, SolverGPU> solvers = new Dictionary<Guid, SolverGPU>();
        static readonly Dictionary<Guid, Preview_Voxel> voxelDensityPreviews = new Dictionary<Guid, Preview_Voxel>();
        static readonly NucleiGpuDisplayConduit conduit = new NucleiGpuDisplayConduit();
        static Grasshopper.GUI.Canvas.GH_Canvas subscribedCanvas;

        static NucleiGpuDisplayManager()
        {
            EnsureCanvasDocumentChangedSubscription();
        }

        public static void RegisterSolver(SolverGPU solver)
        {
            if (solver == null) return;

            EnsureCanvasDocumentChangedSubscription();

            lock (syncRoot)
            {
                solvers[solver.InstanceGuid] = solver;
            }
        }

        public static void UnregisterSolver(Guid solverId)
        {
            lock (syncRoot)
            {
                solvers.Remove(solverId);
            }
        }

        public static bool TryGetSolverForVoxels(VoxelField voxels, out SolverGPU solver)
        {
            solver = null;
            if (voxels == null) return false;

            SolverGPU[] snapshot = SnapshotSolvers();
            for (int i = 0; i < snapshot.Length; i++)
            {
                SolverGPU candidate = snapshot[i];
                if (candidate != null && ReferenceEquals(candidate.OutputVoxels, voxels))
                {
                    solver = candidate;
                    return true;
                }
            }

            return false;
        }

        public static void SetVoxelDensityPreview(Preview_Voxel preview)
        {
            if (preview == null) return;

            EnsureCanvasDocumentChangedSubscription();

            lock (syncRoot)
            {
                voxelDensityPreviews[preview.InstanceGuid] = preview;
                conduit.Enabled = voxelDensityPreviews.Count > 0;
            }
        }

        public static void DisableVoxelDensityPreview(Guid previewId)
        {
            lock (syncRoot)
            {
                voxelDensityPreviews.Remove(previewId);
                GpuDensityFieldD3DRenderer.Unregister(previewId);
                conduit.Enabled = voxelDensityPreviews.Count > 0;
            }
        }

        static SolverGPU[] SnapshotSolvers()
        {
            lock (syncRoot)
            {
                SolverGPU[] snapshot = new SolverGPU[solvers.Count];
                solvers.Values.CopyTo(snapshot, 0);
                return snapshot;
            }
        }

        internal static Preview_Voxel[] SnapshotVoxelDensityPreviews()
        {
            EnsureCanvasDocumentChangedSubscription();

            Preview_Voxel[] registeredPreviews;
            lock (syncRoot)
            {
                registeredPreviews = new Preview_Voxel[voxelDensityPreviews.Count];
                voxelDensityPreviews.Values.CopyTo(registeredPreviews, 0);
            }

            Grasshopper.Kernel.GH_Document activeDocument = ActiveCanvasDocument();
            if (activeDocument == null || !activeDocument.Enabled)
            {
                return Array.Empty<Preview_Voxel>();
            }

            List<Preview_Voxel> activePreviews = new List<Preview_Voxel>(registeredPreviews.Length);
            for (int i = 0; i < registeredPreviews.Length; i++)
            {
                Preview_Voxel preview = registeredPreviews[i];
                if (IsPreviewInDocument(preview, activeDocument, true))
                {
                    activePreviews.Add(preview);
                }
            }

            return activePreviews.ToArray();
        }

        static void EnsureCanvasDocumentChangedSubscription()
        {
            Grasshopper.GUI.Canvas.GH_Canvas canvas;
            try
            {
                canvas = Grasshopper.Instances.ActiveCanvas;
            }
            catch
            {
                return;
            }

            if (canvas == null) return;

            lock (syncRoot)
            {
                if (ReferenceEquals(subscribedCanvas, canvas)) return;

                if (subscribedCanvas != null)
                {
                    subscribedCanvas.DocumentChanged -= CanvasDocumentChanged;
                }

                canvas.DocumentChanged += CanvasDocumentChanged;
                subscribedCanvas = canvas;
            }
        }

        static void CanvasDocumentChanged(
            Grasshopper.GUI.Canvas.GH_Canvas sender,
            Grasshopper.GUI.Canvas.GH_CanvasDocumentChangedEventArgs e)
        {
            Grasshopper.Kernel.GH_Document oldDocument = e == null ? null : e.OldDocument;
            if (oldDocument != null)
            {
                Preview_Voxel[] registeredPreviews;
                lock (syncRoot)
                {
                    registeredPreviews = new Preview_Voxel[voxelDensityPreviews.Count];
                    voxelDensityPreviews.Values.CopyTo(registeredPreviews, 0);
                }

                for (int i = 0; i < registeredPreviews.Length; i++)
                {
                    Preview_Voxel preview = registeredPreviews[i];
                    if (IsPreviewInDocument(preview, oldDocument, false))
                    {
                        GpuDensityFieldD3DRenderer.Unregister(preview.InstanceGuid);
                    }
                }
            }

            RequestRhinoViewportRedraw();
        }

        static Grasshopper.Kernel.GH_Document ActiveCanvasDocument()
        {
            try
            {
                Grasshopper.GUI.Canvas.GH_Canvas canvas = Grasshopper.Instances.ActiveCanvas;
                return canvas == null ? null : canvas.Document;
            }
            catch
            {
                return null;
            }
        }

        static bool IsPreviewInDocument(
            Preview_Voxel preview,
            Grasshopper.Kernel.GH_Document document,
            bool requireEnabled)
        {
            if (preview == null || document == null) return false;

            try
            {
                Grasshopper.Kernel.GH_Document previewDocument = preview.OnPingDocument();
                return ReferenceEquals(previewDocument, document)
                    && (!requireEnabled || previewDocument.Enabled);
            }
            catch
            {
                return false;
            }
        }

        static void RequestRhinoViewportRedraw()
        {
            try
            {
                Rhino.RhinoDoc document = Rhino.RhinoDoc.ActiveDoc;
                if (document != null)
                {
                    document.Views.Redraw();
                }
            }
            catch
            {
            }
        }

        internal static bool HasActivePlanarVoxelDensityPreview()
        {
            Preview_Voxel[] previews = SnapshotVoxelDensityPreviews();
            for (int i = 0; i < previews.Length; i++)
            {
                GpuDensityFieldPreviewFrame frame = previews[i].GetGpuDensityFieldPreviewFrame();
                if (IsPlanarSimulationFrame(frame))
                {
                    return true;
                }
            }

            return false;
        }

        internal static bool IsPlanarSimulationFrame(GpuDensityFieldPreviewFrame frame)
        {
            return frame != null
                && frame.IsValid
                && (frame.ResX == 1 || frame.ResY == 1 || frame.ResZ == 1);
        }
    }

    internal sealed class NucleiGpuDisplayConduit : DisplayConduit
    {
        protected override void CalculateBoundingBox(CalculateBoundingBoxEventArgs e)
        {
            Preview_Voxel[] previews = NucleiGpuDisplayManager.SnapshotVoxelDensityPreviews();
            for (int i = 0; i < previews.Length; i++)
            {
                GpuDensityFieldPreviewFrame frame = previews[i].GetGpuDensityFieldPreviewFrame();
                if (frame != null && frame.IsValid)
                {
                    e.IncludeBoundingBox(frame.ClippingBox);
                }
            }
        }

        protected override void PostDrawObjects(DrawEventArgs e)
        {
            RhinoWipD3DPreviewProbe.TryWriteProbe(e.Display, e.Viewport);

            Preview_Voxel[] previews = NucleiGpuDisplayManager.SnapshotVoxelDensityPreviews();
            if (previews.Length == 0) return;

            bool hasPlanarPreview = false;
            for (int i = 0; i < previews.Length; i++)
            {
                Preview_Voxel preview = previews[i];
                GpuDensityFieldPreviewFrame frame = preview.GetGpuDensityFieldPreviewFrame();
                if (frame == null || !frame.IsValid)
                {
                    continue;
                }

                hasPlanarPreview |= NucleiGpuDisplayManager.IsPlanarSimulationFrame(frame);

                long drawStart = Stopwatch.GetTimestamp();
                if (GpuDensityFieldD3DRenderer.TryDraw(preview.InstanceGuid, e, frame))
                {
                    preview.RecordGpuDensityFieldPreviewDrawTiming(Stopwatch.GetTimestamp() - drawStart);
                    if (frame.FancyRender && GpuDensityFieldD3DRenderer.NeedsFancyRefinement(preview.InstanceGuid))
                    {
                        e.Display.Viewport.ParentView.Redraw();
                    }
                }
            }

            if (hasPlanarPreview)
            {
                ParticlePreviewDisplayConduit.DrawRegisteredPreviews(e);
            }
        }
    }
}
