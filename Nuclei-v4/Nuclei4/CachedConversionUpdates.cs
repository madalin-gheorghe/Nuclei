using Grasshopper.Kernel;
using Grasshopper.Kernel.Types;

namespace Nuclei4
{
    internal static class CachedConversionUpdates
    {
        internal static bool ShouldExpireDownstream(GH_Component component, int updateInputIndex)
        {
            if (component.Locked || component.Params.Input.Count <= updateInputIndex) return true;
            IGH_Param update = component.Params.Input[updateInputIndex];
            // The voxel input expires each solver tick; an unchanged Update input
            // keeps its collected data. If Update itself expires, let GH propagate
            // normally so enabling it (including via an expression) takes effect.
            if ((update.Phase != GH_SolutionPhase.Collected && update.Phase != GH_SolutionPhase.Computed)
                || update.VolatileDataCount == 0)
                return true;

            foreach (IGH_Goo value in update.VolatileData.AllData(true))
            {
                GH_Boolean enabled = value as GH_Boolean;
                if (enabled == null || enabled.Value) return true;
            }
            return false;
        }
    }
}
