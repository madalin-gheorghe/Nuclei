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
        const int ConstantBufferFloatCount = 36;
        const int ConstantBufferBytes = ConstantBufferFloatCount * sizeof(float);
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

                UpdateConstants(e.Viewport, frame);

                D3DStateSnapshot snapshot = D3DStateSnapshot.Capture(context);
                try
                {
                    context.IASetInputLayout(null);
                    context.IASetPrimitiveTopology(PrimitiveTopology.TriangleStrip);

                    context.VSSetShader(vertexShader);
                    context.GSSetShader(null);
                    context.PSSetShader(pixelShader);
                    context.VSSetConstantBuffer(0, constantBuffer);
                    context.PSSetConstantBuffer(0, constantBuffer);
                    context.PSSetShaderResource(0, textureView.ShaderResourceView);

                    context.OMSetBlendState(blendState);
                    context.OMSetDepthStencilState(depthDisabledState, 0);
                    context.RSSetState(rasterizerState);

                    context.Draw(4, 0);
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

        void UpdateConstants(RhinoViewport viewport, GpuDensityFieldPreviewFrame frame)
        {
            Transform worldToScreen = viewport.GetTransform(CoordinateSystem.World, CoordinateSystem.Screen);
            Point3d origin = frame.Origin;
            Vector3d axisU = frame.AxisU;
            Vector3d axisV = frame.AxisV;

            float[] constants =
            {
                (float)worldToScreen.M00, (float)worldToScreen.M01, (float)worldToScreen.M02, (float)worldToScreen.M03,
                (float)worldToScreen.M10, (float)worldToScreen.M11, (float)worldToScreen.M12, (float)worldToScreen.M13,
                (float)worldToScreen.M20, (float)worldToScreen.M21, (float)worldToScreen.M22, (float)worldToScreen.M23,
                (float)worldToScreen.M30, (float)worldToScreen.M31, (float)worldToScreen.M32, (float)worldToScreen.M33,
                Math.Max(1.0f, viewport.Size.Width),
                Math.Max(1.0f, viewport.Size.Height),
                Math.Max(1.0f, frame.Width),
                Math.Max(1.0f, frame.Height),
                (float)origin.X, (float)origin.Y, (float)origin.Z, 0.0f,
                (float)axisU.X, (float)axisU.Y, (float)axisU.Z, 0.0f,
                (float)axisV.X, (float)axisV.Y, (float)axisV.Z, 0.0f,
                0.92f, 1.35f, 0.0f, 0.0f
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

            public bool TryUpdate(ID3D11Device device, GpuDensityFieldPreviewFrame frame)
            {
                if (ShaderResourceView != null && sharedHandle == frame.SharedHandle && width == frame.Width && height == frame.Height)
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
                                Format.R32_Float,
                                0,
                                1,
                                0,
                                1));

                        sharedHandle = frame.SharedHandle;
                        width = frame.Width;
                        height = frame.Height;
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
    row_major float4x4 WorldToScreen;
    float4 ViewportAndTexture;
    float4 Origin;
    float4 AxisU;
    float4 AxisV;
    float4 Style;
};

Texture2D<float> DensityTexture : register(t0);

struct VSOutput
{
    float4 Position : SV_POSITION;
    float2 UV : TEXCOORD0;
};

float2 VertexUv(uint vertexId)
{
    if (vertexId == 0) return float2(0.0, 0.0);
    if (vertexId == 1) return float2(0.0, 1.0);
    if (vertexId == 2) return float2(1.0, 0.0);
    return float2(1.0, 1.0);
}

VSOutput VSMain(uint vertexId : SV_VertexID)
{
    VSOutput output;
    float2 uv = VertexUv(vertexId);
    float3 worldPosition = Origin.xyz + AxisU.xyz * uv.x + AxisV.xyz * uv.y;
    float4 screenPosition = mul(WorldToScreen, float4(worldPosition, 1.0));

    if (abs(screenPosition.w) <= 0.000001)
    {
        output.Position = float4(10.0, 10.0, 0.5, 1.0);
        output.UV = uv;
        return output;
    }

    float2 viewportSize = max(ViewportAndTexture.xy, float2(1.0, 1.0));
    float2 clientPosition = screenPosition.xy / screenPosition.w;
    float2 clipPosition = float2((clientPosition.x / viewportSize.x) * 2.0 - 1.0, 1.0 - (clientPosition.y / viewportSize.y) * 2.0);

    output.Position = float4(clipPosition, 0.52, 1.0);
    output.UV = uv;
    return output;
}

float4 PSMain(VSOutput input) : SV_Target
{
    float2 textureSize = max(ViewportAndTexture.zw, float2(1.0, 1.0));
    int2 texel = clamp((int2)floor(saturate(input.UV) * textureSize), int2(0, 0), (int2)textureSize - int2(1, 1));
    float value = DensityTexture.Load(int3(texel, 0));
    float n = saturate(value * Style.y);

    clip(n - 0.001);

    float glow = smoothstep(0.08, 0.95, n);
    float hot = smoothstep(0.55, 1.0, n);
    float3 baseColor = lerp(float3(0.01, 0.025, 0.035), float3(0.10, 0.92, 0.72), glow);
    float3 color = lerp(baseColor, float3(1.0, 0.78, 0.28), hot);
    float alpha = saturate(0.12 + n * Style.x);
    return float4(color, alpha);
}";
    }
}
