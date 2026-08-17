using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

using SharpGen.Runtime;
using Vortice.D3DCompiler;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;

namespace Nuclei4
{
    internal sealed class GpuDiffusionStepResult
    {
        public double Milliseconds;
        public int Passes;
        public int Range;
        public bool Wrap;
    }

    internal sealed class GpuScalarDiffusionEngine : IDisposable
    {
        ID3D11Device device;
        ID3D11DeviceContext context;
        ID3D11ComputeShader diffusionShader;
        ID3D11ComputeShader decayShader;
        ID3D11Buffer densityA;
        ID3D11Buffer densityB;
        ID3D11Buffer readback;
        ID3D11Buffer parameterBuffer;
        ID3D11Buffer weightsBuffer;
        ID3D11UnorderedAccessView densityAView;
        ID3D11UnorderedAccessView densityBView;
        ID3D11ShaderResourceView weightsView;
        bool densityInA = true;
        int weightsRange = int.MinValue;
        int weightsCount = 0;

        readonly int resX;
        readonly int resY;
        readonly int resZ;
        readonly int voxelCount;
        readonly float[] readbackDensity;

        public GpuScalarDiffusionEngine(int resX, int resY, int resZ, float[] initialDensity)
        {
            this.resX = resX;
            this.resY = resY;
            this.resZ = resZ;
            voxelCount = Math.Max(0, resX * resY * resZ);
            readbackDensity = new float[voxelCount];

            if (voxelCount <= 0)
            {
                throw new ArgumentException("GPU diffusion requires at least one voxel.");
            }

            CreateDevice(out device, out context);
            CompileShaders();
            CreateDensityBuffers(initialDensity);
            CreateParameterBuffer();
        }

        public bool Matches(int x, int y, int z)
        {
            return resX == x && resY == y && resZ == z;
        }

        public GpuDiffusionStepResult Step(Voxel[,,] voxels, SolverGpuSettings settings, SolverGpuDimensionMode dimensionMode)
        {
            Stopwatch stopwatch = Stopwatch.StartNew();
            int passCount = 0;

            EnsureWeights(settings.DiffuseRange);

            if (settings.Diffuse > 0)
            {
                if (!dimensionMode.PlanarYZ)
                {
                    DispatchDiffusionPass(0, settings, dimensionMode);
                    SwapDensityBuffers();
                    passCount++;
                }

                if (!dimensionMode.PlanarXZ)
                {
                    DispatchDiffusionPass(1, settings, dimensionMode);
                    SwapDensityBuffers();
                    passCount++;
                }

                if (!dimensionMode.PlanarXY)
                {
                    DispatchDiffusionPass(2, settings, dimensionMode);
                    SwapDensityBuffers();
                    passCount++;
                }
            }

            DispatchDecayPass(settings, dimensionMode);
            SwapDensityBuffers();
            passCount++;

            ReadBackDensity();
            ApplyDensityToVoxels(voxels);

            stopwatch.Stop();

            return new GpuDiffusionStepResult
            {
                Milliseconds = stopwatch.Elapsed.TotalMilliseconds,
                Passes = passCount,
                Range = settings.DiffuseRange,
                Wrap = settings.WrapBoundaries
            };
        }

        void DispatchDiffusionPass(int axis, SolverGpuSettings settings, SolverGpuDimensionMode dimensionMode)
        {
            DiffusionParameters parameters = CreateParameters(axis, settings, dimensionMode);
            UpdateParameters(parameters);

            ID3D11UnorderedAccessView sourceView = densityInA ? densityAView : densityBView;
            ID3D11UnorderedAccessView destinationView = densityInA ? densityBView : densityAView;

            context.CSSetShader(diffusionShader);
            context.CSSetConstantBuffers(0, new ID3D11Buffer[] { parameterBuffer });
            context.CSSetShaderResources(0, new ID3D11ShaderResourceView[] { weightsView });
            context.CSSetUnorderedAccessView(0, sourceView, -1);
            context.CSSetUnorderedAccessView(1, destinationView, -1);
            context.Dispatch(DispatchGroupCount(voxelCount), 1, 1);
            UnbindComputeResources();
        }

        void DispatchDecayPass(SolverGpuSettings settings, SolverGpuDimensionMode dimensionMode)
        {
            DiffusionParameters parameters = CreateParameters(0, settings, dimensionMode);
            UpdateParameters(parameters);

            ID3D11UnorderedAccessView sourceView = densityInA ? densityAView : densityBView;
            ID3D11UnorderedAccessView destinationView = densityInA ? densityBView : densityAView;

            context.CSSetShader(decayShader);
            context.CSSetConstantBuffers(0, new ID3D11Buffer[] { parameterBuffer });
            context.CSSetUnorderedAccessView(0, sourceView, -1);
            context.CSSetUnorderedAccessView(1, destinationView, -1);
            context.Dispatch(DispatchGroupCount(voxelCount), 1, 1);
            UnbindComputeResources();
        }

        DiffusionParameters CreateParameters(int axis, SolverGpuSettings settings, SolverGpuDimensionMode dimensionMode)
        {
            DiffusionParameters parameters = new DiffusionParameters();
            parameters.ResX = resX;
            parameters.ResY = resY;
            parameters.ResZ = resZ;
            parameters.Axis = axis;
            parameters.Range = settings.DiffuseRange;
            parameters.Wrap = settings.WrapBoundaries ? 1 : 0;
            parameters.Tridimensional = dimensionMode.Tridimensional ? 1 : 0;
            parameters.PlanarXY = dimensionMode.PlanarXY ? 1 : 0;
            parameters.PlanarXZ = dimensionMode.PlanarXZ ? 1 : 0;
            parameters.PlanarYZ = dimensionMode.PlanarYZ ? 1 : 0;
            parameters.VoxelCount = voxelCount;
            parameters.Padding0 = 0;
            parameters.Keep = (float)(1.0 - settings.Diffuse);
            parameters.Diffuse = (float)settings.Diffuse;
            parameters.Decay = (float)settings.Decay;
            parameters.Padding1 = 0;
            return parameters;
        }

        void UpdateParameters(DiffusionParameters parameters)
        {
            context.UpdateSubresourceSafe(ref parameters, parameterBuffer, 0, 0, 0, 0, false);
        }

        void ReadBackDensity()
        {
            ID3D11Buffer source = densityInA ? densityA : densityB;
            context.CopyResource(readback, source);

            MappedSubresource mapped = context.Map(readback, MapMode.Read, Vortice.Direct3D11.MapFlags.None);
            try
            {
                Marshal.Copy(mapped.DataPointer, readbackDensity, 0, readbackDensity.Length);
            }
            finally
            {
                context.Unmap(readback);
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
                            voxel.density = readbackDensity[baseIndex + z];
                        }
                    }
                }
            }
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
            weightsCount = weights.Length;

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

            readback = device.CreateBuffer(
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

        void CreateParameterBuffer()
        {
            parameterBuffer = device.CreateBuffer(
                Marshal.SizeOf(typeof(DiffusionParameters)),
                BindFlags.ConstantBuffer,
                ResourceUsage.Default,
                CpuAccessFlags.None,
                ResourceOptionFlags.None,
                0);
        }

        void CompileShaders()
        {
            using (Blob diffusionBytecode = CompileShader(DiffusionShaderSource, "DiffuseAxis"))
            using (Blob decayBytecode = CompileShader(DiffusionShaderSource, "ApplyDecay"))
            {
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
                "NucleiGpuDiffusion",
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

                throw new InvalidOperationException("diffusion shader compile failed: " + result + " " + errors);
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
            context.CSSetUnorderedAccessView(0, null, -1);
            context.CSSetUnorderedAccessView(1, null, -1);
            context.CSSetShaderResource(0, null);
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
            if (densityA != null) densityA.Dispose();
            if (densityB != null) densityB.Dispose();
            if (readback != null) readback.Dispose();
            if (parameterBuffer != null) parameterBuffer.Dispose();
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

            weightsCount = 0;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct DiffusionParameters
        {
            public int ResX;
            public int ResY;
            public int ResZ;
            public int Axis;
            public int Range;
            public int Wrap;
            public int Tridimensional;
            public int PlanarXY;
            public int PlanarXZ;
            public int PlanarYZ;
            public int VoxelCount;
            public int Padding0;
            public float Keep;
            public float Diffuse;
            public float Decay;
            public float Padding1;
        }

        const string DiffusionShaderSource = @"
cbuffer Params : register(b0)
{
    int ResX;
    int ResY;
    int ResZ;
    int Axis;
    int Range;
    int Wrap;
    int Tridimensional;
    int PlanarXY;
    int PlanarXZ;
    int PlanarYZ;
    int VoxelCount;
    int Padding0;
    float Keep;
    float Diffuse;
    float Decay;
    float Padding1;
}

RWStructuredBuffer<float> Source : register(u0);
RWStructuredBuffer<float> Destination : register(u1);
StructuredBuffer<float> Weights : register(t0);

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
