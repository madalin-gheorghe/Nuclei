namespace Nuclei3
{
    internal static class VoxelPreviewField
    {
        public const int MinimumDensity = 0;
        public const int MaximumDensity = 1;
        public const int Speed = 2;
        public const int SensorDistance = 3;
        public const int SensorAngle = 4;
        public const int RotationAngle = 5;
        public const int Food = 6;
        public const int SlimeChemoattractants = 7;
        public const int AntFoodPheromones = 8;
        public const int AntBasePheromones = 9;

        public const int StaticFieldCount = 7;

        public static bool IsStatic(int valueIndex)
        {
            return valueIndex >= MinimumDensity && valueIndex < StaticFieldCount;
        }

        public static bool IsDynamicDensity(int valueIndex)
        {
            return valueIndex == SlimeChemoattractants;
        }

        public static bool IsGpuSupported(int valueIndex)
        {
            return IsDynamicDensity(valueIndex);
        }
    }
}
