namespace Nuclei4
{
    /// <summary>
    /// Packed, host-neutral reset input. Arrays are passed by reference and retain
    /// the existing D3D11 packing, avoiding translation or copies in the hot path.
    /// </summary>
    internal class GpuSolverInput
    {
        public int ResX;
        public int ResY;
        public int ResZ;
        public int ParticleCount;
        public int GroupCount;
        public float VoxelSize;
        public float[] ParticlePositionsXyz;
        public float[] ParticleDirectionsXyz;
        public float[] ParticleYAxesXyz;
        public float[] ParticleHomesXyz;
        public uint[] ParticleAntStates;
        public uint[] ParticleAntLaunchBoundaryStates;
        public int[] ParticleAges;
        public int[] ParticleGroupIndices;
        public int[] ParticleParentIndices;
        public float[] VoxelDensity;
        public float[] AntFoodPheromone;
        public float[] AntBasePheromone;
        public float[] InitialFood;
        // Ant-consumable food map. Kept separate from InitialFood, which is the
        // slime chemoattractant source, so ants no longer eat the slime map.
        public float[] InitialAntFood;
        public uint[] ActiveVoxelFlags;
        public uint[] VoxelFlags;
        public bool HasStaticPreviewInput;
        public float[] StaticMinimumDensityValues;
        public float[] StaticMaximumDensityValues;
        public float[] StaticSpeedValues;
        public float[] StaticSensorDistanceValues;
        public float[] StaticSensorAngleValues;
        public float[] StaticRotationAngleValues;
        public double StaticMinimumDensityDefault = -1;
        public double StaticMaximumDensityDefault = -1;
        public double StaticSpeedDefault = -1;
        public double StaticSensorDistanceDefault = -1;
        public double StaticSensorAngleDefault = -1;
        public double StaticRotationAngleDefault = -1;
        public float[] VoxelBehaviorData;
        public int SpeedOffset = -1;
        public int SensorDistanceOffset = -1;
        public int SensorAngleOffset = -1;
        public int RotationAngleOffset = -1;
        public float SpeedDefault = 1;
        public float SensorDistanceDefault = 1;
        public float SensorAngleDefault = 1;
        public float RotationAngleDefault = 1;
        public float[] VoxelVectorData;
        public int[] VoxelVectorFrequencies;
        public float VoxelVectorDefaultX;
        public float VoxelVectorDefaultY;
        public float VoxelVectorDefaultZ;
        public int VoxelVectorDefaultFrequency = 3;
        public float[] VoxelDensityLimits;
        public int MinimumDensityOffset = -1;
        public int MaximumDensityOffset = -1;
        public float MinimumDensityDefault = -1;
        public float MaximumDensityDefault = -1;
        public float[] GroupData0;
        public float[] GroupData1;
        public float[] GroupColorData;
        public bool HasAntParticles;
        public bool HasSlimeParticles;
        // V3 always advances the scalar density field, even in ant-only or empty
        // simulations. Species presence separately controls sensing/deposits/food.
        public bool ProcessDensity = true;
    }
}
