using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;

using Grasshopper.Kernel;
using Grasshopper.Kernel.Attributes;

using Rhino.Geometry;

using static Nuclei3.ParticleGroup;

namespace Nuclei3
{
    public class SolverGPU : GH_Component
    {
        public SolverGPU()
          : base("Nuclei4 Solver GPU", "Solver GPU",
              "Experimental GPU compute solver scaffold",
              "Nuclei4", " Solver")
        {
        }

        protected override void RegisterInputParams(GH_Component.GH_InputParamManager pManager)
        {
            pManager.AddBooleanParameter("Reset", "reset", "Reset Boolean", GH_ParamAccess.item);
            pManager.AddGenericParameter("Voxels", "voxels", "Connects to Voxel Constructor", GH_ParamAccess.item);
            pManager.AddParameter(new ParticleGroupParameter(), "Particles", "particles", "Connects to Particle Constructors", GH_ParamAccess.list);
            pManager[2].DataMapping = GH_DataMapping.Flatten;
            pManager.AddTextParameter("Solver Settings", "settings", "Connects to Settings", GH_ParamAccess.list);
            pManager[3].Optional = true;
            pManager[3].DataMapping = GH_DataMapping.Flatten;
        }

        protected override void RegisterOutputParams(GH_Component.GH_OutputParamManager pManager)
        {
            pManager.AddGenericParameter("Output Particles", "particles", "Output Particles", GH_ParamAccess.item);
            pManager.AddGenericParameter("Output Voxels", "voxels", "Output Voxels", GH_ParamAccess.item);
            pManager.AddTextParameter("GPU Status", "status", "GPU compute status", GH_ParamAccess.item);
        }

        public override GH_Exposure Exposure
        {
            get { return GH_Exposure.primary; }
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            long solveStart = Stopwatch.GetTimestamp();
            long settingsTicks = 0;
            long inputsTicks = 0;
            long engineTicks = 0;
            long outputsTicks = 0;
            long setParticlesTicks = 0;
            long setVoxelsTicks = 0;

            bool reset = true;
            VoxelField inputVoxels = null;
            List<ParticleGroup> inputParticleGroups = new List<ParticleGroup>();
            List<string> settings = new List<string>();

            long stageStart = Stopwatch.GetTimestamp();
            DA.GetData(0, ref reset);
            VoxelFieldAccess.TryGet(DA, 1, Globals.voxelSize, out inputVoxels);
            DA.GetDataList(2, inputParticleGroups);
            DA.GetDataList(3, settings);
            inputsTicks = Stopwatch.GetTimestamp() - stageStart;

            if (reset)
            {
                resetTimingAverages();
                TimingReporter.StartRun();
            }

            if (reset || gpuStatus == null)
            {
                gpuStatus = GpuComputeSmokeTest.RunOnce();
            }

            stageStart = Stopwatch.GetTimestamp();
            SolverGpuSettings solverSettings = SolverGpuSettings.FromStrings(settings);
            latestSolverSettings = solverSettings;
            settingsTicks = Stopwatch.GetTimestamp() - stageStart;

            bool hasVisibleParticlePreviewRecipient = HasVisibleParticlePreviewRecipient(Params.Output[0], new HashSet<IGH_Param>());
            bool hasVisibleParticleTrailPreviewRecipient = HasVisibleParticleTrailPreviewRecipient(Params.Output[0], new HashSet<IGH_Param>());
            bool hasParticleTrailRecipient = HasParticleTrailRecipient(Params.Output[0], new HashSet<IGH_Param>());
            int densityPreviewScale = DensityPreviewScaleForRecipients(Params.Output[1], new HashSet<IGH_Param>());
            bool hasVisibleDynamicDensityPreviewRecipient = densityPreviewScale > 0;
            bool useSharedParticlePreview = hasVisibleParticlePreviewRecipient && Rhino.RhinoApp.ExeVersion >= 9;
            bool useSharedParticleTrailPreview = hasVisibleParticleTrailPreviewRecipient && Rhino.RhinoApp.ExeVersion >= 9 && solverSettings.TrailSize > 1;
            bool buildSolverPreviewCache = WantsSolverOwnedPreview(hasVisibleParticlePreviewRecipient);
            bool buildParticlePreviewCache = (hasVisibleParticlePreviewRecipient && !useSharedParticlePreview) || buildSolverPreviewCache;
            solverOwnsPreviewCache = buildSolverPreviewCache;
            bool needsParticleTrailState = hasParticleTrailRecipient && solverSettings.TrailSize > 1;
            bool needsParticleTrailResetOutput = reset && hasParticleTrailRecipient;
            bool needsFullParticleOutput = NeedsOutputData(0) || needsParticleTrailState || needsParticleTrailResetOutput;
            bool buildPreviewDuringFullParticleSync = needsFullParticleOutput && buildParticlePreviewCache;
            bool needsParticleOutput = needsFullParticleOutput || hasVisibleParticlePreviewRecipient || hasVisibleParticleTrailPreviewRecipient;
            bool needsVoxelSync = NeedsOutputData(1);
            bool shouldSetVoxelOutput = needsVoxelSync || HasOutputRecipient(Params.Output[1]);

            bool atMaxIterationsAtEntry = !reset && iteration >= solverSettings.MaxIterations;
            bool shouldResetState = reset
                || voxels == null
                || particles == null
                || !StateMatches(inputVoxels, inputParticleGroups)
                || (fullSolverEngine != null && !fullSolverEngine.SupportsPopulationCapacity(solverSettings));
            bool stateResetFailed = false;

            if (shouldResetState)
            {
                try
                {
                    stageStart = Stopwatch.GetTimestamp();
                    ResetState(inputVoxels, inputParticleGroups, solverSettings, hasVisibleDynamicDensityPreviewRecipient, useSharedParticlePreview, useSharedParticleTrailPreview, densityPreviewScale);
                    if (buildParticlePreviewCache)
                    {
                        BuildPreviewCacheFromCurrentParticles();
                    }
                    inputsTicks += Stopwatch.GetTimestamp() - stageStart;
                }
                catch (Exception ex)
                {
                    DisposeGpuEngines();
                    stateResetFailed = true;
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "GPU reset failed: " + ex.Message);
                    Message = "GPU reset error";
                }
            }

            atMaxIterationsAtEntry = !reset && iteration >= solverSettings.MaxIterations;

            bool settingsSupported = true;
            if (unsupportedGpuReason == "Dynamic population is not supported by Solver GPU yet.")
            {
                unsupportedGpuReason = "";
            }

            if (settingsSupported && !stateResetFailed && fullSolverEngine != null && !atMaxIterationsAtEntry)
            {
                try
                {
                    stageStart = Stopwatch.GetTimestamp();
                    SetDensityFieldPreviewEnabled(hasVisibleDynamicDensityPreviewRecipient, densityPreviewScale);
                    SetParticlePreviewEnabled(useSharedParticlePreview);
                    SetParticleTrailPreviewEnabled(useSharedParticleTrailPreview, solverSettings.TrailSize);
                    if (!UpdateLiveParticleGroupSettings(inputParticleGroups))
                    {
                        settingsSupported = false;
                        AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, unsupportedGpuReason);
                    }
                    else if (!UpdateLiveVoxelBehaviorFields(inputVoxels, inputParticleGroups))
                    {
                        settingsSupported = false;
                        AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, unsupportedGpuReason);
                    }
                    inputsTicks += Stopwatch.GetTimestamp() - stageStart;
                }
                catch (Exception ex)
                {
                    DisposeGpuEngines();
                    stateResetFailed = true;
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "GPU particle settings update failed: " + ex.Message);
                    Message = "GPU settings error";
                }
            }

            GpuFullSolverStepResult solverResult = null;
            if (!reset && !atMaxIterationsAtEntry && settingsSupported && !stateResetFailed && gpuStatus.Available && fullSolverEngine != null && voxels != null && iteration < solverSettings.MaxIterations)
            {
                try
                {
                    stageStart = Stopwatch.GetTimestamp();
                    solverResult = RunGpuSolverStep(solverSettings, needsVoxelSync, needsFullParticleOutput, buildPreviewDuringFullParticleSync);
                    engineTicks = Stopwatch.GetTimestamp() - stageStart;
                    particleCount = solverResult.ParticleCount;
                    iteration++;
                    if (solverResult.SyncedParticles) lastParticleOutputSyncIteration = iteration;
                    if (solverResult.SyncedVoxels) lastVoxelOutputSyncIteration = iteration;
                    if (solverResult.QueuedPreviewReadback)
                    {
                        lastPreviewReadbackQueuedIteration = iteration;
                    }
                }
                catch (Exception ex)
                {
                    DisposeGpuEngines();
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "GPU solver failed: " + ex.Message);
                    Message = "GPU solver error";
                }
            }

            stageStart = Stopwatch.GetTimestamp();
            long outputStageStart = Stopwatch.GetTimestamp();
            if (particles != null)
            {
                DA.SetData(0, particles);
            }
            setParticlesTicks = Stopwatch.GetTimestamp() - outputStageStart;
            outputStageStart = Stopwatch.GetTimestamp();
            if (voxels != null)
            {
                DA.SetData(1, voxels);
            }
            setVoxelsTicks = Stopwatch.GetTimestamp() - outputStageStart;
            outputsTicks = Stopwatch.GetTimestamp() - stageStart;

            string status = CreateStatus(solverSettings, solverResult);
            DA.SetData(2, status);
            latestTimingContext = createTimingContext(solverSettings);
            if (voxels != null)
            {
                NucleiGpuDisplayManager.RegisterSolver(this);
            }
            else
            {
                NucleiGpuDisplayManager.UnregisterSolver(InstanceGuid);
            }

            bool reachedMaxIterations = !reset && iteration >= solverSettings.MaxIterations;

            if (!reset && !reachedMaxIterations)
            {
                recordGpuTimingAverages(
                    solverSettings,
                    solverResult,
                    settingsTicks,
                    inputsTicks,
                    engineTicks,
                    outputsTicks,
                    setParticlesTicks,
                    setVoxelsTicks,
                    Stopwatch.GetTimestamp() - solveStart);
            }

            if (gpuStatus.Available)
            {
                if (Message != "GPU solver error" && Message != "GPU reset error")
                {
                    Message = reset
                        ? "Solution is Reset"
                        : reachedMaxIterations
                            ? "Complete: " + iteration + "/" + solverSettings.MaxIterations
                            : "Iteration: " + iteration;
                }
            }
            else
            {
                Message = "GPU unavailable";
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, gpuStatus.Message);
            }
        }

        void ResetState(VoxelField inputVoxels, List<ParticleGroup> inputParticleGroups, SolverGpuSettings settings, bool enableDensityFieldPreview, bool enableParticlePreview, bool enableParticleTrailPreview, int densityPreviewScale)
        {
            Stopwatch resetTimer = Stopwatch.StartNew();
            Stopwatch stageTimer = Stopwatch.StartNew();
            SolverGpuInputSnapshot snapshot = SolverGpuInputSnapshot.Capture(inputVoxels, inputParticleGroups);
            double snapshotMs = stageTimer.Elapsed.TotalMilliseconds;
            stageTimer.Restart();

            voxels = snapshot.Field;
            inputVoxelReference = inputVoxels;
            particles = snapshot.Particles;
            ClearParticleTrails(particles);
            resX = snapshot.ResX;
            resY = snapshot.ResY;
            resZ = snapshot.ResZ;
            voxelSize = snapshot.VoxelSize;
            ApplyGlobalDimensionState();
            activeVoxelCount = snapshot.ActiveVoxelCount;
            particleCount = snapshot.ParticleCount;
            particleGroupCount = snapshot.GroupCount;
            antParticles = snapshot.HasAntParticles;
            particleGroupReferences = snapshot.ParticleGroups ?? new ParticleGroup[0];
            Globals.particleGroups = new List<ParticleGroup>(particleGroupReferences);
            particleGroupParticleCounts = CaptureInputParticleGroupCounts(inputParticleGroups);
            gpuDensityFieldPreviewEnabled = enableDensityFieldPreview;
            gpuParticlePreviewEnabled = enableParticlePreview;
            gpuParticleTrailPreviewEnabled = enableParticleTrailPreview;
            unsupportedGpuReason = "";
            iteration = 0;
            lastPreviewReadbackQueuedIteration = -1;
            lastParticleOutputSyncIteration = 0;
            lastVoxelOutputSyncIteration = 0;
            ConfigurePreviewCacheRefresh();

            if (gpuStatus != null && gpuStatus.Available && snapshot.ResX > 0 && snapshot.ResY > 0 && snapshot.ResZ > 0)
            {
                DisposeGpuEngines();
                fullSolverEngine = new GpuFullSlimeSolverEngine(snapshot, settings, enableDensityFieldPreview, enableParticlePreview, enableParticleTrailPreview, settings.TrailSize, densityPreviewScale);
                ConfigureGpuParticlePreviewProvider();
                ConfigureGpuParticleTrailPreviewProvider();
                ConfigureGpuVolumeMeshProvider();
                ConfigureGpuOutputSynchronizers();
            }

            double restoreMs = stageTimer.Elapsed.TotalMilliseconds;
            TimingReporter.WriteGpuReset(
                "full",
                particleCount,
                snapshot.ResX * snapshot.ResY * snapshot.ResZ,
                createTimingContext(settings),
                resetTimer.Elapsed.TotalMilliseconds,
                snapshotMs,
                restoreMs);
        }

        void ClearParticleTrails(ParticleList particleList)
        {
            if (particleList == null)
            {
                return;
            }

            for (int i = 0; i < particleList.Count; i++)
            {
                Particle particle = particleList[i];
                if (particle == null)
                {
                    continue;
                }

                if (particle.trails == null)
                {
                    particle.trails = new List<Point3d>();
                }
                else if (particle.trails.Count > 0)
                {
                    particle.trails.Clear();
                }
            }
        }

        void SetDensityFieldPreviewEnabled(bool enabled, int densityPreviewScale)
        {
            gpuDensityFieldPreviewEnabled = enabled;
            if (fullSolverEngine == null)
            {
                return;
            }

            fullSolverEngine.SetSharedDensityPreviewEnabled(
                enabled,
                SolverGpuDimensionMode.FromResolution(resX, resY, resZ),
                densityPreviewScale);
        }

        void SetParticlePreviewEnabled(bool enabled)
        {
            gpuParticlePreviewEnabled = enabled;
            if (fullSolverEngine == null)
            {
                ConfigureGpuParticlePreviewProvider();
                return;
            }

            fullSolverEngine.SetSharedParticlePreviewEnabled(enabled);
            ConfigureGpuParticlePreviewProvider();
        }

        void SetParticleTrailPreviewEnabled(bool enabled, int trailSize)
        {
            gpuParticleTrailPreviewEnabled = enabled;
            if (fullSolverEngine == null)
            {
                ConfigureGpuParticleTrailPreviewProvider();
                return;
            }

            fullSolverEngine.SetSharedParticleTrailPreviewEnabled(enabled, trailSize);
            ConfigureGpuParticleTrailPreviewProvider();
        }

        void ConfigureGpuParticlePreviewProvider()
        {
            if (particles == null)
            {
                return;
            }

            particles.GpuPreviewFrameProvider = fullSolverEngine != null
                ? (Func<GpuParticlePreviewFrame>)CreateParticlePreviewFrameOnDemand
                : null;
        }

        void ConfigureGpuParticleTrailPreviewProvider()
        {
            if (particles == null)
            {
                return;
            }

            particles.GpuTrailPreviewFrameProvider = fullSolverEngine != null
                ? (Func<GpuParticleTrailPreviewFrame>)CreateParticleTrailPreviewFrameOnDemand
                : null;
        }

        GpuParticlePreviewFrame CreateParticlePreviewFrameOnDemand()
        {
            if (fullSolverEngine == null)
            {
                return null;
            }

            if (!gpuParticlePreviewEnabled)
            {
                gpuParticlePreviewEnabled = true;
                fullSolverEngine.SetSharedParticlePreviewEnabled(true);
                fullSolverEngine.RefreshParticlePreview(
                    latestSolverSettings ?? new SolverGpuSettings(),
                    SolverGpuDimensionMode.FromResolution(resX, resY, resZ),
                    iteration);
            }

            return fullSolverEngine.CreateParticlePreviewFrame();
        }

        GpuParticleTrailPreviewFrame CreateParticleTrailPreviewFrameOnDemand()
        {
            if (fullSolverEngine == null)
            {
                return null;
            }

            if (!gpuParticleTrailPreviewEnabled)
            {
                SolverGpuSettings settings = latestSolverSettings ?? new SolverGpuSettings();
                gpuParticleTrailPreviewEnabled = true;
                fullSolverEngine.SetSharedParticleTrailPreviewEnabled(true, settings.TrailSize);
                fullSolverEngine.RefreshParticleTrailPreview(
                    settings,
                    SolverGpuDimensionMode.FromResolution(resX, resY, resZ),
                    iteration);
            }

            return fullSolverEngine.CreateParticleTrailPreviewFrame();
        }

        void ConfigureGpuVolumeMeshProvider()
        {
            if (voxels == null)
            {
                return;
            }

            voxels.GpuVolumeMeshProvider = fullSolverEngine != null
                ? (Func<float, int, int, GpuVolumeMeshResult>)fullSolverEngine.CreateDensityMesh
                : null;
        }

        void ConfigureGpuOutputSynchronizers()
        {
            if (particles != null)
            {
                particles.CpuStateSynchronizer = fullSolverEngine != null
                    ? (Action)SynchronizeParticleOutputOnDemand
                    : null;
            }

            if (voxels != null)
            {
                voxels.DynamicStateSynchronizer = fullSolverEngine != null
                    ? (Action)SynchronizeVoxelOutputOnDemand
                    : null;
            }
        }

        void SynchronizeParticleOutputOnDemand()
        {
            if (fullSolverEngine == null || particles == null || voxels == null || latestSolverSettings == null ||
                lastParticleOutputSyncIteration == iteration)
            {
                return;
            }

            particleCount = fullSolverEngine.SynchronizeParticleOutput(
                particles,
                voxels,
                latestSolverSettings,
                Math.Max(0, iteration - 1));
            lastParticleOutputSyncIteration = iteration;
        }

        void SynchronizeVoxelOutputOnDemand()
        {
            if (fullSolverEngine == null || voxels == null || lastVoxelOutputSyncIteration == iteration)
            {
                return;
            }

            fullSolverEngine.SynchronizeVoxelOutput(voxels);
            lastVoxelOutputSyncIteration = iteration;
        }

        bool StateMatches(VoxelField inputVoxels, List<ParticleGroup> inputParticleGroups)
        {
            if (inputVoxels == null)
            {
                return resX == 0 && resY == 0 && resZ == 0;
            }

            return inputVoxels.ResX == resX
                && inputVoxels.ResY == resY
                && inputVoxels.ResZ == resZ
                && Math.Abs(inputVoxels.VoxelSize - voxelSize) < 0.000001
                && InputParticleGroupCountsMatch(inputParticleGroups);
        }

        void ApplyGlobalDimensionState()
        {
            Globals.tridimensional = resX > 1 && resY > 1 && resZ > 1;
        }

        bool UpdateLiveVoxelBehaviorFields(VoxelField inputVoxels, List<ParticleGroup> inputParticleGroups)
        {
            if (fullSolverEngine == null || ReferenceEquals(inputVoxels, inputVoxelReference))
            {
                return true;
            }

            SolverGpuInputSnapshot snapshot = SolverGpuInputSnapshot.CaptureVoxelFields(inputVoxels, inputParticleGroups);
            if (snapshot.ResX != resX || snapshot.ResY != resY || snapshot.ResZ != resZ)
            {
                unsupportedGpuReason = "GPU voxel behavior map update failed because voxel resolution changed.";
                return false;
            }

            if (!fullSolverEngine.UpdateVoxelBehaviorFields(snapshot))
            {
                unsupportedGpuReason = "GPU voxel behavior map update failed.";
                return false;
            }

            voxels = snapshot.Field;
            activeVoxelCount = snapshot.ActiveVoxelCount;
            inputVoxelReference = inputVoxels;
            ConfigureGpuVolumeMeshProvider();
            ConfigureGpuOutputSynchronizers();
            lastParticleOutputSyncIteration = -1;
            lastVoxelOutputSyncIteration = -1;
            return true;
        }

        bool InputParticleGroupCountsMatch(List<ParticleGroup> inputParticleGroups)
        {
            int[] counts = CaptureInputParticleGroupCounts(inputParticleGroups);
            if (counts.Length != particleGroupCount || particleGroupParticleCounts == null || counts.Length != particleGroupParticleCounts.Length)
            {
                return false;
            }

            for (int i = 0; i < counts.Length; i++)
            {
                if (counts[i] != particleGroupParticleCounts[i])
                {
                    return false;
                }
            }

            return true;
        }

        int[] CaptureInputParticleGroupCounts(List<ParticleGroup> inputParticleGroups)
        {
            int count = inputParticleGroups != null ? inputParticleGroups.Count : 0;
            int[] counts = new int[count];
            for (int i = 0; i < count; i++)
            {
                ParticleGroup group = inputParticleGroups[i];
                counts[i] = group != null && group.particles != null ? group.particles.Count : 0;
            }

            return counts;
        }

        bool HasAntParticleGroups(List<ParticleGroup> inputParticleGroups)
        {
            if (inputParticleGroups == null)
            {
                return false;
            }

            for (int i = 0; i < inputParticleGroups.Count; i++)
            {
                ParticleGroup group = inputParticleGroups[i];
                if (group != null && group.ant)
                {
                    return true;
                }
            }

            return false;
        }

        GpuFullSolverStepResult RunGpuSolverStep(SolverGpuSettings settings, bool syncVoxels, bool syncParticles, bool buildPreviewCache)
        {
            SolverGpuDimensionMode dimensionMode = SolverGpuDimensionMode.FromResolution(resX, resY, resZ);
            return fullSolverEngine.Step(voxels, particles, settings, dimensionMode, iteration, syncVoxels, syncParticles, buildPreviewCache);
        }

        bool UpdateLiveParticleGroupSettings(List<ParticleGroup> inputParticleGroups)
        {
            float[] groupData0;
            float[] groupData1;
            float[] groupColorData;
            bool hasAntParticles;
            bool hasSlimeParticles;
            SolverGpuInputSnapshot.CaptureGroupSettings(inputParticleGroups, out groupData0, out groupData1, out hasAntParticles, out hasSlimeParticles);
            groupColorData = SolverGpuInputSnapshot.CaptureGroupColors(inputParticleGroups);

            ApplyLiveParticleGroupMetadata(inputParticleGroups);

            if (fullSolverEngine == null)
            {
                return true;
            }

            if (!fullSolverEngine.UpdateGroupSettings(groupData0, groupData1, groupColorData))
            {
                unsupportedGpuReason = "GPU particle settings changed in a way that requires reset.";
                return false;
            }

            return true;
        }

        void ApplyLiveParticleGroupMetadata(List<ParticleGroup> inputParticleGroups)
        {
            if (inputParticleGroups == null || particleGroupReferences == null)
            {
                return;
            }

            int count = Math.Min(inputParticleGroups.Count, particleGroupReferences.Length);
            for (int groupIndex = 0; groupIndex < count; groupIndex++)
            {
                ParticleGroup inputGroup = inputParticleGroups[groupIndex];
                ParticleGroup targetGroup = particleGroupReferences[groupIndex];
                CopyParticleGroupSettings(inputGroup, targetGroup);
            }
        }

        void CopyParticleGroupSettings(ParticleGroup source, ParticleGroup target)
        {
            if (source == null || target == null)
            {
                return;
            }

            target.speed = source.speed;
            target.sensorDistance = source.sensorDistance;
            target.sensorAngle = source.sensorAngle;
            target.rotationAngle = source.rotationAngle;
            target.depositValue = source.depositValue;
            target.wanderFrequency = source.wanderFrequency;
            target.baseWanderFrequency = source.baseWanderFrequency;
            target.color = source.color;
            target.ant = source.ant;
        }

        void ConfigurePreviewCacheRefresh()
        {
            if (particles == null || particles.PreviewCache == null)
            {
                return;
            }

            particles.PreviewCache.TryCompleteAsyncUpdate = TryCompletePreviewCacheFromGpu;
            particles.PreviewCache.QueueAsyncUpdate = QueuePreviewCacheReadbackForCurrentIteration;
        }

        bool TryCompletePreviewCacheFromGpu()
        {
            return fullSolverEngine != null
                && particles != null
                && fullSolverEngine.TryCompletePreviewCache(particles);
        }

        bool QueuePreviewCacheReadbackForCurrentIteration()
        {
            if (fullSolverEngine == null || lastPreviewReadbackQueuedIteration == iteration)
            {
                return false;
            }

            if (!fullSolverEngine.QueuePreviewCacheReadback())
            {
                return false;
            }

            lastPreviewReadbackQueuedIteration = iteration;
            return true;
        }

        void BuildPreviewCacheFromCurrentParticles()
        {
            if (particles == null)
            {
                return;
            }

            int count = particles.Count;
            ParticlePreviewCache cache = particles.PreviewCache;
            ParticlePreviewBuildCache buildCache = new ParticlePreviewBuildCache(count);
            cache.BeginBuild(count);

            for (int i = 0; i < count; i++)
            {
                Particle particle = particles[i];
                if (particle != null)
                {
                    buildCache.AddParticle(particle);
                }
            }

            cache.Merge(buildCache);
            cache.CompleteBuild();
        }

        float[] CaptureCurrentDensity()
        {
            int count = resX * resY * resZ;
            float[] density = new float[count];

            if (voxels == null)
            {
                return density;
            }

            VoxelGridData data = voxels.Data;
            for (int ordinal = 0; ordinal < data.ActiveCount; ordinal++)
            {
                int flatIndex = data.ActiveFlatIndexAt(ordinal);
                density[flatIndex] = (float)voxels.GetScalarValue(VoxelPreviewField.SlimeChemoattractants, flatIndex);
            }

            return density;
        }

        string CreateStatus(SolverGpuSettings settings, GpuFullSolverStepResult solverResult)
        {
            string status = gpuStatus.Message
                + " | driver: " + (string.IsNullOrEmpty(gpuStatus.Driver) ? "none" : gpuStatus.Driver)
                + " | feature: " + gpuStatus.FeatureLevel
                + " | iteration: " + iteration
                + " | particles: " + (particles != null ? particles.Count : 0)
                + " | active voxels: " + activeVoxelCount
                + " | mode: " + SolverGpuDimensionMode.FromResolution(resX, resY, resZ).Name
                + " | field preview: " + (gpuDensityFieldPreviewEnabled ? "on" : "off")
                + " | max iterations: " + settings.MaxIterations
                + " | state: " + (iteration >= settings.MaxIterations ? "complete" : "running")
                + " | wrap: " + settings.WrapBoundaries
                + " | range: " + settings.DiffuseRange;

            if (!string.IsNullOrEmpty(unsupportedGpuReason))
            {
                status += " | " + unsupportedGpuReason;
            }

            if (gpuStatus.Milliseconds > 0)
            {
                status += " | smoke test ms: " + gpuStatus.Milliseconds.ToString("0.###");
            }

            if (solverResult != null)
            {
                status += " | gpu step ms: " + solverResult.TotalMilliseconds.ToString("0.###")
                    + " | particle ms: " + solverResult.ParticleMilliseconds.ToString("0.###")
                    + " | population ms: " + solverResult.PopulationMilliseconds.ToString("0.###")
                    + " | diffusion ms: " + solverResult.DiffusionMilliseconds.ToString("0.###")
                    + " | readback ms: " + solverResult.ReadbackMilliseconds.ToString("0.###")
                    + " | sync: " + ParticleSyncMarker(solverResult) + (solverResult.SyncedVoxels ? "v" : "-")
                    + (solverResult.SyncedParticles && solverResult.BuiltPreviewCache ? " cache" : "")
                    + " | passes: " + solverResult.Passes
                    + " | moved: " + solverResult.MovedParticles;
            }

            return status;
        }

        string ParticleSyncMarker(GpuFullSolverStepResult solverResult)
        {
            if (solverResult.SyncedParticles) return "p";
            if (solverResult.BuiltPreviewCache) return "c";
            if (solverResult.QueuedPreviewReadback) return "q";
            return "-";
        }

        bool HasOutputRecipient(IGH_Param sourceParam)
        {
            return sourceParam != null && sourceParam.Recipients != null && sourceParam.Recipients.Count > 0;
        }

        bool NeedsOutputData(int outputIndex)
        {
            if (Params == null || Params.Output == null || outputIndex < 0 || outputIndex >= Params.Output.Count)
            {
                return false;
            }

            return HasDemandingRecipient(Params.Output[outputIndex], new HashSet<IGH_Param>());
        }

        bool HasDemandingRecipient(IGH_Param sourceParam, HashSet<IGH_Param> visited)
        {
            if (sourceParam == null || sourceParam.Recipients == null) return false;

            foreach (IGH_Param recipient in sourceParam.Recipients)
            {
                if (recipient == null || !visited.Add(recipient)) continue;

                Preview_Particle preview = GetOwnerComponent(recipient) as Preview_Particle;
                if (preview != null)
                {
                    continue;
                }

                Preview_Particle_Trails_GPU trailPreview = GetOwnerComponent(recipient) as Preview_Particle_Trails_GPU;
                if (trailPreview != null)
                {
                    continue;
                }

                Preview_Voxel voxelPreview = GetOwnerComponent(recipient) as Preview_Voxel;
                if (voxelPreview != null)
                {
                    if (voxelPreview.WantsSolverVoxelOutput) return true;
                    continue;
                }

                GpuVolumeToMesh volumeMesher = GetOwnerComponent(recipient) as GpuVolumeToMesh;
                if (volumeMesher != null && volumeMesher.UsesSolverGpuDensity)
                {
                    continue;
                }

                bool hasDownstreamRecipients = recipient.Recipients != null && recipient.Recipients.Count > 0;
                if (hasDownstreamRecipients)
                {
                    if (HasDemandingRecipient(recipient, visited)) return true;
                    continue;
                }

                GH_Component owner = GetOwnerComponent(recipient);
                if (owner == null || !owner.Locked)
                {
                    return true;
                }
            }

            return false;
        }

        bool HasVisibleVoxelDensityPreviewRecipient(IGH_Param sourceParam, HashSet<IGH_Param> visited)
        {
            if (sourceParam == null || sourceParam.Recipients == null) return false;

            foreach (IGH_Param recipient in sourceParam.Recipients)
            {
                if (recipient == null || !visited.Add(recipient)) continue;

                Preview_Voxel preview = GetOwnerComponent(recipient) as Preview_Voxel;
                if (preview != null)
                {
                    if (preview.WantsGpuDynamicDensityPreview) return true;
                    continue;
                }

                if (HasVisibleVoxelDensityPreviewRecipient(recipient, visited))
                {
                    return true;
                }
            }

            return false;
        }

        int DensityPreviewScaleForRecipients(IGH_Param sourceParam, HashSet<IGH_Param> visited)
        {
            if (sourceParam == null || sourceParam.Recipients == null) return 0;

            int scale = 0;
            foreach (IGH_Param recipient in sourceParam.Recipients)
            {
                if (recipient == null || !visited.Add(recipient)) continue;

                Preview_Voxel preview = GetOwnerComponent(recipient) as Preview_Voxel;
                if (preview != null)
                {
                    if (preview.WantsGpuDynamicDensityPreview)
                    {
                        scale = Math.Max(scale, preview.GpuDensityPreviewScale);
                    }
                    continue;
                }

                scale = Math.Max(scale, DensityPreviewScaleForRecipients(recipient, visited));
            }

            return scale;
        }

        bool HasVisibleParticlePreviewRecipient(IGH_Param sourceParam, HashSet<IGH_Param> visited)
        {
            if (sourceParam == null || sourceParam.Recipients == null) return false;

            foreach (IGH_Param recipient in sourceParam.Recipients)
            {
                if (recipient == null || !visited.Add(recipient)) continue;

                Preview_Particle preview = GetOwnerComponent(recipient) as Preview_Particle;
                if (preview != null)
                {
                    if (preview.WantsSolverPreviewCache) return true;
                    continue;
                }

                if (HasVisibleParticlePreviewRecipient(recipient, visited))
                {
                    return true;
                }
            }

            return false;
        }

        bool HasVisibleParticleTrailPreviewRecipient(IGH_Param sourceParam, HashSet<IGH_Param> visited)
        {
            if (sourceParam == null || sourceParam.Recipients == null) return false;

            foreach (IGH_Param recipient in sourceParam.Recipients)
            {
                if (recipient == null || !visited.Add(recipient)) continue;

                Preview_Particle_Trails_GPU preview = GetOwnerComponent(recipient) as Preview_Particle_Trails_GPU;
                if (preview != null)
                {
                    if (preview.WantsGpuTrailPreview) return true;
                    continue;
                }

                if (HasVisibleParticleTrailPreviewRecipient(recipient, visited))
                {
                    return true;
                }
            }

            return false;
        }

        bool HasParticleTrailRecipient(IGH_Param sourceParam, HashSet<IGH_Param> visited)
        {
            if (sourceParam == null || sourceParam.Recipients == null) return false;

            foreach (IGH_Param recipient in sourceParam.Recipients)
            {
                if (recipient == null || !visited.Add(recipient)) continue;

                if (GetOwnerComponent(recipient) is Particle_Extractor_TrailPoints)
                {
                    return true;
                }

                if (HasParticleTrailRecipient(recipient, visited))
                {
                    return true;
                }
            }

            return false;
        }

        GH_Component GetOwnerComponent(IGH_Param param)
        {
            if (param == null || param.Attributes == null) return null;

            GH_Component directOwner = param.Attributes.DocObject as GH_Component;
            if (directOwner != null)
            {
                return directOwner;
            }

            GH_Component topLevelOwner = param.Attributes.GetTopLevel != null
                ? param.Attributes.GetTopLevel.DocObject as GH_Component
                : null;
            if (topLevelOwner != null)
            {
                return topLevelOwner;
            }

            GH_LinkedParamAttributes linkedAttributes = param.Attributes as GH_LinkedParamAttributes;
            if (linkedAttributes != null && linkedAttributes.Parent != null)
            {
                GH_Component parentOwner = linkedAttributes.Parent.DocObject as GH_Component;
                if (parentOwner != null)
                {
                    return parentOwner;
                }
            }

            return null;
        }

        internal GpuDensityFieldPreviewFrame GetDensityFieldPreviewFrame()
        {
            if (fullSolverEngine == null)
            {
                return null;
            }

            return fullSolverEngine.CreateDensityFieldPreviewFrame();
        }

        internal GpuDensityFieldPreviewFrame GetDensityFieldPreviewFrame(int valueIndex, float minimumThreshold, float maximumThreshold, int densityPreviewScale)
        {
            if (fullSolverEngine == null)
            {
                return null;
            }

            SolverGpuDimensionMode dimensionMode = SolverGpuDimensionMode.FromResolution(resX, resY, resZ);
            return fullSolverEngine.CreateVoxelFieldPreviewFrame(valueIndex, dimensionMode, minimumThreshold, maximumThreshold, densityPreviewScale);
        }

        internal VoxelField OutputVoxels
        {
            get { return voxels; }
        }

        internal void RecordDensityFieldPreviewDrawTiming(long drawTicks)
        {
            recordGpuFieldPreviewDrawTiming(drawTicks);
        }

        bool WantsSolverOwnedPreview(bool hasVisibleParticlePreviewRecipient)
        {
            return false;
        }

        public override void DrawViewportMeshes(IGH_PreviewArgs args)
        {
            long callStart = Stopwatch.GetTimestamp();
            long rebuildTicks = 0;
            long drawTicks = 0;
            bool drewPreview = false;

            try
            {
                if (Hidden || !solverOwnsPreviewCache || particles == null) return;

                ParticlePreviewCache cache = particles.PreviewCache;
                if (cache == null) return;

                if (cache.TryCompleteAsyncUpdate != null || cache.QueueAsyncUpdate != null)
                {
                    long rebuildStart = Stopwatch.GetTimestamp();
                    bool rebuilt = cache.TryRefreshAsync();
                    if (rebuilt)
                    {
                        rebuildTicks = Stopwatch.GetTimestamp() - rebuildStart;
                    }
                }

                if (cache == null || !cache.IsValid) return;

                long drawStart = Stopwatch.GetTimestamp();
                base.DrawViewportWires(args);

                if (!Globals.tridimensional)
                {
                    args.Display.DrawPolygon(Globals.bgPolygon, Color.Black, true);
                }

                if (cache.SlimePointCloud != null && cache.SlimePointCloud.Count > 0)
                {
                    args.Display.DrawPointCloud(cache.SlimePointCloud, 2.0f);
                }

                if (cache.AntPointCloud1 != null && cache.AntPointCloud1.Count > 0)
                {
                    args.Display.DrawPointCloud(cache.AntPointCloud1, 2.0f);
                }

                if (cache.AntPointCloud2 != null && cache.AntPointCloud2.Count > 0)
                {
                    args.Display.DrawPointCloud(cache.AntPointCloud2, 3.0f);
                }

                drewPreview = true;
                drawTicks = Stopwatch.GetTimestamp() - drawStart;
            }
            finally
            {
                if (drewPreview)
                {
                    long totalTicks = Stopwatch.GetTimestamp() - callStart;
                    recordSolverPreviewDrawTiming(totalTicks, rebuildTicks, drawTicks);
                }
            }
        }

        public override BoundingBox ClippingBox
        {
            get
            {
                ParticlePreviewCache cache = particles != null ? particles.PreviewCache : null;
                if (solverOwnsPreviewCache && cache != null && cache.IsValid && cache.HasPoint)
                {
                    return cache.ClippingBox;
                }

                return base.ClippingBox;
            }
        }

        public override void RemovedFromDocument(GH_Document document)
        {
            writeGpuTimingAverages();
            writeSolverPreviewDrawAverages();
            writeGpuFieldPreviewDrawAverages();
            NucleiGpuDisplayManager.UnregisterSolver(InstanceGuid);
            DisposeGpuEngines();
            base.RemovedFromDocument(document);
        }

        void DisposeGpuEngines()
        {
            if (particles != null)
            {
                particles.GpuPreviewFrameProvider = null;
                particles.GpuTrailPreviewFrameProvider = null;
                particles.CpuStateSynchronizer = null;
            }

            if (voxels != null)
            {
                voxels.GpuVolumeMeshProvider = null;
                voxels.DynamicStateSynchronizer = null;
            }

            if (fullSolverEngine != null)
            {
                fullSolverEngine.Dispose();
                fullSolverEngine = null;
            }
        }

        void resetTimingAverages()
        {
            writeGpuTimingAverages();
            writeSolverPreviewDrawAverages();
            writeGpuFieldPreviewDrawAverages();
            clearTimingCounters();
        }

        void clearTimingCounters()
        {
            timingSampleCount = 0;
            timingTotalTicks = 0;
            timingSettingsTicks = 0;
            timingInputsTicks = 0;
            timingEngineTicks = 0;
            timingGpuMoveMs = 0;
            timingGpuPopulationMs = 0;
            timingGpuDiffuseMs = 0;
            timingGpuReadbackMs = 0;
            timingOutputsTicks = 0;
            timingSetParticlesTicks = 0;
            timingSetVoxelsTicks = 0;
            fieldPreviewDrawCallCount = 0;
            fieldPreviewDrawSampleCount = 0;
            fieldPreviewTotalTicks = 0;
            fieldPreviewDrawTicks = 0;
            timingContext = new TimingReporter.SolverContext();
            timingContextKey = "";
        }

        void recordSolverPreviewDrawTiming(long totalTicks, long rebuildTicks, long drawTicks)
        {
            previewDrawCallCount++;
            previewDrawSampleCount++;
            previewTotalTicks += totalTicks;
            previewRebuildTicks += rebuildTicks;
            previewDrawTicks += drawTicks;

            if (previewDrawSampleCount < TimingReporter.ReportFrequency) return;

            writeSolverPreviewDrawAverages();
        }

        void writeSolverPreviewDrawAverages()
        {
            if (previewDrawSampleCount <= 0) return;

            double totalMs = TimingReporter.TicksToMilliseconds(previewTotalTicks, previewDrawSampleCount);
            double rebuildMs = TimingReporter.TicksToMilliseconds(previewRebuildTicks, previewDrawSampleCount);
            double drawMs = TimingReporter.TicksToMilliseconds(previewDrawTicks, previewDrawSampleCount);
            TimingReporter.WriteSolverGpuPreviewAverages(
                previewDrawCallCount,
                previewDrawSampleCount,
                particles != null ? particles.Count : particleCount,
                totalMs,
                rebuildMs,
                drawMs);

            previewDrawSampleCount = 0;
            previewTotalTicks = 0;
            previewRebuildTicks = 0;
            previewDrawTicks = 0;
        }

        void recordGpuFieldPreviewDrawTiming(long drawTicks)
        {
            fieldPreviewDrawCallCount++;
            fieldPreviewDrawSampleCount++;
            fieldPreviewTotalTicks += drawTicks;
            fieldPreviewDrawTicks += drawTicks;

            if (fieldPreviewDrawSampleCount < TimingReporter.ReportFrequency) return;

            writeGpuFieldPreviewDrawAverages();
        }

        void writeGpuFieldPreviewDrawAverages()
        {
            if (fieldPreviewDrawSampleCount <= 0) return;

            double totalMs = TimingReporter.TicksToMilliseconds(fieldPreviewTotalTicks, fieldPreviewDrawSampleCount);
            double drawMs = TimingReporter.TicksToMilliseconds(fieldPreviewDrawTicks, fieldPreviewDrawSampleCount);
            TimingReporter.WriteGpuDensityFieldPreviewAverages(
                fieldPreviewDrawCallCount,
                fieldPreviewDrawSampleCount,
                particles != null ? particles.Count : particleCount,
                activeVoxelCount,
                latestTimingContext,
                totalMs,
                drawMs);

            fieldPreviewDrawSampleCount = 0;
            fieldPreviewTotalTicks = 0;
            fieldPreviewDrawTicks = 0;
        }

        void recordGpuTimingAverages(
            SolverGpuSettings settings,
            GpuFullSolverStepResult solverResult,
            long settingsTicks,
            long inputsTicks,
            long engineTicks,
            long outputsTicks,
            long setParticlesTicks,
            long setVoxelsTicks,
            long totalTicks)
        {
            TimingReporter.SolverContext currentContext = createTimingContext(settings);
            string currentContextKey = createTimingContextKey(currentContext);

            if (timingSampleCount > 0 && timingContextKey != currentContextKey)
            {
                writeGpuTimingAverages();
                clearTimingCounters();
            }

            if (timingSampleCount == 0)
            {
                timingContext = currentContext;
                timingContextKey = currentContextKey;
            }

            timingSampleCount++;
            timingTotalTicks += totalTicks;
            timingSettingsTicks += settingsTicks;
            timingInputsTicks += inputsTicks;
            timingEngineTicks += engineTicks;
            timingOutputsTicks += outputsTicks;
            timingSetParticlesTicks += setParticlesTicks;
            timingSetVoxelsTicks += setVoxelsTicks;
            if (solverResult != null)
            {
                timingGpuMoveMs += solverResult.ParticleMilliseconds;
                timingGpuPopulationMs += solverResult.PopulationMilliseconds;
                timingGpuDiffuseMs += solverResult.DiffusionMilliseconds;
                timingGpuReadbackMs += solverResult.ReadbackMilliseconds;
            }

            if (timingSampleCount < TimingReporter.ReportFrequency) return;

            writeGpuTimingAverages();
            clearTimingCounters();
        }

        void writeGpuTimingAverages()
        {
            if (timingSampleCount <= 0) return;

            TimingReporter.WriteGpuSolverAverages(
                iteration,
                timingSampleCount,
                particles != null ? particles.Count : 0,
                activeVoxelCount,
                timingContext,
                TimingReporter.TicksToMilliseconds(timingTotalTicks, timingSampleCount),
                TimingReporter.TicksToMilliseconds(timingSettingsTicks, timingSampleCount),
                TimingReporter.TicksToMilliseconds(timingInputsTicks, timingSampleCount),
                timingGpuMoveMs / timingSampleCount,
                timingGpuDiffuseMs / timingSampleCount,
                timingGpuPopulationMs / timingSampleCount,
                timingGpuReadbackMs / timingSampleCount,
                TimingReporter.TicksToMilliseconds(timingOutputsTicks, timingSampleCount),
                TimingReporter.TicksToMilliseconds(timingSetParticlesTicks, timingSampleCount),
                TimingReporter.TicksToMilliseconds(timingSetVoxelsTicks, timingSampleCount));
        }

        TimingReporter.SolverContext createTimingContext(SolverGpuSettings settings)
        {
            TimingReporter.SolverContext context = new TimingReporter.SolverContext();
            context.WrapBoundaries = settings.WrapBoundaries;
            context.ResX = resX;
            context.ResY = resY;
            context.ResZ = resZ;
            context.ActiveVoxels = activeVoxelCount;
            context.DenseVoxelGrid = activeVoxelCount > 0 && activeVoxelCount == resX * resY * resZ;
            context.DimensionMode = SolverGpuDimensionMode.FromResolution(resX, resY, resZ).Name;
            context.Diffuse = settings.Diffuse;
            context.DiffuseRange = settings.DiffuseRange;
            context.Decay = settings.Decay;
            context.AntParticles = antParticles;
            context.DiffuseRangeAnt = settings.AntDiffuseRange;
            context.TrailSize = settings.TrailSize;
            context.TrailFreq = settings.TrailFreq;
            context.DynPop = settings.DynamicPopulation;
            context.Division = settings.Division;
            context.Death = settings.Death;
            context.MaxIterations = settings.MaxIterations;
            context.GpuPreviewMode = gpuDensityFieldPreviewEnabled
                ? "global_density"
                : gpuParticleTrailPreviewEnabled
                    ? "particle_trails"
                    : "off";
            context.GpuDensityFieldPreview = gpuDensityFieldPreviewEnabled;
            return context;
        }

        string createTimingContextKey(TimingReporter.SolverContext context)
        {
            return (context.WrapBoundaries ? "1" : "0")
                + "|" + context.ResX
                + "|" + context.ResY
                + "|" + context.ResZ
                + "|" + context.ActiveVoxels
                + "|" + (context.DenseVoxelGrid ? "1" : "0")
                + "|" + context.DimensionMode
                + "|" + context.Diffuse
                + "|" + context.DiffuseRange
                + "|" + context.Decay
                + "|" + (context.AntParticles ? "1" : "0")
                + "|" + context.DiffuseRangeAnt
                + "|" + context.MaxIterations
                + "|" + (context.DynPop ? "1" : "0")
                + "|" + (context.Division ? "1" : "0")
                + "|" + (context.Death ? "1" : "0")
                + "|" + context.GpuPreviewMode
                + "|" + (context.GpuDensityFieldPreview ? "1" : "0");
        }

        GpuComputeSmokeTestResult gpuStatus;
        GpuFullSlimeSolverEngine fullSolverEngine;
        VoxelField voxels;
        VoxelField inputVoxelReference;
        ParticleList particles;
        int resX;
        int resY;
        int resZ;
        double voxelSize;
        int activeVoxelCount;
        int particleCount;
        int particleGroupCount;
        bool antParticles;
        ParticleGroup[] particleGroupReferences = new ParticleGroup[0];
        int[] particleGroupParticleCounts = new int[0];
        int iteration = 0;
        int lastPreviewReadbackQueuedIteration = -1;
        int lastParticleOutputSyncIteration = -1;
        int lastVoxelOutputSyncIteration = -1;
        SolverGpuSettings latestSolverSettings;
        string unsupportedGpuReason = "";
        bool solverOwnsPreviewCache = false;
        int timingSampleCount = 0;
        long timingTotalTicks = 0;
        long timingSettingsTicks = 0;
        long timingInputsTicks = 0;
        long timingEngineTicks = 0;
        double timingGpuMoveMs = 0;
        double timingGpuPopulationMs = 0;
        double timingGpuDiffuseMs = 0;
        double timingGpuReadbackMs = 0;
        long timingOutputsTicks = 0;
        long timingSetParticlesTicks = 0;
        long timingSetVoxelsTicks = 0;
        int previewDrawCallCount = 0;
        int previewDrawSampleCount = 0;
        long previewTotalTicks = 0;
        long previewRebuildTicks = 0;
        long previewDrawTicks = 0;
        int fieldPreviewDrawCallCount = 0;
        int fieldPreviewDrawSampleCount = 0;
        long fieldPreviewTotalTicks = 0;
        long fieldPreviewDrawTicks = 0;
        TimingReporter.SolverContext timingContext;
        TimingReporter.SolverContext latestTimingContext;
        string timingContextKey = "";
        bool gpuDensityFieldPreviewEnabled = false;
        bool gpuParticlePreviewEnabled = false;
        bool gpuParticleTrailPreviewEnabled = false;

        protected override System.Drawing.Bitmap Icon
        {
            get
            {
                return Nuclei3.Properties.Resources.Solver;
            }
        }

        public override Guid ComponentGuid
        {
            get { return new Guid("e794ab27-6d27-4107-929f-b88e16209976"); }
        }
    }
}
