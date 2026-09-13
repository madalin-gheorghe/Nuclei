using System;
using Grasshopper.Kernel;

namespace Nuclei4
{
    internal static class VoxelAttractorRange
    {
        public const string MinimumDescription = "Minimum distance in model units. Supplied reversed ranges are sorted. Thin walls retain neighboring voxels bracketing the wall; rasterized centers can extend beyond the requested interval.";
        public const string MaximumDescription = "Maximum distance in model units. An omitted maximum grows from minRange if needed. Range width is at least one voxel size; thin walls additionally retain bracketing voxel pairs for at least two neighboring layers total, subject to available input voxels.";

        public static void Normalize(ref double minimum, ref double maximum, double voxelSize, bool maximumSupplied)
        {
            if (!Finite(minimum) || !Finite(maximum) || !Finite(voxelSize) || minimum < 0 || maximum < 0 || voxelSize <= 0)
                throw new ArgumentException("Ranges must be finite and nonnegative, and voxel size must be positive.");
            if (maximumSupplied && maximum < minimum)
            {
                double swap = minimum; minimum = maximum; maximum = swap;
            }
            // A numerical fallback only. VoxelAttractorBand supplies the discrete
            // pair rule; a physical width alone cannot guarantee layer topology.
            double minD = voxelSize;
            maximum = Math.Max(maximum, minimum + minD);
            if (!Finite(maximum) || maximum - minimum < minD * (1 - 1e-12))
                throw new ArgumentException("The range is too large for this voxel size.");
        }

        public static bool TryNormalize(GH_Component component, int maximumIndex, double defaultMaximum,
            double voxelSize, ref double minimum, ref double maximum)
        {
            try
            {
                // Persistent values different from the default count as supplied too.
                bool supplied = component.Params.Input[maximumIndex].SourceCount > 0 || maximum != defaultMaximum;
                Normalize(ref minimum, ref maximum, voxelSize, supplied);
                return true;
            }
            catch (ArgumentException error)
            {
                component.AddRuntimeMessage(GH_RuntimeMessageLevel.Error, error.Message);
                return false;
            }
        }
        static bool Finite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
    }
}
