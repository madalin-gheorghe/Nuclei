using System;
using System.Collections.Generic;
using System.Diagnostics;

using Rhino.Display;

namespace Nuclei3
{
    internal static class NucleiGpuDisplayManager
    {
        static readonly object syncRoot = new object();
        static readonly Dictionary<Guid, SolverGPU> solvers = new Dictionary<Guid, SolverGPU>();
        static readonly Dictionary<Guid, Preview_Voxel> voxelDensityPreviews = new Dictionary<Guid, Preview_Voxel>();
        static readonly NucleiGpuDisplayConduit conduit = new NucleiGpuDisplayConduit();

        public static void RegisterSolver(SolverGPU solver)
        {
            if (solver == null) return;

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

        public static bool TryGetSolverForVoxels(Voxel[,,] voxels, out SolverGPU solver)
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
            lock (syncRoot)
            {
                Preview_Voxel[] snapshot = new Preview_Voxel[voxelDensityPreviews.Count];
                voxelDensityPreviews.Values.CopyTo(snapshot, 0);
                return snapshot;
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
                }
            }

            if (hasPlanarPreview)
            {
                ParticlePreviewDisplayConduit.DrawRegisteredPreviews(e);
            }
        }
    }
}
