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

namespace Nuclei3
{
    internal sealed class ParticlePreviewD3DRenderer
    {
        const int FloatsPerVertex = 8;
        const int VertexStrideBytes = FloatsPerVertex * sizeof(float);
        const int QuadCornerFloats = 2;
        const int QuadCornerStrideBytes = QuadCornerFloats * sizeof(float);
        const int QuadCornerCount = 4;
        const int ConstantBufferFloatCount = 24;
        const int ConstantBufferBytes = ConstantBufferFloatCount * sizeof(float);
        const string StatusPath = @"C:\Nuclei\Nuclei-v3\BenchmarkSuite1\NucleiD3DPreviewRenderer.txt";

        static readonly float[] QuadCorners =
        {
            -1.0f, -1.0f,
            -1.0f,  1.0f,
             1.0f, -1.0f,
             1.0f,  1.0f
        };

        static readonly ParticlePreviewD3DRenderer instance = new ParticlePreviewD3DRenderer();

        readonly Dictionary<Guid, PreviewBuffer> previewBuffers = new Dictionary<Guid, PreviewBuffer>();
        readonly Dictionary<Guid, SharedParticleTextureView> sharedParticleViews = new Dictionary<Guid, SharedParticleTextureView>();

        IntPtr devicePtr = IntPtr.Zero;
        IntPtr contextPtr = IntPtr.Zero;
        ID3D11Device device;
        ID3D11DeviceContext1 context;
        ID3D11VertexShader vertexShader;
        ID3D11VertexShader gpuVertexShader;
        ID3D11PixelShader pixelShader;
        ID3D11InputLayout inputLayout;
        ID3D11Buffer quadCornerBuffer;
        ID3D11BlendState blendState;
        ID3D11RasterizerState rasterizerState;
        ID3D11DepthStencilState depthDisabledState;
        ID3D11Buffer constantBuffer;
        bool disabled;
        bool rhinoVersionUnsupported;
        bool loggedSuccess;

        ParticlePreviewD3DRenderer()
        {
        }

        public static bool TryDraw(Guid previewId, DrawEventArgs e, ParticlePreviewDisplayFrame frame)
        {
            return instance.TryDrawInternal(previewId, e, frame);
        }

        public static void Unregister(Guid previewId)
        {
            instance.UnregisterInternal(previewId);
        }

        bool TryDrawInternal(Guid previewId, DrawEventArgs e, ParticlePreviewDisplayFrame frame)
        {
            if (disabled || e == null || frame == null) return false;
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

                if (frame.GpuFrame != null && frame.GpuFrame.IsValid)
                {
                    return TryDrawGpuFrame(previewId, e, frame.GpuFrame, frame.PointSize);
                }

                PreviewBuffer previewBuffer = GetPreviewBuffer(previewId);
                int vertexCount = previewBuffer.Update(device, context, frame);
                if (vertexCount == 0) return false;

                UpdateConstants(e.Viewport, frame.PointSize, null);

                D3DStateSnapshot snapshot = D3DStateSnapshot.Capture(context);
                try
                {
                    context.IASetInputLayout(inputLayout);
                    context.IASetPrimitiveTopology(PrimitiveTopology.TriangleStrip);
                    context.IASetVertexBuffer(0, quadCornerBuffer, QuadCornerStrideBytes, 0);
                    context.IASetVertexBuffer(1, previewBuffer.VertexBuffer, VertexStrideBytes, 0);

                    context.VSSetShader(vertexShader);
                    context.GSSetShader(null);
                    context.PSSetShader(pixelShader);
                    context.VSSetConstantBuffer(0, constantBuffer);

                    context.OMSetBlendState(blendState);
                    context.OMSetDepthStencilState(depthDisabledState, 0);
                    context.RSSetState(rasterizerState);

                    context.DrawInstanced(QuadCornerCount, vertexCount, 0, 0);
                }
                finally
                {
                    snapshot.Restore(context);
                    snapshot.Dispose();
                }

                if (!loggedSuccess)
                {
                    loggedSuccess = true;
                    WriteStatus("draw_success vertex_count=" + vertexCount.ToString(CultureInfo.InvariantCulture));
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

        bool TryDrawGpuFrame(Guid previewId, DrawEventArgs e, GpuParticlePreviewFrame gpuFrame, double pointSize)
        {
            SharedParticleTextureView textureView = GetSharedParticleTextureView(previewId);
            if (!textureView.TryUpdate(device, gpuFrame))
            {
                return false;
            }

            UpdateConstants(e.Viewport, pointSize, gpuFrame);

            D3DStateSnapshot snapshot = D3DStateSnapshot.Capture(context);
            try
            {
                context.IASetInputLayout(null);
                context.IASetPrimitiveTopology(PrimitiveTopology.TriangleStrip);
                context.IASetVertexBuffer(0, null, 0, 0);
                context.IASetVertexBuffer(1, null, 0, 0);

                context.VSSetShader(gpuVertexShader);
                context.GSSetShader(null);
                context.PSSetShader(pixelShader);
                context.VSSetConstantBuffer(0, constantBuffer);
                context.VSSetShaderResource(0, textureView.ShaderResourceView);

                context.OMSetBlendState(blendState);
                context.OMSetDepthStencilState(depthDisabledState, 0);
                context.RSSetState(rasterizerState);

                context.DrawInstanced(QuadCornerCount, gpuFrame.ParticleCount, 0, 0);
            }
            finally
            {
                snapshot.Restore(context);
                snapshot.Dispose();
            }

            if (!loggedSuccess)
            {
                loggedSuccess = true;
                WriteStatus("draw_success_gpu_texture particle_count=" + gpuFrame.ParticleCount.ToString(CultureInfo.InvariantCulture));
            }

            return true;
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
            byte[] gpuVertexShaderBytes = CompileShader(ShaderSource, "VSGpu", "vs_4_0");
            byte[] pixelShaderBytes = CompileShader(ShaderSource, "PSMain", "ps_4_0");

            vertexShader = device.CreateVertexShader(vertexShaderBytes, null);
            gpuVertexShader = device.CreateVertexShader(gpuVertexShaderBytes, null);
            pixelShader = device.CreatePixelShader(pixelShaderBytes, null);

            InputElementDescription[] inputElements =
            {
                new InputElementDescription("TEXCOORD", 1, Format.R32G32_Float, 0, 0, InputClassification.PerVertexData, 0),
                new InputElementDescription("POSITION", 0, Format.R32G32B32_Float, 0, 1, InputClassification.PerInstanceData, 1),
                new InputElementDescription("COLOR", 0, Format.R32G32B32A32_Float, 12, 1, InputClassification.PerInstanceData, 1),
                new InputElementDescription("TEXCOORD", 0, Format.R32_Float, 28, 1, InputClassification.PerInstanceData, 1)
            };

            inputLayout = device.CreateInputLayout(inputElements, vertexShaderBytes);
            quadCornerBuffer = device.CreateBuffer(
                QuadCorners.Length * sizeof(float),
                BindFlags.VertexBuffer,
                ResourceUsage.Dynamic,
                CpuAccessFlags.Write,
                ResourceOptionFlags.None,
                0);
            UpdateQuadCornerBuffer();
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

        void UpdateQuadCornerBuffer()
        {
            MappedSubresource mapped = context.Map(quadCornerBuffer, MapMode.WriteDiscard, Vortice.Direct3D11.MapFlags.None);
            try
            {
                Marshal.Copy(QuadCorners, 0, mapped.DataPointer, QuadCorners.Length);
            }
            finally
            {
                context.Unmap(quadCornerBuffer);
            }
        }

        static byte[] CompileShader(string shaderSource, string entryPoint, string profile)
        {
            using (Vortice.Direct3D.Blob blob = Compiler.Compile(
                shaderSource,
                entryPoint,
                "NucleiParticlePreviewD3D",
                null,
                null,
                profile,
                ShaderFlags.OptimizationLevel3,
                EffectFlags.None))
            {
                return blob.AsBytes();
            }
        }

        void UpdateConstants(RhinoViewport viewport, double pointSize, GpuParticlePreviewFrame gpuFrame)
        {
            Transform worldToScreen = viewport.GetTransform(Rhino.DocObjects.CoordinateSystem.World, Rhino.DocObjects.CoordinateSystem.Screen);
            float[] constants =
            {
                (float)worldToScreen.M00, (float)worldToScreen.M01, (float)worldToScreen.M02, (float)worldToScreen.M03,
                (float)worldToScreen.M10, (float)worldToScreen.M11, (float)worldToScreen.M12, (float)worldToScreen.M13,
                (float)worldToScreen.M20, (float)worldToScreen.M21, (float)worldToScreen.M22, (float)worldToScreen.M23,
                (float)worldToScreen.M30, (float)worldToScreen.M31, (float)worldToScreen.M32, (float)worldToScreen.M33,
                Math.Max(1.0f, viewport.Size.Width),
                Math.Max(1.0f, viewport.Size.Height),
                (float)Math.Max(1.0, pointSize),
                0.0f,
                gpuFrame != null ? (float)gpuFrame.TextureWidth : 0.0f,
                gpuFrame != null ? (float)gpuFrame.TextureHeight : 0.0f,
                gpuFrame != null ? (float)gpuFrame.ParticleCount : 0.0f,
                (float)Math.Max(1.0, pointSize)
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

        PreviewBuffer GetPreviewBuffer(Guid previewId)
        {
            PreviewBuffer buffer;
            if (!previewBuffers.TryGetValue(previewId, out buffer))
            {
                buffer = new PreviewBuffer();
                previewBuffers[previewId] = buffer;
            }

            return buffer;
        }

        SharedParticleTextureView GetSharedParticleTextureView(Guid previewId)
        {
            SharedParticleTextureView view;
            if (!sharedParticleViews.TryGetValue(previewId, out view))
            {
                view = new SharedParticleTextureView();
                sharedParticleViews[previewId] = view;
            }

            return view;
        }

        void UnregisterInternal(Guid previewId)
        {
            PreviewBuffer buffer;
            if (previewBuffers.TryGetValue(previewId, out buffer))
            {
                buffer.Dispose();
                previewBuffers.Remove(previewId);
            }

            SharedParticleTextureView view;
            if (sharedParticleViews.TryGetValue(previewId, out view))
            {
                view.Dispose();
                sharedParticleViews.Remove(previewId);
            }
        }

        void ReleaseDeviceResources()
        {
            foreach (PreviewBuffer buffer in previewBuffers.Values)
            {
                buffer.Dispose();
            }
            previewBuffers.Clear();

            foreach (SharedParticleTextureView view in sharedParticleViews.Values)
            {
                view.Dispose();
            }
            sharedParticleViews.Clear();

            DisposeCom(constantBuffer);
            DisposeCom(depthDisabledState);
            DisposeCom(rasterizerState);
            DisposeCom(blendState);
            DisposeCom(quadCornerBuffer);
            DisposeCom(inputLayout);
            DisposeCom(pixelShader);
            DisposeCom(gpuVertexShader);
            DisposeCom(vertexShader);
            DisposeCom(context);
            DisposeCom(device);

            constantBuffer = null;
            depthDisabledState = null;
            rasterizerState = null;
            blendState = null;
            quadCornerBuffer = null;
            inputLayout = null;
            pixelShader = null;
            gpuVertexShader = null;
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

        sealed class SharedParticleTextureView : IDisposable
        {
            public ID3D11ShaderResourceView ShaderResourceView;
            IntPtr sharedHandle = IntPtr.Zero;
            int width;
            int height;

            public bool TryUpdate(ID3D11Device device, GpuParticlePreviewFrame frame)
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
                        return true;
                    }
                    finally
                    {
                        DisposeCom(sharedTexture);
                    }
                }
                catch (Exception ex)
                {
                    WriteStatus("open_particle_shared_failed handle=0x" + frame.SharedHandle.ToInt64().ToString("X", CultureInfo.InvariantCulture)
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

        sealed class PreviewBuffer : IDisposable
        {
            public ID3D11Buffer VertexBuffer;
            int vertexCapacity;
            int vertexCount;
            float[] vertices;
            PointCloud slimePointCloud;
            PointCloud antPointCloud1;
            PointCloud antPointCloud2;
            int slimeCount = -1;
            int antCount1 = -1;
            int antCount2 = -1;
            double pointSize = -1;

            public int Update(ID3D11Device device, ID3D11DeviceContext1 context, ParticlePreviewDisplayFrame frame)
            {
                if (!NeedsRebuild(frame)) return vertexCount;

                slimePointCloud = frame.SlimePointCloud;
                antPointCloud1 = frame.AntPointCloud1;
                antPointCloud2 = frame.AntPointCloud2;
                slimeCount = Count(slimePointCloud);
                antCount1 = Count(antPointCloud1);
                antCount2 = Count(antPointCloud2);
                pointSize = frame.PointSize;
                vertexCount = slimeCount + antCount1 + antCount2;

                if (vertexCount == 0) return 0;

                EnsureCapacity(device, vertexCount);
                if (vertices == null || vertices.Length < vertexCount * FloatsPerVertex)
                {
                    vertices = new float[vertexCapacity * FloatsPerVertex];
                }

                int offset = 0;
                offset = AppendPointCloud(vertices, offset, slimePointCloud, (float)frame.PointSize);
                offset = AppendPointCloud(vertices, offset, antPointCloud1, (float)frame.PointSize);
                offset = AppendPointCloud(vertices, offset, antPointCloud2, (float)(frame.PointSize * 1.5));
                vertexCount = offset / FloatsPerVertex;
                if (vertexCount == 0) return 0;

                MappedSubresource mapped = context.Map(VertexBuffer, MapMode.WriteDiscard, Vortice.Direct3D11.MapFlags.None);
                try
                {
                    Marshal.Copy(vertices, 0, mapped.DataPointer, vertexCount * FloatsPerVertex);
                }
                finally
                {
                    context.Unmap(VertexBuffer);
                }

                return vertexCount;
            }

            bool NeedsRebuild(ParticlePreviewDisplayFrame frame)
            {
                return frame.SlimePointCloud != slimePointCloud
                    || frame.AntPointCloud1 != antPointCloud1
                    || frame.AntPointCloud2 != antPointCloud2
                    || Count(frame.SlimePointCloud) != slimeCount
                    || Count(frame.AntPointCloud1) != antCount1
                    || Count(frame.AntPointCloud2) != antCount2
                    || Math.Abs(frame.PointSize - pointSize) > 0.001;
            }

            void EnsureCapacity(ID3D11Device device, int count)
            {
                if (VertexBuffer != null && vertexCapacity >= count) return;

                DisposeCom(VertexBuffer);
                vertexCapacity = NextCapacity(count);
                VertexBuffer = device.CreateBuffer(
                    vertexCapacity * VertexStrideBytes,
                    BindFlags.VertexBuffer,
                    ResourceUsage.Dynamic,
                    CpuAccessFlags.Write,
                    ResourceOptionFlags.None,
                    0);
            }

            static int NextCapacity(int count)
            {
                int capacity = 1024;
                while (capacity < count) capacity *= 2;
                return capacity;
            }

            static int Count(PointCloud pointCloud)
            {
                return pointCloud != null ? pointCloud.Count : 0;
            }

            static int AppendPointCloud(float[] vertices, int offset, PointCloud pointCloud, float pointSize)
            {
                if (pointCloud == null || pointCloud.Count == 0) return offset;

                for (int i = 0; i < pointCloud.Count; i++)
                {
                    PointCloudItem item = pointCloud[i];
                    Point3d point = item.Location;
                    Color color = item.Color;
                    if (color.A == 0) color = Color.FromArgb(255, color.R, color.G, color.B);

                    vertices[offset++] = (float)point.X;
                    vertices[offset++] = (float)point.Y;
                    vertices[offset++] = (float)point.Z;
                    vertices[offset++] = color.R / 255.0f;
                    vertices[offset++] = color.G / 255.0f;
                    vertices[offset++] = color.B / 255.0f;
                    vertices[offset++] = color.A / 255.0f;
                    vertices[offset++] = pointSize;
                }

                return offset;
            }

            public void Dispose()
            {
                DisposeCom(VertexBuffer);
                VertexBuffer = null;
                vertices = null;
                vertexCapacity = 0;
                vertexCount = 0;
            }
        }

        sealed class D3DStateSnapshot : IDisposable
        {
            readonly ID3D11InputLayout inputLayout;
            readonly PrimitiveTopology primitiveTopology;
            readonly ID3D11Buffer[] vertexBuffers = new ID3D11Buffer[2];
            readonly int[] vertexStrides = new int[2];
            readonly int[] vertexOffsets = new int[2];
            readonly ID3D11VertexShader vertexShader;
            readonly ID3D11GeometryShader geometryShader;
            readonly ID3D11PixelShader pixelShader;
            readonly ID3D11ShaderResourceView[] vertexResources = new ID3D11ShaderResourceView[1];
            readonly ID3D11Buffer[] vertexConstantBuffers = new ID3D11Buffer[1];
            readonly ID3D11Buffer[] geometryConstantBuffers = new ID3D11Buffer[1];
            readonly ID3D11BlendState blendState;
            readonly ID3D11DepthStencilState depthStencilState;
            readonly int stencilRef;
            readonly ID3D11RasterizerState rasterizerState;
            bool restored;

            D3DStateSnapshot(ID3D11DeviceContext1 context)
            {
                inputLayout = context.IAGetInputLayout();
                primitiveTopology = context.IAGetPrimitiveTopology();
                context.IAGetVertexBuffers(0, 2, vertexBuffers, vertexStrides, vertexOffsets);
                vertexShader = context.VSGetShader();
                geometryShader = context.GSGetShader();
                pixelShader = context.PSGetShader();
                context.VSGetShaderResources(0, vertexResources);
                context.VSGetConstantBuffers(0, vertexConstantBuffers);
                context.GSGetConstantBuffers(0, geometryConstantBuffers);
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
                context.GSSetConstantBuffers(0, geometryConstantBuffers);
                context.OMSetBlendState(blendState);
                context.OMSetDepthStencilState(depthStencilState, stencilRef);
                context.RSSetState(rasterizerState);
            }

            public void Dispose()
            {
                DisposeCom(inputLayout);
                DisposeCom(vertexBuffers[0]);
                DisposeCom(vertexBuffers[1]);
                DisposeCom(vertexResources[0]);
                DisposeCom(vertexShader);
                DisposeCom(geometryShader);
                DisposeCom(pixelShader);
                DisposeCom(vertexConstantBuffers[0]);
                DisposeCom(geometryConstantBuffers[0]);
                DisposeCom(blendState);
                DisposeCom(depthStencilState);
                DisposeCom(rasterizerState);
            }
        }

        const string ShaderSource = @"
cbuffer PreviewConstants : register(b0)
{
    row_major float4x4 WorldToScreen;
    float4 ViewportAndSize;
    float4 TextureLayout;
};

Texture2D<float4> ParticlePreviewTexture : register(t0);

struct VSInput
{
    float2 Corner : TEXCOORD1;
    float3 Position : POSITION;
    float4 Color : COLOR0;
    float Size : TEXCOORD0;
};

struct VSOutput
{
    float4 Position : SV_POSITION;
    float4 Color : COLOR0;
    float2 UV : TEXCOORD0;
};

VSOutput VSMain(VSInput input)
{
    VSOutput output;
    float4 screenPosition = mul(WorldToScreen, float4(input.Position, 1.0));
    if (abs(screenPosition.w) <= 0.000001)
    {
        output.Position = float4(10.0, 10.0, 0.5, 1.0);
        output.Color = input.Color;
        output.UV = input.Corner;
        return output;
    }

    float2 viewportSize = max(ViewportAndSize.xy, float2(1.0, 1.0));
    float2 clientPosition = screenPosition.xy / screenPosition.w;
    float2 clipPosition = float2((clientPosition.x / viewportSize.x) * 2.0 - 1.0, 1.0 - (clientPosition.y / viewportSize.y) * 2.0);
    float pixelDiameter = max(input.Size, 1.0);
    float2 clipOffset = float2(pixelDiameter / viewportSize.x, pixelDiameter / viewportSize.y);
    float2 margin = clipOffset * 2.0 + float2(0.05, 0.05);
    if (clipPosition.x < -1.0 - margin.x || clipPosition.x > 1.0 + margin.x || clipPosition.y < -1.0 - margin.y || clipPosition.y > 1.0 + margin.y)
    {
        output.Position = float4(10.0, 10.0, 0.5, 1.0);
        output.Color = input.Color;
        output.UV = input.Corner;
        return output;
    }

    output.Position = float4(clipPosition + clipOffset * input.Corner, 0.5, 1.0);
    output.Color = input.Color;
    output.UV = input.Corner;
    return output;
}

float2 QuadCorner(uint vertexId)
{
    if (vertexId == 0) return float2(-1.0, -1.0);
    if (vertexId == 1) return float2(-1.0,  1.0);
    if (vertexId == 2) return float2( 1.0, -1.0);
    return float2(1.0, 1.0);
}

VSOutput VSGpu(uint vertexId : SV_VertexID, uint instanceId : SV_InstanceID)
{
    VSOutput output;

    int particleCount = (int)TextureLayout.z;
    int textureWidth = max(1, (int)TextureLayout.x);
    float pointSize = max(TextureLayout.w, 1.0);
    float2 corner = QuadCorner(vertexId);

    if ((int)instanceId >= particleCount)
    {
        output.Position = float4(10.0, 10.0, 0.5, 1.0);
        output.Color = float4(1.0, 1.0, 1.0, 1.0);
        output.UV = corner;
        return output;
    }

    int texX = (int)instanceId % textureWidth;
    int row = (int)instanceId / textureWidth;
    float4 positionGroup = ParticlePreviewTexture.Load(int3(texX, row * 2, 0));
    float4 color = ParticlePreviewTexture.Load(int3(texX, row * 2 + 1, 0));

    float4 screenPosition = mul(WorldToScreen, float4(positionGroup.xyz, 1.0));
    if (abs(screenPosition.w) <= 0.000001)
    {
        output.Position = float4(10.0, 10.0, 0.5, 1.0);
        output.Color = color;
        output.UV = corner;
        return output;
    }

    float2 viewportSize = max(ViewportAndSize.xy, float2(1.0, 1.0));
    float2 clientPosition = screenPosition.xy / screenPosition.w;
    float2 clipPosition = float2((clientPosition.x / viewportSize.x) * 2.0 - 1.0, 1.0 - (clientPosition.y / viewportSize.y) * 2.0);
    float2 clipOffset = float2(pointSize / viewportSize.x, pointSize / viewportSize.y);
    float2 margin = clipOffset * 2.0 + float2(0.05, 0.05);
    if (clipPosition.x < -1.0 - margin.x || clipPosition.x > 1.0 + margin.x || clipPosition.y < -1.0 - margin.y || clipPosition.y > 1.0 + margin.y)
    {
        output.Position = float4(10.0, 10.0, 0.5, 1.0);
        output.Color = color;
        output.UV = corner;
        return output;
    }

    output.Position = float4(clipPosition + clipOffset * corner, 0.5, 1.0);
    output.Color = color;
    output.UV = corner;
    return output;
}

float4 PSMain(VSOutput input) : SV_Target
{
    float distanceSquared = dot(input.UV, input.UV);
    clip(1.0 - distanceSquared);
    float edge = saturate((1.0 - distanceSquared) * 4.0);
    return float4(input.Color.rgb, input.Color.a * edge);
}";
    }
}
