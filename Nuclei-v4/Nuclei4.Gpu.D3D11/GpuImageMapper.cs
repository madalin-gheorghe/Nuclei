using System;
using System.Runtime.InteropServices;
using Vortice.D3DCompiler;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;

namespace Nuclei4
{
    // Host-neutral image sampling. Arrays are BGRA pixels in top-to-bottom row order.
    internal sealed class GpuImageMapper : IDisposable
    {
        ID3D11Device device;
        ID3D11DeviceContext context;
        ID3D11ComputeShader shader;
        ID3D11Buffer pixels, output, staging, parameters;
        ID3D11ShaderResourceView imageView;
        ID3D11UnorderedAccessView outputView;
        int[] uploadedPixels;
        int capacity;
        public bool SoftwareFallback { get; private set; }

        [StructLayout(LayoutKind.Sequential)]
        struct Parameters
        {
            public int X, Y, Z, Count;
            public int Width, Height, ClampDensity, Padding;
            public float Start, End, Padding2, Padding3;
        }

        public float[] Map(int[] image, int width, int height, int x, int y, int z,
            float start, float end, bool clampDensity)
        {
            if (x < 1 || y < 1 || z < 1 || (x > 1 && y > 1 && z > 1))
                throw new ArgumentException("Only 2D Voxel Field Allowed");
            if (width < 1 || height < 1 || image == null || image.Length != checked(width * height))
                throw new ArgumentException("Invalid image pixels.");
            if (float.IsNaN(start) || float.IsInfinity(start) || float.IsNaN(end) || float.IsInfinity(end))
                throw new ArgumentException("Target values must be finite.");
            int count = checked(x * y * z);
            try
            {
                EnsureDevice();
                if (!ReferenceEquals(uploadedPixels, image))
                {
                    imageView?.Dispose(); imageView = null;
                    pixels?.Dispose(); pixels = null; uploadedPixels = null;
                    pixels = device.CreateBuffer(image, BindFlags.ShaderResource, ResourceUsage.Immutable,
                        CpuAccessFlags.None, ResourceOptionFlags.BufferStructured, checked(image.Length * 4), 4);
                    imageView = device.CreateShaderResourceView(pixels,
                        new ShaderResourceViewDescription(pixels, Format.Unknown, 0, image.Length, BufferExtendedShaderResourceViewFlags.None));
                    uploadedPixels = image;
                }
                if (capacity != count)
                {
                    outputView?.Dispose(); outputView = null;
                    output?.Dispose(); output = null;
                    staging?.Dispose(); staging = null; capacity = 0;
                    output = device.CreateBuffer(checked(count * 4), BindFlags.UnorderedAccess, ResourceUsage.Default,
                        CpuAccessFlags.None, ResourceOptionFlags.BufferStructured, 4);
                    staging = device.CreateBuffer(checked(count * 4), BindFlags.None, ResourceUsage.Staging,
                        CpuAccessFlags.Read, ResourceOptionFlags.None, 0);
                    outputView = device.CreateUnorderedAccessView(output,
                        new UnorderedAccessViewDescription(output, Format.Unknown, 0, count, BufferUnorderedAccessViewFlags.None));
                    capacity = count;
                }
                var p = new Parameters { X = x, Y = y, Z = z, Count = count, Width = width, Height = height,
                    Start = start, End = end, ClampDensity = clampDensity ? 1 : 0 };
                context.UpdateSubresourceSafe(ref p, parameters, 0, 0, 0, 0, false);
                context.CSSetShader(shader);
                context.CSSetConstantBuffers(0, new[] { parameters });
                context.CSSetShaderResources(0, new[] { imageView });
                context.CSSetUnorderedAccessView(0, outputView, -1);
                int groups = (count + 255) / 256;
                context.Dispatch(Math.Min(groups, 65535), (groups + 65534) / 65535, 1);
                context.CSSetUnorderedAccessView(0, null, -1);
                context.CSSetShaderResources(0, new ID3D11ShaderResourceView[] { null });
                context.CSSetShader(null);
                context.CopyResource(staging, output);
                var values = new float[count];
                var mapped = context.Map(staging, MapMode.Read, Vortice.Direct3D11.MapFlags.None);
                try { Marshal.Copy(mapped.DataPointer, values, 0, count); }
                finally { context.Unmap(staging); }
                return values;
            }
            catch { Dispose(); throw; }
        }

        void EnsureDevice()
        {
            if (device != null) return;
            var levels = new[] { FeatureLevel.Level_11_0 };
            var result = D3D11.D3D11CreateDevice(IntPtr.Zero, DriverType.Hardware, DeviceCreationFlags.None,
                levels, out device, out _, out context);
            SoftwareFallback = result.Failure;
            if (result.Failure)
            {
                context?.Dispose(); device?.Dispose(); context = null; device = null;
                D3D11.D3D11CreateDevice(IntPtr.Zero, DriverType.Warp, DeviceCreationFlags.None,
                    levels, out device, out _, out context).CheckError();
            }
            var compiled = Compiler.Compile(ShaderSource, null, null, "MapImage", "NucleiImageMapper", "cs_5_0",
                ShaderFlags.OptimizationLevel3, EffectFlags.None, out var code, out var errors);
            using (code)
            using (errors)
            {
                if (compiled.Failure) throw new InvalidOperationException("Image shader compilation failed: " +
                    (errors == null ? compiled.ToString() : Marshal.PtrToStringAnsi(errors.BufferPointer)));
                shader = device.CreateComputeShader(code, null);
            }
            parameters = device.CreateBuffer(48, BindFlags.ConstantBuffer, ResourceUsage.Default,
                CpuAccessFlags.None, ResourceOptionFlags.None, 0);
        }

        public void Dispose()
        {
            context?.ClearState();
            imageView?.Dispose(); outputView?.Dispose(); pixels?.Dispose(); output?.Dispose(); staging?.Dispose();
            parameters?.Dispose(); shader?.Dispose(); context?.Dispose(); device?.Dispose();
            imageView = null; outputView = null; pixels = null; output = null; staging = null;
            parameters = null; shader = null; context = null; device = null; uploadedPixels = null; capacity = 0;
        }

        const string ShaderSource = @"
cbuffer Params : register(b0) {
    uint rx, ry, rz, count;
    uint width, height, clampDensity, padding;
    float targetStart, targetEnd, padding2, padding3;
};
StructuredBuffer<uint> image : register(t0);
RWStructuredBuffer<float> values : register(u0);
float Gray(uint2 p) {
    uint bgra = image[p.y * width + p.x];
    float3 rgb = float3((bgra >> 16) & 255, (bgra >> 8) & 255, bgra & 255) / 255.0;
    float alpha = (bgra >> 24) / 255.0;
    return lerp(1.0, dot(rgb, float3(0.2126, 0.7152, 0.0722)), alpha);
}
[numthreads(256,1,1)] void MapImage(uint3 id : SV_DispatchThreadID) {
    uint i = id.x + id.y * 65535u * 256u;
    if (i >= count) return;
    uint3 cell = uint3(i / (ry * rz), (i / rz) % ry, i % rz);
    // Match the voxel constructor's plane precedence, including line/point grids.
    float2 uv = rz == 1 ? (float2(cell.xy) + 0.5) / float2(rx, ry)
        : ry == 1 ? (float2(cell.xz) + 0.5) / float2(rx, rz)
        : (float2(cell.yz) + 0.5) / float2(ry, rz);
    uv.y = 1.0 - uv.y; // Image rows descend; the vertical voxel axis ascends.
    float2 p = clamp(uv * float2(width, height) - 0.5, 0, float2(width-1, height-1));
    uint2 a = (uint2)floor(p), b = min(a + 1, uint2(width-1, height-1));
    float2 f = frac(p);
    float gray = saturate(lerp(lerp(Gray(a), Gray(uint2(b.x,a.y)), f.x),
        lerp(Gray(uint2(a.x,b.y)), Gray(b), f.x), f.y));
    // A convex blend also supports reversed ranges without overflowing end-start.
    float value = (1.0-gray) * targetStart + gray * targetEnd;
    if (clampDensity != 0 && value != -1.0) value = saturate(value);
    values[i] = value;
}";
    }
}
