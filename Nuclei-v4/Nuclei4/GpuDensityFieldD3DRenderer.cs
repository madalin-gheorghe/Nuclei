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
        ID3D11PixelShader compositeShader;
        ID3D11ComputeShader occupancyShader;
        ID3D11ComputeShader shadowShader;
        ID3D11BlendState blendState;
        ID3D11BlendState opaqueBlendState;
        ID3D11RasterizerState rasterizerState;
        ID3D11DepthStencilState depthDisabledState;
        ID3D11SamplerState atlasSamplerState;
        ID3D11Texture2D transferTexture;
        ID3D11ShaderResourceView transferTextureView;
        ID3D11Buffer constantBuffer;
        bool disabled;
        bool rhinoVersionUnsupported;
        bool loggedSuccess;
        readonly Dictionary<Guid, bool> loggedFancyStates = new Dictionary<Guid, bool>();

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

        public static void InvalidateHistory(Guid solverId)
        {
            instance.InvalidateHistoryInternal(solverId);
        }

        public static bool NeedsFancyRefinement(Guid solverId)
        {
            SharedTextureView view;
            return instance.sharedTextureViews.TryGetValue(solverId, out view) && view.NeedsFancyRefinement;
        }

        void InvalidateHistoryInternal(Guid solverId)
        {
            SharedTextureView view;
            if (sharedTextureViews.TryGetValue(solverId, out view)) view.InvalidateFancyHistory();
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

                bool previousFancyState;
                if (!loggedFancyStates.TryGetValue(solverId, out previousFancyState) || previousFancyState != frame.FancyRender)
                {
                    loggedFancyStates[solverId] = frame.FancyRender;
                    WriteStatus("fancy_state enabled=" + frame.FancyRender.ToString(CultureInfo.InvariantCulture)
                        + " value=" + frame.ValueIndex.ToString(CultureInfo.InvariantCulture)
                        + " volume=" + frame.VolumeMode.ToString(CultureInfo.InvariantCulture)
                        + " gradient=" + frame.HasGradientTexture.ToString(CultureInfo.InvariantCulture));
                }

                int historyFrameCount = 0;
                ulong cameraHash = 0;
                if (frame.FancyRender)
                {
                    textureView.EnsureFancyResources(device, e.Viewport.Size.Width, e.Viewport.Size.Height);
                    cameraHash = ComputeFancyHistoryHash(e.Viewport, frame);
                    textureView.PrepareFancyHistory(cameraHash, frame.Version);
                    historyFrameCount = textureView.HistoryFrameCount;
                }
                else
                {
                    textureView.DisableFancyResources();
                }

                if (!UpdateConstants(e.Viewport, frame, textureView.HasOccupancyTexture, historyFrameCount))
                {
                    return false;
                }

                D3DStateSnapshot snapshot = D3DStateSnapshot.Capture(context);
                try
                {
                    textureView.UpdateOccupancy(context, occupancyShader, constantBuffer, frame);

                    textureView.UpdateShadow(context, shadowShader, constantBuffer, frame);

                    if (!UpdateConstants(e.Viewport, frame, textureView.HasUsableOccupancy(frame), historyFrameCount))
                    {
                        return false;
                    }

                    context.IASetInputLayout(null);
                    context.IASetPrimitiveTopology(PrimitiveTopology.TriangleList);

                    context.VSSetShader(vertexShader);
                    context.GSSetShader(null);
                    context.PSSetShader(pixelShader);
                    context.VSSetConstantBuffer(0, constantBuffer);
                    context.PSSetConstantBuffer(0, constantBuffer);
                    context.PSSetShaderResource(0, textureView.ShaderResourceView);
                    context.PSSetShaderResource(1, textureView.GradientShaderResourceView);
                    context.PSSetShaderResource(2, transferTextureView);
                    context.PSSetShaderResource(3, textureView.OccupancyShaderResourceView);
                    context.PSSetShaderResource(4, frame.FancyRender ? textureView.ShadowShaderResourceView : null);
                    context.PSSetShaderResource(5, frame.FancyRender ? textureView.HistoryReadView : null);
                    context.PSSetSampler(0, atlasSamplerState);

                    context.OMSetBlendState(blendState);
                    // A translucent ray contains many depths, so testing one representative
                    // depth against Rhino's transient viewport buffer can clip the whole ray.
                    context.OMSetDepthStencilState(depthDisabledState, 0);
                    context.RSSetState(rasterizerState);

                    ID3D11RenderTargetView[] originalTargets = null;
                    ID3D11DepthStencilView originalDepth = null;
                    if (frame.FancyRender && textureView.HistoryWriteTarget != null)
                    {
                        originalTargets = new ID3D11RenderTargetView[1];
                        context.OMGetRenderTargets(1, originalTargets, out originalDepth);
                        context.OMSetRenderTargets(textureView.HistoryWriteTarget, null);
                        context.ClearRenderTargetView(
                            textureView.HistoryWriteTarget,
                            new Vortice.Mathematics.Color4(0.0f, 0.0f, 0.0f, 0.0f));
                        context.OMSetBlendState(opaqueBlendState);
                        context.Draw(FullscreenVertexCount, 0);
                        context.PSSetShaderResource(5, null);
                        context.OMSetRenderTargets(originalTargets, originalDepth);
                        context.OMSetBlendState(blendState);
                        textureView.AdvanceFancyHistory();
                        context.PSSetShader(compositeShader);
                        context.PSSetShaderResource(5, textureView.HistoryReadView);
                        context.Draw(FullscreenVertexCount, 0);
                        context.PSSetShaderResource(5, null);
                        DisposeCom(originalTargets[0]);
                        DisposeCom(originalDepth);
                    }
                    else
                    {
                        context.Draw(FullscreenVertexCount, 0);
                    }
                    context.PSSetShaderResource(0, null);
                    context.PSSetShaderResource(1, null);
                    context.PSSetShaderResource(2, null);
                    context.PSSetShaderResource(3, null);
                    context.PSSetShaderResource(4, null);
                    context.PSSetShaderResource(5, null);
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
            byte[] vertexShaderBytes = LoadShaderBytecode("DensityPreviewVS", "VSMain", "vs_4_0");
            byte[] pixelShaderBytes = LoadShaderBytecode("DensityPreviewPS", "PSMain", "ps_5_0");
            byte[] compositeShaderBytes = LoadShaderBytecode("DensityPreviewComposite", "PSComposite", "ps_5_0");
            byte[] occupancyShaderBytes = LoadShaderBytecode("DensityPreviewOccupancy", "CSBuildOccupancy", "cs_5_0");
            byte[] shadowShaderBytes = LoadShaderBytecode("DensityPreviewShadow", "CSBuildShadow", "cs_5_0");

            vertexShader = device.CreateVertexShader(vertexShaderBytes, null);
            pixelShader = device.CreatePixelShader(pixelShaderBytes, null);
            compositeShader = device.CreatePixelShader(compositeShaderBytes, null);
            occupancyShader = device.CreateComputeShader(occupancyShaderBytes, null);
            shadowShader = device.CreateComputeShader(shadowShaderBytes, null);
            blendState = device.CreateBlendState(BlendDescription.NonPremultiplied);
            opaqueBlendState = device.CreateBlendState(BlendDescription.Opaque);
            rasterizerState = device.CreateRasterizerState(RasterizerDescription.CullNone);
            depthDisabledState = device.CreateDepthStencilState(DepthStencilDescription.None);
            atlasSamplerState = device.CreateSamplerState(SamplerDescription.LinearClamp);
            CreateStyleTextures();
            constantBuffer = device.CreateBuffer(
                ConstantBufferBytes,
                BindFlags.ConstantBuffer,
                ResourceUsage.Dynamic,
                CpuAccessFlags.Write,
                ResourceOptionFlags.None,
                0);
        }

        void CreateStyleTextures()
        {
            const int transferSize = 64;
            const int integrationSamples = 16;
            float[] transfer = new float[transferSize * transferSize * 4];
            for (int y = 0; y < transferSize; y++)
            {
                float currentDensity = y / (float)(transferSize - 1);
                for (int x = 0; x < transferSize; x++)
                {
                    float previousDensity = x / (float)(transferSize - 1);
                    float integratedVisible = 0.0f;
                    float integratedDensity = 0.0f;
                    float peakVisible = 0.0f;
                    for (int sampleIndex = 0; sampleIndex < integrationSamples; sampleIndex++)
                    {
                        float interval = (sampleIndex + 0.5f) / integrationSamples;
                        float density = Lerp(previousDensity, currentDensity, interval);
                        float visible = SmoothStep(0.015f, 0.14f, density) * (float)Math.Pow(density, 0.72);
                        integratedVisible += visible;
                        integratedDensity += density;
                        peakVisible = Math.Max(peakVisible, visible);
                    }

                    integratedVisible /= integrationSamples;
                    integratedDensity /= integrationSamples;
                    int offset = (y * transferSize + x) * 4;
                    transfer[offset] = integratedVisible;
                    transfer[offset + 1] = 0.30f + 0.12f * (float)Math.Pow(integratedDensity, 0.65);
                    transfer[offset + 2] = peakVisible;
                    transfer[offset + 3] = integratedDensity;
                }
            }

            transferTexture = CreateFloatTexture(transferSize, transferSize, Format.R32G32B32A32_Float, transfer);
            transferTextureView = device.CreateShaderResourceView(transferTexture);
        }

        ID3D11Texture2D CreateFloatTexture(int width, int height, Format format, float[] values)
        {
            Texture2DDescription description = new Texture2DDescription(
                format, width, height, 1, 1, BindFlags.ShaderResource, ResourceUsage.Default,
                CpuAccessFlags.None, 1, 0, ResourceOptionFlags.None);
            int channelCount = format == Format.R32G32B32A32_Float ? 4 : 1;
            int rowPitch = width * channelCount * sizeof(float);
            GCHandle pinnedValues = GCHandle.Alloc(values, GCHandleType.Pinned);
            try
            {
                SubresourceData[] initialData =
                {
                    new SubresourceData(pinnedValues.AddrOfPinnedObject(), rowPitch, rowPitch * height)
                };
                return device.CreateTexture2D(description, initialData);
            }
            finally
            {
                pinnedValues.Free();
            }
        }

        static float SmoothStep(float minimum, float maximum, float value)
        {
            float t = Math.Max(0.0f, Math.Min(1.0f, (value - minimum) / (maximum - minimum)));
            return t * t * (3.0f - 2.0f * t);
        }

        static float Lerp(float a, float b, float t)
        {
            return a + (b - a) * t;
        }

        static ulong ComputeFancyHistoryHash(RhinoViewport viewport, GpuDensityFieldPreviewFrame frame)
        {
            Transform transform = viewport.GetTransform(CoordinateSystem.World, CoordinateSystem.Screen);
            ulong hash = 1469598103934665603UL;
            AddHash(ref hash, transform.M00); AddHash(ref hash, transform.M01); AddHash(ref hash, transform.M02); AddHash(ref hash, transform.M03);
            AddHash(ref hash, transform.M10); AddHash(ref hash, transform.M11); AddHash(ref hash, transform.M12); AddHash(ref hash, transform.M13);
            AddHash(ref hash, transform.M20); AddHash(ref hash, transform.M21); AddHash(ref hash, transform.M22); AddHash(ref hash, transform.M23);
            AddHash(ref hash, transform.M30); AddHash(ref hash, transform.M31); AddHash(ref hash, transform.M32); AddHash(ref hash, transform.M33);
            AddHash(ref hash, frame.MinimumThreshold); AddHash(ref hash, frame.MaximumThreshold);
            AddHash(ref hash, frame.ColorR); AddHash(ref hash, frame.ColorG); AddHash(ref hash, frame.ColorB);
            AddHash(ref hash, viewport.Size.Width); AddHash(ref hash, viewport.Size.Height);
            return hash;
        }

        static void AddHash(ref ulong hash, double value)
        {
            long bits = BitConverter.DoubleToInt64Bits(value);
            hash ^= (ulong)bits;
            hash *= 1099511628211UL;
        }

        static void AddHash(ref ulong hash, int value)
        {
            hash ^= (uint)value;
            hash *= 1099511628211UL;
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

        static byte[] LoadShaderBytecode(string shaderName, string entryPoint, string profile)
        {
            string resourceName = "Nuclei3.GpuShaders." + shaderName + ".cso";
            using (Stream stream = typeof(GpuDensityFieldD3DRenderer).Assembly.GetManifestResourceStream(resourceName))
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
                        return bytecode;
                    }
                }
            }

            return CompileShader(ShaderSource, entryPoint, profile);
        }

        bool UpdateConstants(RhinoViewport viewport, GpuDensityFieldPreviewFrame frame, bool hasOccupancyTexture, int historyFrameCount)
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
            int automaticVolumeSteps = Math.Min(256, Math.Max(64, (int)Math.Ceiling(maxResolution * 1.5)));
            int volumeSteps = volumeMode
                ? (frame.VolumeSampleCount > 0 ? ClampInt(frame.VolumeSampleCount, 8, 256) : automaticVolumeSteps)
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
                hasOccupancyTexture ? 1.0f : 0.0f,
                frame.FancyRender ? 1.0f : 0.0f,
                historyFrameCount,
                0.0f,
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
                (float)viewport.CameraLocation.X,
                (float)viewport.CameraLocation.Y,
                (float)viewport.CameraLocation.Z,
                viewport.IsPerspectiveProjection ? 1.0f : 0.0f,
                0.92f,
                volumeContrast,
                planarBackground,
                0.0f,
                (float)screenToWorld.M00, (float)screenToWorld.M01, (float)screenToWorld.M02, (float)screenToWorld.M03,
                (float)screenToWorld.M10, (float)screenToWorld.M11, (float)screenToWorld.M12, (float)screenToWorld.M13,
                (float)screenToWorld.M20, (float)screenToWorld.M21, (float)screenToWorld.M22, (float)screenToWorld.M23,
                (float)screenToWorld.M30, (float)screenToWorld.M31, (float)screenToWorld.M32, (float)screenToWorld.M33,
                frame.DomainResX * frame.VoxelSize,
                frame.DomainResY * frame.VoxelSize,
                frame.DomainResZ * frame.VoxelSize,
                volumeMode ? 1.0f : 0.0f,
                frame.ResX,
                frame.ResY,
                frame.ResZ,
                Math.Max(1, frame.AtlasColumns),
                Math.Max(1, frame.AtlasRows),
                volumeSteps,
                volumeOpacity,
                frame.VolumeRendererVersion
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
            loggedFancyStates.Remove(solverId);
        }

        void ReleaseDeviceResources()
        {
            foreach (SharedTextureView textureView in sharedTextureViews.Values)
            {
                textureView.Dispose();
            }
            sharedTextureViews.Clear();
            loggedFancyStates.Clear();

            DisposeCom(constantBuffer);
            DisposeCom(transferTextureView);
            DisposeCom(transferTexture);
            DisposeCom(atlasSamplerState);
            DisposeCom(depthDisabledState);
            DisposeCom(rasterizerState);
            DisposeCom(blendState);
            DisposeCom(opaqueBlendState);
            DisposeCom(occupancyShader);
            DisposeCom(shadowShader);
            DisposeCom(compositeShader);
            DisposeCom(pixelShader);
            DisposeCom(vertexShader);
            DisposeCom(context);
            DisposeCom(device);

            constantBuffer = null;
            transferTextureView = null;
            transferTexture = null;
            atlasSamplerState = null;
            depthDisabledState = null;
            rasterizerState = null;
            blendState = null;
            opaqueBlendState = null;
            occupancyShader = null;
            shadowShader = null;
            compositeShader = null;
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
            public ID3D11ShaderResourceView GradientShaderResourceView;
            public ID3D11ShaderResourceView OccupancyShaderResourceView;
            ID3D11Texture2D occupancyTexture;
            ID3D11UnorderedAccessView occupancyTextureView;
            public ID3D11ShaderResourceView ShadowShaderResourceView;
            ID3D11Texture2D shadowTexture;
            ID3D11UnorderedAccessView shadowTextureView;
            readonly ID3D11Texture2D[] historyTextures = new ID3D11Texture2D[2];
            readonly ID3D11RenderTargetView[] historyRenderTargetViews = new ID3D11RenderTargetView[2];
            readonly ID3D11ShaderResourceView[] historyShaderResourceViews = new ID3D11ShaderResourceView[2];
            int historyReadIndex;
            IntPtr sharedHandle = IntPtr.Zero;
            IntPtr gradientSharedHandle = IntPtr.Zero;
            int width;
            int height;
            bool colorTexture;
            int occupancyWidth;
            int occupancyHeight;
            int occupancyBlocksX;
            int occupancyBlocksY;
            int occupancyBlocksZ;
            long occupancyVersion = long.MinValue;
            int occupancyValueIndex = int.MinValue;
            long shadowVersion = long.MinValue;
            int shadowValueIndex = int.MinValue;
            int historyWidth;
            int historyHeight;
            long historyVersion = long.MinValue;
            int historyFrameCount;
            ulong historyCameraHash;

            public bool NeedsFancyRefinement
            {
                get { return historyFrameCount > 0 && historyFrameCount < 24; }
            }

            public int HistoryFrameCount
            {
                get { return historyFrameCount; }
            }

            public void InvalidateFancyHistory()
            {
                historyVersion = long.MinValue;
                historyFrameCount = 0;
                historyCameraHash = 0;
            }

            public void DisableFancyResources()
            {
                if (shadowTexture == null && historyTextures[0] == null) return;
                DisposeFancyResources();
            }

            public bool HasOccupancyTexture
            {
                get { return OccupancyShaderResourceView != null; }
            }

            public bool HasUsableOccupancy(GpuDensityFieldPreviewFrame frame)
            {
                return OccupancyShaderResourceView != null
                    && occupancyVersion == frame.Version
                    && occupancyValueIndex == frame.ValueIndex;
            }

            public bool TryUpdate(ID3D11Device device, GpuDensityFieldPreviewFrame frame)
            {
                bool densityCurrent = ShaderResourceView != null
                    && sharedHandle == frame.SharedHandle
                    && width == frame.Width
                    && height == frame.Height
                    && colorTexture == frame.ColorTexture;
                if (!densityCurrent && !OpenDensityTexture(device, frame))
                {
                    return false;
                }

                if (!UpdateGradientTexture(device, frame))
                {
                    return false;
                }

                return EnsureOccupancyTexture(device, frame);
            }

            bool OpenDensityTexture(ID3D11Device device, GpuDensityFieldPreviewFrame frame)
            {
                DisposeCom(ShaderResourceView);
                ShaderResourceView = null;
                sharedHandle = IntPtr.Zero;
                occupancyVersion = long.MinValue;
                occupancyValueIndex = int.MinValue;

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

            bool UpdateGradientTexture(ID3D11Device device, GpuDensityFieldPreviewFrame frame)
            {
                if (!frame.HasGradientTexture)
                {
                    DisposeCom(GradientShaderResourceView);
                    GradientShaderResourceView = null;
                    gradientSharedHandle = IntPtr.Zero;
                    return true;
                }

                if (GradientShaderResourceView != null && gradientSharedHandle == frame.GradientSharedHandle)
                {
                    return true;
                }

                DisposeCom(GradientShaderResourceView);
                GradientShaderResourceView = null;
                gradientSharedHandle = IntPtr.Zero;
                try
                {
                    ID3D11Texture2D sharedTexture = device.OpenSharedResource<ID3D11Texture2D>(frame.GradientSharedHandle);
                    try
                    {
                        GradientShaderResourceView = device.CreateShaderResourceView(
                            sharedTexture,
                            new ShaderResourceViewDescription(
                                sharedTexture,
                                ShaderResourceViewDimension.Texture2D,
                                Format.R16G16B16A16_Float,
                                0,
                                1,
                                0,
                                1));
                        gradientSharedHandle = frame.GradientSharedHandle;
                        return true;
                    }
                    finally
                    {
                        DisposeCom(sharedTexture);
                    }
                }
                catch (Exception ex)
                {
                    WriteStatus("open_gradient_shared_failed handle=0x" + frame.GradientSharedHandle.ToInt64().ToString("X", CultureInfo.InvariantCulture)
                        + " exception=" + ex.GetType().FullName
                        + " message=" + ex.Message);
                    return false;
                }
            }

            bool EnsureOccupancyTexture(ID3D11Device device, GpuDensityFieldPreviewFrame frame)
            {
                if (!frame.VolumeMode)
                {
                    DisposeOccupancyTexture();
                    return true;
                }

                const int blockSize = 4;
                int blocksX = Math.Max(1, (frame.ResX + blockSize - 1) / blockSize);
                int blocksY = Math.Max(1, (frame.ResY + blockSize - 1) / blockSize);
                int blocksZ = Math.Max(1, (frame.ResZ + blockSize - 1) / blockSize);
                int columns = Math.Max(1, frame.AtlasColumns);
                int rows = Math.Max(1, (blocksZ + columns - 1) / columns);
                int requiredWidth = blocksX * columns;
                int requiredHeight = blocksY * rows;
                if (OccupancyShaderResourceView != null
                    && occupancyBlocksX == blocksX
                    && occupancyBlocksY == blocksY
                    && occupancyBlocksZ == blocksZ
                    && occupancyWidth == requiredWidth
                    && occupancyHeight == requiredHeight)
                {
                    return true;
                }

                DisposeOccupancyTexture();
                Texture2DDescription description = new Texture2DDescription(
                    Format.R32_Float,
                    requiredWidth,
                    requiredHeight,
                    1,
                    1,
                    BindFlags.UnorderedAccess | BindFlags.ShaderResource,
                    ResourceUsage.Default,
                    CpuAccessFlags.None,
                    1,
                    0,
                    ResourceOptionFlags.None);
                occupancyTexture = device.CreateTexture2D(description, null);
                occupancyTextureView = device.CreateUnorderedAccessView(
                    occupancyTexture,
                    new UnorderedAccessViewDescription(
                        occupancyTexture,
                        UnorderedAccessViewDimension.Texture2D,
                        Format.R32_Float,
                        0,
                        0,
                        0));
                OccupancyShaderResourceView = device.CreateShaderResourceView(
                    occupancyTexture,
                    new ShaderResourceViewDescription(
                        occupancyTexture,
                        ShaderResourceViewDimension.Texture2D,
                        Format.R32_Float,
                        0,
                        1,
                        0,
                        1));
                occupancyWidth = requiredWidth;
                occupancyHeight = requiredHeight;
                occupancyBlocksX = blocksX;
                occupancyBlocksY = blocksY;
                occupancyBlocksZ = blocksZ;
                occupancyVersion = long.MinValue;
                return true;
            }

            public void UpdateOccupancy(
                ID3D11DeviceContext1 context,
                ID3D11ComputeShader shader,
                ID3D11Buffer constants,
                GpuDensityFieldPreviewFrame frame)
            {
                if (shader == null
                    || occupancyTextureView == null
                    || ShaderResourceView == null
                    || occupancyVersion == frame.Version && occupancyValueIndex == frame.ValueIndex)
                {
                    return;
                }

                context.CSSetShader(shader);
                context.CSSetConstantBuffer(0, constants);
                context.CSSetShaderResource(0, ShaderResourceView);
                context.CSSetUnorderedAccessView(0, occupancyTextureView, -1);
                context.Dispatch(
                    (occupancyBlocksX + 3) / 4,
                    (occupancyBlocksY + 3) / 4,
                    (occupancyBlocksZ + 3) / 4);
                context.CSSetUnorderedAccessView(0, null, -1);
                context.CSSetShaderResource(0, null);
                context.CSSetConstantBuffer(0, null);
                context.CSSetShader(null);
                occupancyVersion = frame.Version;
                occupancyValueIndex = frame.ValueIndex;
            }

            public void EnsureFancyResources(ID3D11Device device, int viewportWidth, int viewportHeight)
            {
                if (shadowTexture == null && occupancyWidth > 0 && occupancyHeight > 0)
                {
                    Texture2DDescription shadowDescription = new Texture2DDescription(
                        Format.R32_Float, occupancyWidth, occupancyHeight, 1, 1,
                        BindFlags.UnorderedAccess | BindFlags.ShaderResource, ResourceUsage.Default,
                        CpuAccessFlags.None, 1, 0, ResourceOptionFlags.None);
                    shadowTexture = device.CreateTexture2D(shadowDescription, null);
                    shadowTextureView = device.CreateUnorderedAccessView(shadowTexture);
                    ShadowShaderResourceView = device.CreateShaderResourceView(shadowTexture);
                    shadowVersion = long.MinValue;
                }

                viewportWidth = Math.Max(1, viewportWidth);
                viewportHeight = Math.Max(1, viewportHeight);
                if (historyTextures[0] != null && historyWidth == viewportWidth && historyHeight == viewportHeight) return;

                DisposeHistoryTexture();
                Texture2DDescription historyDescription = new Texture2DDescription(
                    Format.R16G16B16A16_Float, viewportWidth, viewportHeight, 1, 1,
                    BindFlags.RenderTarget | BindFlags.ShaderResource, ResourceUsage.Default,
                    CpuAccessFlags.None, 1, 0, ResourceOptionFlags.None);
                for (int i = 0; i < 2; i++)
                {
                    historyTextures[i] = device.CreateTexture2D(historyDescription, null);
                    historyRenderTargetViews[i] = device.CreateRenderTargetView(historyTextures[i]);
                    historyShaderResourceViews[i] = device.CreateShaderResourceView(historyTextures[i]);
                }
                historyWidth = viewportWidth;
                historyHeight = viewportHeight;
                InvalidateFancyHistory();
            }

            public void UpdateShadow(
                ID3D11DeviceContext1 context,
                ID3D11ComputeShader shader,
                ID3D11Buffer constants,
                GpuDensityFieldPreviewFrame frame)
            {
                if (!frame.FancyRender || shader == null || shadowTextureView == null || OccupancyShaderResourceView == null
                    || shadowVersion == frame.Version && shadowValueIndex == frame.ValueIndex) return;

                context.CSSetShader(shader);
                context.CSSetConstantBuffer(0, constants);
                context.CSSetShaderResource(3, OccupancyShaderResourceView);
                context.CSSetUnorderedAccessView(1, shadowTextureView, -1);
                context.Dispatch((occupancyBlocksX + 3) / 4, (occupancyBlocksY + 3) / 4, (occupancyBlocksZ + 3) / 4);
                context.CSSetUnorderedAccessView(1, null, -1);
                context.CSSetShaderResource(3, null);
                context.CSSetConstantBuffer(0, null);
                context.CSSetShader(null);
                shadowVersion = frame.Version;
                shadowValueIndex = frame.ValueIndex;
            }

            public bool PrepareFancyHistory(ulong cameraHash, long version)
            {
                if (historyTextures[0] == null) return false;
                if (historyVersion != version || historyCameraHash != cameraHash)
                {
                    historyVersion = version;
                    historyCameraHash = cameraHash;
                    historyFrameCount = 0;
                    historyReadIndex = 0;
                }
                return true;
            }

            public ID3D11RenderTargetView HistoryWriteTarget
            {
                get { return historyRenderTargetViews[1 - historyReadIndex]; }
            }

            public ID3D11ShaderResourceView HistoryReadView
            {
                get { return historyFrameCount > 0 ? historyShaderResourceViews[historyReadIndex] : null; }
            }

            public ID3D11ShaderResourceView HistoryWriteView
            {
                get { return historyShaderResourceViews[1 - historyReadIndex]; }
            }

            public void AdvanceFancyHistory()
            {
                historyReadIndex = 1 - historyReadIndex;
                if (historyFrameCount < 24) historyFrameCount++;
            }

            void DisposeHistoryTexture()
            {
                for (int i = 0; i < 2; i++)
                {
                    DisposeCom(historyShaderResourceViews[i]);
                    DisposeCom(historyRenderTargetViews[i]);
                    DisposeCom(historyTextures[i]);
                    historyShaderResourceViews[i] = null;
                    historyRenderTargetViews[i] = null;
                    historyTextures[i] = null;
                }
                historyWidth = 0;
                historyHeight = 0;
                InvalidateFancyHistory();
            }

            void DisposeFancyResources()
            {
                DisposeCom(ShadowShaderResourceView);
                DisposeCom(shadowTextureView);
                DisposeCom(shadowTexture);
                ShadowShaderResourceView = null;
                shadowTextureView = null;
                shadowTexture = null;
                shadowVersion = long.MinValue;
                shadowValueIndex = int.MinValue;
                DisposeHistoryTexture();
            }

            void DisposeOccupancyTexture()
            {
                DisposeCom(OccupancyShaderResourceView);
                DisposeCom(occupancyTextureView);
                DisposeCom(occupancyTexture);
                OccupancyShaderResourceView = null;
                occupancyTextureView = null;
                occupancyTexture = null;
                occupancyWidth = 0;
                occupancyHeight = 0;
                occupancyBlocksX = 0;
                occupancyBlocksY = 0;
                occupancyBlocksZ = 0;
                occupancyVersion = long.MinValue;
                occupancyValueIndex = int.MinValue;
            }

            public void Dispose()
            {
                DisposeCom(ShaderResourceView);
                DisposeCom(GradientShaderResourceView);
                DisposeOccupancyTexture();
                DisposeFancyResources();
                ShaderResourceView = null;
                GradientShaderResourceView = null;
                sharedHandle = IntPtr.Zero;
                gradientSharedHandle = IntPtr.Zero;
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
            readonly ID3D11ShaderResourceView[] pixelResources = new ID3D11ShaderResourceView[6];
            readonly ID3D11ComputeShader computeShader;
            readonly ID3D11Buffer[] computeConstantBuffers = new ID3D11Buffer[1];
            readonly ID3D11ShaderResourceView[] computeResources = new ID3D11ShaderResourceView[11];
            readonly ID3D11UnorderedAccessView[] computeUnorderedAccessViews = new ID3D11UnorderedAccessView[8];
            readonly ID3D11SamplerState[] pixelSamplers = new ID3D11SamplerState[1];
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
                context.PSGetSamplers(0, pixelSamplers);
                int computeClassInstanceCount = 0;
                context.CSGetShader(out computeShader, null, ref computeClassInstanceCount);
                context.CSGetConstantBuffers(0, computeConstantBuffers);
                context.CSGetShaderResources(0, computeResources);
                context.CSGetUnorderedAccessViews(0, computeUnorderedAccessViews.Length, computeUnorderedAccessViews);
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
                context.PSSetSamplers(0, pixelSamplers);
                context.CSSetShader(computeShader);
                context.CSSetConstantBuffers(0, computeConstantBuffers);
                context.CSSetShaderResources(0, computeResources);
                context.CSSetUnorderedAccessViews(
                    0,
                    computeUnorderedAccessViews,
                    new int[] { -1, -1, -1, -1, -1, -1, -1, -1 });
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
                for (int i = 0; i < pixelResources.Length; i++) DisposeCom(pixelResources[i]);
                DisposeCom(pixelSamplers[0]);
                DisposeCom(computeShader);
                DisposeCom(computeConstantBuffers[0]);
                for (int i = 0; i < computeResources.Length; i++) DisposeCom(computeResources[i]);
                for (int i = 0; i < computeUnorderedAccessViews.Length; i++) DisposeCom(computeUnorderedAccessViews[i]);
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
Texture2D<float4> GradientTexture : register(t1);
Texture2D<float4> TransferTexture : register(t2);
Texture2D<float> OccupancyTexture : register(t3);
Texture2D<float> ShadowTexture : register(t4);
Texture2D<float4> HistoryTexture : register(t5);
RWTexture2D<float> OccupancyOutput : register(u0);
RWTexture2D<float> ShadowOutput : register(u1);
SamplerState AtlasSampler : register(s0);

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

float4 SampleVolumeAtlasSlice(float2 voxelCoord, int z)
{
    int resX = max((int)VolumeGrid.x, 1);
    int resY = max((int)VolumeGrid.y, 1);
    int resZ = max((int)VolumeGrid.z, 1);
    int columns = max((int)VolumeGrid.w, 1);
    int rows = max((int)VolumeAtlas.x, 1);

    z = clamp(z, 0, resZ - 1);
    int column = z % columns;
    int row = z / columns;
    if (row >= rows)
    {
        return 0.0;
    }

    float2 atlasSize = max(ViewportAndTexture.zw, float2(1.0, 1.0));
    float2 clampedVoxel = clamp(voxelCoord, float2(0.0, 0.0), float2((float)(resX - 1), (float)(resY - 1)));
    float2 atlasPixel = float2((float)(column * resX), (float)(row * resY)) + clampedVoxel + 0.5;
    return DensityTexture.SampleLevel(AtlasSampler, atlasPixel / atlasSize, 0.0);
}

float4 SampleVolumeAtlasTrilinear(float3 worldPosition)
{
    int resX = max((int)VolumeGrid.x, 1);
    int resY = max((int)VolumeGrid.y, 1);
    int resZ = max((int)VolumeGrid.z, 1);

    float3 normalized = saturate(worldPosition / max(VolumeBox.xyz, float3(0.0001, 0.0001, 0.0001)));
    float3 coord = normalized * float3((float)max(resX - 1, 0), (float)max(resY - 1, 0), (float)max(resZ - 1, 0));
    int z0 = clamp((int)floor(coord.z), 0, resZ - 1);
    int z1 = min(z0 + 1, resZ - 1);
    float zBlend = frac(coord.z);
    float4 lower = SampleVolumeAtlasSlice(coord.xy, z0);
    float4 upper = SampleVolumeAtlasSlice(coord.xy, z1);
    return lerp(lower, upper, zBlend);
}

float4 LoadVolumeAtlasVoxel(int x, int y, int z);

float CubicWeight(float x)
{
    x = abs(x);
    if (x <= 1.0) return 1.0 - 2.5 * x * x + 1.5 * x * x * x;
    if (x < 2.0) return 2.0 - 4.0 * x + 2.5 * x * x - 0.5 * x * x * x;
    return 0.0;
}

float4 SampleVolumeAtlasTricubic(float3 worldPosition)
{
    int3 resolution = max((int3)VolumeGrid.xyz, int3(1, 1, 1));
    float3 normalized = saturate(worldPosition / max(VolumeBox.xyz, 0.0001));
    float3 coordinate = normalized * max(float3(resolution - 1), 0.0);
    int3 baseVoxel = (int3)floor(coordinate);
    float4 result = 0.0;
    float totalWeight = 0.0;
    float4 minimumSample = 1000000.0;
    float4 maximumSample = -1000000.0;
    [unroll]
    for (int z = -1; z <= 2; z++)
    {
        [unroll]
        for (int y = -1; y <= 2; y++)
        {
            [unroll]
            for (int x = -1; x <= 2; x++)
            {
                int3 voxel = clamp(baseVoxel + int3(x, y, z), 0, resolution - 1);
                float weight = CubicWeight((float)x - frac(coordinate.x))
                    * CubicWeight((float)y - frac(coordinate.y))
                    * CubicWeight((float)z - frac(coordinate.z));
                float4 voxelSample = LoadVolumeAtlasVoxel(voxel.x, voxel.y, voxel.z);
                result += voxelSample * weight;
                totalWeight += weight;
                minimumSample = min(minimumSample, voxelSample);
                maximumSample = max(maximumSample, voxelSample);
            }
        }
    }
    return clamp(result / max(totalWeight, 0.0001), minimumSample, maximumSample);
}

float4 SampleFancyVolume(float3 worldPosition)
{
    return UnusedTransform.y > 0.5 ? SampleVolumeAtlasTricubic(worldPosition) : SampleVolumeAtlasTrilinear(worldPosition);
}

float OccupancyPreviewValue(float4 sample)
{
    if (Thresholds.w < 5.5) return sample.r;

    float food = sample.a;
    if (Thresholds.w < 6.5) return food;
    if (Thresholds.w < 7.5) return max(food, sample.r);
    if (Thresholds.w < 8.5) return max(food, sample.g);
    if (Thresholds.w < 9.5) return max(food, sample.b);
    if (Thresholds.w < 10.5) return max(food, max(sample.g, sample.b));
    return max(food, max(sample.r, max(sample.g, sample.b)));
}

float4 LoadVolumeAtlasVoxel(int x, int y, int z)
{
    int columns = max((int)VolumeGrid.w, 1);
    int column = z % columns;
    int row = z / columns;
    int2 atlasPixel = int2(column * max((int)VolumeGrid.x, 1) + x, row * max((int)VolumeGrid.y, 1) + y);
    return DensityTexture.Load(int3(atlasPixel, 0));
}

[numthreads(4, 4, 4)]
void CSBuildOccupancy(uint3 id : SV_DispatchThreadID)
{
    const int blockSize = 4;
    int blocksX = max(((int)VolumeGrid.x + blockSize - 1) / blockSize, 1);
    int blocksY = max(((int)VolumeGrid.y + blockSize - 1) / blockSize, 1);
    int blocksZ = max(((int)VolumeGrid.z + blockSize - 1) / blockSize, 1);
    if (id.x >= blocksX || id.y >= blocksY || id.z >= blocksZ) return;

    float maximumValue = 0.0;
    [unroll]
    for (int offsetZ = 0; offsetZ < blockSize; offsetZ++)
    {
        int z = (int)id.z * blockSize + offsetZ;
        if (z >= (int)VolumeGrid.z) break;
        [unroll]
        for (int offsetY = 0; offsetY < blockSize; offsetY++)
        {
            int y = (int)id.y * blockSize + offsetY;
            if (y >= (int)VolumeGrid.y) break;
            [unroll]
            for (int offsetX = 0; offsetX < blockSize; offsetX++)
            {
                int x = (int)id.x * blockSize + offsetX;
                if (x >= (int)VolumeGrid.x) break;
                maximumValue = max(maximumValue, OccupancyPreviewValue(LoadVolumeAtlasVoxel(x, y, z)));
            }
        }
    }

    int columns = max((int)VolumeGrid.w, 1);
    int column = (int)id.z % columns;
    int row = (int)id.z / columns;
    int2 atlasPixel = int2(column * blocksX + (int)id.x, row * blocksY + (int)id.y);
    OccupancyOutput[atlasPixel] = maximumValue;
}

[numthreads(4, 4, 4)]
void CSBuildShadow(uint3 id : SV_DispatchThreadID)
{
    const int blockSize = 4;
    int blocksX = max(((int)VolumeGrid.x + blockSize - 1) / blockSize, 1);
    int blocksY = max(((int)VolumeGrid.y + blockSize - 1) / blockSize, 1);
    int blocksZ = max(((int)VolumeGrid.z + blockSize - 1) / blockSize, 1);
    if (id.x >= blocksX || id.y >= blocksY || id.z >= blocksZ) return;

    int columns = max((int)VolumeGrid.w, 1);
    float opticalDepth = 0.0;
    [loop]
    for (int step = 1; step <= 24; step++)
    {
        int3 sampleBlock = int3((int)id.x - step, (int)id.y - step, (int)id.z + step);
        if (any(sampleBlock < 0) || sampleBlock.x >= blocksX || sampleBlock.y >= blocksY || sampleBlock.z >= blocksZ) break;
        int sampleColumn = sampleBlock.z % columns;
        int sampleRow = sampleBlock.z / columns;
        float density = OccupancyTexture.Load(int3(sampleColumn * blocksX + sampleBlock.x, sampleRow * blocksY + sampleBlock.y, 0));
        opticalDepth += saturate(density * Style.y) * 0.12;
        if (opticalDepth > 4.0) break;
    }

    int column = (int)id.z % columns;
    int row = (int)id.z / columns;
    ShadowOutput[int2(column * blocksX + (int)id.x, row * blocksY + (int)id.y)] = exp(-opticalDepth);
}

float SampleShadow(float3 worldPosition)
{
    if (UnusedTransform.y < 0.5) return 1.0;
    const int blockSize = 4;
    int blocksX = max(((int)VolumeGrid.x + blockSize - 1) / blockSize, 1);
    int blocksY = max(((int)VolumeGrid.y + blockSize - 1) / blockSize, 1);
    int blocksZ = max(((int)VolumeGrid.z + blockSize - 1) / blockSize, 1);
    float3 normalized = saturate(worldPosition / max(VolumeBox.xyz, float3(0.0001, 0.0001, 0.0001)));
    int3 block = clamp((int3)floor(normalized * VolumeGrid.xyz / blockSize), 0, int3(blocksX - 1, blocksY - 1, blocksZ - 1));
    int columns = max((int)VolumeGrid.w, 1);
    int column = block.z % columns;
    int row = block.z / columns;
    return ShadowTexture.Load(int3(column * blocksX + block.x, row * blocksY + block.y, 0));
}

float SampleOccupancy(float3 worldPosition)
{
    if (UnusedTransform.x < 0.5) return 1.0;

    const int blockSize = 4;
    int blocksX = max(((int)VolumeGrid.x + blockSize - 1) / blockSize, 1);
    int blocksY = max(((int)VolumeGrid.y + blockSize - 1) / blockSize, 1);
    int blocksZ = max(((int)VolumeGrid.z + blockSize - 1) / blockSize, 1);
    float3 normalized = saturate(worldPosition / max(VolumeBox.xyz, float3(0.0001, 0.0001, 0.0001)));
    int3 block = clamp((int3)floor(normalized * float3(VolumeGrid.xyz) / blockSize), int3(0, 0, 0), int3(blocksX - 1, blocksY - 1, blocksZ - 1));
    int columns = max((int)VolumeGrid.w, 1);
    int column = block.z % columns;
    int row = block.z / columns;
    return OccupancyTexture.Load(int3(column * blocksX + block.x, row * blocksY + block.y, 0));
}

float4 SampleGradientAtlasSlice(float2 voxelCoord, int z)
{
    int resX = max((int)VolumeGrid.x, 1);
    int resY = max((int)VolumeGrid.y, 1);
    int resZ = max((int)VolumeGrid.z, 1);
    int columns = max((int)VolumeGrid.w, 1);
    int rows = max((int)VolumeAtlas.x, 1);

    z = clamp(z, 0, resZ - 1);
    int column = z % columns;
    int row = z / columns;
    if (row >= rows) return 0.0;

    float2 atlasSize = max(ViewportAndTexture.zw, float2(1.0, 1.0));
    float2 clampedVoxel = clamp(voxelCoord, float2(0.0, 0.0), float2((float)(resX - 1), (float)(resY - 1)));
    float2 atlasPixel = float2((float)(column * resX), (float)(row * resY)) + clampedVoxel + 0.5;
    return GradientTexture.SampleLevel(AtlasSampler, atlasPixel / atlasSize, 0.0);
}

float4 SampleGradientAtlasTrilinear(float3 worldPosition)
{
    int resX = max((int)VolumeGrid.x, 1);
    int resY = max((int)VolumeGrid.y, 1);
    int resZ = max((int)VolumeGrid.z, 1);
    float3 normalized = saturate(worldPosition / max(VolumeBox.xyz, float3(0.0001, 0.0001, 0.0001)));
    float3 coord = normalized * float3((float)max(resX - 1, 0), (float)max(resY - 1, 0), (float)max(resZ - 1, 0));
    int z0 = clamp((int)floor(coord.z), 0, resZ - 1);
    int z1 = min(z0 + 1, resZ - 1);
    return lerp(SampleGradientAtlasSlice(coord.xy, z0), SampleGradientAtlasSlice(coord.xy, z1), frac(coord.z));
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

float3 EstimateBroadVolumeGradient(float3 worldPosition)
{
    int resX = max((int)VolumeGrid.x, 1);
    int resY = max((int)VolumeGrid.y, 1);
    int resZ = max((int)VolumeGrid.z, 1);
    float3 voxelStep = VolumeBox.xyz / max(float3((float)resX, (float)resY, (float)resZ), float3(1.0, 1.0, 1.0));
    float3 nearStep = voxelStep * 2.5;
    float3 farStep = voxelStep * 5.0;

    float3 nearGradient = float3(
        PreviewValue(SampleVolumeAtlasTrilinear(worldPosition + float3(nearStep.x, 0.0, 0.0)))
            - PreviewValue(SampleVolumeAtlasTrilinear(worldPosition - float3(nearStep.x, 0.0, 0.0))),
        PreviewValue(SampleVolumeAtlasTrilinear(worldPosition + float3(0.0, nearStep.y, 0.0)))
            - PreviewValue(SampleVolumeAtlasTrilinear(worldPosition - float3(0.0, nearStep.y, 0.0))),
        PreviewValue(SampleVolumeAtlasTrilinear(worldPosition + float3(0.0, 0.0, nearStep.z)))
            - PreviewValue(SampleVolumeAtlasTrilinear(worldPosition - float3(0.0, 0.0, nearStep.z))));
    float3 farGradient = float3(
        PreviewValue(SampleVolumeAtlasTrilinear(worldPosition + float3(farStep.x, 0.0, 0.0)))
            - PreviewValue(SampleVolumeAtlasTrilinear(worldPosition - float3(farStep.x, 0.0, 0.0))),
        PreviewValue(SampleVolumeAtlasTrilinear(worldPosition + float3(0.0, farStep.y, 0.0)))
            - PreviewValue(SampleVolumeAtlasTrilinear(worldPosition - float3(0.0, farStep.y, 0.0))),
        PreviewValue(SampleVolumeAtlasTrilinear(worldPosition + float3(0.0, 0.0, farStep.z)))
            - PreviewValue(SampleVolumeAtlasTrilinear(worldPosition - float3(0.0, 0.0, farStep.z))));

    return lerp(nearGradient, farGradient, 0.62);
}

float VolumeTransfer(float normalizedValue)
{
    float visible = smoothstep(0.02, 0.16, normalizedValue);
    return visible * pow(saturate(normalizedValue), 0.82);
}

float3 VolumeTransferV2(float previousValue, float normalizedValue, float gradientMagnitude)
{
    float intervalDensity = saturate((previousValue + normalizedValue) * 0.5);
    float intervalSpan = abs(normalizedValue - previousValue);
    float relativeGradient = max(gradientMagnitude / max(intervalDensity, 0.08), intervalSpan);
    float boundaryCoordinate = smoothstep(0.06, 0.45, relativeGradient);
    float2 transferUv = (float2(saturate(previousValue), saturate(normalizedValue)) * 63.0 + 0.5) / 64.0;
    float4 integrated = TransferTexture.SampleLevel(
        AtlasSampler,
        transferUv,
        0.0);
    float edge = pow(boundaryCoordinate, 1.2);
    float densityContrast = smoothstep(0.025, 0.22, intervalDensity);
    float visual = integrated.r
        * lerp(0.055, 1.85, edge)
        * lerp(0.40, 1.14, densityContrast);
    float emission = (integrated.g + 1.61 * pow(edge, 1.35))
        * lerp(0.90, 1.08, densityContrast);
    float ridge = max(integrated.b, integrated.r) * edge;
    return float3(visual, emission, ridge);
}

float ToneMapVolumeV2(float value)
{
    value = max(value, 0.0);
    return pow(1.0 - exp(-value * 1.50), 0.96);
}

float3 ToneMapVolumeV2(float3 color)
{
    color = float3(ToneMapVolumeV2(color.r), ToneMapVolumeV2(color.g), ToneMapVolumeV2(color.b));
    float luminance = dot(color, float3(0.2126, 0.7152, 0.0722));
    return saturate(lerp(luminance.xxx, color, 1.42) * 1.13);
}

bool IsInsideVolume(float3 position)
{
    return all(position >= 0.0) && all(position <= VolumeBox.xyz);
}

float CoarseVolumeShadow(float3 position, float3 lightDirection, float voxelSize)
{
    float opticalDepth = 0.0;
    [unroll]
    for (int shadowIndex = 0; shadowIndex < 4; shadowIndex++)
    {
        float distanceInVoxels = 2.0 + shadowIndex * 2.5;
        float3 shadowPosition = position + lightDirection * voxelSize * distanceInVoxels;
        if (!IsInsideVolume(shadowPosition))
        {
            break;
        }

        float shadowValue = PreviewValue(SampleVolumeAtlasTrilinear(shadowPosition));
        float shadowDensity = VolumeTransfer(saturate(shadowValue * Style.y));
        opticalDepth += shadowDensity * 0.34;
    }

    return lerp(0.82, 1.0, exp(-opticalDepth));
}

float RayPositionToDepth(float3 nearWorld, float3 farWorld, float3 worldPosition, float2 screen)
{
    float3 ray = farWorld - nearWorld;
    float rayLength = max(length(ray), 0.000001);
    float q = saturate(dot(worldPosition - nearWorld, ray / rayLength) / rayLength);
    float w0 = ScreenToWorld[3][0] * screen.x + ScreenToWorld[3][1] * screen.y + ScreenToWorld[3][3];
    float perspective = ScreenToWorld[3][2];
    float denominator = w0 + perspective * (1.0 - q);
    if (abs(denominator) <= 0.000001)
    {
        return q;
    }

    return saturate((q * w0) / denominator);
}

float3 ShadeVolume(float3 color, float3 worldPosition, float3 rayDirection, float depthAlongRay)
{
    float3 gradient = EstimateVolumeGradient(worldPosition);
    float gradientStrength = length(gradient);
    float depthCue = lerp(1.06, 0.72, saturate(depthAlongRay));
    if (gradientStrength <= 0.00001)
    {
        return color * depthCue;
    }

    float3 normal = normalize(gradient);
    float3 lightDirection = normalize(-rayDirection + float3(-0.25, -0.35, 0.45));
    float diffuse = abs(dot(normal, lightDirection));
    float facing = abs(dot(normal, -rayDirection));
    float lighting = 0.62 + diffuse * 0.34 + pow(1.0 - facing, 2.0) * 0.12;
    return color * lighting * depthCue;
}

float4 RenderVolume(VSOutput input, out float representativeDepth)
{
    float3 nearWorld = ScreenToWorldPoint(input.Position.xy, 0.0);
    float3 farWorld = ScreenToWorldPoint(input.Position.xy, 1.0);
    float3 viewDirection = normalize(farWorld - nearWorld);
    float volumeDiagonal = length(VolumeBox.xyz);
    bool perspectiveProjection = Unused2.w > 0.5;
    float3 rayOrigin = perspectiveProjection
        ? Unused2.xyz
        : nearWorld - viewDirection * (volumeDiagonal + 1.0);
    float3 direction = perspectiveProjection
        ? normalize(farWorld - rayOrigin)
        : viewDirection;
    representativeDepth = input.Position.z;

    float enterT;
    float exitT;
    if (!IntersectVolumeBox(rayOrigin, direction, enterT, exitT))
    {
        clip(-1.0);
    }

    float startT = max(enterT, 0.0);
    float travel = max(exitT - startT, 0.0001);
    int configuredSteps = clamp((int)VolumeAtlas.y, 8, 256);
    bool version2 = VolumeAtlas.w > 1.5;

    float3 voxelExtent = VolumeBox.xyz / max(VolumeGrid.xyz, float3(1.0, 1.0, 1.0));
    float voxelSize = max(min(voxelExtent.x, min(voxelExtent.y, voxelExtent.z)), 0.0001);
    int maxSteps = clamp(configuredSteps, 24, 256);
    int steps = clamp((int)ceil((travel / voxelSize) * 0.82), 24, maxSteps);
    float baseStepLength = travel / (float)steps;
    float temporalNoise = frac(sin(dot(input.Position.xy, float2(12.9898, 78.233)) + UnusedTransform.z * 37.719) * 43758.5453);
    float t = startT + (UnusedTransform.y > 0.5 ? temporalNoise * baseStepLength : 0.0);

    float4 accumulated = float4(0.0, 0.0, 0.0, 0.0);
    float strongestContribution = 0.0;
    float3 strongestPosition = rayOrigin + direction * startT;
    float strongestDepth = 0.0;
    float3 representativePositionAccumulator = 0.0;
    float representativeWeight = 0.0;
    float representativeDepthMoment = 0.0;
    float representativeDepthMoment2 = 0.0;
    float ridgePeak = 0.0;
    float3 ridgeColor = 0.0;
    bool firstSurfaceFound = false;
    float3 firstSurfacePosition = 0.0;
    float3 firstSurfaceNormal = 0.0;
    float3 firstSurfaceColor = 0.0;
    float firstSurfaceStrength = 0.0;
    float firstSurfaceDepth = 0.0;
    float3 startPosition = strongestPosition;
    float previousNormalizedValue = saturate(PreviewValue(SampleVolumeAtlasTrilinear(startPosition)) * Style.y);
    float nextStepScale = 1.0;

    [loop]
    for (int i = 0; i < 256; i++)
    {
        if (t >= exitT - 0.000001 || accumulated.a > 0.985)
        {
            break;
        }

        float3 currentPosition = rayOrigin + direction * t;
        float occupancy = SampleOccupancy(currentPosition);
        if (occupancy * Style.y < 0.006)
        {
            float blockWorldSize = voxelSize * 4.0;
            float3 blockCoord = floor(currentPosition / blockWorldSize);
            float3 blockMin = blockCoord * blockWorldSize;
            float3 blockMax = min(blockMin + blockWorldSize, VolumeBox.xyz);
            float3 safeDirection = direction;
            safeDirection.x = abs(safeDirection.x) < 0.000001 ? (safeDirection.x < 0.0 ? -0.000001 : 0.000001) : safeDirection.x;
            safeDirection.y = abs(safeDirection.y) < 0.000001 ? (safeDirection.y < 0.0 ? -0.000001 : 0.000001) : safeDirection.y;
            safeDirection.z = abs(safeDirection.z) < 0.000001 ? (safeDirection.z < 0.0 ? -0.000001 : 0.000001) : safeDirection.z;
            float3 boundary = float3(
                safeDirection.x > 0.0 ? blockMax.x : blockMin.x,
                safeDirection.y > 0.0 ? blockMax.y : blockMin.y,
                safeDirection.z > 0.0 ? blockMax.z : blockMin.z);
            float3 axisDistance = (boundary - currentPosition) / safeDirection;
            float skipDistance = min(axisDistance.x, min(axisDistance.y, axisDistance.z));
            t = min(exitT, t + max(skipDistance, baseStepLength) + voxelSize * 0.001);
            previousNormalizedValue = 0.0;
            continue;
        }

        int remainingIterations = max(256 - i, 1);
        float minimumStepToFinish = (exitT - t) / remainingIterations;
        float localStepLength = max(baseStepLength * nextStepScale, minimumStepToFinish);
        float sampleT = min(t + localStepLength, exitT);
        localStepLength = sampleT - t;
        float3 position = rayOrigin + direction * sampleT;
        float4 sample = SampleFancyVolume(position);
        float value = PreviewValue(sample);
        float normalizedValue = saturate(value * Style.y);
        float4 gradientSample = version2 ? SampleGradientAtlasTrilinear(position) : 0.0;
        float gradientMagnitude = gradientSample.a * Style.y;
        nextStepScale = 1.0;
        if (InPreviewRange(value))
        {
            float3 transferSample = version2
                ? VolumeTransferV2(previousNormalizedValue, normalizedValue, gradientMagnitude)
                : float3(VolumeTransfer(normalizedValue), 1.0, 0.0);
            float visual = transferSample.r;
            float emission = transferSample.g;
            float boundary = transferSample.b;
            float extinctionScale = version2 ? 0.076 : 0.075;
            float opticalDepth = visual * VolumeAtlas.z * extinctionScale * (localStepLength / voxelSize);
            float sampleAlpha = 1.0 - exp(-opticalDepth);
            float contribution = (1.0 - accumulated.a) * sampleAlpha;
            float3 color = PreviewSampleColor(sample, value, visual);
            if (Thresholds.z > 0.5 && !IsDynamicColorPreview())
            {
                color = CustomColor.rgb * visual;
            }

            if (version2)
            {
                float3 normal = gradientSample.xyz;
                float normalLength = length(normal);
                float3 lightDirection = normalize(-direction + float3(-0.25, -0.35, 0.45));
                float lighting = 0.38;
                if (normalLength > 0.0001)
                {
                    normal /= normalLength;
                    float diffuse = saturate(dot(normal, lightDirection));
                    float backLight = saturate(dot(-normal, lightDirection));
                    float facing = abs(dot(normal, -direction));
                    lighting += diffuse * 0.70 + backLight * 0.16 + pow(1.0 - facing, 2.0) * 0.10;
                }

                float sampleDepth = saturate((sampleT - startT) / travel);
                color *= emission * lighting * lerp(1.10, 0.38, sampleDepth);

                if (UnusedTransform.y > 0.5 && !firstSurfaceFound && normalLength > 0.0001)
                {
                    float probeDistance = voxelSize * 1.75;
                    float densityBefore = saturate(PreviewValue(SampleVolumeAtlasTrilinear(position - direction * probeDistance)) * Style.y);
                    float densityAfter = saturate(PreviewValue(SampleVolumeAtlasTrilinear(position + direction * probeDistance)) * Style.y);
                    float coherentTransition = abs(densityAfter - densityBefore);
                    float surfaceSignal = smoothstep(0.14, 0.48, boundary)
                        * smoothstep(0.055, 0.24, normalizedValue)
                        * smoothstep(0.045, 0.22, coherentTransition);
                    if (surfaceSignal > 0.14)
                    {
                        float3 broadGradient = EstimateBroadVolumeGradient(position) * Style.y;
                        float broadGradientMagnitude = length(broadGradient);
                        surfaceSignal *= smoothstep(0.035, 0.22, broadGradientMagnitude);
                        if (surfaceSignal > 0.14)
                        {
                            firstSurfaceFound = true;
                            firstSurfacePosition = position;
                            firstSurfaceNormal = broadGradient;
                            firstSurfaceColor = Thresholds.z > 0.5 && !IsDynamicColorPreview()
                                ? CustomColor.rgb
                                : PreviewSampleColor(sample, value, max(visual, normalizedValue));
                            firstSurfaceStrength = surfaceSignal;
                            firstSurfaceDepth = sampleDepth;
                        }
                    }
                }

                if (boundary > 0.12)
                {
                    nextStepScale = 0.72;
                }
                else if (normalizedValue < 0.006 && previousNormalizedValue < 0.006 && gradientMagnitude < 0.006)
                {
                    nextStepScale = 1.35;
                }
            }

            accumulated.rgb += contribution * color;
            accumulated.a += contribution;

            if (version2 && contribution > 0.0)
            {
                representativePositionAccumulator += contribution * position;
                representativeWeight += contribution;
                float normalizedDepth = saturate((sampleT - startT) / travel);
                representativeDepthMoment += contribution * normalizedDepth;
                representativeDepthMoment2 += contribution * normalizedDepth * normalizedDepth;

                float ridgeScore = boundary * normalizedValue;
                if (ridgeScore > ridgePeak)
                {
                    ridgePeak = ridgeScore;
                    ridgeColor = color;
                }
            }

            if (contribution > strongestContribution)
            {
                strongestContribution = contribution;
                strongestPosition = position;
                strongestDepth = saturate((sampleT - startT) / travel);
            }
        }
        else if (version2 && normalizedValue < 0.006 && previousNormalizedValue < 0.006 && gradientMagnitude < 0.006)
        {
            nextStepScale = 1.35;
        }

        previousNormalizedValue = normalizedValue;
        t = sampleT;
    }

    if (accumulated.a <= 0.002)
    {
        clip(-1.0);
    }

    float3 color = accumulated.rgb / max(accumulated.a, 0.001);
    if (version2)
    {
        float3 representativePosition = representativeWeight > 0.0001
            ? representativePositionAccumulator / representativeWeight
            : strongestPosition;
        representativeDepth = RayPositionToDepth(nearWorld, farWorld, representativePosition, input.Position.xy);
        float weightedDepth = representativeDepthMoment / max(representativeWeight, 0.0001);
        float depthVariance = max(0.0, representativeDepthMoment2 / max(representativeWeight, 0.0001) - weightedDepth * weightedDepth);
        float depthSeparation = lerp(0.92, 1.08, saturate(sqrt(depthVariance) * 3.5));
        float3 lightDirection = normalize(-direction + float3(-0.25, -0.35, 0.45));
        float shadow = CoarseVolumeShadow(representativePosition, lightDirection, voxelSize);
        float ridgeBlend = 0.10 * smoothstep(0.04, 0.32, ridgePeak);
        color = lerp(color, max(color, ridgeColor * 0.82), ridgeBlend);
        color = ToneMapVolumeV2(color * shadow * depthSeparation);
        if (UnusedTransform.y > 0.5 && firstSurfaceFound)
        {
            float3 surfaceNormal = normalize(firstSurfaceNormal);
            if (dot(surfaceNormal, direction) > 0.0) surfaceNormal = -surfaceNormal;
            float3 surfaceLightDirection = normalize(-direction + float3(-0.32, -0.24, 0.52));
            float diffuse = saturate(dot(surfaceNormal, surfaceLightDirection));
            float rim = pow(1.0 - saturate(dot(surfaceNormal, -direction)), 2.2);
            float skyLight = 0.72 + 0.28 * saturate(surfaceNormal.z * 0.5 + 0.5);
            float surfaceLighting = (0.58 + diffuse * 0.42 + rim * 0.08) * skyLight;
            float3 surfaceColor = ToneMapVolumeV2(firstSurfaceColor * surfaceLighting * 1.10);
            float surfaceBlend = saturate(0.10 + firstSurfaceStrength * 0.24)
                * lerp(1.0, 0.72, firstSurfaceDepth);
            color = lerp(color, surfaceColor, surfaceBlend);
            accumulated.a = saturate(accumulated.a + (1.0 - accumulated.a) * surfaceBlend * 0.16);
        }
    }
    else
    {
        color = ShadeVolume(color, strongestPosition, direction, strongestDepth);
    }
    return float4(color, accumulated.a);
}

float4 PSMain(VSOutput input) : SV_Target
{
    float4 current;
    if (VolumeBox.w > 0.5)
    {
        float representativeDepth;
        current = RenderVolume(input, representativeDepth);
    }
    else
    {
        current = RenderPlane(input);
    }

    if (UnusedTransform.y > 0.5 && UnusedTransform.z > 0.5)
    {
        float4 previous = HistoryTexture.Load(int3((int2)input.Position.xy, 0));
        float blendWeight = 1.0 / min(UnusedTransform.z + 1.0, 24.0);
        return lerp(previous, current, blendWeight);
    }
    return current;
}

float4 PSComposite(VSOutput input) : SV_Target
{
    return HistoryTexture.Load(int3((int2)input.Position.xy, 0));
}";
    }
}
