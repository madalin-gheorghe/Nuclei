using System;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using Vortice.D3DCompiler;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;

namespace Nuclei4
{
    // One GPU lane writes one selection word: only one bit per voxel crosses back to the CPU.
    internal sealed class GpuPeriodicSurface : IDisposable
    {
        ID3D11Device device;
        ID3D11DeviceContext context;
        ID3D11ComputeShader shader;
        ID3D11Buffer output, staging, parameters;
        ID3D11UnorderedAccessView view;
        string expression;
        int capacity;
        public static string Translate(string source)
        {
            if (string.IsNullOrWhiteSpace(source) || source.Length > 4096) throw new ArgumentException("Enter a formula of at most 4096 characters.");
            string translated = Regex.Replace(source, @"\bMath\.(Sin|Cos|Tan|Asin|Acos|Atan|Atan2|Sinh|Cosh|Tanh|Abs|Sqrt|Pow|Exp|Log|Log10|Floor|Ceiling|Min|Max|PI|E)\b", m => m.Groups[1].Value == "PI" ? "3.141592653589793" : m.Groups[1].Value == "E" ? "2.718281828459045" : m.Groups[1].Value == "Ceiling" ? "ceil" : m.Groups[1].Value.ToLowerInvariant());
            string remainder = Regex.Replace(translated, @"\b(?:\d+(?:\.\d*)?|\.\d+)(?:[eE][+-]?\d+)?\b", "");
            remainder = Regex.Replace(remainder, @"\b(?:x|y|z|sin|cos|tan|asin|acos|atan|atan2|sinh|cosh|tanh|abs|sqrt|pow|exp|log|log10|floor|ceil|min|max)\b", "");
            if (Regex.IsMatch(remainder, @"[^\s\d.+*/%(),\-]")) throw new ArgumentException("Use x, y, z, numbers, arithmetic and supported Math functions only. Enter the expression without an assignment or semicolon.");
            return translated;
        }
        [StructLayout(LayoutKind.Sequential)]
        struct Parameters
        {
            public int X, Y, Z, Count;
            public float Scale, Iso; public int Words; public float VoxelSize;
            public float MinRange, MaxRange, CoordinateOffset, Padding1;
        }
        public int[] Select(VoxelGridData data, string formula, float scale, float iso, float minRange, float maxRange)
            => SelectCore(data, formula, scale, iso, minRange, maxRange, false);
        public int[] SelectCentered(VoxelGridData data, string formula, float scale, float iso, float minRange, float maxRange)
            => SelectCore(data, formula, scale, iso, minRange, maxRange, true);
        int[] SelectCore(VoxelGridData data, string formula, float scale, float iso, float minRange, float maxRange, bool centered)
        {
            double normalizedMin = minRange, normalizedMax = maxRange;
            VoxelAttractorRange.Normalize(ref normalizedMin, ref normalizedMax, data.VoxelSize, true);
            minRange = (float)normalizedMin; maxRange = (float)normalizedMax;
            if (!Finite(scale) || !Finite(iso) || !Finite(maxRange) || !Finite((float)data.VoxelSize) || (float)data.VoxelSize <= 0)
                throw new ArgumentException("Use finite values and a positive voxel size within GPU float range.");
            if (data.Count == 0) return Array.Empty<int>();
            if (device == null)
            {
                D3D11.D3D11CreateDevice(IntPtr.Zero, DriverType.Hardware, DeviceCreationFlags.None,
                    new[] { FeatureLevel.Level_11_0 }, out device, out _, out context).CheckError();
                parameters = device.CreateBuffer(48, BindFlags.ConstantBuffer, ResourceUsage.Default, CpuAccessFlags.None, ResourceOptionFlags.None, 0);
            }
            bool offsetPairs = minRange > 0 && maxRange - minRange < 2 * data.VoxelSize;
            string shaderKey = formula + (offsetPairs ? "\nOffsetPairs" : "\nRegular");
            if (expression != shaderKey)
            {
                string source = "#define OFFSET_PAIRS " + (offsetPairs ? "1\n" : "0\n") + @"cbuffer Params : register(b0) {
 uint rx, ry, rz, count; float scale, iso; uint words; float voxelSize;
 float minRange, maxRange, coordinateOffset, padding1;
};
RWStructuredBuffer<uint> selection : register(u0);
float Function(float3 p) { float x=p.x, y=p.y, z=p.z; return " + formula + @"; }
bool Crosses(float value, float neighbor) {
 return isfinite(neighbor) && ((value <= 0 && neighbor >= 0) || (value >= 0 && neighbor <= 0));
}
float Distance(float3 p, float3 step, float value) {
 if (!isfinite(value)) return -1;
 if (value == 0) return 0;
 // Central differences over 1% of a voxel, with the chain rule in model units.
 float3 h = step * 0.01;
 float3 delta = float3(
   Function(p + float3(h.x,0,0)) - Function(p - float3(h.x,0,0)),
   Function(p + float3(0,h.y,0)) - Function(p - float3(0,h.y,0)),
   Function(p + float3(0,0,h.z)) - Function(p - float3(0,0,h.z)));
 float gradient = length(delta) / (0.02 * voxelSize);
 return gradient > 0 && isfinite(gradient) ? abs(value) / gradient : -1;
}
bool OffsetCrosses(float3 neighbor, float3 step, float distance, float middle) {
 float other = Distance(neighbor, step, Function(neighbor)-iso);
 return other >= 0 && Crosses(distance-middle, other-middle);
}
bool Selected(uint i) {
 uint3 cell = uint3(i / (ry * rz), (i / rz) % ry, i % rz);
 float3 step = scale / float3(rx, ry, rz);
 float3 p = step * (float3(cell) - coordinateOffset * (float3(rx,ry,rz)-1));
 float value = Function(p) - iso;
 if (!isfinite(value)) return false;
 if (value == 0 && minRange == 0) return true;
 float distance = Distance(p, step, value);
 float outer = maxRange;
 // Include exact range boundaries despite single-precision derivative roundoff.
 float tolerance = 0.0001 * voxelSize;
 if (distance >= 0 && distance + tolerance >= minRange && distance <= outer + tolerance) return true;

 // Retain both ends of a sampled crossing even when a flat gradient makes
 // the distance estimate unreliable. Only apply this to bands touching the surface.
 if (minRange != 0) {
#if OFFSET_PAIRS
   // A shifted thin wall needs pairs across its sampled distance mid-level.
   if (maxRange-minRange >= 2*voxelSize || distance < 0) return false;
   float middle = minRange + (maxRange-minRange)*0.5;
   if (cell.x > 0 && OffsetCrosses(p-float3(step.x,0,0),step,distance,middle)) return true;
   if (cell.x+1 < rx && OffsetCrosses(p+float3(step.x,0,0),step,distance,middle)) return true;
   if (cell.y > 0 && OffsetCrosses(p-float3(0,step.y,0),step,distance,middle)) return true;
   if (cell.y+1 < ry && OffsetCrosses(p+float3(0,step.y,0),step,distance,middle)) return true;
   if (cell.z > 0 && OffsetCrosses(p-float3(0,0,step.z),step,distance,middle)) return true;
   if (cell.z+1 < rz && OffsetCrosses(p+float3(0,0,step.z),step,distance,middle)) return true;
#endif
   return false;
 }
 if (cell.x > 0 && Crosses(value, Function(p-float3(step.x,0,0))-iso)) return true;
 if (cell.x+1 < rx && Crosses(value, Function(p+float3(step.x,0,0))-iso)) return true;
 if (cell.y > 0 && Crosses(value, Function(p-float3(0,step.y,0))-iso)) return true;
 if (cell.y+1 < ry && Crosses(value, Function(p+float3(0,step.y,0))-iso)) return true;
 if (cell.z > 0 && Crosses(value, Function(p-float3(0,0,step.z))-iso)) return true;
 if (cell.z+1 < rz && Crosses(value, Function(p+float3(0,0,step.z))-iso)) return true;
 return false;
}
[numthreads(128,1,1)] void Evaluate(uint3 id : SV_DispatchThreadID) {
 uint w = id.x + id.y * 65535u * 128u;
 if (w >= words) return;
 uint mask = 0;
 for (uint b = 0; b < 32; b++) {
 uint i = w * 32 + b;
 if (i >= count) break;
 if (Selected(i)) mask |= 1u << b;
 }
 selection[w] = mask;
}";
                var result = Compiler.Compile(source, null, null, "Evaluate", "NucleiPeriodicSurface", "cs_5_0", ShaderFlags.OptimizationLevel3, EffectFlags.None, out var code, out var errors);
                using (code)
                using (errors)
                {
                    if (result.Failure) throw new ArgumentException("Formula shader compilation failed: " + (errors == null ? result.ToString() : Marshal.PtrToStringAnsi(errors.BufferPointer)));
                    var next = device.CreateComputeShader(code, null);
                    shader?.Dispose(); shader = next; expression = shaderKey;
                }
            }
            int words = (data.Count + 31) >> 5;
            if (capacity != words)
            {
                view?.Dispose(); output?.Dispose(); staging?.Dispose();
                view = null; output = null; staging = null; capacity = 0;
                output = device.CreateBuffer(checked(words * 4), BindFlags.UnorderedAccess, ResourceUsage.Default, CpuAccessFlags.None, ResourceOptionFlags.BufferStructured, 4);
                staging = device.CreateBuffer(checked(words * 4), BindFlags.None, ResourceUsage.Staging, CpuAccessFlags.Read, ResourceOptionFlags.None, 0);
                view = device.CreateUnorderedAccessView(output, new UnorderedAccessViewDescription(output, Format.Unknown, 0, words, BufferUnorderedAccessViewFlags.None));
                capacity = words;
            }
            var p = new Parameters { X = data.ResX, Y = data.ResY, Z = data.ResZ, Count = data.Count, Scale = scale, Iso = iso, Words = words,
                VoxelSize = (float)data.VoxelSize, MinRange = minRange, MaxRange = maxRange, CoordinateOffset = centered ? 0.5f : 0 };
            context.UpdateSubresourceSafe(ref p, parameters, 0, 0, 0, 0, false);
            context.CSSetShader(shader);
            context.CSSetConstantBuffers(0, new[] { parameters });
            context.CSSetUnorderedAccessView(0, view, -1);
            int groups = (words + 127) / 128;
            context.Dispatch(Math.Min(groups, 65535), (groups + 65534) / 65535, 1);
            context.CSSetUnorderedAccessView(0, null, -1);
            context.CSSetShader(null);
            context.CopyResource(staging, output);
            var bits = new int[words];
            var mapped = context.Map(staging, MapMode.Read, Vortice.Direct3D11.MapFlags.None);
            try { Marshal.Copy(mapped.DataPointer, bits, 0, words); }
            finally { context.Unmap(staging); }
            data.AndActiveSelection(bits);
            return bits;
        }
        static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
        public void Dispose()
        {
            view?.Dispose(); output?.Dispose(); staging?.Dispose(); parameters?.Dispose(); shader?.Dispose(); context?.Dispose(); device?.Dispose();
            view = null; output = null; staging = null; parameters = null; shader = null; context = null; device = null; capacity = 0; expression = null;
        }
    }
}
