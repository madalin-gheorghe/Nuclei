using System;
using System.Diagnostics;
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

    /// <summary>Windows D3D11 compute backend; host objects stay behind the output sink.</summary>
    internal sealed class GpuFullSlimeSolverEngine : IGpuSimulationBackend
    {
        const float DepositScale = 1024.0f;
        const int PreviewReadbackBufferCount = 3;
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
        ID3D11ComputeShader boundaryModeTransitionShader;
        ID3D11ComputeShader moveShader;
        ID3D11ComputeShader applyDepositsShader;
        ID3D11ComputeShader projectFoodSourcesShader;
        ID3D11ComputeShader clearCountsShader;
        ID3D11ComputeShader countParticlesShader;
        ID3D11ComputeShader seedNeighbourCountsShader;
        ID3D11ComputeShader sumNeighbourAxisShader;
        ID3D11ComputeShader applyParticleDeathShader;
        ID3D11ComputeShader applyParticleDivisionShader;
        ID3D11ComputeShader diffusionShader;
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
        ID3D11Buffer particleAuxReadbackBuffer;
        ID3D11Buffer populationStateReadbackBuffer;
        readonly ID3D11Buffer[] particlePositionPreviewReadbackBuffers = new ID3D11Buffer[PreviewReadbackBufferCount];
        ID3D11Buffer particleCountBuffer;
        ID3D11Buffer depositBuffer;
        ID3D11Buffer neighbourCountA;
        ID3D11Buffer neighbourCountB;
        ID3D11Buffer groupData0Buffer;
        ID3D11Buffer groupData1Buffer;
        ID3D11Buffer groupColorDataBuffer;
        ID3D11Buffer voxelFlagsBuffer;
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
        ID3D11UnorderedAccessView depositView;
        ID3D11UnorderedAccessView neighbourCountAView;
        ID3D11UnorderedAccessView neighbourCountBView;
        ID3D11ShaderResourceView groupData0View;
        ID3D11ShaderResourceView groupData1View;
        ID3D11ShaderResourceView groupColorDataView;
        ID3D11ShaderResourceView voxelFlagsView;
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
        float[] particlePositionPreviewReadback;
        int[] particleAuxReadback;
        readonly int[] populationStateReadback = new int[4];
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

        bool densityInA = true;
        bool antFoodInA = true;
        bool antBaseInA = true;
        bool wrapBoundaryState;
        readonly bool hasAntParticles;
        readonly bool hasSlimeParticles;
        bool hasVoxelFlags;
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
        readonly int depositElementCount;
        int weightsRange = int.MinValue;
        double weightsGradual = double.NaN;
        int antWeightsRange = int.MinValue;
        int previewReadbackNextIndex = 0;
        int previewReadbackSequenceCounter = 0;
        int previewReadbackCompletedSequence = 0;
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
            if (hasSlimeParticles)
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

            DispatchClearParticleCounts(0);
            DispatchCountParticles(0);
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
            if (!hasSlimeParticles || densityA == null || densityB == null)
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

            if (hasSlimeParticles)
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
            if (particlePositionPreviewReadback != null) Array.Clear(particlePositionPreviewReadback, 0, particlePositionPreviewReadback.Length);
            if (particleAuxReadback != null) Array.Clear(particleAuxReadback, 0, particleAuxReadback.Length);
            Array.Clear(populationStateReadback, 0, populationStateReadback.Length);

            ResetPreviewReadbackState();
            ResetParticleTrailPreviewHistory();
            DispatchClearParticleCounts(0);
            DispatchCountParticles(0);

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

            if (settings.WrapBoundaries != wrapBoundaryState)
            {
                DispatchBoundaryModeTransition(settings, dimensionMode, iteration);
                wrapBoundaryState = settings.WrapBoundaries;
                DispatchClearParticleCounts(iteration);
                DispatchCountParticles(iteration);
            }

            if (hasSlimeParticles)
            {
                EnsureWeights(settings.DiffuseRange, settings.DiffusionGradual);
            }

            if (particleCount > 0 && iteration > 1)
            {
                DispatchMoveParticlesAndDeposit(settings, dimensionMode, iteration);
                DispatchApplyDeposits(settings, dimensionMode, iteration);
                DispatchClearParticleCounts(iteration);
                DispatchCountParticles(iteration);
                movedParticles = true;
            }

            stage.Stop();
            double particleMs = stage.Elapsed.TotalMilliseconds;

            stage.Restart();
            if (settings.DynamicPopulation && particleCapacity > 0 && iteration > 1)
            {
                DispatchDynamicPopulation(settings, dimensionMode, iteration);
            }
            stage.Stop();
            double populationMs = stage.Elapsed.TotalMilliseconds;

            stage.Restart();
            if (hasSlimeParticles && foodSourceOffset >= 0)
            {
                DispatchFoodSourceProjection(settings, dimensionMode, iteration);
                passCount++;
            }

            if (hasSlimeParticles && (settings.Diffuse > 0 || settings.DiffusionGradual < 1))
            {
                int axisCount = GetDiffusionAxisOrder(dimensionMode, iteration, diffusionAxisScratch);
                double strength = GradualDiffusionStrength(settings.Diffuse, settings.DiffusionGradual);
                double retention = GradualDiffusionRetention(settings.Diffuse, settings.DiffusionGradual);
                double baseKeep = 1 - strength;

                for (int i = 0; i < axisCount; i++)
                {
                    // Retention applies only on the final axis so a multi-axis
                    // pass does not compound it, matching V3.
                    double finalScale = i == axisCount - 1 ? retention : 1;
                    DispatchDiffusionPass(
                        diffusionAxisScratch[i],
                        settings,
                        dimensionMode,
                        iteration,
                        baseKeep * finalScale,
                        strength * finalScale);
                    SwapDensityBuffers();
                    passCount++;
                }
            }

            if (hasSlimeParticles)
            {
                DispatchDecayPass(settings, dimensionMode, iteration);
                SwapDensityBuffers();
                passCount++;
            }
            if (hasAntParticles)
            {
                passCount += DispatchAntPheromoneField(true, settings, dimensionMode, iteration);
                passCount += DispatchAntPheromoneField(false, settings, dimensionMode, iteration);
            }
            if (enableSharedDensityPreview)
            {
                DispatchSelectedDensityPreviewPass(settings, dimensionMode, iteration);
            }
            if (enableSharedParticlePreview)
            {
                DispatchParticlePreviewPass(settings, dimensionMode, iteration);
            }
            if (enableSharedParticleTrailPreview)
            {
                DispatchParticleTrailPreviewPass(settings, dimensionMode, iteration);
            }
            stage.Stop();
            double diffusionMs = stage.Elapsed.TotalMilliseconds;

            stage.Restart();
            if (syncVoxels)
            {
                if (hasSlimeParticles) ReadBackDensity();
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
            if (hasSlimeParticles) ReadBackDensity();
            if (hasAntParticles) ReadBackAntFields();
            ApplyDynamicFieldsToOutput();
        }

        void DispatchMoveParticlesAndDeposit(SolverGpuSettings settings, SolverGpuDimensionMode dimensionMode, int iteration)
        {
            if (particleCapacity <= 0 || particlePositionView == null)
            {
                return;
            }

            UpdateParameters(CreateParameters(0, settings, dimensionMode, iteration));

            context.CSSetShader(moveShader);
            context.CSSetConstantBuffers(0, new ID3D11Buffer[] { parameterBuffer });
            context.CSSetShaderResources(1, new ID3D11ShaderResourceView[] { groupData0View, groupData1View, voxelFlagsView, null, voxelBehaviorView, voxelVectorView, voxelDensityLimitsView });
            context.CSSetShaderResource(11, voxelVectorFrequencyView);
            if (hasAntParticles)
            {
                context.CSSetShaderResource(8, CurrentAntFoodResourceView());
                context.CSSetShaderResource(9, CurrentAntBaseResourceView());
                context.CSSetUnorderedAccessView(7, particleHomeView, -1);
            }
            context.CSSetUnorderedAccessView(0, CurrentDensityView(), -1);
            context.CSSetUnorderedAccessView(2, particlePositionView, -1);
            context.CSSetUnorderedAccessView(3, particleDirectionView, -1);
            context.CSSetUnorderedAccessView(4, particleYAxisView, -1);
            context.CSSetUnorderedAccessView(5, particleCountView, -1);
            context.CSSetUnorderedAccessView(6, depositView, -1);
            DispatchLinear256(particleCapacity);
            UnbindComputeResources();
        }

        void DispatchBoundaryModeTransition(SolverGpuSettings settings, SolverGpuDimensionMode dimensionMode, int iteration)
        {
            if (particleCapacity <= 0 || boundaryModeTransitionShader == null || particlePositionView == null)
            {
                return;
            }

            UpdateParameters(CreateParameters(0, settings, dimensionMode, iteration));
            context.CSSetShader(boundaryModeTransitionShader);
            context.CSSetConstantBuffers(0, new ID3D11Buffer[] { parameterBuffer });
            context.CSSetShaderResource(3, voxelFlagsView);
            context.CSSetUnorderedAccessViews(2, new ID3D11UnorderedAccessView[]
            {
                particlePositionView,
                particleDirectionView,
                particleYAxisView
            });
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
            context.CSSetUnorderedAccessView(0, CurrentDensityView(), -1);
            context.CSSetUnorderedAccessView(6, depositView, -1);
            DispatchLinear256(voxelCount);
            UnbindComputeResources();
        }

        void DispatchApplyDeposits(SolverGpuSettings settings, SolverGpuDimensionMode dimensionMode, int iteration)
        {
            UpdateParameters(CreateParameters(0, settings, dimensionMode, iteration));

            context.CSSetShader(applyDepositsShader);
            context.CSSetConstantBuffers(0, new ID3D11Buffer[] { parameterBuffer });
            context.CSSetShaderResource(3, voxelFlagsView);
            context.CSSetShaderResource(7, voxelDensityLimitsView);
            context.CSSetUnorderedAccessView(0, CurrentDensityView(), -1);
            if (hasAntParticles)
            {
                context.CSSetUnorderedAccessView(1, CurrentAntFoodView(), -1);
                context.CSSetUnorderedAccessView(7, CurrentAntBaseView(), -1);
            }
            context.CSSetUnorderedAccessView(6, depositView, -1);
            DispatchLinear256(voxelCount);
            UnbindComputeResources();
        }

        void DispatchClearParticleCounts(int iteration)
        {
            if (particleCountView == null)
            {
                return;
            }

            UpdateParameters(CreateParameters(0, new SolverGpuSettings(), SolverGpuDimensionMode.FromResolution(resX, resY, resZ), iteration));

            context.CSSetShader(clearCountsShader);
            context.CSSetConstantBuffers(0, new ID3D11Buffer[] { parameterBuffer });
            context.CSSetUnorderedAccessView(5, particleCountView, -1);
            DispatchLinear256(Math.Max(voxelCount, groupCount));
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
            context.CSSetUnorderedAccessView(3, particleDirectionView, -1);
            context.CSSetUnorderedAccessView(5, particleCountView, -1);
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

            // Building neighbour counts costs a seed pass plus one pass per axis over
            // the whole grid. The random path is independent of neighbours, so that
            // build is only worth paying for when the neighbour rule itself is due.
            bool deathRuleDue = settings.Death && iteration % settings.DeathFrequency == 0;
            bool deathRandomDue = settings.RandomDeathProbability > 0 && randomDue;
            if (deathRuleDue || deathRandomDue)
            {
                ID3D11UnorderedAccessView neighbourView = deathRuleDue
                    ? DispatchBuildNeighbourCounts(settings.DeathRange, settings, dimensionMode, iteration)
                    : NeighbourCountsWithoutRebuild();
                DispatchParticleDeath(neighbourView, settings, dimensionMode, iteration);
            }

            bool divisionRuleDue = settings.Division && iteration % settings.DivisionFrequency == 0;
            bool divisionRandomDue = settings.RandomDivisionProbability > 0 && randomDue;
            if (divisionRuleDue || divisionRandomDue)
            {
                ID3D11UnorderedAccessView neighbourView = divisionRuleDue
                    ? DispatchBuildNeighbourCounts(settings.DivisionRange, settings, dimensionMode, iteration)
                    : NeighbourCountsWithoutRebuild();
                DispatchParticleDivision(neighbourView, settings, dimensionMode, iteration);
            }
        }

        /// <summary>
        /// Neighbour buffer without refreshing it. Only valid for a dispatch whose
        /// decision ignores neighbour counts, which is the random-only population
        /// path; the per-particle neighbour value it records is stale on those steps.
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

        void DispatchParticleDeath(ID3D11UnorderedAccessView neighbourView, SolverGpuSettings settings, SolverGpuDimensionMode dimensionMode, int iteration)
        {
            if (neighbourView == null || applyParticleDeathShader == null)
            {
                return;
            }

            UpdateParameters(CreateParameters(0, settings, dimensionMode, iteration));
            context.CSSetShader(applyParticleDeathShader);
            context.CSSetConstantBuffers(0, new ID3D11Buffer[] { parameterBuffer });
            context.CSSetUnorderedAccessView(0, neighbourView, -1);
            context.CSSetUnorderedAccessView(2, particlePositionView, -1);
            context.CSSetUnorderedAccessView(3, particleDirectionView, -1);
            context.CSSetUnorderedAccessView(4, particleYAxisView, -1);
            context.CSSetUnorderedAccessView(5, particleCountView, -1);
            context.CSSetUnorderedAccessView(6, depositView, -1);
            DispatchLinear256(particleCapacity);
            UnbindComputeResources();
        }

        void DispatchParticleDivision(ID3D11UnorderedAccessView neighbourView, SolverGpuSettings settings, SolverGpuDimensionMode dimensionMode, int iteration)
        {
            if (neighbourView == null || applyParticleDivisionShader == null)
            {
                return;
            }

            UpdateParameters(CreateParameters(0, settings, dimensionMode, iteration));
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
            context.CSSetUnorderedAccessView(2, particlePositionView, -1);
            context.CSSetUnorderedAccessView(3, particleDirectionView, -1);
            context.CSSetUnorderedAccessView(4, particleYAxisView, -1);
            context.CSSetUnorderedAccessView(5, particleCountView, -1);
            context.CSSetUnorderedAccessView(6, depositView, -1);
            context.CSSetUnorderedAccessView(7, particleHomeView, -1);
            DispatchLinear256(particleCapacity);
            UnbindComputeResources();
        }

        void DispatchDiffusionPass(int axis, SolverGpuSettings settings, SolverGpuDimensionMode dimensionMode, int iteration, double keep, double diffuseAmount)
        {
            FullSolverParameters parameters = CreateParameters(axis, settings, dimensionMode, iteration);
            parameters.Keep = (float)keep;
            parameters.Diffuse = (float)diffuseAmount;
            UpdateParameters(parameters);

            context.CSSetShader(diffusionShader);
            context.CSSetConstantBuffers(0, new ID3D11Buffer[] { parameterBuffer });
            context.CSSetShaderResources(0, new ID3D11ShaderResourceView[] { weightsView });
            context.CSSetShaderResource(3, voxelFlagsView);
            context.CSSetShaderResource(7, voxelDensityLimitsView);
            context.CSSetUnorderedAccessView(0, CurrentDensityView(), -1);
            context.CSSetUnorderedAccessView(1, NextDensityView(), -1);
            DispatchLinear256(voxelCount);
            UnbindComputeResources();
        }

        void DispatchDecayPass(SolverGpuSettings settings, SolverGpuDimensionMode dimensionMode, int iteration)
        {
            UpdateParameters(CreateParameters(0, settings, dimensionMode, iteration));

            context.CSSetShader(decayShader);
            context.CSSetConstantBuffers(0, new ID3D11Buffer[] { parameterBuffer });
            context.CSSetShaderResource(3, voxelFlagsView);
            context.CSSetShaderResource(7, voxelDensityLimitsView);
            context.CSSetUnorderedAccessView(0, CurrentDensityView(), -1);
            context.CSSetUnorderedAccessView(1, NextDensityView(), -1);
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
            context.CSSetUnorderedAccessView(0, foodField ? CurrentAntFoodView() : CurrentAntBaseView(), -1);
            context.CSSetUnorderedAccessView(1, foodField ? NextAntFoodView() : NextAntBaseView(), -1);
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
            parameters.HasVoxelBehavior = hasVoxelBehavior ? 1 : 0;
            parameters.HasVoxelVectors = hasVoxelVectors ? 1 : 0;
            parameters.HasVoxelDensityLimits = hasVoxelDensityLimits ? 1 : 0;
            parameters.HasVoxelVectorFrequencies = hasVoxelVectorFrequencies ? 1 : 0;
            parameters.VoxelVectorDefaultFrequency = voxelVectorDefaultFrequency;
            parameters.HasVoxelVectorData = hasVoxelVectorData ? 1 : 0;
            parameters.VectorPadding0 = 0;
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
            parameters.ChannelPadding0 = 0;
            parameters.ChannelPadding1 = 0;
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
            parameters.RandomPadding1 = 0;
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
            parameters.Padding0 = 0;
            parameters.Padding1 = 0;
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
            UpdateOptionalFloatBuffer(snapshot.VoxelBehaviorData, ref voxelBehaviorBuffer, ref voxelBehaviorView, ref voxelBehaviorElementCount);
            UpdateOptionalFloat3Buffer(snapshot.VoxelVectorData, ref voxelVectorBuffer, ref voxelVectorView);
            UpdateOptionalIntBuffer(snapshot.VoxelVectorFrequencies, ref voxelVectorFrequencyBuffer, ref voxelVectorFrequencyView);
            UpdateOptionalFloatBuffer(snapshot.VoxelDensityLimits, ref voxelDensityLimitsBuffer, ref voxelDensityLimitsView, ref voxelDensityLimitElementCount);
            hasVoxelFlags = snapshot.VoxelFlags != null;
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
                if (valueIndex == VoxelPreviewField.SlimeChemoattractants && !hasSlimeParticles)
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
                    && !hasAntParticles && !hasSlimeParticles)
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
            int auxiliaryCount = checked(particleCapacity * 5);

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
            if (particleAuxReadback == null || particleAuxReadback.Length != auxiliaryCount)
            {
                particleAuxReadback = new int[auxiliaryCount];
            }
            int floatByteCount = checked(floatCount * sizeof(float));
            if (particlePositionReadbackBuffer == null) particlePositionReadbackBuffer = CreateReadbackBuffer(floatByteCount);
            if (particleDirectionReadbackBuffer == null) particleDirectionReadbackBuffer = CreateReadbackBuffer(floatByteCount);
            if (particleYAxisReadbackBuffer == null) particleYAxisReadbackBuffer = CreateReadbackBuffer(floatByteCount);
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
                hasSlimeParticles ? densityReadback : null,
                hasAntParticles ? antFoodReadback : null,
                hasAntParticles ? antBaseReadback : null,
                hasAntParticles ? ConvertRemainingFood() : null,
                hasSlimeParticles,
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

        void ReadBackParticleAxes()
        {
            if (particleCapacity <= 0)
            {
                return;
            }

            ReadBackFloat4Buffer(particleDirectionReadbackBuffer, particleDirectionBuffer, particleDirectionReadback);
            ReadBackFloat4Buffer(particleYAxisReadbackBuffer, particleYAxisBuffer, particleYAxisReadback);
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
                homes[target4 + 3] = 1;
            }
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

            particleCountBuffer = device.CreateBuffer(
                checked((voxelCount + 4 + groupCount) * sizeof(uint)),
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
        /// V3 advances age inside particleCheckParentVoxel, which runs once during reset
        /// and again every iteration, so by the time its population rules are evaluated a
        /// particle's age is iteration + 1. V4 only advances age inside the move kernel,
        /// which is skipped on iteration 1, giving iteration - 1. Seeding the uploaded
        /// ages with those two missing increments makes every age gate -- death,
        /// division and ant behaviour -- fire on the same iteration as V3.
        /// </summary>
        const int V3AgeAlignmentOffset = 2;

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
            moveShader = CreateComputeShader("MoveParticlesAndDeposit");
            applyDepositsShader = CreateComputeShader("ApplyDeposits");
            projectFoodSourcesShader = CreateComputeShader("ProjectFoodSources");
            clearCountsShader = CreateComputeShader("ClearParticleCounts");
            countParticlesShader = CreateComputeShader("CountParticles");
            seedNeighbourCountsShader = CreateComputeShader("SeedNeighbourCounts");
            sumNeighbourAxisShader = CreateComputeShader("SumNeighbourAxis");
            applyParticleDeathShader = CreateComputeShader("ApplyParticleDeath");
            applyParticleDivisionShader = CreateComputeShader("ApplyParticleDivision");
            diffusionShader = CreateComputeShader("DiffuseAxis");
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

            for (int i = 0; i <= 11; i++)
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
            if (depositView != null) depositView.Dispose();
            if (neighbourCountAView != null) neighbourCountAView.Dispose();
            if (neighbourCountBView != null) neighbourCountBView.Dispose();
            if (groupData0View != null) groupData0View.Dispose();
            if (groupData1View != null) groupData1View.Dispose();
            if (groupColorDataView != null) groupColorDataView.Dispose();
            if (voxelFlagsView != null) voxelFlagsView.Dispose();
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
            if (particleCountBuffer != null) particleCountBuffer.Dispose();
            if (depositBuffer != null) depositBuffer.Dispose();
            if (neighbourCountA != null) neighbourCountA.Dispose();
            if (neighbourCountB != null) neighbourCountB.Dispose();
            if (groupData0Buffer != null) groupData0Buffer.Dispose();
            if (groupData1Buffer != null) groupData1Buffer.Dispose();
            if (groupColorDataBuffer != null) groupColorDataBuffer.Dispose();
            if (voxelFlagsBuffer != null) voxelFlagsBuffer.Dispose();
            if (voxelBehaviorBuffer != null) voxelBehaviorBuffer.Dispose();
            if (voxelVectorBuffer != null) voxelVectorBuffer.Dispose();
            if (voxelVectorFrequencyBuffer != null) voxelVectorFrequencyBuffer.Dispose();
            if (voxelDensityLimitsBuffer != null) voxelDensityLimitsBuffer.Dispose();
            if (parameterBuffer != null) parameterBuffer.Dispose();
            if (volumeMeshParameterBuffer != null) volumeMeshParameterBuffer.Dispose();
            if (boundaryModeTransitionShader != null) boundaryModeTransitionShader.Dispose();
            if (moveShader != null) moveShader.Dispose();
            if (applyDepositsShader != null) applyDepositsShader.Dispose();
            if (clearCountsShader != null) clearCountsShader.Dispose();
            if (countParticlesShader != null) countParticlesShader.Dispose();
            if (seedNeighbourCountsShader != null) seedNeighbourCountsShader.Dispose();
            if (sumNeighbourAxisShader != null) sumNeighbourAxisShader.Dispose();
            if (applyParticleDeathShader != null) applyParticleDeathShader.Dispose();
            if (applyParticleDivisionShader != null) applyParticleDivisionShader.Dispose();
            if (diffusionShader != null) diffusionShader.Dispose();
            if (decayShader != null) decayShader.Dispose();
            if (densityPreviewShader != null) densityPreviewShader.Dispose();
            if (combinedDensityPreviewShader != null) combinedDensityPreviewShader.Dispose();
            if (densityGradientPreviewShader != null) densityGradientPreviewShader.Dispose();
            if (particlePreviewShader != null) particlePreviewShader.Dispose();
            if (particleTrailPreviewShader != null) particleTrailPreviewShader.Dispose();
            if (volumeSmoothShader != null) volumeSmoothShader.Dispose();
            if (volumeCellClassifyShader != null) volumeCellClassifyShader.Dispose();
            if (volumeTriangleShader != null) volumeTriangleShader.Dispose();
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
            public int Padding0;
            public int Padding1;
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
            public int VectorPadding0;
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
            public int ChannelPadding0;
            public int ChannelPadding1;
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
            public int RandomPadding1;
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
    int Padding0;
    int Padding1;
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
    int VectorPadding0;
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
    int ChannelPadding0;
    int ChannelPadding1;
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
    int RandomPadding1;
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
    if (minDensity >= 0.0 && value > 0.0 && value < minDensity) value = minDensity;
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

float PositiveModulo(float value, float extent)
{
    if (extent <= 0.0) return 0.0;
    return value - floor(value / extent) * extent;
}

float3 WrapSensorPosition(float3 p)
{
    if (PlanarYZ == 0) p.x = PositiveModulo(p.x, DimX);
    if (PlanarXZ == 0) p.y = PositiveModulo(p.y, DimY);
    if (PlanarXY == 0) p.z = PositiveModulo(p.z, DimZ);

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

float SampleAntField(float3 p, bool foundFood, int currentParentIndex, int particleIndex)
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
    float slimeInfluence = HasSlimeParticles != 0 ? Source[index] * AntSlime : 0.0;
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

float3 ApplyPlanarMode(float3 value)
{
    if (PlanarXY != 0) value.z = 0;
    if (PlanarXZ != 0) value.y = 0;
    if (PlanarYZ != 0) value.x = 0;
    return value;
}

float3 ApplyPlanarPosition(float3 value)
{
    if (PlanarXY != 0) value.z = DimZ * 0.5;
    if (PlanarXZ != 0) value.y = DimY * 0.5;
    if (PlanarYZ != 0) value.x = DimX * 0.5;
    return value;
}

void WrapMovementCoordinate(inout float position, float extent, inout uint wrapped)
{
    if (position >= 0.0 && position < extent) return;
    position = PositiveModulo(position, extent);
    wrapped = 1u;
}

void ReflectMovementCoordinate(inout float position, inout float direction, float extent)
{
    float minimum = VoxelSize;
    float maximum = extent - VoxelSize;
    if (maximum <= minimum)
    {
        position = extent * 0.5;
        direction = -direction;
        return;
    }

    if (position > minimum && position < maximum) return;

    float span = maximum - minimum;
    float phase = PositiveModulo(position - minimum, span * 2.0);
    bool ascending = phase <= span;
    position = ascending ? minimum + phase : maximum - (phase - span);
    direction *= ascending ? 1.0 : -1.0;
    position = clamp(position, minimum + 1e-4, maximum - 1e-4);
}

void ReconcileReflectiveCoordinate(inout float position, inout float direction, float extent, uint seed)
{
    float minimum = VoxelSize;
    float maximum = extent - VoxelSize;
    if (maximum <= minimum)
    {
        position = extent * 0.5;
        direction = -direction;
        return;
    }

    float inset = min(VoxelSize * 0.75, (maximum - minimum) * 0.25);
    inset *= 0.25 + Hash01(seed) * 0.75;
    if (position <= minimum)
    {
        position = minimum + inset;
        direction = abs(direction);
    }
    else if (position >= maximum)
    {
        position = maximum - inset;
        direction = -abs(direction);
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

bool CanDepositAtVoxel(int index, float sensorDistance)
{
    if (Wrap != 0) return true;

    int x;
    int y;
    int z;
    Coordinates(index, x, y, z);

    int boundaryRange = 1;
    float sensorDiameter = sensorDistance * 2.0;

    if (Tridimensional != 0)
    {
        if (DimX > sensorDiameter && DimY > sensorDiameter && DimZ > sensorDiameter)
        {
            boundaryRange = (int)sensorDistance;
        }

        return x >= boundaryRange && x < ResX - boundaryRange &&
               y >= boundaryRange && y < ResY - boundaryRange &&
               z >= boundaryRange && z < ResZ - boundaryRange;
    }

    if (PlanarXY != 0)
    {
        if (DimX > sensorDiameter && DimY > sensorDiameter)
        {
            boundaryRange = (int)sensorDistance;
        }

        return x >= boundaryRange && x < ResX - boundaryRange &&
               y >= boundaryRange && y < ResY - boundaryRange;
    }

    if (PlanarXZ != 0)
    {
        if (DimX > sensorDiameter && DimZ > sensorDiameter)
        {
            boundaryRange = (int)sensorDistance;
        }

        return x >= boundaryRange && x < ResX - boundaryRange &&
               z >= boundaryRange && z < ResZ - boundaryRange;
    }

    if (DimY > sensorDiameter && DimZ > sensorDiameter)
    {
        boundaryRange = (int)sensorDistance;
    }

    return y >= boundaryRange && y < ResY - boundaryRange &&
           z >= boundaryRange && z < ResZ - boundaryRange;
}

bool TryRecoverWalkableStep(int currentParentIndex, int particleIndex, float speed, out int recoveredIndex, out float3 recoveredPosition)
{
    recoveredIndex = -1;
    recoveredPosition = 0.0;
    if (!IsValidVoxelIndex(currentParentIndex)) return false;

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
                if (!IsValidVoxelIndex(candidateIndex) || IsBoundary(x, y, z)) continue;

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

[numthreads(256, 1, 1)]
void ApplyBoundaryModeTransition(uint3 id : SV_DispatchThreadID)
{
    int particleIndex = (int)LinearIndex256(id);
    if (particleIndex >= ParticleCount) return;

    float4 posGroup = ParticlePosition[particleIndex];
    if (posGroup.w < -0.5) return;

    float4 dirParent = ParticleDirection[particleIndex];
    float4 yWrapped = ParticleYAxis[particleIndex];
    float3 position = ApplyPlanarPosition(posGroup.xyz);
    float3 direction = NormalizeOr(ApplyPlanarMode(dirParent.xyz), float3(1, 0, 0));
    uint wrapped = 0;

    if (Wrap != 0)
    {
        ApplyMovementBoundaries(position, direction, wrapped);
    }
    else
    {
        if (PlanarYZ == 0) ReconcileReflectiveCoordinate(position.x, direction.x, DimX, Hash((uint)particleIndex ^ 0x68bc21ebu));
        if (PlanarXZ == 0) ReconcileReflectiveCoordinate(position.y, direction.y, DimY, Hash((uint)particleIndex ^ 0x02e5be93u));
        if (PlanarXY == 0) ReconcileReflectiveCoordinate(position.z, direction.z, DimZ, Hash((uint)particleIndex ^ 0x967a889bu));
    }
    position = ApplyPlanarPosition(position);

    int previousParentIndex = (int)round(dirParent.w);
    int parentIndex = VoxelIndexFromPosition(position);
    if (parentIndex < 0)
    {
        int recoveredIndex;
        float3 recoveredPosition;
        if (TryRecoverWalkableStep(previousParentIndex, particleIndex, 0.0, recoveredIndex, recoveredPosition))
        {
            parentIndex = recoveredIndex;
            direction = NormalizeOr(ApplyPlanarMode(recoveredPosition - position), -direction);
            position = recoveredPosition;
        }
        else
        {
            parentIndex = IsValidVoxelIndex(previousParentIndex) ? previousParentIndex : -1;
            position = ApplyPlanarPosition(posGroup.xyz);
        }
    }

    direction = NormalizeOr(ApplyPlanarMode(direction), float3(1, 0, 0));
    float3 yAxis = SafeYAxis(direction, yWrapped.xyz);
    ParticlePosition[particleIndex] = float4(position, posGroup.w);
    ParticleDirection[particleIndex] = float4(direction, (float)parentIndex);
    ParticleYAxis[particleIndex] = float4(yAxis, (float)wrapped);
}

[numthreads(256, 1, 1)]
void MoveParticlesAndDeposit(uint3 id : SV_DispatchThreadID)
{
    int particleIndex = (int)LinearIndex256(id);
    if (particleIndex >= ParticleCount) return;

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
    bool isAnt = group1.y > 0.5;
    uint particleAge = DepositFixed[ParticleAgeIndex(particleIndex)];
    bool foundFood = isAnt && DepositFixed[ParticleAntStateIndex(particleIndex)] != 0u;
    float3 homePosition = isAnt ? ParticleHome[particleIndex].xyz : position;

    int currentParentIndex = (int)round(dirParent.w);
    if (!IsValidVoxelIndex(currentParentIndex))
    {
        currentParentIndex = VoxelIndexFromPosition(position);
    }

    float4 behavior = float4(1.0, 1.0, 1.0, 1.0);
    float4 vectorField = float4(0.0, 0.0, 0.0, 0.0);
    if (IsValidVoxelIndex(currentParentIndex))
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
    float sensorDistance = group0.y * behavior.y;
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
    if (DynamicPopulation != 0)
    {
        float wander = saturate(group0.w);
        float groupPopulation = (float)ParticleCounts[GroupPopulationIndex(groupIndex)];
        wanderFrequency = wander <= 0.0
            ? 0u
            : max(1u, (uint)floor(pow(1.0 - wander, 3.0) * groupPopulation / 10.0));
    }

    float3 homeOffset = position - homePosition;
    float homeDistance = length(homeOffset);
    float3 towardsHome = NormalizeOr(-homeOffset, -x);

    float3 leftSensor = position + (x * sensorCos - y * sensorSin) * sensorDistance;
    float3 frontSensor = position + x * sensorDistance;
    float3 rightSensor = position + (x * sensorCos + y * sensorSin) * sensorDistance;

    float value0 = isAnt ? SampleAntField(leftSensor, foundFood, currentParentIndex, particleIndex) : SampleDensity(leftSensor);
    float value1 = isAnt ? SampleAntField(frontSensor, foundFood, currentParentIndex, particleIndex) : SampleDensity(frontSensor);
    float value2 = isAnt ? SampleAntField(rightSensor, foundFood, currentParentIndex, particleIndex) : SampleDensity(rightSensor);
    float value3 = -1.0;
    float value4 = -1.0;

    if (Tridimensional != 0)
    {
        float3 zAxis = NormalizeOr(cross(y, x), float3(0, 0, 1));
        value3 = isAnt
            ? SampleAntField(position + (x * sensorCos + zAxis * sensorSin) * sensorDistance, foundFood, currentParentIndex, particleIndex)
            : SampleDensity(position + (x * sensorCos + zAxis * sensorSin) * sensorDistance);
        value4 = isAnt
            ? SampleAntField(position + (x * sensorCos - zAxis * sensorSin) * sensorDistance, foundFood, currentParentIndex, particleIndex)
            : SampleDensity(position + (x * sensorCos - zAxis * sensorSin) * sensorDistance);
    }

    int bestIndex = ChooseBestSensor(value0, value1, value2, value3, value4);
    float3 force = x;
    if (bestIndex < 0)
    {
        uint turnSeed = Hash((uint)particleIndex + (uint)Iteration * 747796405u);
        force = RotateForce((turnSeed & 1u) == 0u ? 0 : 2, x, y, rotationCos, rotationSin);
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
        if (particleAge < 30u && homeDistance < sensorDistance * 3.0)
        {
            force += NormalizeOr(homeOffset, x) * 10.0;
        }
        if (foundFood && antSteeringOrder % wanderFrequency == 0u)
        {
            force += towardsHome;
        }
        if (!foundFood && particleAge > 100u)
        {
            force += towardsHome * (0.01 * particleAge / 100.0);
        }
        if (homeDistance <= sensorDistance * 2.0 && particleAge > 30u)
        {
            x = towardsHome;
            y = SafeYAxis(x, y);
            force += towardsHome;
        }
    }

    float3 steeringDirection = NormalizeOr(force, x);
    float3 moveDirection = NormalizeOr(steeringDirection + x * 0.2, x);
    uint movementSeed = Hash((uint)particleIndex + (uint)Iteration * 2891336453u);
    if (!isAnt && wanderFrequency > 0u && movementSeed % wanderFrequency == 0u)
    {
        moveDirection = NormalizeOr(moveDirection + RandomPlanarVector(movementSeed) * 1.5, moveDirection);
    }

    moveDirection = NormalizeOr(ApplyPlanarMode(moveDirection), x);
    float3 nextPosition = position + moveDirection * speed;
    nextPosition = ApplyPlanarPosition(nextPosition);

    uint wrapped = 0;
    ApplyMovementBoundaries(nextPosition, moveDirection, wrapped);
    nextPosition = ApplyPlanarPosition(nextPosition);

    int parentIndex = VoxelIndexFromPosition(nextPosition);
    if (parentIndex < 0)
    {
        int recoveredIndex;
        float3 recoveredPosition;
        if (TryRecoverWalkableStep(currentParentIndex, particleIndex, speed, recoveredIndex, recoveredPosition))
        {
            parentIndex = recoveredIndex;
            moveDirection = NormalizeOr(ApplyPlanarMode(recoveredPosition - position), -moveDirection);
            nextPosition = recoveredPosition;
        }
        else
        {
            parentIndex = currentParentIndex;
            if (!IsValidVoxelIndex(parentIndex)) parentIndex = -1;
            nextPosition = position;
            moveDirection = NormalizeOr(ApplyPlanarMode(-moveDirection), x);
        }
    }

    if (parentIndex >= 0 && parentIndex < VoxelCount)
    {
        uint previousCount = ParticleCounts[parentIndex];
        // Mirrors V3: the deposit is scaled by whether the *previous* move landed in
        // an empty voxel, and the flag is then updated for the next step. V3 updates
        // the flag whenever the destination is empty, even if the boundary guard
        // suppresses the deposit itself, so the update sits outside that check.
        bool wasHighDeposit = HighDepositOffset < 0
            || DepositFixed[HighDepositIndex(particleIndex)] != 0u;
        if (HighDepositOffset >= 0)
        {
            DepositFixed[HighDepositIndex(particleIndex)] = previousCount == 0u ? 1u : 0u;
        }

        if (previousCount == 0u && CanDepositAtVoxel(parentIndex, sensorDistance))
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
        if (nextHomeDistance < max(speed, 1e-4))
        {
            foundFood = false;
            nextParticleAge = 1u;
        }
    }

    x = NormalizeOr(moveDirection, x);
    y = SafeYAxis(x, y);

    ParticlePosition[particleIndex] = float4(nextPosition, (float)groupIndex);
    ParticleDirection[particleIndex] = float4(x, (float)parentIndex);
    ParticleYAxis[particleIndex] = float4(y, (float)wrapped);
    DepositFixed[ParticleAgeIndex(particleIndex)] = nextParticleAge;
    if (isAnt) DepositFixed[ParticleAntStateIndex(particleIndex)] = foundFood ? 1u : 0u;
}

// Mirrors V3 projectFoodSources. Runs before diffusion so the injected value
// is diffused and decayed by the normal slime-field update in the same step.
// The source map is immutable, so every reset re-establishes the same strength.
[numthreads(256, 1, 1)]
void ProjectFoodSources(uint3 id : SV_DispatchThreadID)
{
    int index = (int)LinearIndex256(id);
    if (index >= VoxelCount) return;
    if (!IsValidVoxelIndex(index)) return;

    float foodValue = FoodSourceAt(index);
    if (foodValue <= 0.0) return;

    Source[index] = ClampDensityLimits(Source[index] + foodValue, index);
}

[numthreads(256, 1, 1)]
void ApplyDeposits(uint3 id : SV_DispatchThreadID)
{
    int index = (int)LinearIndex256(id);
    if (index >= VoxelCount) return;

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
        Source[index] = ClampDensityLimits(Source[index] + fixedDeposit / DepositScale, index);
    }
    if (HasSlimeParticles != 0) DepositFixed[SlimeDepositIndex(index)] = 0u;

    if (HasAntParticles != 0)
    {
        uint foodDeposit = DepositFixed[AntFoodDepositIndex(index)];
        uint baseDeposit = DepositFixed[AntBaseDepositIndex(index)];
        if (foodDeposit > 0u) Destination[index] = min(1.0, Destination[index] + foodDeposit / DepositScale);
        if (baseDeposit > 0u) AntBaseDestination[index] = min(1.0, AntBaseDestination[index] + baseDeposit / DepositScale);
        DepositFixed[AntFoodDepositIndex(index)] = 0u;
        DepositFixed[AntBaseDepositIndex(index)] = 0u;
    }
}

[numthreads(256, 1, 1)]
void ClearParticleCounts(uint3 id : SV_DispatchThreadID)
{
    int index = (int)LinearIndex256(id);
    if (index < VoxelCount)
    {
        ParticleCounts[index] = 0u;
    }
    if (index < GroupCount)
    {
        ParticleCounts[GroupPopulationIndex(index)] = 0u;
    }
}

[numthreads(256, 1, 1)]
void CountParticles(uint3 id : SV_DispatchThreadID)
{
    int index = (int)LinearIndex256(id);
    if (index >= ParticleCount) return;
    if (!IsParticleAlive(index)) return;

    int parentIndex = (int)round(ParticleDirection[index].w);
    if (IsValidVoxelIndex(parentIndex))
    {
        InterlockedAdd(ParticleCounts[parentIndex], 1u);
    }

    int groupIndex = (int)round(ParticlePosition[index].w);
    if (groupIndex >= 0 && groupIndex < GroupCount)
    {
        InterlockedAdd(ParticleCounts[GroupPopulationIndex(groupIndex)], 1u);
    }
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

// Reserve first, undo if we crossed the floor. A single atomic add always
// succeeds, whereas the bounded compare-exchange retry loop this replaces
// silently dropped claims once many particles contended on the one counter --
// which made the observed death rate track contention instead of the
// configured probability.
// Reserve first, undo if we crossed the floor. A single atomic add always
// succeeds, whereas the bounded compare-exchange retry loop this replaces
// silently dropped claims once many particles contended on the one counter --
// which made the observed death rate track contention instead of the
// configured probability.
bool TryClaimDeath()
{
    uint minimum = (uint)max(MinimumPopulation, 0);
    uint previous;
    InterlockedAdd(ParticleCounts[ActivePopulationIndex()], 0xffffffffu, previous);
    if (previous > minimum) return true;

    uint ignored;
    InterlockedAdd(ParticleCounts[ActivePopulationIndex()], 1u, ignored);
    return false;
}

[numthreads(256, 1, 1)]
void ApplyParticleDeath(uint3 id : SV_DispatchThreadID)
{
    int particleIndex = (int)LinearIndex256(id);
    if (particleIndex >= ParticleCapacity || !IsParticleAlive(particleIndex)) return;

    int parentIndex = (int)round(ParticleDirection[particleIndex].w);
    int neighbourCount = 0;
    if (parentIndex >= 0 && parentIndex < VoxelCount)
    {
        // Excludes the particle itself, matching V3, which subtracts one after
        // summing the box (Solver.cs particleCheckNeighbourCount).
        neighbourCount = max(0, (int)round(Source[parentIndex]) - 1);
    }
    DepositFixed[ParticleDeathNeighbourIndex(particleIndex)] = (uint)neighbourCount;

    uint age = DepositFixed[ParticleAgeIndex(particleIndex)];
    bool outsideNeighbourRange = neighbourCount < DeathMinimumNeighbours ||
                                 neighbourCount > DeathMaximumNeighbours;
    bool oldEnoughToDie = age >= (uint)max(DeathMinimumAge, 0);
    bool shouldDie = DeathEnabled != 0 && oldEnoughToDie && outsideNeighbourRange;

    // Random death is independent of the neighbour rule and of the age gate,
    // matching V3 applyRandomParticleDeath. TryClaimDeath still enforces the
    // minimum-population budget.
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
    position.w = -1.0;
    ParticlePosition[particleIndex] = position;

    float4 direction = ParticleDirection[particleIndex];
    direction.w = -1.0;
    ParticleDirection[particleIndex] = direction;

    float4 yAxis = ParticleYAxis[particleIndex];
    yAxis.w = -1.0;
    ParticleYAxis[particleIndex] = yAxis;

    if (parentIndex >= 0 && parentIndex < VoxelCount)
    {
        uint previousCount;
        InterlockedAdd(ParticleCounts[parentIndex], 0xffffffffu, previousCount);
    }

    uint ignoredGroupCount;
    InterlockedAdd(ParticleCounts[GroupPopulationIndex(groupIndex)], 0xffffffffu, ignoredGroupCount);

    uint freeIndex;
    InterlockedAdd(ParticleCounts[FreePopulationIndex()], 1u, freeIndex);
    if (freeIndex < (uint)ParticleCapacity)
    {
        DepositFixed[FreeSlotIndex((int)freeIndex)] = (uint)particleIndex;
    }
}

// Same reserve-then-undo shape as TryClaimDeath, for the same reason.
bool TryClaimBirth(out uint particleSlot)
{
    particleSlot = 0u;
    uint maximum = (uint)min(max(MaximumPopulation, 0), ParticleCapacity);
    uint ignored;
    uint previous;
    InterlockedAdd(ParticleCounts[ActivePopulationIndex()], 1u, previous);
    if (previous >= maximum)
    {
        InterlockedAdd(ParticleCounts[ActivePopulationIndex()], 0xffffffffu, ignored);
        return false;
    }

    uint previousFree;
    InterlockedAdd(ParticleCounts[FreePopulationIndex()], 0xffffffffu, previousFree);
    if (previousFree == 0u)
    {
        InterlockedAdd(ParticleCounts[FreePopulationIndex()], 1u, ignored);
        InterlockedAdd(ParticleCounts[ActivePopulationIndex()], 0xffffffffu, ignored);
        return false;
    }

    particleSlot = DepositFixed[FreeSlotIndex((int)previousFree - 1)];
    if (particleSlot >= (uint)ParticleCapacity)
    {
        InterlockedAdd(ParticleCounts[FreePopulationIndex()], 1u, ignored);
        InterlockedAdd(ParticleCounts[ActivePopulationIndex()], 0xffffffffu, ignored);
        return false;
    }

    return true;
}

[numthreads(256, 1, 1)]
void ApplyParticleDivision(uint3 id : SV_DispatchThreadID)
{
    int particleIndex = (int)LinearIndex256(id);
    if (particleIndex >= ParticleCapacity || !IsParticleAlive(particleIndex)) return;
    if (ParticleYAxis[particleIndex].w < -0.5) return;

    int parentIndex = (int)round(ParticleDirection[particleIndex].w);
    if (!IsValidVoxelIndex(parentIndex)) return;

    // Excludes the particle itself, matching V3.
    int neighbourCount = max(0, (int)round(Source[parentIndex]) - 1);
    DepositFixed[ParticleDivisionNeighbourIndex(particleIndex)] = (uint)neighbourCount;
    uint age = DepositFixed[ParticleAgeIndex(particleIndex)];
    bool neighbourEligible = DivisionEnabled != 0 &&
                             age >= (uint)max(DivisionMinimumAge, 0) &&
                             neighbourCount >= DivisionMinimumNeighbours &&
                             neighbourCount <= DivisionMaximumNeighbours;

    bool divide = false;
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
        divide = UnitFromHash(divisionSeed) < RandomDivisionProbability;
    }

    if (!divide) return;

    uint childSlot;
    if (!TryClaimBirth(childSlot)) return;

    float4 parentPosition = ParticlePosition[particleIndex];
    float4 parentDirection = ParticleDirection[particleIndex];
    float4 parentYAxis = ParticleYAxis[particleIndex];
    int groupIndex = clamp((int)round(parentPosition.w), 0, max(GroupCount - 1, 0));
    float3 x = NormalizeOr(parentDirection.xyz, float3(1, 0, 0));
    float3 y = SafeYAxis(x, parentYAxis.xyz);
    float3 childBase = x;

    float splitAngle = GroupData1[groupIndex].x * 0.25;
    float splitSin;
    float splitCos;
    sincos(splitAngle, splitSin, splitCos);
    float3 parentX = NormalizeOr(RotateForce(2, childBase, y, splitCos, splitSin), x);
    float3 childX = NormalizeOr(RotateForce(0, childBase, y, splitCos, splitSin), x);
    float3 childY = SafeYAxis(childX, y);

    ParticleDirection[particleIndex] = float4(parentX, parentDirection.w);
    ParticleYAxis[particleIndex] = float4(SafeYAxis(parentX, y), parentYAxis.w);

    ParticlePosition[childSlot] = float4(parentPosition.xyz, (float)groupIndex);
    ParticleDirection[childSlot] = float4(childX, (float)parentIndex);
    ParticleYAxis[childSlot] = float4(childY, -1.0);
    ParticleHome[childSlot] = ParticleHome[particleIndex];
    DepositFixed[ParticleAgeIndex((int)childSlot)] = 0u;
    DepositFixed[ParticleDeathNeighbourIndex((int)childSlot)] = 0u;
    DepositFixed[ParticleDivisionNeighbourIndex((int)childSlot)] = 0u;
    DepositFixed[ParticleGenerationIndex((int)childSlot)] = DepositFixed[ParticleGenerationIndex((int)childSlot)] + 1u;
    DepositFixed[ParticleAntStateIndex((int)childSlot)] = DepositFixed[ParticleAntStateIndex(particleIndex)];

    uint ignoredVoxelCount;
    InterlockedAdd(ParticleCounts[parentIndex], 1u, ignoredVoxelCount);
    InterlockedAdd(DepositFixed[ParticleAgeIndex(particleIndex)], 1u, ignoredVoxelCount);

    uint ignoredGroupCount;
    InterlockedAdd(ParticleCounts[GroupPopulationIndex(groupIndex)], 1u, ignoredGroupCount);
}

float ClampPassDensity(float value, int index, int x, int y, int z)
{
    if (!IsValidVoxelIndex(index)) return 0.0;
    if (value > 1.0) value = 1.0;
    value = ClampDensityLimits(value, index);

    if (Wrap == 0 && IsBoundary(x, y, z) && value > 0.01)
    {
        value = 0.01;
    }

    return value;
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
    Destination[index] = ClampPassDensity(value, index, x, y, z);
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

    if (Wrap == 0 && IsBoundary(x, y, z))
    {
        Destination[index] = 0.0;
        return;
    }

    if (!IsValidVoxelIndex(index))
    {
        Destination[index] = 0.0;
        return;
    }

    float value = Source[index] - Decay;
    value = ClampDensityLimits(value, index);
    Destination[index] = value > 0.0 ? value : 0.0;
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
    float slime = HasSlimeParticles != 0 ? max(Source[index], 0.0) : 0.0;
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
    if (particleIndex >= ParticleCount) return;

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
    if (particleIndex >= ParticleCount) return;

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
