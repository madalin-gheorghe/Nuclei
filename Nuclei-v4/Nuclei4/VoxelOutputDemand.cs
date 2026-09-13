using Grasshopper.Kernel;

namespace Nuclei4
{
    // Wiring expires the recipient, not necessarily its already-solved source.
    // Refresh at the start of that normal solution, before any data collection.
    // No timer, recursive solution, or work on unchanged demand is needed.
    internal sealed class VoxelOutputDemand
    {
        readonly GH_Component component;
        GH_Document document;
        int previousMask;

        public VoxelOutputDemand(GH_Component component) { this.component = component; }

        public void Attach(GH_Document target)
        {
            Detach();
            document = target;
            previousMask = DemandMask();
            if (document != null) document.SolutionStart += SolutionStarting;
        }

        public void Detach()
        {
            if (document != null) document.SolutionStart -= SolutionStarting;
            document = null;
        }

        void SolutionStarting(object sender, GH_SolutionEventArgs args)
        {
            int mask = DemandMask();
            if (mask == previousMask) return;
            previousMask = mask;
            // Recompute only when a lazy output gains its first consumer or
            // loses its last. Extra consumers reuse the already populated output.
            component.ExpireSolution(false);
        }

        int DemandMask()
        {
            int mask = 0;
            for (int index = 1; index < component.Params.Output.Count; index++)
                if (component.Params.Output[index].Recipients.Count > 0) mask |= 1 << index;
            return mask;
        }
    }
}
