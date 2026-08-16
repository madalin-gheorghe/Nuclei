using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;

using SharpGen.Runtime;
using Vortice.D3DCompiler;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;

namespace Nuclei3
{
    internal sealed class GpuComputeSmokeTestResult
    {
        public bool Available;
        public string Message;
        public string Driver;
        public FeatureLevel FeatureLevel;
        public double Milliseconds;
    }

    internal static class GpuComputeSmokeTest
    {
        static readonly object syncRoot = new object();
        static GpuComputeSmokeTestResult cachedResult;
        static readonly string[] managedDependencyNames = new string[]
        {
            "Microsoft.Bcl.HashCode",
            "SharpGen.Runtime",
            "SharpGen.Runtime.COM",
            "System.Buffers",
            "System.Memory",
            "System.Numerics.Vectors",
            "System.Resources.Extensions",
            "System.Runtime.CompilerServices.Unsafe",
            "Vortice.D3DCompiler",
            "Vortice.Direct3D11",
            "Vortice.DirectX",
            "Vortice.DXGI",
            "Vortice.Mathematics"
        };

        static GpuComputeSmokeTest()
        {
            AppDomain.CurrentDomain.AssemblyResolve += ResolveGpuDependency;
        }

        public static GpuComputeSmokeTestResult RunOnce()
        {
            lock (syncRoot)
            {
                if (cachedResult != null)
                {
                    return cachedResult;
                }

                cachedResult = Run();
                return cachedResult;
            }
        }

        static GpuComputeSmokeTestResult Run()
        {
            Stopwatch stopwatch = Stopwatch.StartNew();
            string stage = "start";

            try
            {
                ID3D11Device device;
                ID3D11DeviceContext context;
                FeatureLevel featureLevel;
                string driver;

                stage = "create D3D11 device";
                CreateDevice(out device, out context, out featureLevel, out driver);

                using (device)
                using (context)
                {
                    stage = "compile smoke shader";
                    using (Blob shaderBytecode = CompileSmokeShader())
                    {
                        stage = "create compute shader";
                        using (ID3D11ComputeShader shader = device.CreateComputeShader(shaderBytecode, null))
                        {
                            float[] input = new float[] { 1, 2, 3, 4, 5, 6, 7, 8 };
                            float[] output = new float[input.Length];

                            stage = "create structured buffer";
                            using (ID3D11Buffer valuesBuffer = device.CreateBuffer(
                                input,
                                BindFlags.UnorderedAccess,
                                ResourceUsage.Default,
                                CpuAccessFlags.None,
                                ResourceOptionFlags.BufferStructured,
                                input.Length * sizeof(float),
                                sizeof(float)))
                            {
                                stage = "create unordered access view";
                                using (ID3D11UnorderedAccessView valuesView = device.CreateUnorderedAccessView(
                                    valuesBuffer,
                                    new UnorderedAccessViewDescription(valuesBuffer, Format.Unknown, 0, input.Length, BufferUnorderedAccessViewFlags.None)))
                                {
                                    stage = "create staging buffer";
                                    using (ID3D11Buffer stagingBuffer = device.CreateBuffer(
                                        input.Length * sizeof(float),
                                        BindFlags.None,
                                        ResourceUsage.Staging,
                                        CpuAccessFlags.Read,
                                        ResourceOptionFlags.None,
                                        0))
                                    {
                                        stage = "dispatch compute shader";
                                        context.CSSetShader(shader);
                                        context.CSSetUnorderedAccessView(0, valuesView, -1);
                                        context.Dispatch(1, 1, 1);
                                        context.CSSetUnorderedAccessView(0, null, -1);
                                        context.CSSetShader(null);

                                        stage = "copy GPU result";
                                        context.CopyResource(stagingBuffer, valuesBuffer);

                                        stage = "map GPU result";
                                        MappedSubresource mapped = context.Map(stagingBuffer, MapMode.Read, Vortice.Direct3D11.MapFlags.None);
                                        try
                                        {
                                            Marshal.Copy(mapped.DataPointer, output, 0, output.Length);
                                        }
                                        finally
                                        {
                                            context.Unmap(stagingBuffer);
                                        }

                                        stage = "validate GPU result";
                                        ValidateOutput(input, output);
                                    }
                                }
                            }
                        }
                    }
                }

                stopwatch.Stop();

                return new GpuComputeSmokeTestResult
                {
                    Available = true,
                    Driver = driver,
                    FeatureLevel = featureLevel,
                    Milliseconds = stopwatch.Elapsed.TotalMilliseconds,
                    Message = "GPU compute ready"
                };
            }
            catch (Exception ex)
            {
                stopwatch.Stop();

                return new GpuComputeSmokeTestResult
                {
                    Available = false,
                    Driver = "",
                    FeatureLevel = 0,
                    Milliseconds = stopwatch.Elapsed.TotalMilliseconds,
                    Message = "GPU compute unavailable at " + stage + ": " + ex.Message
                };
            }
        }

        static Assembly ResolveGpuDependency(object sender, ResolveEventArgs args)
        {
            AssemblyName requestedName = new AssemblyName(args.Name);

            bool allowed = false;
            for (int i = 0; i < managedDependencyNames.Length; i++)
            {
                if (string.Equals(managedDependencyNames[i], requestedName.Name, StringComparison.OrdinalIgnoreCase))
                {
                    allowed = true;
                    break;
                }
            }

            if (!allowed)
            {
                return null;
            }

            Assembly[] loadedAssemblies = AppDomain.CurrentDomain.GetAssemblies();
            for (int i = 0; i < loadedAssemblies.Length; i++)
            {
                AssemblyName loadedName = loadedAssemblies[i].GetName();
                if (string.Equals(loadedName.Name, requestedName.Name, StringComparison.OrdinalIgnoreCase))
                {
                    return loadedAssemblies[i];
                }
            }

            string assemblyDirectory = Path.GetDirectoryName(typeof(GpuComputeSmokeTest).Assembly.Location);
            if (string.IsNullOrEmpty(assemblyDirectory))
            {
                return null;
            }

            string path = Path.Combine(assemblyDirectory, requestedName.Name + ".dll");
            if (!File.Exists(path))
            {
                return null;
            }

            return Assembly.LoadFrom(path);
        }

        static void CreateDevice(out ID3D11Device device, out ID3D11DeviceContext context, out FeatureLevel featureLevel, out string driver)
        {
            FeatureLevel[] levels = new FeatureLevel[] { FeatureLevel.Level_11_0 };

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
                driver = "hardware";
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

            if (result.Success)
            {
                driver = "warp";
                return;
            }

            throw new InvalidOperationException("D3D11CreateDevice failed: " + result);
        }

        static Blob CompileSmokeShader()
        {
            const string shaderSource = @"
RWStructuredBuffer<float> Values : register(u0);

[numthreads(64, 1, 1)]
void CSMain(uint3 id : SV_DispatchThreadID)
{
    if (id.x >= 8) return;
    Values[id.x] = Values[id.x] * 2.0 + 1.0;
}";

            Blob shaderBytecode = null;
            Blob errorBlob = null;

            Result result = Compiler.Compile(
                shaderSource,
                null,
                null,
                "CSMain",
                "NucleiGpuSmokeTest",
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

                throw new InvalidOperationException("shader compile failed: " + result + " " + errors);
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

        static void ValidateOutput(float[] input, float[] output)
        {
            for (int i = 0; i < input.Length; i++)
            {
                float expected = input[i] * 2.0f + 1.0f;
                if (Math.Abs(output[i] - expected) > 0.0001f)
                {
                    throw new InvalidOperationException("GPU smoke test returned " + output[i] + " at index " + i + ", expected " + expected);
                }
            }
        }
    }
}
