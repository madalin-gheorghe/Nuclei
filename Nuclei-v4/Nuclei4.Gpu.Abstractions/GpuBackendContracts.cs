using System;

namespace Nuclei4
{
    /// <summary>
    /// Immutable capability report produced when a backend is selected. Backend
    /// selection happens once per simulation session, outside all hot loops.
    /// </summary>
    internal sealed class GpuBackendCapabilities
    {
        public GpuBackendKind Backend;
        public string BackendName;
        public string AdapterName;
        public string ApiVersion;
        public GpuDeviceIdentity DeviceIdentity;
        public bool Available;
        public bool HardwareAccelerated;
        public bool SoftwareFallback;
        public bool ComputeShaders;
        public bool NativePreviewInterop;
        public string Message;
    }

    [Flags]
    internal enum GpuStepDemand
    {
        None = 0,
        SynchronizeVoxels = 1,
        SynchronizeParticles = 2,
        BuildCpuPreviewCache = 4,
        PublishDensityPreview = 8,
        PublishParticlePreview = 16,
        PublishTrailPreview = 32
    }

    /// <summary>
    /// Frontend-neutral description of one solver step. GH1 and GH2 translate
    /// their graph demand into this coarse request; no component or graph object
    /// crosses into the backend.
    /// </summary>
    internal readonly struct GpuStepRequest
    {
        public GpuStepRequest(int iteration, GpuStepDemand demand)
        {
            Iteration = iteration;
            Demand = demand;
        }

        public int Iteration { get; }

        public GpuStepDemand Demand { get; }

        public bool Requires(GpuStepDemand value)
        {
            return (Demand & value) == value;
        }
    }

    /// <summary>
    /// Coarse lifecycle shared by native compute implementations. Concrete
    /// backends may expose optional preview and meshing capabilities separately.
    /// </summary>
    internal interface IGpuSimulationBackend : IDisposable
    {
        GpuBackendCapabilities Capabilities { get; }

        bool Matches(int resX, int resY, int resZ, int particleCount);

        GpuFullSolverStepResult Step(SolverGpuSettings settings, GpuStepRequest request);
    }

    internal sealed class GpuFullSolverStepResult
    {
        public double TotalMilliseconds;
        public double ParticleMilliseconds;
        public double PopulationMilliseconds;
        public double DiffusionMilliseconds;
        public double ReadbackMilliseconds;
        public int Passes;
        public int Range;
        public bool Wrap;
        public int ParticleCount;
        public bool MovedParticles;
        public bool SyncedVoxels;
        public bool SyncedParticles;
        public bool BuiltPreviewCache;
        public bool QueuedPreviewReadback;
        public bool CompletedPreviewReadback;
    }
}
