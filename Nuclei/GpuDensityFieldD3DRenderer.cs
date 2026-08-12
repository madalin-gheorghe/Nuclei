using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;

using Rhino.Display;
using Rhino.DocObjects;
using Rhino.Geometry;

using Vortice.D3DCompiler;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;

namespace Nuclei3
{
    internal sealed class GpuDensityFieldD3DRenderer
    {
        const int ConstantBufferFloatCount = 64;
        const int ConstantBufferBytes = ConstantBufferFloatCount * sizeof(float);
        const int FullscreenVertexCount = 3;
        const string StatusPath = @"C:\Nuclei\BenchmarkSuite1\NucleiGpuDensityFieldRenderer.txt";

        static readonly GpuDensityFieldD3DRenderer instance = new GpuDensityFieldD3DRenderer();

        readonly Dictionary<Guid, SharedTextureView> sharedTextureViews = new Dictionary<Guid, SharedTextureView>();

        IntPtr devicePtr = IntPtr.Zero;
        IntPtr contextPtr = IntPtr.Zero;
        ID3D11Device device;
        ID3D11DeviceContext1 context;
        ID3D11VertexShader vertexShader;
        ID3D11PixelShader pixelShader;
        ID3D11BlendState blendState;
        ID3D11RasterizerState rasterizerState;
        ID3D11DepthStencilState depthDisabledState;
        ID3D11Buffer constantBuffer;
        bool disabled;
        bool rhinoVersionUnsupported;
        bool loggedSuccess;

        GpuDensityFieldD3DRenderer()
        {
        }

        public static bool TryDraw(Guid solverId, DrawEventArgs e, GpuDensityFieldPreviewFrame frame)
        {
            return instance.TryDrawInternal(solverId, e, frame);
        }

        public static void Unregister(Guid solverId)
        {
            instance.UnregisterInternal(solverId);
        }

        bool TryDrawInternal(Guid solverId, DrawEventArgs e, GpuDensityFieldPreviewFrame frame)
        {
            if (disabled || e == null || frame == null || !frame.IsValid) return false;
            if (rhinoVersionUnsupported) return false;

            try
            {
                if (Rhino.RhinoApp.ExeVersion < 9)
                {
                    rhinoVersionUnsupported = true;
                    return false;
                }

                IntPtr currentDevicePtr;
                IntPtr currentContextPtr;
                if (!RhinoWipD3DPreviewProbe.TryGetRhinoD3D(e.Display, e.Viewport, out currentDevicePtr, out currentContextPtr))
                {
                    return false;
                }

                EnsureDevice(currentDevicePtr, currentContextPtr);
                if (device == null || context == null) return false;

                SharedTextureView textureView = GetSharedTextureView(solverId);
                if (!textureView.TryUpdate(device, frame))
                {
                    return false;
                }

                if (!UpdateConstants(e.Viewport, frame))
                {
                    return false;
                }

                D3DStateSnapshot snapshot = D3DStateSnapshot.Capture(context);
                try
                {
                    context.IASetInputLayout(null);
                    context.IASetPrimitiveTopology(PrimitiveTopology.TriangleList);

                    context.VSSetShader(vertexShader);
                    context.GSSetShader(null);
                    context.PSSetShader(pixelShader);
                    context.VSSetConstantBuffer(0, constantBuffer);
                    context.PSSetConstantBuffer(0, constantBuffer);
                    context.PSSetShaderResource(0, textureView.ShaderResourceView);

                    context.OMSetBlendState(blendState);
                    context.OMSetDepthStencilState(depthDisabledState, 0);
                    context.RSSetState(rasterizerState);

                    context.Draw(FullscreenVertexCount, 0);
                    context.PSSetShaderResource(0, null);
                }
                finally
                {
                    snapshot.Restore(context);
                    snapshot.Dispose();
                }

                if (!loggedSuccess)
                {
                    loggedSuccess = true;
                    WriteStatus("draw_success width=" + frame.Width.ToString(CultureInfo.InvariantCulture)
                        + " height=" + frame.Height.ToString(CultureInfo.InvariantCulture));
                }

                return true;
            }
            catch (Exception ex)
            {
                disabled = true;
                WriteStatus("disabled exception=" + ex.GetType().FullName + " message=" + ex.Message);
                return false;
            }
        }

        void EnsureDevice(IntPtr currentDevicePtr, IntPtr currentContextPtr)
        {
            if (devicePtr == currentDevicePtr && contextPtr == currentContextPtr && device != null && context != null)
            {
                return;
            }

            ReleaseDeviceResources();

            devicePtr = currentDevicePtr;
            contextPtr = currentContextPtr;

            Marshal.AddRef(devicePtr);
            Marshal.AddRef(contextPtr);

            device = new ID3D11Device(devicePtr);
            context = new ID3D11DeviceContext1(contextPtr);

            CreateResources();
            WriteStatus("device_ready device=0x" + devicePtr.ToInt64().ToString("X", CultureInfo.InvariantCulture)
                + " context=0x" + contextPtr.ToInt64().ToString("X", CultureInfo.InvariantCulture));
        }

        void CreateResources()
        {
            byte[] vertexShaderBytes = CompileShader(ShaderSource, "VSMain", "vs_4_0");
            byte[] pixelShaderBytes = CompileShader(ShaderSource, "PSMain", "ps_4_0");

            vertexShader = device.CreateVertexShader(vertexShaderBytes, null);
            pixelShader = device.CreatePixelShader(pixelShaderBytes, null);
            blendState = device.CreateBlendState(BlendDescription.NonPremultiplied);
            rasterizerState = device.CreateRasterizerState(RasterizerDescription.CullNone);
            depthDisabledState = device.CreateDepthStencilState(DepthStencilDescription.None);
            constantBuffer = device.CreateBuffer(
                ConstantBufferBytes,
                BindFlags.ConstantBuffer,
                ResourceUsage.Dynamic,
                CpuAccessFlags.Write,
                ResourceOptionFlags.None,
                0);
        }

        static byte[] CompileShader(string shaderSource, string entryPoint, string profile)
        {
            using (Vortice.Direct3D.Blob blob = Compiler.Compile(
                shaderSource,
                entryPoint,
                "NucleiGpuDensityFieldD3D",
                null,
                null,
                profile,
                ShaderFlags.OptimizationLevel3,
                EffectFlags.None))
            {
                return blob.AsBytes();
            }
        }

        bool UpdateConstants(RhinoViewport viewport, GpuDensityFieldPreviewFrame frame)
        {
            bool volumeMode = frame.VolumeMode;
            double[] screenToUv = new double[8];
            if (!volumeMode)
            {
                Transform worldToScreen = viewport.GetTransform(CoordinateSystem.World, CoordinateSystem.Screen);
                Point3d origin = frame.Origin;
                Vector3d axisU = frame.AxisU;
                Vector3d axisV = frame.AxisV;
                if (!TryCreateScreenToUv(
                    worldToScreen,
                    origin,
                    origin + axisU,
                    origin + axisV,
                    origin + axisU + axisV,
                    out screenToUv))
                {
                    return false;
                }
            }

            Transform screenToWorld = viewport.GetTransform(CoordinateSystem.Screen, CoordinateSystem.World);
            int maxResolution = Math.Max(frame.ResX, Math.Max(frame.ResY, frame.ResZ));
            int automaticVolumeSteps = Math.Min(112, Math.Max(32, maxResolution / 2));
            int volumeSteps = volumeMode
                ? (frame.VolumeSampleCount > 0 ? ClampInt(frame.VolumeSampleCount, 8, 160) : automaticVolumeSteps)
                : 0;
            float volumeOpacity = ClampFloat(frame.VolumeOpacity, 0.0f, 12.0f);
            float volumeContrast = frame.VolumeContrast > 0
                ? ClampFloat(frame.VolumeContrast, 0.01f, 12.0f)
                : (frame.PreviewScale > 0 ? ClampFloat(frame.PreviewScale, 0.01f, 12.0f) : 1.0f);
            float planarBackground = !volumeMode && (frame.ResX == 1 || frame.ResY == 1 || frame.ResZ == 1) ? 1.0f : 0.0f;

            float[] constants =
            {
                (float)screenToUv[0], (float)screenToUv[1], (float)screenToUv[2], 0.0f,
                (float)screenToUv[3], (float)screenToUv[4], (float)screenToUv[5], 0.0f,
                (float)screenToUv[6], (float)screenToUv[7], 1.0f, 0.0f,
                0.0f, 0.0f, 0.0f, 0.0f,
                Math.Max(1.0f, viewport.Size.Width),
                Math.Max(1.0f, viewport.Size.Height),
                Math.Max(1.0f, frame.Width),
                Math.Max(1.0f, frame.Height),
                frame.MinimumThreshold,
                frame.MaximumThreshold,
                frame.UseCustomColor ? 1.0f : 0.0f,
                frame.ValueIndex,
                frame.ColorR,
                frame.ColorG,
                frame.ColorB,
                frame.ColorA,
                0.0f, 0.0f, 0.0f, 0.0f,
                0.92f,
                volumeContrast,
                planarBackground,
                0.0f,
                (float)screenToWorld.M00, (float)screenToWorld.M01, (float)screenToWorld.M02, (float)screenToWorld.M03,
                (float)screenToWorld.M10, (float)screenToWorld.M11, (float)screenToWorld.M12, (float)screenToWorld.M13,
                (float)screenToWorld.M20, (float)screenToWorld.M21, (float)screenToWorld.M22, (float)screenToWorld.M23,
                (float)screenToWorld.M30, (float)screenToWorld.M31, (float)screenToWorld.M32, (float)screenToWorld.M33,
                frame.ResX * frame.VoxelSize,
                frame.ResY * frame.VoxelSize,
                frame.ResZ * frame.VoxelSize,
                volumeMode ? 1.0f : 0.0f,
                frame.ResX,
                frame.ResY,
                frame.ResZ,
                Math.Max(1, frame.AtlasColumns),
                Math.Max(1, frame.AtlasRows),
                volumeSteps,
                volumeOpacity,
                frame.VolumeRenderMode
            };

            MappedSubresource mapped = context.Map(constantBuffer, MapMode.WriteDiscard, Vortice.Direct3D11.MapFlags.None);
            try
            {
                Marshal.Copy(constants, 0, mapped.DataPointer, constants.Length);
            }
            finally
            {
                context.Unmap(constantBuffer);
            }

            return true;
        }

        static bool TryCreateScreenToUv(
            Transform worldToScreen,
            Point3d uv00,
            Point3d uv10,
            Point3d uv01,
            Point3d uv11,
            out double[] homography)
        {
            homography = null;

            double x00, y00, x10, y10, x01, y01, x11, y11;
            if (!TryProject(worldToScreen, uv00, out x00, out y00)) return false;
            if (!TryProject(worldToScreen, uv10, out x10, out y10)) return false;
            if (!TryProject(worldToScreen, uv01, out x01, out y01)) return false;
            if (!TryProject(worldToScreen, uv11, out x11, out y11)) return false;

            double[,] matrix = new double[8, 9];
            AddHomographyRows(matrix, 0, x00, y00, 0.0, 0.0);
            AddHomographyRows(matrix, 2, x10, y10, 1.0, 0.0);
            AddHomographyRows(matrix, 4, x01, y01, 0.0, 1.0);
            AddHomographyRows(matrix, 6, x11, y11, 1.0, 1.0);

            double[] solved;
            if (!SolveLinearSystem(matrix, 8, out solved)) return false;

            homography = solved;
            return true;
        }

        static bool TryProject(Transform transform, Point3d point, out double x, out double y)
        {
            double sx = transform.M00 * point.X + transform.M01 * point.Y + transform.M02 * point.Z + transform.M03;
            double sy = transform.M10 * point.X + transform.M11 * point.Y + transform.M12 * point.Z + transform.M13;
            double sw = transform.M30 * point.X + transform.M31 * point.Y + transform.M32 * point.Z + transform.M33;

            x = 0;
            y = 0;
            if (Math.Abs(sw) < 1e-9) return false;

            x = sx / sw;
            y = sy / sw;
            return IsFinite(x) && IsFinite(y);
        }

        static void AddHomographyRows(double[,] matrix, int row, double x, double y, double u, double v)
        {
            matrix[row, 0] = x;
            matrix[row, 1] = y;
            matrix[row, 2] = 1.0;
            matrix[row, 6] = -u * x;
            matrix[row, 7] = -u * y;
            matrix[row, 8] = u;

            matrix[row + 1, 3] = x;
            matrix[row + 1, 4] = y;
            matrix[row + 1, 5] = 1.0;
            matrix[row + 1, 6] = -v * x;
            matrix[row + 1, 7] = -v * y;
            matrix[row + 1, 8] = v;
        }

        static bool SolveLinearSystem(double[,] matrix, int size, out double[] solution)
        {
            solution = new double[size];

            for (int pivot = 0; pivot < size; pivot++)
            {
                int bestRow = pivot;
                double best = Math.Abs(matrix[pivot, pivot]);
                for (int row = pivot + 1; row < size; row++)
                {
                    double candidate = Math.Abs(matrix[row, pivot]);
                    if (candidate > best)
                    {
                        best = candidate;
                        bestRow = row;
                    }
                }

                if (best < 1e-9) return false;

                if (bestRow != pivot)
                {
                    for (int column = pivot; column <= size; column++)
                    {
                        double tmp = matrix[pivot, column];
                        matrix[pivot, column] = matrix[bestRow, column];
                        matrix[bestRow, column] = tmp;
                    }
                }

                double divisor = matrix[pivot, pivot];
                for (int column = pivot; column <= size; column++)
                {
                    matrix[pivot, column] /= divisor;
                }

                for (int row = 0; row < size; row++)
                {
                    if (row == pivot) continue;

                    double factor = matrix[row, pivot];
                    if (Math.Abs(factor) < 1e-12) continue;

                    for (int column = pivot; column <= size; column++)
                    {
                        matrix[row, column] -= factor * matrix[pivot, column];
                    }
                }
            }

            for (int row = 0; row < size; row++)
            {
                solution[row] = matrix[row, size];
                if (!IsFinite(solution[row])) return false;
            }

            return true;
        }

        static bool IsFinite(double value)
        {
            return !double.IsNaN(value) && !double.IsInfinity(value);
        }

        static int ClampInt(int value, int minimum, int maximum)
        {
            if (value < minimum) return minimum;
            if (value > maximum) return maximum;
            return value;
        }

        static float ClampFloat(float value, float minimum, float maximum)
        {
            if (float.IsNaN(value) || float.IsInfinity(value)) return minimum;
            if (value < minimum) return minimum;
            if (value > maximum) return maximum;
            return value;
        }

        SharedTextureView GetSharedTextureView(Guid solverId)
        {
            SharedTextureView textureView;
            if (!sharedTextureViews.TryGetValue(solverId, out textureView))
            {
                textureView = new SharedTextureView();
                sharedTextureViews[solverId] = textureView;
            }

            return textureView;
        }

        void UnregisterInternal(Guid solverId)
        {
            SharedTextureView textureView;
            if (sharedTextureViews.TryGetValue(solverId, out textureView))
            {
                textureView.Dispose();
                sharedTextureViews.Remove(solverId);
            }
        }

        void ReleaseDeviceResources()
        {
            foreach (SharedTextureView textureView in sharedTextureViews.Values)
            {
                textureView.Dispose();
            }
            sharedTextureViews.Clear();

            DisposeCom(constantBuffer);
            DisposeCom(depthDisabledState);
            DisposeCom(rasterizerState);
            DisposeCom(blendState);
            DisposeCom(pixelShader);
            DisposeCom(vertexShader);
            DisposeCom(context);
            DisposeCom(device);

            constantBuffer = null;
            depthDisabledState = null;
            rasterizerState = null;
            blendState = null;
            pixelShader = null;
            vertexShader = null;
            context = null;
            device = null;
            devicePtr = IntPtr.Zero;
            contextPtr = IntPtr.Zero;
            loggedSuccess = false;
        }

        static void DisposeCom(IDisposable disposable)
        {
            if (disposable != null) disposable.Dispose();
        }

        static void WriteStatus(string message)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(StatusPath));
                File.AppendAllText(
                    StatusPath,
                    DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture) + " " + message + System.Environment.NewLine);
            }
            catch
            {
            }
        }

        sealed class SharedTextureView : IDisposable
        {
            public ID3D11ShaderResourceView ShaderResourceView;
            IntPtr sharedHandle = IntPtr.Zero;
            int width;
            int height;
            bool colorTexture;

            public bool TryUpdate(ID3D11Device device, GpuDensityFieldPreviewFrame frame)
            {
                if (ShaderResourceView != null
                    && sharedHandle == frame.SharedHandle
                    && width == frame.Width
                    && height == frame.Height
                    && colorTexture == frame.ColorTexture)
                {
                    return true;
                }

                Dispose();

                try
                {
                    ID3D11Texture2D sharedTexture = device.OpenSharedResource<ID3D11Texture2D>(frame.SharedHandle);
                    try
                    {
                        ShaderResourceView = device.CreateShaderResourceView(
                            sharedTexture,
                            new ShaderResourceViewDescription(
                                sharedTexture,
                                ShaderResourceViewDimension.Texture2D,
                                frame.ColorTexture ? Format.R32G32B32A32_Float : Format.R32_Float,
                                0,
                                1,
                                0,
                                1));

                        sharedHandle = frame.SharedHandle;
                        width = frame.Width;
                        height = frame.Height;
                        colorTexture = frame.ColorTexture;
                        return true;
                    }
                    finally
                    {
                        DisposeCom(sharedTexture);
                    }
                }
                catch (Exception ex)
                {
                    WriteStatus("open_shared_failed handle=0x" + frame.SharedHandle.ToInt64().ToString("X", CultureInfo.InvariantCulture)
                        + " exception=" + ex.GetType().FullName
                        + " message=" + ex.Message);
                    return false;
                }
            }

            public void Dispose()
            {
                DisposeCom(ShaderResourceView);
                ShaderResourceView = null;
                sharedHandle = IntPtr.Zero;
                width = 0;
                height = 0;
                colorTexture = false;
            }
        }

        sealed class D3DStateSnapshot : IDisposable
        {
            readonly ID3D11InputLayout inputLayout;
            readonly PrimitiveTopology primitiveTopology;
            readonly ID3D11VertexShader vertexShader;
            readonly ID3D11GeometryShader geometryShader;
            readonly ID3D11PixelShader pixelShader;
            readonly ID3D11Buffer[] vertexConstantBuffers = new ID3D11Buffer[1];
            readonly ID3D11Buffer[] pixelConstantBuffers = new ID3D11Buffer[1];
            readonly ID3D11ShaderResourceView[] pixelResources = new ID3D11ShaderResourceView[1];
            readonly ID3D11BlendState blendState;
            readonly ID3D11DepthStencilState depthStencilState;
            readonly int stencilRef;
            readonly ID3D11RasterizerState rasterizerState;
            bool restored;

            D3DStateSnapshot(ID3D11DeviceContext1 context)
            {
                inputLayout = context.IAGetInputLayout();
                primitiveTopology = context.IAGetPrimitiveTopology();
                vertexShader = context.VSGetShader();
                geometryShader = context.GSGetShader();
                pixelShader = context.PSGetShader();
                context.VSGetConstantBuffers(0, vertexConstantBuffers);
                context.PSGetConstantBuffers(0, pixelConstantBuffers);
                context.PSGetShaderResources(0, pixelResources);
                blendState = context.OMGetBlendState();
                context.OMGetDepthStencilState(out depthStencilState, out stencilRef);
                rasterizerState = context.RSGetState();
            }

            public static D3DStateSnapshot Capture(ID3D11DeviceContext1 context)
            {
                return new D3DStateSnapshot(context);
            }

            public void Restore(ID3D11DeviceContext1 context)
            {
                if (restored) return;
                restored = true;

                context.IASetInputLayout(inputLayout);
                context.IASetPrimitiveTopology(primitiveTopology);
                context.VSSetShader(vertexShader);
                context.GSSetShader(geometryShader);
                context.PSSetShader(pixelShader);
                context.VSSetConstantBuffers(0, vertexConstantBuffers);
                context.PSSetConstantBuffers(0, pixelConstantBuffers);
                context.PSSetShaderResources(0, pixelResources);
                context.OMSetBlendState(blendState);
                context.OMSetDepthStencilState(depthStencilState, stencilRef);
                context.RSSetState(rasterizerState);
            }

            public void Dispose()
            {
                DisposeCom(inputLayout);
                DisposeCom(vertexShader);
                DisposeCom(geometryShader);
                DisposeCom(pixelShader);
                DisposeCom(vertexConstantBuffers[0]);
                DisposeCom(pixelConstantBuffers[0]);
                DisposeCom(pixelResources[0]);
                DisposeCom(blendState);
                DisposeCom(depthStencilState);
                DisposeCom(rasterizerState);
            }
        }

        const string ShaderSource = @"
cbuffer FieldConstants : register(b0)
{
    float4 ScreenToUv0;
    float4 ScreenToUv1;
    float4 ScreenToUv2;
    float4 UnusedTransform;
    float4 ViewportAndTexture;
    float4 Thresholds;
    float4 CustomColor;
    float4 Unused2;
    float4 Style;
    row_major float4x4 ScreenToWorld;
    float4 VolumeBox;
    float4 VolumeGrid;
    float4 VolumeAtlas;
};

Texture2D<float4> DensityTexture : register(t0);

struct VSOutput
{
    float4 Position : SV_POSITION;
};

VSOutput VSMain(uint vertexId : SV_VertexID)
{
    VSOutput output;
    if (vertexId == 0)
    {
        output.Position = float4(-1.0, -1.0, 0.52, 1.0);
    }
    else if (vertexId == 1)
    {
        output.Position = float4(-1.0, 3.0, 0.52, 1.0);
    }
    else
    {
        output.Position = float4(3.0, -1.0, 0.52, 1.0);
    }

    return output;
}

float2 ScreenToUv(float2 screen)
{
    float denom = dot(ScreenToUv2.xyz, float3(screen, 1.0));
    if (abs(denom) <= 0.000001)
    {
        return float2(-10.0, -10.0);
    }

    float u = dot(ScreenToUv0.xyz, float3(screen, 1.0)) / denom;
    float v = dot(ScreenToUv1.xyz, float3(screen, 1.0)) / denom;
    return float2(u, v);
}

bool InPreviewRange(float value)
{
    bool allowZero = Thresholds.w > 0.5 && Thresholds.w < 1.5;
    return (allowZero || value > 0.001) && value >= Thresholds.x && value <= Thresholds.y;
}

bool IsDynamicColorPreview()
{
    return Thresholds.w > 5.5;
}

float PreviewValue(float4 sample)
{
    if (!IsDynamicColorPreview()) return sample.r;

    float food = sample.a;
    if (Thresholds.w < 6.5) return food;
    if (Thresholds.w < 7.5) return max(food, sample.r);
    if (Thresholds.w < 8.5) return max(food, sample.g);
    if (Thresholds.w < 9.5) return max(food, sample.b);
    if (Thresholds.w < 10.5) return max(food, max(sample.g, sample.b));
    return max(food, max(sample.r, max(sample.g, sample.b)));
}

float3 PreviewColor(float value, float n)
{
    float glow = smoothstep(0.08, 0.95, n);
    float hot = smoothstep(0.55, 1.0, n);
    float3 baseColor = lerp(float3(0.01, 0.025, 0.035), float3(0.10, 0.92, 0.72), glow);
    float3 color = lerp(baseColor, float3(1.0, 0.78, 0.28), hot);
    if (Thresholds.z > 0.5)
    {
        return CustomColor.rgb;
    }

    if (Thresholds.w < 6.5)
    {
        return float3(n, n, n);
    }

    if (Thresholds.w < 7.5)
    {
        return float3(0.874510, 1.0, 0.482353) * n;
    }

    if (Thresholds.w < 8.5)
    {
        return float3(0.223529, 1.0, 0.666667) * n;
    }

    if (Thresholds.w < 9.5)
    {
        return float3(1.0, 0.0, 0.392157) * n;
    }

    return color;
}

float3 PreviewSampleColor(float4 sample, float value, float n)
{
    if (IsDynamicColorPreview())
    {
        float foodVisual = saturate(sample.a * Style.y);
        float slimeVisual = saturate(sample.r * Style.y);
        float foodPheromoneVisual = saturate(sample.g * Style.y);
        float basePheromoneVisual = saturate(sample.b * Style.y);
        float3 slimeColor = Thresholds.z > 0.5 ? CustomColor.rgb : float3(0.874510, 1.0, 0.482353);
        float3 foodPheromoneColor = Thresholds.z > 0.5 && Thresholds.w < 8.5 ? CustomColor.rgb : float3(0.223529, 1.0, 0.666667);
        float3 basePheromoneColor = Thresholds.z > 0.5 && Thresholds.w < 9.5 ? CustomColor.rgb : float3(1.0, 0.0, 0.392157);

        float3 foreground = 0.0;
        float foregroundStrength = 0.0;
        if (Thresholds.w > 6.5 && Thresholds.w < 7.5)
        {
            foreground = slimeColor * slimeVisual;
            foregroundStrength = slimeVisual;
        }
        else if (Thresholds.w < 8.5 && Thresholds.w > 7.5)
        {
            foreground = foodPheromoneColor * foodPheromoneVisual;
            foregroundStrength = foodPheromoneVisual;
        }
        else if (Thresholds.w < 9.5 && Thresholds.w > 8.5)
        {
            foreground = basePheromoneColor * basePheromoneVisual;
            foregroundStrength = basePheromoneVisual;
        }
        else if (Thresholds.w < 10.5 && Thresholds.w > 9.5)
        {
            foreground = foodPheromoneColor * foodPheromoneVisual + basePheromoneColor * basePheromoneVisual;
            foregroundStrength = max(foodPheromoneVisual, basePheromoneVisual);
        }
        else if (Thresholds.w > 10.5)
        {
            foreground = slimeColor * slimeVisual
                + foodPheromoneColor * foodPheromoneVisual
                + basePheromoneColor * basePheromoneVisual;
            foregroundStrength = max(slimeVisual, max(foodPheromoneVisual, basePheromoneVisual));
        }

        float3 foodBackground = float3(foodVisual, foodVisual, foodVisual) * (1.0 - saturate(foregroundStrength));
        return saturate(foodBackground + foreground);
    }

    return PreviewColor(value, n);
}

float4 RenderPlane(VSOutput input)
{
    float2 uv = ScreenToUv(input.Position.xy);
    if (uv.x < 0.0 || uv.x > 1.0 || uv.y < 0.0 || uv.y > 1.0)
    {
        clip(-1.0);
    }

    float2 textureSize = max(ViewportAndTexture.zw, float2(1.0, 1.0));
    int2 texel = clamp((int2)floor(saturate(uv) * textureSize), int2(0, 0), (int2)textureSize - int2(1, 1));
    float4 sample = DensityTexture.Load(int3(texel, 0));
    float value = PreviewValue(sample);
    bool inRange = InPreviewRange(value);
    float n = saturate(value * Style.y);

    if (Style.z <= 0.5)
    {
        if (!inRange)
        {
            clip(-1.0);
        }
    }

    float3 color = PreviewSampleColor(sample, value, n);
    if (Thresholds.z > 0.5 && !IsDynamicColorPreview())
    {
        color = CustomColor.rgb * n;
    }
    else if (Thresholds.w > 0.5 && Thresholds.w < 1.5 && value < 0.01)
    {
        color = float3(0.113725, 0.074510, 0.207843);
    }

    if (Style.z > 0.5 && !inRange)
    {
        color = float3(0.0, 0.0, 0.0);
    }
    float alpha = Style.z > 0.5 ? 1.0 : saturate(0.12 + n * Style.x);
    return float4(color, alpha);
}

float3 ScreenToWorldPoint(float2 screen, float depth)
{
    float4 world = mul(ScreenToWorld, float4(screen.x, screen.y, depth, 1.0));
    if (abs(world.w) <= 0.000001)
    {
        return world.xyz;
    }

    return world.xyz / world.w;
}

bool IntersectVolumeBox(float3 origin, float3 direction, out float enterT, out float exitT)
{
    float3 safeDirection = direction;
    safeDirection.x = abs(safeDirection.x) < 0.000001 ? (safeDirection.x < 0.0 ? -0.000001 : 0.000001) : safeDirection.x;
    safeDirection.y = abs(safeDirection.y) < 0.000001 ? (safeDirection.y < 0.0 ? -0.000001 : 0.000001) : safeDirection.y;
    safeDirection.z = abs(safeDirection.z) < 0.000001 ? (safeDirection.z < 0.0 ? -0.000001 : 0.000001) : safeDirection.z;

    float3 boxMin = float3(0.0, 0.0, 0.0);
    float3 boxMax = max(VolumeBox.xyz, float3(0.0001, 0.0001, 0.0001));
    float3 invDirection = 1.0 / safeDirection;
    float3 t0 = (boxMin - origin) * invDirection;
    float3 t1 = (boxMax - origin) * invDirection;
    float3 tNear = min(t0, t1);
    float3 tFar = max(t0, t1);
    enterT = max(max(tNear.x, tNear.y), tNear.z);
    exitT = min(min(tFar.x, tFar.y), tFar.z);
    return exitT > max(enterT, 0.0);
}

float4 LoadVolumeAtlasVoxel(int x, int y, int z)
{
    int resX = max((int)VolumeGrid.x, 1);
    int resY = max((int)VolumeGrid.y, 1);
    int resZ = max((int)VolumeGrid.z, 1);
    int columns = max((int)VolumeGrid.w, 1);
    int rows = max((int)VolumeAtlas.x, 1);

    x = clamp(x, 0, resX - 1);
    y = clamp(y, 0, resY - 1);
    z = clamp(z, 0, resZ - 1);

    int column = z % columns;
    int row = z / columns;
    if (row >= rows)
    {
        return 0.0;
    }

    return DensityTexture.Load(int3(column * resX + x, row * resY + y, 0));
}

float4 SampleVolumeAtlasNearest(float3 worldPosition)
{
    int resX = max((int)VolumeGrid.x, 1);
    int resY = max((int)VolumeGrid.y, 1);
    int resZ = max((int)VolumeGrid.z, 1);

    float3 normalized = saturate(worldPosition / max(VolumeBox.xyz, float3(0.0001, 0.0001, 0.0001)));
    int x = clamp((int)floor(normalized.x * (float)resX), 0, resX - 1);
    int y = clamp((int)floor(normalized.y * (float)resY), 0, resY - 1);
    int z = clamp((int)floor(normalized.z * (float)resZ), 0, resZ - 1);
    return LoadVolumeAtlasVoxel(x, y, z);
}

float4 SampleVolumeAtlasTrilinear(float3 worldPosition)
{
    int resX = max((int)VolumeGrid.x, 1);
    int resY = max((int)VolumeGrid.y, 1);
    int resZ = max((int)VolumeGrid.z, 1);

    float3 normalized = saturate(worldPosition / max(VolumeBox.xyz, float3(0.0001, 0.0001, 0.0001)));
    float3 coord = normalized * float3((float)max(resX - 1, 0), (float)max(resY - 1, 0), (float)max(resZ - 1, 0));
    int x0 = clamp((int)floor(coord.x), 0, resX - 1);
    int y0 = clamp((int)floor(coord.y), 0, resY - 1);
    int z0 = clamp((int)floor(coord.z), 0, resZ - 1);
    int x1 = min(x0 + 1, resX - 1);
    int y1 = min(y0 + 1, resY - 1);
    int z1 = min(z0 + 1, resZ - 1);
    float3 f = frac(coord);

    float4 c000 = LoadVolumeAtlasVoxel(x0, y0, z0);
    float4 c100 = LoadVolumeAtlasVoxel(x1, y0, z0);
    float4 c010 = LoadVolumeAtlasVoxel(x0, y1, z0);
    float4 c110 = LoadVolumeAtlasVoxel(x1, y1, z0);
    float4 c001 = LoadVolumeAtlasVoxel(x0, y0, z1);
    float4 c101 = LoadVolumeAtlasVoxel(x1, y0, z1);
    float4 c011 = LoadVolumeAtlasVoxel(x0, y1, z1);
    float4 c111 = LoadVolumeAtlasVoxel(x1, y1, z1);

    float4 c00 = lerp(c000, c100, f.x);
    float4 c10 = lerp(c010, c110, f.x);
    float4 c01 = lerp(c001, c101, f.x);
    float4 c11 = lerp(c011, c111, f.x);
    float4 c0 = lerp(c00, c10, f.y);
    float4 c1 = lerp(c01, c11, f.y);
    return lerp(c0, c1, f.z);
}

float FancyTransfer(float normalizedValue)
{
    float visible = smoothstep(0.025, 0.18, normalizedValue);
    float shaped = pow(saturate(normalizedValue), 0.72);
    float highlight = smoothstep(0.52, 1.0, normalizedValue) * 0.22;
    return saturate(visible * shaped + highlight);
}

float3 EstimateVolumeGradient(float3 worldPosition)
{
    int resX = max((int)VolumeGrid.x, 1);
    int resY = max((int)VolumeGrid.y, 1);
    int resZ = max((int)VolumeGrid.z, 1);
    float3 voxelStep = VolumeBox.xyz / max(float3((float)resX, (float)resY, (float)resZ), float3(1.0, 1.0, 1.0));

    float dx = PreviewValue(SampleVolumeAtlasTrilinear(worldPosition + float3(voxelStep.x, 0.0, 0.0)))
        - PreviewValue(SampleVolumeAtlasTrilinear(worldPosition - float3(voxelStep.x, 0.0, 0.0)));
    float dy = PreviewValue(SampleVolumeAtlasTrilinear(worldPosition + float3(0.0, voxelStep.y, 0.0)))
        - PreviewValue(SampleVolumeAtlasTrilinear(worldPosition - float3(0.0, voxelStep.y, 0.0)));
    float dz = PreviewValue(SampleVolumeAtlasTrilinear(worldPosition + float3(0.0, 0.0, voxelStep.z)))
        - PreviewValue(SampleVolumeAtlasTrilinear(worldPosition - float3(0.0, 0.0, voxelStep.z)));

    return float3(dx, dy, dz);
}

float3 ApplyFancyLighting(float3 color, float3 worldPosition, float3 rayDirection, float depthAlongRay)
{
    float3 gradient = EstimateVolumeGradient(worldPosition);
    float gradientStrength = length(gradient);
    if (gradientStrength <= 0.00001)
    {
        float flatDepthCue = lerp(1.08, 0.62, saturate(depthAlongRay));
        return color * flatDepthCue;
    }

    float3 normal = normalize(gradient);
    float3 lightDirection = normalize(float3(-0.35, -0.55, 0.75));
    float diffuseA = saturate(dot(normal, lightDirection));
    float diffuseB = saturate(dot(-normal, lightDirection)) * 0.45;
    float rim = pow(saturate(1.0 - abs(dot(normal, -rayDirection))), 2.0) * 0.22;
    float depthCue = lerp(1.12, 0.58, saturate(depthAlongRay));
    float lighting = (0.58 + diffuseA * 0.42 + diffuseB + rim) * depthCue;
    return color * lighting;
}

float LightningGlowTransfer(float normalizedValue)
{
    float visible = smoothstep(0.055, 0.28, normalizedValue);
    return saturate(visible * pow(saturate(normalizedValue), 1.35));
}

float LightningCoreTransfer(float normalizedValue)
{
    float core = smoothstep(0.44, 0.88, normalizedValue);
    return saturate(core * core);
}

float3 LightningColor(float value, float glow, float core, float depthAlongRay)
{
    if (Thresholds.z > 0.5)
    {
        return CustomColor.rgb * saturate(glow + core);
    }

    float3 coolHalo = float3(0.11, 0.06, 0.26);
    float3 warmHalo = float3(1.0, 0.58, 0.22);
    float3 whiteCore = float3(1.0, 0.96, 0.88);
    float3 color = lerp(coolHalo, warmHalo, saturate(glow * 1.35));
    color = lerp(color, whiteCore, saturate(core * 1.45));

    float farTint = saturate(depthAlongRay);
    color = lerp(color, color * float3(0.78, 0.84, 1.18), farTint * 0.22);
    return color;
}

float4 RenderLightningVolume(float3 nearWorld, float3 direction, float startT, float travel, float stepLength, int steps)
{
    float t = startT + stepLength * 0.5;
    float maximumValue = 0.0;
    float maximumGlow = 0.0;
    float maximumCore = 0.0;
    float maximumDepth = 0.0;
    float3 maximumPosition = nearWorld + direction * startT;
    float4 maximumSample = 0.0;
    float4 accumulated = float4(0.0, 0.0, 0.0, 0.0);

    [loop]
    for (int i = 0; i < 160; i++)
    {
        if (i >= steps || accumulated.a > 0.92)
        {
            break;
        }

        float3 p = nearWorld + direction * t;
        float4 sample = SampleVolumeAtlasTrilinear(p);
        float value = PreviewValue(sample);
        if (InPreviewRange(value))
        {
            float n = saturate(value * Style.y);
            float glow = LightningGlowTransfer(n);
            float core = LightningCoreTransfer(n);
            float signal = saturate(glow * 0.55 + core);
            float depthAlongRay = saturate((t - startT) / travel);

            if (signal > maximumGlow * 0.55 + maximumCore)
            {
                maximumValue = value;
                maximumGlow = glow;
                maximumCore = core;
                maximumDepth = depthAlongRay;
                maximumPosition = p;
                maximumSample = sample;
            }

            float depthFade = lerp(1.10, 0.60, depthAlongRay);
            float glowAlpha = glow * glow * 0.92;
            float coreAlpha = core * 2.35;
            float sampleAlpha = saturate((glowAlpha + coreAlpha) * (3.35 / max((float)steps, 1.0)) * VolumeAtlas.z * depthFade);
            float3 color = IsDynamicColorPreview()
                ? PreviewSampleColor(sample, value, signal)
                : LightningColor(value, glow, core, depthAlongRay);

            accumulated.rgb += (1.0 - accumulated.a) * sampleAlpha * color;
            accumulated.a += (1.0 - accumulated.a) * sampleAlpha;
        }

        t += stepLength;
    }

    if (maximumGlow <= 0.002 && accumulated.a <= 0.002)
    {
        clip(-1.0);
    }

    float edgeStrength = saturate(length(EstimateVolumeGradient(maximumPosition)) * 18.0);
    float3 coreColor = IsDynamicColorPreview()
        ? PreviewSampleColor(maximumSample, maximumValue, saturate(maximumGlow + maximumCore))
        : LightningColor(maximumValue, maximumGlow, maximumCore, maximumDepth);
    coreColor = ApplyFancyLighting(coreColor, maximumPosition, direction, maximumDepth);
    float3 glowColor = accumulated.a > 0.002 ? accumulated.rgb / max(accumulated.a, 0.001) : coreColor;

    float coreAlpha = saturate((0.08 + maximumGlow * 0.26 + maximumCore * 0.92) * VolumeAtlas.z);
    float edgeBoost = lerp(0.82, 1.24, edgeStrength);
    float finalAlpha = saturate((accumulated.a * 0.66 + coreAlpha * 0.48) * edgeBoost);
    float coreBlend = saturate(0.42 + maximumCore * 0.42 + edgeStrength * 0.16);
    float3 finalColor = lerp(glowColor, coreColor, coreBlend);
    return float4(finalColor, finalAlpha);
}

float4 RenderVolume(VSOutput input)
{
    float3 nearWorld = ScreenToWorldPoint(input.Position.xy, 0.0);
    float3 farWorld = ScreenToWorldPoint(input.Position.xy, 1.0);
    float3 direction = normalize(farWorld - nearWorld);

    float enterT;
    float exitT;
    if (!IntersectVolumeBox(nearWorld, direction, enterT, exitT))
    {
        clip(-1.0);
    }

    int steps = clamp((int)VolumeAtlas.y, 8, 160);
    float startT = max(enterT, 0.0);
    float travel = max(exitT - startT, 0.0001);
    float stepLength = travel / (float)steps;
    float t = startT + stepLength * 0.5;
    if (VolumeAtlas.w > 1.5)
    {
        return RenderLightningVolume(nearWorld, direction, startT, travel, stepLength, steps);
    }

    bool maximumIntensityMode = VolumeAtlas.w > 0.5;

    if (maximumIntensityMode)
    {
        float maximumValue = 0.0;
        float maximumVisual = 0.0;
        float maximumDepth = 0.0;
        float3 maximumPosition = nearWorld + direction * startT;
        float4 maximumSample = 0.0;
        float4 accumulated = float4(0.0, 0.0, 0.0, 0.0);

        [loop]
        for (int i = 0; i < 160; i++)
        {
            if (i >= steps)
            {
                break;
            }

            float3 p = nearWorld + direction * t;
            float4 sample = SampleVolumeAtlasTrilinear(p);
            float value = PreviewValue(sample);
            if (InPreviewRange(value))
            {
                float n = saturate(value * Style.y);
                float visual = FancyTransfer(n);
                float depthAlongRay = saturate((t - startT) / travel);
                if (visual > maximumVisual)
                {
                    maximumVisual = visual;
                    maximumValue = value;
                    maximumDepth = depthAlongRay;
                    maximumPosition = p;
                    maximumSample = sample;
                }

                float depthAlpha = lerp(1.12, 0.55, depthAlongRay);
                float sampleAlpha = saturate(visual * visual * (2.7 / max((float)steps, 1.0)) * VolumeAtlas.z * depthAlpha);
                float3 volumeColor = PreviewSampleColor(sample, value, visual);
                if (Thresholds.z > 0.5 && !IsDynamicColorPreview())
                {
                    volumeColor = CustomColor.rgb * visual;
                }

                accumulated.rgb += (1.0 - accumulated.a) * sampleAlpha * volumeColor;
                accumulated.a += (1.0 - accumulated.a) * sampleAlpha;
            }

            t += stepLength;
        }

        if (maximumVisual <= 0.002 && accumulated.a <= 0.002)
        {
            clip(-1.0);
        }

        float3 mipColor = PreviewSampleColor(maximumSample, maximumValue, maximumVisual);
        if (Thresholds.z > 0.5 && !IsDynamicColorPreview())
        {
            mipColor = CustomColor.rgb * maximumVisual;
        }

        mipColor = ApplyFancyLighting(mipColor, maximumPosition, direction, maximumDepth);
        float3 volumeColorFinal = accumulated.a > 0.002 ? accumulated.rgb / max(accumulated.a, 0.001) : mipColor;
        volumeColorFinal = ApplyFancyLighting(volumeColorFinal, maximumPosition, direction, maximumDepth);

        float mipAlpha = saturate((0.05 + maximumVisual * 0.72) * VolumeAtlas.z);
        float finalAlpha = saturate(accumulated.a * 0.72 + mipAlpha * 0.38);
        float mipBlend = 0.42;
        float3 finalColor = lerp(volumeColorFinal, mipColor, mipBlend);
        return float4(finalColor, finalAlpha);
    }

    float4 accumulated = float4(0.0, 0.0, 0.0, 0.0);

    [loop]
    for (int i = 0; i < 160; i++)
    {
        if (i >= steps || accumulated.a > 0.96)
        {
            break;
        }

        float3 p = nearWorld + direction * t;
        float4 sample = SampleVolumeAtlasNearest(p);
        float value = PreviewValue(sample);
        if (InPreviewRange(value))
        {
            float n = saturate(value * Style.y);
            float sampleAlpha = saturate(n * (2.35 / max((float)steps, 1.0)) * VolumeAtlas.z);
            float3 color = PreviewSampleColor(sample, value, n);
            accumulated.rgb += (1.0 - accumulated.a) * sampleAlpha * color;
            accumulated.a += (1.0 - accumulated.a) * sampleAlpha;
        }

        t += stepLength;
    }

    if (accumulated.a <= 0.002)
    {
        clip(-1.0);
    }

    return float4(accumulated.rgb / max(accumulated.a, 0.001), accumulated.a);
}

float4 PSMain(VSOutput input) : SV_Target
{
    if (VolumeBox.w > 0.5)
    {
        return RenderVolume(input);
    }

    return RenderPlane(input);
}";
    }
}
