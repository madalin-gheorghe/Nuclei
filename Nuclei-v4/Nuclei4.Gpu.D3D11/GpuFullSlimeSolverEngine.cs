using System;
using System.Diagnostics;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

using SharpGen.Runtime;
using Vortice.D3DCompiler;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;

using Rhino.Geometry;

namespace Nuclei4
{
    internal sealed class GpuVolumeMeshResult
    {
        public bool Success;
        public string Error;
        public Mesh Mesh;
        public int ActiveCellCount;
        public int TriangleCount;
        public double Milliseconds;
    }

    internal sealed class GpuPassTimestampSample
    {
        public string PassName;
        public int StepOrdinal;
        public int Occurrence;
        public double Milliseconds;
    }

    internal sealed class GpuPassTimestampBatchResult
    {
        public double TotalMilliseconds;
        public GpuPassTimestampSample[] Samples;
    }

    /// <summary>Windows D3D11 compute backend; host objects stay behind the output sink.</summary>
    internal sealed class GpuFullSlimeSolverEngine : IGpuSimulationBackend
    {
        const float DepositScale = 1024.0f;
        const int PopulationNeighbourDisabled = 0;
        const int PopulationNeighbourApplyStored = 2;
        const int PopulationNeighbourPublishOnly = -1;
        const int PopulationNeighbourPublishZero = -2;
        const int PreviewReadbackBufferCount = 3;
        const int PopulationReadbackBufferCount = 3;
        const int TiledDiffusionTileSize = 16;
        const int MaximumTiledDiffusionRange = 16;
        const int MaxSharedPreviewTextureDimension = 16384;
        const long MaxSharedDensityPreviewTexturePixels = 33554432;
        static readonly SolverGpuSettings GradientPreviewSettings = new SolverGpuSettings();
        const int MaxAdaptiveVolumePreviewResolution = 256;
        const int MaxParticleTrailPreviewTexels = 33554432;
        const string SharedDensityPreviewStatusPath = @"C:\Nuclei\Nuclei-v4\BenchmarkSuite1\NucleiGpuDensityFieldSource.txt";
        const string SharedParticlePreviewStatusPath = @"C:\Nuclei\Nuclei-v4\BenchmarkSuite1\NucleiGpuParticlePreviewSource.txt";
        static readonly int[,] TridimensionalDiffusionAxisOrders = new int[,]
        {
            { 0, 1, 2 },
            { 1, 2, 0 },
            { 2, 0, 1 },
            { 2, 1, 0 },
            { 1, 0, 2 },
            { 0, 2, 1 }
        };

        ID3D11Device device;
        ID3D11DeviceContext context;
        ID3D11Query benchmarkTimestampDisjointQuery;
        ID3D11Query benchmarkTimestampStartQuery;
        ID3D11Query benchmarkTimestampEndQuery;
        readonly List<ID3D11Query> benchmarkPassTimestampQueries = new List<ID3D11Query>();
        string[] benchmarkPassTimestampNames = Array.Empty<string>();
        int[] benchmarkPassTimestampStepOrdinals = Array.Empty<int>();
        int benchmarkPassTimestampCount;
        int benchmarkPassTimestampStepOrdinal;
        bool benchmarkPassTimestampStepOpen;
        bool benchmarkPassTimestampProfiling;
        bool benchmarkPassTimestampPending;
        bool benchmarkPassTimestampOverflow;
        bool benchmarkTimestampOpen;
        bool benchmarkTimestampPending;
        // Internal A/B switches used by the architecture probe. Production
        // retains the coalesced voxel deposit resolver and persistent counts;
        // particle-scattered deposits and full recounts are validation controls.
        bool forceDirectDiffusionForValidation = false;
        bool disableScalarDecayFusionForValidation = false;
        bool forceParticleDrivenDepositForValidation = false;
        bool forceFullParticleCountRebuildForValidation = false;
        ID3D11ComputeShader boundaryModeTransitionShader;
        ID3D11ComputeShader claimParticleOwnersShader;
        ID3D11ComputeShader cullParticleOwnerConflictsShader;
        ID3D11ComputeShader moveShader;
        ID3D11ComputeShader antMoveShader;
        ID3D11ComputeShader applyDepositsShader;
        ID3D11ComputeShader projectFoodSourcesShader;
        ID3D11ComputeShader clearCountsShader;
        ID3D11ComputeShader countParticlesShader;
        ID3D11ComputeShader advanceParticleAgesShader;
        ID3D11ComputeShader seedNeighbourCountsShader;
        ID3D11ComputeShader sumNeighbourAxisShader;
        ID3D11ComputeShader applyParticleDeathShader;
        ID3D11ComputeShader applyParticleDivisionShader;
        ID3D11ComputeShader diffusionShader;
        ID3D11ComputeShader diffusionXTiledShader;
        ID3D11ComputeShader diffusionYTiledShader;
        ID3D11ComputeShader diffusionZTiledShader;
        ID3D11ComputeShader decayShader;
        ID3D11ComputeShader densityPreviewShader;
        ID3D11ComputeShader combinedDensityPreviewShader;
        ID3D11ComputeShader densityGradientPreviewShader;
        ID3D11ComputeShader particlePreviewShader;
        ID3D11ComputeShader particleTrailPreviewShader;
        ID3D11ComputeShader volumeSmoothShader;
        ID3D11ComputeShader volumeCellClassifyShader;
        ID3D11ComputeShader volumeTriangleShader;

        ID3D11Buffer densityA;
        ID3D11Buffer densityB;
        ID3D11Buffer densityReadbackBuffer;
        ID3D11Buffer antFoodA;
        ID3D11Buffer antFoodB;
        ID3D11Buffer antBaseA;
        ID3D11Buffer antBaseB;
        ID3D11Buffer antFoodReadbackBuffer;
        ID3D11Buffer antBaseReadbackBuffer;
        ID3D11Buffer antFoodRemainingReadbackBuffer;
        ID3D11Buffer particlePositionBuffer;
        ID3D11Buffer particleDirectionBuffer;
        ID3D11Buffer particleYAxisBuffer;
        ID3D11Buffer particleHomeBuffer;
        ID3D11Buffer particlePositionReadbackBuffer;
        ID3D11Buffer particleDirectionReadbackBuffer;
        ID3D11Buffer particleYAxisReadbackBuffer;
        ID3D11Buffer particleHomeReadbackBuffer;
        ID3D11Buffer particleAuxReadbackBuffer;
        ID3D11Buffer populationStateReadbackBuffer;
        readonly ID3D11Buffer[] particlePositionPreviewReadbackBuffers = new ID3D11Buffer[PreviewReadbackBufferCount];
        readonly ID3D11Buffer[] populationAsyncReadbackBuffers = new ID3D11Buffer[PopulationReadbackBufferCount];
        ID3D11Buffer particleCountBuffer;
        ID3D11Buffer particleOwnerBuffer;
        ID3D11Buffer depositBuffer;
        ID3D11Buffer neighbourCountA;
        ID3D11Buffer neighbourCountB;
        ID3D11Buffer groupData0Buffer;
        ID3D11Buffer groupData1Buffer;
        ID3D11Buffer groupColorDataBuffer;
        ID3D11Buffer voxelFlagsBuffer;
        ID3D11Buffer activeVoxelFlagsBuffer;
        ID3D11Buffer voxelBehaviorBuffer;
        ID3D11Buffer voxelVectorBuffer;
        ID3D11Buffer voxelVectorFrequencyBuffer;
        ID3D11Buffer voxelDensityLimitsBuffer;
        ID3D11Buffer parameterBuffer;
        ID3D11Buffer volumeMeshParameterBuffer;
        ID3D11Buffer weightsBuffer;
        ID3D11Buffer antWeightsBuffer;
        ID3D11Texture2D densityPreviewTexture;
        ID3D11Texture2D densityGradientPreviewTexture;
        readonly ID3D11Texture2D[] staticFieldPreviewTextures = new ID3D11Texture2D[VoxelPreviewField.StaticFieldCount];
        ID3D11Texture2D particlePreviewTexture;
        ID3D11Texture2D particleTrailPreviewTexture;
        IDXGIKeyedMutex particleTrailPreviewMutex;
        readonly int[] diffusionAxisScratch = new int[3];

        ID3D11UnorderedAccessView densityAView;
        ID3D11UnorderedAccessView densityBView;
        ID3D11UnorderedAccessView antFoodAView;
        ID3D11UnorderedAccessView antFoodBView;
        ID3D11UnorderedAccessView antBaseAView;
        ID3D11UnorderedAccessView antBaseBView;
        ID3D11ShaderResourceView antFoodAResourceView;
        ID3D11ShaderResourceView antFoodBResourceView;
        ID3D11ShaderResourceView antBaseAResourceView;
        ID3D11ShaderResourceView antBaseBResourceView;
        ID3D11UnorderedAccessView particlePositionView;
        ID3D11UnorderedAccessView particleDirectionView;
        ID3D11UnorderedAccessView particleYAxisView;
        ID3D11UnorderedAccessView particleHomeView;
        ID3D11UnorderedAccessView particleCountView;
        ID3D11UnorderedAccessView particleOwnerView;
        ID3D11UnorderedAccessView depositView;
        ID3D11UnorderedAccessView neighbourCountAView;
        ID3D11UnorderedAccessView neighbourCountBView;
        ID3D11ShaderResourceView groupData0View;
        ID3D11ShaderResourceView groupData1View;
        ID3D11ShaderResourceView groupColorDataView;
        ID3D11ShaderResourceView voxelFlagsView;
        ID3D11ShaderResourceView activeVoxelFlagsView;
        ID3D11ShaderResourceView voxelBehaviorView;
        ID3D11ShaderResourceView voxelVectorView;
        ID3D11ShaderResourceView voxelVectorFrequencyView;
        ID3D11ShaderResourceView voxelDensityLimitsView;
        ID3D11ShaderResourceView weightsView;
        ID3D11ShaderResourceView antWeightsView;
        ID3D11UnorderedAccessView densityPreviewTextureView;
        ID3D11ShaderResourceView densityPreviewTextureResourceView;
        ID3D11UnorderedAccessView densityGradientPreviewTextureView;
        ID3D11UnorderedAccessView particlePreviewTextureView;
        ID3D11UnorderedAccessView particleTrailPreviewTextureView;

        readonly int resX;
        readonly int resY;
        readonly int resZ;
        readonly int voxelCount;
        readonly int particleCapacity;
        int particleCount;
        readonly int groupCount;
        readonly float voxelSize;
        bool enableSharedDensityPreview;
        bool enableSharedParticlePreview;
        bool enableSharedParticleTrailPreview;
        readonly float dimX;
        readonly float dimY;
        readonly float dimZ;
        float[] densityReadback;
        float[] antFoodReadback;
        float[] antBaseReadback;
        int[] antFoodRemainingReadback;
        float[] antFoodRemainingAsFloat;
        float[] particlePositionReadback;
        float[] particleDirectionReadback;
        float[] particleYAxisReadback;
        float[] particleHomeReadback;
        float[] particleHomeAxesReadback;
        float[] particlePositionPreviewReadback;
        int[] particleAuxReadback;
        readonly int[] populationStateReadback = new int[4];
        int[] populationAsyncReadback;
        readonly IGpuSolverOutputSink outputSink;
        uint[] staticActiveVoxelFlags;
        bool hasStaticPreviewInput;
        float[] staticMinimumDensityValues;
        float[] staticMaximumDensityValues;
        float[] staticSpeedValues;
        float[] staticSensorDistanceValues;
        float[] staticSensorAngleValues;
        float[] staticRotationAngleValues;
        double staticMinimumDensityDefault;
        double staticMaximumDensityDefault;
        double staticSpeedDefault;
        double staticSensorDistanceDefault;
        double staticSensorAngleDefault;
        double staticRotationAngleDefault;
        readonly bool[] previewReadbackPending = new bool[PreviewReadbackBufferCount];
        readonly int[] previewReadbackSequences = new int[PreviewReadbackBufferCount];
        readonly bool[] populationReadbackPending = new bool[PopulationReadbackBufferCount];
        readonly int[] populationReadbackSequences = new int[PopulationReadbackBufferCount];
        readonly bool[] populationReadbackAttempted = new bool[PopulationReadbackBufferCount];

        bool densityInA = true;
        bool antFoodInA = true;
        bool antBaseInA = true;
        bool wrapBoundaryState;
        readonly bool hasAntParticles;
        readonly bool hasSlimeParticles;
        readonly bool processDensity;
        bool hasVoxelFlags;
        bool hasActiveVoxelFlags;
        bool hasVoxelBehavior;
        bool hasVoxelVectors;
        bool hasVoxelVectorData;
        bool hasVoxelVectorFrequencies;
        bool hasVoxelDensityLimits;
        int voxelVectorDefaultFrequency = 3;
        float voxelVectorDefaultX;
        float voxelVectorDefaultY;
        float voxelVectorDefaultZ;
        int speedOffset = -1;
        int sensorDistanceOffset = -1;
        int sensorAngleOffset = -1;
        int rotationAngleOffset = -1;
        int minimumDensityOffset = -1;
        int maximumDensityOffset = -1;
        float speedDefault = 1;
        float sensorDistanceDefault = 1;
        float sensorAngleDefault = 1;
        float rotationAngleDefault = 1;
        float minimumDensityDefault = -1;
        float maximumDensityDefault = -1;
        int voxelBehaviorElementCount;
        int voxelDensityLimitElementCount;
        readonly int slimeDepositOffset;
        readonly int antFoodDepositOffset;
        readonly int antBaseDepositOffset;
        readonly int foodRemainingOffset;
        readonly int foodSourceOffset;
        readonly int freeSlotOffset;
        readonly int particleAgeOffset;
        readonly int particleDeathNeighbourOffset;
        readonly int particleDivisionNeighbourOffset;
        readonly int particleGenerationOffset;
        readonly int particleAntStateOffset;
        readonly int particleHighDepositOffset;
        readonly int particleAntLaunchBoundaryOffset;
        readonly int particleAntHomeYAxisXOffset;
        readonly int particleAntHomeYAxisYOffset;
        readonly int particleAntHomeYAxisZOffset;
        readonly int particleAntHomeXAxisXOffset;
        readonly int particleAntHomeXAxisYOffset;
        readonly int particleAntHomeXAxisZOffset;
        readonly int depositElementCount;
        int weightsRange = int.MinValue;
        double weightsGradual = double.NaN;
        int antWeightsRange = int.MinValue;
        int previewReadbackNextIndex = 0;
        int previewReadbackSequenceCounter = 0;
        int previewReadbackCompletedSequence = 0;
        int populationReadbackNextIndex = 0;
        int populationReadbackSequenceCounter = 0;
        int populationReadbackCompletedSequence = 0;
        IntPtr densityPreviewSharedHandle = IntPtr.Zero;
        IntPtr densityGradientPreviewSharedHandle = IntPtr.Zero;
        int densityPreviewWidth;
        int densityPreviewHeight;
        int densityPreviewResX;
        int densityPreviewResY;
        int densityPreviewResZ;
        int densityPreviewAxisMode;
        int densityPreviewSlice;
        int densityPreviewAtlasColumns = 1;
        int densityPreviewAtlasRows = 1;
        int densityPreviewScale = 1;
        long densityPreviewVersion = 0;
        long densityGradientSourceVersion = -1;
        int densityPreviewValueIndex = VoxelPreviewField.SlimeChemoattractants;
        bool densityPreviewColorTexture;
        readonly IntPtr[] staticFieldPreviewSharedHandles = new IntPtr[VoxelPreviewField.StaticFieldCount];
        readonly int[] staticFieldPreviewWidths = new int[VoxelPreviewField.StaticFieldCount];
        readonly int[] staticFieldPreviewHeights = new int[VoxelPreviewField.StaticFieldCount];
        readonly int[] staticFieldPreviewResX = new int[VoxelPreviewField.StaticFieldCount];
        readonly int[] staticFieldPreviewResY = new int[VoxelPreviewField.StaticFieldCount];
        readonly int[] staticFieldPreviewResZ = new int[VoxelPreviewField.StaticFieldCount];
        readonly int[] staticFieldPreviewAxisModes = new int[VoxelPreviewField.StaticFieldCount];
        readonly int[] staticFieldPreviewSlices = new int[VoxelPreviewField.StaticFieldCount];
        readonly int[] staticFieldPreviewAtlasColumns = new int[VoxelPreviewField.StaticFieldCount];
        readonly int[] staticFieldPreviewAtlasRows = new int[VoxelPreviewField.StaticFieldCount];
        readonly long[] staticFieldPreviewVersions = new long[VoxelPreviewField.StaticFieldCount];
        readonly float[] staticFieldPreviewScaleMinimums = new float[VoxelPreviewField.StaticFieldCount];
        readonly float[] staticFieldPreviewScaleMaximums = new float[VoxelPreviewField.StaticFieldCount];
        readonly float[] staticFieldPreviewScales = new float[VoxelPreviewField.StaticFieldCount];
        readonly bool[] staticFieldPreviewScaleValid = new bool[VoxelPreviewField.StaticFieldCount];
        IntPtr particlePreviewSharedHandle = IntPtr.Zero;
        int particlePreviewWidth;
        int particlePreviewHeight;
        long particlePreviewVersion = 0;
        IntPtr particleTrailPreviewSharedHandle = IntPtr.Zero;
        int particleTrailPreviewWidth;
        int particleTrailPreviewHeight;
        int particleTrailPreviewTrailSize;
        int particleTrailPreviewHeadIndex;
        int particleTrailPreviewValidCount;
        int particleTrailPreviewLastDispatchIteration = -1;
        int lastSolverIteration = -1;
        float[] particleTrailPreviewGroupColorData;
        long particleTrailPreviewVersion = 0;

        public GpuFullSlimeSolverEngine(GpuSolverInput snapshot, IGpuSolverOutputSink outputSink, SolverGpuSettings settings, bool enableSharedDensityPreview, bool enableSharedParticlePreview, bool enableSharedParticleTrailPreview, int particleTrailSize, int densityPreviewScale)
        {
            if (snapshot == null)
            {
                throw new ArgumentNullException(nameof(snapshot));
            }
            if (outputSink == null)
            {
                throw new ArgumentNullException(nameof(outputSink));
            }

            this.outputSink = outputSink;

            resX = snapshot.ResX;
            resY = snapshot.ResY;
            resZ = snapshot.ResZ;
            voxelCount = CheckedVoxelCount(resX, resY, resZ);
            particleCount = Math.Max(0, snapshot.ParticleCount);
            int requestedCapacity = settings != null && settings.DynamicPopulation
                ? settings.MaximumPopulation
                : particleCount;
            particleCapacity = Math.Max(particleCount, Math.Max(0, requestedCapacity));
            groupCount = Math.Max(0, snapshot.GroupCount);
            hasAntParticles = snapshot.HasAntParticles;
            hasSlimeParticles = snapshot.HasSlimeParticles;
            processDensity = snapshot.ProcessDensity;
            wrapBoundaryState = settings != null && settings.WrapBoundaries;
            if (!ValidVoxelFlags(snapshot.VoxelFlags, voxelCount)
                || !ValidVoxelFlags(snapshot.ActiveVoxelFlags, voxelCount)
                || !ValidStaticPreviewLayout(snapshot, voxelCount)
                || !ValidBehaviorLayout(snapshot, voxelCount)
                || !ValidDensityLimitLayout(snapshot, voxelCount))
            {
                throw new ArgumentException("GPU voxel channel layout does not match the voxel field.");
            }

            hasVoxelFlags = snapshot.VoxelFlags != null;
            hasVoxelBehavior = HasVoxelBehavior(snapshot);
            if ((snapshot.VoxelVectorData != null && snapshot.VoxelVectorData.Length != checked(voxelCount * 3))
                || (snapshot.VoxelVectorFrequencies != null && snapshot.VoxelVectorFrequencies.Length != voxelCount))
            {
                throw new ArgumentException("GPU vector channel layout does not match the voxel field.");
            }
            hasVoxelVectorData = snapshot.VoxelVectorData != null;
            hasVoxelVectors = hasVoxelVectorData || snapshot.VoxelVectorDefaultX != 0 || snapshot.VoxelVectorDefaultY != 0 || snapshot.VoxelVectorDefaultZ != 0;
            hasVoxelVectorFrequencies = hasVoxelVectors && snapshot.VoxelVectorFrequencies != null;
            voxelVectorDefaultFrequency = Math.Max(1, snapshot.VoxelVectorDefaultFrequency);
            voxelVectorDefaultX = snapshot.VoxelVectorDefaultX;
            voxelVectorDefaultY = snapshot.VoxelVectorDefaultY;
            voxelVectorDefaultZ = snapshot.VoxelVectorDefaultZ;
            hasVoxelDensityLimits = HasVoxelDensityLimits(snapshot);
            ApplySnapshotChannelOffsets(snapshot);
            ApplyStaticPreviewInput(snapshot);
            particleTrailPreviewGroupColorData = snapshot.GroupColorData;
            voxelSize = snapshot.VoxelSize > 0 ? snapshot.VoxelSize : 1.0f;
            this.densityPreviewScale = NormalizeDensityPreviewScale(densityPreviewScale);
            this.enableSharedDensityPreview = enableSharedDensityPreview;
            this.enableSharedParticlePreview = enableSharedParticlePreview;
            this.enableSharedParticleTrailPreview = enableSharedParticleTrailPreview;
            particleTrailPreviewTrailSize = ClampTrailPreviewSizeForParticleCount(particleTrailSize);
            dimX = resX * voxelSize;
            dimY = resY * voxelSize;
            dimZ = resZ * voxelSize;

            int auxiliaryOffset = 0;
            slimeDepositOffset = ReserveAuxiliaryChannel(ref auxiliaryOffset, voxelCount, hasSlimeParticles);
            antFoodDepositOffset = ReserveAuxiliaryChannel(ref auxiliaryOffset, voxelCount, hasAntParticles);
            antBaseDepositOffset = ReserveAuxiliaryChannel(ref auxiliaryOffset, voxelCount, hasAntParticles);
            // Ant-consumable remaining food is now fed by the separate ant food map.
            foodRemainingOffset = ReserveAuxiliaryChannel(ref auxiliaryOffset, voxelCount, snapshot.InitialAntFood != null);
            // Immutable slime food source, projected into density every step.
            foodSourceOffset = ReserveAuxiliaryChannel(ref auxiliaryOffset, voxelCount, snapshot.InitialFood != null);
            freeSlotOffset = ReserveAuxiliaryChannel(ref auxiliaryOffset, particleCapacity, particleCapacity > 0);
            particleAgeOffset = ReserveAuxiliaryChannel(ref auxiliaryOffset, particleCapacity, particleCapacity > 0);
            particleDeathNeighbourOffset = ReserveAuxiliaryChannel(ref auxiliaryOffset, particleCapacity, particleCapacity > 0);
            particleDivisionNeighbourOffset = ReserveAuxiliaryChannel(ref auxiliaryOffset, particleCapacity, particleCapacity > 0);
            particleGenerationOffset = ReserveAuxiliaryChannel(ref auxiliaryOffset, particleCapacity, particleCapacity > 0);
            particleAntStateOffset = ReserveAuxiliaryChannel(ref auxiliaryOffset, particleCapacity, particleCapacity > 0);
            // V3's high-deposit flag: a particle whose previous move landed in an
            // occupied voxel deposits a quarter of the normal amount.
            particleHighDepositOffset = ReserveAuxiliaryChannel(ref auxiliaryOffset, particleCapacity, particleCapacity > 0);
            // Ant launch state and the fixed home-plane axes stay GPU-resident. The
            // Y axis drives V3's deterministic launch wave; both axes are retained so
            // synchronized random-born ants expose the inherited home plane exactly.
            particleAntLaunchBoundaryOffset = ReserveAuxiliaryChannel(ref auxiliaryOffset, particleCapacity, hasAntParticles);
            particleAntHomeYAxisXOffset = ReserveAuxiliaryChannel(ref auxiliaryOffset, particleCapacity, hasAntParticles);
            particleAntHomeYAxisYOffset = ReserveAuxiliaryChannel(ref auxiliaryOffset, particleCapacity, hasAntParticles);
            particleAntHomeYAxisZOffset = ReserveAuxiliaryChannel(ref auxiliaryOffset, particleCapacity, hasAntParticles);
            particleAntHomeXAxisXOffset = ReserveAuxiliaryChannel(ref auxiliaryOffset, particleCapacity, hasAntParticles);
            particleAntHomeXAxisYOffset = ReserveAuxiliaryChannel(ref auxiliaryOffset, particleCapacity, hasAntParticles);
            particleAntHomeXAxisZOffset = ReserveAuxiliaryChannel(ref auxiliaryOffset, particleCapacity, hasAntParticles);
            depositElementCount = Math.Max(1, auxiliaryOffset);

            if (voxelCount <= 0)
            {
                throw new ArgumentException("GPU solver requires at least one voxel.");
            }

            densityReadback = null;
            antFoodReadback = null;
            antBaseReadback = null;
            antFoodRemainingReadback = null;
            antFoodRemainingAsFloat = null;
            particlePositionReadback = null;
            particleDirectionReadback = null;
            particleYAxisReadback = null;
            particleHomeReadback = null;
            particleHomeAxesReadback = null;
            particlePositionPreviewReadback = null;
            particleAuxReadback = null;

            bool softwareFallback;
            CreateDevice(out device, out context, out softwareFallback);
            Capabilities = new GpuBackendCapabilities
            {
                Backend = GpuBackendKind.Direct3D11,
                BackendName = "Direct3D 11",
                ApiVersion = "D3D11 / shader model 5.0",
                Available = true,
                HardwareAccelerated = !softwareFallback,
                SoftwareFallback = softwareFallback,
                ComputeShaders = true,
                NativePreviewInterop = !softwareFallback,
                Message = softwareFallback
                    ? "D3D11 WARP software fallback initialized."
                    : "D3D11 hardware backend initialized."
            };
            CompileShaders();
            if (processDensity)
            {
                CreateDensityBuffers(snapshot.VoxelDensity);
            }
            if (hasAntParticles)
            {
                CreateAntFieldBuffers(snapshot.AntFoodPheromone, snapshot.AntBasePheromone);
            }
            CreateParameterBuffer();
            if (enableSharedDensityPreview)
            {
                CreateDensityPreviewTexture(SolverGpuDimensionMode.FromResolution(resX, resY, resZ));
            }
            if (enableSharedParticlePreview)
            {
                CreateParticlePreviewTexture();
            }
            if (enableSharedParticleTrailPreview)
            {
                CreateParticleTrailPreviewTexture(particleTrailPreviewTrailSize);
            }
            CreateVoxelFlagBuffer(snapshot);
            CreateVoxelBehaviorBuffers(snapshot);
            CreateParticleBuffers(snapshot);
            CreateGroupBuffers(snapshot);

            DispatchRebuildParticleOwnership(
                settings ?? new SolverGpuSettings(),
                SolverGpuDimensionMode.FromResolution(resX, resY, resZ),
                0);
            DispatchClearParticleCounts(0, false);
            DispatchCountParticles(0);
            ReadBackPopulationState();
            if (enableSharedParticlePreview)
            {
                DispatchParticlePreviewPass(new SolverGpuSettings(), SolverGpuDimensionMode.FromResolution(resX, resY, resZ), 0);
            }
            if (enableSharedParticleTrailPreview)
            {
                DispatchParticleTrailPreviewPass(new SolverGpuSettings { TrailSize = particleTrailPreviewTrailSize, TrailFreq = 1 }, SolverGpuDimensionMode.FromResolution(resX, resY, resZ), 0);
            }
        }

        public bool Matches(int x, int y, int z, int particles)
        {
            return resX == x && resY == y && resZ == z && particleCount == particles;
        }

        public bool SupportsPopulationCapacity(SolverGpuSettings settings)
        {
            return settings == null || !settings.DynamicPopulation || settings.MaximumPopulation <= particleCapacity;
        }

        public GpuVolumeMeshResult CreateDensityMesh(float isoValue, int maximumTriangles, int smoothingIterations)
        {
            Stopwatch timer = Stopwatch.StartNew();
            GpuVolumeMeshResult result = new GpuVolumeMeshResult();
            if (!processDensity || densityA == null || densityB == null)
            {
                result.Error = "The current Solver GPU has no slime density field to mesh.";
                return result;
            }

            if (volumeSmoothShader == null || volumeCellClassifyShader == null || volumeTriangleShader == null)
            {
                result.Error = "GPU volume meshing shaders are unavailable.";
                return result;
            }

            int triangleLimit = Math.Max(1, maximumTriangles);
            int cellResX = checked(resX + 1);
            int cellResY = checked(resY + 1);
            int cellResZ = checked(resZ + 1);
            int cellsPerLayer = checked(cellResX * cellResY);
            const int targetActiveCellCapacity = 2000000;
            const int activeBatchSize = 65536;
            const int maximumTrianglesPerCell = 12;
            const int triangleStride = 48;

            int chunkDepth = Math.Max(1, Math.Min(cellResZ, targetActiveCellCapacity / Math.Max(1, cellsPerLayer)));
            int activeCellCapacity = checked(cellsPerLayer * chunkDepth);
            int triangleCapacity = checked(activeBatchSize * maximumTrianglesPerCell);

            ID3D11Buffer activeCells = null;
            ID3D11UnorderedAccessView activeCellsUav = null;
            ID3D11ShaderResourceView activeCellsSrv = null;
            ID3D11Buffer triangles = null;
            ID3D11UnorderedAccessView trianglesUav = null;
            ID3D11Buffer triangleReadback = null;
            ID3D11Buffer countBuffer = null;
            ID3D11Buffer countReadback = null;
            ID3D11Buffer smoothingScratch = null;
            ID3D11UnorderedAccessView smoothingScratchView = null;

            try
            {
                activeCells = CreateStructuredBuffer(activeCellCapacity, sizeof(uint), BindFlags.UnorderedAccess | BindFlags.ShaderResource);
                activeCellsUav = device.CreateUnorderedAccessView(
                    activeCells,
                    new UnorderedAccessViewDescription(activeCells, Format.Unknown, 0, activeCellCapacity, BufferUnorderedAccessViewFlags.Append));
                activeCellsSrv = CreateSrv(activeCells, activeCellCapacity);

                triangles = CreateStructuredBuffer(triangleCapacity, triangleStride, BindFlags.UnorderedAccess);
                trianglesUav = device.CreateUnorderedAccessView(
                    triangles,
                    new UnorderedAccessViewDescription(triangles, Format.Unknown, 0, triangleCapacity, BufferUnorderedAccessViewFlags.Append));
                triangleReadback = CreateReadbackBuffer(checked(triangleCapacity * triangleStride));
                countBuffer = device.CreateBuffer(4, BindFlags.None, ResourceUsage.Default, CpuAccessFlags.None, ResourceOptionFlags.None, 0);
                countReadback = CreateReadbackBuffer(4);

                Mesh mesh = new Mesh();
                int totalTriangles = 0;
                int totalActiveCells = 0;
                int smoothPasses = Math.Max(0, Math.Min(8, smoothingIterations));
                ID3D11UnorderedAccessView meshingDensityView = CurrentDensityView();

                if (smoothPasses > 0)
                {
                    if (smoothPasses > 1)
                    {
                        smoothingScratch = CreateStructuredBuffer(voxelCount, sizeof(float), BindFlags.UnorderedAccess);
                        smoothingScratchView = CreateUav(smoothingScratch, voxelCount);
                    }

                    GpuMeshParameters smoothParameters = CreateMeshParameters(isoValue, 0, cellResZ, 0, 0);
                    UpdateMeshParameters(smoothParameters);
                    for (int pass = 0; pass < smoothPasses; pass++)
                    {
                        ID3D11UnorderedAccessView sourceView = pass == 0
                            ? CurrentDensityView()
                            : (pass % 2 == 1 ? NextDensityView() : smoothingScratchView);
                        ID3D11UnorderedAccessView destinationView = pass % 2 == 0
                            ? NextDensityView()
                            : smoothingScratchView;

                        UnbindComputeResources();
                        context.CSSetShader(volumeSmoothShader);
                        context.CSSetConstantBuffer(1, volumeMeshParameterBuffer);
                        context.CSSetUnorderedAccessView(0, sourceView, -1);
                        context.CSSetUnorderedAccessView(1, destinationView, -1);
                        DispatchLinear256(voxelCount);
                    }
                    UnbindComputeResources();
                    meshingDensityView = smoothPasses % 2 == 1 ? NextDensityView() : smoothingScratchView;
                }

                for (int startZ = 0; startZ < cellResZ; startZ += chunkDepth)
                {
                    int endZ = Math.Min(cellResZ, startZ + chunkDepth);
                    GpuMeshParameters parameters = CreateMeshParameters(isoValue, startZ, endZ, 0, 0);
                    UpdateMeshParameters(parameters);

                    UnbindComputeResources();
                    context.CSSetShader(volumeCellClassifyShader);
                    context.CSSetConstantBuffer(1, volumeMeshParameterBuffer);
                    context.CSSetUnorderedAccessView(0, meshingDensityView, -1);
                    context.CSSetUnorderedAccessView(1, activeCellsUav, 0);
                    context.Dispatch(
                        Math.Max(1, (cellResX + 7) / 8),
                        Math.Max(1, (cellResY + 7) / 8),
                        Math.Max(1, (endZ - startZ + 3) / 4));
                    UnbindComputeResources();

                    int activeCount = ReadAppendCount(activeCellsUav, countBuffer, countReadback);
                    totalActiveCells = checked(totalActiveCells + activeCount);
                    for (int activeOffset = 0; activeOffset < activeCount; activeOffset += activeBatchSize)
                    {
                        int activeInBatch = Math.Min(activeBatchSize, activeCount - activeOffset);
                        parameters = CreateMeshParameters(isoValue, startZ, endZ, activeOffset, activeInBatch);
                        UpdateMeshParameters(parameters);

                        context.CSSetShader(volumeTriangleShader);
                        context.CSSetConstantBuffer(1, volumeMeshParameterBuffer);
                        context.CSSetShaderResource(0, activeCellsSrv);
                        context.CSSetUnorderedAccessView(0, meshingDensityView, -1);
                        context.CSSetUnorderedAccessView(1, trianglesUav, 0);
                        context.Dispatch(Math.Max(1, (activeInBatch + 127) / 128), 1, 1);
                        UnbindComputeResources();

                        int triangleCount = ReadAppendCount(trianglesUav, countBuffer, countReadback);
                        if ((long)totalTriangles + triangleCount > triangleLimit)
                        {
                            result.Error = "The isosurface exceeds the Maximum Triangles limit of " + triangleLimit.ToString("N0") + ". Increase the limit or use a higher iso value.";
                            return result;
                        }

                        AppendTrianglesToMesh(mesh, triangles, triangleReadback, triangleCount);
                        totalTriangles += triangleCount;
                    }
                }

                if (totalTriangles == 0)
                {
                    result.Error = "No surface crosses the requested iso value.";
                    return result;
                }

                mesh.Vertices.CombineIdentical(true, true);
                mesh.Faces.CullDegenerateFaces();
                mesh.Vertices.CullUnused();
                mesh.UnifyNormals();
                if (smoothPasses > 0)
                {
                    mesh.Smooth(
                        0.3,
                        smoothPasses,
                        true,
                        true,
                        true,
                        true,
                        SmoothingCoordinateSystem.World,
                        Plane.WorldXY);
                }
                mesh.Normals.ComputeNormals();
                mesh.Weld(Math.PI);
                mesh.Compact();
                result.Success = true;
                result.Mesh = mesh;
                result.ActiveCellCount = totalActiveCells;
                result.TriangleCount = totalTriangles;
                result.Milliseconds = timer.Elapsed.TotalMilliseconds;
                return result;
            }
            catch (Exception ex)
            {
                result.Error = "GPU volume meshing failed: " + ex.Message;
                return result;
            }
            finally
            {
                UnbindComputeResources();
                context.CSSetConstantBuffer(1, null);
                if (activeCellsUav != null) activeCellsUav.Dispose();
                if (activeCellsSrv != null) activeCellsSrv.Dispose();
                if (activeCells != null) activeCells.Dispose();
                if (trianglesUav != null) trianglesUav.Dispose();
                if (triangles != null) triangles.Dispose();
                if (triangleReadback != null) triangleReadback.Dispose();
                if (countBuffer != null) countBuffer.Dispose();
                if (countReadback != null) countReadback.Dispose();
                if (smoothingScratchView != null) smoothingScratchView.Dispose();
                if (smoothingScratch != null) smoothingScratch.Dispose();
            }
        }

        GpuMeshParameters CreateMeshParameters(float isoValue, int startZ, int endZ, int activeOffset, int activeCount)
        {
            GpuMeshParameters parameters = new GpuMeshParameters();
            parameters.ResX = resX;
            parameters.ResY = resY;
            parameters.ResZ = resZ;
            parameters.CellStartZ = startZ;
            parameters.CellEndZ = endZ;
            parameters.ActiveOffset = activeOffset;
            parameters.ActiveCount = activeCount;
            parameters.IsoValue = Math.Max(0.000001f, isoValue);
            parameters.VoxelSize = voxelSize;
            return parameters;
        }

        void UpdateMeshParameters(GpuMeshParameters parameters)
        {
            context.UpdateSubresourceSafe(ref parameters, volumeMeshParameterBuffer, 0, 0, 0, 0, false);
        }

        int ReadAppendCount(ID3D11UnorderedAccessView source, ID3D11Buffer gpuCount, ID3D11Buffer stagingCount)
        {
            context.CopyStructureCount(gpuCount, 0, source);
            context.CopyResource(stagingCount, gpuCount);
            MappedSubresource mapped = context.Map(stagingCount, MapMode.Read, Vortice.Direct3D11.MapFlags.None);
            try
            {
                return Math.Max(0, Marshal.ReadInt32(mapped.DataPointer));
            }
            finally
            {
                context.Unmap(stagingCount, 0);
            }
        }

        void AppendTrianglesToMesh(Mesh mesh, ID3D11Buffer source, ID3D11Buffer staging, int triangleCount)
        {
            if (triangleCount <= 0)
            {
                return;
            }

            context.CopyResource(staging, source);
            MappedSubresource mapped = context.Map(staging, MapMode.Read, Vortice.Direct3D11.MapFlags.None);
            try
            {
                float[] values = new float[checked(triangleCount * 12)];
                Marshal.Copy(mapped.DataPointer, values, 0, values.Length);
                for (int triangle = 0; triangle < triangleCount; triangle++)
                {
                    int sourceOffset = triangle * 12;
                    int vertexOffset = mesh.Vertices.Count;
                    mesh.Vertices.Add(values[sourceOffset], values[sourceOffset + 1], values[sourceOffset + 2]);
                    mesh.Vertices.Add(values[sourceOffset + 4], values[sourceOffset + 5], values[sourceOffset + 6]);
                    mesh.Vertices.Add(values[sourceOffset + 8], values[sourceOffset + 9], values[sourceOffset + 10]);
                    mesh.Faces.AddFace(vertexOffset, vertexOffset + 1, vertexOffset + 2);
                }
            }
            finally
            {
                context.Unmap(staging, 0);
            }
        }

        public bool CanFastReset(GpuSolverInput snapshot, SolverGpuSettings settings)
        {
            if (snapshot == null || settings == null)
            {
                return false;
            }

            return snapshot.ResX == resX
                && snapshot.ResY == resY
                && snapshot.ResZ == resZ
                && Math.Abs(snapshot.VoxelSize - voxelSize) < 0.000001f
                && snapshot.ParticleCount <= particleCapacity
                && snapshot.GroupCount == groupCount
                && snapshot.HasAntParticles == hasAntParticles
                && snapshot.HasSlimeParticles == hasSlimeParticles
                && snapshot.ProcessDensity == processDensity
                && (snapshot.InitialAntFood != null) == (foodRemainingOffset >= 0)
                && (snapshot.InitialFood != null) == (foodSourceOffset >= 0)
                && SupportsPopulationCapacity(settings);
        }

        public void FastReset(GpuSolverInput snapshot, SolverGpuSettings settings)
        {
            if (!CanFastReset(snapshot, settings))
            {
                throw new InvalidOperationException("GPU solver state is not compatible with fast reset.");
            }

            UnbindComputeResources();
            wrapBoundaryState = settings.WrapBoundaries;
            particleCount = snapshot.ParticleCount;

            if (processDensity)
            {
                ResetFloatFieldPair(snapshot.VoxelDensity, densityA, densityAView, densityB, densityBView);
                densityInA = true;
            }

            if (hasAntParticles)
            {
                ResetFloatFieldPair(snapshot.AntFoodPheromone, antFoodA, antFoodAView, antFoodB, antFoodBView);
                ResetFloatFieldPair(snapshot.AntBasePheromone, antBaseA, antBaseAView, antBaseB, antBaseBView);
                antFoodInA = true;
                antBaseInA = true;
            }

            float[] positions;
            float[] directions;
            float[] yAxes;
            float[] homes;
            BuildParticleBufferData(snapshot, out positions, out directions, out yAxes, out homes);
            if (particleCapacity > 0)
            {
                context.UpdateSubresourceSafe(positions, particlePositionBuffer, 0, 0, 0, 0, false);
                context.UpdateSubresourceSafe(directions, particleDirectionBuffer, 0, 0, 0, 0, false);
                context.UpdateSubresourceSafe(yAxes, particleYAxisBuffer, 0, 0, 0, 0, false);
                context.UpdateSubresourceSafe(homes, particleHomeBuffer, 0, 0, 0, 0, false);
            }

            ResetAuxiliaryState(snapshot);

            if (neighbourCountAView != null)
            {
                context.ClearUnorderedAccessView(neighbourCountAView, System.Numerics.Vector4.Zero);
            }
            if (neighbourCountBView != null)
            {
                context.ClearUnorderedAccessView(neighbourCountBView, System.Numerics.Vector4.Zero);
            }
            if (!UpdateGroupSettings(snapshot.GroupData0, snapshot.GroupData1, snapshot.GroupColorData)
                || !UpdateVoxelBehaviorFields(snapshot))
            {
                throw new InvalidOperationException("GPU reset could not restore group or voxel fields.");
            }

            if (densityReadback != null) Array.Clear(densityReadback, 0, densityReadback.Length);
            if (antFoodReadback != null) Array.Clear(antFoodReadback, 0, antFoodReadback.Length);
            if (antBaseReadback != null) Array.Clear(antBaseReadback, 0, antBaseReadback.Length);
            if (antFoodRemainingReadback != null) Array.Clear(antFoodRemainingReadback, 0, antFoodRemainingReadback.Length);
            if (antFoodRemainingAsFloat != null) Array.Clear(antFoodRemainingAsFloat, 0, antFoodRemainingAsFloat.Length);
            if (particlePositionReadback != null) Array.Clear(particlePositionReadback, 0, particlePositionReadback.Length);
            if (particleDirectionReadback != null) Array.Clear(particleDirectionReadback, 0, particleDirectionReadback.Length);
            if (particleYAxisReadback != null) Array.Clear(particleYAxisReadback, 0, particleYAxisReadback.Length);
            if (particleHomeReadback != null) Array.Clear(particleHomeReadback, 0, particleHomeReadback.Length);
            if (particleHomeAxesReadback != null) Array.Clear(particleHomeAxesReadback, 0, particleHomeAxesReadback.Length);
            if (particlePositionPreviewReadback != null) Array.Clear(particlePositionPreviewReadback, 0, particlePositionPreviewReadback.Length);
            if (particleAuxReadback != null) Array.Clear(particleAuxReadback, 0, particleAuxReadback.Length);
            Array.Clear(populationStateReadback, 0, populationStateReadback.Length);

            ResetPreviewReadbackState();
            InvalidatePendingPopulationReadbacks();
            ResetParticleTrailPreviewHistory();
            DispatchRebuildParticleOwnership(settings, SolverGpuDimensionMode.FromResolution(resX, resY, resZ), 0);
            DispatchClearParticleCounts(0, false);
            DispatchCountParticles(0);
            ReadBackPopulationState();

            SolverGpuDimensionMode dimensionMode = SolverGpuDimensionMode.FromResolution(resX, resY, resZ);
            if (enableSharedDensityPreview && densityPreviewTextureView != null)
            {
                DispatchDensityPreviewPass(settings, dimensionMode, 0);
            }
            if (enableSharedParticlePreview && particlePreviewTextureView != null)
            {
                DispatchParticlePreviewPass(settings, dimensionMode, 0);
            }
            if (enableSharedParticleTrailPreview && particleTrailPreviewTextureView != null)
            {
                DispatchParticleTrailPreviewPass(
                    new SolverGpuSettings { TrailSize = particleTrailPreviewTrailSize, TrailFreq = 1 },
                    dimensionMode,
                    0);
            }
            context.Flush();
        }

        public int SynchronizeActiveParticleCount()
        {
            ReadBackPopulationState();
            return particleCount;
        }

        /// <summary>
        /// Opens a hardware GPU timestamp scope for an offline benchmark batch.
        /// The matching end timestamp is issued before the blocking validation
        /// fence so query time excludes readback overhead.
        /// </summary>
        public void BeginGpuTimestampBatch()
        {
            BeginGpuTimestampBatchCore(false, 0);
        }

        /// <summary>
        /// Opens the same hardware timestamp scope with additional offline-only
        /// pass boundaries. Query objects and metadata storage are allocated
        /// before the disjoint scope so no resources are created while timing.
        /// </summary>
        public void BeginGpuPassTimestampBatch(int expectedSteps)
        {
            if (expectedSteps <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(expectedSteps));
            }

            int markerCapacity = checked(expectedSteps * 32 + 1);
            EnsureBenchmarkPassTimestampQueries(markerCapacity);
            if (benchmarkPassTimestampNames.Length < markerCapacity)
            {
                benchmarkPassTimestampNames = new string[markerCapacity];
                benchmarkPassTimestampStepOrdinals = new int[markerCapacity];
            }
            BeginGpuTimestampBatchCore(true, markerCapacity);
        }

        void BeginGpuTimestampBatchCore(bool profilePasses, int markerCapacity)
        {
            if (benchmarkTimestampOpen || benchmarkTimestampPending)
            {
                throw new InvalidOperationException("A GPU timestamp batch is already open or awaiting resolution.");
            }

            EnsureBenchmarkTimestampQueries();
            benchmarkPassTimestampProfiling = profilePasses;
            benchmarkPassTimestampPending = false;
            benchmarkPassTimestampCount = 0;
            benchmarkPassTimestampStepOrdinal = -1;
            benchmarkPassTimestampStepOpen = false;
            benchmarkPassTimestampOverflow = false;
            if (profilePasses)
            {
                Array.Clear(benchmarkPassTimestampNames, 0, markerCapacity);
            }
            context.Begin(benchmarkTimestampDisjointQuery);
            context.End(benchmarkTimestampStartQuery);
            benchmarkTimestampOpen = true;
        }

        public void EndGpuTimestampBatch()
        {
            if (!benchmarkTimestampOpen)
            {
                throw new InvalidOperationException("No GPU timestamp batch is open.");
            }
            if (benchmarkPassTimestampStepOpen)
            {
                throw new InvalidOperationException("A profiled GPU step was not closed before the batch ended.");
            }

            context.End(benchmarkTimestampEndQuery);
            context.End(benchmarkTimestampDisjointQuery);
            benchmarkTimestampOpen = false;
            benchmarkTimestampPending = true;
            benchmarkPassTimestampPending = benchmarkPassTimestampProfiling;
        }

        public double ResolveGpuTimestampBatchMilliseconds()
        {
            if (benchmarkTimestampOpen || !benchmarkTimestampPending)
            {
                throw new InvalidOperationException("No completed GPU timestamp batch is awaiting resolution.");
            }
            if (benchmarkPassTimestampPending)
            {
                throw new InvalidOperationException(
                    "This batch contains pass timestamps; resolve it with ResolveGpuPassTimestampBatch.");
            }

            ReadBenchmarkTimestampRange(out ulong start, out ulong end, out QueryDataTimestampDisjoint timing);
            benchmarkTimestampPending = false;
            benchmarkPassTimestampProfiling = false;
            return TimestampMilliseconds(start, end, timing);
        }

        public GpuPassTimestampBatchResult ResolveGpuPassTimestampBatch()
        {
            if (benchmarkTimestampOpen || !benchmarkTimestampPending || !benchmarkPassTimestampPending)
            {
                throw new InvalidOperationException("No completed GPU pass-timestamp batch is awaiting resolution.");
            }
            if (benchmarkPassTimestampOverflow)
            {
                benchmarkTimestampPending = false;
                benchmarkPassTimestampPending = false;
                benchmarkPassTimestampProfiling = false;
                throw new InvalidOperationException("GPU pass-timestamp query capacity was exceeded.");
            }

            ReadBenchmarkTimestampRange(out ulong start, out ulong end, out QueryDataTimestampDisjoint timing);
            double totalMilliseconds = TimestampMilliseconds(start, end, timing);
            ulong[] timestamps = new ulong[benchmarkPassTimestampCount];
            for (int i = 0; i < timestamps.Length; i++)
            {
                if (!context.GetData(benchmarkPassTimestampQueries[i], out timestamps[i]))
                {
                    throw new InvalidOperationException(
                        "GPU pass timestamp data was not ready after the synchronized validation fence.");
                }
            }

            List<GpuPassTimestampSample> samples = new List<GpuPassTimestampSample>(
                Math.Max(0, benchmarkPassTimestampCount - 1));
            Dictionary<string, int> occurrences = new Dictionary<string, int>(StringComparer.Ordinal);
            int occurrenceStep = -1;
            for (int i = 0; i + 1 < benchmarkPassTimestampCount; i++)
            {
                string passName = benchmarkPassTimestampNames[i];
                int stepOrdinal = benchmarkPassTimestampStepOrdinals[i];
                if (passName == null || benchmarkPassTimestampStepOrdinals[i + 1] != stepOrdinal)
                {
                    continue;
                }
                if (timestamps[i + 1] < timestamps[i])
                {
                    throw new InvalidOperationException("GPU pass timestamp sample was invalid.");
                }
                if (stepOrdinal != occurrenceStep)
                {
                    occurrences.Clear();
                    occurrenceStep = stepOrdinal;
                }
                occurrences.TryGetValue(passName, out int occurrence);
                occurrence++;
                occurrences[passName] = occurrence;
                samples.Add(new GpuPassTimestampSample
                {
                    PassName = passName,
                    StepOrdinal = stepOrdinal,
                    Occurrence = occurrence,
                    Milliseconds = (timestamps[i + 1] - timestamps[i]) * 1000.0 / timing.Frequency
                });
            }

            benchmarkTimestampPending = false;
            benchmarkPassTimestampPending = false;
            benchmarkPassTimestampProfiling = false;
            return new GpuPassTimestampBatchResult
            {
                TotalMilliseconds = totalMilliseconds,
                Samples = samples.ToArray()
            };
        }

        void ReadBenchmarkTimestampRange(
            out ulong start,
            out ulong end,
            out QueryDataTimestampDisjoint timing)
        {
            if (!context.GetData(benchmarkTimestampStartQuery, out start)
                || !context.GetData(benchmarkTimestampEndQuery, out end)
                || !context.GetData(benchmarkTimestampDisjointQuery, out timing))
            {
                throw new InvalidOperationException(
                    "GPU timestamp data was not ready after the synchronized validation fence.");
            }
        }

        static double TimestampMilliseconds(
            ulong start,
            ulong end,
            QueryDataTimestampDisjoint timing)
        {
            if (timing.Disjoint)
            {
                throw new InvalidOperationException(
                    "GPU timestamp sample was disjoint because the hardware clock changed during the batch.");
            }
            if (timing.Frequency == 0 || end < start)
            {
                throw new InvalidOperationException("GPU timestamp sample was invalid.");
            }
            return (end - start) * 1000.0 / timing.Frequency;
        }

        void EnsureBenchmarkTimestampQueries()
        {
            if (benchmarkTimestampDisjointQuery != null) return;

            benchmarkTimestampDisjointQuery = device.CreateQuery(
                new QueryDescription(QueryType.TimestampDisjoint, QueryFlags.None));
            benchmarkTimestampStartQuery = device.CreateQuery(
                new QueryDescription(QueryType.Timestamp, QueryFlags.None));
            benchmarkTimestampEndQuery = device.CreateQuery(
                new QueryDescription(QueryType.Timestamp, QueryFlags.None));
        }

        void EnsureBenchmarkPassTimestampQueries(int capacity)
        {
            while (benchmarkPassTimestampQueries.Count < capacity)
            {
                benchmarkPassTimestampQueries.Add(device.CreateQuery(
                    new QueryDescription(QueryType.Timestamp, QueryFlags.None)));
            }
        }

        void BeginGpuPassTimestampStep()
        {
            if (!benchmarkPassTimestampProfiling) return;
            if (benchmarkPassTimestampStepOpen)
            {
                throw new InvalidOperationException("A profiled GPU step is already open.");
            }
            benchmarkPassTimestampStepOrdinal++;
            benchmarkPassTimestampStepOpen = true;
            MarkGpuPassTimestampBoundary("Other");
        }

        void MarkGpuPassTimestampBoundary(string nextPassName)
        {
            if (!benchmarkPassTimestampProfiling) return;
            if (benchmarkPassTimestampCount >= benchmarkPassTimestampQueries.Count
                || benchmarkPassTimestampCount >= benchmarkPassTimestampNames.Length)
            {
                benchmarkPassTimestampOverflow = true;
                return;
            }

            int index = benchmarkPassTimestampCount++;
            benchmarkPassTimestampNames[index] = nextPassName;
            benchmarkPassTimestampStepOrdinals[index] = benchmarkPassTimestampStepOrdinal;
            context.End(benchmarkPassTimestampQueries[index]);
        }

        void EndGpuPassTimestampStep()
        {
            if (!benchmarkPassTimestampProfiling) return;
            if (!benchmarkPassTimestampStepOpen)
            {
                throw new InvalidOperationException("No profiled GPU step is open.");
            }
            MarkGpuPassTimestampBoundary(null);
            benchmarkPassTimestampStepOpen = false;
        }

        public bool TryCompletePopulationReadback(int[] groupPopulations, out int totalPopulation)
        {
            totalPopulation = particleCount;
            if (groupPopulations == null
                || groupPopulations.Length < groupCount
                || populationAsyncReadback == null)
            {
                return false;
            }

            Array.Clear(populationReadbackAttempted, 0, populationReadbackAttempted.Length);
            for (int attempt = 0; attempt < PopulationReadbackBufferCount; attempt++)
            {
                int index = FindNewestPendingPopulationReadback(populationReadbackAttempted);
                if (index < 0)
                {
                    return false;
                }

                populationReadbackAttempted[index] = true;
                ID3D11Buffer readbackBuffer = populationAsyncReadbackBuffers[index];
                if (readbackBuffer == null)
                {
                    populationReadbackPending[index] = false;
                    continue;
                }

                MappedSubresource mapped;
                Result result = context.Map(
                    readbackBuffer,
                    0,
                    MapMode.Read,
                    Vortice.Direct3D11.MapFlags.DoNotWait,
                    out mapped);
                if (result.Failure)
                {
                    const int dxgiErrorWasStillDrawing = unchecked((int)0x887A000A);
                    if (result.Code == dxgiErrorWasStillDrawing)
                    {
                        continue;
                    }

                    result.CheckError();
                }

                int sequence = populationReadbackSequences[index];
                try
                {
                    if (sequence > populationReadbackCompletedSequence)
                    {
                        Marshal.Copy(
                            mapped.DataPointer,
                            populationAsyncReadback,
                            0,
                            populationAsyncReadback.Length);
                    }
                }
                finally
                {
                    context.Unmap(readbackBuffer);
                    populationReadbackPending[index] = false;
                }

                if (sequence <= populationReadbackCompletedSequence)
                {
                    continue;
                }

                populationReadbackCompletedSequence = sequence;
                particleCount = Math.Max(0, Math.Min(particleCapacity, populationAsyncReadback[0]));
                totalPopulation = particleCount;
                for (int groupIndex = 0; groupIndex < groupCount; groupIndex++)
                {
                    groupPopulations[groupIndex] = Math.Max(0, populationAsyncReadback[4 + groupIndex]);
                }
                return true;
            }

            return false;
        }

        static int DiffusionOrderIndex(int iteration)
        {
            int index = (iteration - 1) % 6;
            return index < 0 ? 0 : index;
        }

        static int GetDiffusionAxisOrder(SolverGpuDimensionMode dimensionMode, int iteration, int[] axes)
        {
            if (dimensionMode.Tridimensional)
            {
                int orderIndex = DiffusionOrderIndex(iteration);
                axes[0] = TridimensionalDiffusionAxisOrders[orderIndex, 0];
                axes[1] = TridimensionalDiffusionAxisOrders[orderIndex, 1];
                axes[2] = TridimensionalDiffusionAxisOrders[orderIndex, 2];
                return 3;
            }

            int count = 0;
            if (!dimensionMode.PlanarYZ) axes[count++] = 0;
            if (!dimensionMode.PlanarXZ) axes[count++] = 1;
            if (!dimensionMode.PlanarXY) axes[count++] = 2;

            if (count == 2 && (DiffusionOrderIndex(iteration) & 1) == 1)
            {
                int first = axes[0];
                axes[0] = axes[1];
                axes[1] = first;
            }

            return count;
        }

        static string DiffusionTimestampPassName(int axis, bool scalarDecayFused)
        {
            if (axis == 0) return scalarDecayFused ? "DiffusionX+ScalarDecay" : "DiffusionX";
            if (axis == 1) return scalarDecayFused ? "DiffusionY+ScalarDecay" : "DiffusionY";
            return scalarDecayFused ? "DiffusionZ+ScalarDecay" : "DiffusionZ";
        }

        public void SetSharedDensityPreviewEnabled(bool enabled, SolverGpuDimensionMode dimensionMode, int previewScale)
        {
            int normalizedScale = NormalizeDensityPreviewScale(previewScale);
            if (densityPreviewScale != normalizedScale)
            {
                densityPreviewScale = normalizedScale;
                DisposeDensityPreviewTexture();
            }

            enableSharedDensityPreview = enabled;
            if (!enabled || densityPreviewTexture != null)
            {
                return;
            }

            CreateDensityPreviewTexture(dimensionMode);
        }

        static int NormalizeDensityPreviewScale(int scale)
        {
            return scale >= 10 ? 10 : 1;
        }

        static int NormalizeTrailPreviewSize(int trailSize)
        {
            if (trailSize < 2) return 0;
            if (trailSize > 512) return 512;
            return trailSize;
        }

        int ClampTrailPreviewSizeForParticleCount(int trailSize)
        {
            trailSize = NormalizeTrailPreviewSize(trailSize);
            if (trailSize <= 1 || particleCapacity <= 0) return 0;

            int maxSamplesForParticles = MaxParticleTrailPreviewTexels / Math.Max(1, particleCapacity);
            if (maxSamplesForParticles < 2) return 0;
            return Math.Min(trailSize, maxSamplesForParticles);
        }

        public void SetSharedParticlePreviewEnabled(bool enabled)
        {
            enableSharedParticlePreview = enabled;
            if (!enabled || particlePreviewTexture != null)
            {
                return;
            }

            CreateParticlePreviewTexture();
        }

        public void RefreshParticlePreview(SolverGpuSettings settings, SolverGpuDimensionMode dimensionMode, int iteration)
        {
            if (!enableSharedParticlePreview)
            {
                SetSharedParticlePreviewEnabled(true);
            }

            DispatchParticlePreviewPass(settings ?? new SolverGpuSettings(), dimensionMode, iteration);
        }

        public void RefreshParticleTrailPreview(SolverGpuSettings settings, SolverGpuDimensionMode dimensionMode, int iteration)
        {
            settings = settings ?? new SolverGpuSettings();
            if (!enableSharedParticleTrailPreview)
            {
                SetSharedParticleTrailPreviewEnabled(true, settings.TrailSize);
            }

            DispatchParticleTrailPreviewPass(settings, dimensionMode, iteration);
        }

        public void SetSharedParticleTrailPreviewEnabled(bool enabled, int trailSize)
        {
            bool wasEnabled = enableSharedParticleTrailPreview;
            int normalizedTrailSize = ClampTrailPreviewSizeForParticleCount(trailSize);
            if (particleTrailPreviewTrailSize != normalizedTrailSize)
            {
                DisposeParticleTrailPreviewTexture();
                particleTrailPreviewTrailSize = normalizedTrailSize;
            }

            enableSharedParticleTrailPreview = enabled && normalizedTrailSize > 1;
            if (!wasEnabled
                && enableSharedParticleTrailPreview
                && particleTrailPreviewLastDispatchIteration >= 0
                && lastSolverIteration > particleTrailPreviewLastDispatchIteration)
            {
                ResetParticleTrailPreviewHistory();
            }

            if (!enableSharedParticleTrailPreview || particleTrailPreviewTexture != null)
            {
                return;
            }

            CreateParticleTrailPreviewTexture(normalizedTrailSize);
        }

        void ResetParticleTrailPreviewHistory()
        {
            particleTrailPreviewHeadIndex = 0;
            particleTrailPreviewValidCount = 0;
            particleTrailPreviewLastDispatchIteration = -1;
            particleTrailPreviewVersion++;
        }

        public GpuBackendCapabilities Capabilities { get; private set; }

        public GpuFullSolverStepResult Step(SolverGpuSettings settings, GpuStepRequest request)
        {
            SolverGpuDimensionMode dimensionMode = SolverGpuDimensionMode.FromResolution(resX, resY, resZ);
            return Step(
                settings,
                dimensionMode,
                request.Iteration,
                request.Requires(GpuStepDemand.SynchronizeVoxels),
                request.Requires(GpuStepDemand.SynchronizeParticles),
                request.Requires(GpuStepDemand.BuildCpuPreviewCache));
        }

        public GpuFullSolverStepResult Step(
            SolverGpuSettings settings,
            SolverGpuDimensionMode dimensionMode,
            int iteration,
            bool syncVoxels,
            bool syncParticleState,
            bool buildPreviewCache)
        {
            lastSolverIteration = iteration;
            Stopwatch total = Stopwatch.StartNew();
            Stopwatch stage = Stopwatch.StartNew();
            int passCount = 0;
            bool movedParticles = false;
            BeginGpuPassTimestampStep();

            if (settings.WrapBoundaries != wrapBoundaryState)
            {
                MarkGpuPassTimestampBoundary("BoundaryTransition");
                DispatchBoundaryModeTransition(settings, dimensionMode, iteration);
                wrapBoundaryState = settings.WrapBoundaries;
                DisposeStaticFieldPreviewTexture(VoxelPreviewField.MaximumDensity);
                staticFieldPreviewScaleValid[VoxelPreviewField.MaximumDensity] = false;
                // Boundary transitions use the same persistent ownership update as
                // movement and cannot change active/free/group totals. The legacy
                // rebuild remains available for same-binary validation.
                if (forceFullParticleCountRebuildForValidation)
                {
                    MarkGpuPassTimestampBoundary("ClearCounts.LegacyVoxel");
                    DispatchClearParticleCounts(iteration, true);
                    MarkGpuPassTimestampBoundary("CountParticles");
                    DispatchCountParticles(iteration);
                }
            }

            if (processDensity)
            {
                EnsureWeights(settings.DiffuseRange, settings.DiffusionGradual);
            }

            if (particleCount > 0 && iteration > 1)
            {
                MarkGpuPassTimestampBoundary("Move");
                DispatchMoveParticlesAndDeposit(settings, dimensionMode, iteration);
                MarkGpuPassTimestampBoundary(
                    forceParticleDrivenDepositForValidation
                        ? "ApplyDeposits.ParticleExperimental"
                        : "ApplyDeposits.CoalescedVoxel");
                DispatchApplyDeposits(settings, dimensionMode, iteration);
                // Movement maintains binary voxel occupancy incrementally. Static
                // populations also keep their aggregate counters unchanged, so the
                // old 27M-voxel clear and capacity-wide recount are unnecessary.
                // Dynamic populations retain the recount because it rebuilds the
                // legacy free-slot ordering before death/division.
                if (settings.DynamicPopulation || forceFullParticleCountRebuildForValidation)
                {
                    MarkGpuPassTimestampBoundary(
                        forceFullParticleCountRebuildForValidation
                            ? "ClearCounts.LegacyVoxel"
                            : "ClearCounts.Aggregates");
                    DispatchClearParticleCounts(
                        iteration,
                        forceFullParticleCountRebuildForValidation);
                    MarkGpuPassTimestampBoundary("CountParticles");
                    DispatchCountParticles(iteration);
                }
                movedParticles = true;
            }
            else if (particleCount > 0)
            {
                // V3 still runs particleCheckParentVoxel during iterations 0 and 1.
                // Movement is gated, but every live particle ages once on each of
                // those warm-up solutions before iteration 2 starts sensing.
                MarkGpuPassTimestampBoundary("AdvanceAges");
                DispatchAdvanceParticleAges(iteration);
            }

            stage.Stop();
            double particleMs = stage.Elapsed.TotalMilliseconds;

            stage.Restart();
            if (settings.DynamicPopulation && particleCapacity > 0 && iteration > 1)
            {
                MarkGpuPassTimestampBoundary("DynamicPopulation");
                DispatchDynamicPopulation(settings, dimensionMode, iteration);
                if (!syncParticleState)
                {
                    // Stage only four global counters plus one counter per group.
                    // The frontend polls this ring next solution with DoNotWait.
                    QueuePopulationReadback();
                }
            }
            stage.Stop();
            double populationMs = stage.Elapsed.TotalMilliseconds;

            stage.Restart();
            if (hasSlimeParticles && foodSourceOffset >= 0)
            {
                MarkGpuPassTimestampBoundary("FoodProjection");
                DispatchFoodSourceProjection(settings, dimensionMode, iteration);
                passCount++;
            }

            bool scalarDecayFused = false;
            if (processDensity && (settings.Diffuse > 0 || settings.DiffusionGradual < 1))
            {
                int axisCount = GetDiffusionAxisOrder(dimensionMode, iteration, diffusionAxisScratch);
                double strength = GradualDiffusionStrength(settings.Diffuse, settings.DiffusionGradual);
                double retention = GradualDiffusionRetention(settings.Diffuse, settings.DiffusionGradual);
                double baseKeep = 1 - strength;

                for (int i = 0; i < axisCount; i++)
                {
                    // Retention applies only on the final axis so a multi-axis
                    // pass does not compound it, matching V3.
                    bool finalAxis = i == axisCount - 1;
                    bool fuseDecay = finalAxis && !disableScalarDecayFusionForValidation;
                    double finalScale = finalAxis ? retention : 1;
                    MarkGpuPassTimestampBoundary(
                        DiffusionTimestampPassName(diffusionAxisScratch[i], fuseDecay));
                    DispatchDiffusionPass(
                        diffusionAxisScratch[i],
                        settings,
                        dimensionMode,
                        iteration,
                        baseKeep * finalScale,
                        strength * finalScale,
                        fuseDecay);
                    SwapDensityBuffers();
                    passCount++;
                    scalarDecayFused |= fuseDecay;
                }
            }

            if (processDensity && !scalarDecayFused)
            {
                MarkGpuPassTimestampBoundary("ScalarDecay");
                DispatchDecayPass(settings, dimensionMode, iteration);
                SwapDensityBuffers();
                passCount++;
            }
            if (hasAntParticles)
            {
                MarkGpuPassTimestampBoundary("AntFoodField");
                passCount += DispatchAntPheromoneField(true, settings, dimensionMode, iteration);
                MarkGpuPassTimestampBoundary("AntBaseField");
                passCount += DispatchAntPheromoneField(false, settings, dimensionMode, iteration);
            }
            if (enableSharedDensityPreview)
            {
                MarkGpuPassTimestampBoundary("DensityPreview");
                DispatchSelectedDensityPreviewPass(settings, dimensionMode, iteration);
            }
            if (enableSharedParticlePreview)
            {
                MarkGpuPassTimestampBoundary("ParticlePreview");
                DispatchParticlePreviewPass(settings, dimensionMode, iteration);
            }
            if (enableSharedParticleTrailPreview)
            {
                MarkGpuPassTimestampBoundary("TrailPreview");
                DispatchParticleTrailPreviewPass(settings, dimensionMode, iteration);
            }
            stage.Stop();
            double diffusionMs = stage.Elapsed.TotalMilliseconds;

            stage.Restart();
            MarkGpuPassTimestampBoundary("Readback");
            if (syncVoxels)
            {
                if (processDensity) ReadBackDensity();
                if (hasAntParticles) ReadBackAntFields();
                ApplyDynamicFieldsToOutput();
            }

            bool builtPreviewCache = false;
            bool completedPreviewReadback = false;
            bool queuedPreviewReadback = false;
            if (syncParticleState)
            {
                ClearPendingPreviewReadbacks();
                ReadBackParticles();
                builtPreviewCache = ApplyParticlesToOutput(settings, iteration, buildPreviewCache);
            }
            else if (buildPreviewCache)
            {
                queuedPreviewReadback = QueuePreviewCacheReadback();
            }
            stage.Stop();
            double readbackMs = stage.Elapsed.TotalMilliseconds;
            EndGpuPassTimestampStep();

            total.Stop();

            return new GpuFullSolverStepResult
            {
                TotalMilliseconds = total.Elapsed.TotalMilliseconds,
                ParticleMilliseconds = particleMs,
                PopulationMilliseconds = populationMs,
                DiffusionMilliseconds = diffusionMs,
                ReadbackMilliseconds = readbackMs,
                Passes = passCount,
                Range = settings.DiffuseRange,
                Wrap = settings.WrapBoundaries,
                ParticleCount = particleCount,
                MovedParticles = movedParticles,
                SyncedVoxels = syncVoxels,
                SyncedParticles = syncParticleState,
                BuiltPreviewCache = builtPreviewCache,
                QueuedPreviewReadback = queuedPreviewReadback,
                CompletedPreviewReadback = completedPreviewReadback
            };
        }

        public int SynchronizeParticleOutput(SolverGpuSettings settings, int iteration)
        {
            ClearPendingPreviewReadbacks();
            ReadBackParticles();
            ApplyParticlesToOutput(settings, iteration, false);
            return particleCount;
        }

        public void SynchronizeVoxelOutput()
        {
            if (processDensity) ReadBackDensity();
            if (hasAntParticles) ReadBackAntFields();
            ApplyDynamicFieldsToOutput();
        }

        void DispatchMoveParticlesAndDeposit(SolverGpuSettings settings, SolverGpuDimensionMode dimensionMode, int iteration)
        {
            if (particleCapacity <= 0 || particlePositionView == null || particleOwnerView == null)
            {
                return;
            }

            UpdateParameters(CreateParameters(0, settings, dimensionMode, iteration));

            context.CSSetShader(hasAntParticles && !hasSlimeParticles ? antMoveShader : moveShader);
            context.CSSetConstantBuffers(0, new ID3D11Buffer[] { parameterBuffer });
            context.CSSetShaderResources(1, new ID3D11ShaderResourceView[] { groupData0View, groupData1View, voxelFlagsView, null, voxelBehaviorView, voxelVectorView, voxelDensityLimitsView });
            context.CSSetShaderResource(11, voxelVectorFrequencyView);
            context.CSSetShaderResource(12, activeVoxelFlagsView);
            if (hasAntParticles)
            {
                context.CSSetShaderResource(8, CurrentAntFoodResourceView());
                context.CSSetShaderResource(9, CurrentAntBaseResourceView());
                context.CSSetUnorderedAccessView(7, particleHomeView, -1);
            }
            context.CSSetUnorderedAccessView(0, CurrentDensityView(), -1);
            context.CSSetUnorderedAccessView(1, particleOwnerView, -1);
            context.CSSetUnorderedAccessView(2, particlePositionView, -1);
            context.CSSetUnorderedAccessView(3, particleDirectionView, -1);
            context.CSSetUnorderedAccessView(4, particleYAxisView, -1);
            context.CSSetUnorderedAccessView(5, particleCountView, -1);
            context.CSSetUnorderedAccessView(6, depositView, -1);
            DispatchLinear256(particleCapacity);
            UnbindComputeResources();
        }

        /// <summary>
        /// Rebuilds the persistent voxel-owner map from particle positions. An
        /// atomic minimum makes duplicate resolution stable: the lowest live slot
        /// owns the voxel and every other claimant is retired before recounting.
        /// </summary>
        void DispatchRebuildParticleOwnership(
            SolverGpuSettings settings,
            SolverGpuDimensionMode dimensionMode,
            int iteration)
        {
            if (particleCapacity <= 0
                || particleOwnerView == null
                || claimParticleOwnersShader == null
                || cullParticleOwnerConflictsShader == null)
            {
                return;
            }

            context.ClearUnorderedAccessView(
                particleOwnerView,
                new Vortice.Mathematics.Int4(-1));

            UpdateParameters(CreateParameters(0, settings, dimensionMode, iteration));
            context.CSSetShader(claimParticleOwnersShader);
            context.CSSetConstantBuffers(0, new ID3D11Buffer[] { parameterBuffer });
            context.CSSetShaderResource(3, voxelFlagsView);
            context.CSSetShaderResource(12, activeVoxelFlagsView);
            context.CSSetUnorderedAccessView(1, particleOwnerView, -1);
            context.CSSetUnorderedAccessView(2, particlePositionView, -1);
            DispatchLinear256(particleCapacity);
            UnbindComputeResources();

            context.CSSetShader(cullParticleOwnerConflictsShader);
            context.CSSetConstantBuffers(0, new ID3D11Buffer[] { parameterBuffer });
            context.CSSetShaderResource(3, voxelFlagsView);
            context.CSSetShaderResource(12, activeVoxelFlagsView);
            context.CSSetUnorderedAccessView(1, particleOwnerView, -1);
            context.CSSetUnorderedAccessView(2, particlePositionView, -1);
            context.CSSetUnorderedAccessView(3, particleDirectionView, -1);
            context.CSSetUnorderedAccessView(4, particleYAxisView, -1);
            DispatchLinear256(particleCapacity);
            UnbindComputeResources();
        }

        void DispatchBoundaryModeTransition(SolverGpuSettings settings, SolverGpuDimensionMode dimensionMode, int iteration)
        {
            if (particleCapacity <= 0
                || boundaryModeTransitionShader == null
                || particlePositionView == null
                || particleOwnerView == null)
            {
                return;
            }

            UpdateParameters(CreateParameters(0, settings, dimensionMode, iteration));
            context.CSSetShader(boundaryModeTransitionShader);
            context.CSSetConstantBuffers(0, new ID3D11Buffer[] { parameterBuffer });
            context.CSSetShaderResource(3, voxelFlagsView);
            context.CSSetShaderResource(12, activeVoxelFlagsView);
            context.CSSetUnorderedAccessView(1, particleOwnerView, -1);
            context.CSSetUnorderedAccessViews(2, new ID3D11UnorderedAccessView[]
            {
                particlePositionView,
                particleDirectionView,
                particleYAxisView
            });
            context.CSSetUnorderedAccessView(5, particleCountView, -1);
            DispatchLinear256(particleCapacity);
            UnbindComputeResources();
        }

        void DispatchFoodSourceProjection(SolverGpuSettings settings, SolverGpuDimensionMode dimensionMode, int iteration)
        {
            if (foodSourceOffset < 0 || projectFoodSourcesShader == null) return;

            UpdateParameters(CreateParameters(0, settings, dimensionMode, iteration));

            context.CSSetShader(projectFoodSourcesShader);
            context.CSSetConstantBuffers(0, new ID3D11Buffer[] { parameterBuffer });
            context.CSSetShaderResource(3, voxelFlagsView);
            context.CSSetShaderResource(7, voxelDensityLimitsView);
            context.CSSetShaderResource(12, activeVoxelFlagsView);
            context.CSSetUnorderedAccessView(0, CurrentDensityView(), -1);
            context.CSSetUnorderedAccessView(6, depositView, -1);
            DispatchLinear256(voxelCount);
            UnbindComputeResources();
        }

        void DispatchApplyDeposits(SolverGpuSettings settings, SolverGpuDimensionMode dimensionMode, int iteration)
        {
            FullSolverParameters parameters = CreateParameters(0, settings, dimensionMode, iteration);
            // ApplyDeposits uses this otherwise-irrelevant parameter as an internal
            // dispatch-mode selector without changing the preserved cbuffer ABI.
            parameters.PreviewPadding0 = forceParticleDrivenDepositForValidation ? 0 : 1;
            UpdateParameters(parameters);

            context.CSSetShader(applyDepositsShader);
            context.CSSetConstantBuffers(0, new ID3D11Buffer[] { parameterBuffer });
            context.CSSetShaderResource(3, voxelFlagsView);
            context.CSSetShaderResource(7, voxelDensityLimitsView);
            context.CSSetShaderResource(12, activeVoxelFlagsView);
            context.CSSetUnorderedAccessView(0, CurrentDensityView(), -1);
            if (hasAntParticles)
            {
                context.CSSetUnorderedAccessView(1, CurrentAntFoodView(), -1);
                context.CSSetUnorderedAccessView(7, CurrentAntBaseView(), -1);
            }
            if (forceParticleDrivenDepositForValidation)
            {
                context.CSSetUnorderedAccessView(2, particlePositionView, -1);
                context.CSSetUnorderedAccessView(3, particleDirectionView, -1);
            }
            context.CSSetUnorderedAccessView(6, depositView, -1);
            DispatchLinear256(
                forceParticleDrivenDepositForValidation ? particleCapacity : voxelCount);
            UnbindComputeResources();
        }

        void DispatchClearParticleCounts(int iteration, bool clearVoxelOccupancy)
        {
            if (particleCountView == null)
            {
                return;
            }

            FullSolverParameters parameters = CreateParameters(
                0,
                new SolverGpuSettings(),
                SolverGpuDimensionMode.FromResolution(resX, resY, resZ),
                iteration);
            // ClearParticleCounts shares the selector with ApplyDeposits. Zero is
            // the production counter-only path; one reproduces the old full scan.
            parameters.PreviewPadding0 = clearVoxelOccupancy ? 1 : 0;
            UpdateParameters(parameters);

            context.CSSetShader(clearCountsShader);
            context.CSSetConstantBuffers(0, new ID3D11Buffer[] { parameterBuffer });
            context.CSSetUnorderedAccessView(5, particleCountView, -1);
            DispatchLinear256(Math.Max(clearVoxelOccupancy ? voxelCount : 1, groupCount));
            UnbindComputeResources();
        }

        void DispatchCountParticles(int iteration)
        {
            if (particleCapacity <= 0 || particleCountView == null)
            {
                return;
            }

            UpdateParameters(CreateParameters(0, new SolverGpuSettings(), SolverGpuDimensionMode.FromResolution(resX, resY, resZ), iteration));

            context.CSSetShader(countParticlesShader);
            context.CSSetConstantBuffers(0, new ID3D11Buffer[] { parameterBuffer });
            context.CSSetShaderResource(3, voxelFlagsView);
            context.CSSetShaderResource(12, activeVoxelFlagsView);
            context.CSSetUnorderedAccessView(1, particleOwnerView, -1);
            context.CSSetUnorderedAccessView(2, particlePositionView, -1);
            context.CSSetUnorderedAccessView(3, particleDirectionView, -1);
            context.CSSetUnorderedAccessView(5, particleCountView, -1);
            context.CSSetUnorderedAccessView(6, depositView, -1);
            DispatchLinear256(particleCapacity);
            UnbindComputeResources();
        }

        void DispatchAdvanceParticleAges(int iteration)
        {
            if (particleCapacity <= 0 || advanceParticleAgesShader == null || depositView == null)
            {
                return;
            }

            UpdateParameters(CreateParameters(
                0,
                new SolverGpuSettings(),
                SolverGpuDimensionMode.FromResolution(resX, resY, resZ),
                iteration));

            context.CSSetShader(advanceParticleAgesShader);
            context.CSSetConstantBuffers(0, new ID3D11Buffer[] { parameterBuffer });
            context.CSSetUnorderedAccessView(2, particlePositionView, -1);
            context.CSSetUnorderedAccessView(6, depositView, -1);
            DispatchLinear256(particleCapacity);
            UnbindComputeResources();
        }

        void DispatchDynamicPopulation(SolverGpuSettings settings, SolverGpuDimensionMode dimensionMode, int iteration)
        {
            if (particleCapacity <= 0 || particleCountView == null || depositView == null)
            {
                return;
            }

            bool randomDue = settings.RandomPopulationFrequency > 0
                && iteration % settings.RandomPopulationFrequency == 0;

            // V3 applies population changes in four distinct stages. In particular,
            // random death runs after normal division and therefore samples the
            // post-division population, including normal newborns.
            bool deathRuleDue = settings.Death && iteration % settings.DeathFrequency == 0;
            bool deathRandomDue = settings.RandomDeathProbability > 0 && randomDue;
            bool divisionRuleDue = settings.Division && iteration % settings.DivisionFrequency == 0;
            bool divisionRandomDue = settings.RandomDivisionProbability > 0 && randomDue;

            if (settings.Death || settings.Division)
            {
                int checkRange = settings.Death && settings.Division
                    ? Math.Max(settings.DeathRange, settings.DivisionRange)
                    : settings.Death
                        ? settings.DeathRange
                        : settings.DivisionRange;

                if (checkRange > 0)
                {
                    // V3 refreshes both observable per-particle fields before any
                    // population mutation, even when one rule is disabled or neither
                    // frequency is due. With only one rule enabled, its shared scan
                    // range truncates the disabled field.
                    int deathRange = Math.Min(settings.DeathRange, checkRange);
                    int divisionRange = Math.Min(settings.DivisionRange, checkRange);
                    PublishPopulationNeighbourCounts(
                        deathRange,
                        divisionRange,
                        settings,
                        dimensionMode,
                        iteration);

                    // The published values are the pre-death snapshot V3 consumes.
                    // Reading them per particle also preserves different stale values
                    // for particles sharing a voxel when a later range is zero.
                    if (deathRuleDue)
                    {
                        DispatchParticleDeath(
                            NeighbourCountsWithoutRebuild(),
                            settings,
                            dimensionMode,
                            iteration,
                            PopulationNeighbourApplyStored,
                            false);
                    }

                    if (divisionRuleDue)
                    {
                        DispatchParticleDivision(
                            NeighbourCountsWithoutRebuild(),
                            settings,
                            dimensionMode,
                            iteration,
                            PopulationNeighbourApplyStored,
                            false);

                        // V3 recounts both fields after normal division and before
                        // random population changes. Normal newborns are included;
                        // random newborns therefore retain their default zero counts.
                        PublishPopulationNeighbourCounts(
                            deathRange,
                            divisionRange,
                            settings,
                            dimensionMode,
                            iteration);
                    }
                }
                else
                {
                    // V3's checkRange <= 0 path does not clear or recalculate either
                    // field. Normal rules still run against the stored/default values.
                    if (deathRuleDue)
                    {
                        DispatchParticleDeath(
                            NeighbourCountsWithoutRebuild(),
                            settings,
                            dimensionMode,
                            iteration,
                            PopulationNeighbourApplyStored,
                            false);
                    }

                    if (divisionRuleDue)
                    {
                        DispatchParticleDivision(
                            NeighbourCountsWithoutRebuild(),
                            settings,
                            dimensionMode,
                            iteration,
                            PopulationNeighbourApplyStored,
                            false);
                    }
                }
            }

            if (deathRandomDue)
            {
                DispatchParticleDeath(
                    NeighbourCountsWithoutRebuild(),
                    settings,
                    dimensionMode,
                    iteration,
                    PopulationNeighbourDisabled,
                    true);
            }

            // V3 applies random division after normal division and samples the
            // survivors of random death, including surviving normal newborns.
            if (divisionRandomDue)
            {
                DispatchParticleDivision(
                    NeighbourCountsWithoutRebuild(),
                    settings,
                    dimensionMode,
                    iteration,
                    PopulationNeighbourDisabled,
                    true);
            }
        }

        void PublishPopulationNeighbourCounts(
            int deathRange,
            int divisionRange,
            SolverGpuSettings settings,
            SolverGpuDimensionMode dimensionMode,
            int iteration)
        {
            if (deathRange == divisionRange && deathRange >= 0)
            {
                ID3D11UnorderedAccessView sharedView = DispatchBuildNeighbourCounts(
                    deathRange,
                    settings,
                    dimensionMode,
                    iteration);
                DispatchParticleDeath(
                    sharedView,
                    settings,
                    dimensionMode,
                    iteration,
                    PopulationNeighbourPublishOnly,
                    false);
                DispatchParticleDivision(
                    sharedView,
                    settings,
                    dimensionMode,
                    iteration,
                    PopulationNeighbourPublishOnly,
                    false);
                return;
            }

            ID3D11UnorderedAccessView deathView = deathRange < 0
                ? NeighbourCountsWithoutRebuild()
                : DispatchBuildNeighbourCounts(
                    deathRange,
                    settings,
                    dimensionMode,
                    iteration);
            DispatchParticleDeath(
                deathView,
                settings,
                dimensionMode,
                iteration,
                deathRange < 0
                    ? PopulationNeighbourPublishZero
                    : PopulationNeighbourPublishOnly,
                false);

            ID3D11UnorderedAccessView divisionView = divisionRange < 0
                ? NeighbourCountsWithoutRebuild()
                : DispatchBuildNeighbourCounts(
                    divisionRange,
                    settings,
                    dimensionMode,
                    iteration);
            DispatchParticleDivision(
                divisionView,
                settings,
                dimensionMode,
                iteration,
                divisionRange < 0
                    ? PopulationNeighbourPublishZero
                    : PopulationNeighbourPublishOnly,
                false);
        }

        /// <summary>
        /// Bound placeholder for passes that consume published per-particle counts or
        /// ignore neighbour counts entirely. The Source UAV is not read by those modes.
        /// </summary>
        ID3D11UnorderedAccessView NeighbourCountsWithoutRebuild()
        {
            EnsureNeighbourBuffers();
            return neighbourCountAView;
        }

        ID3D11UnorderedAccessView DispatchBuildNeighbourCounts(int range, SolverGpuSettings settings, SolverGpuDimensionMode dimensionMode, int iteration)
        {
            EnsureNeighbourBuffers();
            FullSolverParameters parameters = CreateParameters(0, settings, dimensionMode, iteration);
            parameters.Range = Math.Max(0, range);
            UpdateParameters(parameters);

            context.CSSetShader(seedNeighbourCountsShader);
            context.CSSetConstantBuffers(0, new ID3D11Buffer[] { parameterBuffer });
            context.CSSetUnorderedAccessView(1, neighbourCountAView, -1);
            context.CSSetUnorderedAccessView(5, particleCountView, -1);
            DispatchLinear256(voxelCount);
            UnbindComputeResources();

            ID3D11UnorderedAccessView current = neighbourCountAView;
            ID3D11UnorderedAccessView next = neighbourCountBView;
            int axisCount = GetDiffusionAxisOrder(dimensionMode, iteration, diffusionAxisScratch);
            for (int i = 0; i < axisCount; i++)
            {
                int axis = diffusionAxisScratch[i];
                parameters.Axis = axis;
                UpdateParameters(parameters);

                context.CSSetShader(sumNeighbourAxisShader);
                context.CSSetConstantBuffers(0, new ID3D11Buffer[] { parameterBuffer });
                context.CSSetUnorderedAccessView(0, current, -1);
                context.CSSetUnorderedAccessView(1, next, -1);
                DispatchLinear64(NeighbourLineCount(axis));
                UnbindComputeResources();

                ID3D11UnorderedAccessView swap = current;
                current = next;
                next = swap;
            }

            return current;
        }

        int NeighbourLineCount(int axis)
        {
            if (axis == 0) return resY * resZ;
            if (axis == 1) return resX * resZ;
            return resX * resY;
        }

        void DispatchParticleDeath(
            ID3D11UnorderedAccessView neighbourView,
            SolverGpuSettings settings,
            SolverGpuDimensionMode dimensionMode,
            int iteration,
            int neighbourMode,
            bool enableRandomRule)
        {
            if (neighbourView == null || applyParticleDeathShader == null)
            {
                return;
            }

            FullSolverParameters parameters = CreateParameters(0, settings, dimensionMode, iteration);
            parameters.DeathEnabled = neighbourMode;
            parameters.RandomDeathProbability = enableRandomRule
                ? (float)settings.RandomDeathProbability
                : 0;
            UpdateParameters(parameters);
            context.CSSetShader(applyParticleDeathShader);
            context.CSSetConstantBuffers(0, new ID3D11Buffer[] { parameterBuffer });
            context.CSSetUnorderedAccessView(0, neighbourView, -1);
            context.CSSetUnorderedAccessView(1, particleOwnerView, -1);
            context.CSSetUnorderedAccessView(2, particlePositionView, -1);
            context.CSSetUnorderedAccessView(3, particleDirectionView, -1);
            context.CSSetUnorderedAccessView(4, particleYAxisView, -1);
            context.CSSetUnorderedAccessView(5, particleCountView, -1);
            context.CSSetUnorderedAccessView(6, depositView, -1);
            DispatchLinear256(particleCapacity);
            UnbindComputeResources();
        }

        void DispatchParticleDivision(
            ID3D11UnorderedAccessView neighbourView,
            SolverGpuSettings settings,
            SolverGpuDimensionMode dimensionMode,
            int iteration,
            int neighbourMode,
            bool enableRandomRule)
        {
            if (neighbourView == null || applyParticleDivisionShader == null)
            {
                return;
            }

            FullSolverParameters parameters = CreateParameters(0, settings, dimensionMode, iteration);
            parameters.DivisionEnabled = neighbourMode;
            parameters.RandomDivisionProbability = enableRandomRule
                ? (float)settings.RandomDivisionProbability
                : 0;
            UpdateParameters(parameters);
            context.CSSetShader(applyParticleDivisionShader);
            context.CSSetConstantBuffers(0, new ID3D11Buffer[] { parameterBuffer });
            context.CSSetShaderResources(2, new ID3D11ShaderResourceView[]
            {
                groupData1View,
                voxelFlagsView,
                null,
                null,
                null,
                voxelDensityLimitsView
            });
            context.CSSetUnorderedAccessView(0, neighbourView, -1);
            context.CSSetUnorderedAccessView(1, particleOwnerView, -1);
            context.CSSetUnorderedAccessView(2, particlePositionView, -1);
            context.CSSetUnorderedAccessView(3, particleDirectionView, -1);
            context.CSSetUnorderedAccessView(4, particleYAxisView, -1);
            context.CSSetUnorderedAccessView(5, particleCountView, -1);
            context.CSSetUnorderedAccessView(6, depositView, -1);
            context.CSSetUnorderedAccessView(7, particleHomeView, -1);
            DispatchLinear256(particleCapacity);
            UnbindComputeResources();
        }

        void DispatchDiffusionPass(
            int axis,
            SolverGpuSettings settings,
            SolverGpuDimensionMode dimensionMode,
            int iteration,
            double keep,
            double diffuseAmount,
            bool applyDecay)
        {
            FullSolverParameters parameters = CreateParameters(axis, settings, dimensionMode, iteration);
            parameters.Keep = (float)keep;
            parameters.Diffuse = (float)diffuseAmount;
            parameters.ApplyScalarDecayAfterDiffusion = applyDecay ? 1 : 0;
            UpdateParameters(parameters);

            ID3D11ComputeShader selectedShader;
            int groupsX;
            int groupsY;
            int groupsZ;
            bool tiled = TryGetTiledDiffusionDispatch(
                axis,
                settings.DiffuseRange,
                dimensionMode,
                out selectedShader,
                out groupsX,
                out groupsY,
                out groupsZ);

            context.CSSetShader(selectedShader);
            context.CSSetConstantBuffers(0, new ID3D11Buffer[] { parameterBuffer });
            context.CSSetShaderResources(0, new ID3D11ShaderResourceView[] { weightsView });
            context.CSSetShaderResource(3, voxelFlagsView);
            context.CSSetShaderResource(7, voxelDensityLimitsView);
            context.CSSetShaderResource(12, activeVoxelFlagsView);
            context.CSSetUnorderedAccessView(0, CurrentDensityView(), -1);
            context.CSSetUnorderedAccessView(1, NextDensityView(), -1);
            if (applyDecay)
            {
                context.CSSetUnorderedAccessView(5, particleCountView, -1);
            }

            if (tiled)
            {
                context.Dispatch(groupsX, groupsY, groupsZ);
            }
            else
            {
                DispatchLinear256(voxelCount);
            }
            UnbindComputeResources();
        }

        bool TryGetTiledDiffusionDispatch(
            int axis,
            int range,
            SolverGpuDimensionMode dimensionMode,
            out ID3D11ComputeShader shader,
            out int groupsX,
            out int groupsY,
            out int groupsZ)
        {
            shader = diffusionShader;
            groupsX = 0;
            groupsY = 0;
            groupsZ = 0;

            if (forceDirectDiffusionForValidation
                || !dimensionMode.Tridimensional
                || range < 2
                || range > MaximumTiledDiffusionRange)
            {
                return false;
            }

            if (axis == 0)
            {
                shader = diffusionXTiledShader;
                groupsX = DivideRoundUpTiledDiffusion(resX);
                groupsY = DivideRoundUpTiledDiffusion(resZ);
                groupsZ = resY;
            }
            else if (axis == 1)
            {
                shader = diffusionYTiledShader;
                groupsX = DivideRoundUpTiledDiffusion(resY);
                groupsY = DivideRoundUpTiledDiffusion(resZ);
                groupsZ = resX;
            }
            else if (axis == 2)
            {
                shader = diffusionZTiledShader;
                groupsX = DivideRoundUpTiledDiffusion(resZ);
                groupsY = DivideRoundUpTiledDiffusion(resY);
                groupsZ = resX;
            }
            else
            {
                shader = diffusionShader;
                return false;
            }

            if (shader == null
                || groupsX <= 0 || groupsX > 65535
                || groupsY <= 0 || groupsY > 65535
                || groupsZ <= 0 || groupsZ > 65535)
            {
                shader = diffusionShader;
                return false;
            }

            return true;
        }

        static int DivideRoundUpTiledDiffusion(int value)
        {
            return (int)(((long)value + TiledDiffusionTileSize - 1) / TiledDiffusionTileSize);
        }

        void DispatchDecayPass(SolverGpuSettings settings, SolverGpuDimensionMode dimensionMode, int iteration)
        {
            UpdateParameters(CreateParameters(0, settings, dimensionMode, iteration));

            context.CSSetShader(decayShader);
            context.CSSetConstantBuffers(0, new ID3D11Buffer[] { parameterBuffer });
            context.CSSetShaderResource(3, voxelFlagsView);
            context.CSSetShaderResource(7, voxelDensityLimitsView);
            context.CSSetShaderResource(12, activeVoxelFlagsView);
            context.CSSetUnorderedAccessView(0, CurrentDensityView(), -1);
            context.CSSetUnorderedAccessView(1, NextDensityView(), -1);
            context.CSSetUnorderedAccessView(5, particleCountView, -1);
            DispatchLinear256(voxelCount);
            UnbindComputeResources();
        }

        int DispatchAntPheromoneField(bool foodField, SolverGpuSettings settings, SolverGpuDimensionMode dimensionMode, int iteration)
        {
            float diffuse = (float)(foodField ? settings.AntFoodDiffuse : settings.AntBaseDiffuse);
            float decay = (float)(foodField ? settings.AntFoodDecay : settings.AntBaseDecay);
            int range = Math.Max(0, settings.AntDiffuseRange);
            int passes = 0;

            EnsureAntWeights(range);
            if (diffuse > 0)
            {
                int axisCount = GetDiffusionAxisOrder(dimensionMode, iteration, diffusionAxisScratch);
                for (int i = 0; i < axisCount; i++)
                {
                    FullSolverParameters parameters = CreateParameters(diffusionAxisScratch[i], settings, dimensionMode, iteration);
                    parameters.Range = range;
                    parameters.Keep = 1.0f - diffuse;
                    parameters.Diffuse = diffuse;
                    parameters.Decay = decay;
                    parameters.FieldMode = foodField ? 1 : 2;
                    UpdateParameters(parameters);
                    context.CSSetShader(diffusionShader);
                    context.CSSetConstantBuffers(0, new ID3D11Buffer[] { parameterBuffer });
                    context.CSSetShaderResource(0, antWeightsView);
                    context.CSSetShaderResource(3, voxelFlagsView);
                    context.CSSetShaderResource(7, voxelDensityLimitsView);
                    context.CSSetShaderResource(12, activeVoxelFlagsView);
                    context.CSSetUnorderedAccessView(0, foodField ? CurrentAntFoodView() : CurrentAntBaseView(), -1);
                    context.CSSetUnorderedAccessView(1, foodField ? NextAntFoodView() : NextAntBaseView(), -1);
                    DispatchLinear256(voxelCount);
                    UnbindComputeResources();
                    if (foodField) antFoodInA = !antFoodInA;
                    else antBaseInA = !antBaseInA;
                    passes++;
                }
            }

            FullSolverParameters decayParameters = CreateParameters(0, settings, dimensionMode, iteration);
            decayParameters.Decay = decay;
            decayParameters.FieldMode = foodField ? 1 : 2;
            UpdateParameters(decayParameters);
            context.CSSetShader(decayShader);
            context.CSSetConstantBuffers(0, new ID3D11Buffer[] { parameterBuffer });
            context.CSSetShaderResource(3, voxelFlagsView);
            context.CSSetShaderResource(7, voxelDensityLimitsView);
            context.CSSetShaderResource(12, activeVoxelFlagsView);
            context.CSSetUnorderedAccessView(0, foodField ? CurrentAntFoodView() : CurrentAntBaseView(), -1);
            context.CSSetUnorderedAccessView(1, foodField ? NextAntFoodView() : NextAntBaseView(), -1);
            context.CSSetUnorderedAccessView(5, particleCountView, -1);
            DispatchLinear256(voxelCount);
            UnbindComputeResources();
            if (foodField) antFoodInA = !antFoodInA;
            else antBaseInA = !antBaseInA;
            return passes + 1;
        }

        void DispatchDensityPreviewPass(SolverGpuSettings settings, SolverGpuDimensionMode dimensionMode, int iteration, ID3D11UnorderedAccessView sourceView = null)
        {
            bool colorPreview = densityPreviewColorTexture;
            ID3D11ComputeShader previewShader = colorPreview ? combinedDensityPreviewShader : densityPreviewShader;
            if (previewShader == null || densityPreviewTextureView == null || parameterBuffer == null)
            {
                WriteSharedDensityPreviewStatus("dispatch_skip missing_resource shader="
                    + (previewShader != null)
                    + " texture_uav=" + (densityPreviewTextureView != null)
                    + " parameters=" + (parameterBuffer != null));
                return;
            }

            WriteSharedDensityPreviewStatus("dispatch_begin width=" + densityPreviewWidth + " height=" + densityPreviewHeight);
            FullSolverParameters previewParameters = CreateParameters(0, settings, dimensionMode, iteration);
            previewParameters.FieldMode = densityPreviewValueIndex;
            UpdateParameters(previewParameters);
            WriteSharedDensityPreviewStatus("dispatch_parameters_ok");

            context.CSSetShader(previewShader);
            context.CSSetConstantBuffers(0, new ID3D11Buffer[] { parameterBuffer });
            context.CSSetUnorderedAccessView(0, colorPreview ? CurrentDensityView() : sourceView ?? CurrentDensityView(), -1);
            if (colorPreview)
            {
                context.CSSetUnorderedAccessView(6, depositView, -1);
            }
            context.CSSetUnorderedAccessView(7, densityPreviewTextureView, -1);
            if (colorPreview && hasAntParticles)
            {
                context.CSSetShaderResource(8, CurrentAntFoodResourceView());
                context.CSSetShaderResource(9, CurrentAntBaseResourceView());
            }
            WriteSharedDensityPreviewStatus("dispatch_bind_ok");
            context.Dispatch((densityPreviewWidth + 15) / 16, (densityPreviewHeight + 15) / 16, 1);
            WriteSharedDensityPreviewStatus("dispatch_call_ok");
            UnbindComputeResources();
            context.Flush();
            WriteSharedDensityPreviewStatus("dispatch_flush_ok");
            densityPreviewVersion++;
        }

        void DispatchSelectedDensityPreviewPass(SolverGpuSettings settings, SolverGpuDimensionMode dimensionMode, int iteration)
        {
            ID3D11UnorderedAccessView source = CurrentDensityView();
            if (!densityPreviewColorTexture && densityPreviewValueIndex == VoxelPreviewField.AntFoodPheromones && hasAntParticles)
            {
                source = CurrentAntFoodView();
            }
            else if (!densityPreviewColorTexture && densityPreviewValueIndex == VoxelPreviewField.AntBasePheromones && hasAntParticles)
            {
                source = CurrentAntBaseView();
            }
            DispatchDensityPreviewPass(settings, dimensionMode, iteration, source);
        }

        bool EnsureDensityGradientPreview()
        {
            if (densityPreviewAxisMode != 3
                || densityPreviewTextureResourceView == null
                || densityGradientPreviewShader == null
                || densityPreviewWidth <= 0
                || densityPreviewHeight <= 0)
            {
                return false;
            }

            if (densityGradientPreviewTexture == null)
            {
                CreateDensityGradientPreviewTexture();
            }

            if (densityGradientPreviewTextureView == null || densityGradientPreviewSharedHandle == IntPtr.Zero)
            {
                return false;
            }

            if (densityGradientSourceVersion == densityPreviewVersion)
            {
                return true;
            }

            // Particle and trail preview passes share this constant buffer and
            // overwrite its preview layout. Restore the density atlas layout
            // before generating gradients so this pass never depends on which
            // preview happened to dispatch last.
            FullSolverParameters parameters = CreateParameters(
                0,
                GradientPreviewSettings,
                SolverGpuDimensionMode.FromResolution(resX, resY, resZ),
                0);
            UpdateParameters(parameters);

            context.CSSetShader(densityGradientPreviewShader);
            context.CSSetConstantBuffers(0, new ID3D11Buffer[] { parameterBuffer });
            context.CSSetShaderResource(10, densityPreviewTextureResourceView);
            context.CSSetUnorderedAccessView(7, densityGradientPreviewTextureView, -1);
            context.Dispatch((densityPreviewWidth + 15) / 16, (densityPreviewHeight + 15) / 16, 1);
            UnbindComputeResources();
            context.Flush();
            densityGradientSourceVersion = densityPreviewVersion;
            return true;
        }

        void DispatchParticlePreviewPass(SolverGpuSettings settings, SolverGpuDimensionMode dimensionMode, int iteration)
        {
            if (particleCapacity <= 0 || particlePreviewShader == null || particlePreviewTextureView == null || parameterBuffer == null || groupColorDataView == null)
            {
                WriteSharedParticlePreviewStatus("dispatch_skip particle_count=" + particleCount
                    + " shader=" + (particlePreviewShader != null)
                    + " texture_uav=" + (particlePreviewTextureView != null)
                    + " parameters=" + (parameterBuffer != null)
                    + " colors=" + (groupColorDataView != null));
                return;
            }

            FullSolverParameters parameters = CreateParameters(0, settings, dimensionMode, iteration);
            parameters.PreviewWidth = particlePreviewWidth;
            parameters.PreviewHeight = particlePreviewHeight;
            UpdateParameters(parameters);

            context.CSSetShader(particlePreviewShader);
            context.CSSetConstantBuffers(0, new ID3D11Buffer[] { parameterBuffer });
            context.CSSetShaderResource(2, groupData1View);
            context.CSSetShaderResource(4, groupColorDataView);
            context.CSSetUnorderedAccessView(2, particlePositionView, -1);
            context.CSSetUnorderedAccessView(6, depositView, -1);
            context.CSSetUnorderedAccessView(7, particlePreviewTextureView, -1);
            DispatchLinear256(particleCapacity);
            UnbindComputeResources();
            context.Flush();
            particlePreviewVersion++;
        }

        void DispatchParticleTrailPreviewPass(SolverGpuSettings settings, SolverGpuDimensionMode dimensionMode, int iteration)
        {
            if (particleCapacity <= 0 || particleTrailPreviewShader == null || particleTrailPreviewTextureView == null || parameterBuffer == null)
            {
                return;
            }

            // The solver selects the active history size before each step. Re-reading
            // TrailSize here would resize and clear a hidden two-sample history on
            // every iteration, so dispatch into the texture that is already active.
            int trailSize = particleTrailPreviewTrailSize;
            if (trailSize <= 1)
            {
                return;
            }

            if (particleTrailPreviewTexture == null || particleTrailPreviewTrailSize != trailSize)
            {
                SetSharedParticleTrailPreviewEnabled(true, trailSize);
                if (particleTrailPreviewTexture == null || particleTrailPreviewTextureView == null)
                {
                    return;
                }
            }

            if (particleTrailPreviewLastDispatchIteration >= 0
                && iteration != particleTrailPreviewLastDispatchIteration + 1)
            {
                ResetParticleTrailPreviewHistory();
            }
            particleTrailPreviewLastDispatchIteration = iteration;

            if (!TryAcquireParticleTrailPreviewMutex(8))
            {
                return;
            }

            try
            {
                bool sampleTrail = settings.TrailFreq <= 1 || iteration % settings.TrailFreq == 0;
                if (sampleTrail)
                {
                    if (particleTrailPreviewValidCount == 0)
                    {
                        particleTrailPreviewHeadIndex = 0;
                        particleTrailPreviewValidCount = 1;
                    }
                    else
                    {
                        particleTrailPreviewHeadIndex = (particleTrailPreviewHeadIndex + trailSize - 1) % trailSize;
                        if (particleTrailPreviewValidCount < trailSize)
                        {
                            particleTrailPreviewValidCount++;
                        }
                    }
                }
                else if (particleTrailPreviewValidCount == 0)
                {
                    particleTrailPreviewHeadIndex = 0;
                    particleTrailPreviewValidCount = 1;
                }

                FullSolverParameters parameters = CreateParameters(0, settings, dimensionMode, iteration);
                parameters.PreviewWidth = particleTrailPreviewWidth;
                parameters.PreviewHeight = particleTrailPreviewHeight;
                parameters.PreviewSlice = particleTrailPreviewHeadIndex;
                parameters.PreviewAtlasColumns = trailSize;
                parameters.PreviewAtlasRows = particleTrailPreviewValidCount;
                UpdateParameters(parameters);

                context.CSSetShader(particleTrailPreviewShader);
                context.CSSetConstantBuffers(0, new ID3D11Buffer[] { parameterBuffer });
                context.CSSetUnorderedAccessView(2, particlePositionView, -1);
                context.CSSetUnorderedAccessView(4, particleYAxisView, -1);
                context.CSSetUnorderedAccessView(7, particleTrailPreviewTextureView, -1);
                DispatchLinear256(particleCapacity);
                UnbindComputeResources();
                context.Flush();
                particleTrailPreviewVersion++;
            }
            finally
            {
                ReleaseParticleTrailPreviewMutex();
            }
        }

        bool TryAcquireParticleTrailPreviewMutex(int timeoutMilliseconds)
        {
            if (particleTrailPreviewMutex == null)
            {
                return true;
            }

            try
            {
                particleTrailPreviewMutex.AcquireSync(0, timeoutMilliseconds);
                return true;
            }
            catch
            {
                return false;
            }
        }

        void ReleaseParticleTrailPreviewMutex()
        {
            if (particleTrailPreviewMutex == null)
            {
                return;
            }

            try
            {
                particleTrailPreviewMutex.ReleaseSync(0);
            }
            catch
            {
            }
        }

        FullSolverParameters CreateParameters(int axis, SolverGpuSettings settings, SolverGpuDimensionMode dimensionMode, int iteration)
        {
            FullSolverParameters parameters = new FullSolverParameters();
            parameters.ResX = resX;
            parameters.ResY = resY;
            parameters.ResZ = resZ;
            parameters.VoxelCount = voxelCount;
            parameters.ParticleCount = particleCapacity;
            parameters.ParticleCapacity = particleCapacity;
            parameters.MinimumPopulation = settings.MinimumPopulation;
            parameters.MaximumPopulation = Math.Min(settings.MaximumPopulation, particleCapacity);
            parameters.DynamicPopulation = settings.DynamicPopulation ? 1 : 0;
            parameters.DivisionEnabled = settings.Division ? 1 : 0;
            parameters.DivisionMinimumAge = settings.DivisionMinimumAge;
            parameters.DivisionRange = settings.DivisionRange;
            parameters.DivisionMinimumNeighbours = settings.DivisionMinimumNeighbours;
            parameters.DivisionMaximumNeighbours = settings.DivisionMaximumNeighbours;
            parameters.DivisionFrequency = settings.DivisionFrequency;
            parameters.DeathEnabled = settings.Death ? 1 : 0;
            parameters.DeathMinimumAge = settings.DeathMinimumAge;
            parameters.DeathRange = settings.DeathRange;
            parameters.DeathMinimumNeighbours = settings.DeathMinimumNeighbours;
            parameters.DeathMaximumNeighbours = settings.DeathMaximumNeighbours;
            parameters.DeathFrequency = settings.DeathFrequency;
            parameters.HasAntParticles = hasAntParticles ? 1 : 0;
            parameters.FieldMode = 0;
            parameters.AntDiffuseRange = settings.AntDiffuseRange;
            parameters.HasSlimeParticles = hasSlimeParticles ? 1 : 0;
            parameters.AntFoodDiffuse = (float)settings.AntFoodDiffuse;
            parameters.AntFoodDecay = (float)settings.AntFoodDecay;
            parameters.AntBaseDiffuse = (float)settings.AntBaseDiffuse;
            parameters.AntBaseDecay = (float)settings.AntBaseDecay;
            parameters.SlimeAntFood = (float)settings.SlimeAntFood;
            parameters.SlimeAntBase = (float)settings.SlimeAntBase;
            parameters.AntSlime = (float)settings.AntSlime;
            parameters.AntPaddingFloat = 0;
            parameters.HasVoxelFlags = hasVoxelFlags ? 1 : 0;
            parameters.HasActiveVoxelFlags = hasActiveVoxelFlags ? 1 : 0;
            parameters.HasVoxelBehavior = hasVoxelBehavior ? 1 : 0;
            parameters.HasVoxelVectors = hasVoxelVectors ? 1 : 0;
            parameters.HasVoxelDensityLimits = hasVoxelDensityLimits ? 1 : 0;
            parameters.HasVoxelVectorFrequencies = hasVoxelVectorFrequencies ? 1 : 0;
            parameters.VoxelVectorDefaultFrequency = voxelVectorDefaultFrequency;
            parameters.HasVoxelVectorData = hasVoxelVectorData ? 1 : 0;
            parameters.AntHomeYAxisZOffset = particleAntHomeYAxisZOffset;
            parameters.VoxelVectorDefaultX = voxelVectorDefaultX;
            parameters.VoxelVectorDefaultY = voxelVectorDefaultY;
            parameters.VoxelVectorDefaultZ = voxelVectorDefaultZ;
            parameters.VectorDefaultPadding = 0;
            parameters.SpeedOffset = speedOffset;
            parameters.SensorDistanceOffset = sensorDistanceOffset;
            parameters.SensorAngleOffset = sensorAngleOffset;
            parameters.RotationAngleOffset = rotationAngleOffset;
            parameters.MinimumDensityOffset = minimumDensityOffset;
            parameters.MaximumDensityOffset = maximumDensityOffset;
            parameters.AntHomeYAxisXOffset = particleAntHomeYAxisXOffset;
            parameters.AntHomeYAxisYOffset = particleAntHomeYAxisYOffset;
            parameters.SpeedDefault = speedDefault;
            parameters.SensorDistanceDefault = sensorDistanceDefault;
            parameters.SensorAngleDefault = sensorAngleDefault;
            parameters.RotationAngleDefault = rotationAngleDefault;
            parameters.MinimumDensityDefault = minimumDensityDefault;
            parameters.MaximumDensityDefault = maximumDensityDefault;
            parameters.ChannelDefaultPadding0 = 0;
            parameters.ChannelDefaultPadding1 = 0;
            parameters.SlimeDepositOffset = slimeDepositOffset;
            parameters.AntFoodDepositOffset = antFoodDepositOffset;
            parameters.AntBaseDepositOffset = antBaseDepositOffset;
            parameters.FoodRemainingOffset = foodRemainingOffset;
            parameters.FoodSourceOffset = foodSourceOffset;
            parameters.RandomPopulationFrequency = Math.Max(1, settings.RandomPopulationFrequency);
            parameters.RandomDeathProbability = (float)settings.RandomDeathProbability;
            parameters.RandomDivisionProbability = (float)settings.RandomDivisionProbability;
            parameters.HighDepositOffset = particleHighDepositOffset;
            parameters.AntLaunchBoundaryOffset = particleAntLaunchBoundaryOffset;
            parameters.FreeSlotOffset = freeSlotOffset;
            parameters.ParticleAgeOffset = particleAgeOffset;
            parameters.ParticleDeathNeighbourOffset = particleDeathNeighbourOffset;
            parameters.ParticleDivisionNeighbourOffset = particleDivisionNeighbourOffset;
            parameters.ParticleGenerationOffset = particleGenerationOffset;
            parameters.ParticleAntStateOffset = particleAntStateOffset;
            parameters.Axis = axis;
            parameters.Range = settings.DiffuseRange;
            parameters.Wrap = settings.WrapBoundaries ? 1 : 0;
            parameters.Tridimensional = dimensionMode.Tridimensional ? 1 : 0;
            parameters.PlanarXY = dimensionMode.PlanarXY ? 1 : 0;
            parameters.PlanarXZ = dimensionMode.PlanarXZ ? 1 : 0;
            parameters.PlanarYZ = dimensionMode.PlanarYZ ? 1 : 0;
            parameters.Iteration = iteration;
            parameters.GroupCount = groupCount;
            parameters.ApplyScalarDecayAfterDiffusion = 0;
            parameters.VoxelSize = voxelSize;
            parameters.DimX = dimX;
            parameters.DimY = dimY;
            parameters.DimZ = dimZ;
            parameters.Keep = (float)(1.0 - settings.Diffuse);
            parameters.Diffuse = (float)settings.Diffuse;
            parameters.Decay = (float)settings.Decay;
            parameters.DepositScale = DepositScale;
            parameters.PreviewWidth = densityPreviewWidth;
            parameters.PreviewHeight = densityPreviewHeight;
            parameters.PreviewAxisMode = densityPreviewAxisMode;
            parameters.PreviewSlice = densityPreviewAxisMode == 3 ? densityPreviewResZ : densityPreviewSlice;
            parameters.PreviewAtlasColumns = densityPreviewAtlasColumns;
            parameters.PreviewAtlasRows = densityPreviewAtlasRows;
            parameters.PreviewPadding0 = densityPreviewResX;
            parameters.PreviewPadding1 = densityPreviewResY;
            return parameters;
        }

        void UpdateParameters(FullSolverParameters parameters)
        {
            context.UpdateSubresourceSafe(ref parameters, parameterBuffer, 0, 0, 0, 0, false);
        }

        public bool UpdateGroupSettings(float[] groupData0, float[] groupData1, float[] groupColorData)
        {
            if (groupCount <= 0)
            {
                return true;
            }

            if (groupData0 == null
                || groupData1 == null
                || groupColorData == null
                || groupData0.Length != groupCount * 4
                || groupData1.Length != groupCount * 4
                || groupColorData.Length != groupCount * 4)
            {
                return false;
            }

            if (groupData0Buffer == null || groupData1Buffer == null || groupColorDataBuffer == null)
            {
                return false;
            }

            context.UpdateSubresourceSafe(groupData0, groupData0Buffer, 0, 0, 0, 0, false);
            context.UpdateSubresourceSafe(groupData1, groupData1Buffer, 0, 0, 0, 0, false);
            context.UpdateSubresourceSafe(groupColorData, groupColorDataBuffer, 0, 0, 0, 0, false);
            particleTrailPreviewGroupColorData = groupColorData;
            return true;
        }

        public bool UpdateVoxelBehaviorFields(GpuSolverInput snapshot)
        {
            if (snapshot == null
                || !ValidVoxelFlags(snapshot.VoxelFlags, voxelCount)
                || !ValidVoxelFlags(snapshot.ActiveVoxelFlags, voxelCount)
                || !ValidStaticPreviewLayout(snapshot, voxelCount)
                || !ValidBehaviorLayout(snapshot, voxelCount)
                || (snapshot.VoxelVectorData != null && snapshot.VoxelVectorData.Length != checked(voxelCount * 3))
                || (snapshot.VoxelVectorFrequencies != null && snapshot.VoxelVectorFrequencies.Length != voxelCount)
                || !ValidDensityLimitLayout(snapshot, voxelCount)
                || (snapshot.InitialAntFood != null) != (foodRemainingOffset >= 0)
                || (snapshot.InitialFood != null) != (foodSourceOffset >= 0))
            {
                return false;
            }

            UpdateOptionalUIntBuffer(snapshot.VoxelFlags, ref voxelFlagsBuffer, ref voxelFlagsView);
            UpdateOptionalUIntBuffer(snapshot.ActiveVoxelFlags, ref activeVoxelFlagsBuffer, ref activeVoxelFlagsView);
            UpdateOptionalFloatBuffer(snapshot.VoxelBehaviorData, ref voxelBehaviorBuffer, ref voxelBehaviorView, ref voxelBehaviorElementCount);
            UpdateOptionalFloat3Buffer(snapshot.VoxelVectorData, ref voxelVectorBuffer, ref voxelVectorView);
            UpdateOptionalIntBuffer(snapshot.VoxelVectorFrequencies, ref voxelVectorFrequencyBuffer, ref voxelVectorFrequencyView);
            UpdateOptionalFloatBuffer(snapshot.VoxelDensityLimits, ref voxelDensityLimitsBuffer, ref voxelDensityLimitsView, ref voxelDensityLimitElementCount);
            hasVoxelFlags = snapshot.VoxelFlags != null;
            hasActiveVoxelFlags = snapshot.ActiveVoxelFlags != null;
            hasVoxelBehavior = HasVoxelBehavior(snapshot);
            hasVoxelVectorData = snapshot.VoxelVectorData != null;
            hasVoxelVectors = hasVoxelVectorData || snapshot.VoxelVectorDefaultX != 0 || snapshot.VoxelVectorDefaultY != 0 || snapshot.VoxelVectorDefaultZ != 0;
            hasVoxelVectorFrequencies = hasVoxelVectors && snapshot.VoxelVectorFrequencies != null;
            voxelVectorDefaultFrequency = Math.Max(1, snapshot.VoxelVectorDefaultFrequency);
            voxelVectorDefaultX = snapshot.VoxelVectorDefaultX;
            voxelVectorDefaultY = snapshot.VoxelVectorDefaultY;
            voxelVectorDefaultZ = snapshot.VoxelVectorDefaultZ;
            hasVoxelDensityLimits = HasVoxelDensityLimits(snapshot);
            ApplySnapshotChannelOffsets(snapshot);
            ApplyStaticPreviewInput(snapshot);
            InvalidateStaticFieldPreviews();
            return true;
        }

        ID3D11UnorderedAccessView CurrentDensityView()
        {
            return densityInA ? densityAView : densityBView;
        }

        ID3D11UnorderedAccessView NextDensityView()
        {
            return densityInA ? densityBView : densityAView;
        }

        ID3D11Buffer CurrentDensityBuffer()
        {
            return densityInA ? densityA : densityB;
        }

        ID3D11UnorderedAccessView CurrentAntFoodView()
        {
            return antFoodInA ? antFoodAView : antFoodBView;
        }

        ID3D11UnorderedAccessView NextAntFoodView()
        {
            return antFoodInA ? antFoodBView : antFoodAView;
        }

        ID3D11Buffer CurrentAntFoodBuffer()
        {
            return antFoodInA ? antFoodA : antFoodB;
        }

        ID3D11ShaderResourceView CurrentAntFoodResourceView()
        {
            return antFoodInA ? antFoodAResourceView : antFoodBResourceView;
        }

        ID3D11UnorderedAccessView CurrentAntBaseView()
        {
            return antBaseInA ? antBaseAView : antBaseBView;
        }

        ID3D11UnorderedAccessView NextAntBaseView()
        {
            return antBaseInA ? antBaseBView : antBaseAView;
        }

        ID3D11Buffer CurrentAntBaseBuffer()
        {
            return antBaseInA ? antBaseA : antBaseB;
        }

        ID3D11ShaderResourceView CurrentAntBaseResourceView()
        {
            return antBaseInA ? antBaseAResourceView : antBaseBResourceView;
        }

        public GpuDensityFieldPreviewFrame CreateDensityFieldPreviewFrame()
        {
            if (!enableSharedDensityPreview)
            {
                return null;
            }

            if (densityPreviewSharedHandle == IntPtr.Zero || densityPreviewTexture == null || densityPreviewWidth <= 0 || densityPreviewHeight <= 0)
            {
                return null;
            }

            return CreateDynamicDensityFieldPreviewFrame(densityPreviewValueIndex);
        }

        GpuDensityFieldPreviewFrame CreateDynamicDensityFieldPreviewFrame(int valueIndex)
        {
            return new GpuDensityFieldPreviewFrame
            {
                SharedHandle = densityPreviewSharedHandle,
                Width = densityPreviewWidth,
                Height = densityPreviewHeight,
                ResX = densityPreviewResX,
                ResY = densityPreviewResY,
                ResZ = densityPreviewResZ,
                SourceResX = resX,
                SourceResY = resY,
                SourceResZ = resZ,
                AxisMode = densityPreviewAxisMode,
                Slice = densityPreviewSlice,
                AtlasColumns = densityPreviewAtlasColumns,
                AtlasRows = densityPreviewAtlasRows,
                VoxelSize = voxelSize,
                Version = densityPreviewVersion,
                ValueIndex = valueIndex,
                PreviewScale = 1.35f
            };
        }

        public GpuDensityFieldPreviewFrame CreateVoxelFieldPreviewFrame(int valueIndex, SolverGpuDimensionMode dimensionMode, float minimumThreshold, float maximumThreshold, int previewScale)
        {
            bool wantsGradientPreview = valueIndex == VoxelPreviewField.SlimeChemoattractants
                || valueIndex == VoxelPreviewField.SlimeChemoattractantsV2;
            valueIndex = VoxelPreviewField.SourceField(valueIndex);
            if (VoxelPreviewField.HasGpuDensityTexture(valueIndex))
            {
                if (valueIndex == VoxelPreviewField.SlimeChemoattractants && !processDensity)
                {
                    return null;
                }
                int normalizedScale = NormalizeDensityPreviewScale(previewScale);
                if ((valueIndex == VoxelPreviewField.AntFoodPheromones
                    || valueIndex == VoxelPreviewField.AntBasePheromones
                    || valueIndex == VoxelPreviewField.AntPheromones) && !hasAntParticles)
                {
                    return null;
                }

                // Ants and Slime is a union of both systems, so it must still draw
                // when only one of them is present. Requiring ants here meant a
                // slime-only document rendered nothing at all.
                if (valueIndex == VoxelPreviewField.AntsAndSlime
                    && !hasAntParticles && !processDensity)
                {
                    return null;
                }
                bool colorTexture = VoxelPreviewField.IsDynamicDensity(valueIndex);
                if (densityPreviewColorTexture != colorTexture)
                {
                    densityPreviewValueIndex = valueIndex;
                    densityPreviewColorTexture = colorTexture;
                    DisposeDensityPreviewTexture();
                }
                bool needsInitialDispatch = !enableSharedDensityPreview
                    || densityPreviewScale != normalizedScale
                    || densityPreviewTexture == null;
                if (needsInitialDispatch)
                {
                    SetSharedDensityPreviewEnabled(true, dimensionMode, normalizedScale);
                }
                if (needsInitialDispatch || valueIndex != densityPreviewValueIndex)
                {
                    densityPreviewValueIndex = valueIndex;
                    DispatchSelectedDensityPreviewPass(new SolverGpuSettings(), dimensionMode, 0);
                }

                GpuDensityFieldPreviewFrame frame = CreateDynamicDensityFieldPreviewFrame(valueIndex);
                if (wantsGradientPreview && frame != null && frame.VolumeMode && EnsureDensityGradientPreview())
                {
                    frame.GradientSharedHandle = densityGradientPreviewSharedHandle;
                }
                return frame;
            }

            if (!VoxelPreviewField.IsStatic(valueIndex))
            {
                return null;
            }

            if (!EnsureStaticFieldPreviewTexture(valueIndex, dimensionMode))
            {
                return null;
            }

            return new GpuDensityFieldPreviewFrame
            {
                SharedHandle = staticFieldPreviewSharedHandles[valueIndex],
                Width = staticFieldPreviewWidths[valueIndex],
                Height = staticFieldPreviewHeights[valueIndex],
                ResX = staticFieldPreviewResX[valueIndex],
                ResY = staticFieldPreviewResY[valueIndex],
                ResZ = staticFieldPreviewResZ[valueIndex],
                SourceResX = resX,
                SourceResY = resY,
                SourceResZ = resZ,
                AxisMode = staticFieldPreviewAxisModes[valueIndex],
                Slice = staticFieldPreviewSlices[valueIndex],
                AtlasColumns = staticFieldPreviewAtlasColumns[valueIndex],
                AtlasRows = staticFieldPreviewAtlasRows[valueIndex],
                VoxelSize = voxelSize,
                Version = staticFieldPreviewVersions[valueIndex],
                ValueIndex = valueIndex,
                PreviewScale = StaticFieldPreviewScale(valueIndex, minimumThreshold, maximumThreshold)
            };
        }

        public GpuParticlePreviewFrame CreateParticlePreviewFrame()
        {
            if (!enableSharedParticlePreview)
            {
                return null;
            }

            if (particlePreviewSharedHandle == IntPtr.Zero || particlePreviewTexture == null || particlePreviewWidth <= 0 || particlePreviewHeight <= 1)
            {
                return null;
            }

            return new GpuParticlePreviewFrame
            {
                SharedHandle = particlePreviewSharedHandle,
                TextureWidth = particlePreviewWidth,
                TextureHeight = particlePreviewHeight,
                ParticleCount = particleCapacity,
                ResX = resX,
                ResY = resY,
                ResZ = resZ,
                VoxelSize = voxelSize,
                Version = particlePreviewVersion
            };
        }

        public GpuParticleTrailPreviewFrame CreateParticleTrailPreviewFrame()
        {
            if (!enableSharedParticleTrailPreview)
            {
                return null;
            }

            if (particleTrailPreviewSharedHandle == IntPtr.Zero
                || particleTrailPreviewTexture == null
                || particleTrailPreviewWidth <= 0
                || particleTrailPreviewHeight <= 0
                || particleTrailPreviewTrailSize <= 1
                || particleTrailPreviewValidCount <= 1)
            {
                return null;
            }

            return new GpuParticleTrailPreviewFrame
            {
                SharedHandle = particleTrailPreviewSharedHandle,
                TextureWidth = particleTrailPreviewWidth,
                TextureHeight = particleTrailPreviewHeight,
                ParticleCount = particleCapacity,
                TrailSize = particleTrailPreviewTrailSize,
                ValidTrailCount = particleTrailPreviewValidCount,
                HeadIndex = particleTrailPreviewHeadIndex,
                ResX = resX,
                ResY = resY,
                ResZ = resZ,
                VoxelSize = voxelSize,
                GroupCount = groupCount,
                GroupColorData = particleTrailPreviewGroupColorData,
                Version = particleTrailPreviewVersion
            };
        }

        void ReadBackDensity()
        {
            EnsureDensityReadbackResources();
            context.CopyResource(densityReadbackBuffer, CurrentDensityBuffer());

            MappedSubresource mapped = context.Map(densityReadbackBuffer, MapMode.Read, Vortice.Direct3D11.MapFlags.None);
            try
            {
                Marshal.Copy(mapped.DataPointer, densityReadback, 0, densityReadback.Length);
            }
            finally
            {
                context.Unmap(densityReadbackBuffer);
            }
        }

        void EnsureDensityReadbackResources()
        {
            if (densityReadback == null || densityReadback.Length != voxelCount)
            {
                densityReadback = new float[voxelCount];
            }
            if (densityReadbackBuffer == null)
            {
                densityReadbackBuffer = CreateReadbackBuffer(voxelCount * sizeof(float));
            }
        }

        void EnsureParticleReadbackResources()
        {
            int floatCount = checked(particleCapacity * 4);
            int auxiliaryCount = checked(particleCapacity * (hasAntParticles ? 13 : 6));

            if (particlePositionReadback == null || particlePositionReadback.Length != floatCount)
            {
                particlePositionReadback = new float[floatCount];
            }
            if (particleDirectionReadback == null || particleDirectionReadback.Length != floatCount)
            {
                particleDirectionReadback = new float[floatCount];
            }
            if (particleYAxisReadback == null || particleYAxisReadback.Length != floatCount)
            {
                particleYAxisReadback = new float[floatCount];
            }
            if (hasAntParticles && (particleHomeReadback == null || particleHomeReadback.Length != floatCount))
            {
                particleHomeReadback = new float[floatCount];
            }
            int homeAxesCount = checked(particleCapacity * 6);
            if (hasAntParticles && (particleHomeAxesReadback == null || particleHomeAxesReadback.Length != homeAxesCount))
            {
                particleHomeAxesReadback = new float[homeAxesCount];
            }
            if (particleAuxReadback == null || particleAuxReadback.Length != auxiliaryCount)
            {
                particleAuxReadback = new int[auxiliaryCount];
            }
            int floatByteCount = checked(floatCount * sizeof(float));
            if (particlePositionReadbackBuffer == null) particlePositionReadbackBuffer = CreateReadbackBuffer(floatByteCount);
            if (particleDirectionReadbackBuffer == null) particleDirectionReadbackBuffer = CreateReadbackBuffer(floatByteCount);
            if (particleYAxisReadbackBuffer == null) particleYAxisReadbackBuffer = CreateReadbackBuffer(floatByteCount);
            if (hasAntParticles && particleHomeReadbackBuffer == null) particleHomeReadbackBuffer = CreateReadbackBuffer(floatByteCount);
            if (particleAuxReadbackBuffer == null)
            {
                particleAuxReadbackBuffer = CreateReadbackBuffer(Math.Max(sizeof(uint), checked(auxiliaryCount * sizeof(uint))));
            }
        }

        void EnsureParticlePreviewReadbackResources()
        {
            int floatCount = checked(particleCapacity * 4);
            if (particlePositionPreviewReadback == null || particlePositionPreviewReadback.Length != floatCount)
            {
                particlePositionPreviewReadback = new float[floatCount];
            }

            int byteCount = checked(floatCount * sizeof(float));
            for (int i = 0; i < particlePositionPreviewReadbackBuffers.Length; i++)
            {
                if (particlePositionPreviewReadbackBuffers[i] == null)
                {
                    particlePositionPreviewReadbackBuffers[i] = CreateReadbackBuffer(byteCount);
                }
            }
        }

        void EnsureAntReadbackResources()
        {
            if (antFoodReadback == null || antFoodReadback.Length != voxelCount)
            {
                antFoodReadback = new float[voxelCount];
            }
            if (antBaseReadback == null || antBaseReadback.Length != voxelCount)
            {
                antBaseReadback = new float[voxelCount];
            }
            if (antFoodReadbackBuffer == null)
            {
                antFoodReadbackBuffer = CreateReadbackBuffer(voxelCount * sizeof(float));
            }
            if (antBaseReadbackBuffer == null)
            {
                antBaseReadbackBuffer = CreateReadbackBuffer(voxelCount * sizeof(float));
            }
            if (foodRemainingOffset >= 0)
            {
                if (antFoodRemainingReadback == null || antFoodRemainingReadback.Length != voxelCount)
                {
                    antFoodRemainingReadback = new int[voxelCount];
                }
                if (antFoodRemainingReadbackBuffer == null)
                {
                    antFoodRemainingReadbackBuffer = CreateReadbackBuffer(voxelCount * sizeof(uint));
                }
            }
        }

        void ApplyDynamicFieldsToOutput()
        {
            outputSink.ApplyVoxelFields(new GpuVoxelReadbackView(
                processDensity ? densityReadback : null,
                hasAntParticles ? antFoodReadback : null,
                hasAntParticles ? antBaseReadback : null,
                hasAntParticles ? ConvertRemainingFood() : null,
                processDensity,
                hasAntParticles));
        }

        float[] ConvertRemainingFood()
        {
            if (antFoodRemainingReadback == null)
            {
                return null;
            }

            if (antFoodRemainingAsFloat == null || antFoodRemainingAsFloat.Length != antFoodRemainingReadback.Length)
            {
                antFoodRemainingAsFloat = new float[antFoodRemainingReadback.Length];
            }
            for (int i = 0; i < antFoodRemainingAsFloat.Length; i++)
            {
                antFoodRemainingAsFloat[i] = antFoodRemainingReadback[i] / DepositScale;
            }
            return antFoodRemainingAsFloat;
        }

        void ReadBackAntFields()
        {
            EnsureAntReadbackResources();
            ReadBackFloatBuffer(antFoodReadbackBuffer, CurrentAntFoodBuffer(), antFoodReadback);
            ReadBackFloatBuffer(antBaseReadbackBuffer, CurrentAntBaseBuffer(), antBaseReadback);

            if (foodRemainingOffset < 0)
            {
                return;
            }

            int sourceOffset = foodRemainingOffset * sizeof(uint);
            int byteCount = voxelCount * sizeof(uint);
            context.CopySubresourceRegion(
                antFoodRemainingReadbackBuffer,
                0,
                0,
                0,
                0,
                depositBuffer,
                0,
                new Vortice.Mathematics.Box(sourceOffset, 0, 0, sourceOffset + byteCount, 1, 1));
            MappedSubresource mapped = context.Map(antFoodRemainingReadbackBuffer, MapMode.Read, Vortice.Direct3D11.MapFlags.None);
            try
            {
                Marshal.Copy(mapped.DataPointer, antFoodRemainingReadback, 0, antFoodRemainingReadback.Length);
            }
            finally
            {
                context.Unmap(antFoodRemainingReadbackBuffer);
            }
        }

        void ReadBackFloatBuffer(ID3D11Buffer readbackBuffer, ID3D11Buffer sourceBuffer, float[] destination)
        {
            if (readbackBuffer == null || sourceBuffer == null || destination == null)
            {
                return;
            }
            context.CopyResource(readbackBuffer, sourceBuffer);
            MappedSubresource mapped = context.Map(readbackBuffer, MapMode.Read, Vortice.Direct3D11.MapFlags.None);
            try
            {
                Marshal.Copy(mapped.DataPointer, destination, 0, destination.Length);
            }
            finally
            {
                context.Unmap(readbackBuffer);
            }
        }

        void ReadBackParticles()
        {
            if (particleCapacity <= 0)
            {
                return;
            }

            EnsureParticleReadbackResources();
            ReadBackParticlePositions();
            ReadBackParticleAxes();
            ReadBackParticleAuxiliaryState();
            ReadBackPopulationState();
        }

        public int[] ReadBackValidationParticleCounts()
        {
            return ReadBackValidationUIntBuffer(
                particleCountBuffer,
                checked(voxelCount + 4 + groupCount));
        }

        public int[] ReadBackValidationParticleOwners()
        {
            return ReadBackValidationUIntBuffer(particleOwnerBuffer, voxelCount);
        }

        public int[] ReadBackValidationDeposits()
        {
            return ReadBackValidationUIntBuffer(depositBuffer, depositElementCount);
        }

        int[] ReadBackValidationUIntBuffer(ID3D11Buffer source, int elementCount)
        {
            if (source == null || elementCount <= 0)
            {
                return Array.Empty<int>();
            }

            int[] values = new int[elementCount];
            using (ID3D11Buffer staging = CreateReadbackBuffer(checked(elementCount * sizeof(uint))))
            {
                context.CopyResource(staging, source);
                MappedSubresource mapped = context.Map(
                    staging,
                    MapMode.Read,
                    Vortice.Direct3D11.MapFlags.None);
                try
                {
                    Marshal.Copy(mapped.DataPointer, values, 0, values.Length);
                }
                finally
                {
                    context.Unmap(staging);
                }
            }
            return values;
        }

        void ReadBackParticlePositions()
        {
            if (particleCapacity <= 0)
            {
                return;
            }

            ClearPendingPreviewReadbacks();
            ReadBackFloat4Buffer(particlePositionReadbackBuffer, particlePositionBuffer, particlePositionReadback);
        }

        public bool QueuePreviewCacheReadback()
        {
            if (particleCapacity <= 0)
            {
                return false;
            }

            EnsureParticlePreviewReadbackResources();
            int index = FindFreePreviewReadbackBuffer();
            if (index < 0)
            {
                return false;
            }

            context.CopyResource(particlePositionPreviewReadbackBuffers[index], particlePositionBuffer);
            previewReadbackPending[index] = true;
            previewReadbackSequences[index] = ++previewReadbackSequenceCounter;
            previewReadbackNextIndex = (index + 1) % PreviewReadbackBufferCount;
            return true;
        }

        bool QueuePopulationReadback()
        {
            if (particleCountBuffer == null || populationAsyncReadback == null)
            {
                return false;
            }

            int index = FindFreePopulationReadback();
            if (index < 0)
            {
                return false;
            }

            int sourceOffset = checked(voxelCount * sizeof(uint));
            int byteCount = checked(populationAsyncReadback.Length * sizeof(uint));
            context.CopySubresourceRegion(
                populationAsyncReadbackBuffers[index],
                0,
                0,
                0,
                0,
                particleCountBuffer,
                0,
                new Vortice.Mathematics.Box(
                    sourceOffset,
                    0,
                    0,
                    sourceOffset + byteCount,
                    1,
                    1));
            populationReadbackPending[index] = true;
            populationReadbackSequences[index] = ++populationReadbackSequenceCounter;
            populationReadbackNextIndex = (index + 1) % PopulationReadbackBufferCount;
            return true;
        }

        public bool TryCompletePreviewCache()
        {
            if (particleCapacity <= 0)
            {
                return false;
            }

            bool[] attempted = new bool[PreviewReadbackBufferCount];
            for (int attempt = 0; attempt < PreviewReadbackBufferCount; attempt++)
            {
                int index = FindNewestPendingPreviewReadbackBuffer(attempted);
                if (index < 0)
                {
                    return false;
                }

                attempted[index] = true;

                ID3D11Buffer readbackBuffer = particlePositionPreviewReadbackBuffers[index];
                if (readbackBuffer == null)
                {
                    previewReadbackPending[index] = false;
                    continue;
                }

                MappedSubresource mapped;
                Result result = context.Map(
                    readbackBuffer,
                    0,
                    MapMode.Read,
                    Vortice.Direct3D11.MapFlags.DoNotWait,
                    out mapped);

                if (result.Failure)
                {
                    const int dxgiErrorWasStillDrawing = unchecked((int)0x887A000A);
                    if (result.Code == dxgiErrorWasStillDrawing)
                    {
                        continue;
                    }

                    result.CheckError();
                }

                int sequence = previewReadbackSequences[index];
                try
                {
                    if (sequence > previewReadbackCompletedSequence)
                    {
                        Marshal.Copy(mapped.DataPointer, particlePositionPreviewReadback, 0, particlePositionPreviewReadback.Length);
                    }
                }
                finally
                {
                    context.Unmap(readbackBuffer);
                    previewReadbackPending[index] = false;
                }

                if (sequence <= previewReadbackCompletedSequence)
                {
                    continue;
                }

                previewReadbackCompletedSequence = sequence;
                return ApplyPreviewPositions(particlePositionPreviewReadback);
            }

            return false;
        }

        int FindFreePreviewReadbackBuffer()
        {
            for (int offset = 0; offset < PreviewReadbackBufferCount; offset++)
            {
                int index = (previewReadbackNextIndex + offset) % PreviewReadbackBufferCount;
                if (!previewReadbackPending[index] && particlePositionPreviewReadbackBuffers[index] != null)
                {
                    return index;
                }
            }

            return -1;
        }

        int FindFreePopulationReadback()
        {
            for (int offset = 0; offset < PopulationReadbackBufferCount; offset++)
            {
                int index = (populationReadbackNextIndex + offset) % PopulationReadbackBufferCount;
                if (!populationReadbackPending[index] && populationAsyncReadbackBuffers[index] != null)
                {
                    return index;
                }
            }

            return -1;
        }

        int FindNewestPendingPopulationReadback(bool[] attempted)
        {
            int bestIndex = -1;
            int bestSequence = int.MinValue;
            for (int i = 0; i < PopulationReadbackBufferCount; i++)
            {
                if (attempted[i] || !populationReadbackPending[i])
                {
                    continue;
                }

                if (populationReadbackSequences[i] > bestSequence)
                {
                    bestSequence = populationReadbackSequences[i];
                    bestIndex = i;
                }
            }

            return bestIndex;
        }

        int FindNewestPendingPreviewReadbackBuffer(bool[] attempted)
        {
            int bestIndex = -1;
            int bestSequence = int.MinValue;
            for (int i = 0; i < PreviewReadbackBufferCount; i++)
            {
                if (attempted[i] || !previewReadbackPending[i])
                {
                    continue;
                }

                if (previewReadbackSequences[i] > bestSequence)
                {
                    bestSequence = previewReadbackSequences[i];
                    bestIndex = i;
                }
            }

            return bestIndex;
        }

        void ClearPendingPreviewReadbacks()
        {
            previewReadbackCompletedSequence = previewReadbackSequenceCounter;
        }

        void ResetPreviewReadbackState()
        {
            Array.Clear(previewReadbackPending, 0, previewReadbackPending.Length);
            Array.Clear(previewReadbackSequences, 0, previewReadbackSequences.Length);
            previewReadbackNextIndex = 0;
            previewReadbackSequenceCounter = 0;
            previewReadbackCompletedSequence = 0;
        }

        void InvalidatePendingPopulationReadbacks()
        {
            populationReadbackCompletedSequence = populationReadbackSequenceCounter;
        }

        void ReadBackParticleAxes()
        {
            if (particleCapacity <= 0)
            {
                return;
            }

            ReadBackFloat4Buffer(particleDirectionReadbackBuffer, particleDirectionBuffer, particleDirectionReadback);
            ReadBackFloat4Buffer(particleYAxisReadbackBuffer, particleYAxisBuffer, particleYAxisReadback);
            if (hasAntParticles)
            {
                ReadBackFloat4Buffer(particleHomeReadbackBuffer, particleHomeBuffer, particleHomeReadback);
            }
        }

        void ReadBackParticleAuxiliaryState()
        {
            if (particleAuxReadbackBuffer == null || depositBuffer == null || particleAuxReadback == null || particleAuxReadback.Length == 0)
            {
                return;
            }

            int sourceOffset = particleAgeOffset * sizeof(uint);
            int byteCount = particleAuxReadback.Length * sizeof(uint);
            context.CopySubresourceRegion(
                particleAuxReadbackBuffer,
                0,
                0,
                0,
                0,
                depositBuffer,
                0,
                new Vortice.Mathematics.Box(sourceOffset, 0, 0, sourceOffset + byteCount, 1, 1));

            MappedSubresource mapped = context.Map(particleAuxReadbackBuffer, MapMode.Read, Vortice.Direct3D11.MapFlags.None);
            try
            {
                Marshal.Copy(mapped.DataPointer, particleAuxReadback, 0, particleAuxReadback.Length);
            }
            finally
            {
                context.Unmap(particleAuxReadbackBuffer);
            }

            if (hasAntParticles && particleHomeAxesReadback != null)
            {
                int sourceByteOffset = checked(particleCapacity * 7 * sizeof(uint));
                int homeAxesByteCount = checked(particleHomeAxesReadback.Length * sizeof(float));
                Buffer.BlockCopy(particleAuxReadback, sourceByteOffset, particleHomeAxesReadback, 0, homeAxesByteCount);
            }
        }

        void ReadBackPopulationState()
        {
            if (populationStateReadbackBuffer == null || particleCountBuffer == null)
            {
                return;
            }

            int sourceOffset = voxelCount * sizeof(uint);
            context.CopySubresourceRegion(
                populationStateReadbackBuffer,
                0,
                0,
                0,
                0,
                particleCountBuffer,
                0,
                new Vortice.Mathematics.Box(sourceOffset, 0, 0, sourceOffset + 4 * sizeof(uint), 1, 1));

            MappedSubresource mapped = context.Map(populationStateReadbackBuffer, MapMode.Read, Vortice.Direct3D11.MapFlags.None);
            try
            {
                Marshal.Copy(mapped.DataPointer, populationStateReadback, 0, populationStateReadback.Length);
            }
            finally
            {
                context.Unmap(populationStateReadbackBuffer);
            }

            particleCount = Math.Max(0, Math.Min(particleCapacity, populationStateReadback[0]));
            // This blocking result is newer than every staging copy queued before
            // it. Leave those resources pending until a nonblocking Map can safely
            // discard them, but prevent their sequences from replacing this state.
            InvalidatePendingPopulationReadbacks();
        }

        void ReadBackFloat4Buffer(ID3D11Buffer readbackBuffer, ID3D11Buffer sourceBuffer, float[] destination)
        {
            context.CopyResource(readbackBuffer, sourceBuffer);

            MappedSubresource mapped = context.Map(readbackBuffer, MapMode.Read, Vortice.Direct3D11.MapFlags.None);
            try
            {
                Marshal.Copy(mapped.DataPointer, destination, 0, destination.Length);
            }
            finally
            {
                context.Unmap(readbackBuffer);
            }
        }

        bool ApplyParticlesToOutput(SolverGpuSettings settings, int iteration, bool buildPreviewCache)
        {
            if (particleCapacity <= 0)
            {
                return false;
            }

            bool builtPreviewCache = outputSink.ApplyParticles(
                new GpuParticleReadbackView(
                    particleCapacity,
                    particleCount,
                    groupCount,
                    particlePositionReadback,
                    particleDirectionReadback,
                    particleYAxisReadback,
                    particleHomeReadback,
                    particleHomeAxesReadback,
                    particleAuxReadback),
                settings,
                iteration,
                buildPreviewCache);
            particleCount = Math.Max(0, Math.Min(particleCapacity, outputSink.ParticleCount));
            return builtPreviewCache;
        }

        bool ApplyPreviewPositions(float[] positionReadback)
        {
            if (particleCapacity <= 0)
            {
                return false;
            }

            return outputSink.ApplyPreviewPositions(new GpuParticlePreviewReadbackView(
                particleCapacity,
                particleCount,
                groupCount,
                positionReadback));
        }

        int FlatIndex(int x, int y, int z)
        {
            return x * resY * resZ + y * resZ + z;
        }

        void EnsureWeights(int range, double gradual)
        {
            if (weightsView != null && weightsRange == range && weightsGradual.Equals(gradual))
            {
                return;
            }

            DisposeWeights();

            float[] weights = PrecomputeWeights(range, gradual);
            weightsRange = range;
            weightsGradual = gradual;

            weightsBuffer = device.CreateBuffer(
                weights,
                BindFlags.ShaderResource,
                ResourceUsage.Default,
                CpuAccessFlags.None,
                ResourceOptionFlags.BufferStructured,
                weights.Length * sizeof(float),
                sizeof(float));

            weightsView = device.CreateShaderResourceView(
                weightsBuffer,
                new ShaderResourceViewDescription(weightsBuffer, Format.Unknown, 0, weights.Length, BufferExtendedShaderResourceViewFlags.None));
        }

        void EnsureAntWeights(int range)
        {
            if (antWeightsView != null && antWeightsRange == range) return;

            if (antWeightsView != null) antWeightsView.Dispose();
            if (antWeightsBuffer != null) antWeightsBuffer.Dispose();
            float[] weights = PrecomputeWeights(range, 1.0);
            antWeightsRange = range;
            antWeightsBuffer = device.CreateBuffer(weights, BindFlags.ShaderResource, ResourceUsage.Default,
                CpuAccessFlags.None, ResourceOptionFlags.BufferStructured, weights.Length * sizeof(float), sizeof(float));
            antWeightsView = device.CreateShaderResourceView(
                antWeightsBuffer,
                new ShaderResourceViewDescription(antWeightsBuffer, Format.Unknown, 0, weights.Length, BufferExtendedShaderResourceViewFlags.None));
        }

        // Mirrors V3 precomputeWeights. The kernel keeps 2*range+1 entries, the
        // same length the DiffuseAxis shader indexes as Weights[offset + Range].
        // gradual 1 is the original raised-cosine kernel; gradual 0 is a flat box
        // average matching V2's immediate averaging.
        static float[] PrecomputeWeights(int range, double gradual)
        {
            int total = range * 2 + 1;
            float[] weights = new float[total];
            double[] raw = new double[total];
            double weightSum = 0;

            for (int i = 0; i < total; i++)
            {
                int offset = i - range;
                double angle = Math.PI * offset / (range + 1.0);
                double v3Weight = (1 + Math.Cos(angle)) * 0.5;

                if (gradual <= 0) raw[i] = 1;
                else if (gradual >= 1) raw[i] = v3Weight;
                else raw[i] = Math.Pow(v3Weight, gradual);

                weightSum += raw[i];
            }

            for (int i = 0; i < total; i++)
            {
                weights[i] = (float)(raw[i] / weightSum);
            }

            return weights;
        }

        // Mirrors V3 gradualDiffusionStrength / gradualDiffusionRetention.
        static double GradualDiffusionStrength(double rate, double gradual)
        {
            if (gradual <= 0) return 1;
            if (gradual >= 1) return rate;
            return 1 - gradual * (1 - rate);
        }

        static double GradualDiffusionRetention(double rate, double gradual)
        {
            if (gradual <= 0) return 1 - rate;
            if (gradual >= 1) return 1;
            return 1 - rate + gradual * rate;
        }

        void CreateDensityBuffers(float[] initialDensity)
        {
            densityA = device.CreateBuffer(
                voxelCount * sizeof(float),
                BindFlags.UnorderedAccess,
                ResourceUsage.Default,
                CpuAccessFlags.None,
                ResourceOptionFlags.BufferStructured,
                sizeof(float));

            densityB = device.CreateBuffer(
                voxelCount * sizeof(float),
                BindFlags.UnorderedAccess,
                ResourceUsage.Default,
                CpuAccessFlags.None,
                ResourceOptionFlags.BufferStructured,
                sizeof(float));

            densityAView = device.CreateUnorderedAccessView(
                densityA,
                new UnorderedAccessViewDescription(densityA, Format.Unknown, 0, voxelCount, BufferUnorderedAccessViewFlags.None));

            densityBView = device.CreateUnorderedAccessView(
                densityB,
                new UnorderedAccessViewDescription(densityB, Format.Unknown, 0, voxelCount, BufferUnorderedAccessViewFlags.None));

            ResetFloatFieldPair(initialDensity, densityA, densityAView, densityB, densityBView);
        }

        void CreateAntFieldBuffers(float[] initialFoodPheromone, float[] initialBasePheromone)
        {
            CreateAntFieldPair(initialFoodPheromone, out antFoodA, out antFoodB, out antFoodAView, out antFoodBView, out antFoodAResourceView, out antFoodBResourceView);
            CreateAntFieldPair(initialBasePheromone, out antBaseA, out antBaseB, out antBaseAView, out antBaseBView, out antBaseAResourceView, out antBaseBResourceView);
        }

        void CreateAntFieldPair(
            float[] initial,
            out ID3D11Buffer bufferA,
            out ID3D11Buffer bufferB,
            out ID3D11UnorderedAccessView viewA,
            out ID3D11UnorderedAccessView viewB,
            out ID3D11ShaderResourceView resourceA,
            out ID3D11ShaderResourceView resourceB)
        {
            BindFlags bind = BindFlags.UnorderedAccess | BindFlags.ShaderResource;
            bufferA = device.CreateBuffer(voxelCount * sizeof(float), bind, ResourceUsage.Default, CpuAccessFlags.None,
                ResourceOptionFlags.BufferStructured, sizeof(float));
            bufferB = device.CreateBuffer(voxelCount * sizeof(float), bind, ResourceUsage.Default, CpuAccessFlags.None,
                ResourceOptionFlags.BufferStructured, sizeof(float));
            viewA = CreateUav(bufferA, voxelCount);
            viewB = CreateUav(bufferB, voxelCount);
            resourceA = CreateSrv(bufferA, voxelCount);
            resourceB = CreateSrv(bufferB, voxelCount);
            ResetFloatFieldPair(initial, bufferA, viewA, bufferB, viewB);
        }

        bool EnsureStaticFieldPreviewTexture(int fieldIndex, SolverGpuDimensionMode dimensionMode)
        {
            if (!VoxelPreviewField.IsStatic(fieldIndex))
            {
                return false;
            }

            if (!hasStaticPreviewInput)
            {
                return false;
            }

            int width;
            int height;
            int previewResX;
            int previewResY;
            int previewResZ;
            int axisMode;
            int slice;
            int atlasColumns;
            int atlasRows;
            ResolveDensityPreviewLayout(
                dimensionMode,
                out width,
                out height,
                out previewResX,
                out previewResY,
                out previewResZ,
                out axisMode,
                out slice,
                out atlasColumns,
                out atlasRows);
            if (width <= 0 || height <= 0)
            {
                return false;
            }
            if (!CanCreateSharedDensityPreviewTexture(width, height))
            {
                WriteSharedDensityPreviewStatus(
                    "static_field_texture_skip_too_large field=" + fieldIndex
                    + " width=" + width
                    + " height=" + height
                    + " pixels=" + ((long)width * height));
                return false;
            }

            if (staticFieldPreviewTextures[fieldIndex] != null
                && staticFieldPreviewWidths[fieldIndex] == width
                && staticFieldPreviewHeights[fieldIndex] == height
                && staticFieldPreviewResX[fieldIndex] == previewResX
                && staticFieldPreviewResY[fieldIndex] == previewResY
                && staticFieldPreviewResZ[fieldIndex] == previewResZ
                && staticFieldPreviewAxisModes[fieldIndex] == axisMode
                && staticFieldPreviewSlices[fieldIndex] == slice
                && staticFieldPreviewAtlasColumns[fieldIndex] == atlasColumns
                && staticFieldPreviewAtlasRows[fieldIndex] == atlasRows)
            {
                return true;
            }

            DisposeStaticFieldPreviewTexture(fieldIndex);

            WriteSharedDensityPreviewStatus(
                "static_field_texture_begin field=" + fieldIndex
                + " width=" + width
                + " height=" + height
                + " axis=" + axisMode
                + " slice=" + slice
                + " preview_res=" + previewResX + "x" + previewResY + "x" + previewResZ
                + " atlas=" + atlasColumns + "x" + atlasRows);

            float[] previewValues = BuildStaticFieldPreviewValues(
                fieldIndex,
                width,
                height,
                previewResX,
                previewResY,
                previewResZ,
                axisMode,
                slice,
                atlasColumns,
                atlasRows);

            Texture2DDescription description = new Texture2DDescription(
                Format.R32_Float,
                width,
                height,
                1,
                1,
                BindFlags.ShaderResource,
                ResourceUsage.Default,
                CpuAccessFlags.None,
                1,
                0,
                ResourceOptionFlags.Shared);

            ID3D11Texture2D texture = device.CreateTexture2D(description, null);
            context.UpdateSubresourceSafe(previewValues, texture, 0, 0, 0, 0, false);
            context.Flush();

            staticFieldPreviewTextures[fieldIndex] = texture;
            staticFieldPreviewWidths[fieldIndex] = width;
            staticFieldPreviewHeights[fieldIndex] = height;
            staticFieldPreviewResX[fieldIndex] = previewResX;
            staticFieldPreviewResY[fieldIndex] = previewResY;
            staticFieldPreviewResZ[fieldIndex] = previewResZ;
            staticFieldPreviewAxisModes[fieldIndex] = axisMode;
            staticFieldPreviewSlices[fieldIndex] = slice;
            staticFieldPreviewAtlasColumns[fieldIndex] = atlasColumns;
            staticFieldPreviewAtlasRows[fieldIndex] = atlasRows;
            staticFieldPreviewVersions[fieldIndex]++;

            using (IDXGIResource resource = texture.QueryInterface<IDXGIResource>())
            {
                staticFieldPreviewSharedHandles[fieldIndex] = resource.SharedHandle;
            }

            WriteSharedDensityPreviewStatus(
                "static_field_texture field=" + fieldIndex
                + " width=" + width
                + " height=" + height
                + " handle=0x" + staticFieldPreviewSharedHandles[fieldIndex].ToInt64().ToString("X"));

            return staticFieldPreviewSharedHandles[fieldIndex] != IntPtr.Zero;
        }

        float[] BuildStaticFieldPreviewValues(
            int fieldIndex,
            int width,
            int height,
            int previewResX,
            int previewResY,
            int previewResZ,
            int axisMode,
            int slice,
            int atlasColumns,
            int atlasRows)
        {
            float[] previewValues = new float[width * height];
            atlasColumns = Math.Max(1, atlasColumns);
            atlasRows = Math.Max(1, atlasRows);
            previewResX = Math.Max(1, previewResX);
            previewResY = Math.Max(1, previewResY);
            previewResZ = Math.Max(1, previewResZ);

            for (int v = 0; v < height; v++)
            {
                for (int u = 0; u < width; u++)
                {
                    int x = u;
                    int y = v;
                    int z = slice;

                    if (axisMode == 3)
                    {
                        int tileX = u / previewResX;
                        int tileY = v / previewResY;
                        int previewZ = tileY * atlasColumns + tileX;
                        if (previewZ >= previewResZ || tileY >= atlasRows)
                        {
                            previewValues[v * width + u] = 0;
                            continue;
                        }

                        int previewX = u - tileX * previewResX;
                        int previewY = v - tileY * previewResY;
                        x = PreviewSourceIndex(previewX, previewResX, resX);
                        y = PreviewSourceIndex(previewY, previewResY, resY);
                        z = PreviewSourceIndex(previewZ, previewResZ, resZ);
                    }
                    else if (axisMode == 1)
                    {
                        x = u;
                        y = 0;
                        z = v;
                    }
                    else if (axisMode == 2)
                    {
                        x = 0;
                        y = u;
                        z = v;
                    }

                    x = ClampIndex(x, resX);
                    y = ClampIndex(y, resY);
                    z = ClampIndex(z, resZ);

                    int flatIndex = FlatIndex(x, y, z);
                    previewValues[v * width + u] = StaticVoxelFieldValue(flatIndex, fieldIndex);
                }
            }

            return previewValues;
        }

        static int PreviewSourceIndex(int previewIndex, int previewResolution, int sourceResolution)
        {
            if (sourceResolution <= 1 || previewResolution <= 1) return 0;
            double normalized = previewIndex / (double)(previewResolution - 1);
            return ClampIndex((int)Math.Round(normalized * (sourceResolution - 1)), sourceResolution);
        }

        float StaticFieldPreviewScale(int fieldIndex, float minimumThreshold, float maximumThreshold)
        {
            if (fieldIndex < 0 || fieldIndex >= VoxelPreviewField.StaticFieldCount)
            {
                return 1.0f;
            }

            if (maximumThreshold < minimumThreshold)
            {
                float temp = minimumThreshold;
                minimumThreshold = maximumThreshold;
                maximumThreshold = temp;
            }

            if (staticFieldPreviewScaleValid[fieldIndex]
                && staticFieldPreviewScaleMinimums[fieldIndex] == minimumThreshold
                && staticFieldPreviewScaleMaximums[fieldIndex] == maximumThreshold)
            {
                return staticFieldPreviewScales[fieldIndex];
            }

            float maximum = 1.0f;
            if (fieldIndex >= VoxelPreviewField.Speed && fieldIndex <= VoxelPreviewField.Food)
            {
                maximum = MaxVisibleStaticFieldValue(fieldIndex, minimumThreshold, maximumThreshold);
            }

            float scale = maximum > 0.0001f ? 1.0f / maximum : 1.0f;
            staticFieldPreviewScaleMinimums[fieldIndex] = minimumThreshold;
            staticFieldPreviewScaleMaximums[fieldIndex] = maximumThreshold;
            staticFieldPreviewScales[fieldIndex] = scale;
            staticFieldPreviewScaleValid[fieldIndex] = true;
            return scale;
        }

        void InvalidateStaticFieldPreviewScales()
        {
            for (int i = 0; i < staticFieldPreviewScaleValid.Length; i++)
            {
                staticFieldPreviewScaleValid[i] = false;
            }
        }

        void InvalidateStaticFieldPreviews()
        {
            for (int i = 0; i < staticFieldPreviewTextures.Length; i++)
            {
                DisposeStaticFieldPreviewTexture(i);
            }

            InvalidateStaticFieldPreviewScales();
        }

        float MaxVisibleStaticFieldValue(int fieldIndex, float minimumThreshold, float maximumThreshold)
        {
            if (!hasStaticPreviewInput)
            {
                return 1.0f;
            }

            int width = staticFieldPreviewWidths[fieldIndex];
            int height = staticFieldPreviewHeights[fieldIndex];
            int axisMode = staticFieldPreviewAxisModes[fieldIndex];
            int slice = staticFieldPreviewSlices[fieldIndex];
            int previewResX = staticFieldPreviewResX[fieldIndex];
            int previewResY = staticFieldPreviewResY[fieldIndex];
            int previewResZ = staticFieldPreviewResZ[fieldIndex];
            int atlasColumns = staticFieldPreviewAtlasColumns[fieldIndex];
            int atlasRows = staticFieldPreviewAtlasRows[fieldIndex];
            float maximum = 0;

            for (int v = 0; v < height; v++)
            {
                for (int u = 0; u < width; u++)
                {
                    int x;
                    int y;
                    int z;
                    if (!StaticPreviewCoordinates(
                        u,
                        v,
                        axisMode,
                        slice,
                        previewResX,
                        previewResY,
                        previewResZ,
                        atlasColumns,
                        atlasRows,
                        out x,
                        out y,
                        out z))
                    {
                        continue;
                    }

                    int flatIndex = FlatIndex(x, y, z);
                    float value = StaticVoxelFieldValue(flatIndex, fieldIndex);
                    if (value > 0.01f && value >= minimumThreshold && value <= maximumThreshold && value > maximum)
                    {
                        maximum = value;
                    }
                }
            }

            return maximum > 0.0001f ? maximum : 1.0f;
        }

        float StaticVoxelFieldValue(int flatIndex, int fieldIndex)
        {
            if (!hasStaticPreviewInput || flatIndex < 0 || flatIndex >= voxelCount || !StaticVoxelIsActive(flatIndex)) return 0;

            switch (fieldIndex)
            {
                case VoxelPreviewField.MinimumDensity:
                    return StaticScalarFieldValue(staticMinimumDensityValues, staticMinimumDensityDefault, flatIndex);
                case VoxelPreviewField.MaximumDensity:
                    if (StaticVoxelIsSolverBoundary(flatIndex)) return 0;
                    return StaticScalarFieldValue(staticMaximumDensityValues, staticMaximumDensityDefault, flatIndex);
                case VoxelPreviewField.Speed:
                    return StaticScalarFieldValue(staticSpeedValues, staticSpeedDefault, flatIndex);
                case VoxelPreviewField.SensorDistance:
                    return StaticScalarFieldValue(staticSensorDistanceValues, staticSensorDistanceDefault, flatIndex);
                case VoxelPreviewField.SensorAngle:
                    return StaticScalarFieldValue(staticSensorAngleValues, staticSensorAngleDefault, flatIndex);
                case VoxelPreviewField.RotationAngle:
                    return StaticScalarFieldValue(staticRotationAngleValues, staticRotationAngleDefault, flatIndex);
                default:
                    return 0;
            }
        }

        bool StaticVoxelIsActive(int flatIndex)
        {
            return staticActiveVoxelFlags == null
                || (staticActiveVoxelFlags[flatIndex >> 5] & (1u << (flatIndex & 31))) != 0;
        }

        bool StaticVoxelIsSolverBoundary(int flatIndex)
        {
            int yz = resY * resZ;
            int x = flatIndex / yz;
            int remainder = flatIndex - x * yz;
            int y = remainder / resZ;
            int z = remainder - y * resZ;

            if (!wrapBoundaryState)
            {
                bool tridimensional = resX > 1 && resY > 1 && resZ > 1;
                if (tridimensional &&
                    (x == 0 || x == resX - 1 || y == 0 || y == resY - 1 || z == 0 || z == resZ - 1))
                {
                    return true;
                }
                if (!tridimensional)
                {
                    if (resX == 1 && (y == 0 || y == resY - 1 || z == 0 || z == resZ - 1)) return true;
                    if (resX != 1 && resY == 1 && (x == 0 || x == resX - 1 || z == 0 || z == resZ - 1)) return true;
                    if (resX != 1 && resY != 1 && (x == 0 || x == resX - 1 || y == 0 || y == resY - 1)) return true;
                }
            }

            if (staticActiveVoxelFlags == null)
            {
                return false;
            }

            for (int u = Math.Max(0, x - 1); u <= Math.Min(resX - 1, x + 1); u++)
            {
                for (int v = Math.Max(0, y - 1); v <= Math.Min(resY - 1, y + 1); v++)
                {
                    for (int w = Math.Max(0, z - 1); w <= Math.Min(resZ - 1, z + 1); w++)
                    {
                        int neighbourIndex = FlatIndex(u, v, w);
                        if (!StaticVoxelIsActive(neighbourIndex)) return true;
                    }
                }
            }

            return false;
        }

        static float StaticScalarFieldValue(float[] values, double defaultValue, int flatIndex)
        {
            double value = values != null && flatIndex < values.Length
                ? values[flatIndex]
                : defaultValue;
            if (double.IsNaN(value) || double.IsInfinity(value) || value < 0) return 0;
            return (float)value;
        }

        bool StaticPreviewCoordinates(
            int u,
            int v,
            int axisMode,
            int slice,
            int previewResX,
            int previewResY,
            int previewResZ,
            int atlasColumns,
            int atlasRows,
            out int x,
            out int y,
            out int z)
        {
            x = u;
            y = v;
            z = slice;

            if (axisMode == 3)
            {
                previewResX = Math.Max(1, previewResX);
                previewResY = Math.Max(1, previewResY);
                previewResZ = Math.Max(1, previewResZ);
                atlasColumns = Math.Max(1, atlasColumns);
                atlasRows = Math.Max(1, atlasRows);
                int tileX = u / previewResX;
                int tileY = v / previewResY;
                int previewZ = tileY * atlasColumns + tileX;
                if (previewZ >= previewResZ || tileY >= atlasRows)
                {
                    x = y = z = 0;
                    return false;
                }

                x = PreviewSourceIndex(u - tileX * previewResX, previewResX, resX);
                y = PreviewSourceIndex(v - tileY * previewResY, previewResY, resY);
                z = PreviewSourceIndex(previewZ, previewResZ, resZ);
            }
            else if (axisMode == 1)
            {
                x = u;
                y = 0;
                z = v;
            }
            else if (axisMode == 2)
            {
                x = 0;
                y = u;
                z = v;
            }

            x = ClampIndex(x, resX);
            y = ClampIndex(y, resY);
            z = ClampIndex(z, resZ);
            return true;
        }

        static int ClampIndex(int value, int dimension)
        {
            if (dimension <= 1) return 0;
            if (value < 0) return 0;
            if (value >= dimension) return dimension - 1;
            return value;
        }

        void DisposeStaticFieldPreviewTexture(int fieldIndex)
        {
            if (fieldIndex < 0 || fieldIndex >= staticFieldPreviewTextures.Length)
            {
                return;
            }

            if (staticFieldPreviewTextures[fieldIndex] != null)
            {
                staticFieldPreviewTextures[fieldIndex].Dispose();
                staticFieldPreviewTextures[fieldIndex] = null;
            }

            staticFieldPreviewSharedHandles[fieldIndex] = IntPtr.Zero;
            staticFieldPreviewWidths[fieldIndex] = 0;
            staticFieldPreviewHeights[fieldIndex] = 0;
            staticFieldPreviewResX[fieldIndex] = 0;
            staticFieldPreviewResY[fieldIndex] = 0;
            staticFieldPreviewResZ[fieldIndex] = 0;
            staticFieldPreviewAxisModes[fieldIndex] = 0;
            staticFieldPreviewSlices[fieldIndex] = 0;
            staticFieldPreviewAtlasColumns[fieldIndex] = 1;
            staticFieldPreviewAtlasRows[fieldIndex] = 1;
            staticFieldPreviewScaleValid[fieldIndex] = false;
        }

        void DisposeDensityPreviewTexture()
        {
            DisposeDensityGradientPreviewTexture();

            if (densityPreviewTextureResourceView != null)
            {
                densityPreviewTextureResourceView.Dispose();
                densityPreviewTextureResourceView = null;
            }

            if (densityPreviewTextureView != null)
            {
                densityPreviewTextureView.Dispose();
                densityPreviewTextureView = null;
            }

            if (densityPreviewTexture != null)
            {
                densityPreviewTexture.Dispose();
                densityPreviewTexture = null;
            }

            densityPreviewSharedHandle = IntPtr.Zero;
            densityPreviewWidth = 0;
            densityPreviewHeight = 0;
            densityPreviewResX = 0;
            densityPreviewResY = 0;
            densityPreviewResZ = 0;
            densityPreviewAxisMode = 0;
            densityPreviewSlice = 0;
            densityPreviewAtlasColumns = 1;
            densityPreviewAtlasRows = 1;
            densityPreviewVersion++;
        }

        void DisposeDensityGradientPreviewTexture()
        {
            if (densityGradientPreviewTextureView != null)
            {
                densityGradientPreviewTextureView.Dispose();
                densityGradientPreviewTextureView = null;
            }

            if (densityGradientPreviewTexture != null)
            {
                densityGradientPreviewTexture.Dispose();
                densityGradientPreviewTexture = null;
            }

            densityGradientPreviewSharedHandle = IntPtr.Zero;
            densityGradientSourceVersion = -1;
        }

        void DisposeParticleTrailPreviewTexture()
        {
            if (particleTrailPreviewMutex != null)
            {
                particleTrailPreviewMutex.Dispose();
                particleTrailPreviewMutex = null;
            }

            if (particleTrailPreviewTextureView != null)
            {
                particleTrailPreviewTextureView.Dispose();
                particleTrailPreviewTextureView = null;
            }

            if (particleTrailPreviewTexture != null)
            {
                particleTrailPreviewTexture.Dispose();
                particleTrailPreviewTexture = null;
            }

            particleTrailPreviewSharedHandle = IntPtr.Zero;
            particleTrailPreviewWidth = 0;
            particleTrailPreviewHeight = 0;
            particleTrailPreviewHeadIndex = 0;
            particleTrailPreviewValidCount = 0;
            particleTrailPreviewLastDispatchIteration = -1;
            particleTrailPreviewVersion++;
        }

        void CreateDensityPreviewTexture(SolverGpuDimensionMode dimensionMode)
        {
            ResolveDensityPreviewLayout(
                dimensionMode,
                out densityPreviewWidth,
                out densityPreviewHeight,
                out densityPreviewResX,
                out densityPreviewResY,
                out densityPreviewResZ,
                out densityPreviewAxisMode,
                out densityPreviewSlice,
                out densityPreviewAtlasColumns,
                out densityPreviewAtlasRows);
            if (densityPreviewAxisMode != 3)
            {
                ApplyDensityPreviewScale(ref densityPreviewWidth, ref densityPreviewHeight);
            }
            if (densityPreviewWidth <= 0 || densityPreviewHeight <= 0)
            {
                WriteSharedDensityPreviewStatus("skip invalid_layout");
                return;
            }
            if (!CanCreateSharedDensityPreviewTexture(densityPreviewWidth, densityPreviewHeight))
            {
                WriteSharedDensityPreviewStatus(
                    "skip texture_too_large width=" + densityPreviewWidth
                    + " height=" + densityPreviewHeight
                    + " pixels=" + ((long)densityPreviewWidth * densityPreviewHeight));
                densityPreviewWidth = 0;
                densityPreviewHeight = 0;
                return;
            }

            WriteSharedDensityPreviewStatus(
                "create_texture_begin width=" + densityPreviewWidth
                + " height=" + densityPreviewHeight
                + " scale=" + densityPreviewScale
                + " axis=" + densityPreviewAxisMode
                + " slice=" + densityPreviewSlice
                + " preview_res=" + densityPreviewResX + "x" + densityPreviewResY + "x" + densityPreviewResZ
                + " atlas=" + densityPreviewAtlasColumns + "x" + densityPreviewAtlasRows
                + " res=" + resX + "x" + resY + "x" + resZ);

            Format previewFormat = densityPreviewColorTexture ? Format.R32G32B32A32_Float : Format.R32_Float;
            Texture2DDescription description = new Texture2DDescription(
                previewFormat,
                densityPreviewWidth,
                densityPreviewHeight,
                1,
                1,
                BindFlags.UnorderedAccess | BindFlags.ShaderResource,
                ResourceUsage.Default,
                CpuAccessFlags.None,
                1,
                0,
                ResourceOptionFlags.Shared);

            densityPreviewTexture = device.CreateTexture2D(description, null);
            WriteSharedDensityPreviewStatus("create_texture_ok");

            densityPreviewTextureView = device.CreateUnorderedAccessView(
                densityPreviewTexture,
                new UnorderedAccessViewDescription(
                    densityPreviewTexture,
                    UnorderedAccessViewDimension.Texture2D,
                    previewFormat,
                    0,
                    0,
                    0));
            WriteSharedDensityPreviewStatus("create_uav_ok");

            densityPreviewTextureResourceView = device.CreateShaderResourceView(
                densityPreviewTexture,
                new ShaderResourceViewDescription(
                    densityPreviewTexture,
                    ShaderResourceViewDimension.Texture2D,
                    previewFormat,
                    0,
                    1,
                    0,
                    1));

            using (IDXGIResource resource = densityPreviewTexture.QueryInterface<IDXGIResource>())
            {
                densityPreviewSharedHandle = resource.SharedHandle;
            }

            WriteSharedDensityPreviewStatus("shared_handle=0x" + densityPreviewSharedHandle.ToInt64().ToString("X"));
            DispatchSelectedDensityPreviewPass(new SolverGpuSettings(), dimensionMode, 0);
            WriteSharedDensityPreviewStatus("initial_dispatch_ok");
        }

        void CreateDensityGradientPreviewTexture()
        {
            DisposeDensityGradientPreviewTexture();
            if (densityPreviewWidth <= 0 || densityPreviewHeight <= 0 || densityPreviewAxisMode != 3)
            {
                return;
            }

            const Format gradientFormat = Format.R16G16B16A16_Float;
            Texture2DDescription description = new Texture2DDescription(
                gradientFormat,
                densityPreviewWidth,
                densityPreviewHeight,
                1,
                1,
                BindFlags.UnorderedAccess | BindFlags.ShaderResource,
                ResourceUsage.Default,
                CpuAccessFlags.None,
                1,
                0,
                ResourceOptionFlags.Shared);

            densityGradientPreviewTexture = device.CreateTexture2D(description, null);
            densityGradientPreviewTextureView = device.CreateUnorderedAccessView(
                densityGradientPreviewTexture,
                new UnorderedAccessViewDescription(
                    densityGradientPreviewTexture,
                    UnorderedAccessViewDimension.Texture2D,
                    gradientFormat,
                    0,
                    0,
                    0));

            using (IDXGIResource resource = densityGradientPreviewTexture.QueryInterface<IDXGIResource>())
            {
                densityGradientPreviewSharedHandle = resource.SharedHandle;
            }
            densityGradientSourceVersion = -1;
        }

        void ApplyDensityPreviewScale(ref int width, ref int height)
        {
            int scale = Math.Max(1, densityPreviewScale);
            if (scale == 1)
            {
                return;
            }

            long scaledWidth = (long)width * scale;
            long scaledHeight = (long)height * scale;
            if (!CanCreateSharedDensityPreviewTexture(scaledWidth, scaledHeight))
            {
                return;
            }

            width = (int)scaledWidth;
            height = (int)scaledHeight;
        }

        static bool CanCreateSharedDensityPreviewTexture(long width, long height)
        {
            if (width <= 0 || height <= 0)
            {
                return false;
            }

            if (width > MaxSharedPreviewTextureDimension || height > MaxSharedPreviewTextureDimension)
            {
                return false;
            }

            return width * height <= MaxSharedDensityPreviewTexturePixels;
        }

        void CreateParticlePreviewTexture()
        {
            if (particleCapacity <= 0)
            {
                return;
            }

            ResolveParticlePreviewLayout(out particlePreviewWidth, out particlePreviewHeight);
            WriteSharedParticlePreviewStatus(
                "create_texture_begin width=" + particlePreviewWidth
                + " height=" + particlePreviewHeight
                + " particles=" + particleCapacity);

            Texture2DDescription description = new Texture2DDescription(
                Format.R32G32B32A32_Float,
                particlePreviewWidth,
                particlePreviewHeight,
                1,
                1,
                BindFlags.UnorderedAccess | BindFlags.ShaderResource,
                ResourceUsage.Default,
                CpuAccessFlags.None,
                1,
                0,
                ResourceOptionFlags.Shared);

            particlePreviewTexture = device.CreateTexture2D(description, null);
            particlePreviewTextureView = device.CreateUnorderedAccessView(
                particlePreviewTexture,
                new UnorderedAccessViewDescription(
                    particlePreviewTexture,
                    UnorderedAccessViewDimension.Texture2D,
                    Format.R32G32B32A32_Float,
                    0,
                    0,
                    0));

            using (IDXGIResource resource = particlePreviewTexture.QueryInterface<IDXGIResource>())
            {
                particlePreviewSharedHandle = resource.SharedHandle;
            }

            WriteSharedParticlePreviewStatus("shared_handle=0x" + particlePreviewSharedHandle.ToInt64().ToString("X"));
        }

        void CreateParticleTrailPreviewTexture(int trailSize)
        {
            trailSize = ClampTrailPreviewSizeForParticleCount(trailSize);
            if (particleCapacity <= 0 || trailSize <= 1)
            {
                return;
            }

            ResolveParticleTrailPreviewLayout(trailSize, out particleTrailPreviewWidth, out particleTrailPreviewHeight);
            particleTrailPreviewTrailSize = trailSize;
            particleTrailPreviewHeadIndex = 0;
            particleTrailPreviewValidCount = 0;
            particleTrailPreviewLastDispatchIteration = -1;

            bool createdWithKeyedMutex = true;
            Texture2DDescription description = new Texture2DDescription(
                Format.R32G32B32A32_Float,
                particleTrailPreviewWidth,
                particleTrailPreviewHeight,
                1,
                1,
                BindFlags.UnorderedAccess | BindFlags.ShaderResource,
                ResourceUsage.Default,
                CpuAccessFlags.None,
                1,
                0,
                ResourceOptionFlags.SharedKeyedMutex);

            try
            {
                particleTrailPreviewTexture = device.CreateTexture2D(description, null);
            }
            catch
            {
                createdWithKeyedMutex = false;
                description = new Texture2DDescription(
                    Format.R32G32B32A32_Float,
                    particleTrailPreviewWidth,
                    particleTrailPreviewHeight,
                    1,
                    1,
                    BindFlags.UnorderedAccess | BindFlags.ShaderResource,
                    ResourceUsage.Default,
                    CpuAccessFlags.None,
                    1,
                    0,
                    ResourceOptionFlags.Shared);
                particleTrailPreviewTexture = device.CreateTexture2D(description, null);
            }

            particleTrailPreviewTextureView = device.CreateUnorderedAccessView(
                particleTrailPreviewTexture,
                new UnorderedAccessViewDescription(
                    particleTrailPreviewTexture,
                    UnorderedAccessViewDimension.Texture2D,
                    Format.R32G32B32A32_Float,
                    0,
                    0,
                    0));

            using (IDXGIResource resource = particleTrailPreviewTexture.QueryInterface<IDXGIResource>())
            {
                particleTrailPreviewSharedHandle = resource.SharedHandle;
            }

            if (createdWithKeyedMutex)
            {
                try
                {
                    particleTrailPreviewMutex = particleTrailPreviewTexture.QueryInterface<IDXGIKeyedMutex>();
                }
                catch
                {
                    particleTrailPreviewMutex = null;
                }
            }
            else
            {
                particleTrailPreviewMutex = null;
            }

            WriteSharedParticlePreviewStatus("trail_shared_handle=0x" + particleTrailPreviewSharedHandle.ToInt64().ToString("X")
                + " width=" + particleTrailPreviewWidth
                + " height=" + particleTrailPreviewHeight
                + " trail_size=" + trailSize
                + " keyed_mutex=" + (particleTrailPreviewMutex != null));
        }

        void ResolveParticlePreviewLayout(out int width, out int height)
        {
            int maxRows = MaxSharedPreviewTextureDimension / 2;
            width = Math.Min(4096, Math.Max(1, particleCapacity));
            int rows = (particleCapacity + width - 1) / width;

            if (rows > maxRows)
            {
                width = (particleCapacity + maxRows - 1) / maxRows;
                if (width > MaxSharedPreviewTextureDimension)
                {
                    throw new InvalidOperationException("Particle preview texture would exceed Direct3D texture limits.");
                }

                rows = (particleCapacity + width - 1) / width;
            }

            height = Math.Max(2, rows * 2);
        }

        void ResolveParticleTrailPreviewLayout(int trailSize, out int width, out int height)
        {
            int maxParticleRows = Math.Max(1, MaxSharedPreviewTextureDimension / Math.Max(2, trailSize));
            width = Math.Min(MaxSharedPreviewTextureDimension, Math.Max(1, particleCapacity));
            int particleRows = (particleCapacity + width - 1) / width;

            if (particleRows > maxParticleRows)
            {
                width = (particleCapacity + maxParticleRows - 1) / maxParticleRows;
                if (width > MaxSharedPreviewTextureDimension)
                {
                    throw new InvalidOperationException("Particle trail preview texture would exceed Direct3D texture limits.");
                }

                particleRows = (particleCapacity + width - 1) / width;
            }

            height = Math.Max(trailSize, particleRows * trailSize);
            if (height > MaxSharedPreviewTextureDimension)
            {
                throw new InvalidOperationException("Particle trail preview texture would exceed Direct3D texture limits.");
            }
        }

        void ResolveDensityPreviewLayout(
            SolverGpuDimensionMode dimensionMode,
            out int width,
            out int height,
            out int previewResX,
            out int previewResY,
            out int previewResZ,
            out int axisMode,
            out int slice,
            out int atlasColumns,
            out int atlasRows)
        {
            atlasColumns = 1;
            atlasRows = 1;
            previewResX = Math.Max(1, resX);
            previewResY = Math.Max(1, resY);
            previewResZ = Math.Max(1, resZ);

            if (dimensionMode.Tridimensional)
            {
                ResolveAdaptiveVolumeAtlasLayout(
                    out previewResX,
                    out previewResY,
                    out previewResZ,
                    out atlasColumns,
                    out atlasRows,
                    out width,
                    out height);
                axisMode = 3;
                slice = 0;
                return;
            }

            if (dimensionMode.PlanarXZ)
            {
                width = Math.Max(1, resX);
                height = Math.Max(1, resZ);
                axisMode = 1;
                slice = 0;
                return;
            }

            if (dimensionMode.PlanarYZ)
            {
                width = Math.Max(1, resY);
                height = Math.Max(1, resZ);
                axisMode = 2;
                slice = 0;
                return;
            }

            width = Math.Max(1, resX);
            height = Math.Max(1, resY);
            axisMode = 0;
            slice = dimensionMode.Tridimensional ? Math.Max(0, resZ / 2) : 0;
        }

        void ResolveAdaptiveVolumeAtlasLayout(
            out int previewResX,
            out int previewResY,
            out int previewResZ,
            out int columns,
            out int rows,
            out int width,
            out int height)
        {
            int sourceX = Math.Max(1, resX);
            int sourceY = Math.Max(1, resY);
            int sourceZ = Math.Max(1, resZ);
            if (TryResolveVolumeAtlasLayout(sourceX, sourceY, sourceZ, out columns, out rows, out width, out height))
            {
                previewResX = sourceX;
                previewResY = sourceY;
                previewResZ = sourceZ;
                return;
            }

            double low = 0.0;
            int maximumSourceResolution = Math.Max(sourceX, Math.Max(sourceY, sourceZ));
            double high = Math.Min(1.0, MaxAdaptiveVolumePreviewResolution / (double)maximumSourceResolution);
            previewResX = ScaledPreviewResolution(sourceX, 0.01);
            previewResY = ScaledPreviewResolution(sourceY, 0.01);
            previewResZ = ScaledPreviewResolution(sourceZ, 0.01);
            if (!TryResolveVolumeAtlasLayout(previewResX, previewResY, previewResZ, out columns, out rows, out width, out height))
            {
                throw new InvalidOperationException("3D voxel preview atlas cannot fit within Direct3D texture limits.");
            }

            int cappedX = ScaledPreviewResolution(sourceX, high);
            int cappedY = ScaledPreviewResolution(sourceY, high);
            int cappedZ = ScaledPreviewResolution(sourceZ, high);
            if (TryResolveVolumeAtlasLayout(cappedX, cappedY, cappedZ, out columns, out rows, out width, out height))
            {
                previewResX = cappedX;
                previewResY = cappedY;
                previewResZ = cappedZ;
                return;
            }

            for (int iteration = 0; iteration < 32; iteration++)
            {
                double scale = (low + high) * 0.5;
                int candidateX = ScaledPreviewResolution(sourceX, scale);
                int candidateY = ScaledPreviewResolution(sourceY, scale);
                int candidateZ = ScaledPreviewResolution(sourceZ, scale);
                int candidateColumns;
                int candidateRows;
                int candidateWidth;
                int candidateHeight;
                if (TryResolveVolumeAtlasLayout(
                    candidateX,
                    candidateY,
                    candidateZ,
                    out candidateColumns,
                    out candidateRows,
                    out candidateWidth,
                    out candidateHeight))
                {
                    low = scale;
                    previewResX = candidateX;
                    previewResY = candidateY;
                    previewResZ = candidateZ;
                    columns = candidateColumns;
                    rows = candidateRows;
                    width = candidateWidth;
                    height = candidateHeight;
                }
                else
                {
                    high = scale;
                }
            }
        }

        static int ScaledPreviewResolution(int sourceResolution, double scale)
        {
            if (sourceResolution <= 1) return 1;
            return Math.Min(sourceResolution, Math.Max(2, (int)Math.Floor(sourceResolution * scale)));
        }

        static bool TryResolveVolumeAtlasLayout(
            int sliceWidth,
            int sliceHeight,
            int sliceCount,
            out int columns,
            out int rows,
            out int width,
            out int height)
        {
            sliceWidth = Math.Max(1, sliceWidth);
            sliceHeight = Math.Max(1, sliceHeight);
            sliceCount = Math.Max(1, sliceCount);
            double targetColumns = Math.Sqrt((double)sliceCount * sliceHeight / sliceWidth);
            columns = Math.Max(1, (int)Math.Ceiling(targetColumns));
            rows = (sliceCount + columns - 1) / columns;

            while (columns > 1 && (long)columns * sliceWidth > MaxSharedPreviewTextureDimension)
            {
                columns--;
                rows = (sliceCount + columns - 1) / columns;
            }

            while ((long)rows * sliceHeight > MaxSharedPreviewTextureDimension)
            {
                columns++;
                rows = (sliceCount + columns - 1) / columns;
                if ((long)columns * sliceWidth > MaxSharedPreviewTextureDimension)
                {
                    width = 0;
                    height = 0;
                    return false;
                }
            }

            long resolvedWidth = (long)columns * sliceWidth;
            long resolvedHeight = (long)rows * sliceHeight;
            long pixels = resolvedWidth * resolvedHeight;
            if (resolvedWidth > MaxSharedPreviewTextureDimension
                || resolvedHeight > MaxSharedPreviewTextureDimension
                || pixels > MaxSharedDensityPreviewTexturePixels)
            {
                width = 0;
                height = 0;
                return false;
            }

            width = (int)resolvedWidth;
            height = (int)resolvedHeight;
            return true;
        }

        void CreateParticleBuffers(GpuSolverInput snapshot)
        {
            if (particleCapacity <= 0)
            {
                return;
            }

            float[] positions;
            float[] directions;
            float[] yAxes;
            float[] homes;
            BuildParticleBufferData(snapshot, out positions, out directions, out yAxes, out homes);

            particlePositionBuffer = CreateFloat4Buffer(positions, BindFlags.UnorderedAccess);
            particleDirectionBuffer = CreateFloat4Buffer(directions, BindFlags.UnorderedAccess);
            particleYAxisBuffer = CreateFloat4Buffer(yAxes, BindFlags.UnorderedAccess);
            particleHomeBuffer = CreateFloat4Buffer(homes, BindFlags.UnorderedAccess);
            populationStateReadbackBuffer = CreateReadbackBuffer(4 * sizeof(uint));
            populationAsyncReadback = new int[4 + groupCount];
            int populationByteCount = checked(populationAsyncReadback.Length * sizeof(uint));
            for (int i = 0; i < populationAsyncReadbackBuffers.Length; i++)
            {
                populationAsyncReadbackBuffers[i] = CreateReadbackBuffer(populationByteCount);
            }

            particlePositionView = CreateUav(particlePositionBuffer, particleCapacity);
            particleDirectionView = CreateUav(particleDirectionBuffer, particleCapacity);
            particleYAxisView = CreateUav(particleYAxisBuffer, particleCapacity);
            particleHomeView = CreateUav(particleHomeBuffer, particleCapacity);
        }

        void BuildParticleBufferData(
            GpuSolverInput snapshot,
            out float[] positions,
            out float[] directions,
            out float[] yAxes,
            out float[] homes)
        {
            positions = new float[particleCapacity * 4];
            directions = new float[particleCapacity * 4];
            yAxes = new float[particleCapacity * 4];
            homes = new float[particleCapacity * 4];

            for (int i = 0; i < particleCapacity; i++)
            {
                positions[i * 4 + 3] = -1;
                directions[i * 4 + 3] = -1;
                yAxes[i * 4] = 0;
                yAxes[i * 4 + 1] = 1;
                yAxes[i * 4 + 2] = 0;
                yAxes[i * 4 + 3] = -1;
                homes[i * 4 + 3] = -1;
            }

            for (int i = 0; i < particleCount; i++)
            {
                int source3 = i * 3;
                int target4 = i * 4;
                positions[target4] = snapshot.ParticlePositionsXyz[source3];
                positions[target4 + 1] = snapshot.ParticlePositionsXyz[source3 + 1];
                positions[target4 + 2] = snapshot.ParticlePositionsXyz[source3 + 2];
                positions[target4 + 3] = snapshot.ParticleGroupIndices[i];

                directions[target4] = snapshot.ParticleDirectionsXyz[source3];
                directions[target4 + 1] = snapshot.ParticleDirectionsXyz[source3 + 1];
                directions[target4 + 2] = snapshot.ParticleDirectionsXyz[source3 + 2];
                directions[target4 + 3] = snapshot.ParticleParentIndices[i];

                yAxes[target4] = snapshot.ParticleYAxesXyz[source3];
                yAxes[target4 + 1] = snapshot.ParticleYAxesXyz[source3 + 1];
                yAxes[target4 + 2] = snapshot.ParticleYAxesXyz[source3 + 2];
                yAxes[target4 + 3] = 0;
                if (snapshot.ParticleHomesXyz != null && snapshot.ParticleHomesXyz.Length >= source3 + 3)
                {
                    homes[target4] = snapshot.ParticleHomesXyz[source3];
                    homes[target4 + 1] = snapshot.ParticleHomesXyz[source3 + 1];
                    homes[target4 + 2] = snapshot.ParticleHomesXyz[source3 + 2];
                }
                else
                {
                    homes[target4] = positions[target4];
                    homes[target4 + 1] = positions[target4 + 1];
                    homes[target4 + 2] = positions[target4 + 2];
                }
                homes[target4 + 3] = AntLaunchVariationBase(
                    homes[target4],
                    homes[target4 + 1],
                    homes[target4 + 2],
                    directions[target4],
                    directions[target4 + 1],
                    directions[target4 + 2]);
            }
        }

        static float AntLaunchVariationBase(
            float homeX,
            float homeY,
            float homeZ,
            float axisX,
            float axisY,
            float axisZ)
        {
            return (float)(
                homeX * 0.1031 + homeY * 0.11369 + homeZ * 0.13787 +
                axisX * 12.9898 + axisY * 78.233 + axisZ * 37.719);
        }

        void CreateGroupBuffers(GpuSolverInput snapshot)
        {
            if (groupCount <= 0)
            {
                return;
            }

            groupData0Buffer = CreateFloat4Buffer(snapshot.GroupData0, BindFlags.ShaderResource);
            groupData1Buffer = CreateFloat4Buffer(snapshot.GroupData1, BindFlags.ShaderResource);
            groupColorDataBuffer = CreateFloat4Buffer(snapshot.GroupColorData, BindFlags.ShaderResource);
            groupData0View = CreateSrv(groupData0Buffer, groupCount);
            groupData1View = CreateSrv(groupData1Buffer, groupCount);
            groupColorDataView = CreateSrv(groupColorDataBuffer, groupCount);
        }

        void CreateVoxelFlagBuffer(GpuSolverInput snapshot)
        {
            UpdateOptionalUIntBuffer(snapshot.VoxelFlags, ref voxelFlagsBuffer, ref voxelFlagsView);
            UpdateOptionalUIntBuffer(snapshot.ActiveVoxelFlags, ref activeVoxelFlagsBuffer, ref activeVoxelFlagsView);
            hasActiveVoxelFlags = snapshot.ActiveVoxelFlags != null;

            particleCountBuffer = device.CreateBuffer(
                checked((voxelCount + 4 + groupCount) * sizeof(uint)),
                BindFlags.UnorderedAccess,
                ResourceUsage.Default,
                CpuAccessFlags.None,
                ResourceOptionFlags.BufferStructured,
                sizeof(uint));

            particleOwnerBuffer = device.CreateBuffer(
                checked(voxelCount * sizeof(uint)),
                BindFlags.UnorderedAccess,
                ResourceUsage.Default,
                CpuAccessFlags.None,
                ResourceOptionFlags.BufferStructured,
                sizeof(uint));

            depositBuffer = device.CreateBuffer(
                checked(depositElementCount * sizeof(uint)),
                BindFlags.UnorderedAccess,
                ResourceUsage.Default,
                CpuAccessFlags.None,
                ResourceOptionFlags.BufferStructured,
                sizeof(uint));

            particleCountView = CreateUav(particleCountBuffer, voxelCount + 4 + groupCount);
            particleOwnerView = CreateUav(particleOwnerBuffer, voxelCount);
            depositView = CreateUav(depositBuffer, depositElementCount);
            ResetAuxiliaryState(snapshot);
        }

        void ResetAuxiliaryState(GpuSolverInput snapshot)
        {
            context.ClearUnorderedAccessView(particleCountView, new Vortice.Mathematics.Int4(0));
            context.ClearUnorderedAccessView(depositView, new Vortice.Mathematics.Int4(0));

            uint[] populationState = new uint[4 + groupCount];
            populationState[0] = (uint)particleCount;
            populationState[1] = (uint)Math.Max(0, particleCapacity - particleCount);
            populationState[2] = (uint)particleCount;
            int initialGroupIndexCount = snapshot.ParticleGroupIndices != null
                ? Math.Min(particleCount, snapshot.ParticleGroupIndices.Length)
                : 0;
            for (int particleIndex = 0; particleIndex < initialGroupIndexCount; particleIndex++)
            {
                int groupIndex = snapshot.ParticleGroupIndices[particleIndex];
                if (groupIndex >= 0 && groupIndex < groupCount)
                {
                    populationState[4 + groupIndex]++;
                }
            }
            UpdateUIntBufferRegion(particleCountBuffer, voxelCount, populationState, populationState.Length);

            if (foodRemainingOffset >= 0 && snapshot.InitialAntFood != null)
            {
                UploadFoodChannel(snapshot.InitialAntFood, foodRemainingOffset);
            }

            if (foodSourceOffset >= 0 && snapshot.InitialFood != null)
            {
                UploadFoodChannel(snapshot.InitialFood, foodSourceOffset);
            }

            UploadFreeParticleSlots();
            UploadParticleHighDepositFlags();
            UploadParticleAges(snapshot);
            UploadParticleAntStates(snapshot);
            UploadParticleAntLaunchBoundaryStates(snapshot);
            UploadParticleAntHomeAxes(snapshot);
        }

        void UploadFoodChannel(float[] initialFood, int channelOffset)
        {
            const int chunkSize = 262144;
            uint[] chunk = new uint[Math.Min(chunkSize, voxelCount)];
            for (int start = 0; start < voxelCount; start += chunk.Length)
            {
                int count = Math.Min(chunk.Length, voxelCount - start);
                for (int i = 0; i < count; i++)
                {
                    float food = start + i < initialFood.Length ? initialFood[start + i] : 0;
                    chunk[i] = (uint)Math.Round(Math.Max(0, food) * DepositScale);
                }
                UpdateUIntBufferRegion(depositBuffer, channelOffset + start, chunk, count);
            }
        }

        void UploadFreeParticleSlots()
        {
            int freeCount = Math.Max(0, particleCapacity - particleCount);
            if (freeCount == 0 || freeSlotOffset < 0) return;

            const int chunkSize = 262144;
            uint[] chunk = new uint[Math.Min(chunkSize, freeCount)];
            for (int start = 0; start < freeCount; start += chunk.Length)
            {
                int count = Math.Min(chunk.Length, freeCount - start);
                for (int i = 0; i < count; i++) chunk[i] = (uint)(particleCount + start + i);
                UpdateUIntBufferRegion(depositBuffer, freeSlotOffset + start, chunk, count);
            }
        }

        /// <summary>
        /// V3 advances age once during reset. The two no-movement warm-up solutions
        /// are advanced explicitly by AdvanceParticleAges; later increments happen
        /// inside the movement kernel at the same post-move point as V3's parent check.
        /// </summary>
        const int V3AgeAlignmentOffset = 1;

        /// <summary>
        /// V3 particles start with the flag clear, so a reset must not inherit stale
        /// values from a previous run in the reused deposit buffer.
        /// </summary>
        void UploadParticleHighDepositFlags()
        {
            if (particleCapacity == 0 || particleHighDepositOffset < 0) return;

            const int chunkSize = 262144;
            uint[] chunk = new uint[Math.Min(chunkSize, particleCapacity)];
            for (int start = 0; start < particleCapacity; start += chunk.Length)
            {
                int count = Math.Min(chunk.Length, particleCapacity - start);
                Array.Clear(chunk, 0, count);
                UpdateUIntBufferRegion(depositBuffer, particleHighDepositOffset + start, chunk, count);
            }
        }

        void UploadParticleAges(GpuSolverInput snapshot)
        {
            if (particleCount == 0 || particleAgeOffset < 0) return;

            const int chunkSize = 262144;
            uint[] chunk = new uint[Math.Min(chunkSize, particleCount)];
            for (int start = 0; start < particleCount; start += chunk.Length)
            {
                int count = Math.Min(chunk.Length, particleCount - start);
                for (int i = 0; i < count; i++)
                {
                    int slot = start + i;
                    chunk[i] = (uint)(Math.Max(0,
                        snapshot.ParticleAges != null && slot < snapshot.ParticleAges.Length
                            ? snapshot.ParticleAges[slot]
                            : 0) + V3AgeAlignmentOffset);
                }
                UpdateUIntBufferRegion(depositBuffer, particleAgeOffset + start, chunk, count);
            }
        }

        void UploadParticleAntStates(GpuSolverInput snapshot)
        {
            if (particleCount == 0 || particleAntStateOffset < 0 || snapshot.ParticleAntStates == null) return;

            int uploadCount = Math.Min(particleCount, snapshot.ParticleAntStates.Length);
            if (uploadCount == 0) return;

            const int chunkSize = 262144;
            uint[] chunk = new uint[Math.Min(chunkSize, uploadCount)];
            for (int start = 0; start < uploadCount; start += chunk.Length)
            {
                int count = Math.Min(chunk.Length, uploadCount - start);
                for (int i = 0; i < count; i++)
                {
                    int slot = start + i;
                    chunk[i] = slot < snapshot.ParticleAntStates.Length ? snapshot.ParticleAntStates[slot] : 0u;
                }
                UpdateUIntBufferRegion(depositBuffer, particleAntStateOffset + start, chunk, count);
            }
        }

        void UploadParticleAntLaunchBoundaryStates(GpuSolverInput snapshot)
        {
            if (particleCount == 0 || particleAntLaunchBoundaryOffset < 0 || snapshot.ParticleAntLaunchBoundaryStates == null) return;

            int uploadCount = Math.Min(particleCount, snapshot.ParticleAntLaunchBoundaryStates.Length);
            if (uploadCount == 0) return;

            const int chunkSize = 262144;
            uint[] chunk = new uint[Math.Min(chunkSize, uploadCount)];
            for (int start = 0; start < uploadCount; start += chunk.Length)
            {
                int count = Math.Min(chunk.Length, uploadCount - start);
                Array.Copy(snapshot.ParticleAntLaunchBoundaryStates, start, chunk, 0, count);
                UpdateUIntBufferRegion(depositBuffer, particleAntLaunchBoundaryOffset + start, chunk, count);
            }
        }

        void UploadParticleAntHomeAxes(GpuSolverInput snapshot)
        {
            if (particleCount == 0 || particleAntHomeYAxisXOffset < 0) return;

            UploadParticleAxisChannel(snapshot.ParticleYAxesXyz, 0, particleAntHomeYAxisXOffset, 0);
            UploadParticleAxisChannel(snapshot.ParticleYAxesXyz, 1, particleAntHomeYAxisYOffset, 1);
            UploadParticleAxisChannel(snapshot.ParticleYAxesXyz, 2, particleAntHomeYAxisZOffset, 0);
            UploadParticleAxisChannel(snapshot.ParticleDirectionsXyz, 0, particleAntHomeXAxisXOffset, 1);
            UploadParticleAxisChannel(snapshot.ParticleDirectionsXyz, 1, particleAntHomeXAxisYOffset, 0);
            UploadParticleAxisChannel(snapshot.ParticleDirectionsXyz, 2, particleAntHomeXAxisZOffset, 0);
        }

        void UploadParticleAxisChannel(float[] source, int component, int channelOffset, float fallback)
        {
            const int chunkSize = 262144;
            float[] floatChunk = new float[Math.Min(chunkSize, particleCount)];
            uint[] uintChunk = new uint[floatChunk.Length];
            for (int start = 0; start < particleCount; start += floatChunk.Length)
            {
                int count = Math.Min(floatChunk.Length, particleCount - start);
                for (int i = 0; i < count; i++)
                {
                    int sourceIndex = (start + i) * 3 + component;
                    floatChunk[i] = source != null && sourceIndex < source.Length
                        ? source[sourceIndex]
                        : fallback;
                }

                Buffer.BlockCopy(floatChunk, 0, uintChunk, 0, count * sizeof(float));
                UpdateUIntBufferRegion(depositBuffer, channelOffset + start, uintChunk, count);
            }
        }

        void UpdateUIntBufferRegion(ID3D11Buffer buffer, int elementOffset, uint[] values, int valueCount)
        {
            if (buffer == null || values == null || valueCount <= 0) return;
            int left = checked(elementOffset * sizeof(uint));
            int right = checked((elementOffset + valueCount) * sizeof(uint));
            Vortice.Mathematics.Box region = new Vortice.Mathematics.Box(left, 0, 0, right, 1, 1);
            context.UpdateSubresource<uint>(new ReadOnlySpan<uint>(values, 0, valueCount), buffer, 0, 0, 0, region);
        }

        void CreateVoxelBehaviorBuffers(GpuSolverInput snapshot)
        {
            UpdateOptionalFloatBuffer(snapshot.VoxelBehaviorData, ref voxelBehaviorBuffer, ref voxelBehaviorView, ref voxelBehaviorElementCount);
            UpdateOptionalFloat3Buffer(snapshot.VoxelVectorData, ref voxelVectorBuffer, ref voxelVectorView);
            UpdateOptionalIntBuffer(snapshot.VoxelVectorFrequencies, ref voxelVectorFrequencyBuffer, ref voxelVectorFrequencyView);
            UpdateOptionalFloatBuffer(snapshot.VoxelDensityLimits, ref voxelDensityLimitsBuffer, ref voxelDensityLimitsView, ref voxelDensityLimitElementCount);
        }

        ID3D11Buffer CreateFloat4Buffer(float[] values, BindFlags bindFlags)
        {
            int elementCount = values.Length / 4;
            return device.CreateBuffer(
                values,
                bindFlags,
                ResourceUsage.Default,
                CpuAccessFlags.None,
                ResourceOptionFlags.BufferStructured,
                values.Length * sizeof(float),
                4 * sizeof(float));
        }

        void UpdateOptionalUIntBuffer(uint[] values, ref ID3D11Buffer buffer, ref ID3D11ShaderResourceView view)
        {
            if (values == null)
            {
                DisposeOptionalBuffer(ref buffer, ref view);
                return;
            }

            if (buffer == null)
            {
                buffer = device.CreateBuffer(
                    values,
                    BindFlags.ShaderResource,
                    ResourceUsage.Default,
                    CpuAccessFlags.None,
                    ResourceOptionFlags.BufferStructured,
                    values.Length * sizeof(uint),
                    sizeof(uint));
                view = CreateSrv(buffer, values.Length);
                return;
            }

            context.UpdateSubresourceSafe(values, buffer, 0, 0, 0, 0, false);
        }

        void UpdateOptionalFloat4Buffer(float[] values, ref ID3D11Buffer buffer, ref ID3D11ShaderResourceView view)
        {
            if (values == null)
            {
                DisposeOptionalBuffer(ref buffer, ref view);
                return;
            }

            if (buffer == null)
            {
                buffer = CreateFloat4Buffer(values, BindFlags.ShaderResource);
                view = CreateSrv(buffer, values.Length / 4);
                return;
            }

            context.UpdateSubresourceSafe(values, buffer, 0, 0, 0, 0, false);
        }

        void UpdateOptionalFloat3Buffer(float[] values, ref ID3D11Buffer buffer, ref ID3D11ShaderResourceView view)
        {
            if (values == null)
            {
                DisposeOptionalBuffer(ref buffer, ref view);
                return;
            }

            if (buffer == null)
            {
                buffer = device.CreateBuffer(
                    values,
                    BindFlags.ShaderResource,
                    ResourceUsage.Default,
                    CpuAccessFlags.None,
                    ResourceOptionFlags.BufferStructured,
                    values.Length * sizeof(float),
                    3 * sizeof(float));
                view = CreateSrv(buffer, values.Length / 3);
                return;
            }

            context.UpdateSubresourceSafe(values, buffer, 0, 0, 0, 0, false);
        }

        void UpdateOptionalIntBuffer(int[] values, ref ID3D11Buffer buffer, ref ID3D11ShaderResourceView view)
        {
            if (values == null)
            {
                DisposeOptionalBuffer(ref buffer, ref view);
                return;
            }

            if (buffer == null)
            {
                buffer = device.CreateBuffer(
                    values,
                    BindFlags.ShaderResource,
                    ResourceUsage.Default,
                    CpuAccessFlags.None,
                    ResourceOptionFlags.BufferStructured,
                    values.Length * sizeof(int),
                    sizeof(int));
                view = CreateSrv(buffer, values.Length);
                return;
            }

            context.UpdateSubresourceSafe(values, buffer, 0, 0, 0, 0, false);
        }

        void UpdateOptionalFloatBuffer(
            float[] values,
            ref ID3D11Buffer buffer,
            ref ID3D11ShaderResourceView view,
            ref int elementCount)
        {
            if (values == null)
            {
                DisposeOptionalBuffer(ref buffer, ref view);
                elementCount = 0;
                return;
            }

            if (buffer == null || elementCount != values.Length)
            {
                DisposeOptionalBuffer(ref buffer, ref view);
                buffer = device.CreateBuffer(
                    values,
                    BindFlags.ShaderResource,
                    ResourceUsage.Default,
                    CpuAccessFlags.None,
                    ResourceOptionFlags.BufferStructured,
                    values.Length * sizeof(float),
                    sizeof(float));
                view = CreateSrv(buffer, values.Length);
                elementCount = values.Length;
                return;
            }

            context.UpdateSubresourceSafe(values, buffer, 0, 0, 0, 0, false);
        }

        static int CheckedVoxelCount(int x, int y, int z)
        {
            long count = (long)Math.Max(0, x) * Math.Max(0, y) * Math.Max(0, z);
            if (count <= 0 || count > int.MaxValue)
            {
                throw new ArgumentException("GPU solver voxel dimensions exceed the supported field size.");
            }

            return (int)count;
        }

        static bool ValidVoxelFlags(uint[] flags, int count)
        {
            return flags == null || flags.Length == ((count + 31) >> 5);
        }

        static bool ValidStaticPreviewLayout(GpuSolverInput snapshot, int count)
        {
            if (snapshot == null || !snapshot.HasStaticPreviewInput) return true;
            return ValidOptionalStaticField(snapshot.StaticMinimumDensityValues, count)
                && ValidOptionalStaticField(snapshot.StaticMaximumDensityValues, count)
                && ValidOptionalStaticField(snapshot.StaticSpeedValues, count)
                && ValidOptionalStaticField(snapshot.StaticSensorDistanceValues, count)
                && ValidOptionalStaticField(snapshot.StaticSensorAngleValues, count)
                && ValidOptionalStaticField(snapshot.StaticRotationAngleValues, count);
        }

        static bool ValidOptionalStaticField(float[] values, int count)
        {
            return values == null || values.Length == count;
        }

        static bool ValidBehaviorLayout(GpuSolverInput snapshot, int count)
        {
            if (snapshot == null) return false;
            return ValidPackedChannels(
                snapshot.VoxelBehaviorData,
                count,
                snapshot.SpeedOffset,
                snapshot.SensorDistanceOffset,
                snapshot.SensorAngleOffset,
                snapshot.RotationAngleOffset);
        }

        static bool ValidDensityLimitLayout(GpuSolverInput snapshot, int count)
        {
            if (snapshot == null) return false;
            return ValidPackedChannels(
                snapshot.VoxelDensityLimits,
                count,
                snapshot.MinimumDensityOffset,
                snapshot.MaximumDensityOffset);
        }

        static bool ValidPackedChannels(float[] values, int count, params int[] offsets)
        {
            bool hasChannel = false;
            for (int i = 0; i < offsets.Length; i++)
            {
                int offset = offsets[i];
                if (offset < 0) continue;
                hasChannel = true;
                if (values == null || offset > values.Length - count) return false;
            }

            return hasChannel ? values != null : values == null || values.Length == 0;
        }

        static bool HasVoxelBehavior(GpuSolverInput snapshot)
        {
            return snapshot != null &&
                ((snapshot.VoxelBehaviorData != null && snapshot.VoxelBehaviorData.Length > 0) ||
                 snapshot.SpeedDefault != 1 ||
                 snapshot.SensorDistanceDefault != 1 ||
                 snapshot.SensorAngleDefault != 1 ||
                 snapshot.RotationAngleDefault != 1);
        }

        static bool HasVoxelDensityLimits(GpuSolverInput snapshot)
        {
            return snapshot != null &&
                ((snapshot.VoxelDensityLimits != null && snapshot.VoxelDensityLimits.Length > 0) ||
                 snapshot.MinimumDensityDefault >= 0 ||
                 snapshot.MaximumDensityDefault >= 0);
        }

        void ApplySnapshotChannelOffsets(GpuSolverInput snapshot)
        {
            speedOffset = hasVoxelBehavior ? snapshot.SpeedOffset : -1;
            sensorDistanceOffset = hasVoxelBehavior ? snapshot.SensorDistanceOffset : -1;
            sensorAngleOffset = hasVoxelBehavior ? snapshot.SensorAngleOffset : -1;
            rotationAngleOffset = hasVoxelBehavior ? snapshot.RotationAngleOffset : -1;
            minimumDensityOffset = hasVoxelDensityLimits ? snapshot.MinimumDensityOffset : -1;
            maximumDensityOffset = hasVoxelDensityLimits ? snapshot.MaximumDensityOffset : -1;
            speedDefault = snapshot.SpeedDefault;
            sensorDistanceDefault = snapshot.SensorDistanceDefault;
            sensorAngleDefault = snapshot.SensorAngleDefault;
            rotationAngleDefault = snapshot.RotationAngleDefault;
            minimumDensityDefault = snapshot.MinimumDensityDefault;
            maximumDensityDefault = snapshot.MaximumDensityDefault;
        }

        void ApplyStaticPreviewInput(GpuSolverInput snapshot)
        {
            hasStaticPreviewInput = snapshot.HasStaticPreviewInput;
            staticActiveVoxelFlags = snapshot.ActiveVoxelFlags;
            staticMinimumDensityValues = snapshot.StaticMinimumDensityValues;
            staticMaximumDensityValues = snapshot.StaticMaximumDensityValues;
            staticSpeedValues = snapshot.StaticSpeedValues;
            staticSensorDistanceValues = snapshot.StaticSensorDistanceValues;
            staticSensorAngleValues = snapshot.StaticSensorAngleValues;
            staticRotationAngleValues = snapshot.StaticRotationAngleValues;
            staticMinimumDensityDefault = snapshot.StaticMinimumDensityDefault;
            staticMaximumDensityDefault = snapshot.StaticMaximumDensityDefault;
            staticSpeedDefault = snapshot.StaticSpeedDefault;
            staticSensorDistanceDefault = snapshot.StaticSensorDistanceDefault;
            staticSensorAngleDefault = snapshot.StaticSensorAngleDefault;
            staticRotationAngleDefault = snapshot.StaticRotationAngleDefault;
        }

        static void DisposeOptionalBuffer(ref ID3D11Buffer buffer, ref ID3D11ShaderResourceView view)
        {
            if (view != null)
            {
                view.Dispose();
                view = null;
            }
            if (buffer != null)
            {
                buffer.Dispose();
                buffer = null;
            }
        }

        void EnsureNeighbourBuffers()
        {
            if (neighbourCountA != null && neighbourCountB != null)
            {
                return;
            }

            neighbourCountA = device.CreateBuffer(
                voxelCount * sizeof(float),
                BindFlags.UnorderedAccess,
                ResourceUsage.Default,
                CpuAccessFlags.None,
                ResourceOptionFlags.BufferStructured,
                sizeof(float));
            neighbourCountB = device.CreateBuffer(
                voxelCount * sizeof(float),
                BindFlags.UnorderedAccess,
                ResourceUsage.Default,
                CpuAccessFlags.None,
                ResourceOptionFlags.BufferStructured,
                sizeof(float));
            neighbourCountAView = CreateUav(neighbourCountA, voxelCount);
            neighbourCountBView = CreateUav(neighbourCountB, voxelCount);
            context.ClearUnorderedAccessView(neighbourCountAView, System.Numerics.Vector4.Zero);
            context.ClearUnorderedAccessView(neighbourCountBView, System.Numerics.Vector4.Zero);
        }

        void ResetFloatFieldPair(
            float[] values,
            ID3D11Buffer bufferA,
            ID3D11UnorderedAccessView viewA,
            ID3D11Buffer bufferB,
            ID3D11UnorderedAccessView viewB)
        {
            if (values != null && values.Length == voxelCount)
            {
                context.UpdateSubresourceSafe(values, bufferA, 0, 0, 0, 0, false);
                context.UpdateSubresourceSafe(values, bufferB, 0, 0, 0, 0, false);
                return;
            }

            context.ClearUnorderedAccessView(viewA, System.Numerics.Vector4.Zero);
            context.ClearUnorderedAccessView(viewB, System.Numerics.Vector4.Zero);
        }

        ID3D11Buffer CreateReadbackBuffer(int byteWidth)
        {
            return device.CreateBuffer(
                byteWidth,
                BindFlags.None,
                ResourceUsage.Staging,
                CpuAccessFlags.Read,
                ResourceOptionFlags.None,
                0);
        }

        ID3D11Buffer CreateStructuredBuffer(int elementCount, int stride, BindFlags bindFlags)
        {
            return device.CreateBuffer(
                checked(elementCount * stride),
                bindFlags,
                ResourceUsage.Default,
                CpuAccessFlags.None,
                ResourceOptionFlags.BufferStructured,
                stride);
        }

        ID3D11UnorderedAccessView CreateUav(ID3D11Buffer buffer, int elementCount)
        {
            return device.CreateUnorderedAccessView(
                buffer,
                new UnorderedAccessViewDescription(buffer, Format.Unknown, 0, elementCount, BufferUnorderedAccessViewFlags.None));
        }

        ID3D11ShaderResourceView CreateSrv(ID3D11Buffer buffer, int elementCount)
        {
            return device.CreateShaderResourceView(
                buffer,
                new ShaderResourceViewDescription(buffer, Format.Unknown, 0, elementCount, BufferExtendedShaderResourceViewFlags.None));
        }

        void CreateParameterBuffer()
        {
            parameterBuffer = device.CreateBuffer(
                Marshal.SizeOf(typeof(FullSolverParameters)),
                BindFlags.ConstantBuffer,
                ResourceUsage.Default,
                CpuAccessFlags.None,
                ResourceOptionFlags.None,
                0);
            volumeMeshParameterBuffer = device.CreateBuffer(
                Marshal.SizeOf(typeof(GpuMeshParameters)),
                BindFlags.ConstantBuffer,
                ResourceUsage.Default,
                CpuAccessFlags.None,
                ResourceOptionFlags.None,
                0);
        }

        void CompileShaders()
        {
            boundaryModeTransitionShader = CreateComputeShader("ApplyBoundaryModeTransition");
            claimParticleOwnersShader = CreateComputeShader("ClaimParticleOwners");
            cullParticleOwnerConflictsShader = CreateComputeShader("CullParticleOwnerConflicts");
            moveShader = CreateComputeShader("MoveParticlesAndDeposit");
            antMoveShader = CreateComputeShader("MoveAntParticlesAndDeposit");
            applyDepositsShader = CreateComputeShader("ApplyDeposits");
            projectFoodSourcesShader = CreateComputeShader("ProjectFoodSources");
            clearCountsShader = CreateComputeShader("ClearParticleCounts");
            countParticlesShader = CreateComputeShader("CountParticles");
            advanceParticleAgesShader = CreateComputeShader("AdvanceParticleAges");
            seedNeighbourCountsShader = CreateComputeShader("SeedNeighbourCounts");
            sumNeighbourAxisShader = CreateComputeShader("SumNeighbourAxis");
            applyParticleDeathShader = CreateComputeShader("ApplyParticleDeath");
            applyParticleDivisionShader = CreateComputeShader("ApplyParticleDivision");
            diffusionShader = CreateComputeShader("DiffuseAxis");
            diffusionXTiledShader = CreateComputeShader("DiffuseAxisXTiled");
            diffusionYTiledShader = CreateComputeShader("DiffuseAxisYTiled");
            diffusionZTiledShader = CreateComputeShader("DiffuseAxisZTiled");
            decayShader = CreateComputeShader("ApplyDecay");
            densityPreviewShader = CreateComputeShader("BuildDensityPreview");
            combinedDensityPreviewShader = CreateComputeShader("BuildCombinedDensityPreview");
            densityGradientPreviewShader = CreateComputeShader("BuildDensityGradientPreview");
            particlePreviewShader = CreateComputeShader("BuildParticlePreview");
            particleTrailPreviewShader = CreateComputeShader("BuildParticleTrailPreview");
            volumeSmoothShader = CreateComputeShader("SmoothVolumeForMesh");
            volumeCellClassifyShader = CreateComputeShader("ClassifyVolumeCells");
            volumeTriangleShader = CreateComputeShader("EmitVolumeTriangles");
        }

        ID3D11ComputeShader CreateComputeShader(string entryPoint)
        {
            string resourceName = "Nuclei4.GpuShaders." + entryPoint + ".cso";
            using (Stream stream = typeof(GpuFullSlimeSolverEngine).Assembly.GetManifestResourceStream(resourceName))
            {
                if (stream != null)
                {
                    byte[] bytecode = new byte[stream.Length];
                    int offset = 0;
                    while (offset < bytecode.Length)
                    {
                        int read = stream.Read(bytecode, offset, bytecode.Length - offset);
                        if (read <= 0) break;
                        offset += read;
                    }
                    if (offset == bytecode.Length)
                    {
                        return device.CreateComputeShader(bytecode, null);
                    }
                }
            }

            using (Blob runtimeBytecode = CompileShader(FullSolverShaderSource, entryPoint))
            {
                return device.CreateComputeShader(runtimeBytecode, null);
            }
        }

        static Blob CompileShader(string shaderSource, string entryPoint)
        {
            Blob shaderBytecode = null;
            Blob errorBlob = null;

            Result result = Compiler.Compile(
                shaderSource,
                null,
                null,
                entryPoint,
                "NucleiGpuFullSlimeSolver",
                "cs_5_0",
                ShaderFlags.OptimizationLevel3,
                EffectFlags.None,
                out shaderBytecode,
                out errorBlob);

            if (result.Failure)
            {
                string errors = BlobToString(errorBlob);
                if (errorBlob != null)
                {
                    errorBlob.Dispose();
                }

                throw new InvalidOperationException("full GPU solver shader compile failed: " + result + " " + errors);
            }

            if (errorBlob != null)
            {
                errorBlob.Dispose();
            }

            return shaderBytecode;
        }

        static string BlobToString(Blob blob)
        {
            if (blob == null || blob.BufferPointer == IntPtr.Zero)
            {
                return "";
            }

            int byteCount = (int)blob.BufferSize;
            if (byteCount <= 0)
            {
                return "";
            }

            byte[] bytes = new byte[byteCount];
            Marshal.Copy(blob.BufferPointer, bytes, 0, byteCount);
            return Encoding.ASCII.GetString(bytes).Trim('\0', '\r', '\n', ' ');
        }

        void DispatchLinear256(int count)
        {
            DispatchLinear(count, 256);
        }

        void DispatchLinear64(int count)
        {
            DispatchLinear(count, 64);
        }

        void DispatchLinear(int count, int threadsPerGroup)
        {
            if (count <= 0) return;

            long groupCount = ((long)count + threadsPerGroup - 1) / threadsPerGroup;
            int groupsX = (int)Math.Min(65535L, groupCount);
            int groupsY = (int)((groupCount + groupsX - 1) / groupsX);
            if (groupsY > 65535)
            {
                throw new InvalidOperationException("GPU dispatch exceeds the Direct3D 11 two-dimensional group limit.");
            }

            context.Dispatch(groupsX, groupsY, 1);
        }

        static int ReserveAuxiliaryChannel(ref int nextOffset, int count, bool enabled)
        {
            if (!enabled || count <= 0)
            {
                return -1;
            }

            int offset = nextOffset;
            nextOffset = checked(nextOffset + count);
            return offset;
        }

        void SwapDensityBuffers()
        {
            densityInA = !densityInA;
        }

        void UnbindComputeResources()
        {
            for (int i = 0; i <= 7; i++)
            {
                context.CSSetUnorderedAccessView(i, null, -1);
            }

            for (int i = 0; i <= 12; i++)
            {
                context.CSSetShaderResource(i, null);
            }

            context.CSSetShader(null);
        }

        static void CreateDevice(out ID3D11Device device, out ID3D11DeviceContext context, out bool softwareFallback)
        {
            softwareFallback = false;
            FeatureLevel[] levels = new FeatureLevel[] { FeatureLevel.Level_11_0 };
            FeatureLevel featureLevel;

            Result result = D3D11.D3D11CreateDevice(
                IntPtr.Zero,
                DriverType.Hardware,
                DeviceCreationFlags.None,
                levels,
                out device,
                out featureLevel,
                out context);

            if (result.Success)
            {
                return;
            }

            softwareFallback = true;
            result = D3D11.D3D11CreateDevice(
                IntPtr.Zero,
                DriverType.Warp,
                DeviceCreationFlags.None,
                levels,
                out device,
                out featureLevel,
                out context);

            if (result.Failure)
            {
                throw new InvalidOperationException("D3D11CreateDevice failed: " + result);
            }
        }

        public void Dispose()
        {
            DisposeWeights();
            for (int i = 0; i < staticFieldPreviewTextures.Length; i++)
            {
                DisposeStaticFieldPreviewTexture(i);
            }
            if (densityPreviewTextureView != null) densityPreviewTextureView.Dispose();
            if (densityPreviewTextureResourceView != null) densityPreviewTextureResourceView.Dispose();
            if (densityGradientPreviewTextureView != null) densityGradientPreviewTextureView.Dispose();
            if (particlePreviewTextureView != null) particlePreviewTextureView.Dispose();
            if (particleTrailPreviewTextureView != null) particleTrailPreviewTextureView.Dispose();
            if (particleTrailPreviewMutex != null) particleTrailPreviewMutex.Dispose();
            if (densityAView != null) densityAView.Dispose();
            if (densityBView != null) densityBView.Dispose();
            if (antFoodAView != null) antFoodAView.Dispose();
            if (antFoodBView != null) antFoodBView.Dispose();
            if (antBaseAView != null) antBaseAView.Dispose();
            if (antBaseBView != null) antBaseBView.Dispose();
            if (antFoodAResourceView != null) antFoodAResourceView.Dispose();
            if (antFoodBResourceView != null) antFoodBResourceView.Dispose();
            if (antBaseAResourceView != null) antBaseAResourceView.Dispose();
            if (antBaseBResourceView != null) antBaseBResourceView.Dispose();
            if (particlePositionView != null) particlePositionView.Dispose();
            if (particleDirectionView != null) particleDirectionView.Dispose();
            if (particleYAxisView != null) particleYAxisView.Dispose();
            if (particleHomeView != null) particleHomeView.Dispose();
            if (particleCountView != null) particleCountView.Dispose();
            if (particleOwnerView != null) particleOwnerView.Dispose();
            if (depositView != null) depositView.Dispose();
            if (neighbourCountAView != null) neighbourCountAView.Dispose();
            if (neighbourCountBView != null) neighbourCountBView.Dispose();
            if (groupData0View != null) groupData0View.Dispose();
            if (groupData1View != null) groupData1View.Dispose();
            if (groupColorDataView != null) groupColorDataView.Dispose();
            if (voxelFlagsView != null) voxelFlagsView.Dispose();
            if (activeVoxelFlagsView != null) activeVoxelFlagsView.Dispose();
            if (voxelBehaviorView != null) voxelBehaviorView.Dispose();
            if (voxelVectorView != null) voxelVectorView.Dispose();
            if (voxelVectorFrequencyView != null) voxelVectorFrequencyView.Dispose();
            if (voxelDensityLimitsView != null) voxelDensityLimitsView.Dispose();
            if (densityA != null) densityA.Dispose();
            if (densityB != null) densityB.Dispose();
            if (antFoodA != null) antFoodA.Dispose();
            if (antFoodB != null) antFoodB.Dispose();
            if (antBaseA != null) antBaseA.Dispose();
            if (antBaseB != null) antBaseB.Dispose();
            if (antFoodReadbackBuffer != null) antFoodReadbackBuffer.Dispose();
            if (antBaseReadbackBuffer != null) antBaseReadbackBuffer.Dispose();
            if (antFoodRemainingReadbackBuffer != null) antFoodRemainingReadbackBuffer.Dispose();
            if (densityPreviewTexture != null) densityPreviewTexture.Dispose();
            if (densityGradientPreviewTexture != null) densityGradientPreviewTexture.Dispose();
            if (particlePreviewTexture != null) particlePreviewTexture.Dispose();
            if (particleTrailPreviewTexture != null) particleTrailPreviewTexture.Dispose();
            if (densityReadbackBuffer != null) densityReadbackBuffer.Dispose();
            if (particlePositionBuffer != null) particlePositionBuffer.Dispose();
            if (particleDirectionBuffer != null) particleDirectionBuffer.Dispose();
            if (particleYAxisBuffer != null) particleYAxisBuffer.Dispose();
            if (particleHomeBuffer != null) particleHomeBuffer.Dispose();
            if (particlePositionReadbackBuffer != null) particlePositionReadbackBuffer.Dispose();
            if (particleDirectionReadbackBuffer != null) particleDirectionReadbackBuffer.Dispose();
            if (particleYAxisReadbackBuffer != null) particleYAxisReadbackBuffer.Dispose();
            if (particleHomeReadbackBuffer != null) particleHomeReadbackBuffer.Dispose();
            if (particleAuxReadbackBuffer != null) particleAuxReadbackBuffer.Dispose();
            if (populationStateReadbackBuffer != null) populationStateReadbackBuffer.Dispose();
            for (int i = 0; i < particlePositionPreviewReadbackBuffers.Length; i++)
            {
                if (particlePositionPreviewReadbackBuffers[i] != null)
                {
                    particlePositionPreviewReadbackBuffers[i].Dispose();
                    particlePositionPreviewReadbackBuffers[i] = null;
                }
            }
            for (int i = 0; i < populationAsyncReadbackBuffers.Length; i++)
            {
                if (populationAsyncReadbackBuffers[i] != null)
                {
                    populationAsyncReadbackBuffers[i].Dispose();
                    populationAsyncReadbackBuffers[i] = null;
                }
            }
            if (particleCountBuffer != null) particleCountBuffer.Dispose();
            if (particleOwnerBuffer != null) particleOwnerBuffer.Dispose();
            if (depositBuffer != null) depositBuffer.Dispose();
            if (neighbourCountA != null) neighbourCountA.Dispose();
            if (neighbourCountB != null) neighbourCountB.Dispose();
            if (groupData0Buffer != null) groupData0Buffer.Dispose();
            if (groupData1Buffer != null) groupData1Buffer.Dispose();
            if (groupColorDataBuffer != null) groupColorDataBuffer.Dispose();
            if (voxelFlagsBuffer != null) voxelFlagsBuffer.Dispose();
            if (activeVoxelFlagsBuffer != null) activeVoxelFlagsBuffer.Dispose();
            if (voxelBehaviorBuffer != null) voxelBehaviorBuffer.Dispose();
            if (voxelVectorBuffer != null) voxelVectorBuffer.Dispose();
            if (voxelVectorFrequencyBuffer != null) voxelVectorFrequencyBuffer.Dispose();
            if (voxelDensityLimitsBuffer != null) voxelDensityLimitsBuffer.Dispose();
            if (parameterBuffer != null) parameterBuffer.Dispose();
            if (volumeMeshParameterBuffer != null) volumeMeshParameterBuffer.Dispose();
            if (boundaryModeTransitionShader != null) boundaryModeTransitionShader.Dispose();
            if (claimParticleOwnersShader != null) claimParticleOwnersShader.Dispose();
            if (cullParticleOwnerConflictsShader != null) cullParticleOwnerConflictsShader.Dispose();
            if (moveShader != null) moveShader.Dispose();
            if (antMoveShader != null) antMoveShader.Dispose();
            if (applyDepositsShader != null) applyDepositsShader.Dispose();
            if (projectFoodSourcesShader != null) projectFoodSourcesShader.Dispose();
            if (clearCountsShader != null) clearCountsShader.Dispose();
            if (countParticlesShader != null) countParticlesShader.Dispose();
            if (advanceParticleAgesShader != null) advanceParticleAgesShader.Dispose();
            if (seedNeighbourCountsShader != null) seedNeighbourCountsShader.Dispose();
            if (sumNeighbourAxisShader != null) sumNeighbourAxisShader.Dispose();
            if (applyParticleDeathShader != null) applyParticleDeathShader.Dispose();
            if (applyParticleDivisionShader != null) applyParticleDivisionShader.Dispose();
            if (diffusionShader != null) diffusionShader.Dispose();
            if (diffusionXTiledShader != null) diffusionXTiledShader.Dispose();
            if (diffusionYTiledShader != null) diffusionYTiledShader.Dispose();
            if (diffusionZTiledShader != null) diffusionZTiledShader.Dispose();
            if (decayShader != null) decayShader.Dispose();
            if (densityPreviewShader != null) densityPreviewShader.Dispose();
            if (combinedDensityPreviewShader != null) combinedDensityPreviewShader.Dispose();
            if (densityGradientPreviewShader != null) densityGradientPreviewShader.Dispose();
            if (particlePreviewShader != null) particlePreviewShader.Dispose();
            if (particleTrailPreviewShader != null) particleTrailPreviewShader.Dispose();
            if (volumeSmoothShader != null) volumeSmoothShader.Dispose();
            if (volumeCellClassifyShader != null) volumeCellClassifyShader.Dispose();
            if (volumeTriangleShader != null) volumeTriangleShader.Dispose();
            if (benchmarkTimestampDisjointQuery != null) benchmarkTimestampDisjointQuery.Dispose();
            if (benchmarkTimestampStartQuery != null) benchmarkTimestampStartQuery.Dispose();
            if (benchmarkTimestampEndQuery != null) benchmarkTimestampEndQuery.Dispose();
            for (int i = 0; i < benchmarkPassTimestampQueries.Count; i++)
            {
                benchmarkPassTimestampQueries[i].Dispose();
            }
            benchmarkPassTimestampQueries.Clear();
            if (context != null) context.Dispose();
            if (device != null) device.Dispose();
        }

        void DisposeWeights()
        {
            if (weightsView != null)
            {
                weightsView.Dispose();
                weightsView = null;
            }

            if (weightsBuffer != null)
            {
                weightsBuffer.Dispose();
                weightsBuffer = null;
            }


            if (antWeightsView != null)
            {
                antWeightsView.Dispose();
                antWeightsView = null;
            }

            if (antWeightsBuffer != null)
            {
                antWeightsBuffer.Dispose();
                antWeightsBuffer = null;
            }
        }

        static void WriteSharedDensityPreviewStatus(string message)
        {
            try
            {
                string directory = Path.GetDirectoryName(SharedDensityPreviewStatusPath);
                if (!string.IsNullOrEmpty(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                File.AppendAllText(
                    SharedDensityPreviewStatusPath,
                    DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff") + " " + message + Environment.NewLine);
            }
            catch
            {
            }
        }

        static void WriteSharedParticlePreviewStatus(string message)
        {
            try
            {
                string directory = Path.GetDirectoryName(SharedParticlePreviewStatusPath);
                if (!string.IsNullOrEmpty(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                File.AppendAllText(
                    SharedParticlePreviewStatusPath,
                    DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff") + " " + message + Environment.NewLine);
            }
            catch
            {
            }
        }

        [StructLayout(LayoutKind.Sequential)]
        struct FullSolverParameters
        {
            public int ResX;
            public int ResY;
            public int ResZ;
            public int VoxelCount;
            public int ParticleCount;
            public int Axis;
            public int Range;
            public int Wrap;
            public int Tridimensional;
            public int PlanarXY;
            public int PlanarXZ;
            public int PlanarYZ;
            public int Iteration;
            public int GroupCount;
            public int HasActiveVoxelFlags;
            public int ApplyScalarDecayAfterDiffusion;
            public float VoxelSize;
            public float DimX;
            public float DimY;
            public float DimZ;
            public float Keep;
            public float Diffuse;
            public float Decay;
            public float DepositScale;
            public int PreviewWidth;
            public int PreviewHeight;
            public int PreviewAxisMode;
            public int PreviewSlice;
            public int PreviewAtlasColumns;
            public int PreviewAtlasRows;
            public int PreviewPadding0;
            public int PreviewPadding1;
            public int ParticleCapacity;
            public int MinimumPopulation;
            public int MaximumPopulation;
            public int DynamicPopulation;
            public int DivisionEnabled;
            public int DivisionMinimumAge;
            public int DivisionRange;
            public int DivisionMinimumNeighbours;
            public int DivisionMaximumNeighbours;
            public int DivisionFrequency;
            public int DeathEnabled;
            public int DeathMinimumAge;
            public int DeathRange;
            public int DeathMinimumNeighbours;
            public int DeathMaximumNeighbours;
            public int DeathFrequency;
            public int HasAntParticles;
            public int FieldMode;
            public int AntDiffuseRange;
            public int HasSlimeParticles;
            public float AntFoodDiffuse;
            public float AntFoodDecay;
            public float AntBaseDiffuse;
            public float AntBaseDecay;
            public float SlimeAntFood;
            public float SlimeAntBase;
            public float AntSlime;
            public float AntPaddingFloat;
            public int HasVoxelFlags;
            public int HasVoxelBehavior;
            public int HasVoxelVectors;
            public int HasVoxelDensityLimits;
            public int HasVoxelVectorFrequencies;
            public int VoxelVectorDefaultFrequency;
            public int HasVoxelVectorData;
            public int AntHomeYAxisZOffset;
            public float VoxelVectorDefaultX;
            public float VoxelVectorDefaultY;
            public float VoxelVectorDefaultZ;
            public float VectorDefaultPadding;
            public int SpeedOffset;
            public int SensorDistanceOffset;
            public int SensorAngleOffset;
            public int RotationAngleOffset;
            public int MinimumDensityOffset;
            public int MaximumDensityOffset;
            public int AntHomeYAxisXOffset;
            public int AntHomeYAxisYOffset;
            public float SpeedDefault;
            public float SensorDistanceDefault;
            public float SensorAngleDefault;
            public float RotationAngleDefault;
            public float MinimumDensityDefault;
            public float MaximumDensityDefault;
            public float ChannelDefaultPadding0;
            public float ChannelDefaultPadding1;
            public int SlimeDepositOffset;
            public int AntFoodDepositOffset;
            public int AntBaseDepositOffset;
            public int FoodRemainingOffset;
            public int FreeSlotOffset;
            public int ParticleAgeOffset;
            public int ParticleDeathNeighbourOffset;
            public int ParticleDivisionNeighbourOffset;
            public int ParticleGenerationOffset;
            public int ParticleAntStateOffset;
            public int FoodSourceOffset;
            public int RandomPopulationFrequency;
            public float RandomDeathProbability;
            public float RandomDivisionProbability;
            public int HighDepositOffset;
            public int AntLaunchBoundaryOffset;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct GpuMeshParameters
        {
            public int ResX;
            public int ResY;
            public int ResZ;
            public int CellStartZ;
            public int CellEndZ;
            public int ActiveOffset;
            public int ActiveCount;
            public float IsoValue;
            public float VoxelSize;
            public float Padding0;
            public float Padding1;
            public float Padding2;
        }

        const string FullSolverShaderSource = @"
cbuffer Params : register(b0)
{
    int ResX;
    int ResY;
    int ResZ;
    int VoxelCount;
    int ParticleCount;
    int Axis;
    int Range;
    int Wrap;
    int Tridimensional;
    int PlanarXY;
    int PlanarXZ;
    int PlanarYZ;
    int Iteration;
    int GroupCount;
    int HasActiveVoxelFlags;
    int ApplyScalarDecayAfterDiffusion;
    float VoxelSize;
    float DimX;
    float DimY;
    float DimZ;
    float Keep;
    float Diffuse;
    float Decay;
    float DepositScale;
    int PreviewWidth;
    int PreviewHeight;
    int PreviewAxisMode;
    int PreviewSlice;
    int PreviewAtlasColumns;
    int PreviewAtlasRows;
    int PreviewPadding0;
    int PreviewPadding1;
    int ParticleCapacity;
    int MinimumPopulation;
    int MaximumPopulation;
    int DynamicPopulation;
    int DivisionEnabled;
    int DivisionMinimumAge;
    int DivisionRange;
    int DivisionMinimumNeighbours;
    int DivisionMaximumNeighbours;
    int DivisionFrequency;
    int DeathEnabled;
    int DeathMinimumAge;
    int DeathRange;
    int DeathMinimumNeighbours;
    int DeathMaximumNeighbours;
    int DeathFrequency;
    int HasAntParticles;
    int FieldMode;
    int AntDiffuseRange;
    int HasSlimeParticles;
    float AntFoodDiffuse;
    float AntFoodDecay;
    float AntBaseDiffuse;
    float AntBaseDecay;
    float SlimeAntFood;
    float SlimeAntBase;
    float AntSlime;
    float AntPaddingFloat;
    int HasVoxelFlags;
    int HasVoxelBehavior;
    int HasVoxelVectors;
    int HasVoxelDensityLimits;
    int HasVoxelVectorFrequencies;
    int VoxelVectorDefaultFrequency;
    int HasVoxelVectorData;
    int AntHomeYAxisZOffset;
    float VoxelVectorDefaultX;
    float VoxelVectorDefaultY;
    float VoxelVectorDefaultZ;
    float VectorDefaultPadding;
    int SpeedOffset;
    int SensorDistanceOffset;
    int SensorAngleOffset;
    int RotationAngleOffset;
    int MinimumDensityOffset;
    int MaximumDensityOffset;
    int AntHomeYAxisXOffset;
    int AntHomeYAxisYOffset;
    float SpeedDefault;
    float SensorDistanceDefault;
    float SensorAngleDefault;
    float RotationAngleDefault;
    float MinimumDensityDefault;
    float MaximumDensityDefault;
    float ChannelDefaultPadding0;
    float ChannelDefaultPadding1;
    int SlimeDepositOffset;
    int AntFoodDepositOffset;
    int AntBaseDepositOffset;
    int FoodRemainingOffset;
    int FreeSlotOffset;
    int ParticleAgeOffset;
    int ParticleDeathNeighbourOffset;
    int ParticleDivisionNeighbourOffset;
    int ParticleGenerationOffset;
    int ParticleAntStateOffset;
    int FoodSourceOffset;
    int RandomPopulationFrequency;
    float RandomDeathProbability;
    float RandomDivisionProbability;
    int HighDepositOffset;
    int AntLaunchBoundaryOffset;
}

cbuffer MeshParams : register(b1)
{
    int MeshResX;
    int MeshResY;
    int MeshResZ;
    int MeshCellStartZ;
    int MeshCellEndZ;
    int MeshActiveOffset;
    int MeshActiveCount;
    float MeshIsoValue;
    float MeshVoxelSize;
    float MeshPadding0;
    float MeshPadding1;
    float MeshPadding2;
}

struct MeshTriangle
{
    float4 A;
    float4 B;
    float4 C;
};

RWStructuredBuffer<float> Source : register(u0);
RWStructuredBuffer<float> Destination : register(u1);
RWStructuredBuffer<uint> ParticleOwners : register(u1);
AppendStructuredBuffer<uint> MeshActiveCells : register(u1);
AppendStructuredBuffer<MeshTriangle> MeshTriangles : register(u1);
RWStructuredBuffer<float4> ParticlePosition : register(u2);
RWStructuredBuffer<float4> ParticleDirection : register(u3);
RWStructuredBuffer<float4> ParticleYAxis : register(u4);
RWStructuredBuffer<uint> ParticleCounts : register(u5);
RWStructuredBuffer<uint> DepositFixed : register(u6);
RWStructuredBuffer<float4> ParticleHome : register(u7);
RWStructuredBuffer<float> AntBaseDestination : register(u7);
RWTexture2D<float> DensityPreview : register(u7);
RWTexture2D<float4> CombinedDensityPreview : register(u7);
RWTexture2D<float4> DensityGradientPreview : register(u7);
RWTexture2D<float4> ParticlePreview : register(u7);
RWTexture2D<float4> ParticleTrailPreview : register(u7);

StructuredBuffer<float> Weights : register(t0);
StructuredBuffer<uint> MeshActiveCellSource : register(t0);
StructuredBuffer<float4> GroupData0 : register(t1);
StructuredBuffer<float4> GroupData1 : register(t2);
StructuredBuffer<uint> VoxelFlags : register(t3);
StructuredBuffer<float4> GroupColorData : register(t4);
StructuredBuffer<float> VoxelBehavior : register(t5);
StructuredBuffer<float3> VoxelVectors : register(t6);
StructuredBuffer<float> VoxelDensityLimits : register(t7);
StructuredBuffer<float> AntFoodPheromone : register(t8);
StructuredBuffer<float> AntBasePheromone : register(t9);
Texture2D<float4> DensityPreviewSource : register(t10);
StructuredBuffer<int> VoxelVectorFrequencies : register(t11);
StructuredBuffer<uint> ActiveVoxelFlags : register(t12);

// A 16-wide output tile plus a maximum 16-cell halo on either side. Invalid
// neighbour samples are masked to zero while loading the tile. Each thread
// keeps its unmasked center value in a register because V3 retains a blocked
// target's own density through Keep while excluding it from neighbour sums.
groupshared float TiledWeightedDensity[768];
groupshared float TiledDiffusionWeights[33];

uint LinearIndex256(uint3 dispatchThreadId)
{
    return dispatchThreadId.x + dispatchThreadId.y * (65535u * 256u);
}

uint LinearIndex64(uint3 dispatchThreadId)
{
    return dispatchThreadId.x + dispatchThreadId.y * (65535u * 64u);
}

int ActivePopulationIndex()
{
    return VoxelCount;
}

int FreePopulationIndex()
{
    return VoxelCount + 1;
}

int GroupPopulationIndex(int groupIndex)
{
    return VoxelCount + 4 + groupIndex;
}

int FreeSlotIndex(int stackIndex)
{
    return FreeSlotOffset + stackIndex;
}

int HighDepositIndex(int particleIndex)
{
    return HighDepositOffset + particleIndex;
}

int ParticleAntLaunchBoundaryIndex(int particleIndex)
{
    return AntLaunchBoundaryOffset + particleIndex;
}

int ParticleAntHomeYAxisXIndex(int particleIndex)
{
    return AntHomeYAxisXOffset + particleIndex;
}

int ParticleAntHomeYAxisYIndex(int particleIndex)
{
    return AntHomeYAxisYOffset + particleIndex;
}

int ParticleAntHomeYAxisZIndex(int particleIndex)
{
    return AntHomeYAxisZOffset + particleIndex;
}

int ParticleAntHomeXAxisXIndex(int particleIndex)
{
    return AntHomeYAxisZOffset + ParticleCapacity + particleIndex;
}

int ParticleAntHomeXAxisYIndex(int particleIndex)
{
    return AntHomeYAxisZOffset + ParticleCapacity * 2 + particleIndex;
}

int ParticleAntHomeXAxisZIndex(int particleIndex)
{
    return AntHomeYAxisZOffset + ParticleCapacity * 3 + particleIndex;
}

int ParticleAgeIndex(int particleIndex)
{
    return ParticleAgeOffset + particleIndex;
}

int ParticleDeathNeighbourIndex(int particleIndex)
{
    return ParticleDeathNeighbourOffset + particleIndex;
}

int ParticleDivisionNeighbourIndex(int particleIndex)
{
    return ParticleDivisionNeighbourOffset + particleIndex;
}

int ParticleGenerationIndex(int particleIndex)
{
    return ParticleGenerationOffset + particleIndex;
}

int ParticleAntStateIndex(int particleIndex)
{
    return ParticleAntStateOffset + particleIndex;
}

int SlimeDepositIndex(int voxelIndex)
{
    return SlimeDepositOffset + voxelIndex;
}

int AntFoodDepositIndex(int voxelIndex)
{
    return AntFoodDepositOffset + voxelIndex;
}

int AntBaseDepositIndex(int voxelIndex)
{
    return AntBaseDepositOffset + voxelIndex;
}

int FoodRemainingIndex(int voxelIndex)
{
    return FoodRemainingOffset + voxelIndex;
}

int FoodSourceIndex(int voxelIndex)
{
    return FoodSourceOffset + voxelIndex;
}

bool IsParticleAlive(int particleIndex)
{
    return particleIndex >= 0 && particleIndex < ParticleCapacity && ParticlePosition[particleIndex].w >= -0.5;
}

uint EmptyParticleOwner()
{
    return 0xffffffffu;
}

bool ParticleOwnsVoxel(int particleIndex, int voxelIndex)
{
    return voxelIndex >= 0 && voxelIndex < VoxelCount &&
           ParticleOwners[voxelIndex] == (uint)particleIndex;
}

bool TryClaimParticleMove(int particleIndex, int currentVoxelIndex, int targetVoxelIndex)
{
    if (!ParticleOwnsVoxel(particleIndex, currentVoxelIndex)) return false;
    if (targetVoxelIndex == currentVoxelIndex) return true;
    if (targetVoxelIndex < 0 || targetVoxelIndex >= VoxelCount) return false;

    uint token = (uint)particleIndex;
    uint previousTargetOwner;
    InterlockedCompareExchange(
        ParticleOwners[targetVoxelIndex],
        EmptyParticleOwner(),
        token,
        previousTargetOwner);
    if (previousTargetOwner != EmptyParticleOwner()) return false;

    // Keep the old voxel reserved until the new claim is established. Clearing
    // its count before publishing the empty owner prevents a later claimant's
    // count from being erased by this thread.
    ParticleCounts[currentVoxelIndex] = 0u;
    uint previousCurrentOwner;
    InterlockedCompareExchange(
        ParticleOwners[currentVoxelIndex],
        token,
        EmptyParticleOwner(),
        previousCurrentOwner);
    if (previousCurrentOwner != token)
    {
        ParticleCounts[currentVoxelIndex] = 1u;
        uint ignored;
        InterlockedCompareExchange(
            ParticleOwners[targetVoxelIndex],
            token,
            EmptyParticleOwner(),
            ignored);
        return false;
    }

    ParticleCounts[targetVoxelIndex] = 1u;
    return true;
}

void ReleaseParticleVoxel(int particleIndex, int voxelIndex)
{
    if (voxelIndex < 0 || voxelIndex >= VoxelCount) return;

    uint token = (uint)particleIndex;
    // Clear the count while this particle still owns the voxel. Once the empty
    // owner is published, another thread may claim and restore the count to one.
    ParticleCounts[voxelIndex] = 0u;
    uint previousOwner;
    InterlockedCompareExchange(
        ParticleOwners[voxelIndex],
        token,
        EmptyParticleOwner(),
        previousOwner);
    if (previousOwner != token)
    {
        ParticleCounts[voxelIndex] = 1u;
    }
}

int FlatIndex(int x, int y, int z)
{
    return x * ResY * ResZ + y * ResZ + z;
}

void Coordinates(int index, out int x, out int y, out int z)
{
    int yz = ResY * ResZ;
    x = index / yz;
    int rem = index - x * yz;
    y = rem / ResZ;
    z = rem - y * ResZ;
}

float3 VoxelCenter(int index)
{
    int x;
    int y;
    int z;
    Coordinates(index, x, y, z);
    float3 center = float3(
        (x + 0.5) * VoxelSize,
        (y + 0.5) * VoxelSize,
        (z + 0.5) * VoxelSize);
    if (PlanarXY != 0) center.z = DimZ * 0.5;
    if (PlanarXZ != 0) center.y = DimY * 0.5;
    if (PlanarYZ != 0) center.x = DimX * 0.5;
    return center;
}

int WrapIndex(int value, int count)
{
    if (value >= 0 && value < count) return value;
    value = value % count;
    return value < 0 ? value + count : value;
}

bool IsBoundary(int x, int y, int z)
{
    if (Tridimensional != 0)
    {
        return x == 0 || x == ResX - 1 || y == 0 || y == ResY - 1 || z == 0 || z == ResZ - 1;
    }

    if (PlanarXY != 0)
    {
        return x == 0 || x == ResX - 1 || y == 0 || y == ResY - 1;
    }

    if (PlanarXZ != 0)
    {
        return x == 0 || x == ResX - 1 || z == 0 || z == ResZ - 1;
    }

    return y == 0 || y == ResY - 1 || z == 0 || z == ResZ - 1;
}

bool IsValidVoxelIndex(int index)
{
    if (index < 0 || index >= VoxelCount) return false;
    if (HasVoxelFlags == 0) return true;
    uint word = VoxelFlags[(uint)index >> 5];
    return (word & (1u << ((uint)index & 31u))) != 0;
}

bool IsActiveVoxelIndex(int index)
{
    if (index < 0 || index >= VoxelCount) return false;
    if (HasActiveVoxelFlags == 0) return true;
    uint word = ActiveVoxelFlags[(uint)index >> 5];
    return (word & (1u << ((uint)index & 31u))) != 0;
}

float ReadBehaviorChannel(int offset, int index, float fallback)
{
    return offset >= 0 ? VoxelBehavior[offset + index] : fallback;
}

float ReadDensityLimit(int offset, int index, float fallback)
{
    return offset >= 0 ? VoxelDensityLimits[offset + index] : fallback;
}

float ClampDensityLimits(float value, int index)
{
    if (index < 0 || index >= VoxelCount) return value;

    if (HasVoxelDensityLimits == 0) return value;

    float minDensity = ReadDensityLimit(MinimumDensityOffset, index, MinimumDensityDefault);
    float maxDensity = ReadDensityLimit(MaximumDensityOffset, index, MaximumDensityDefault);

    if (maxDensity >= 0.0 && value > maxDensity) value = maxDensity;
    // V3's scalar field only raises a positive value to its minimum. Both ant
    // pheromone diffusion paths apply the authored minimum unconditionally,
    // including when the diffused value is exactly zero.
    if (minDensity >= 0.0
        && value < minDensity
        && (FieldMode != 0 || value > 0.0)) value = minDensity;
    return value;
}

float RemainingFoodAt(int index)
{
    if (FoodRemainingOffset < 0 || index < 0 || index >= VoxelCount) return 0.0;
    return DepositFixed[FoodRemainingIndex(index)] / DepositScale;
}

bool HasRemainingFoodAt(int index)
{
    return FoodRemainingOffset >= 0 && index >= 0 && index < VoxelCount && DepositFixed[FoodRemainingIndex(index)] != 0u;
}

float FoodSourceAt(int index)
{
    if (FoodSourceOffset < 0 || index < 0 || index >= VoxelCount) return 0.0;
    return DepositFixed[FoodSourceIndex(index)] / DepositScale;
}

int VoxelIndexFromPosition(float3 position)
{
    int x = PlanarYZ != 0 ? 0 : (int)floor(position.x / VoxelSize);
    int y = PlanarXZ != 0 ? 0 : (int)floor(position.y / VoxelSize);
    int z = PlanarXY != 0 ? 0 : (int)floor(position.z / VoxelSize);

    if (x < 0 || x >= ResX || y < 0 || y >= ResY || z < 0 || z >= ResZ)
    {
        return -1;
    }

    int index = FlatIndex(x, y, z);
    if (!IsValidVoxelIndex(index)) return -1;
    return index;
}

int ActiveVoxelIndexFromPosition(float3 position)
{
    int x = PlanarYZ != 0 ? 0 : (int)floor(position.x / VoxelSize);
    int y = PlanarXZ != 0 ? 0 : (int)floor(position.y / VoxelSize);
    int z = PlanarXY != 0 ? 0 : (int)floor(position.z / VoxelSize);
    if (x < 0 || x >= ResX || y < 0 || y >= ResY || z < 0 || z >= ResZ)
    {
        return -1;
    }

    int index = FlatIndex(x, y, z);
    return IsActiveVoxelIndex(index) ? index : -1;
}

float3 NormalizeOr(float3 value, float3 fallback)
{
    float lenSq = dot(value, value);
    if (lenSq <= 1e-12) return fallback;
    return value * rsqrt(lenSq);
}

float3 SafeYAxis(float3 x, float3 y)
{
    y = NormalizeOr(y, abs(x.z) < 0.9 ? normalize(cross(float3(0, 0, 1), x)) : normalize(cross(float3(0, 1, 0), x)));
    y = y - x * dot(x, y);
    return NormalizeOr(y, abs(x.z) < 0.9 ? normalize(cross(float3(0, 0, 1), x)) : normalize(cross(float3(0, 1, 0), x)));
}

// Mirrors V3 ParticleGenerator.ToUnit so both toolsets draw from the same range.
// The cast and float reciprocal are load-bearing: the masked value is always
// below 16777216, so an integer division truncates every draw to exactly 0 and
// makes any positive probability fire on every particle.
float UnitFromHash(uint value)
{
    return (float)(value & 0x00FFFFFFu) * (1.0f / 16777216.0f);
}

bool RandomPopulationDue()
{
    return RandomPopulationFrequency > 0 && (Iteration % RandomPopulationFrequency) == 0;
}

uint Hash(uint value)
{
    value ^= value >> 16;
    value *= 0x7feb352du;
    value ^= value >> 15;
    value *= 0x846ca68bu;
    value ^= value >> 16;
    return value;
}

float Hash01(uint value)
{
    return (Hash(value) & 0x00ffffffu) / 16777215.0;
}

float3 RandomPlanarVector(uint seed)
{
    float angle = Hash01(seed) * 6.28318530718;
    float c = cos(angle);
    float s = sin(angle);

    if (PlanarXZ != 0) return float3(c, 0, s);
    if (PlanarYZ != 0) return float3(0, c, s);
    return float3(c, s, 0);
}

float3 RandomUnitDirection(uint seed)
{
    if (Tridimensional == 0)
    {
        return RandomPlanarVector(seed);
    }

    float z = Hash01(seed ^ 0x68bc21ebu) * 2.0 - 1.0;
    float angle = Hash01(seed ^ 0x02e5be93u) * 6.28318530718;
    float radius = sqrt(max(0.0, 1.0 - z * z));
    return float3(cos(angle) * radius, sin(angle) * radius, z);
}

float PositiveModulo(float value, float extent)
{
    if (extent <= 0.0) return 0.0;
    return value - floor(value / extent) * extent;
}

float V3WrapCoordinate(float position, float extent)
{
    if (position < 0.01) return extent - 0.1;
    if (position > extent - 0.01) return 0.1;
    return position;
}

float3 WrapSensorPosition(float3 p)
{
    if (PlanarYZ == 0) p.x = V3WrapCoordinate(p.x, DimX);
    if (PlanarXZ == 0) p.y = V3WrapCoordinate(p.y, DimY);
    if (PlanarXY == 0) p.z = V3WrapCoordinate(p.z, DimZ);

    return p;
}

float SampleDensity(float3 p)
{
    if (Wrap != 0)
    {
        p = WrapSensorPosition(p);
    }

    int index = VoxelIndexFromPosition(p);
    if (index < 0) return -1.0;

    int x;
    int y;
    int z;
    Coordinates(index, x, y, z);
    if (Wrap == 0 && IsBoundary(x, y, z)) return -1.0;

    float value = Source[index];
    float foodValue = FoodSourceAt(index);
    if (foodValue > 0.0)
    {
        // V3 senses the authored slime-food strength directly, even when the
        // diffused density field has already saturated at one.
        value = max(value, foodValue);
    }

    if (HasAntParticles != 0)
    {
        value += AntFoodPheromone[index] * SlimeAntFood;
        value += AntBasePheromone[index] * SlimeAntBase;
    }

    return value;
}

uint AntOrderKey(int particleIndex, uint salt)
{
    return Hash((uint)particleIndex + (uint)Iteration * 747796405u + salt);
}

float SampleAntField(float3 p, bool foundFood, int currentParentIndex, int particleIndex, bool antOnly)
{
    if (Wrap != 0) p = WrapSensorPosition(p);
    int index = VoxelIndexFromPosition(p);
    if (index < 0) return -1.0;

    int x;
    int y;
    int z;
    Coordinates(index, x, y, z);
    if (Wrap == 0 && IsBoundary(x, y, z)) return -1.0;

    float value = -99.0;
    // V3 lets ants sense the scalar density field whenever ant_slime is enabled,
    // including an ant-only retained population and its specialized move shader.
    float slimeInfluence = AntSlime > 0.0 ? Source[index] * AntSlime : 0.0;
    if (foundFood)
    {
        value = AntBasePheromone[index] + slimeInfluence;
    }
    else if (currentParentIndex >= 0 && !HasRemainingFoodAt(currentParentIndex))
    {
        if (AntFoodPheromone[index] > 0.0)
        {
            value = AntFoodPheromone[index] + slimeInfluence;
        }
        else if (((uint)Iteration + AntOrderKey(particleIndex, 0x51ed270bu)) % 3u == 0u)
        {
            value = AntBasePheromone[index] + slimeInfluence;
        }
    }
    else
    {
        value = 1.0;
    }

    return value == -99.0 ? 0.0 : value;
}

bool TryConsumeFood(int voxelIndex)
{
    if (FoodRemainingOffset < 0 || voxelIndex < 0 || voxelIndex >= VoxelCount) return false;
    uint amount = (uint)round(DepositScale);
    uint current = DepositFixed[FoodRemainingIndex(voxelIndex)];
    while (current > 0u)
    {
        uint consumed = min(current, amount);
        uint original;
        InterlockedCompareExchange(DepositFixed[FoodRemainingIndex(voxelIndex)], current, current - consumed, original);
        if (original == current) return true;
        current = original;
    }
    return false;
}

void UpdateBest(float value, int index, inout float minValue, inout float maxValue, inout int bestIndex)
{
    if (value > maxValue)
    {
        maxValue = value;
        bestIndex = index;
    }

    if (value < minValue)
    {
        minValue = value;
    }
}

int ChooseBestSensor(float value0, float value1, float value2, float value3, float value4)
{
    float minValue = 9999.0;
    float maxValue = -1.0;
    int bestIndex = -1;

    UpdateBest(value0, 0, minValue, maxValue, bestIndex);
    UpdateBest(value1, 1, minValue, maxValue, bestIndex);
    UpdateBest(value2, 2, minValue, maxValue, bestIndex);

    if (Tridimensional != 0)
    {
        UpdateBest(value3, 3, minValue, maxValue, bestIndex);
        UpdateBest(value4, 4, minValue, maxValue, bestIndex);
    }

    if (minValue == maxValue)
    {
        bestIndex = 1;
    }

    return bestIndex;
}

float ConnectedSensorValue(float value)
{
    if (isnan(value) || value <= 0.0) return 0.0;
    if (isinf(value)) return 3.402823466e+38;
    return value;
}

float ConnectedSensorWeight(float value, float maxValue, float selectivityPower)
{
    if (value <= 0.0) return 0.0;
    if (value >= maxValue) return 1.0;
    return pow(value / maxValue, selectivityPower);
}

float ConnectedSteeringSample(int particleIndex)
{
    uint sampleKey = (uint)particleIndex ^ ((uint)Iteration * 2654435769u) ^ 2738958700u;
    sampleKey ^= sampleKey >> 16;
    sampleKey *= 2146121005u;
    sampleKey ^= sampleKey >> 15;
    sampleKey *= 2221713035u;
    sampleKey ^= sampleKey >> 16;
    // HLSL performs this multiplication in float precision. The largest hash
    // can otherwise round to exactly 1.0 even though V3's random sample is
    // strictly less than one, allowing a zero-weight fallthrough choice.
    return min(sampleKey * (1.0 / 4294967296.0), asfloat(0x3f7fffffu));
}

int ChooseConnectedSensor(
    float value0,
    float value1,
    float value2,
    float value3,
    float value4,
    float exploration,
    int particleIndex)
{
    exploration = saturate(exploration);
    if (exploration <= 0.0)
    {
        return ChooseBestSensor(value0, value1, value2, value3, value4);
    }

    float positive0 = ConnectedSensorValue(value0);
    float positive1 = ConnectedSensorValue(value1);
    float positive2 = ConnectedSensorValue(value2);
    float positive3 = Tridimensional != 0 ? ConnectedSensorValue(value3) : 0.0;
    float positive4 = Tridimensional != 0 ? ConnectedSensorValue(value4) : 0.0;
    float maxPositive = max(positive0, max(positive1, positive2));
    if (Tridimensional != 0) maxPositive = max(maxPositive, max(positive3, positive4));
    if (maxPositive <= 0.0)
    {
        return ChooseBestSensor(value0, value1, value2, value3, value4);
    }

    float selectivityPower = 7.0 * (1.0 - exploration);
    float weight0 = ConnectedSensorWeight(positive0, maxPositive, selectivityPower);
    float weight1 = ConnectedSensorWeight(positive1, maxPositive, selectivityPower);
    float weight2 = ConnectedSensorWeight(positive2, maxPositive, selectivityPower);
    float weight3 = Tridimensional != 0 ? ConnectedSensorWeight(positive3, maxPositive, selectivityPower) : 0.0;
    float weight4 = Tridimensional != 0 ? ConnectedSensorWeight(positive4, maxPositive, selectivityPower) : 0.0;
    float totalWeight = weight0 + weight1 + weight2 + weight3 + weight4;
    if (totalWeight <= 0.0 || isnan(totalWeight))
    {
        return ChooseBestSensor(value0, value1, value2, value3, value4);
    }

    float target = ConnectedSteeringSample(particleIndex) * totalWeight;
    float cumulativeWeight = weight0;
    if (target < cumulativeWeight) return 0;
    cumulativeWeight += weight1;
    if (target < cumulativeWeight) return 1;
    cumulativeWeight += weight2;
    if (target < cumulativeWeight) return 2;
    if (Tridimensional != 0)
    {
        cumulativeWeight += weight3;
        if (target < cumulativeWeight) return 3;
        return 4;
    }

    return 2;
}

float3 RotateForce(int bestIndex, float3 x, float3 y, float rotationCos, float rotationSin)
{
    if (bestIndex == 0) return NormalizeOr(x * rotationCos - y * rotationSin, x);
    if (bestIndex == 2) return NormalizeOr(x * rotationCos + y * rotationSin, x);

    if (Tridimensional != 0)
    {
        float3 zAxis = NormalizeOr(cross(y, x), float3(0, 0, 1));
        if (bestIndex == 3) return NormalizeOr(x * rotationCos + zAxis * rotationSin, x);
        if (bestIndex == 4) return NormalizeOr(x * rotationCos - zAxis * rotationSin, x);
    }

    return x;
}

float3 RotateAroundAxis(float3 value, float3 axis, float rotationCos, float rotationSin)
{
    axis = NormalizeOr(axis, float3(0, 1, 0));
    return value * rotationCos
        + cross(axis, value) * rotationSin
        + axis * dot(axis, value) * (1.0 - rotationCos);
}

void ReflectSensorPlane(inout float3 planeX, inout float3 planeY, int coordinate)
{
    // V3 reflects the current plane X axis, rotates that reflected vector by
    // 90 degrees about the old plane Z axis, and reconstructs the plane. This
    // is deliberately not the same as flipping one component of planeY.
    float3 planeZ = NormalizeOr(cross(planeX, planeY), float3(0, 0, 1));
    float3 reflectedX = NormalizeOr(planeX, float3(1, 0, 0));
    if (coordinate == 0) reflectedX.x = -reflectedX.x;
    else if (coordinate == 1) reflectedX.y = -reflectedX.y;
    else reflectedX.z = -reflectedX.z;

    float3 reconstructedY = RotateAroundAxis(reflectedX, planeZ, 0.0, 1.0);
    planeX = NormalizeOr(reflectedX, planeX);
    planeY = SafeYAxis(planeX, reconstructedY);
}

void ApplyNonWrappedSensorBoundaries(inout float3 sensorPosition, inout float3 planeX, inout float3 planeY)
{
    float boundaryDistance = VoxelSize;
    if ((PlanarYZ != 0 || (sensorPosition.x > boundaryDistance && sensorPosition.x < DimX - boundaryDistance)) &&
        (PlanarXZ != 0 || (sensorPosition.y > boundaryDistance && sensorPosition.y < DimY - boundaryDistance)) &&
        (PlanarXY != 0 || (sensorPosition.z > boundaryDistance && sensorPosition.z < DimZ - boundaryDistance)))
    {
        return;
    }

    // Match V3 boundaries() exactly: X, then Y, then Z; lower and upper tests
    // are independent so a degenerate extent can reflect twice. Planar Z is
    // clamped but does not mutate the plane unless the solver is truly 3D.
    if (PlanarYZ == 0)
    {
        if (sensorPosition.x <= boundaryDistance)
        {
            sensorPosition.x = boundaryDistance;
            ReflectSensorPlane(planeX, planeY, 0);
        }
        if (sensorPosition.x >= DimX - boundaryDistance)
        {
            sensorPosition.x = DimX - boundaryDistance;
            ReflectSensorPlane(planeX, planeY, 0);
        }
    }

    if (PlanarXZ == 0)
    {
        if (sensorPosition.y <= boundaryDistance)
        {
            sensorPosition.y = boundaryDistance;
            ReflectSensorPlane(planeX, planeY, 1);
        }
        if (sensorPosition.y >= DimY - boundaryDistance)
        {
            sensorPosition.y = DimY - boundaryDistance;
            ReflectSensorPlane(planeX, planeY, 1);
        }
    }

    if (PlanarXY == 0)
    {
        if (sensorPosition.z <= boundaryDistance)
        {
            sensorPosition.z = boundaryDistance;
            if (Tridimensional != 0) ReflectSensorPlane(planeX, planeY, 2);
        }
        if (sensorPosition.z >= DimZ - boundaryDistance)
        {
            sensorPosition.z = DimZ - boundaryDistance;
            if (Tridimensional != 0) ReflectSensorPlane(planeX, planeY, 2);
        }
    }
}

float3 ApplyPlanarMode(float3 value)
{
    if (PlanarXY != 0) value.z = 0;
    if (PlanarXZ != 0) value.y = 0;
    if (PlanarYZ != 0) value.x = 0;
    return value;
}

float3 ApplyPlanarPosition(float3 value)
{
    // V3 flattens the direction axes but preserves the input plane's origin.
    // Keeping the inactive coordinate also avoids a false ant home offset for
    // valid planar particles that were not created on the voxel mid-plane.
    return value;
}

float3 CenterPlanarMovePosition(float3 value)
{
    // Reset and boundary-mode transitions preserve an authored off-midplane
    // origin, but V3 centers the inactive coordinate on every attempted move.
    if (PlanarXY != 0) value.z = DimZ * 0.5;
    if (PlanarXZ != 0) value.y = DimY * 0.5;
    if (PlanarYZ != 0) value.x = DimX * 0.5;
    return value;
}

void WrapMovementCoordinate(inout float position, float extent, inout uint wrapped)
{
    // V3 uses a fixed tolerance and teleports to a fixed inset, discarding all
    // overshoot. Values exactly on either tolerance edge remain untouched.
    float original = position;
    position = V3WrapCoordinate(position, extent);
    if (position != original)
    {
        wrapped = 1u;
    }
}

void ReflectMovementCoordinate(inout float position, inout float direction, float extent)
{
    float minimum = VoxelSize;
    float maximum = extent - VoxelSize;
    // These are intentionally independent checks. V3 clamps to the exact voxel
    // boundary and flips once for each crossed side, even in degenerate extents.
    if (position <= minimum)
    {
        position = minimum;
        direction = -direction;
    }
    if (position >= maximum)
    {
        position = maximum;
        direction = -direction;
    }
}

void ApplyMovementBoundaries(inout float3 position, inout float3 direction, inout uint wrapped)
{
    if (Wrap != 0)
    {
        if (PlanarYZ == 0) WrapMovementCoordinate(position.x, DimX, wrapped);
        if (PlanarXZ == 0) WrapMovementCoordinate(position.y, DimY, wrapped);
        if (PlanarXY == 0) WrapMovementCoordinate(position.z, DimZ, wrapped);

        return;
    }

    if (PlanarYZ == 0) ReflectMovementCoordinate(position.x, direction.x, DimX);
    if (PlanarXZ == 0) ReflectMovementCoordinate(position.y, direction.y, DimY);
    if (PlanarXY == 0) ReflectMovementCoordinate(position.z, direction.z, DimZ);
}

float AntLaunchVariation(float variationBase, float salt)
{
    float value = sin(variationBase + salt) * 43758.5453;
    return value - floor(value);
}

uint AntLaunchDuration(float3 homePosition, float groupSpeed)
{
    float speed = abs(groupSpeed);
    if (speed <= 2.3283064365386963e-10) return 0u;

    float minimumBoundary = VoxelSize;
    float dx = PlanarYZ != 0
        ? 0.0
        : max(abs(homePosition.x - minimumBoundary), abs(DimX - minimumBoundary - homePosition.x));
    float dy = PlanarXZ != 0
        ? 0.0
        : max(abs(homePosition.y - minimumBoundary), abs(DimY - minimumBoundary - homePosition.y));
    float dz = PlanarXY != 0
        ? 0.0
        : max(abs(homePosition.z - minimumBoundary), abs(DimZ - minimumBoundary - homePosition.z));
    float farthestBoundaryDistance = sqrt(dx * dx + dy * dy + dz * dz) * 0.75;
    float duration = ceil(farthestBoundaryDistance / speed);
    if (isnan(duration) || isinf(duration) || duration >= 2147483520.0) return 2147483647u;
    return (uint)max(0.0, duration);
}

float3 AntLaunchWaveVector(int particleIndex, uint particleAge, float variationBase)
{
    float3 lateralAxis = float3(
        asfloat(DepositFixed[ParticleAntHomeYAxisXIndex(particleIndex)]),
        asfloat(DepositFixed[ParticleAntHomeYAxisYIndex(particleIndex)]),
        asfloat(DepositFixed[ParticleAntHomeYAxisZIndex(particleIndex)]));
    float lengthSquared = dot(lateralAxis, lateralAxis);
    if (lengthSquared <= 1e-12) return 0.0;
    lateralAxis *= rsqrt(lengthSquared);

    float frequency = 0.28 + AntLaunchVariation(variationBase, 73.91) * 0.14;
    float phase = AntLaunchVariation(variationBase, 109.37) * 6.28318530718;
    float wave = sin((float)particleAge * frequency + phase);
    return lateralAxis * wave;
}

bool AntMoveTouchesBoundary(float3 nextPosition)
{
    float boundaryDistance = VoxelSize;
    return (PlanarYZ == 0 && (nextPosition.x <= boundaryDistance || nextPosition.x >= DimX - boundaryDistance)) ||
           (PlanarXZ == 0 && (nextPosition.y <= boundaryDistance || nextPosition.y >= DimY - boundaryDistance)) ||
           (PlanarXY == 0 && (nextPosition.z <= boundaryDistance || nextPosition.z >= DimZ - boundaryDistance));
}

int BankersRoundToInt(float value)
{
    float magnitude = abs(value);
    int lower = (int)floor(magnitude);
    float fraction = magnitude - (float)lower;
    int rounded = fraction > 0.5 || (fraction == 0.5 && (lower & 1) != 0)
        ? lower + 1
        : lower;
    return value < 0.0 ? -rounded : rounded;
}

bool CanDepositAtVoxel(int index, float rawSensorDistance)
{
    if (Wrap != 0) return true;

    int x;
    int y;
    int z;
    Coordinates(index, x, y, z);

    int boundaryRange = 1;
    float sensorDiameter = rawSensorDistance * 2.0;

    if (Tridimensional != 0)
    {
        if (DimX > sensorDiameter && DimY > sensorDiameter && DimZ > sensorDiameter)
        {
            boundaryRange = BankersRoundToInt(rawSensorDistance);
        }

        return x >= boundaryRange && x < ResX - boundaryRange &&
               y >= boundaryRange && y < ResY - boundaryRange &&
               z >= boundaryRange && z < ResZ - boundaryRange;
    }

    if (PlanarXY != 0)
    {
        if (DimX > sensorDiameter && DimY > sensorDiameter)
        {
            boundaryRange = BankersRoundToInt(rawSensorDistance);
        }

        return x >= boundaryRange && x < ResX - boundaryRange &&
               y >= boundaryRange && y < ResY - boundaryRange;
    }

    if (PlanarXZ != 0)
    {
        if (DimX > sensorDiameter && DimZ > sensorDiameter)
        {
            boundaryRange = BankersRoundToInt(rawSensorDistance);
        }

        return x >= boundaryRange && x < ResX - boundaryRange &&
               z >= boundaryRange && z < ResZ - boundaryRange;
    }

    if (DimY > sensorDiameter && DimZ > sensorDiameter)
    {
        boundaryRange = BankersRoundToInt(rawSensorDistance);
    }

    return y >= boundaryRange && y < ResY - boundaryRange &&
           z >= boundaryRange && z < ResZ - boundaryRange;
}

bool TryRecoverWalkableStep(int currentParentIndex, int particleIndex, float speed, out int recoveredIndex, out float3 recoveredPosition)
{
    recoveredIndex = -1;
    recoveredPosition = 0.0;
    // V3 recovers around the coordinates of any stored parent voxel, including
    // an active max-density obstacle introduced by a live boundary-mode change.
    if (!IsActiveVoxelIndex(currentParentIndex)) return false;

    int parentX;
    int parentY;
    int parentZ;
    Coordinates(currentParentIndex, parentX, parentY, parentZ);

    int step = max(1, (int)(speed / max(VoxelSize, 1e-6)));
    uint selectionKey = AntOrderKey(particleIndex, 0xa511e9b3u);
    uint candidateCount = 0u;

    [unroll]
    for (int offsetX = -1; offsetX <= 1; offsetX++)
    {
        [unroll]
        for (int offsetY = -1; offsetY <= 1; offsetY++)
        {
            [unroll]
            for (int offsetZ = -1; offsetZ <= 1; offsetZ++)
            {
                if (offsetX == 0 && offsetY == 0 && offsetZ == 0) continue;
                int x = parentX + offsetX * step;
                int y = parentY + offsetY * step;
                int z = parentZ + offsetZ * step;
                if (x < 0 || x >= ResX || y < 0 || y >= ResY || z < 0 || z >= ResZ) continue;
                if (PlanarYZ != 0 && x != 0) continue;
                if (PlanarXZ != 0 && y != 0) continue;
                if (PlanarXY != 0 && z != 0) continue;

                int candidateIndex = FlatIndex(x, y, z);
                if (!IsValidVoxelIndex(candidateIndex) || (Wrap == 0 && IsBoundary(x, y, z))) continue;

                candidateCount++;
                if (Hash(selectionKey + candidateCount * 2246822519u) % candidateCount == 0u)
                {
                    recoveredIndex = candidateIndex;
                    recoveredPosition = float3(
                        (x + 0.5) * VoxelSize,
                        (y + 0.5) * VoxelSize,
                        (z + 0.5) * VoxelSize);
                }
            }
        }
    }

    if (recoveredIndex < 0) return false;
    recoveredPosition = ApplyPlanarPosition(recoveredPosition);
    return true;
}

// Ownership is rebuilt in two passes. Atomic minimum makes reset and boundary
// transition collisions independent of GPU thread scheduling: the lowest live
// particle slot is retained for each walkable voxel.
[numthreads(256, 1, 1)]
void ClaimParticleOwners(uint3 id : SV_DispatchThreadID)
{
    int particleIndex = (int)LinearIndex256(id);
    if (particleIndex >= ParticleCapacity || !IsParticleAlive(particleIndex)) return;

    int voxelIndex = VoxelIndexFromPosition(ParticlePosition[particleIndex].xyz);
    if (voxelIndex < 0) return;

    uint ignored;
    InterlockedMin(ParticleOwners[voxelIndex], (uint)particleIndex, ignored);
}

[numthreads(256, 1, 1)]
void CullParticleOwnerConflicts(uint3 id : SV_DispatchThreadID)
{
    int particleIndex = (int)LinearIndex256(id);
    if (particleIndex >= ParticleCapacity || !IsParticleAlive(particleIndex)) return;

    float4 position = ParticlePosition[particleIndex];
    int voxelIndex = VoxelIndexFromPosition(position.xyz);
    if (ParticleOwnsVoxel(particleIndex, voxelIndex))
    {
        float4 direction = ParticleDirection[particleIndex];
        direction.w = (float)voxelIndex;
        ParticleDirection[particleIndex] = direction;
        return;
    }

    position.w = -1.0;
    ParticlePosition[particleIndex] = position;

    float4 direction = ParticleDirection[particleIndex];
    direction.w = -1.0;
    ParticleDirection[particleIndex] = direction;

    float4 yAxis = ParticleYAxis[particleIndex];
    yAxis.w = -1.0;
    ParticleYAxis[particleIndex] = yAxis;
}

[numthreads(256, 1, 1)]
void ApplyBoundaryModeTransition(uint3 id : SV_DispatchThreadID)
{
    int particleIndex = (int)LinearIndex256(id);
    if (particleIndex >= ParticleCapacity) return;

    float4 posGroup = ParticlePosition[particleIndex];
    if (posGroup.w < -0.5) return;

    float4 dirParent = ParticleDirection[particleIndex];
    float4 yWrapped = ParticleYAxis[particleIndex];
    int currentParentIndex = (int)round(dirParent.w);
    float3 position = ApplyPlanarPosition(posGroup.xyz);
    float3 direction = NormalizeOr(ApplyPlanarMode(dirParent.xyz), float3(1, 0, 0));
    uint wrapped = 0;

    ApplyMovementBoundaries(position, direction, wrapped);
    position = ApplyPlanarPosition(position);

    int parentIndex = VoxelIndexFromPosition(position);
    if (parentIndex < 0)
    {
        // An invalid transition target leaves the complete previous state alone.
        return;
    }

    if (parentIndex != currentParentIndex &&
        !TryClaimParticleMove(particleIndex, currentParentIndex, parentIndex))
    {
        // Preserve the source voxel and position when a transition collides.
        // Only the next-step orientation changes, so no particle is culled and
        // the source owner cannot be stolen by the winning transition.
        uint boundarySeed = Hash(
            (uint)particleIndex +
            ((uint)Iteration * 747796405u) ^
            0xd1b54a35u);
        direction = NormalizeOr(ApplyPlanarMode(RandomUnitDirection(boundarySeed)), direction);
        float3 blockedYAxis = SafeYAxis(direction, yWrapped.xyz);
        ParticleDirection[particleIndex] = float4(direction, dirParent.w);
        ParticleYAxis[particleIndex] = float4(blockedYAxis, 0.0);
        return;
    }

    direction = NormalizeOr(ApplyPlanarMode(direction), float3(1, 0, 0));
    float3 yAxis = SafeYAxis(direction, yWrapped.xyz);
    ParticlePosition[particleIndex] = float4(position, posGroup.w);
    ParticleDirection[particleIndex] = float4(direction, (float)parentIndex);
    ParticleYAxis[particleIndex] = float4(yAxis, (float)wrapped);
}

void MoveParticlesAndDepositCore(uint3 id, bool antOnly)
{
    int particleIndex = (int)LinearIndex256(id);
    if (particleIndex >= ParticleCapacity) return;

    float4 posGroup = ParticlePosition[particleIndex];
    if (posGroup.w < -0.5) return;
    float4 dirParent = ParticleDirection[particleIndex];
    float4 yWrapped = ParticleYAxis[particleIndex];

    int groupIndex = (int)round(posGroup.w);
    if (groupIndex < 0 || groupIndex >= GroupCount) return;

    float3 position = posGroup.xyz;
    float3 x = NormalizeOr(dirParent.xyz, float3(1, 0, 0));
    float3 y = SafeYAxis(x, yWrapped.xyz);

    float4 group0 = GroupData0[groupIndex];
    float4 group1 = GroupData1[groupIndex];
    bool isAnt = antOnly || group1.y > 0.5;
    bool connectedSteering = !antOnly && !isAnt && group1.y < -0.5;
    uint particleAge = DepositFixed[ParticleAgeIndex(particleIndex)];
    bool foundFood = isAnt && DepositFixed[ParticleAntStateIndex(particleIndex)] != 0u;
    float4 homeState = isAnt ? ParticleHome[particleIndex] : float4(position, 0.0);
    float3 homePosition = homeState.xyz;
    bool antLaunchBoundaryHit = false;
    if (isAnt)
    {
        antLaunchBoundaryHit = DepositFixed[ParticleAntLaunchBoundaryIndex(particleIndex)] != 0u;
    }
    bool originalAntLaunchBoundaryHit = antLaunchBoundaryHit;

    int currentParentIndex = (int)round(dirParent.w);
    // A stored V3 parent remains usable for behavior and recovery while it is
    // active, even when maxDensity makes it non-walkable. Keep the fallback
    // strict so an actually missing parent is not resurrected from an obstacle.
    if (!IsActiveVoxelIndex(currentParentIndex))
    {
        currentParentIndex = VoxelIndexFromPosition(position);
    }

    float4 behavior = float4(1.0, 1.0, 1.0, 1.0);
    float4 vectorField = float4(0.0, 0.0, 0.0, 0.0);
    if (IsActiveVoxelIndex(currentParentIndex))
    {
        if (HasVoxelBehavior != 0)
        {
            behavior.x = ReadBehaviorChannel(SpeedOffset, currentParentIndex, SpeedDefault);
            behavior.y = ReadBehaviorChannel(SensorDistanceOffset, currentParentIndex, SensorDistanceDefault);
            behavior.z = ReadBehaviorChannel(SensorAngleOffset, currentParentIndex, SensorAngleDefault);
            behavior.w = ReadBehaviorChannel(RotationAngleOffset, currentParentIndex, RotationAngleDefault);
        }
        if (HasVoxelVectors != 0)
        {
            vectorField.xyz = HasVoxelVectorData != 0
                ? VoxelVectors[currentParentIndex]
                : float3(VoxelVectorDefaultX, VoxelVectorDefaultY, VoxelVectorDefaultZ);
            vectorField.w = HasVoxelVectorFrequencies != 0
                ? max(1, VoxelVectorFrequencies[currentParentIndex])
                : max(1, VoxelVectorDefaultFrequency);
        }
    }

    float speed = group0.x * behavior.x;
    float rawSensorDistance = group0.y;
    float sensorDistance = rawSensorDistance * behavior.y;
    float sensorAngle = group0.z * behavior.z;
    float sensorSin;
    float sensorCos;
    sincos(sensorAngle, sensorSin, sensorCos);
    float rotationAngle = group1.x * behavior.w;
    float rotationSin;
    float rotationCos;
    sincos(rotationAngle, rotationSin, rotationCos);
    float depositValue = group1.z;
    uint wanderFrequency = group1.w <= 0.0 ? 0u : max(1u, (uint)round(group1.w));
    if (connectedSteering)
    {
        wanderFrequency = 0u;
    }
    else if (DynamicPopulation != 0)
    {
        float groupPopulation = (float)ParticleCounts[GroupPopulationIndex(groupIndex)];
        if (isAnt)
        {
            // For ants group0.w carries the raw base-wander control. V3 derives
            // its interval from the current retained population every solution.
            float baseWander = saturate(group0.w);
            wanderFrequency = max(1u, (uint)floor(baseWander * groupPopulation / 40.0));
        }
        else
        {
            float wander = saturate(group0.w);
            wanderFrequency = wander <= 0.0
                ? 0u
                : max(1u, (uint)floor(pow(1.0 - wander, 3.0) * groupPopulation / 10.0));
        }
    }

    float3 homeOffset = position - homePosition;
    float homeDistance = length(homeOffset);
    float3 towardsHome = NormalizeOr(-homeOffset, -x);

    // V3 snapshots these axes before constructing the first three sensors, but
    // non-wrapped boundaries() mutates the particle plane after every sensor in
    // left/front/right order. Keep the sensor geometry snapshot separate from
    // the working axes used by the eventual steering force.
    float3 sensorPlaneX = x;
    float3 sensorPlaneY = y;
    float3 leftSensor = position + (sensorPlaneX * sensorCos - sensorPlaneY * sensorSin) * sensorDistance;
    float3 frontSensor = position + sensorPlaneX * sensorDistance;
    float3 rightSensor = position + (sensorPlaneX * sensorCos + sensorPlaneY * sensorSin) * sensorDistance;
    if (Wrap == 0)
    {
        ApplyNonWrappedSensorBoundaries(leftSensor, x, y);
        ApplyNonWrappedSensorBoundaries(frontSensor, x, y);
        ApplyNonWrappedSensorBoundaries(rightSensor, x, y);
    }

    float value0 = isAnt ? SampleAntField(leftSensor, foundFood, currentParentIndex, particleIndex, antOnly) : SampleDensity(leftSensor);
    float value1 = isAnt ? SampleAntField(frontSensor, foundFood, currentParentIndex, particleIndex, antOnly) : SampleDensity(frontSensor);
    float value2 = isAnt ? SampleAntField(rightSensor, foundFood, currentParentIndex, particleIndex, antOnly) : SampleDensity(rightSensor);
    float value3 = -1.0;
    float value4 = -1.0;

    if (Tridimensional != 0)
    {
        // V3 rotates each vertical sensor from the original X axis about the
        // working Y axis. The up-sensor boundary mutation therefore affects the
        // axis used to construct the down sensor.
        float3 upSensor = position + RotateAroundAxis(sensorPlaneX, y, sensorCos, sensorSin) * sensorDistance;
        if (Wrap == 0) ApplyNonWrappedSensorBoundaries(upSensor, x, y);
        value3 = isAnt
            ? SampleAntField(upSensor, foundFood, currentParentIndex, particleIndex, antOnly)
            : SampleDensity(upSensor);

        float3 downSensor = position + RotateAroundAxis(sensorPlaneX, y, sensorCos, -sensorSin) * sensorDistance;
        if (Wrap == 0) ApplyNonWrappedSensorBoundaries(downSensor, x, y);
        value4 = isAnt
            ? SampleAntField(downSensor, foundFood, currentParentIndex, particleIndex, antOnly)
            : SampleDensity(downSensor);
    }

    int bestIndex = connectedSteering
        ? ChooseConnectedSensor(value0, value1, value2, value3, value4, group0.w, particleIndex)
        : ChooseBestSensor(value0, value1, value2, value3, value4);
    float3 force = 0.0;
    if (bestIndex < 0)
    {
        // V3 adds no sensor force for this sentinel. In non-wrap mode it still
        // rotates the working plane deterministically by the raw group rotation
        // angle times the particle index; its negative-degree normalization is
        // equivalent to taking the absolute periodic angle.
        if (Wrap == 0)
        {
            float noSensorSin;
            float noSensorCos;
            sincos(abs(group1.x) * (float)particleIndex, noSensorSin, noSensorCos);
            float3 previousX = x;
            float3 previousY = y;
            x = NormalizeOr(previousX * noSensorCos + previousY * noSensorSin, previousX);
            y = SafeYAxis(x, previousY * noSensorCos - previousX * noSensorSin);
        }
    }
    else
    {
        force = RotateForce(bestIndex, x, y, rotationCos, rotationSin);
    }

    if (vectorField.w >= 1.0)
    {
        uint vectorFrequency = max(1u, (uint)round(vectorField.w));
        if (vectorFrequency == 1u || ((uint)Iteration % vectorFrequency) == ((uint)particleIndex % vectorFrequency))
        {
            force += ApplyPlanarMode(vectorField.xyz);
        }
    }

    if (isAnt)
    {
        uint antSteeringOrder = AntOrderKey(particleIndex, 0x9e3779b9u);
        if (particleAge < 15u)
        {
            float blend = particleAge / 15.0;
            float3 target = foundFood ? towardsHome : NormalizeOr(homeOffset, x);
            force += NormalizeOr(lerp(x, target, blend), x) * 2.0;
        }
        if (antSteeringOrder % 7u == 0u)
        {
            force += RandomPlanarVector(Hash(antSteeringOrder + (uint)Iteration));
        }
        if (!foundFood && !antLaunchBoundaryHit)
        {
            // Once the launch has ended at a boundary (or while carrying food),
            // V3's launch force cannot affect this step. Keep the expensive
            // distance/sqrt calculation inside that state gate so the steady ant
            // path does not recompute an unused duration forever.
            uint launchDuration = AntLaunchDuration(homePosition, group0.x);
            if (particleAge < launchDuration)
            {
                float3 launchVector = NormalizeOr(homeOffset, x);
                float launchProgress = launchDuration > 0u
                    ? saturate((float)particleAge / (float)launchDuration)
                    : 1.0;
                float launchFade = 0.5 * (1.0 + cos(3.14159265359 * launchProgress));
                float outwardStrength = (7.0 + AntLaunchVariation(homeState.w, 17.17) * 2.0) * launchFade;
                float lateralStrength = outwardStrength * (0.55 + AntLaunchVariation(homeState.w, 41.73) * 0.20);
                force += launchVector * outwardStrength;
                force += AntLaunchWaveVector(particleIndex, particleAge, homeState.w) * lateralStrength;
            }
        }
        if (foundFood && antSteeringOrder % wanderFrequency == 0u)
        {
            force += towardsHome;
        }
        if (!foundFood && particleAge > 100u)
        {
            force += towardsHome * (0.01 * particleAge / 100.0);
        }
        if (homeDistance <= rawSensorDistance * 2.0 && particleAge > 30u)
        {
            x = towardsHome;
            y = SafeYAxis(x, y);
            force += towardsHome;
        }
    }

    float3 steeringDirection = NormalizeOr(force, x);
    float3 moveDirection = NormalizeOr(steeringDirection + x * 0.2, x);
    uint movementSeed = Hash((uint)particleIndex + (uint)Iteration * 2891336453u);
    if (!isAnt && !connectedSteering && wanderFrequency > 0u && movementSeed % wanderFrequency == 0u)
    {
        moveDirection = NormalizeOr(moveDirection + RandomPlanarVector(movementSeed) * 1.5, moveDirection);
    }

    moveDirection = NormalizeOr(ApplyPlanarMode(moveDirection), x);
    float3 nextPosition = position + moveDirection * speed;
    nextPosition = CenterPlanarMovePosition(nextPosition);

    if (isAnt && !antLaunchBoundaryHit && AntMoveTouchesBoundary(nextPosition))
    {
        antLaunchBoundaryHit = true;
    }

    uint wrapped = 0;
    ApplyMovementBoundaries(nextPosition, moveDirection, wrapped);
    nextPosition = CenterPlanarMovePosition(nextPosition);

    int parentIndex = VoxelIndexFromPosition(nextPosition);
    bool hasWalkableTarget = parentIndex >= 0;
    if (!hasWalkableTarget)
    {
        if (isAnt)
        {
            antLaunchBoundaryHit = true;
        }

        int recoveredIndex;
        float3 recoveredPosition;
        if (TryRecoverWalkableStep(currentParentIndex, particleIndex, speed, recoveredIndex, recoveredPosition))
        {
            parentIndex = recoveredIndex;
            hasWalkableTarget = true;
            moveDirection = NormalizeOr(ApplyPlanarMode(recoveredPosition - position), -moveDirection);
            nextPosition = recoveredPosition;
        }
        else
        {
            parentIndex = currentParentIndex;
            if (!IsActiveVoxelIndex(parentIndex)) parentIndex = -1;
            nextPosition = position;
            moveDirection = NormalizeOr(ApplyPlanarMode(-moveDirection), x);
        }
    }

    bool moveAccepted = hasWalkableTarget &&
                        TryClaimParticleMove(particleIndex, currentParentIndex, parentIndex);
    bool occupiedTargetRejected = hasWalkableTarget && !moveAccepted;
    if (occupiedTargetRejected)
    {
        // The occupied destination never displaces the current owner. It stays
        // in place, emits no trail, and receives a fresh orientation for the next
        // step, matching the particle/gel transition in the reference model.
        parentIndex = currentParentIndex;
        nextPosition = position;
        wrapped = 0u;
        antLaunchBoundaryHit = originalAntLaunchBoundaryHit;
        moveDirection = NormalizeOr(
            ApplyPlanarMode(RandomUnitDirection(movementSeed ^ 0xd1b54a35u)),
            x);
    }

    bool wasHighDeposit = HighDepositOffset < 0
        || DepositFixed[HighDepositIndex(particleIndex)] != 0u;
    bool enteredEmptyVoxel = moveAccepted && parentIndex != currentParentIndex;
    if (HighDepositOffset >= 0)
    {
        DepositFixed[HighDepositIndex(particleIndex)] = enteredEmptyVoxel ? 1u : 0u;
    }

    if (enteredEmptyVoxel && CanDepositAtVoxel(parentIndex, rawSensorDistance))
    {
        float ageT = saturate(particleAge / 99.0);
        float antMultiplier = foundFood ? lerp(1.0, 0.3, ageT) : lerp(1.0, 0.2, ageT);
        float antTrailFactor = !foundFood && AntFoodPheromone[parentIndex] > 0.0 ? 1.1 : 0.9;
        float slimeScale = wasHighDeposit ? 1.0 : 0.25;
        float effectiveDeposit = isAnt
            ? depositValue * antMultiplier * (foundFood ? 1.0 : antTrailFactor)
            : depositValue * slimeScale;
        uint fixedDeposit = (uint)round(max(0.0, effectiveDeposit * DepositScale));
        if (fixedDeposit > 0u)
        {
            if (isAnt)
            {
                InterlockedAdd(DepositFixed[foundFood ? AntFoodDepositIndex(parentIndex) : AntBaseDepositIndex(parentIndex)], fixedDeposit);
            }
            else
            {
                InterlockedAdd(DepositFixed[SlimeDepositIndex(parentIndex)], fixedDeposit);
            }
        }
    }

    uint nextParticleAge = particleAge + 1u;
    if (isAnt && Iteration > 1)
    {
        if (!foundFood && TryConsumeFood(parentIndex))
        {
            foundFood = true;
            nextParticleAge = 1u;
        }

        // V3 treats every visit inside one movement step as a nest visit. Repeating
        // the age reset keeps the outward departure force active until the ant exits.
        float nextHomeDistance = length(nextPosition - homePosition);
        if (nextHomeDistance < group0.x)
        {
            foundFood = false;
            nextParticleAge = 1u;
            antLaunchBoundaryHit = false;
        }
    }

    x = NormalizeOr(moveDirection, x);
    y = SafeYAxis(x, y);

    // Keep the lightweight CPU preview self-contained. The integer portion is
    // still the group index consumed by every solver path; an ant carrying food
    // uses a small fractional tag that round(groupTag) safely ignores.
    float previewGroupTag = (float)groupIndex + (isAnt && foundFood ? 0.25 : 0.0);
    ParticlePosition[particleIndex] = float4(nextPosition, previewGroupTag);
    ParticleDirection[particleIndex] = float4(x, (float)parentIndex);
    ParticleYAxis[particleIndex] = float4(y, (float)wrapped);
    DepositFixed[ParticleAgeIndex(particleIndex)] = nextParticleAge;
    if (isAnt)
    {
        DepositFixed[ParticleAntStateIndex(particleIndex)] = foundFood ? 1u : 0u;
        DepositFixed[ParticleAntLaunchBoundaryIndex(particleIndex)] = antLaunchBoundaryHit ? 1u : 0u;
    }
}

[numthreads(256, 1, 1)]
void MoveParticlesAndDeposit(uint3 id : SV_DispatchThreadID)
{
    MoveParticlesAndDepositCore(id, false);
}

// Ant-only engines compile the same movement body with the population kind as
// a constant. D3DCompiler can then remove the disconnected slime/connected
// branches and their register pressure without maintaining a second algorithm.
[numthreads(256, 1, 1)]
void MoveAntParticlesAndDeposit(uint3 id : SV_DispatchThreadID)
{
    MoveParticlesAndDepositCore(id, true);
}

// Mirrors V3 projectFoodSources. Runs before diffusion so the injected value
// is diffused and decayed by the normal slime-field update in the same step.
// The source map is immutable, so every reset re-establishes the same strength.
[numthreads(256, 1, 1)]
void ProjectFoodSources(uint3 id : SV_DispatchThreadID)
{
    int index = (int)LinearIndex256(id);
    if (index >= VoxelCount) return;
    if (!IsActiveVoxelIndex(index)) return;

    float foodValue = FoodSourceAt(index);
    if (foodValue <= 0.0) return;

    // V3 projects into every active voxel, including max-density-zero obstacle
    // voxels, and applies density limits only if a diffusion pass follows.
    Source[index] += foodValue;
}

void ApplyDepositsAtVoxel(int index)
{
    uint fixedDeposit = HasSlimeParticles != 0 ? DepositFixed[SlimeDepositIndex(index)] : 0u;
    if (!IsValidVoxelIndex(index))
    {
        if (HasSlimeParticles != 0) DepositFixed[SlimeDepositIndex(index)] = 0u;
        if (HasAntParticles != 0)
        {
            DepositFixed[AntFoodDepositIndex(index)] = 0u;
            DepositFixed[AntBaseDepositIndex(index)] = 0u;
        }
        return;
    }

    if (fixedDeposit > 0u)
    {
        Source[index] += fixedDeposit / DepositScale;
    }
    if (HasSlimeParticles != 0) DepositFixed[SlimeDepositIndex(index)] = 0u;

    if (HasAntParticles != 0)
    {
        uint foodDeposit = DepositFixed[AntFoodDepositIndex(index)];
        uint baseDeposit = DepositFixed[AntBaseDepositIndex(index)];
        // V3 applies upper/minimum density constraints only in diffusion. With
        // a zero diffusion rate, deposits remain raw until the decay stage.
        if (foodDeposit > 0u) Destination[index] += foodDeposit / DepositScale;
        if (baseDeposit > 0u) AntBaseDestination[index] += baseDeposit / DepositScale;
        DepositFixed[AntFoodDepositIndex(index)] = 0u;
        DepositFixed[AntBaseDepositIndex(index)] = 0u;
    }
}

[numthreads(256, 1, 1)]
void ApplyDeposits(uint3 id : SV_DispatchThreadID)
{
    int index = (int)LinearIndex256(id);
    if (PreviewPadding0 != 0)
    {
        // Production path: visit every voxel in contiguous order. Despite the
        // larger dispatch, this is faster for the high3d workload than scattered
        // particle-addressed writes.
        if (index >= VoxelCount) return;
        ApplyDepositsAtVoxel(index);
        return;
    }

    // Experimental validation path: every pending deposit belongs to the unique live
    // particle that successfully claimed this stored parent in the preceding
    // movement dispatch. Keeping this as a separate dispatch preserves the
    // rule that movement sensing cannot observe same-step deposits.
    if (index >= ParticleCapacity || !IsParticleAlive(index)) return;
    int parentIndex = (int)round(ParticleDirection[index].w);
    if (parentIndex < 0 || parentIndex >= VoxelCount) return;
    ApplyDepositsAtVoxel(parentIndex);
}

[numthreads(256, 1, 1)]
void ClearParticleCounts(uint3 id : SV_DispatchThreadID)
{
    int index = (int)LinearIndex256(id);
    if (PreviewPadding0 != 0 && index < VoxelCount)
    {
        // Exact legacy validation path. Production retains persistent binary
        // occupancy and clears only the aggregate population counters below.
        ParticleCounts[index] = 0u;
    }
    if (index < GroupCount)
    {
        ParticleCounts[GroupPopulationIndex(index)] = 0u;
    }
    if (index == 0)
    {
        ParticleCounts[ActivePopulationIndex()] = 0u;
        ParticleCounts[FreePopulationIndex()] = 0u;
    }
}

[numthreads(256, 1, 1)]
void CountParticles(uint3 id : SV_DispatchThreadID)
{
    int index = (int)LinearIndex256(id);
    if (index >= ParticleCapacity) return;

    if (!IsParticleAlive(index))
    {
        uint freeIndex;
        InterlockedAdd(ParticleCounts[FreePopulationIndex()], 1u, freeIndex);
        if (freeIndex < (uint)ParticleCapacity)
        {
            DepositFixed[FreeSlotIndex((int)freeIndex)] = (uint)index;
        }
        return;
    }

    uint ignoredActive;
    InterlockedAdd(ParticleCounts[ActivePopulationIndex()], 1u, ignoredActive);

    int parentIndex = (int)round(ParticleDirection[index].w);
    if (ParticleOwnsVoxel(index, parentIndex))
    {
        // Ownership guarantees no competing writer can represent another live
        // particle in this voxel. The count remains a binary occupancy field.
        ParticleCounts[parentIndex] = 1u;
    }

    int groupIndex = (int)round(ParticlePosition[index].w);
    if (groupIndex >= 0 && groupIndex < GroupCount)
    {
        InterlockedAdd(ParticleCounts[GroupPopulationIndex(groupIndex)], 1u);
    }
}

[numthreads(256, 1, 1)]
void AdvanceParticleAges(uint3 id : SV_DispatchThreadID)
{
    int index = (int)LinearIndex256(id);
    if (index >= ParticleCapacity || !IsParticleAlive(index)) return;

    uint ignored;
    InterlockedAdd(DepositFixed[ParticleAgeIndex(index)], 1u, ignored);
}

[numthreads(256, 1, 1)]
void SeedNeighbourCounts(uint3 id : SV_DispatchThreadID)
{
    int index = (int)LinearIndex256(id);
    if (index >= VoxelCount) return;
    Destination[index] = (float)ParticleCounts[index];
}

int NeighbourLineCountForAxis(int axis)
{
    if (axis == 0) return ResY * ResZ;
    if (axis == 1) return ResX * ResZ;
    return ResX * ResY;
}

int NeighbourLineLength(int axis)
{
    if (axis == 0) return ResX;
    if (axis == 1) return ResY;
    return ResZ;
}

int NeighbourLineVoxelIndex(int axis, int lineIndex, int coordinate)
{
    if (axis == 0)
    {
        int y = lineIndex / ResZ;
        int z = lineIndex - y * ResZ;
        return FlatIndex(coordinate, y, z);
    }

    if (axis == 1)
    {
        int x = lineIndex / ResZ;
        int z = lineIndex - x * ResZ;
        return FlatIndex(x, coordinate, z);
    }

    int x = lineIndex / ResY;
    int y = lineIndex - x * ResY;
    return FlatIndex(x, y, coordinate);
}

[numthreads(64, 1, 1)]
void SumNeighbourAxis(uint3 id : SV_DispatchThreadID)
{
    int lineIndex = (int)LinearIndex64(id);
    int lineCount = NeighbourLineCountForAxis(Axis);
    if (lineIndex >= lineCount) return;

    int length = NeighbourLineLength(Axis);
    int radius = max(Range, 0);
    float sum = 0.0;
    int initialEnd = min(radius, length - 1);
    for (int coordinate = 0; coordinate <= initialEnd; coordinate++)
    {
        sum += Source[NeighbourLineVoxelIndex(Axis, lineIndex, coordinate)];
    }

    for (int coordinate = 0; coordinate < length; coordinate++)
    {
        Destination[NeighbourLineVoxelIndex(Axis, lineIndex, coordinate)] = sum;

        int removeCoordinate = coordinate - radius;
        if (removeCoordinate >= 0)
        {
            sum -= Source[NeighbourLineVoxelIndex(Axis, lineIndex, removeCoordinate)];
        }

        int addCoordinate = coordinate + radius + 1;
        if (addCoordinate < length)
        {
            sum += Source[NeighbourLineVoxelIndex(Axis, lineIndex, addCoordinate)];
        }
    }
}

// The active population only decreases during a death dispatch. Claim with a
// compare-exchange loop so failed contenders never publish a transient value
// below the configured minimum (and can therefore never underflow it).
bool TryClaimDeath()
{
    uint minimum = (uint)max(MinimumPopulation, 0);
    uint current;
    InterlockedAdd(ParticleCounts[ActivePopulationIndex()], 0u, current);
    while (current > minimum)
    {
        uint previous;
        InterlockedCompareExchange(
            ParticleCounts[ActivePopulationIndex()],
            current,
            current - 1u,
            previous);
        if (previous == current) return true;
        current = previous;
    }
    return false;
}

[numthreads(256, 1, 1)]
void ApplyParticleDeath(uint3 id : SV_DispatchThreadID)
{
    int particleIndex = (int)LinearIndex256(id);
    if (particleIndex >= ParticleCapacity || !IsParticleAlive(particleIndex)) return;

    int parentIndex = (int)round(ParticleDirection[particleIndex].w);
    int neighbourCount = 0;
    if (DeathEnabled == 2)
    {
        neighbourCount = (int)DepositFixed[ParticleDeathNeighbourIndex(particleIndex)];
    }
    else if (DeathEnabled == -2)
    {
        // A negative V3 range participates in a positive shared scan but no
        // offset can satisfy abs(offset) <= range, so the observable field is 0.
        DepositFixed[ParticleDeathNeighbourIndex(particleIndex)] = 0u;
    }
    else if (DeathEnabled != 0)
    {
        if (parentIndex >= 0 && parentIndex < VoxelCount)
        {
            // Excludes the particle itself, matching V3, which subtracts one after
            // summing the box (Solver.cs particleCheckNeighbourCount).
            neighbourCount = max(0, (int)round(Source[parentIndex]) - 1);
        }
        DepositFixed[ParticleDeathNeighbourIndex(particleIndex)] = (uint)neighbourCount;
    }

    uint age = DepositFixed[ParticleAgeIndex(particleIndex)];
    bool outsideNeighbourRange = neighbourCount < DeathMinimumNeighbours ||
                                 neighbourCount > DeathMaximumNeighbours;
    bool oldEnoughToDie = age >= (uint)max(DeathMinimumAge, 0);
    bool shouldDie = DeathEnabled > 0 && oldEnoughToDie && outsideNeighbourRange;

    // C# dispatches this kernel once for the neighbour rule and, later, once for
    // random death. The random-only pass runs after normal division, so normal
    // newborns are deliberately eligible here, matching V3.
    if (!shouldDie && RandomDeathProbability > 0.0 && RandomPopulationDue())
    {
        uint deathSeed = Hash((uint)particleIndex ^ ((uint)Iteration * 2654435761u) ^ 0x9E3779B9u);
        shouldDie = UnitFromHash(deathSeed) < RandomDeathProbability;
    }

    // Separate statements are load-bearing. HLSL does not guarantee short-circuit
    // evaluation, so a combined !shouldDie || !TryClaimDeath() condition let
    // the compiler call TryClaimDeath for every particle, and that call
    // decrements the population counter as a side effect, draining the
    // population regardless of any rule.
    if (!shouldDie) return;
    if (!TryClaimDeath()) return;

    float4 position = ParticlePosition[particleIndex];
    int groupIndex = clamp((int)round(position.w), 0, max(GroupCount - 1, 0));
    ReleaseParticleVoxel(particleIndex, parentIndex);
    position.w = -1.0;
    ParticlePosition[particleIndex] = position;

    float4 direction = ParticleDirection[particleIndex];
    direction.w = -1.0;
    ParticleDirection[particleIndex] = direction;

    float4 yAxis = ParticleYAxis[particleIndex];
    yAxis.w = -1.0;
    ParticleYAxis[particleIndex] = yAxis;

    // Release the voxel immediately so later population stages in this same
    // solution can claim it without ever producing duplicate occupancy.
    uint ignoredGroupCount;
    InterlockedAdd(ParticleCounts[GroupPopulationIndex(groupIndex)], 0xffffffffu, ignoredGroupCount);

    uint freeIndex;
    InterlockedAdd(ParticleCounts[FreePopulationIndex()], 1u, freeIndex);
    if (freeIndex < (uint)ParticleCapacity)
    {
        DepositFixed[FreeSlotIndex((int)freeIndex)] = (uint)particleIndex;
    }
}

bool TryClaimBirth(out uint particleSlot)
{
    particleSlot = 0u;
    uint maximum = (uint)min(max(MaximumPopulation, 0), ParticleCapacity);
    uint ignored;
    uint currentActive;
    InterlockedAdd(ParticleCounts[ActivePopulationIndex()], 0u, currentActive);
    bool activeReserved = false;
    while (currentActive < maximum)
    {
        uint previousActive;
        InterlockedCompareExchange(
            ParticleCounts[ActivePopulationIndex()],
            currentActive,
            currentActive + 1u,
            previousActive);
        if (previousActive == currentActive)
        {
            activeReserved = true;
            break;
        }
        currentActive = previousActive;
    }
    if (!activeReserved) return false;

    // Free slots only decrease during a division dispatch. Pop with CAS rather
    // than decrement-and-undo: the latter exposes UINT_MAX while an empty-stack
    // loser repairs its decrement, and another contender can mistake that
    // transient underflow for a valid stack depth.
    uint currentFree;
    InterlockedAdd(ParticleCounts[FreePopulationIndex()], 0u, currentFree);
    while (currentFree > 0u)
    {
        uint previousFree;
        InterlockedCompareExchange(
            ParticleCounts[FreePopulationIndex()],
            currentFree,
            currentFree - 1u,
            previousFree);
        if (previousFree == currentFree)
        {
            particleSlot = DepositFixed[FreeSlotIndex((int)currentFree - 1)];
            if (particleSlot >= (uint)ParticleCapacity)
            {
                InterlockedAdd(ParticleCounts[FreePopulationIndex()], 1u, ignored);
                InterlockedAdd(ParticleCounts[ActivePopulationIndex()], 0xffffffffu, ignored);
                return false;
            }
            return true;
        }
        currentFree = previousFree;
    }

    InterlockedAdd(ParticleCounts[ActivePopulationIndex()], 0xffffffffu, ignored);
    return false;
}

uint BirthReservationToken(int particleIndex)
{
    return 0x80000000u | (uint)particleIndex;
}

bool TryReserveBirthVoxel(
    int parentVoxelIndex,
    int particleIndex,
    out int childVoxelIndex,
    out uint reservationToken)
{
    childVoxelIndex = -1;
    reservationToken = BirthReservationToken(particleIndex);
    if (!IsValidVoxelIndex(parentVoxelIndex)) return false;

    int parentX;
    int parentY;
    int parentZ;
    Coordinates(parentVoxelIndex, parentX, parentY, parentZ);

    uint start = Hash((uint)particleIndex ^ ((uint)Iteration * 2246822519u) ^ 0x51ed270bu) % 27u;
    [unroll]
    for (uint attempt = 0u; attempt < 27u; attempt++)
    {
        uint ordinal = (start + attempt) % 27u;
        int offsetX = (int)(ordinal / 9u) - 1;
        int offsetY = (int)((ordinal / 3u) % 3u) - 1;
        int offsetZ = (int)(ordinal % 3u) - 1;
        if (offsetX == 0 && offsetY == 0 && offsetZ == 0) continue;
        if (PlanarYZ != 0 && offsetX != 0) continue;
        if (PlanarXZ != 0 && offsetY != 0) continue;
        if (PlanarXY != 0 && offsetZ != 0) continue;

        int x = parentX + offsetX;
        int y = parentY + offsetY;
        int z = parentZ + offsetZ;
        if (Wrap != 0)
        {
            x = WrapIndex(x, ResX);
            y = WrapIndex(y, ResY);
            z = WrapIndex(z, ResZ);
        }
        else if (x < 0 || x >= ResX || y < 0 || y >= ResY || z < 0 || z >= ResZ)
        {
            continue;
        }
        if (Wrap == 0 && IsBoundary(x, y, z)) continue;

        int candidate = FlatIndex(x, y, z);
        if (!IsValidVoxelIndex(candidate)) continue;

        uint previousOwner;
        InterlockedCompareExchange(
            ParticleOwners[candidate],
            EmptyParticleOwner(),
            reservationToken,
            previousOwner);
        if (previousOwner == EmptyParticleOwner())
        {
            childVoxelIndex = candidate;
            return true;
        }
    }

    return false;
}

void ReleaseBirthReservation(int voxelIndex, uint reservationToken)
{
    if (voxelIndex < 0 || voxelIndex >= VoxelCount) return;
    uint ignored;
    InterlockedCompareExchange(
        ParticleOwners[voxelIndex],
        reservationToken,
        EmptyParticleOwner(),
        ignored);
}

[numthreads(256, 1, 1)]
void ApplyParticleDivision(uint3 id : SV_DispatchThreadID)
{
    int particleIndex = (int)LinearIndex256(id);
    if (particleIndex >= ParticleCapacity || !IsParticleAlive(particleIndex)) return;
    float birthMarker = ParticleYAxis[particleIndex].w;
    bool publishOnly = DivisionEnabled < 0;
    bool randomOnlyPass = DivisionEnabled == 0 &&
                          RandomDivisionProbability > 0.0 &&
                          RandomPopulationDue();
    // -2 marks a child born in the completed normal pass. V3 includes that
    // child in the following random pass. -1 marks a child born in the current
    // pass and prevents scheduling-dependent recursive division.
    if (!publishOnly && birthMarker < -0.5 && !(randomOnlyPass && birthMarker < -1.5)) return;
    if (!publishOnly && randomOnlyPass && birthMarker < -1.5)
    {
        float4 normalNewbornYAxis = ParticleYAxis[particleIndex];
        normalNewbornYAxis.w = 0.0;
        ParticleYAxis[particleIndex] = normalNewbornYAxis;
    }

    int parentIndex = (int)round(ParticleDirection[particleIndex].w);
    if (!IsValidVoxelIndex(parentIndex)) return;

    int neighbourCount = 0;
    if (DivisionEnabled == 2)
    {
        neighbourCount = (int)DepositFixed[ParticleDivisionNeighbourIndex(particleIndex)];
    }
    else if (DivisionEnabled == -2)
    {
        // See the matching death path: a negative range publishes zero when
        // the other rule keeps V3's shared neighbour scan active.
        DepositFixed[ParticleDivisionNeighbourIndex(particleIndex)] = 0u;
    }
    else if (DivisionEnabled != 0)
    {
        // Excludes the particle itself, matching V3.
        neighbourCount = max(0, (int)round(Source[parentIndex]) - 1);
        DepositFixed[ParticleDivisionNeighbourIndex(particleIndex)] = (uint)neighbourCount;
    }
    uint age = DepositFixed[ParticleAgeIndex(particleIndex)];
    bool neighbourEligible = DivisionEnabled > 0 &&
                             age >= (uint)max(DivisionMinimumAge, 0) &&
                             neighbourCount >= DivisionMinimumNeighbours &&
                             neighbourCount <= DivisionMaximumNeighbours;

    bool divide = false;
    bool randomDivide = false;
    if (neighbourEligible)
    {
        uint selectionSeed = Hash((uint)particleIndex + (uint)Iteration * 3266489917u);
        divide = (selectionSeed & 1u) == 0u;
    }

    // Random division is independent of the neighbour rule, matching V3
    // applyRandomParticleDivision. TryClaimBirth enforces the maximum.
    if (!divide && RandomDivisionProbability > 0.0 && RandomPopulationDue())
    {
        uint divisionSeed = Hash((uint)particleIndex ^ ((uint)Iteration * 40503u) ^ 0x85EBCA6Bu);
        randomDivide = UnitFromHash(divisionSeed) < RandomDivisionProbability;
        divide = randomDivide;
    }

    if (!divide) return;

    int childVoxelIndex;
    uint birthReservation;
    if (!TryReserveBirthVoxel(
        parentIndex,
        particleIndex,
        childVoxelIndex,
        birthReservation)) return;

    uint childSlot;
    if (!TryClaimBirth(childSlot))
    {
        ReleaseBirthReservation(childVoxelIndex, birthReservation);
        return;
    }

    // This thread is the only writer allowed to replace its reservation token.
    // Publishing the child slot before making the slot live keeps the target
    // unavailable to every other movement/division thread throughout creation.
    ParticleOwners[childVoxelIndex] = childSlot;

    float4 parentPosition = ParticlePosition[particleIndex];
    float4 parentDirection = ParticleDirection[particleIndex];
    float4 parentYAxis = ParticleYAxis[particleIndex];
    int groupIndex = clamp((int)round(parentPosition.w), 0, max(GroupCount - 1, 0));
    float3 x = NormalizeOr(parentDirection.xyz, float3(1, 0, 0));
    float3 y = SafeYAxis(x, parentYAxis.xyz);
    float3 parentX = x;
    float3 childX = x;
    float3 childY = y;
    if (randomDivide)
    {
        uint angleSeed = Hash((uint)particleIndex ^ ((uint)Iteration * 2246822519u) ^ 0x27d4eb2du);
        float childAngle = Hash01(angleSeed) * 6.28318530718;
        float childSin;
        float childCos;
        sincos(childAngle, childSin, childCos);
        childX = NormalizeOr(x * childCos + y * childSin, x);
        childY = NormalizeOr(-x * childSin + y * childCos, y);
    }
    else
    {
        float splitAngle = GroupData1[groupIndex].x * 0.25;
        float splitSin;
        float splitCos;
        sincos(splitAngle, splitSin, splitCos);
        parentX = NormalizeOr(RotateForce(2, x, y, splitCos, splitSin), x);
        childX = NormalizeOr(RotateForce(0, x, y, splitCos, splitSin), x);
        childY = SafeYAxis(childX, y);
    }

    ParticleDirection[particleIndex] = float4(parentX, parentDirection.w);
    ParticleYAxis[particleIndex] = float4(SafeYAxis(parentX, y), parentYAxis.w);

    uint childAntState = randomDivide
        ? DepositFixed[ParticleAntStateIndex(particleIndex)]
        : 0u;
    float childPreviewGroupTag = (float)groupIndex
        + (GroupData1[groupIndex].y > 0.5 && childAntState != 0u ? 0.25 : 0.0);
    float3 childPosition = VoxelCenter(childVoxelIndex);
    ParticleDirection[childSlot] = float4(childX, (float)childVoxelIndex);
    ParticleYAxis[childSlot] = float4(childY, randomDivide ? -1.0 : -2.0);
    ParticleHome[childSlot] = randomDivide ? ParticleHome[particleIndex] : 0.0;
    DepositFixed[ParticleAgeIndex((int)childSlot)] = randomDivide ? age : 0u;
    DepositFixed[ParticleDeathNeighbourIndex((int)childSlot)] = 0u;
    DepositFixed[ParticleDivisionNeighbourIndex((int)childSlot)] = 0u;
    DepositFixed[ParticleGenerationIndex((int)childSlot)] = DepositFixed[ParticleGenerationIndex((int)childSlot)] + 1u;
    DepositFixed[ParticleAntStateIndex((int)childSlot)] = childAntState;
    DepositFixed[HighDepositIndex((int)childSlot)] = 0u;
    if (AntLaunchBoundaryOffset >= 0)
    {
        DepositFixed[ParticleAntLaunchBoundaryIndex((int)childSlot)] = randomDivide
            ? DepositFixed[ParticleAntLaunchBoundaryIndex(particleIndex)]
            : 0u;
        DepositFixed[ParticleAntHomeYAxisXIndex((int)childSlot)] = randomDivide
            ? DepositFixed[ParticleAntHomeYAxisXIndex(particleIndex)]
            : 0u;
        DepositFixed[ParticleAntHomeYAxisYIndex((int)childSlot)] = randomDivide
            ? DepositFixed[ParticleAntHomeYAxisYIndex(particleIndex)]
            : 0u;
        DepositFixed[ParticleAntHomeYAxisZIndex((int)childSlot)] = randomDivide
            ? DepositFixed[ParticleAntHomeYAxisZIndex(particleIndex)]
            : 0u;
        DepositFixed[ParticleAntHomeXAxisXIndex((int)childSlot)] = randomDivide
            ? DepositFixed[ParticleAntHomeXAxisXIndex(particleIndex)]
            : 0u;
        DepositFixed[ParticleAntHomeXAxisYIndex((int)childSlot)] = randomDivide
            ? DepositFixed[ParticleAntHomeXAxisYIndex(particleIndex)]
            : 0u;
        DepositFixed[ParticleAntHomeXAxisZIndex((int)childSlot)] = randomDivide
            ? DepositFixed[ParticleAntHomeXAxisZIndex(particleIndex)]
            : 0u;
    }

    ParticleCounts[childVoxelIndex] = 1u;
    uint ignoredVoxelCount;
    if (!randomDivide)
    {
        // V3's normal-division parent lookup advances the parent age before
        // the subsequent random-division pass.
        InterlockedAdd(DepositFixed[ParticleAgeIndex(particleIndex)], 1u, ignoredVoxelCount);
    }
    uint ignoredGroupCount;
    InterlockedAdd(ParticleCounts[GroupPopulationIndex(groupIndex)], 1u, ignoredGroupCount);

    // Position.w is the slot's live flag. Publish it only after every other
    // child field (especially the negative birth marker) is globally visible,
    // so the child slot's own thread cannot recursively divide partial state.
    DeviceMemoryBarrier();
    ParticlePosition[childSlot] = float4(childPosition, childPreviewGroupTag);
}

float ClampPassDensity(float value, int index, int x, int y, int z)
{
    // V3 advances every authored/active target. A max-density obstacle is not a
    // valid neighbour or particle parent, but its own retained contribution is
    // still diffused and then clamped to its authored maximum.
    if (!IsActiveVoxelIndex(index)) return 0.0;
    if (value > 1.0) value = 1.0;
    value = ClampDensityLimits(value, index);

    if (Wrap == 0 && IsBoundary(x, y, z)) value = 0.0;

    return value;
}

float ApplyDecayToValue(float value, int index, int x, int y, int z)
{
    if (!IsActiveVoxelIndex(index)) return 0.0;

    // V3's post-diffusion parent check clears scalar density under a particle
    // that could not escape an active max-density obstacle. CountParticles has
    // already published those active blocked parents for this frame.
    if (FieldMode == 0 && !IsValidVoxelIndex(index) && ParticleCounts[index] > 0u)
    {
        return 0.0;
    }

    // V3 clears density and ant-food pheromone on non-wrapped outer faces, but
    // ant-base pheromone is only decayed there. Preserve that asymmetry.
    if (Wrap == 0 && IsBoundary(x, y, z) && FieldMode != 2)
    {
        return 0.0;
    }

    return max(value - Decay, 0.0);
}

float FinalizeDiffusionValue(float value, int index, int x, int y, int z)
{
    value = ClampPassDensity(value, index, x, y, z);
    if (ApplyScalarDecayAfterDiffusion != 0 && FieldMode == 0)
    {
        value = ApplyDecayToValue(value, index, x, y, z);
    }
    return value;
}

int ResolveTiledDiffusionAxis(int coordinate, int count, out bool include)
{
    if (Wrap != 0)
    {
        include = count > 0;
        return include ? WrapIndex(coordinate, count) : 0;
    }

    include = coordinate >= 0 && coordinate < count;
    return coordinate;
}

float StoreTiledDiffusionSample(int slot, bool include, int x, int y, int z)
{
    if (!include || x < 0 || x >= ResX || y < 0 || y >= ResY || z < 0 || z >= ResZ)
    {
        TiledWeightedDensity[slot] = 0.0;
        return 0.0;
    }

    int index = FlatIndex(x, y, z);
    float rawDensity = Source[index];
    TiledWeightedDensity[slot] = IsValidVoxelIndex(index) ? rawDensity : 0.0;
    return rawDensity;
}

void LoadTiledDiffusionWeight(uint3 groupThreadId)
{
    int threadIndex = (int)(groupThreadId.y * 16u + groupThreadId.x);
    int weightCount = Range * 2 + 1;
    if (threadIndex < weightCount)
    {
        TiledDiffusionWeights[threadIndex] = Weights[threadIndex];
    }
}

[numthreads(256, 1, 1)]
void DiffuseAxis(uint3 id : SV_DispatchThreadID)
{
    int index = (int)LinearIndex256(id);
    if (index >= VoxelCount) return;

    int x;
    int y;
    int z;
    Coordinates(index, x, y, z);

    float weighted = 0.0;
    for (int offset = -Range; offset <= Range; offset++)
    {
        int sx = x;
        int sy = y;
        int sz = z;
        bool include = true;

        if (Axis == 0)
        {
            sx = x + offset;
            if (Wrap != 0) sx = WrapIndex(sx, ResX);
            else include = sx >= 0 && sx < ResX;
        }
        else if (Axis == 1)
        {
            sy = y + offset;
            if (Wrap != 0) sy = WrapIndex(sy, ResY);
            else include = sy >= 0 && sy < ResY;
        }
        else
        {
            sz = z + offset;
            if (Wrap != 0) sz = WrapIndex(sz, ResZ);
            else include = sz >= 0 && sz < ResZ;
        }

        if (include)
        {
            int sampleIndex = FlatIndex(sx, sy, sz);
            if (IsValidVoxelIndex(sampleIndex))
            {
                weighted += Source[sampleIndex] * Weights[offset + Range];
            }
        }
    }

    float value = Source[index] * Keep + Diffuse * weighted;
    Destination[index] = FinalizeDiffusionValue(value, index, x, y, z);
}

[numthreads(16, 16, 1)]
void DiffuseAxisXTiled(
    uint3 groupThreadId : SV_GroupThreadID,
    uint3 groupId : SV_GroupID)
{
    int localZ = (int)groupThreadId.x;
    int localX = (int)groupThreadId.y;
    int tileX = (int)groupId.x * 16;
    int x = tileX + localX;
    int z = (int)groupId.y * 16 + localZ;
    int y = (int)groupId.z;
    bool lineIncluded = y >= 0 && y < ResY && z >= 0 && z < ResZ;

    bool centerIncluded;
    int centerX = ResolveTiledDiffusionAxis(x, ResX, centerIncluded);
    int centerSlot = (16 + localX) * 16 + localZ;
    float rawCenter = StoreTiledDiffusionSample(
        centerSlot,
        lineIncluded && centerIncluded,
        centerX,
        y,
        z);

    if (localX < Range)
    {
        bool leftIncluded;
        int leftX = ResolveTiledDiffusionAxis(tileX + localX - Range, ResX, leftIncluded);
        int leftSlot = (16 - Range + localX) * 16 + localZ;
        StoreTiledDiffusionSample(leftSlot, lineIncluded && leftIncluded, leftX, y, z);

        bool rightIncluded;
        int rightX = ResolveTiledDiffusionAxis(tileX + 16 + localX, ResX, rightIncluded);
        int rightSlot = (32 + localX) * 16 + localZ;
        StoreTiledDiffusionSample(rightSlot, lineIncluded && rightIncluded, rightX, y, z);
    }

    LoadTiledDiffusionWeight(groupThreadId);
    GroupMemoryBarrierWithGroupSync();

    if (x >= ResX || y >= ResY || z >= ResZ) return;

    float weighted = 0.0;
    for (int offset = -Range; offset <= Range; offset++)
    {
        int slot = (16 + localX + offset) * 16 + localZ;
        weighted += TiledWeightedDensity[slot] * TiledDiffusionWeights[offset + Range];
    }

    int index = FlatIndex(x, y, z);
    float value = rawCenter * Keep + Diffuse * weighted;
    Destination[index] = FinalizeDiffusionValue(value, index, x, y, z);
}

[numthreads(16, 16, 1)]
void DiffuseAxisYTiled(
    uint3 groupThreadId : SV_GroupThreadID,
    uint3 groupId : SV_GroupID)
{
    int localZ = (int)groupThreadId.x;
    int localY = (int)groupThreadId.y;
    int tileY = (int)groupId.x * 16;
    int y = tileY + localY;
    int z = (int)groupId.y * 16 + localZ;
    int x = (int)groupId.z;
    bool lineIncluded = x >= 0 && x < ResX && z >= 0 && z < ResZ;

    bool centerIncluded;
    int centerY = ResolveTiledDiffusionAxis(y, ResY, centerIncluded);
    int centerSlot = (16 + localY) * 16 + localZ;
    float rawCenter = StoreTiledDiffusionSample(
        centerSlot,
        lineIncluded && centerIncluded,
        x,
        centerY,
        z);

    if (localY < Range)
    {
        bool leftIncluded;
        int leftY = ResolveTiledDiffusionAxis(tileY + localY - Range, ResY, leftIncluded);
        int leftSlot = (16 - Range + localY) * 16 + localZ;
        StoreTiledDiffusionSample(leftSlot, lineIncluded && leftIncluded, x, leftY, z);

        bool rightIncluded;
        int rightY = ResolveTiledDiffusionAxis(tileY + 16 + localY, ResY, rightIncluded);
        int rightSlot = (32 + localY) * 16 + localZ;
        StoreTiledDiffusionSample(rightSlot, lineIncluded && rightIncluded, x, rightY, z);
    }

    LoadTiledDiffusionWeight(groupThreadId);
    GroupMemoryBarrierWithGroupSync();

    if (x >= ResX || y >= ResY || z >= ResZ) return;

    float weighted = 0.0;
    for (int offset = -Range; offset <= Range; offset++)
    {
        int slot = (16 + localY + offset) * 16 + localZ;
        weighted += TiledWeightedDensity[slot] * TiledDiffusionWeights[offset + Range];
    }

    int index = FlatIndex(x, y, z);
    float value = rawCenter * Keep + Diffuse * weighted;
    Destination[index] = FinalizeDiffusionValue(value, index, x, y, z);
}

[numthreads(16, 16, 1)]
void DiffuseAxisZTiled(
    uint3 groupThreadId : SV_GroupThreadID,
    uint3 groupId : SV_GroupID)
{
    int localZ = (int)groupThreadId.x;
    int localY = (int)groupThreadId.y;
    int tileZ = (int)groupId.x * 16;
    int z = tileZ + localZ;
    int y = (int)groupId.y * 16 + localY;
    int x = (int)groupId.z;
    bool lineIncluded = x >= 0 && x < ResX && y >= 0 && y < ResY;

    bool centerIncluded;
    int centerZ = ResolveTiledDiffusionAxis(z, ResZ, centerIncluded);
    int centerSlot = localY * 48 + 16 + localZ;
    float rawCenter = StoreTiledDiffusionSample(
        centerSlot,
        lineIncluded && centerIncluded,
        x,
        y,
        centerZ);

    if (localZ < Range)
    {
        bool leftIncluded;
        int leftZ = ResolveTiledDiffusionAxis(tileZ + localZ - Range, ResZ, leftIncluded);
        int leftSlot = localY * 48 + 16 - Range + localZ;
        StoreTiledDiffusionSample(leftSlot, lineIncluded && leftIncluded, x, y, leftZ);

        bool rightIncluded;
        int rightZ = ResolveTiledDiffusionAxis(tileZ + 16 + localZ, ResZ, rightIncluded);
        int rightSlot = localY * 48 + 32 + localZ;
        StoreTiledDiffusionSample(rightSlot, lineIncluded && rightIncluded, x, y, rightZ);
    }

    LoadTiledDiffusionWeight(groupThreadId);
    GroupMemoryBarrierWithGroupSync();

    if (x >= ResX || y >= ResY || z >= ResZ) return;

    float weighted = 0.0;
    for (int offset = -Range; offset <= Range; offset++)
    {
        int slot = localY * 48 + 16 + localZ + offset;
        weighted += TiledWeightedDensity[slot] * TiledDiffusionWeights[offset + Range];
    }

    int index = FlatIndex(x, y, z);
    float value = rawCenter * Keep + Diffuse * weighted;
    Destination[index] = FinalizeDiffusionValue(value, index, x, y, z);
}

[numthreads(256, 1, 1)]
void ApplyDecay(uint3 id : SV_DispatchThreadID)
{
    int index = (int)LinearIndex256(id);
    if (index >= VoxelCount) return;

    int x;
    int y;
    int z;
    Coordinates(index, x, y, z);

    Destination[index] = ApplyDecayToValue(Source[index], index, x, y, z);
}

[numthreads(16, 16, 1)]
void BuildDensityPreview(uint3 id : SV_DispatchThreadID)
{
    int u = id.x;
    int v = id.y;
    if (u >= PreviewWidth || v >= PreviewHeight) return;

    if (PreviewAxisMode == 3)
    {
        int sliceWidth = max(PreviewPadding0, 1);
        int sliceHeight = max(PreviewPadding1, 1);
        int sliceCount = max(PreviewSlice, 1);
        int columns = max(PreviewAtlasColumns, 1);
        int tileX = u / sliceWidth;
        int tileY = v / sliceHeight;
        int previewZ = tileY * columns + tileX;
        if (previewZ >= sliceCount)
        {
            DensityPreview[int2(u, v)] = 0.0;
            return;
        }

        int previewX = u - tileX * sliceWidth;
        int previewY = v - tileY * sliceHeight;
        int x = sliceWidth <= 1 ? 0 : (int)floor(((float)previewX * (ResX - 1) / (sliceWidth - 1)) + 0.5);
        int y = sliceHeight <= 1 ? 0 : (int)floor(((float)previewY * (ResY - 1) / (sliceHeight - 1)) + 0.5);
        int z = sliceCount <= 1 ? 0 : (int)floor(((float)previewZ * (ResZ - 1) / (sliceCount - 1)) + 0.5);
        DensityPreview[int2(u, v)] = Source[FlatIndex(clamp(x, 0, ResX - 1), clamp(y, 0, ResY - 1), clamp(z, 0, ResZ - 1))];
        return;
    }

    int sourceWidth = PreviewAxisMode == 2 ? ResY : ResX;
    int sourceHeight = PreviewAxisMode == 0 ? ResY : ResZ;
    sourceWidth = max(sourceWidth, 1);
    sourceHeight = max(sourceHeight, 1);

    float sourceU = PreviewWidth <= 1 ? 0.0 : ((float)u / (float)(PreviewWidth - 1)) * (float)(sourceWidth - 1);
    float sourceV = PreviewHeight <= 1 ? 0.0 : ((float)v / (float)(PreviewHeight - 1)) * (float)(sourceHeight - 1);
    int u0 = clamp((int)floor(sourceU), 0, sourceWidth - 1);
    int v0 = clamp((int)floor(sourceV), 0, sourceHeight - 1);
    int u1 = min(u0 + 1, sourceWidth - 1);
    int v1 = min(v0 + 1, sourceHeight - 1);
    float fu = frac(sourceU);
    float fv = frac(sourceV);

    int x00 = u0;
    int y00 = v0;
    int z00 = PreviewSlice;
    int x10 = u1;
    int y10 = v0;
    int z10 = PreviewSlice;
    int x01 = u0;
    int y01 = v1;
    int z01 = PreviewSlice;
    int x11 = u1;
    int y11 = v1;
    int z11 = PreviewSlice;

    if (PreviewAxisMode == 1)
    {
        y00 = y10 = y01 = y11 = PreviewSlice;
        z00 = z10 = v0;
        z01 = z11 = v1;
    }
    else if (PreviewAxisMode == 2)
    {
        x00 = x10 = x01 = x11 = PreviewSlice;
        y00 = y01 = u0;
        y10 = y11 = u1;
        z00 = z10 = v0;
        z01 = z11 = v1;
    }

    x00 = clamp(x00, 0, ResX - 1);
    x10 = clamp(x10, 0, ResX - 1);
    x01 = clamp(x01, 0, ResX - 1);
    x11 = clamp(x11, 0, ResX - 1);
    y00 = clamp(y00, 0, ResY - 1);
    y10 = clamp(y10, 0, ResY - 1);
    y01 = clamp(y01, 0, ResY - 1);
    y11 = clamp(y11, 0, ResY - 1);
    z00 = clamp(z00, 0, ResZ - 1);
    z10 = clamp(z10, 0, ResZ - 1);
    z01 = clamp(z01, 0, ResZ - 1);
    z11 = clamp(z11, 0, ResZ - 1);

    float d00 = Source[FlatIndex(x00, y00, z00)];
    float d10 = Source[FlatIndex(x10, y10, z10)];
    float d01 = Source[FlatIndex(x01, y01, z01)];
    float d11 = Source[FlatIndex(x11, y11, z11)];
    float dx0 = lerp(d00, d10, fu);
    float dx1 = lerp(d01, d11, fu);
    DensityPreview[int2(u, v)] = lerp(dx0, dx1, fv);
}

float4 CombinedPreviewVoxel(int index)
{
    // Scalar density evolves independently of retained species in V3 and remains
    // visible in the combined preview for empty and ant-only populations.
    float slime = max(Source[index], 0.0);
    float foodPheromone = HasAntParticles != 0 ? max(AntFoodPheromone[index], 0.0) : 0.0;
    float basePheromone = HasAntParticles != 0 ? max(AntBasePheromone[index], 0.0) : 0.0;
    float remainingFood = RemainingFoodAt(index);
    return float4(slime, foodPheromone, basePheromone, max(remainingFood, 0.0));
}

[numthreads(16, 16, 1)]
void BuildCombinedDensityPreview(uint3 id : SV_DispatchThreadID)
{
    int u = id.x;
    int v = id.y;
    if (u >= PreviewWidth || v >= PreviewHeight) return;

    if (PreviewAxisMode == 3)
    {
        int sliceWidth = max(PreviewPadding0, 1);
        int sliceHeight = max(PreviewPadding1, 1);
        int sliceCount = max(PreviewSlice, 1);
        int columns = max(PreviewAtlasColumns, 1);
        int tileX = u / sliceWidth;
        int tileY = v / sliceHeight;
        int previewZ = tileY * columns + tileX;
        if (previewZ >= sliceCount)
        {
            CombinedDensityPreview[int2(u, v)] = 0.0;
            return;
        }

        int previewX = u - tileX * sliceWidth;
        int previewY = v - tileY * sliceHeight;
        int x = sliceWidth <= 1 ? 0 : (int)floor(((float)previewX * (ResX - 1) / (sliceWidth - 1)) + 0.5);
        int y = sliceHeight <= 1 ? 0 : (int)floor(((float)previewY * (ResY - 1) / (sliceHeight - 1)) + 0.5);
        int z = sliceCount <= 1 ? 0 : (int)floor(((float)previewZ * (ResZ - 1) / (sliceCount - 1)) + 0.5);
        CombinedDensityPreview[int2(u, v)] = CombinedPreviewVoxel(
            FlatIndex(clamp(x, 0, ResX - 1), clamp(y, 0, ResY - 1), clamp(z, 0, ResZ - 1)));
        return;
    }

    int sourceWidth = PreviewAxisMode == 2 ? ResY : ResX;
    int sourceHeight = PreviewAxisMode == 0 ? ResY : ResZ;
    sourceWidth = max(sourceWidth, 1);
    sourceHeight = max(sourceHeight, 1);

    float sourceU = PreviewWidth <= 1 ? 0.0 : ((float)u / (float)(PreviewWidth - 1)) * (float)(sourceWidth - 1);
    float sourceV = PreviewHeight <= 1 ? 0.0 : ((float)v / (float)(PreviewHeight - 1)) * (float)(sourceHeight - 1);
    int u0 = clamp((int)floor(sourceU), 0, sourceWidth - 1);
    int v0 = clamp((int)floor(sourceV), 0, sourceHeight - 1);
    int u1 = min(u0 + 1, sourceWidth - 1);
    int v1 = min(v0 + 1, sourceHeight - 1);
    float fu = frac(sourceU);
    float fv = frac(sourceV);

    int x00 = u0;
    int y00 = v0;
    int z00 = PreviewSlice;
    int x10 = u1;
    int y10 = v0;
    int z10 = PreviewSlice;
    int x01 = u0;
    int y01 = v1;
    int z01 = PreviewSlice;
    int x11 = u1;
    int y11 = v1;
    int z11 = PreviewSlice;

    if (PreviewAxisMode == 1)
    {
        y00 = y10 = y01 = y11 = PreviewSlice;
        z00 = z10 = v0;
        z01 = z11 = v1;
    }
    else if (PreviewAxisMode == 2)
    {
        x00 = x10 = x01 = x11 = PreviewSlice;
        y00 = y01 = u0;
        y10 = y11 = u1;
        z00 = z10 = v0;
        z01 = z11 = v1;
    }

    x00 = clamp(x00, 0, ResX - 1);
    x10 = clamp(x10, 0, ResX - 1);
    x01 = clamp(x01, 0, ResX - 1);
    x11 = clamp(x11, 0, ResX - 1);
    y00 = clamp(y00, 0, ResY - 1);
    y10 = clamp(y10, 0, ResY - 1);
    y01 = clamp(y01, 0, ResY - 1);
    y11 = clamp(y11, 0, ResY - 1);
    z00 = clamp(z00, 0, ResZ - 1);
    z10 = clamp(z10, 0, ResZ - 1);
    z01 = clamp(z01, 0, ResZ - 1);
    z11 = clamp(z11, 0, ResZ - 1);

    float4 d00 = CombinedPreviewVoxel(FlatIndex(x00, y00, z00));
    float4 d10 = CombinedPreviewVoxel(FlatIndex(x10, y10, z10));
    float4 d01 = CombinedPreviewVoxel(FlatIndex(x01, y01, z01));
    float4 d11 = CombinedPreviewVoxel(FlatIndex(x11, y11, z11));
    float4 dx0 = lerp(d00, d10, fu);
    float4 dx1 = lerp(d01, d11, fu);
    CombinedDensityPreview[int2(u, v)] = lerp(dx0, dx1, fv);
}

int2 DensityAtlasPixel(int x, int y, int z)
{
    int previewResX = max(PreviewPadding0, 1);
    int previewResY = max(PreviewPadding1, 1);
    int columns = max(PreviewAtlasColumns, 1);
    int column = z % columns;
    int row = z / columns;
    return int2(column * previewResX + x, row * previewResY + y);
}

float DensityAtlasValue(int x, int y, int z)
{
    x = clamp(x, 0, max(PreviewPadding0 - 1, 0));
    y = clamp(y, 0, max(PreviewPadding1 - 1, 0));
    z = clamp(z, 0, max(PreviewSlice - 1, 0));
    return max(DensityPreviewSource.Load(int3(DensityAtlasPixel(x, y, z), 0)).r, 0.0);
}

[numthreads(16, 16, 1)]
void BuildDensityGradientPreview(uint3 id : SV_DispatchThreadID)
{
    int u = id.x;
    int v = id.y;
    if (u >= PreviewWidth || v >= PreviewHeight) return;

    if (PreviewAxisMode != 3)
    {
        DensityGradientPreview[int2(u, v)] = 0.0;
        return;
    }

    int sliceWidth = max(PreviewPadding0, 1);
    int sliceHeight = max(PreviewPadding1, 1);
    int sliceCount = max(PreviewSlice, 1);
    int columns = max(PreviewAtlasColumns, 1);
    int tileX = u / sliceWidth;
    int tileY = v / sliceHeight;
    int z = tileY * columns + tileX;
    if (z >= sliceCount)
    {
        DensityGradientPreview[int2(u, v)] = 0.0;
        return;
    }

    int x = clamp(u - tileX * sliceWidth, 0, sliceWidth - 1);
    int y = clamp(v - tileY * sliceHeight, 0, sliceHeight - 1);
    float dxNear = 0.5 * (DensityAtlasValue(x + 1, y, z) - DensityAtlasValue(x - 1, y, z));
    float dyNear = 0.5 * (DensityAtlasValue(x, y + 1, z) - DensityAtlasValue(x, y - 1, z));
    float dzNear = 0.5 * (DensityAtlasValue(x, y, z + 1) - DensityAtlasValue(x, y, z - 1));
    float dxFar = 0.25 * (DensityAtlasValue(x + 2, y, z) - DensityAtlasValue(x - 2, y, z));
    float dyFar = 0.25 * (DensityAtlasValue(x, y + 2, z) - DensityAtlasValue(x, y - 2, z));
    float dzFar = 0.25 * (DensityAtlasValue(x, y, z + 2) - DensityAtlasValue(x, y, z - 2));
    float dx = lerp(dxNear, dxFar, 0.68);
    float dy = lerp(dyNear, dyFar, 0.68);
    float dz = lerp(dzNear, dzFar, 0.68);
    float3 gradient = float3(dx, dy, dz);
    float magnitude = length(gradient);
    float3 normal = magnitude > 0.000001 ? gradient / magnitude : 0.0;
    DensityGradientPreview[int2(u, v)] = float4(normal, magnitude);
}

[numthreads(256, 1, 1)]
void BuildParticlePreview(uint3 id : SV_DispatchThreadID)
{
    int particleIndex = (int)LinearIndex256(id);
    if (particleIndex >= ParticleCapacity) return;

    int texX = particleIndex % PreviewWidth;
    int row = particleIndex / PreviewWidth;
    int posY = row * 2;
    if (posY + 1 >= PreviewHeight) return;

    float4 positionGroup = ParticlePosition[particleIndex];
    if (positionGroup.w < -0.5)
    {
        ParticlePreview[int2(texX, posY)] = float4(0.0, 0.0, 0.0, -1.0);
        ParticlePreview[int2(texX, posY + 1)] = float4(0.0, 0.0, 0.0, 0.0);
        return;
    }
    int groupIndex = (int)round(positionGroup.w);
    groupIndex = clamp(groupIndex, 0, max(GroupCount - 1, 0));
    float4 color = float4(1.0, 1.0, 1.0, 1.0);
    if (GroupCount > 0)
    {
        color = GroupColorData[groupIndex];
        bool isAnt = GroupData1[groupIndex].y > 0.5;
        bool foundFood = isAnt && DepositFixed[ParticleAntStateIndex(particleIndex)] != 0u;
        if (foundFood)
        {
            color.rgb = saturate(color.rgb * 1.75);
            color.a = 175.0 / 255.0;
        }
    }

    ParticlePreview[int2(texX, posY)] = positionGroup;
    ParticlePreview[int2(texX, posY + 1)] = color;
}

[numthreads(256, 1, 1)]
void BuildParticleTrailPreview(uint3 id : SV_DispatchThreadID)
{
    int particleIndex = (int)LinearIndex256(id);
    if (particleIndex >= ParticleCapacity) return;

    int textureWidth = max(PreviewWidth, 1);
    int trailSize = max(PreviewAtlasColumns, 2);
    int headIndex = clamp(PreviewSlice, 0, trailSize - 1);

    int texX = particleIndex % textureWidth;
    int particleRow = particleIndex / textureWidth;
    int texY = particleRow * trailSize + headIndex;
    if (texY >= PreviewHeight) return;

    float4 position = ParticlePosition[particleIndex];
    float4 yWrapped = ParticleYAxis[particleIndex];
    if (position.w < -0.5 && yWrapped.w < -1.5)
    {
        return;
    }

    if (position.w < -0.5 || yWrapped.w < -0.5 || yWrapped.w > 0.5)
    {
        int rowStart = particleRow * trailSize;
        float4 invalidPosition = float4(position.xyz, -1.0);
        for (int sample = 0; sample < trailSize; sample++)
        {
            int clearY = rowStart + sample;
            if (clearY < PreviewHeight)
            {
                ParticleTrailPreview[int2(texX, clearY)] = invalidPosition;
            }
        }

        ParticleTrailPreview[int2(texX, texY)] = position;
        if (position.w < -0.5)
        {
            yWrapped.w = -2.0;
            ParticleYAxis[particleIndex] = yWrapped;
        }
        else if (yWrapped.w < -0.5)
        {
            yWrapped.w = 0.0;
            ParticleYAxis[particleIndex] = yWrapped;
        }
        return;
    }

    ParticleTrailPreview[int2(texX, texY)] = position;
}

float MeshRawDensity(int3 sampleIndex)
{
    if (sampleIndex.x < 0 || sampleIndex.y < 0 || sampleIndex.z < 0 ||
        sampleIndex.x >= MeshResX || sampleIndex.y >= MeshResY || sampleIndex.z >= MeshResZ)
    {
        return 0.0;
    }

    int flatIndex = sampleIndex.x * MeshResY * MeshResZ + sampleIndex.y * MeshResZ + sampleIndex.z;
    return max(Source[flatIndex], 0.0);
}

float MeshDensity(int3 sampleIndex)
{
    return MeshRawDensity(sampleIndex);
}

[numthreads(256, 1, 1)]
void SmoothVolumeForMesh(uint3 id : SV_DispatchThreadID)
{
    uint index = LinearIndex256(id);
    uint voxelCountForMesh = (uint)(MeshResX * MeshResY * MeshResZ);
    if (index >= voxelCountForMesh) return;

    int layerSize = MeshResY * MeshResZ;
    int x = (int)(index / layerSize);
    int remainder = (int)(index - (uint)(x * layerSize));
    int y = remainder / MeshResZ;
    int z = remainder - y * MeshResZ;
    int3 sampleIndex = int3(x, y, z);
    float weightedDensity = 0.0;
    float totalWeight = 0.0;

    [unroll]
    for (int dz = -1; dz <= 1; dz++)
    {
        if (MeshResZ <= 1 && dz != 0) continue;
        float wz = dz == 0 ? 2.0 : 1.0;

        [unroll]
        for (int dy = -1; dy <= 1; dy++)
        {
            if (MeshResY <= 1 && dy != 0) continue;
            float wy = dy == 0 ? 2.0 : 1.0;

            [unroll]
            for (int dx = -1; dx <= 1; dx++)
            {
                if (MeshResX <= 1 && dx != 0) continue;
                float wx = dx == 0 ? 2.0 : 1.0;
                float weight = wx * wy * wz;
                weightedDensity += MeshRawDensity(sampleIndex + int3(dx, dy, dz)) * weight;
                totalWeight += weight;
            }
        }
    }

    Destination[index] = totalWeight > 0.0 ? weightedDensity / totalWeight : 0.0;
}

float3 MeshPosition(int3 sampleIndex)
{
    return (float3(sampleIndex) + 0.5) * MeshVoxelSize;
}

float3 MeshEdgePoint(float3 a, float valueA, float3 b, float valueB)
{
    float denominator = valueB - valueA;
    float amount = abs(denominator) > 0.0000001 ? (MeshIsoValue - valueA) / denominator : 0.5;
    return lerp(a, b, saturate(amount));
}

void AppendOrientedMeshTriangle(float3 a, float3 b, float3 c, float3 insidePoint, float3 outsidePoint)
{
    if (dot(cross(b - a, c - a), outsidePoint - insidePoint) < 0.0)
    {
        float3 swapValue = b;
        b = c;
        c = swapValue;
    }

    MeshTriangle outputTriangle;
    outputTriangle.A = float4(a, 1.0);
    outputTriangle.B = float4(b, 1.0);
    outputTriangle.C = float4(c, 1.0);
    MeshTriangles.Append(outputTriangle);
}

void PolygoniseMeshTetra(
    float3 p0, float v0,
    float3 p1, float v1,
    float3 p2, float v2,
    float3 p3, float v3)
{
    float3 positions[4] = { p0, p1, p2, p3 };
    float values[4] = { v0, v1, v2, v3 };
    int insideIndices[4];
    int outsideIndices[4];
    int insideCount = 0;
    int outsideCount = 0;

    [unroll]
    for (int i = 0; i < 4; i++)
    {
        if (values[i] >= MeshIsoValue)
        {
            insideIndices[insideCount++] = i;
        }
        else
        {
            outsideIndices[outsideCount++] = i;
        }
    }

    if (insideCount == 0 || insideCount == 4) return;

    float3 insidePoint = 0.0;
    float3 outsidePoint = 0.0;
    [unroll]
    for (int insideIndex = 0; insideIndex < insideCount; insideIndex++)
    {
        insidePoint += positions[insideIndices[insideIndex]];
    }
    [unroll]
    for (int outsideIndex = 0; outsideIndex < outsideCount; outsideIndex++)
    {
        outsidePoint += positions[outsideIndices[outsideIndex]];
    }
    insidePoint /= (float)insideCount;
    outsidePoint /= (float)outsideCount;

    if (insideCount == 1 || insideCount == 3)
    {
        int singleIndex = insideCount == 1 ? insideIndices[0] : outsideIndices[0];
        int other0 = insideCount == 1 ? outsideIndices[0] : insideIndices[0];
        int other1 = insideCount == 1 ? outsideIndices[1] : insideIndices[1];
        int other2 = insideCount == 1 ? outsideIndices[2] : insideIndices[2];
        float3 a = MeshEdgePoint(positions[singleIndex], values[singleIndex], positions[other0], values[other0]);
        float3 b = MeshEdgePoint(positions[singleIndex], values[singleIndex], positions[other1], values[other1]);
        float3 c = MeshEdgePoint(positions[singleIndex], values[singleIndex], positions[other2], values[other2]);
        AppendOrientedMeshTriangle(a, b, c, insidePoint, outsidePoint);
        return;
    }

    int in0 = insideIndices[0];
    int in1 = insideIndices[1];
    int out0 = outsideIndices[0];
    int out1 = outsideIndices[1];
    float3 a = MeshEdgePoint(positions[in0], values[in0], positions[out0], values[out0]);
    float3 b = MeshEdgePoint(positions[in0], values[in0], positions[out1], values[out1]);
    float3 c = MeshEdgePoint(positions[in1], values[in1], positions[out0], values[out0]);
    float3 d = MeshEdgePoint(positions[in1], values[in1], positions[out1], values[out1]);
    AppendOrientedMeshTriangle(a, b, d, insidePoint, outsidePoint);
    AppendOrientedMeshTriangle(a, d, c, insidePoint, outsidePoint);
}

[numthreads(8, 8, 4)]
void ClassifyVolumeCells(uint3 id : SV_DispatchThreadID)
{
    int cellResX = MeshResX + 1;
    int cellResY = MeshResY + 1;
    int x = (int)id.x;
    int y = (int)id.y;
    int z = MeshCellStartZ + (int)id.z;
    if (x >= cellResX || y >= cellResY || z >= MeshCellEndZ) return;

    int3 baseSample = int3(x - 1, y - 1, z - 1);
    float v0 = MeshDensity(baseSample + int3(0, 0, 0));
    float v1 = MeshDensity(baseSample + int3(1, 0, 0));
    float v2 = MeshDensity(baseSample + int3(1, 1, 0));
    float v3 = MeshDensity(baseSample + int3(0, 1, 0));
    float v4 = MeshDensity(baseSample + int3(0, 0, 1));
    float v5 = MeshDensity(baseSample + int3(1, 0, 1));
    float v6 = MeshDensity(baseSample + int3(1, 1, 1));
    float v7 = MeshDensity(baseSample + int3(0, 1, 1));
    float minimumValue = min(min(min(v0, v1), min(v2, v3)), min(min(v4, v5), min(v6, v7)));
    float maximumValue = max(max(max(v0, v1), max(v2, v3)), max(max(v4, v5), max(v6, v7)));
    if (minimumValue < MeshIsoValue && maximumValue >= MeshIsoValue)
    {
        MeshActiveCells.Append((uint)(x + y * cellResX + z * cellResX * cellResY));
    }
}

[numthreads(128, 1, 1)]
void EmitVolumeTriangles(uint3 id : SV_DispatchThreadID)
{
    int localIndex = (int)id.x;
    if (localIndex >= MeshActiveCount) return;

    int cellResX = MeshResX + 1;
    int cellResY = MeshResY + 1;
    int cellsPerLayer = cellResX * cellResY;
    int flatCell = (int)MeshActiveCellSource[MeshActiveOffset + localIndex];
    int z = flatCell / cellsPerLayer;
    int remainder = flatCell - z * cellsPerLayer;
    int y = remainder / cellResX;
    int x = remainder - y * cellResX;
    int3 baseSample = int3(x - 1, y - 1, z - 1);

    int3 s0 = baseSample + int3(0, 0, 0);
    int3 s1 = baseSample + int3(1, 0, 0);
    int3 s2 = baseSample + int3(1, 1, 0);
    int3 s3 = baseSample + int3(0, 1, 0);
    int3 s4 = baseSample + int3(0, 0, 1);
    int3 s5 = baseSample + int3(1, 0, 1);
    int3 s6 = baseSample + int3(1, 1, 1);
    int3 s7 = baseSample + int3(0, 1, 1);
    float3 p0 = MeshPosition(s0); float v0 = MeshDensity(s0);
    float3 p1 = MeshPosition(s1); float v1 = MeshDensity(s1);
    float3 p2 = MeshPosition(s2); float v2 = MeshDensity(s2);
    float3 p3 = MeshPosition(s3); float v3 = MeshDensity(s3);
    float3 p4 = MeshPosition(s4); float v4 = MeshDensity(s4);
    float3 p5 = MeshPosition(s5); float v5 = MeshDensity(s5);
    float3 p6 = MeshPosition(s6); float v6 = MeshDensity(s6);
    float3 p7 = MeshPosition(s7); float v7 = MeshDensity(s7);

    PolygoniseMeshTetra(p0, v0, p1, v1, p2, v2, p6, v6);
    PolygoniseMeshTetra(p0, v0, p2, v2, p3, v3, p6, v6);
    PolygoniseMeshTetra(p0, v0, p3, v3, p7, v7, p6, v6);
    PolygoniseMeshTetra(p0, v0, p7, v7, p4, v4, p6, v6);
    PolygoniseMeshTetra(p0, v0, p4, v4, p5, v5, p6, v6);
    PolygoniseMeshTetra(p0, v0, p5, v5, p1, v1, p6, v6);
}";
    }
}
