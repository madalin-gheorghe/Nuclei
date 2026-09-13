namespace Nuclei4
{
    internal static class ParticleCapacityWarning
    {
        internal static string Create(long requested, int population, int capacity)
        {
            if (requested <= 0 && population <= 0) return null;
            // Integer arithmetic keeps the strict 90% boundary exact.
            long threshold = (long)capacity * 9 / 10;
            if (capacity <= 0 || requested > threshold || population > threshold)
                return "Too many particles";
            return null;
        }
    }
}
