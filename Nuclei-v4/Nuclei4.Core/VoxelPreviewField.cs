namespace Nuclei4
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
        public const int SlimeFood = Food;
        public const int SlimeChemoattractants = 7;
        public const int AntFoodPheromones = 8;
        public const int AntBasePheromones = 9;
        public const int AntPheromones = 10;
        public const int AntsAndSlime = 11;
        public const int SlimeChemoattractantsV2 = 12;
        // 12 is already taken by SlimeChemoattractantsV2, so ant food takes 13.
        // V3 uses the same index so the two toolsets stay value-compatible.
        public const int AntFood = 13;

        public const int StaticFieldCount = 6;

        public static bool IsStatic(int valueIndex)
        {
            return valueIndex >= MinimumDensity && valueIndex < StaticFieldCount;
        }

        public static bool IsDynamicDensity(int valueIndex)
        {
            return valueIndex == Food
                || valueIndex == AntFood
                || valueIndex == SlimeChemoattractants
                || valueIndex == AntFoodPheromones
                || valueIndex == AntBasePheromones
                || valueIndex == AntPheromones
                || valueIndex == AntsAndSlime
                || valueIndex == SlimeChemoattractantsV2;
        }

        /// <summary>
        /// True when the field is backed by a float density buffer the GPU can
        /// raymarch. Slime food and ant food are dynamic (ant food is consumed and
        /// read back) but live in the packed deposit buffer, not a density buffer,
        /// so they must use the CPU preview path instead of allocating a second
        /// volumetric atlas.
        /// </summary>
        public static bool HasGpuDensityTexture(int valueIndex)
        {
            return IsDynamicDensity(valueIndex)
                && valueIndex != Food
                && valueIndex != AntFood;
        }

        public static bool IsCombinedDynamicDensity(int valueIndex)
        {
            return valueIndex == AntPheromones || valueIndex == AntsAndSlime;
        }

        public static bool IsGpuSupported(int valueIndex)
        {
            return IsStatic(valueIndex) || IsDynamicDensity(valueIndex);
        }

        public static int SourceField(int valueIndex)
        {
            return valueIndex == SlimeChemoattractantsV2 ? SlimeChemoattractants : valueIndex;
        }
    }
}
