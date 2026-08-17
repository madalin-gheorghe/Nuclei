using Rhino.Display;
using Rhino.Geometry;

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;

using Vortice.D3DCompiler;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;

namespace Nuclei4
{
    internal sealed class ParticleTrailD3DRenderer
    {
        const int ConstantBufferFloatCount = 44;
        const int ConstantBufferBytes = ConstantBufferFloatCount * sizeof(float);
        const int MaxTrailPaletteGroups = 256;
        const int PaletteBufferFloatCount = MaxTrailPaletteGroups * 8;
        const int PaletteBufferBytes = PaletteBufferFloatCount * sizeof(float);
        const string StatusPath = @"C:\Nuclei\Nuclei-v4\BenchmarkSuite1\NucleiD3DTrailPreviewRenderer.txt";

        static readonly ParticleTrailD3DRenderer instance = new ParticleTrailD3DRenderer();

        readonly Dictionary<Guid, SharedTrailTextureView> sharedTrailViews = new Dictionary<Guid, SharedTrailTextureView>();

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
        ID3D11Buffer paletteBuffer;
        readonly float[] paletteConstants = new float[PaletteBufferFloatCount];
        bool disabled;
        bool rhinoVersionUnsupported;
        bool loggedSuccess;

        ParticleTrailD3DRenderer()
        {
        }

        public static bool TryDraw(Guid previewId, DrawEventArgs e, ParticleTrailPreviewDisplayFrame frame)
        {
            return instance.TryDrawInternal(previewId, e, frame);
        }

        public static void Unregister(Guid previewId)
        {
            instance.UnregisterInternal(previewId);
        }

        bool TryDrawInternal(Guid previewId, DrawEventArgs e, ParticleTrailPreviewDisplayFrame frame)
        {
            if (disabled || e == null || frame == null || frame.GpuFrame == null || !frame.GpuFrame.IsValid) return false;
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

                SharedTrailTextureView textureView = GetSharedTrailTextureView(previewId);
                if (!textureView.TryUpdate(device, frame.GpuFrame))
                {
                    return false;
                }

                int segmentCount = frame.GpuFrame.SegmentCount;
                if (segmentCount <= 0) return false;
                if (!textureView.TryAcquire(1))
                {
                    return false;
                }

                int paletteCount = UpdatePaletteConstants(frame);
                UpdateConstants(e.Viewport, frame, paletteCount);

                D3DStateSnapshot snapshot = null;
                try
                {
                    snapshot = D3DStateSnapshot.Capture(context);
                    context.IASetInputLayout(null);
                    context.IASetPrimitiveTopology(PrimitiveTopology.LineList);
                    context.IASetVertexBuffer(0, null, 0, 0);

                    context.VSSetShader(vertexShader);
                    context.GSSetShader(null);
                    context.PSSetShader(pixelShader);
                    context.VSSetConstantBuffer(0, constantBuffer);
                    context.VSSetConstantBuffer(1, paletteBuffer);
                    context.VSSetShaderResource(0, textureView.ShaderResourceView);

                    context.OMSetBlendState(blendState);
                    context.OMSetDepthStencilState(depthDisabledState, 0);
                    context.RSSetState(rasterizerState);

                    context.Draw(segmentCount * 2, 0);
                }
                finally
                {
                    if (snapshot != null)
                    {
                        snapshot.Restore(context);
                        snapshot.Dispose();
                    }
                    textureView.Release();
                }

                if (!loggedSuccess)
                {
                    loggedSuccess = true;
                    WriteStatus("draw_success segments=" + segmentCount.ToString(CultureInfo.InvariantCulture));
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
            paletteBuffer = device.CreateBuffer(
                PaletteBufferBytes,
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
                "NucleiParticleTrailD3D",
                null,
                null,
                profile,
                ShaderFlags.OptimizationLevel3,
                EffectFlags.None))
            {
                return blob.AsBytes();
            }
        }

        int UpdatePaletteConstants(ParticleTrailPreviewDisplayFrame frame)
        {
            Color[] freshColors = frame.FreshColors;
            Color[] oldColors = frame.OldColors;
            int paletteCount = freshColors != null ? Math.Min(freshColors.Length, MaxTrailPaletteGroups) : 0;
            if (paletteCount <= 0)
            {
                paletteCount = 1;
                freshColors = new Color[] { frame.FreshColor };
                oldColors = new Color[] { frame.OldColor };
            }

            int oldColorCount = oldColors != null ? oldColors.Length : 0;
            for (int groupIndex = 0; groupIndex < paletteCount; groupIndex++)
            {
                Color fresh = freshColors[groupIndex];
                Color old = groupIndex < oldColorCount ? oldColors[groupIndex] : frame.OldColor;
                int freshOffset = groupIndex * 4;
                int oldOffset = (MaxTrailPaletteGroups + groupIndex) * 4;
                paletteConstants[freshOffset] = fresh.R / 255.0f;
                paletteConstants[freshOffset + 1] = fresh.G / 255.0f;
                paletteConstants[freshOffset + 2] = fresh.B / 255.0f;
                paletteConstants[freshOffset + 3] = Alpha01(fresh);
                paletteConstants[oldOffset] = old.R / 255.0f;
                paletteConstants[oldOffset + 1] = old.G / 255.0f;
                paletteConstants[oldOffset + 2] = old.B / 255.0f;
                paletteConstants[oldOffset + 3] = Alpha01(old);
            }

            MappedSubresource mapped = context.Map(paletteBuffer, MapMode.WriteDiscard, Vortice.Direct3D11.MapFlags.None);
            try
            {
                Marshal.Copy(paletteConstants, 0, mapped.DataPointer, paletteConstants.Length);
            }
            finally
            {
                context.Unmap(paletteBuffer);
            }

            return paletteCount;
        }

        void UpdateConstants(RhinoViewport viewport, ParticleTrailPreviewDisplayFrame frame, int paletteCount)
        {
            GpuParticleTrailPreviewFrame gpuFrame = frame.GpuFrame;
            Transform worldToScreen = viewport.GetTransform(Rhino.DocObjects.CoordinateSystem.World, Rhino.DocObjects.CoordinateSystem.Screen);
            float dimX = Math.Max(gpuFrame.ResX * gpuFrame.VoxelSize, gpuFrame.VoxelSize);
            float dimY = Math.Max(gpuFrame.ResY * gpuFrame.VoxelSize, gpuFrame.VoxelSize);
            float dimZ = Math.Max(gpuFrame.ResZ * gpuFrame.VoxelSize, gpuFrame.VoxelSize);
            Vector3d viewDirection = viewport.CameraDirection;
            if (!viewDirection.Unitize())
            {
                viewDirection = -Vector3d.ZAxis;
            }
            double minViewDepth;
            double invViewDepthRange;
            ComputeViewDepthRange(viewDirection, dimX, dimY, dimZ, out minViewDepth, out invViewDepthRange);

            float[] constants =
            {
                (float)worldToScreen.M00, (float)worldToScreen.M01, (float)worldToScreen.M02, (float)worldToScreen.M03,
                (float)worldToScreen.M10, (float)worldToScreen.M11, (float)worldToScreen.M12, (float)worldToScreen.M13,
                (float)worldToScreen.M20, (float)worldToScreen.M21, (float)worldToScreen.M22, (float)worldToScreen.M23,
                (float)worldToScreen.M30, (float)worldToScreen.M31, (float)worldToScreen.M32, (float)worldToScreen.M33,
                frame.FreshColor.R / 255.0f, frame.FreshColor.G / 255.0f, frame.FreshColor.B / 255.0f, Alpha01(frame.FreshColor),
                frame.OldColor.R / 255.0f, frame.OldColor.G / 255.0f, frame.OldColor.B / 255.0f, Alpha01(frame.OldColor),
                gpuFrame.TextureWidth, gpuFrame.TrailSize, gpuFrame.ValidTrailCount, gpuFrame.HeadIndex,
                Math.Max(1.0f, viewport.Size.Width), Math.Max(1.0f, viewport.Size.Height), (float)frame.Alpha, (float)frame.FadePower,
                dimX, dimY, dimZ, 0.0f,
                (float)viewDirection.X, (float)viewDirection.Y, (float)viewDirection.Z, (float)minViewDepth,
                (float)invViewDepthRange, (float)CorrectDepthFocus(frame.DepthFocus, gpuFrame), paletteCount, 0.0f
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

        static void ComputeViewDepthRange(Vector3d viewDirection, double dimX, double dimY, double dimZ, out double minDepth, out double invRange)
        {
            minDepth = double.PositiveInfinity;
            double maxDepth = double.NegativeInfinity;

            for (int x = 0; x <= 1; x++)
            {
                for (int y = 0; y <= 1; y++)
                {
                    for (int z = 0; z <= 1; z++)
                    {
                        double px = x == 0 ? 0.0 : dimX;
                        double py = y == 0 ? 0.0 : dimY;
                        double pz = z == 0 ? 0.0 : dimZ;
                        double depth = px * viewDirection.X + py * viewDirection.Y + pz * viewDirection.Z;
                        if (depth < minDepth) minDepth = depth;
                        if (depth > maxDepth) maxDepth = depth;
                    }
                }
            }

            double range = maxDepth - minDepth;
            invRange = range > 1e-9 ? 1.0 / range : 0.0;
        }

        static double Clamp01(double value)
        {
            if (double.IsNaN(value) || double.IsInfinity(value)) return 0.0;
            if (value < 0.0) return 0.0;
            if (value > 1.0) return 1.0;
            return value;
        }

        static double CorrectDepthFocus(double value, GpuParticleTrailPreviewFrame frame)
        {
            double focus = Clamp01(value);
            if (frame == null || frame.ResX <= 1 || frame.ResY <= 1 || frame.ResZ <= 1)
            {
                return 0.0;
            }

            double equivalentResolution = Math.Pow((double)frame.ResX * frame.ResY * frame.ResZ, 1.0 / 3.0);
            if (equivalentResolution <= 1.0)
            {
                return focus;
            }

            double correction = -0.12 * Math.Log(equivalentResolution / 100.0);
            return Clamp01(focus + correction);
        }

        static float Alpha01(Color color)
        {
            return (color.A == 0 ? 255 : color.A) / 255.0f;
        }

        SharedTrailTextureView GetSharedTrailTextureView(Guid previewId)
        {
            SharedTrailTextureView view;
            if (!sharedTrailViews.TryGetValue(previewId, out view))
            {
                view = new SharedTrailTextureView();
                sharedTrailViews[previewId] = view;
            }

            return view;
        }

        void UnregisterInternal(Guid previewId)
        {
            SharedTrailTextureView view;
            if (sharedTrailViews.TryGetValue(previewId, out view))
            {
                view.Dispose();
                sharedTrailViews.Remove(previewId);
            }
        }

        void ReleaseDeviceResources()
        {
            foreach (SharedTrailTextureView view in sharedTrailViews.Values)
            {
                view.Dispose();
            }
            sharedTrailViews.Clear();

            DisposeCom(constantBuffer);
            DisposeCom(paletteBuffer);
            DisposeCom(depthDisabledState);
            DisposeCom(rasterizerState);
            DisposeCom(blendState);
            DisposeCom(pixelShader);
            DisposeCom(vertexShader);
            DisposeCom(context);
            DisposeCom(device);

            constantBuffer = null;
            paletteBuffer = null;
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
                    DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture) + " " + message + Environment.NewLine);
            }
            catch
            {
            }
        }

        sealed class SharedTrailTextureView : IDisposable
        {
            public ID3D11ShaderResourceView ShaderResourceView;
            IDXGIKeyedMutex keyedMutex;
            IntPtr sharedHandle = IntPtr.Zero;
            int width;
            int height;

            public bool TryUpdate(ID3D11Device device, GpuParticleTrailPreviewFrame frame)
            {
                if (ShaderResourceView != null && sharedHandle == frame.SharedHandle && width == frame.TextureWidth && height == frame.TextureHeight)
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
                                Format.R32G32B32A32_Float,
                                0,
                                1,
                                0,
                                1));

                        sharedHandle = frame.SharedHandle;
                        width = frame.TextureWidth;
                        height = frame.TextureHeight;

                        try
                        {
                            keyedMutex = sharedTexture.QueryInterface<IDXGIKeyedMutex>();
                        }
                        catch
                        {
                            keyedMutex = null;
                        }

                        return true;
                    }
                    finally
                    {
                        DisposeCom(sharedTexture);
                    }
                }
                catch (Exception ex)
                {
                    WriteStatus("open_trail_shared_failed handle=0x" + frame.SharedHandle.ToInt64().ToString("X", CultureInfo.InvariantCulture)
                        + " exception=" + ex.GetType().FullName
                        + " message=" + ex.Message);
                    return false;
                }
            }

            public bool TryAcquire(int timeoutMilliseconds)
            {
                if (keyedMutex == null)
                {
                    return true;
                }

                try
                {
                    keyedMutex.AcquireSync(0, timeoutMilliseconds);
                    return true;
                }
                catch
                {
                    return false;
                }
            }

            public void Release()
            {
                if (keyedMutex == null)
                {
                    return;
                }

                try
                {
                    keyedMutex.ReleaseSync(0);
                }
                catch
                {
                }
            }

            public void Dispose()
            {
                DisposeCom(keyedMutex);
                DisposeCom(ShaderResourceView);
                keyedMutex = null;
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
            readonly ID3D11Buffer[] vertexBuffers = new ID3D11Buffer[1];
            readonly int[] vertexStrides = new int[1];
            readonly int[] vertexOffsets = new int[1];
            readonly ID3D11VertexShader vertexShader;
            readonly ID3D11GeometryShader geometryShader;
            readonly ID3D11PixelShader pixelShader;
            readonly ID3D11ShaderResourceView[] vertexResources = new ID3D11ShaderResourceView[1];
            readonly ID3D11Buffer[] vertexConstantBuffers = new ID3D11Buffer[2];
            readonly ID3D11BlendState blendState;
            readonly ID3D11DepthStencilState depthStencilState;
            readonly int stencilRef;
            readonly ID3D11RasterizerState rasterizerState;
            bool restored;

            D3DStateSnapshot(ID3D11DeviceContext1 context)
            {
                inputLayout = context.IAGetInputLayout();
                primitiveTopology = context.IAGetPrimitiveTopology();
                context.IAGetVertexBuffers(0, 1, vertexBuffers, vertexStrides, vertexOffsets);
                vertexShader = context.VSGetShader();
                geometryShader = context.GSGetShader();
                pixelShader = context.PSGetShader();
                context.VSGetShaderResources(0, vertexResources);
                context.VSGetConstantBuffers(0, vertexConstantBuffers);
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
                context.IASetVertexBuffers(0, vertexBuffers, vertexStrides, vertexOffsets);
                context.VSSetShader(vertexShader);
                context.GSSetShader(geometryShader);
                context.PSSetShader(pixelShader);
                context.VSSetShaderResources(0, vertexResources);
                context.VSSetConstantBuffers(0, vertexConstantBuffers);
                context.OMSetBlendState(blendState);
                context.OMSetDepthStencilState(depthStencilState, stencilRef);
                context.RSSetState(rasterizerState);
            }

            public void Dispose()
            {
                DisposeCom(inputLayout);
                DisposeCom(vertexBuffers[0]);
                DisposeCom(vertexResources[0]);
                DisposeCom(vertexShader);
                DisposeCom(geometryShader);
                DisposeCom(pixelShader);
                for (int i = 0; i < vertexConstantBuffers.Length; i++)
                {
                    DisposeCom(vertexConstantBuffers[i]);
                }
                DisposeCom(blendState);
                DisposeCom(depthStencilState);
                DisposeCom(rasterizerState);
            }
        }

        const string ShaderSource = @"
cbuffer TrailConstants : register(b0)
{
    row_major float4x4 WorldToScreen;
    float4 FreshColor;
    float4 OldColor;
    float4 Layout;
    float4 Settings;
    float4 Bounds;
    float4 ViewDepth;
    float4 ViewDepthSettings;
};

cbuffer TrailPalette : register(b1)
{
    float4 FreshColors[256];
    float4 OldColors[256];
};

Texture2D<float4> TrailTexture : register(t0);

struct VSOutput
{
    float4 Position : SV_POSITION;
    float4 Color : COLOR0;
};

VSOutput VSMain(uint vertexId : SV_VertexID)
{
    VSOutput output;

    int textureWidth = max(1, (int)Layout.x);
    int trailSize = max(2, (int)Layout.y);
    int validCount = max(0, (int)Layout.z);
    int headIndex = (int)Layout.w;
    int segmentsPerParticle = max(0, validCount - 1);

    if (segmentsPerParticle <= 0)
    {
        output.Position = float4(10.0, 10.0, 0.5, 1.0);
        output.Color = float4(0.0, 0.0, 0.0, 0.0);
        return output;
    }

    int segmentIndex = (int)vertexId / 2;
    int endpoint = (int)vertexId - segmentIndex * 2;
    int particleIndex = segmentIndex / segmentsPerParticle;
    int segmentAge = segmentIndex - particleIndex * segmentsPerParticle;
    int sampleAge0 = segmentAge;
    int sampleAge1 = segmentAge + 1;
    int sampleAge = endpoint == 0 ? sampleAge0 : sampleAge1;
    int sampleIndex0 = (headIndex + sampleAge0) % trailSize;
    int sampleIndex1 = (headIndex + sampleAge1) % trailSize;

    int texX = particleIndex % textureWidth;
    int particleRow = particleIndex / textureWidth;
    int texY0 = particleRow * trailSize + sampleIndex0;
    int texY1 = particleRow * trailSize + sampleIndex1;

    float4 trailPosition0 = TrailTexture.Load(int3(texX, texY0, 0));
    float4 trailPosition1 = TrailTexture.Load(int3(texX, texY1, 0));
    float4 trailPosition = endpoint == 0 ? trailPosition0 : trailPosition1;

    if (trailPosition0.w < -0.5 || trailPosition1.w < -0.5)
    {
        output.Position = float4(10.0, 10.0, 0.5, 1.0);
        output.Color = float4(0.0, 0.0, 0.0, 0.0);
        return output;
    }

    float3 jump = abs(trailPosition0.xyz - trailPosition1.xyz);
    float3 jumpLimit = max(Bounds.xyz * 0.5, float3(1.0, 1.0, 1.0));
    if (jump.x > jumpLimit.x || jump.y > jumpLimit.y || jump.z > jumpLimit.z)
    {
        output.Position = float4(10.0, 10.0, 0.5, 1.0);
        output.Color = float4(0.0, 0.0, 0.0, 0.0);
        return output;
    }

    float t = validCount <= 1 ? 0.0 : (float)sampleAge / (float)(validCount - 1);
    float colorT = pow(saturate(t), max(Settings.w, 0.1));
    int paletteCount = max(1, (int)ViewDepthSettings.z);
    int groupIndex = clamp((int)round(trailPosition0.w), 0, paletteCount - 1);
    float4 color = lerp(FreshColors[groupIndex], OldColors[groupIndex], colorT);
    color.a *= Settings.z * (1.0 - saturate(t) / 3.0);

    float depthStrength = saturate(ViewDepthSettings.y);
    if (depthStrength > 0.0001)
    {
        float3 segmentMid = (trailPosition0.xyz + trailPosition1.xyz) * 0.5;
        float normalizedViewDepth = saturate((dot(segmentMid, ViewDepth.xyz) - ViewDepth.w) * ViewDepthSettings.x);
        float focus = depthStrength * depthStrength;
        float depthWeight = saturate(pow(normalizedViewDepth, lerp(1.05, 0.38, focus)) * lerp(1.0, 2.85, focus));
        color.a *= lerp(1.0, 0.0005, depthWeight);
    }

    float4 screenPosition0 = mul(WorldToScreen, float4(trailPosition0.xyz, 1.0));
    float4 screenPosition1 = mul(WorldToScreen, float4(trailPosition1.xyz, 1.0));
    if (screenPosition0.w <= 0.000001 || screenPosition1.w <= 0.000001)
    {
        output.Position = float4(10.0, 10.0, 0.5, 1.0);
        output.Color = float4(color.rgb, 0.0);
        return output;
    }

    float4 screenPosition = endpoint == 0 ? screenPosition0 : screenPosition1;
    float2 viewportSize = max(Settings.xy, float2(1.0, 1.0));
    float2 clientPosition = screenPosition.xy / screenPosition.w;
    float2 clipPosition = float2((clientPosition.x / viewportSize.x) * 2.0 - 1.0, 1.0 - (clientPosition.y / viewportSize.y) * 2.0);

    output.Position = float4(clipPosition, 0.5, 1.0);
    output.Color = color;
    return output;
}

float4 PSMain(VSOutput input) : SV_Target
{
    return input.Color;
}";
    }
}
