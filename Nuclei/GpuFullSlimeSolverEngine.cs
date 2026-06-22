using System;
using System.Diagnostics;
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

        ID3D11Device device;
        ID3D11DeviceContext context;
        ID3D11ComputeShader moveShader;
        ID3D11ComputeShader applyDepositsShader;
        ID3D11ComputeShader clearCountsShader;
        ID3D11ComputeShader countParticlesShader;
        ID3D11ComputeShader diffusionShader;
        ID3D11ComputeShader decayShader;

        ID3D11Buffer densityA;
        ID3D11Buffer densityB;
        ID3D11Buffer densityReadbackBuffer;
        ID3D11Buffer particlePositionBuffer;
        ID3D11Buffer particleDirectionBuffer;
        ID3D11Buffer particleYAxisBuffer;
        ID3D11Buffer particlePositionReadbackBuffer;
        ID3D11Buffer particleDirectionReadbackBuffer;
        ID3D11Buffer particleYAxisReadbackBuffer;
        readonly ID3D11Buffer[] particlePositionPreviewReadbackBuffers = new ID3D11Buffer[PreviewReadbackBufferCount];
        ID3D11Buffer particleCountBuffer;
        ID3D11Buffer depositBuffer;
        ID3D11Buffer groupData0Buffer;
        ID3D11Buffer groupData1Buffer;
        ID3D11Buffer voxelFlagsBuffer;
        ID3D11Buffer parameterBuffer;
        ID3D11Buffer weightsBuffer;

        ID3D11UnorderedAccessView densityAView;
        ID3D11UnorderedAccessView densityBView;
        ID3D11UnorderedAccessView particlePositionView;
        ID3D11UnorderedAccessView particleDirectionView;
        ID3D11UnorderedAccessView particleYAxisView;
        ID3D11UnorderedAccessView particleCountView;
        ID3D11UnorderedAccessView depositView;
        ID3D11ShaderResourceView groupData0View;
        ID3D11ShaderResourceView groupData1View;
        ID3D11ShaderResourceView voxelFlagsView;
        ID3D11ShaderResourceView weightsView;

        readonly int resX;
        readonly int resY;
        readonly int resZ;
        readonly int voxelCount;
        readonly int particleCount;
        readonly int groupCount;
        readonly float voxelSize;
        readonly float dimX;
        readonly float dimY;
        readonly float dimZ;
        readonly float[] densityReadback;
        readonly float[] particlePositionReadback;
        readonly float[] particleDirectionReadback;
        readonly float[] particleYAxisReadback;
        readonly float[] particlePositionPreviewReadback;
        readonly bool[] previewReadbackPending = new bool[PreviewReadbackBufferCount];
        readonly int[] previewReadbackSequences = new int[PreviewReadbackBufferCount];

        bool densityInA = true;
        int weightsRange = int.MinValue;
        int previewReadbackNextIndex = 0;
        int previewReadbackSequenceCounter = 0;
        int previewReadbackCompletedSequence = 0;

        public GpuFullSlimeSolverEngine(SolverGpuInputSnapshot snapshot)
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
            groupCount = Math.Max(0, snapshot.GroupCount);
            voxelSize = snapshot.VoxelSize > 0 ? snapshot.VoxelSize : 1.0f;
            dimX = resX * voxelSize;
            dimY = resY * voxelSize;
            dimZ = resZ * voxelSize;

            if (voxelCount <= 0)
            {
                throw new ArgumentException("GPU solver requires at least one voxel.");
            }

            densityReadback = new float[voxelCount];
            particlePositionReadback = new float[particleCount * 4];
            particleDirectionReadback = new float[particleCount * 4];
            particleYAxisReadback = new float[particleCount * 4];
            particlePositionPreviewReadback = new float[particleCount * 4];

            CreateDevice(out device, out context);
            CompileShaders();
            CreateDensityBuffers(snapshot.VoxelDensity);
            CreateVoxelFlagBuffer(snapshot.VoxelFlags);
            CreateParticleBuffers(snapshot);
            CreateGroupBuffers(snapshot);
            CreateParameterBuffer();

            DispatchClearParticleCounts(0);
            DispatchCountParticles(0);
        }

        public bool Matches(int x, int y, int z, int particles)
        {
            return resX == x && resY == y && resZ == z && particleCount == particles;
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

            EnsureWeights(settings.DiffuseRange);

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
            if (settings.Diffuse > 0)
            {
                if (!dimensionMode.PlanarYZ)
                {
                    DispatchDiffusionPass(0, settings, dimensionMode, iteration);
                    SwapDensityBuffers();
                    passCount++;
                }

                if (!dimensionMode.PlanarXZ)
                {
                    DispatchDiffusionPass(1, settings, dimensionMode, iteration);
                    SwapDensityBuffers();
                    passCount++;
                }

                if (!dimensionMode.PlanarXY)
                {
                    DispatchDiffusionPass(2, settings, dimensionMode, iteration);
                    SwapDensityBuffers();
                    passCount++;
                }
            }

            DispatchDecayPass(settings, dimensionMode, iteration);
            SwapDensityBuffers();
            passCount++;
            stage.Stop();
            double diffusionMs = stage.Elapsed.TotalMilliseconds;

            stage.Restart();
            if (syncVoxels)
            {
                ReadBackDensity();
                ApplyDensityToVoxels(voxels);
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
            if (particleCount <= 0 || particlePositionView == null)
            {
                return;
            }

            UpdateParameters(CreateParameters(0, settings, dimensionMode, iteration));

            context.CSSetShader(moveShader);
            context.CSSetConstantBuffers(0, new ID3D11Buffer[] { parameterBuffer });
            context.CSSetShaderResources(1, new ID3D11ShaderResourceView[] { groupData0View, groupData1View, voxelFlagsView });
            context.CSSetUnorderedAccessView(0, CurrentDensityView(), -1);
            context.CSSetUnorderedAccessView(2, particlePositionView, -1);
            context.CSSetUnorderedAccessView(3, particleDirectionView, -1);
            context.CSSetUnorderedAccessView(4, particleYAxisView, -1);
            context.CSSetUnorderedAccessView(5, particleCountView, -1);
            context.CSSetUnorderedAccessView(6, depositView, -1);
            context.Dispatch(DispatchGroupCount(particleCount), 1, 1);
            UnbindComputeResources();
        }

        void DispatchApplyDeposits(SolverGpuSettings settings, SolverGpuDimensionMode dimensionMode, int iteration)
        {
            UpdateParameters(CreateParameters(0, settings, dimensionMode, iteration));

            context.CSSetShader(applyDepositsShader);
            context.CSSetConstantBuffers(0, new ID3D11Buffer[] { parameterBuffer });
            context.CSSetUnorderedAccessView(0, CurrentDensityView(), -1);
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
            context.Dispatch(DispatchGroupCount(voxelCount), 1, 1);
            UnbindComputeResources();
        }

        void DispatchCountParticles(int iteration)
        {
            if (particleCount <= 0 || particleCountView == null)
            {
                return;
            }

            UpdateParameters(CreateParameters(0, new SolverGpuSettings(), SolverGpuDimensionMode.FromResolution(resX, resY, resZ), iteration));

            context.CSSetShader(countParticlesShader);
            context.CSSetConstantBuffers(0, new ID3D11Buffer[] { parameterBuffer });
            context.CSSetUnorderedAccessView(3, particleDirectionView, -1);
            context.CSSetUnorderedAccessView(5, particleCountView, -1);
            context.Dispatch(DispatchGroupCount(particleCount), 1, 1);
            UnbindComputeResources();
        }

        void DispatchDiffusionPass(int axis, SolverGpuSettings settings, SolverGpuDimensionMode dimensionMode, int iteration)
        {
            UpdateParameters(CreateParameters(axis, settings, dimensionMode, iteration));

            context.CSSetShader(diffusionShader);
            context.CSSetConstantBuffers(0, new ID3D11Buffer[] { parameterBuffer });
            context.CSSetShaderResources(0, new ID3D11ShaderResourceView[] { weightsView });
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
            context.CSSetUnorderedAccessView(0, CurrentDensityView(), -1);
            context.CSSetUnorderedAccessView(1, NextDensityView(), -1);
            context.Dispatch(DispatchGroupCount(voxelCount), 1, 1);
            UnbindComputeResources();
        }

        FullSolverParameters CreateParameters(int axis, SolverGpuSettings settings, SolverGpuDimensionMode dimensionMode, int iteration)
        {
            FullSolverParameters parameters = new FullSolverParameters();
            parameters.ResX = resX;
            parameters.ResY = resY;
            parameters.ResZ = resZ;
            parameters.VoxelCount = voxelCount;
            parameters.ParticleCount = particleCount;
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
            return parameters;
        }

        void UpdateParameters(FullSolverParameters parameters)
        {
            context.UpdateSubresourceSafe(ref parameters, parameterBuffer, 0, 0, 0, 0, false);
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

        void ReadBackParticles()
        {
            if (particleCount <= 0)
            {
                return;
            }

            ReadBackParticlePositions();
            ReadBackParticleAxes();
        }

        void ReadBackParticlePositions()
        {
            if (particleCount <= 0)
            {
                return;
            }

            ClearPendingPreviewReadbacks();
            ReadBackFloat4Buffer(particlePositionReadbackBuffer, particlePositionBuffer, particlePositionReadback);
        }

        public bool QueuePreviewCacheReadback()
        {
            if (particleCount <= 0)
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
            if (particleCount <= 0)
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

        void ReadBackParticleAxes()
        {
            if (particleCount <= 0)
            {
                return;
            }

            ReadBackFloat4Buffer(particleDirectionReadbackBuffer, particleDirectionBuffer, particleDirectionReadback);
            ReadBackFloat4Buffer(particleYAxisReadbackBuffer, particleYAxisBuffer, particleYAxisReadback);
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
            if (particles == null || particleCount <= 0)
            {
                return false;
            }

            int count = Math.Min(particles.Count, particleCount);
            ParticlePreviewCache previewCache = buildPreviewCache ? particles.PreviewCache : null;
            ParticlePreviewBuildCache previewBuildCache = previewCache != null ? new ParticlePreviewBuildCache(count) : null;
            if (previewCache != null)
            {
                previewCache.BeginBuild(count);
            }

            for (int i = 0; i < count; i++)
            {
                Particle particle = particles[i];
                if (particle == null)
                {
                    continue;
                }

                int offset = i * 4;
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

                if (particleYAxisReadback[offset + 3] > 0.5f)
                {
                    particle.trails.Clear();
                }

                if (previewBuildCache != null)
                {
                    previewBuildCache.AddParticle(particle);
                }
            }

            RecordTrails(particles, settings, iteration);

            if (previewCache != null)
            {
                previewCache.Merge(previewBuildCache);
                previewCache.CompleteBuild();
                return true;
            }

            particles.PreviewCache.Invalidate(count);
            return false;
        }

        bool BuildPreviewCacheFromPositions(ParticleList particles)
        {
            return BuildPreviewCacheFromPositions(particles, particlePositionReadback);
        }

        bool BuildPreviewCacheFromPositions(ParticleList particles, float[] positionReadback)
        {
            if (particles == null || particleCount <= 0)
            {
                return false;
            }

            int count = Math.Min(particles.Count, particleCount);
            ParticlePreviewCache previewCache = particles.PreviewCache;
            ParticlePreviewBuildCache previewBuildCache = new ParticlePreviewBuildCache(count);
            previewCache.BeginBuild(count);

            for (int i = 0; i < count; i++)
            {
                Particle particle = particles[i];
                if (particle == null)
                {
                    continue;
                }

                int offset = i * 4;
                Point3d origin = new Point3d(
                    positionReadback[offset],
                    positionReadback[offset + 1],
                    positionReadback[offset + 2]);

                previewBuildCache.AddParticlePoint(particle, origin);
            }

            previewCache.Merge(previewBuildCache);
            previewCache.CompleteBuild();
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

        void CreateParticleBuffers(SolverGpuInputSnapshot snapshot)
        {
            if (particleCount <= 0)
            {
                return;
            }

            float[] positions = new float[particleCount * 4];
            float[] directions = new float[particleCount * 4];
            float[] yAxes = new float[particleCount * 4];

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
            }

            particlePositionBuffer = CreateFloat4Buffer(positions, BindFlags.UnorderedAccess);
            particleDirectionBuffer = CreateFloat4Buffer(directions, BindFlags.UnorderedAccess);
            particleYAxisBuffer = CreateFloat4Buffer(yAxes, BindFlags.UnorderedAccess);
            particlePositionReadbackBuffer = CreateReadbackBuffer(positions.Length * sizeof(float));
            particleDirectionReadbackBuffer = CreateReadbackBuffer(directions.Length * sizeof(float));
            particleYAxisReadbackBuffer = CreateReadbackBuffer(yAxes.Length * sizeof(float));
            for (int i = 0; i < particlePositionPreviewReadbackBuffers.Length; i++)
            {
                particlePositionPreviewReadbackBuffers[i] = CreateReadbackBuffer(positions.Length * sizeof(float));
            }

            particlePositionView = CreateUav(particlePositionBuffer, particleCount);
            particleDirectionView = CreateUav(particleDirectionBuffer, particleCount);
            particleYAxisView = CreateUav(particleYAxisBuffer, particleCount);
        }

        void CreateGroupBuffers(SolverGpuInputSnapshot snapshot)
        {
            if (groupCount <= 0)
            {
                return;
            }

            groupData0Buffer = CreateFloat4Buffer(snapshot.GroupData0, BindFlags.ShaderResource);
            groupData1Buffer = CreateFloat4Buffer(snapshot.GroupData1, BindFlags.ShaderResource);
            groupData0View = CreateSrv(groupData0Buffer, groupCount);
            groupData1View = CreateSrv(groupData1Buffer, groupCount);
        }

        void CreateVoxelFlagBuffer(uint[] flags)
        {
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

            particleCountBuffer = device.CreateBuffer(
                voxelCount * sizeof(uint),
                BindFlags.UnorderedAccess,
                ResourceUsage.Default,
                CpuAccessFlags.None,
                ResourceOptionFlags.BufferStructured,
                sizeof(uint));

            depositBuffer = device.CreateBuffer(
                voxelCount * sizeof(uint),
                BindFlags.UnorderedAccess,
                ResourceUsage.Default,
                CpuAccessFlags.None,
                ResourceOptionFlags.BufferStructured,
                sizeof(uint));

            particleCountView = CreateUav(particleCountBuffer, voxelCount);
            depositView = CreateUav(depositBuffer, voxelCount);
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
            using (Blob moveBytecode = CompileShader(FullSolverShaderSource, "MoveParticlesAndDeposit"))
            using (Blob applyDepositsBytecode = CompileShader(FullSolverShaderSource, "ApplyDeposits"))
            using (Blob clearCountsBytecode = CompileShader(FullSolverShaderSource, "ClearParticleCounts"))
            using (Blob countParticlesBytecode = CompileShader(FullSolverShaderSource, "CountParticles"))
            using (Blob diffusionBytecode = CompileShader(FullSolverShaderSource, "DiffuseAxis"))
            using (Blob decayBytecode = CompileShader(FullSolverShaderSource, "ApplyDecay"))
            {
                moveShader = device.CreateComputeShader(moveBytecode, null);
                applyDepositsShader = device.CreateComputeShader(applyDepositsBytecode, null);
                clearCountsShader = device.CreateComputeShader(clearCountsBytecode, null);
                countParticlesShader = device.CreateComputeShader(countParticlesBytecode, null);
                diffusionShader = device.CreateComputeShader(diffusionBytecode, null);
                decayShader = device.CreateComputeShader(decayBytecode, null);
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

        void SwapDensityBuffers()
        {
            densityInA = !densityInA;
        }

        void UnbindComputeResources()
        {
            for (int i = 0; i <= 6; i++)
            {
                context.CSSetUnorderedAccessView(i, null, -1);
            }

            for (int i = 0; i <= 3; i++)
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
            if (densityAView != null) densityAView.Dispose();
            if (densityBView != null) densityBView.Dispose();
            if (particlePositionView != null) particlePositionView.Dispose();
            if (particleDirectionView != null) particleDirectionView.Dispose();
            if (particleYAxisView != null) particleYAxisView.Dispose();
            if (particleCountView != null) particleCountView.Dispose();
            if (depositView != null) depositView.Dispose();
            if (groupData0View != null) groupData0View.Dispose();
            if (groupData1View != null) groupData1View.Dispose();
            if (voxelFlagsView != null) voxelFlagsView.Dispose();
            if (densityA != null) densityA.Dispose();
            if (densityB != null) densityB.Dispose();
            if (densityReadbackBuffer != null) densityReadbackBuffer.Dispose();
            if (particlePositionBuffer != null) particlePositionBuffer.Dispose();
            if (particleDirectionBuffer != null) particleDirectionBuffer.Dispose();
            if (particleYAxisBuffer != null) particleYAxisBuffer.Dispose();
            if (particlePositionReadbackBuffer != null) particlePositionReadbackBuffer.Dispose();
            if (particleDirectionReadbackBuffer != null) particleDirectionReadbackBuffer.Dispose();
            if (particleYAxisReadbackBuffer != null) particleYAxisReadbackBuffer.Dispose();
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
            if (groupData0Buffer != null) groupData0Buffer.Dispose();
            if (groupData1Buffer != null) groupData1Buffer.Dispose();
            if (voxelFlagsBuffer != null) voxelFlagsBuffer.Dispose();
            if (parameterBuffer != null) parameterBuffer.Dispose();
            if (moveShader != null) moveShader.Dispose();
            if (applyDepositsShader != null) applyDepositsShader.Dispose();
            if (clearCountsShader != null) clearCountsShader.Dispose();
            if (countParticlesShader != null) countParticlesShader.Dispose();
            if (diffusionShader != null) diffusionShader.Dispose();
            if (decayShader != null) decayShader.Dispose();
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
}

RWStructuredBuffer<float> Source : register(u0);
RWStructuredBuffer<float> Destination : register(u1);
RWStructuredBuffer<float4> ParticlePosition : register(u2);
RWStructuredBuffer<float4> ParticleDirection : register(u3);
RWStructuredBuffer<float4> ParticleYAxis : register(u4);
RWStructuredBuffer<uint> ParticleCounts : register(u5);
RWStructuredBuffer<uint> DepositFixed : register(u6);

StructuredBuffer<float> Weights : register(t0);
StructuredBuffer<float4> GroupData0 : register(t1);
StructuredBuffer<float4> GroupData1 : register(t2);
StructuredBuffer<uint> VoxelFlags : register(t3);

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

    return Source[index];
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

[numthreads(256, 1, 1)]
void MoveParticlesAndDeposit(uint3 id : SV_DispatchThreadID)
{
    int particleIndex = id.x;
    if (particleIndex >= ParticleCount) return;

    float4 posGroup = ParticlePosition[particleIndex];
    float4 dirParent = ParticleDirection[particleIndex];
    float4 yWrapped = ParticleYAxis[particleIndex];

    int groupIndex = (int)round(posGroup.w);
    if (groupIndex < 0 || groupIndex >= GroupCount) return;

    float3 position = posGroup.xyz;
    float3 x = NormalizeOr(dirParent.xyz, float3(1, 0, 0));
    float3 y = SafeYAxis(x, yWrapped.xyz);

    float4 group0 = GroupData0[groupIndex];
    float4 group1 = GroupData1[groupIndex];

    float speed = group0.x;
    float sensorDistance = group0.y;
    float sensorCos = group0.z;
    float sensorSin = group0.w;
    float rotationCos = group1.x;
    float rotationSin = group1.y;
    float depositValue = group1.z;
    uint wanderFrequency = max(1u, (uint)round(group1.w));

    float3 leftSensor = position + (x * sensorCos - y * sensorSin) * sensorDistance;
    float3 frontSensor = position + x * sensorDistance;
    float3 rightSensor = position + (x * sensorCos + y * sensorSin) * sensorDistance;

    float value0 = SampleDensity(leftSensor);
    float value1 = SampleDensity(frontSensor);
    float value2 = SampleDensity(rightSensor);
    float value3 = -1.0;
    float value4 = -1.0;

    if (Tridimensional != 0)
    {
        float3 zAxis = NormalizeOr(cross(y, x), float3(0, 0, 1));
        value3 = SampleDensity(position + (x * sensorCos + zAxis * sensorSin) * sensorDistance);
        value4 = SampleDensity(position + (x * sensorCos - zAxis * sensorSin) * sensorDistance);
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

    float3 moveDirection = NormalizeOr(force + x, x);
    uint movementSeed = Hash((uint)particleIndex + (uint)Iteration * 2891336453u);
    if (wanderFrequency > 0u && movementSeed % wanderFrequency == 0u)
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
        parentIndex = (int)round(dirParent.w);
        nextPosition = position;
    }

    if (parentIndex >= 0 && parentIndex < VoxelCount)
    {
        uint previousCount = ParticleCounts[parentIndex];
        if (previousCount == 0u && CanDepositAtVoxel(parentIndex, sensorDistance))
        {
            uint fixedDeposit = (uint)round(max(0.0, depositValue * DepositScale));
            if (fixedDeposit > 0u)
            {
                InterlockedAdd(DepositFixed[parentIndex], fixedDeposit);
            }
        }
    }

    x = NormalizeOr(moveDirection, x);
    y = SafeYAxis(x, y);

    ParticlePosition[particleIndex] = float4(nextPosition, (float)groupIndex);
    ParticleDirection[particleIndex] = float4(x, (float)parentIndex);
    ParticleYAxis[particleIndex] = float4(y, (float)wrapped);
}

[numthreads(256, 1, 1)]
void ApplyDeposits(uint3 id : SV_DispatchThreadID)
{
    int index = id.x;
    if (index >= VoxelCount) return;

    uint fixedDeposit = DepositFixed[index];
    if (fixedDeposit == 0u) return;

    Source[index] += fixedDeposit / DepositScale;
    DepositFixed[index] = 0u;
}

[numthreads(256, 1, 1)]
void ClearParticleCounts(uint3 id : SV_DispatchThreadID)
{
    int index = id.x;
    if (index >= VoxelCount) return;
    ParticleCounts[index] = 0u;
}

[numthreads(256, 1, 1)]
void CountParticles(uint3 id : SV_DispatchThreadID)
{
    int index = id.x;
    if (index >= ParticleCount) return;

    int parentIndex = (int)round(ParticleDirection[index].w);
    if (parentIndex >= 0 && parentIndex < VoxelCount)
    {
        InterlockedAdd(ParticleCounts[parentIndex], 1u);
    }
}

float ClampPassDensity(float value, int x, int y, int z)
{
    if (value > 1.0) value = 1.0;

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
            weighted += Source[FlatIndex(sx, sy, sz)] * Weights[offset + Range];
        }
    }

    float value = Source[index] * Keep + Diffuse * weighted;
    Destination[index] = ClampPassDensity(value, x, y, z);
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

    float value = Source[index] - Decay;
    Destination[index] = value > 0.0 ? value : 0.0;
}";
    }
}
