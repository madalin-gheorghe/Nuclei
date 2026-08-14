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

namespace Nuclei3
{
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

    internal sealed class GpuFullSlimeSolverEngine : IDisposable
    {
        const float DepositScale = 1024.0f;
        const int PreviewReadbackBufferCount = 3;
        const int MaxSharedPreviewTextureDimension = 16384;
        const long MaxSharedDensityPreviewTexturePixels = 33554432;
        const int MaxParticleTrailPreviewTexels = 33554432;
        const string SharedDensityPreviewStatusPath = @"C:\Nuclei\BenchmarkSuite1\NucleiGpuDensityFieldSource.txt";
        const string SharedParticlePreviewStatusPath = @"C:\Nuclei\BenchmarkSuite1\NucleiGpuParticlePreviewSource.txt";
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
        ID3D11ComputeShader moveShader;
        ID3D11ComputeShader applyDepositsShader;
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
        ID3D11Buffer voxelDensityLimitsBuffer;
        ID3D11Buffer parameterBuffer;
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
        readonly float[] densityReadback;
        readonly float[] antFoodReadback;
        readonly float[] antBaseReadback;
        readonly int[] antFoodRemainingReadback;
        readonly float[] particlePositionReadback;
        readonly float[] particleDirectionReadback;
        readonly float[] particleYAxisReadback;
        readonly float[] particlePositionPreviewReadback;
        readonly int[] particleAuxReadback;
        readonly int[] particleSlotGenerations;
        readonly int[] populationStateReadback = new int[4];
        readonly Particle[] particleSlots;
        readonly ParticleGroup[] particleGroups;
        Voxel[,,] staticPreviewVoxels;
        readonly bool[] previewReadbackPending = new bool[PreviewReadbackBufferCount];
        readonly int[] previewReadbackSequences = new int[PreviewReadbackBufferCount];

        bool densityInA = true;
        bool antFoodInA = true;
        bool antBaseInA = true;
        readonly bool hasAntParticles;
        readonly bool hasSlimeParticles;
        int weightsRange = int.MinValue;
        int antWeightsRange = int.MinValue;
        int previewReadbackNextIndex = 0;
        int previewReadbackSequenceCounter = 0;
        int previewReadbackCompletedSequence = 0;
        IntPtr densityPreviewSharedHandle = IntPtr.Zero;
        IntPtr densityGradientPreviewSharedHandle = IntPtr.Zero;
        int densityPreviewWidth;
        int densityPreviewHeight;
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
        float[] particleTrailPreviewGroupColorData;
        long particleTrailPreviewVersion = 0;

        public GpuFullSlimeSolverEngine(SolverGpuInputSnapshot snapshot, SolverGpuSettings settings, bool enableSharedDensityPreview, bool enableSharedParticlePreview, bool enableSharedParticleTrailPreview, int particleTrailSize, int densityPreviewScale)
        {
            if (snapshot == null)
            {
                throw new ArgumentNullException(nameof(snapshot));
            }

            resX = snapshot.ResX;
            resY = snapshot.ResY;
            resZ = snapshot.ResZ;
            voxelCount = Math.Max(0, resX * resY * resZ);
            particleCount = Math.Max(0, snapshot.ParticleCount);
            int requestedCapacity = settings != null && settings.DynamicPopulation
                ? settings.MaximumPopulation
                : particleCount;
            particleCapacity = Math.Max(particleCount, Math.Max(0, requestedCapacity));
            groupCount = Math.Max(0, snapshot.GroupCount);
            hasAntParticles = snapshot.HasAntParticles;
            hasSlimeParticles = snapshot.HasSlimeParticles;
            particleGroups = snapshot.ParticleGroups ?? new ParticleGroup[groupCount];
            particleSlots = new Particle[particleCapacity];
            int snapshotParticleObjectCount = snapshot.Particles != null ? snapshot.Particles.Count : 0;
            for (int i = 0; i < particleCount && i < snapshotParticleObjectCount; i++)
            {
                particleSlots[i] = snapshot.Particles[i];
            }
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

            if (voxelCount <= 0)
            {
                throw new ArgumentException("GPU solver requires at least one voxel.");
            }

            densityReadback = hasSlimeParticles ? new float[voxelCount] : new float[0];
            antFoodReadback = hasAntParticles ? new float[voxelCount] : new float[0];
            antBaseReadback = hasAntParticles ? new float[voxelCount] : new float[0];
            antFoodRemainingReadback = hasAntParticles ? new int[voxelCount] : new int[0];
            particlePositionReadback = new float[particleCapacity * 4];
            particleDirectionReadback = new float[particleCapacity * 4];
            particleYAxisReadback = new float[particleCapacity * 4];
            particlePositionPreviewReadback = new float[particleCapacity * 4];
            particleAuxReadback = new int[particleCapacity * 5];
            particleSlotGenerations = new int[particleCapacity];
            staticPreviewVoxels = snapshot.Voxels;

            CreateDevice(out device, out context);
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

        public bool CanFastReset(SolverGpuInputSnapshot snapshot, SolverGpuSettings settings)
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
                && SupportsPopulationCapacity(settings);
        }

        public void FastReset(SolverGpuInputSnapshot snapshot, SolverGpuSettings settings)
        {
            if (!CanFastReset(snapshot, settings))
            {
                throw new InvalidOperationException("GPU solver state is not compatible with fast reset.");
            }

            UnbindComputeResources();
            particleCount = snapshot.ParticleCount;
            staticPreviewVoxels = snapshot.Voxels;

            Array.Clear(particleSlots, 0, particleSlots.Length);
            int snapshotParticleCount = snapshot.Particles != null ? snapshot.Particles.Count : 0;
            for (int i = 0; i < particleCount && i < snapshotParticleCount; i++)
            {
                particleSlots[i] = snapshot.Particles[i];
            }
            if (snapshot.ParticleGroups == null || snapshot.ParticleGroups.Length != groupCount)
            {
                throw new InvalidOperationException("GPU reset particle groups do not match the existing solver.");
            }
            for (int i = 0; i < groupCount; i++)
            {
                particleGroups[i] = snapshot.ParticleGroups[i];
            }

            if (hasSlimeParticles)
            {
                float[] initialDensity = snapshot.VoxelDensity != null && snapshot.VoxelDensity.Length == voxelCount
                    ? snapshot.VoxelDensity
                    : new float[voxelCount];
                context.UpdateSubresourceSafe(initialDensity, densityA, 0, 0, 0, 0, false);
                context.UpdateSubresourceSafe(initialDensity, densityB, 0, 0, 0, 0, false);
                densityInA = true;
            }

            if (hasAntParticles)
            {
                float[] initialFoodPheromone = snapshot.AntFoodPheromone != null && snapshot.AntFoodPheromone.Length == voxelCount
                    ? snapshot.AntFoodPheromone
                    : new float[voxelCount];
                float[] initialBasePheromone = snapshot.AntBasePheromone != null && snapshot.AntBasePheromone.Length == voxelCount
                    ? snapshot.AntBasePheromone
                    : new float[voxelCount];
                context.UpdateSubresourceSafe(initialFoodPheromone, antFoodA, 0, 0, 0, 0, false);
                context.UpdateSubresourceSafe(initialFoodPheromone, antFoodB, 0, 0, 0, 0, false);
                context.UpdateSubresourceSafe(initialBasePheromone, antBaseA, 0, 0, 0, 0, false);
                context.UpdateSubresourceSafe(initialBasePheromone, antBaseB, 0, 0, 0, 0, false);
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

            uint[] particleCounts;
            uint[] depositAndParticleAux;
            BuildAuxiliaryState(snapshot, out particleCounts, out depositAndParticleAux);
            context.UpdateSubresourceSafe(particleCounts, particleCountBuffer, 0, 0, 0, 0, false);
            context.UpdateSubresourceSafe(depositAndParticleAux, depositBuffer, 0, 0, 0, 0, false);

            float[] zeroVoxelState = new float[voxelCount];
            context.UpdateSubresourceSafe(zeroVoxelState, neighbourCountA, 0, 0, 0, 0, false);
            context.UpdateSubresourceSafe(zeroVoxelState, neighbourCountB, 0, 0, 0, 0, false);

            if (!UpdateGroupSettings(snapshot.GroupData0, snapshot.GroupData1, snapshot.GroupColorData)
                || !UpdateVoxelBehaviorFields(snapshot))
            {
                throw new InvalidOperationException("GPU reset could not restore group or voxel fields.");
            }

            Array.Clear(densityReadback, 0, densityReadback.Length);
            Array.Clear(antFoodReadback, 0, antFoodReadback.Length);
            Array.Clear(antBaseReadback, 0, antBaseReadback.Length);
            Array.Clear(antFoodRemainingReadback, 0, antFoodRemainingReadback.Length);
            Array.Clear(particlePositionReadback, 0, particlePositionReadback.Length);
            Array.Clear(particleDirectionReadback, 0, particleDirectionReadback.Length);
            Array.Clear(particleYAxisReadback, 0, particleYAxisReadback.Length);
            Array.Clear(particlePositionPreviewReadback, 0, particlePositionPreviewReadback.Length);
            Array.Clear(particleAuxReadback, 0, particleAuxReadback.Length);
            Array.Clear(particleSlotGenerations, 0, particleSlotGenerations.Length);
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
            if (wasEnabled != enableSharedParticleTrailPreview)
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

        public GpuFullSolverStepResult Step(
            Voxel[,,] voxels,
            ParticleList particles,
            SolverGpuSettings settings,
            SolverGpuDimensionMode dimensionMode,
            int iteration,
            bool syncVoxels,
            bool syncParticleState,
            bool buildPreviewCache)
        {
            Stopwatch total = Stopwatch.StartNew();
            Stopwatch stage = Stopwatch.StartNew();
            int passCount = 0;
            bool movedParticles = false;

            if (hasSlimeParticles)
            {
                EnsureWeights(settings.DiffuseRange);
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
            if (hasSlimeParticles && settings.Diffuse > 0)
            {
                int axisCount = GetDiffusionAxisOrder(dimensionMode, iteration, diffusionAxisScratch);
                for (int i = 0; i < axisCount; i++)
                {
                    DispatchDiffusionPass(diffusionAxisScratch[i], settings, dimensionMode, iteration);
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
                if (hasSlimeParticles) ApplyDensityToVoxels(voxels);
                if (hasAntParticles) ApplyAntFieldsToVoxels(voxels);
            }

            bool builtPreviewCache = false;
            bool completedPreviewReadback = false;
            bool queuedPreviewReadback = false;
            if (syncParticleState)
            {
                ClearPendingPreviewReadbacks();
                ReadBackParticles();
                builtPreviewCache = ApplyParticlesToOutput(particles, voxels, settings, iteration, buildPreviewCache);
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
            context.Dispatch(DispatchGroupCount(particleCapacity), 1, 1);
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
            context.Dispatch(DispatchGroupCount(voxelCount), 1, 1);
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
            context.Dispatch(DispatchGroupCount(Math.Max(voxelCount, groupCount)), 1, 1);
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
            context.Dispatch(DispatchGroupCount(particleCapacity), 1, 1);
            UnbindComputeResources();
        }

        void DispatchDynamicPopulation(SolverGpuSettings settings, SolverGpuDimensionMode dimensionMode, int iteration)
        {
            if (particleCapacity <= 0 || particleCountView == null || depositView == null)
            {
                return;
            }

            if (settings.Death && iteration % settings.DeathFrequency == 0)
            {
                ID3D11UnorderedAccessView neighbourView = DispatchBuildNeighbourCounts(settings.DeathRange, settings, dimensionMode, iteration);
                DispatchParticleDeath(neighbourView, settings, dimensionMode, iteration);
            }

            if (settings.Division && iteration % settings.DivisionFrequency == 0)
            {
                ID3D11UnorderedAccessView neighbourView = DispatchBuildNeighbourCounts(settings.DivisionRange, settings, dimensionMode, iteration);
                DispatchParticleDivision(neighbourView, settings, dimensionMode, iteration);
            }
        }

        ID3D11UnorderedAccessView DispatchBuildNeighbourCounts(int range, SolverGpuSettings settings, SolverGpuDimensionMode dimensionMode, int iteration)
        {
            FullSolverParameters parameters = CreateParameters(0, settings, dimensionMode, iteration);
            parameters.Range = Math.Max(0, range);
            UpdateParameters(parameters);

            context.CSSetShader(seedNeighbourCountsShader);
            context.CSSetConstantBuffers(0, new ID3D11Buffer[] { parameterBuffer });
            context.CSSetUnorderedAccessView(1, neighbourCountAView, -1);
            context.CSSetUnorderedAccessView(5, particleCountView, -1);
            context.Dispatch(DispatchGroupCount(voxelCount), 1, 1);
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
                context.Dispatch(DispatchLineGroupCount(NeighbourLineCount(axis)), 1, 1);
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
            context.Dispatch(DispatchGroupCount(particleCapacity), 1, 1);
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
            context.Dispatch(DispatchGroupCount(particleCapacity), 1, 1);
            UnbindComputeResources();
        }

        void DispatchDiffusionPass(int axis, SolverGpuSettings settings, SolverGpuDimensionMode dimensionMode, int iteration)
        {
            UpdateParameters(CreateParameters(axis, settings, dimensionMode, iteration));

            context.CSSetShader(diffusionShader);
            context.CSSetConstantBuffers(0, new ID3D11Buffer[] { parameterBuffer });
            context.CSSetShaderResources(0, new ID3D11ShaderResourceView[] { weightsView });
            context.CSSetShaderResource(3, voxelFlagsView);
            context.CSSetShaderResource(7, voxelDensityLimitsView);
            context.CSSetUnorderedAccessView(0, CurrentDensityView(), -1);
            context.CSSetUnorderedAccessView(1, NextDensityView(), -1);
            context.Dispatch(DispatchGroupCount(voxelCount), 1, 1);
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
            context.Dispatch(DispatchGroupCount(voxelCount), 1, 1);
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
                    context.Dispatch(DispatchGroupCount(voxelCount), 1, 1);
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
            context.Dispatch(DispatchGroupCount(voxelCount), 1, 1);
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
            context.Dispatch(DispatchGroupCount(particleCapacity), 1, 1);
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

            int trailSize = ClampTrailPreviewSizeForParticleCount(settings.TrailSize);
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
                context.Dispatch(DispatchGroupCount(particleCapacity), 1, 1);
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
            parameters.PreviewSlice = densityPreviewSlice;
            parameters.PreviewAtlasColumns = densityPreviewAtlasColumns;
            parameters.PreviewAtlasRows = densityPreviewAtlasRows;
            parameters.PreviewPadding0 = 0;
            parameters.PreviewPadding1 = 0;
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

        public bool UpdateVoxelBehaviorFields(SolverGpuInputSnapshot snapshot)
        {
            if (snapshot == null
                || snapshot.VoxelFlags == null
                || snapshot.VoxelBehaviorData == null
                || snapshot.VoxelVectorData == null
                || snapshot.VoxelDensityLimits == null
                || snapshot.VoxelFlags.Length != voxelCount
                || snapshot.VoxelBehaviorData.Length != voxelCount * 4
                || snapshot.VoxelVectorData.Length != voxelCount * 4
                || snapshot.VoxelDensityLimits.Length != voxelCount * 4)
            {
                return false;
            }

            if (voxelFlagsBuffer == null
                || voxelBehaviorBuffer == null
                || voxelVectorBuffer == null
                || voxelDensityLimitsBuffer == null)
            {
                return false;
            }

            staticPreviewVoxels = snapshot.Voxels;
            context.UpdateSubresourceSafe(snapshot.VoxelFlags, voxelFlagsBuffer, 0, 0, 0, 0, false);
            context.UpdateSubresourceSafe(snapshot.VoxelBehaviorData, voxelBehaviorBuffer, 0, 0, 0, 0, false);
            context.UpdateSubresourceSafe(snapshot.VoxelVectorData, voxelVectorBuffer, 0, 0, 0, 0, false);
            context.UpdateSubresourceSafe(snapshot.VoxelDensityLimits, voxelDensityLimitsBuffer, 0, 0, 0, 0, false);
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
                ResX = resX,
                ResY = resY,
                ResZ = resZ,
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
            if (VoxelPreviewField.IsDynamicDensity(valueIndex))
            {
                if (valueIndex == VoxelPreviewField.SlimeChemoattractants && !hasSlimeParticles)
                {
                    return null;
                }
                int normalizedScale = NormalizeDensityPreviewScale(previewScale);
                if ((valueIndex == VoxelPreviewField.AntFoodPheromones
                    || valueIndex == VoxelPreviewField.AntBasePheromones
                    || VoxelPreviewField.IsCombinedDynamicDensity(valueIndex)) && !hasAntParticles)
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
                if (!enableSharedDensityPreview || densityPreviewScale != normalizedScale || densityPreviewTexture == null)
                {
                    SetSharedDensityPreviewEnabled(true, dimensionMode, normalizedScale);
                }
                if (valueIndex != densityPreviewValueIndex)
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
                ResX = resX,
                ResY = resY,
                ResZ = resZ,
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

        void ApplyDensityToVoxels(Voxel[,,] voxels)
        {
            if (voxels == null)
            {
                return;
            }

            for (int x = 0; x < resX; x++)
            {
                int xBase = x * resY * resZ;
                for (int y = 0; y < resY; y++)
                {
                    int baseIndex = xBase + y * resZ;
                    for (int z = 0; z < resZ; z++)
                    {
                        Voxel voxel = voxels[x, y, z];
                        if (voxel != null)
                        {
                            voxel.density = densityReadback[baseIndex + z];
                        }
                    }
                }
            }
        }

        void ReadBackAntFields()
        {
            ReadBackFloatBuffer(antFoodReadbackBuffer, CurrentAntFoodBuffer(), antFoodReadback);
            ReadBackFloatBuffer(antBaseReadbackBuffer, CurrentAntBaseBuffer(), antBaseReadback);

            int sourceOffset = voxelCount * 3 * sizeof(uint);
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

        void ApplyAntFieldsToVoxels(Voxel[,,] voxels)
        {
            if (voxels == null) return;
            for (int x = 0; x < resX; x++)
            {
                for (int y = 0; y < resY; y++)
                {
                    int baseIndex = x * resY * resZ + y * resZ;
                    for (int z = 0; z < resZ; z++)
                    {
                        Voxel voxel = voxels[x, y, z];
                        if (voxel == null) continue;
                        int index = baseIndex + z;
                        voxel.towardsFoodPheromone = antFoodReadback[index];
                        voxel.towardsBasePheromone = antBaseReadback[index];
                        voxel.food = antFoodRemainingReadback[index] / DepositScale;
                    }
                }
            }
        }

        void ReadBackParticles()
        {
            if (particleCapacity <= 0)
            {
                return;
            }

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

        public bool TryCompletePreviewCache(ParticleList particles)
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
                return BuildPreviewCacheFromPositions(particles, particlePositionPreviewReadback);
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
            if (particleAuxReadbackBuffer == null || depositBuffer == null || particleAuxReadback.Length == 0)
            {
                return;
            }

            int sourceOffset = (voxelCount * 4 + particleCapacity) * sizeof(uint);
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

        bool ApplyParticlesToOutput(ParticleList particles, Voxel[,,] voxels, SolverGpuSettings settings, int iteration, bool buildPreviewCache)
        {
            if (particles == null || particleCapacity <= 0)
            {
                return false;
            }

            ParticlePreviewCache previewCache = buildPreviewCache ? particles.PreviewCache : null;
            ParticlePreviewBuildCache previewBuildCache = previewCache != null ? new ParticlePreviewBuildCache(particleCount) : null;
            if (previewCache != null)
            {
                previewCache.BeginBuild(particleCount);
            }

            particles.Clear();
            for (int groupIndex = 0; groupIndex < particleGroups.Length; groupIndex++)
            {
                ParticleGroup group = particleGroups[groupIndex];
                if (group != null && group.particles != null)
                {
                    group.particles.Clear();
                }
            }
            int activeCount = 0;
            for (int i = 0; i < particleCapacity; i++)
            {
                int offset = i * 4;
                int groupIndex = (int)Math.Round(particlePositionReadback[offset + 3]);
                if (groupIndex < 0 || groupIndex >= groupCount)
                {
                    Particle deadParticle = particleSlots[i];
                    if (deadParticle != null && deadParticle.trails != null)
                    {
                        deadParticle.trails.Clear();
                    }
                    particleSlots[i] = null;
                    continue;
                }

                Particle particle = particleSlots[i];
                int generation = particleAuxReadback[particleCapacity * 3 + i];
                if (particle == null)
                {
                    particle = new Particle();
                    particleSlots[i] = particle;
                }
                else if (particleSlotGenerations[i] != generation && particle.trails != null)
                {
                    particle.trails.Clear();
                }
                particleSlotGenerations[i] = generation;
                particle.parentParticleGroup = groupIndex < particleGroups.Length ? particleGroups[groupIndex] : null;
                particle.age = particleAuxReadback[i];
                particle.foundFood = particle.parentParticleGroup != null
                    && particle.parentParticleGroup.ant
                    && particleAuxReadback[particleCapacity * 4 + i] != 0;

                Point3d origin = new Point3d(
                    particlePositionReadback[offset],
                    particlePositionReadback[offset + 1],
                    particlePositionReadback[offset + 2]);

                Vector3d xAxis = new Vector3d(
                    particleDirectionReadback[offset],
                    particleDirectionReadback[offset + 1],
                    particleDirectionReadback[offset + 2]);

                Vector3d yAxis = new Vector3d(
                    particleYAxisReadback[offset],
                    particleYAxisReadback[offset + 1],
                    particleYAxisReadback[offset + 2]);

                if (!xAxis.Unitize())
                {
                    xAxis = new Vector3d(1, 0, 0);
                }

                yAxis = OrthonormalYAxis(xAxis, yAxis);
                particle.pPlane = new Plane(origin, xAxis, yAxis);

                int parentIndex = (int)Math.Round(particleDirectionReadback[offset + 3]);
                particle.parentVoxel = VoxelFromFlatIndex(voxels, parentIndex);
                particle.age = particleAuxReadback[i];
                particle.neighbourCount_Die = particleAuxReadback[particleCapacity + i];
                particle.neighbourCount_Div = particleAuxReadback[particleCapacity * 2 + i];

                if (particleYAxisReadback[offset + 3] > 0.5f)
                {
                    particle.trails.Clear();
                }

                if (previewBuildCache != null)
                {
                    previewBuildCache.AddParticle(particle);
                }

                particles.Add(particle);
                if (particle.parentParticleGroup != null && particle.parentParticleGroup.particles != null)
                {
                    particle.parentParticleGroup.particles.Add(particle);
                }
                activeCount++;
            }

            particleCount = activeCount;

            RecordTrails(particles, settings, iteration);

            if (previewCache != null)
            {
                previewCache.Merge(previewBuildCache);
                previewCache.CompleteBuild();
                return true;
            }

            particles.PreviewCache.Invalidate(activeCount);
            return false;
        }

        bool BuildPreviewCacheFromPositions(ParticleList particles)
        {
            return BuildPreviewCacheFromPositions(particles, particlePositionReadback);
        }

        bool BuildPreviewCacheFromPositions(ParticleList particles, float[] positionReadback)
        {
            if (particles == null || particleCapacity <= 0)
            {
                return false;
            }

            ParticlePreviewCache previewCache = particles.PreviewCache;
            ParticlePreviewBuildCache previewBuildCache = new ParticlePreviewBuildCache(particleCount);
            previewCache.BeginBuild(particleCount);

            int activeCount = 0;
            for (int i = 0; i < particleCapacity; i++)
            {
                int offset = i * 4;
                int groupIndex = (int)Math.Round(positionReadback[offset + 3]);
                if (groupIndex < 0 || groupIndex >= groupCount)
                {
                    continue;
                }

                Particle particle = particleSlots[i];
                if (particle == null)
                {
                    particle = new Particle();
                    particle.parentParticleGroup = groupIndex < particleGroups.Length ? particleGroups[groupIndex] : null;
                    particleSlots[i] = particle;
                }

                Point3d origin = new Point3d(
                    positionReadback[offset],
                    positionReadback[offset + 1],
                    positionReadback[offset + 2]);

                previewBuildCache.AddParticlePoint(particle, origin);
                activeCount++;
            }

            previewCache.Merge(previewBuildCache);
            previewCache.CompleteBuild();
            previewCache.ParticleCount = activeCount;
            return true;
        }

        void RecordTrails(ParticleList particles, SolverGpuSettings settings, int iteration)
        {
            if (particles == null)
            {
                return;
            }

            bool sampleTrail = settings.TrailFreq <= 1 || iteration % settings.TrailFreq == 0;
            for (int i = 0; i < particles.Count; i++)
            {
                Particle particle = particles[i];
                if (particle == null || particle.parentVoxel == null)
                {
                    continue;
                }

                if (settings.TrailSize <= 1)
                {
                    if (particle.trails.Count > 0)
                    {
                        particle.trails.Clear();
                    }

                    continue;
                }

                if (particle.trails.Capacity < settings.TrailSize)
                {
                    particle.trails.Capacity = settings.TrailSize;
                }

                Point3d origin = particle.pPlane.Origin;
                if (sampleTrail)
                {
                    if (particle.trails.Count > 0)
                    {
                        particle.trails.Insert(0, origin);
                    }
                    else
                    {
                        particle.trails.Add(origin);
                    }

                    if (particle.trails.Count > settings.TrailSize)
                    {
                        particle.trails.RemoveAt(particle.trails.Count - 1);
                    }
                }
                else if (particle.trails.Count > 0)
                {
                    particle.trails[0] = origin;
                }
                else
                {
                    particle.trails.Add(origin);
                }
            }
        }

        Voxel VoxelFromFlatIndex(Voxel[,,] voxels, int index)
        {
            if (voxels == null || index < 0 || index >= voxelCount)
            {
                return null;
            }

            int yz = resY * resZ;
            int x = index / yz;
            int rem = index - x * yz;
            int y = rem / resZ;
            int z = rem - y * resZ;

            if (x < 0 || x >= resX || y < 0 || y >= resY || z < 0 || z >= resZ)
            {
                return null;
            }

            return voxels[x, y, z];
        }

        int FlatIndex(int x, int y, int z)
        {
            return x * resY * resZ + y * resZ + z;
        }

        static Vector3d OrthonormalYAxis(Vector3d xAxis, Vector3d yAxis)
        {
            if (!yAxis.Unitize())
            {
                yAxis = Math.Abs(xAxis.Z) < 0.9
                    ? Vector3d.CrossProduct(Vector3d.ZAxis, xAxis)
                    : Vector3d.CrossProduct(Vector3d.YAxis, xAxis);
            }

            double dot = xAxis.X * yAxis.X + xAxis.Y * yAxis.Y + xAxis.Z * yAxis.Z;
            yAxis -= xAxis * dot;

            if (!yAxis.Unitize())
            {
                yAxis = Math.Abs(xAxis.Z) < 0.9
                    ? Vector3d.CrossProduct(Vector3d.ZAxis, xAxis)
                    : Vector3d.CrossProduct(Vector3d.YAxis, xAxis);
                yAxis.Unitize();
            }

            return yAxis;
        }

        void EnsureWeights(int range)
        {
            if (weightsView != null && weightsRange == range)
            {
                return;
            }

            DisposeWeights();

            float[] weights = PrecomputeWeights(range);
            weightsRange = range;

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
            float[] weights = PrecomputeWeights(range);
            antWeightsRange = range;
            antWeightsBuffer = device.CreateBuffer(weights, BindFlags.ShaderResource, ResourceUsage.Default,
                CpuAccessFlags.None, ResourceOptionFlags.BufferStructured, weights.Length * sizeof(float), sizeof(float));
            antWeightsView = device.CreateShaderResourceView(
                antWeightsBuffer,
                new ShaderResourceViewDescription(antWeightsBuffer, Format.Unknown, 0, weights.Length, BufferExtendedShaderResourceViewFlags.None));
        }

        static float[] PrecomputeWeights(int range)
        {
            int total = (range + 1) * 2 + 1;
            float[] weights = new float[total - 2];
            double weightSum = 0;
            double[] fullWeights = new double[total];

            for (int i = 0; i < total; i++)
            {
                double n = Math.PI * (i - (range + 1)) / (range + 1);
                double weight = (1 + Math.Cos(n)) / 2;
                fullWeights[i] = weight;
                weightSum += weight;
            }

            for (int i = 1; i < total - 1; i++)
            {
                weights[i - 1] = (float)(fullWeights[i] / weightSum);
            }

            return weights;
        }

        void CreateDensityBuffers(float[] initialDensity)
        {
            float[] sourceDensity = initialDensity != null && initialDensity.Length == voxelCount
                ? initialDensity
                : new float[voxelCount];

            densityA = device.CreateBuffer(
                sourceDensity,
                BindFlags.UnorderedAccess,
                ResourceUsage.Default,
                CpuAccessFlags.None,
                ResourceOptionFlags.BufferStructured,
                voxelCount * sizeof(float),
                sizeof(float));

            densityB = device.CreateBuffer(
                voxelCount * sizeof(float),
                BindFlags.UnorderedAccess,
                ResourceUsage.Default,
                CpuAccessFlags.None,
                ResourceOptionFlags.BufferStructured,
                sizeof(float));

            densityReadbackBuffer = device.CreateBuffer(
                voxelCount * sizeof(float),
                BindFlags.None,
                ResourceUsage.Staging,
                CpuAccessFlags.Read,
                ResourceOptionFlags.None,
                0);

            densityAView = device.CreateUnorderedAccessView(
                densityA,
                new UnorderedAccessViewDescription(densityA, Format.Unknown, 0, voxelCount, BufferUnorderedAccessViewFlags.None));

            densityBView = device.CreateUnorderedAccessView(
                densityB,
                new UnorderedAccessViewDescription(densityB, Format.Unknown, 0, voxelCount, BufferUnorderedAccessViewFlags.None));
        }

        void CreateAntFieldBuffers(float[] initialFoodPheromone, float[] initialBasePheromone)
        {
            float[] food = initialFoodPheromone != null && initialFoodPheromone.Length == voxelCount
                ? initialFoodPheromone
                : new float[voxelCount];
            float[] basePheromone = initialBasePheromone != null && initialBasePheromone.Length == voxelCount
                ? initialBasePheromone
                : new float[voxelCount];

            CreateAntFieldPair(food, out antFoodA, out antFoodB, out antFoodAView, out antFoodBView, out antFoodAResourceView, out antFoodBResourceView);
            CreateAntFieldPair(basePheromone, out antBaseA, out antBaseB, out antBaseAView, out antBaseBView, out antBaseAResourceView, out antBaseBResourceView);
            antFoodReadbackBuffer = CreateReadbackBuffer(voxelCount * sizeof(float));
            antBaseReadbackBuffer = CreateReadbackBuffer(voxelCount * sizeof(float));
            antFoodRemainingReadbackBuffer = CreateReadbackBuffer(voxelCount * sizeof(uint));
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
            bufferA = device.CreateBuffer(initial, bind, ResourceUsage.Default, CpuAccessFlags.None,
                ResourceOptionFlags.BufferStructured, voxelCount * sizeof(float), sizeof(float));
            bufferB = device.CreateBuffer(voxelCount * sizeof(float), bind, ResourceUsage.Default, CpuAccessFlags.None,
                ResourceOptionFlags.BufferStructured, sizeof(float));
            viewA = CreateUav(bufferA, voxelCount);
            viewB = CreateUav(bufferB, voxelCount);
            resourceA = CreateSrv(bufferA, voxelCount);
            resourceB = CreateSrv(bufferB, voxelCount);
        }

        bool EnsureStaticFieldPreviewTexture(int fieldIndex, SolverGpuDimensionMode dimensionMode)
        {
            if (!VoxelPreviewField.IsStatic(fieldIndex))
            {
                return false;
            }

            if (staticPreviewVoxels == null)
            {
                return false;
            }

            int width;
            int height;
            int axisMode;
            int slice;
            int atlasColumns;
            int atlasRows;
            ResolveDensityPreviewLayout(dimensionMode, out width, out height, out axisMode, out slice, out atlasColumns, out atlasRows);
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
                + " atlas=" + atlasColumns + "x" + atlasRows);

            float[] previewValues = BuildStaticFieldPreviewValues(fieldIndex, width, height, axisMode, slice, atlasColumns, atlasRows);

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

        float[] BuildStaticFieldPreviewValues(int fieldIndex, int width, int height, int axisMode, int slice, int atlasColumns, int atlasRows)
        {
            float[] previewValues = new float[width * height];
            atlasColumns = Math.Max(1, atlasColumns);
            atlasRows = Math.Max(1, atlasRows);

            for (int v = 0; v < height; v++)
            {
                for (int u = 0; u < width; u++)
                {
                    int x = u;
                    int y = v;
                    int z = slice;

                    if (axisMode == 3)
                    {
                        int tileX = u / Math.Max(1, resX);
                        int tileY = v / Math.Max(1, resY);
                        z = tileY * atlasColumns + tileX;
                        if (z >= resZ || tileY >= atlasRows)
                        {
                            previewValues[v * width + u] = 0;
                            continue;
                        }

                        x = u - tileX * Math.Max(1, resX);
                        y = v - tileY * Math.Max(1, resY);
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

                    Voxel voxel = staticPreviewVoxels[x, y, z];
                    previewValues[v * width + u] = StaticVoxelFieldValue(voxel, fieldIndex);
                }
            }

            return previewValues;
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
            if (staticPreviewVoxels == null)
            {
                return 1.0f;
            }

            int width = staticFieldPreviewWidths[fieldIndex];
            int height = staticFieldPreviewHeights[fieldIndex];
            int axisMode = staticFieldPreviewAxisModes[fieldIndex];
            int slice = staticFieldPreviewSlices[fieldIndex];
            float maximum = 0;

            for (int v = 0; v < height; v++)
            {
                for (int u = 0; u < width; u++)
                {
                    int x;
                    int y;
                    int z;
                    StaticPreviewCoordinates(u, v, axisMode, slice, out x, out y, out z);

                    Voxel voxel = staticPreviewVoxels[x, y, z];
                    float value = StaticVoxelFieldValue(voxel, fieldIndex);
                    if (value > 0.01f && value >= minimumThreshold && value <= maximumThreshold && value > maximum)
                    {
                        maximum = value;
                    }
                }
            }

            return maximum > 0.0001f ? maximum : 1.0f;
        }

        static float StaticVoxelFieldValue(Voxel voxel, int fieldIndex)
        {
            if (voxel == null) return 0;

            double value = 0;
            if (fieldIndex == VoxelPreviewField.MinimumDensity) value = voxel.minDensity;
            else if (fieldIndex == VoxelPreviewField.MaximumDensity) value = voxel.maxDensity;
            else if (fieldIndex == VoxelPreviewField.Speed) value = voxel.speedMultiplier;
            else if (fieldIndex == VoxelPreviewField.SensorDistance) value = voxel.sensorDistanceMultiplier;
            else if (fieldIndex == VoxelPreviewField.SensorAngle) value = voxel.sensorAngleMultiplier;
            else if (fieldIndex == VoxelPreviewField.RotationAngle) value = voxel.rotationAngleMultiplier;
            else if (fieldIndex == VoxelPreviewField.Food) value = voxel.food;

            if (double.IsNaN(value) || double.IsInfinity(value) || value < 0) return 0;
            return (float)value;
        }

        void StaticPreviewCoordinates(int u, int v, int axisMode, int slice, out int x, out int y, out int z)
        {
            x = u;
            y = v;
            z = slice;

            if (axisMode == 1)
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
            out int axisMode,
            out int slice,
            out int atlasColumns,
            out int atlasRows)
        {
            atlasColumns = 1;
            atlasRows = 1;

            if (dimensionMode.Tridimensional)
            {
                ResolveVolumeAtlasLayout(out atlasColumns, out atlasRows);
                width = Math.Max(1, resX) * atlasColumns;
                height = Math.Max(1, resY) * atlasRows;
                axisMode = 3;
                slice = 0;

                if (width > MaxSharedPreviewTextureDimension || height > MaxSharedPreviewTextureDimension)
                {
                    throw new InvalidOperationException("3D voxel preview atlas would exceed Direct3D texture limits.");
                }

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

        void ResolveVolumeAtlasLayout(out int columns, out int rows)
        {
            int sliceCount = Math.Max(1, resZ);
            int sliceWidth = Math.Max(1, resX);
            int sliceHeight = Math.Max(1, resY);
            double targetColumns = Math.Sqrt((double)sliceCount * sliceHeight / sliceWidth);
            columns = Math.Max(1, (int)Math.Ceiling(targetColumns));
            rows = (sliceCount + columns - 1) / columns;

            while (columns > 1 && columns * sliceWidth > MaxSharedPreviewTextureDimension)
            {
                columns--;
                rows = (sliceCount + columns - 1) / columns;
            }

            while (rows * sliceHeight > MaxSharedPreviewTextureDimension)
            {
                columns++;
                rows = (sliceCount + columns - 1) / columns;
                if (columns * sliceWidth > MaxSharedPreviewTextureDimension)
                {
                    throw new InvalidOperationException("3D voxel preview atlas would exceed Direct3D texture limits.");
                }
            }
        }

        void CreateParticleBuffers(SolverGpuInputSnapshot snapshot)
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
            particlePositionReadbackBuffer = CreateReadbackBuffer(positions.Length * sizeof(float));
            particleDirectionReadbackBuffer = CreateReadbackBuffer(directions.Length * sizeof(float));
            particleYAxisReadbackBuffer = CreateReadbackBuffer(yAxes.Length * sizeof(float));
            particleAuxReadbackBuffer = CreateReadbackBuffer(Math.Max(sizeof(uint), particleAuxReadback.Length * sizeof(uint)));
            populationStateReadbackBuffer = CreateReadbackBuffer(4 * sizeof(uint));
            for (int i = 0; i < particlePositionPreviewReadbackBuffers.Length; i++)
            {
                particlePositionPreviewReadbackBuffers[i] = CreateReadbackBuffer(positions.Length * sizeof(float));
            }

            particlePositionView = CreateUav(particlePositionBuffer, particleCapacity);
            particleDirectionView = CreateUav(particleDirectionBuffer, particleCapacity);
            particleYAxisView = CreateUav(particleYAxisBuffer, particleCapacity);
            particleHomeView = CreateUav(particleHomeBuffer, particleCapacity);
        }

        void BuildParticleBufferData(
            SolverGpuInputSnapshot snapshot,
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

        void CreateGroupBuffers(SolverGpuInputSnapshot snapshot)
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

        void CreateVoxelFlagBuffer(SolverGpuInputSnapshot snapshot)
        {
            uint[] flags = snapshot.VoxelFlags;
            uint[] source = flags != null && flags.Length == voxelCount ? flags : new uint[voxelCount];

            voxelFlagsBuffer = device.CreateBuffer(
                source,
                BindFlags.ShaderResource,
                ResourceUsage.Default,
                CpuAccessFlags.None,
                ResourceOptionFlags.BufferStructured,
                voxelCount * sizeof(uint),
                sizeof(uint));

            voxelFlagsView = device.CreateShaderResourceView(
                voxelFlagsBuffer,
                new ShaderResourceViewDescription(voxelFlagsBuffer, Format.Unknown, 0, voxelCount, BufferExtendedShaderResourceViewFlags.None));

            uint[] particleCounts;
            uint[] depositAndParticleAux;
            BuildAuxiliaryState(snapshot, out particleCounts, out depositAndParticleAux);

            particleCountBuffer = device.CreateBuffer(
                particleCounts,
                BindFlags.UnorderedAccess,
                ResourceUsage.Default,
                CpuAccessFlags.None,
                ResourceOptionFlags.BufferStructured,
                particleCounts.Length * sizeof(uint),
                sizeof(uint));

            depositBuffer = device.CreateBuffer(
                depositAndParticleAux,
                BindFlags.UnorderedAccess,
                ResourceUsage.Default,
                CpuAccessFlags.None,
                ResourceOptionFlags.BufferStructured,
                depositAndParticleAux.Length * sizeof(uint),
                sizeof(uint));

            particleCountView = CreateUav(particleCountBuffer, particleCounts.Length);
            depositView = CreateUav(depositBuffer, depositAndParticleAux.Length);

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
        }

        void BuildAuxiliaryState(
            SolverGpuInputSnapshot snapshot,
            out uint[] particleCounts,
            out uint[] depositAndParticleAux)
        {
            particleCounts = new uint[voxelCount + 4 + groupCount];
            particleCounts[voxelCount] = (uint)particleCount;
            particleCounts[voxelCount + 1] = (uint)Math.Max(0, particleCapacity - particleCount);
            particleCounts[voxelCount + 2] = (uint)particleCount;
            int initialGroupIndexCount = snapshot.ParticleGroupIndices != null
                ? Math.Min(particleCount, snapshot.ParticleGroupIndices.Length)
                : 0;
            for (int particleIndex = 0; particleIndex < initialGroupIndexCount; particleIndex++)
            {
                int groupIndex = snapshot.ParticleGroupIndices[particleIndex];
                if (groupIndex >= 0 && groupIndex < groupCount)
                {
                    particleCounts[voxelCount + 4 + groupIndex]++;
                }
            }

            depositAndParticleAux = new uint[voxelCount * 4 + particleCapacity * 6];
            int foodRemainingBase = voxelCount * 3;
            if (snapshot.VoxelDensityLimits != null)
            {
                for (int voxelIndex = 0; voxelIndex < voxelCount; voxelIndex++)
                {
                    float food = snapshot.VoxelDensityLimits[voxelIndex * 4 + 2];
                    depositAndParticleAux[foodRemainingBase + voxelIndex] = (uint)Math.Round(Math.Max(0, food) * DepositScale);
                }
            }

            int freeSlotBase = voxelCount * 4;
            for (int slot = particleCount; slot < particleCapacity; slot++)
            {
                depositAndParticleAux[freeSlotBase + slot - particleCount] = (uint)slot;
            }

            int ageBase = voxelCount * 4 + particleCapacity;
            int antStateBase = voxelCount * 4 + particleCapacity * 5;
            int particleObjectCount = snapshot.Particles != null ? snapshot.Particles.Count : 0;
            for (int slot = 0; slot < particleCount && slot < particleObjectCount; slot++)
            {
                Particle particle = snapshot.Particles[slot];
                depositAndParticleAux[ageBase + slot] = (uint)Math.Max(0, particle != null ? particle.age : 0);
                depositAndParticleAux[antStateBase + slot] = snapshot.ParticleAntStates != null && slot < snapshot.ParticleAntStates.Length
                    ? snapshot.ParticleAntStates[slot]
                    : 0u;
            }

        }

        void CreateVoxelBehaviorBuffers(SolverGpuInputSnapshot snapshot)
        {
            float[] behavior = snapshot.VoxelBehaviorData != null && snapshot.VoxelBehaviorData.Length == voxelCount * 4
                ? snapshot.VoxelBehaviorData
                : DefaultVoxelBehaviorData();
            float[] vectors = snapshot.VoxelVectorData != null && snapshot.VoxelVectorData.Length == voxelCount * 4
                ? snapshot.VoxelVectorData
                : new float[voxelCount * 4];
            float[] limits = snapshot.VoxelDensityLimits != null && snapshot.VoxelDensityLimits.Length == voxelCount * 4
                ? snapshot.VoxelDensityLimits
                : DefaultVoxelDensityLimits();

            voxelBehaviorBuffer = CreateFloat4Buffer(behavior, BindFlags.ShaderResource);
            voxelVectorBuffer = CreateFloat4Buffer(vectors, BindFlags.ShaderResource);
            voxelDensityLimitsBuffer = CreateFloat4Buffer(limits, BindFlags.ShaderResource);
            voxelBehaviorView = CreateSrv(voxelBehaviorBuffer, voxelCount);
            voxelVectorView = CreateSrv(voxelVectorBuffer, voxelCount);
            voxelDensityLimitsView = CreateSrv(voxelDensityLimitsBuffer, voxelCount);
        }

        float[] DefaultVoxelBehaviorData()
        {
            float[] data = new float[voxelCount * 4];
            for (int i = 0; i < voxelCount; i++)
            {
                int offset = i * 4;
                data[offset] = 1;
                data[offset + 1] = 1;
                data[offset + 2] = 1;
                data[offset + 3] = 1;
            }

            return data;
        }

        float[] DefaultVoxelDensityLimits()
        {
            float[] data = new float[voxelCount * 4];
            for (int i = 0; i < voxelCount; i++)
            {
                int offset = i * 4;
                data[offset] = -1;
                data[offset + 1] = -1;
            }

            return data;
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
        }

        void CompileShaders()
        {
            moveShader = CreateComputeShader("MoveParticlesAndDeposit");
            applyDepositsShader = CreateComputeShader("ApplyDeposits");
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
        }

        ID3D11ComputeShader CreateComputeShader(string entryPoint)
        {
            string resourceName = "Nuclei3.GpuShaders." + entryPoint + ".cso";
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

        static int DispatchGroupCount(int count)
        {
            return (count + 255) / 256;
        }

        static int DispatchLineGroupCount(int count)
        {
            return (count + 63) / 64;
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

            for (int i = 0; i <= 10; i++)
            {
                context.CSSetShaderResource(i, null);
            }

            context.CSSetShader(null);
        }

        static void CreateDevice(out ID3D11Device device, out ID3D11DeviceContext context)
        {
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
            if (voxelDensityLimitsBuffer != null) voxelDensityLimitsBuffer.Dispose();
            if (parameterBuffer != null) parameterBuffer.Dispose();
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
}

RWStructuredBuffer<float> Source : register(u0);
RWStructuredBuffer<float> Destination : register(u1);
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
StructuredBuffer<float4> GroupData0 : register(t1);
StructuredBuffer<float4> GroupData1 : register(t2);
StructuredBuffer<uint> VoxelFlags : register(t3);
StructuredBuffer<float4> GroupColorData : register(t4);
StructuredBuffer<float4> VoxelBehavior : register(t5);
StructuredBuffer<float4> VoxelVectors : register(t6);
StructuredBuffer<float4> VoxelDensityLimits : register(t7);
StructuredBuffer<float> AntFoodPheromone : register(t8);
StructuredBuffer<float> AntBasePheromone : register(t9);
Texture2D<float4> DensityPreviewSource : register(t10);

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
    return VoxelCount * 4 + stackIndex;
}

int ParticleAgeIndex(int particleIndex)
{
    return VoxelCount * 4 + ParticleCapacity + particleIndex;
}

int ParticleDeathNeighbourIndex(int particleIndex)
{
    return VoxelCount * 4 + ParticleCapacity * 2 + particleIndex;
}

int ParticleDivisionNeighbourIndex(int particleIndex)
{
    return VoxelCount * 4 + ParticleCapacity * 3 + particleIndex;
}

int ParticleGenerationIndex(int particleIndex)
{
    return VoxelCount * 4 + ParticleCapacity * 4 + particleIndex;
}

int ParticleAntStateIndex(int particleIndex)
{
    return VoxelCount * 4 + ParticleCapacity * 5 + particleIndex;
}

int AntFoodDepositIndex(int voxelIndex)
{
    return VoxelCount + voxelIndex;
}

int AntBaseDepositIndex(int voxelIndex)
{
    return VoxelCount * 2 + voxelIndex;
}

int FoodRemainingIndex(int voxelIndex)
{
    return VoxelCount * 3 + voxelIndex;
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
    return index >= 0 && index < VoxelCount && (VoxelFlags[index] & 1) != 0;
}

float ClampDensityLimits(float value, int index)
{
    if (index < 0 || index >= VoxelCount) return value;

    float4 limits = VoxelDensityLimits[index];
    float minDensity = limits.x;
    float maxDensity = limits.y;

    if (maxDensity >= 0.0 && value > maxDensity) value = maxDensity;
    if (minDensity >= 0.0 && value > 0.0 && value < minDensity) value = minDensity;
    return value;
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

float3 WrapSensorPosition(float3 p)
{
    const float wrapDistance = 0.01;

    if (PlanarYZ == 0)
    {
        if (p.x < wrapDistance) p.x = DimX - 0.1;
        else if (p.x > DimX - wrapDistance) p.x = 0.1;
    }

    if (PlanarXZ == 0)
    {
        if (p.y < wrapDistance) p.y = DimY - 0.1;
        else if (p.y > DimY - wrapDistance) p.y = 0.1;
    }

    if (PlanarXY == 0)
    {
        if (p.z < wrapDistance) p.z = DimZ - 0.1;
        else if (p.z > DimZ - wrapDistance) p.z = 0.1;
    }

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
    float food = DepositFixed[FoodRemainingIndex(index)] / DepositScale;
    if (food > value && food > 0.0)
    {
        value = food;
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
    else if (currentParentIndex >= 0 && DepositFixed[FoodRemainingIndex(currentParentIndex)] == 0u)
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
    if (voxelIndex < 0 || voxelIndex >= VoxelCount) return false;
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

void ApplyMovementBoundaries(inout float3 position, inout float3 direction, inout uint wrapped)
{
    if (Wrap != 0)
    {
        const float wrapDistance = 0.01;

        if (PlanarYZ == 0)
        {
            if (position.x < wrapDistance) { position.x = DimX - 0.1; wrapped = 1; }
            else if (position.x > DimX - wrapDistance) { position.x = 0.1; wrapped = 1; }
        }

        if (PlanarXZ == 0)
        {
            if (position.y < wrapDistance) { position.y = DimY - 0.1; wrapped = 1; }
            else if (position.y > DimY - wrapDistance) { position.y = 0.1; wrapped = 1; }
        }

        if (PlanarXY == 0)
        {
            if (position.z < wrapDistance) { position.z = DimZ - 0.1; wrapped = 1; }
            else if (position.z > DimZ - wrapDistance) { position.z = 0.1; wrapped = 1; }
        }

        return;
    }

    float boundaryDistance = VoxelSize;
    if (PlanarYZ == 0)
    {
        if (position.x <= boundaryDistance) { position.x = boundaryDistance; direction.x = abs(direction.x); }
        else if (position.x >= DimX - boundaryDistance) { position.x = DimX - boundaryDistance; direction.x = -abs(direction.x); }
    }

    if (PlanarXZ == 0)
    {
        if (position.y <= boundaryDistance) { position.y = boundaryDistance; direction.y = abs(direction.y); }
        else if (position.y >= DimY - boundaryDistance) { position.y = DimY - boundaryDistance; direction.y = -abs(direction.y); }
    }

    if (PlanarXY == 0)
    {
        if (position.z <= boundaryDistance) { position.z = boundaryDistance; direction.z = abs(direction.z); }
        else if (position.z >= DimZ - boundaryDistance) { position.z = DimZ - boundaryDistance; direction.z = -abs(direction.z); }
    }
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
void MoveParticlesAndDeposit(uint3 id : SV_DispatchThreadID)
{
    int particleIndex = id.x;
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
        behavior = VoxelBehavior[currentParentIndex];
        vectorField = VoxelVectors[currentParentIndex];
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
    uint wanderFrequency = max(1u, (uint)round(group1.w));
    if (DynamicPopulation != 0)
    {
        float wander = saturate(group0.w);
        float groupPopulation = (float)ParticleCounts[GroupPopulationIndex(groupIndex)];
        wanderFrequency = max(1u, (uint)floor(pow(1.0 - wander, 3.0) * groupPopulation / 40.0));
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
    float3 moveDirection = NormalizeOr(steeringDirection + x, x);
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
            nextPosition = recoveredPosition;
        }
        else
        {
            parentIndex = currentParentIndex;
            if (!IsValidVoxelIndex(parentIndex)) parentIndex = -1;
            nextPosition = position;
        }
    }

    if (parentIndex >= 0 && parentIndex < VoxelCount)
    {
        uint previousCount = ParticleCounts[parentIndex];
        if (previousCount == 0u && CanDepositAtVoxel(parentIndex, sensorDistance))
        {
            float ageT = saturate(particleAge / 99.0);
            float antMultiplier = foundFood ? lerp(1.0, 0.3, ageT) : lerp(1.0, 0.2, ageT);
            float antTrailFactor = !foundFood && AntFoodPheromone[parentIndex] > 0.0 ? 1.1 : 0.9;
            float effectiveDeposit = isAnt ? depositValue * antMultiplier * (foundFood ? 1.0 : antTrailFactor) : depositValue;
            uint fixedDeposit = (uint)round(max(0.0, effectiveDeposit * DepositScale));
            if (fixedDeposit > 0u)
            {
                if (isAnt)
                {
                    InterlockedAdd(DepositFixed[foundFood ? AntFoodDepositIndex(parentIndex) : AntBaseDepositIndex(parentIndex)], fixedDeposit);
                }
                else
                {
                    InterlockedAdd(DepositFixed[parentIndex], fixedDeposit);
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

[numthreads(256, 1, 1)]
void ApplyDeposits(uint3 id : SV_DispatchThreadID)
{
    int index = id.x;
    if (index >= VoxelCount) return;

    uint fixedDeposit = HasSlimeParticles != 0 ? DepositFixed[index] : 0u;
    if (!IsValidVoxelIndex(index))
    {
        DepositFixed[index] = 0u;
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
    DepositFixed[index] = 0u;

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
    int index = id.x;
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
    int index = id.x;
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
    int index = id.x;
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
    int lineIndex = id.x;
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

bool TryClaimDeath()
{
    uint current = ParticleCounts[ActivePopulationIndex()];
    uint minimum = (uint)max(MinimumPopulation, 0);
    [loop]
    for (int attempt = 0; attempt < 64; attempt++)
    {
        if (current <= minimum) return false;

        uint observed;
        InterlockedCompareExchange(ParticleCounts[ActivePopulationIndex()], current, current - 1u, observed);
        if (observed == current) return true;
        current = observed;
    }

    return false;
}

[numthreads(256, 1, 1)]
void ApplyParticleDeath(uint3 id : SV_DispatchThreadID)
{
    int particleIndex = id.x;
    if (particleIndex >= ParticleCapacity || !IsParticleAlive(particleIndex)) return;

    int parentIndex = (int)round(ParticleDirection[particleIndex].w);
    int neighbourCount = 0;
    if (parentIndex >= 0 && parentIndex < VoxelCount)
    {
        neighbourCount = max(0, (int)round(Source[parentIndex]) - 1);
    }
    DepositFixed[ParticleDeathNeighbourIndex(particleIndex)] = (uint)neighbourCount;

    uint age = DepositFixed[ParticleAgeIndex(particleIndex)];
    bool shouldDie = parentIndex < 0 || parentIndex >= VoxelCount ||
                     neighbourCount <= DeathMinimumNeighbours ||
                     neighbourCount >= DeathMaximumNeighbours;
    if (parentIndex >= 0 && age < (uint)max(DeathMinimumAge, 0) && neighbourCount > 2)
    {
        shouldDie = false;
    }

    if (!shouldDie || !TryClaimDeath()) return;

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

bool TryClaimBirth(out uint particleSlot)
{
    particleSlot = 0u;
    uint current = ParticleCounts[ActivePopulationIndex()];
    uint maximum = (uint)min(max(MaximumPopulation, 0), ParticleCapacity);
    [loop]
    for (int attempt = 0; attempt < 64; attempt++)
    {
        if (current >= maximum) return false;

        uint observed;
        InterlockedCompareExchange(ParticleCounts[ActivePopulationIndex()], current, current + 1u, observed);
        if (observed == current)
        {
            uint previousFree;
            uint ignored;
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

        current = observed;
    }

    return false;
}

[numthreads(256, 1, 1)]
void ApplyParticleDivision(uint3 id : SV_DispatchThreadID)
{
    int particleIndex = id.x;
    if (particleIndex >= ParticleCapacity || !IsParticleAlive(particleIndex)) return;
    if (ParticleYAxis[particleIndex].w < -0.5) return;

    int parentIndex = (int)round(ParticleDirection[particleIndex].w);
    if (!IsValidVoxelIndex(parentIndex)) return;

    int neighbourCount = max(0, (int)round(Source[parentIndex]) - 1);
    DepositFixed[ParticleDivisionNeighbourIndex(particleIndex)] = (uint)neighbourCount;
    uint age = DepositFixed[ParticleAgeIndex(particleIndex)];
    if ((VoxelDensityLimits[parentIndex].x >= 0.0 && age < (uint)max(DivisionMinimumAge, 0)) ||
        neighbourCount < DivisionMinimumNeighbours ||
        neighbourCount > DivisionMaximumNeighbours)
    {
        return;
    }

    uint selectionSeed = Hash((uint)particleIndex + (uint)Iteration * 3266489917u);
    if ((selectionSeed & 1u) != 0u) return;

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
    int index = id.x;
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
    int index = id.x;
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
        int sliceWidth = max(ResX, 1);
        int sliceHeight = max(ResY, 1);
        int columns = max(PreviewAtlasColumns, 1);
        int tileX = u / sliceWidth;
        int tileY = v / sliceHeight;
        int z = tileY * columns + tileX;
        if (z >= ResZ)
        {
            DensityPreview[int2(u, v)] = 0.0;
            return;
        }

        int x = u - tileX * sliceWidth;
        int y = v - tileY * sliceHeight;
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
    float remainingFood = DepositFixed[FoodRemainingIndex(index)] / DepositScale;
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
        int sliceWidth = max(ResX, 1);
        int sliceHeight = max(ResY, 1);
        int columns = max(PreviewAtlasColumns, 1);
        int tileX = u / sliceWidth;
        int tileY = v / sliceHeight;
        int z = tileY * columns + tileX;
        if (z >= ResZ)
        {
            CombinedDensityPreview[int2(u, v)] = 0.0;
            return;
        }

        int x = u - tileX * sliceWidth;
        int y = v - tileY * sliceHeight;
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
    int columns = max(PreviewAtlasColumns, 1);
    int column = z % columns;
    int row = z / columns;
    return int2(column * max(ResX, 1) + x, row * max(ResY, 1) + y);
}

float DensityAtlasValue(int x, int y, int z)
{
    x = clamp(x, 0, max(ResX - 1, 0));
    y = clamp(y, 0, max(ResY - 1, 0));
    z = clamp(z, 0, max(ResZ - 1, 0));
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

    int sliceWidth = max(ResX, 1);
    int sliceHeight = max(ResY, 1);
    int columns = max(PreviewAtlasColumns, 1);
    int tileX = u / sliceWidth;
    int tileY = v / sliceHeight;
    int z = tileY * columns + tileX;
    if (z >= ResZ)
    {
        DensityGradientPreview[int2(u, v)] = 0.0;
        return;
    }

    int x = clamp(u - tileX * sliceWidth, 0, ResX - 1);
    int y = clamp(v - tileY * sliceHeight, 0, ResY - 1);
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
    int particleIndex = id.x;
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
    int particleIndex = id.x;
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
}";
    }
}
