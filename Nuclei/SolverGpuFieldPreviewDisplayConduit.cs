using System;
using System.Collections.Generic;
using System.Diagnostics;

using Rhino.Display;

namespace Nuclei3
{
    internal sealed class SolverGpuFieldPreviewDisplayConduit : DisplayConduit
    {
        static readonly SolverGpuFieldPreviewDisplayConduit instance = new SolverGpuFieldPreviewDisplayConduit();

        readonly object syncRoot = new object();
        readonly Dictionary<Guid, SolverGPU> solvers = new Dictionary<Guid, SolverGPU>();

        SolverGpuFieldPreviewDisplayConduit()
        {
        }

        public static void Register(SolverGPU solver)
        {
            if (solver == null) return;
            instance.RegisterInternal(solver);
        }

        public static void Unregister(Guid id)
        {
            instance.UnregisterInternal(id);
        }

        void RegisterInternal(SolverGPU solver)
        {
            lock (syncRoot)
            {
                solvers[solver.InstanceGuid] = solver;
                Enabled = solvers.Count > 0;
            }
        }

        void UnregisterInternal(Guid id)
        {
            lock (syncRoot)
            {
                solvers.Remove(id);
                GpuDensityFieldD3DRenderer.Unregister(id);
                Enabled = solvers.Count > 0;
            }
        }

        protected override void CalculateBoundingBox(CalculateBoundingBoxEventArgs e)
        {
            SolverGPU[] snapshot = Snapshot();
            for (int i = 0; i < snapshot.Length; i++)
            {
                GpuDensityFieldPreviewFrame frame = snapshot[i].GetDensityFieldPreviewFrame();
                if (frame != null && frame.IsValid)
                {
                    e.IncludeBoundingBox(frame.ClippingBox);
                }
            }
        }

        protected override void PostDrawObjects(DrawEventArgs e)
        {
            RhinoWipD3DPreviewProbe.TryWriteProbe(e.Display, e.Viewport);

            SolverGPU[] snapshot = Snapshot();
            if (snapshot.Length == 0) return;

            for (int i = 0; i < snapshot.Length; i++)
            {
                SolverGPU solver = snapshot[i];
                GpuDensityFieldPreviewFrame frame = solver.GetDensityFieldPreviewFrame();
                if (frame == null || !frame.IsValid)
                {
                    continue;
                }

                long drawStart = Stopwatch.GetTimestamp();
                if (GpuDensityFieldD3DRenderer.TryDraw(solver.InstanceGuid, e, frame))
                {
                    solver.RecordDensityFieldPreviewDrawTiming(Stopwatch.GetTimestamp() - drawStart);
                }
            }
        }

        SolverGPU[] Snapshot()
        {
            lock (syncRoot)
            {
                SolverGPU[] snapshot = new SolverGPU[solvers.Count];
                solvers.Values.CopyTo(snapshot, 0);
                return snapshot;
            }
        }
    }
}
