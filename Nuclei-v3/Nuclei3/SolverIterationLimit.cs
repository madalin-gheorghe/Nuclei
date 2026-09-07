using Grasshopper.Kernel;
using Grasshopper.Kernel.Special;

namespace Nuclei3
{
    internal static class SolverIterationLimit
    {
        internal static void PauseDedicatedTimers(GH_Component solver)
        {
            GH_Document document = solver?.OnPingDocument();
            if (document == null) return;

            foreach (IGH_DocumentObject documentObject in document.Objects)
            {
                GH_Timer timer = documentObject as GH_Timer;
                if (timer == null || timer.Locked || timer.Manual)
                {
                    continue;
                }

                bool targetsSolver = false;
                bool targetsAnotherObject = false;
                foreach (System.Guid target in timer.Targets)
                {
                    // Saved triggers can retain IDs of deleted components. GH
                    // ignores those when ticking, so they are not shared targets.
                    IGH_DocumentObject targetObject = document.FindObject(target, true);
                    if (targetObject == null) continue;
                    if (ReferenceEquals(targetObject, solver)) targetsSolver = true;
                    else targetsAnotherObject = true;
                }

                if (targetsSolver && !targetsAnotherObject)
                {
                    // Re-enable this timer manually to start another run.
                    timer.Locked = true;
                }
            }
        }
    }
}
