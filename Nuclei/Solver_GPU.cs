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
          : base("Nuclei3 Solver GPU", "Solver GPU",
              "Experimental GPU compute solver scaffold",
              "Nuclei3", " Solver")
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
            get { return GH_Exposure.secondary; }
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
            bool hasVisibleParticlePreviewRecipient = HasVisibleParticlePreviewRecipient(Params.Output[0], new HashSet<IGH_Param>());
            bool buildSolverPreviewCache = WantsSolverOwnedPreview(hasVisibleParticlePreviewRecipient);
            bool buildParticlePreviewCache = hasVisibleParticlePreviewRecipient || buildSolverPreviewCache;
            solverOwnsPreviewCache = buildSolverPreviewCache;
            bool needsFullParticleOutput = NeedsOutputData(0);
            bool buildPreviewDuringFullParticleSync = needsFullParticleOutput && buildParticlePreviewCache;
            bool needsParticleOutput = needsFullParticleOutput || hasVisibleParticlePreviewRecipient;
            bool needsVoxelOutput = NeedsOutputData(1);

            bool reset = true;
            Voxel[,,] inputVoxels = null;
            List<ParticleGroup> inputParticleGroups = new List<ParticleGroup>();
            List<string> settings = new List<string>();

            long stageStart = Stopwatch.GetTimestamp();
            DA.GetData(0, ref reset);
            DA.GetData(1, ref inputVoxels);
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
            settingsTicks = Stopwatch.GetTimestamp() - stageStart;
            bool shouldResetState = reset || voxels == null || particles == null || !StateMatches(inputVoxels, inputParticleGroups);
            bool stateResetFailed = false;

            if (shouldResetState)
            {
                try
                {
                    stageStart = Stopwatch.GetTimestamp();
                    ResetState(inputVoxels, inputParticleGroups);
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

            bool settingsSupported = true;
            if (solverSettings.DynamicPopulation)
            {
                unsupportedGpuReason = "Dynamic population is not supported by Solver GPU yet.";
                settingsSupported = false;
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, unsupportedGpuReason);
            }
            else if (unsupportedGpuReason == "Dynamic population is not supported by Solver GPU yet.")
            {
                unsupportedGpuReason = "";
            }

            GpuFullSolverStepResult solverResult = null;
            if (!reset && settingsSupported && !stateResetFailed && gpuStatus.Available && fullSolverEngine != null && voxels != null && iteration < solverSettings.MaxIterations)
            {
                try
                {
                    stageStart = Stopwatch.GetTimestamp();
                    solverResult = RunGpuSolverStep(solverSettings, needsVoxelOutput, needsFullParticleOutput, buildPreviewDuringFullParticleSync);
                    engineTicks = Stopwatch.GetTimestamp() - stageStart;
                    iteration++;
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
            if (needsParticleOutput)
            {
                DA.SetData(0, particles);
            }
            setParticlesTicks = Stopwatch.GetTimestamp() - outputStageStart;
            outputStageStart = Stopwatch.GetTimestamp();
            if (needsVoxelOutput)
            {
                DA.SetData(1, voxels);
            }
            setVoxelsTicks = Stopwatch.GetTimestamp() - outputStageStart;
            outputsTicks = Stopwatch.GetTimestamp() - stageStart;

            string status = CreateStatus(solverSettings, solverResult);
            DA.SetData(2, status);

            if (!reset)
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
                    Message = reset ? "Solution is Reset" : "Iteration: " + iteration;
                }
            }
            else
            {
                Message = "GPU unavailable";
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, gpuStatus.Message);
            }
        }

        void ResetState(Voxel[,,] inputVoxels, List<ParticleGroup> inputParticleGroups)
        {
            SolverGpuInputSnapshot snapshot = SolverGpuInputSnapshot.Capture(inputVoxels, inputParticleGroups);
            voxels = snapshot.Voxels;
            particles = snapshot.Particles;
            resX = snapshot.ResX;
            resY = snapshot.ResY;
            resZ = snapshot.ResZ;
            activeVoxelCount = snapshot.ActiveVoxelCount;
            particleCount = snapshot.ParticleCount;
            unsupportedGpuReason = "";
            iteration = 0;
            lastPreviewReadbackQueuedIteration = -1;
            ConfigurePreviewCacheRefresh();

            DisposeGpuEngines();

            if (snapshot.HasAntParticles)
            {
                unsupportedGpuReason = "Ant particle groups are not supported by Solver GPU yet.";
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, unsupportedGpuReason);
                return;
            }

            if (gpuStatus != null && gpuStatus.Available && snapshot.VoxelDensity != null && snapshot.VoxelDensity.Length > 0)
            {
                fullSolverEngine = new GpuFullSlimeSolverEngine(snapshot);
            }
        }

        bool StateMatches(Voxel[,,] inputVoxels, List<ParticleGroup> inputParticleGroups)
        {
            if (inputVoxels == null)
            {
                return resX == 0 && resY == 0 && resZ == 0;
            }

            return inputVoxels.GetLength(0) == resX
                && inputVoxels.GetLength(1) == resY
                && inputVoxels.GetLength(2) == resZ
                && CountInputParticles(inputParticleGroups) == particleCount;
        }

        int CountInputParticles(List<ParticleGroup> inputParticleGroups)
        {
            int count = 0;
            if (inputParticleGroups == null)
            {
                return 0;
            }

            for (int i = 0; i < inputParticleGroups.Count; i++)
            {
                ParticleGroup group = inputParticleGroups[i];
                if (group != null && group.particles != null)
                {
                    count += group.particles.Count;
                }
            }

            return count;
        }

        GpuFullSolverStepResult RunGpuSolverStep(SolverGpuSettings settings, bool syncVoxels, bool syncParticles, bool buildPreviewCache)
        {
            SolverGpuDimensionMode dimensionMode = SolverGpuDimensionMode.FromResolution(resX, resY, resZ);
            return fullSolverEngine.Step(voxels, particles, settings, dimensionMode, iteration, syncVoxels, syncParticles, buildPreviewCache);
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
                            density[baseIndex + z] = (float)voxel.density;
                        }
                    }
                }
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

                Preview_Voxel voxelPreview = GetOwnerComponent(recipient) as Preview_Voxel;
                if (voxelPreview != null)
                {
                    if (voxelPreview.WantsSolverVoxelOutput) return true;
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
            DisposeGpuEngines();
            base.RemovedFromDocument(document);
        }

        void DisposeGpuEngines()
        {
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
            timingGpuDiffuseMs = 0;
            timingGpuReadbackMs = 0;
            timingOutputsTicks = 0;
            timingSetParticlesTicks = 0;
            timingSetVoxelsTicks = 0;
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
            context.AntParticles = false;
            context.DiffuseRangeAnt = 0;
            context.TrailSize = settings.TrailSize;
            context.TrailFreq = settings.TrailFreq;
            context.DynPop = settings.DynamicPopulation;
            context.Division = settings.Division;
            context.Death = settings.Death;
            context.MaxIterations = settings.MaxIterations;
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
                + "|" + context.MaxIterations;
        }

        GpuComputeSmokeTestResult gpuStatus;
        GpuFullSlimeSolverEngine fullSolverEngine;
        Voxel[,,] voxels;
        ParticleList particles;
        int resX;
        int resY;
        int resZ;
        int activeVoxelCount;
        int particleCount;
        int iteration = 0;
        int lastPreviewReadbackQueuedIteration = -1;
        string unsupportedGpuReason = "";
        bool solverOwnsPreviewCache = false;
        int timingSampleCount = 0;
        long timingTotalTicks = 0;
        long timingSettingsTicks = 0;
        long timingInputsTicks = 0;
        long timingEngineTicks = 0;
        double timingGpuMoveMs = 0;
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
        TimingReporter.SolverContext timingContext;
        string timingContextKey = "";

        protected override System.Drawing.Bitmap Icon
        {
            get
            {
                return Nuclei3.Properties.Resources.Solver;
            }
        }

        public override Guid ComponentGuid
        {
            get { return new Guid("931b7e8e-700b-476c-8320-b26296c5b661"); }
        }
    }
}
