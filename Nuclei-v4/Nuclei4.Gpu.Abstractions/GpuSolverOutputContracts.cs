namespace Nuclei4
{
    /// <summary>
    /// Array-reference view over dynamic voxel fields read back by a GPU backend.
    /// The backend retains ownership of the arrays; the host consumes them without
    /// per-voxel translation or allocation.
    /// </summary>
    internal readonly struct GpuVoxelReadbackView
    {
        public GpuVoxelReadbackView(
            float[] density,
            float[] antFood,
            float[] antBase,
            float[] remainingFood,
            bool hasSlime,
            bool hasAnt)
        {
            Density = density;
            AntFood = antFood;
            AntBase = antBase;
            RemainingFood = remainingFood;
            HasSlime = hasSlime;
            HasAnt = hasAnt;
        }

        public float[] Density { get; }

        public float[] AntFood { get; }

        public float[] AntBase { get; }

        public float[] RemainingFood { get; }

        public bool HasSlime { get; }

        public bool HasAnt { get; }
    }

    /// <summary>
    /// Array-reference view over a complete particle readback. Float arrays use
    /// the existing float4-per-particle layout; auxiliary data uses the existing
    /// five capacity-sized age/death/division/generation/ant-state channels.
    /// </summary>
    internal readonly struct GpuParticleReadbackView
    {
        public GpuParticleReadbackView(
            int capacity,
            int count,
            int groupCount,
            float[] positions,
            float[] directions,
            float[] yAxes,
            int[] auxiliary)
        {
            Capacity = capacity;
            Count = count;
            GroupCount = groupCount;
            Positions = positions;
            Directions = directions;
            YAxes = yAxes;
            Auxiliary = auxiliary;
        }

        public int Capacity { get; }

        public int Count { get; }

        public int GroupCount { get; }

        public float[] Positions { get; }

        public float[] Directions { get; }

        public float[] YAxes { get; }

        public int[] Auxiliary { get; }
    }

    /// <summary>
    /// Lightweight position-only readback used to refresh the CPU preview cache
    /// without synchronizing full particle state.
    /// </summary>
    internal readonly struct GpuParticlePreviewReadbackView
    {
        public GpuParticlePreviewReadbackView(
            int capacity,
            int count,
            int groupCount,
            float[] positions)
        {
            Capacity = capacity;
            Count = count;
            GroupCount = groupCount;
            Positions = positions;
        }

        public int Capacity { get; }

        public int Count { get; }

        public int GroupCount { get; }

        public float[] Positions { get; }
    }

    /// <summary>
    /// One coarse host-materialization boundary per requested readback. Backends
    /// never call through this interface from particle or voxel hot loops.
    /// </summary>
    internal interface IGpuSolverOutputSink
    {
        int ParticleCount { get; }

        void ApplyVoxelFields(GpuVoxelReadbackView view);

        bool ApplyParticles(
            GpuParticleReadbackView view,
            SolverGpuSettings settings,
            int iteration,
            bool buildPreviewCache);

        bool ApplyPreviewPositions(GpuParticlePreviewReadbackView view);
    }
}
