using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

using Vortice.Direct3D11;
using Vortice.DXGI;
using Vortice.Mathematics;

namespace Nuclei4
{
    // A separate spatial-only draw path. The source atlas, transfer function and
    // per-ray sample budget are supplied unchanged by the existing renderer.
    internal sealed class NewVolumeD3DRenderer : IDisposable
    {
        const int MaximumCachedViewports = 4;
        readonly ID3D11Device device;
        readonly Dictionary<Guid, ReducedTarget> targets = new Dictionary<Guid, ReducedTarget>();
        ID3D11PixelShader rayShader;
        ID3D11PixelShader compositeShader;
        ID3D11BlendState premultipliedBlend;
        ID3D11BlendState opaqueBlend;
        ID3D11Buffer viewportConstants;
        long useSequence;

        public NewVolumeD3DRenderer(ID3D11Device device, byte[] rayBytecode, byte[] compositeBytecode)
        {
            this.device = device;
            try
            {
                rayShader = device.CreatePixelShader(rayBytecode, null);
                compositeShader = device.CreatePixelShader(compositeBytecode, null);
                premultipliedBlend = device.CreateBlendState(BlendDescription.AlphaBlend);
                opaqueBlend = device.CreateBlendState(BlendDescription.Opaque);
                viewportConstants = device.CreateBuffer(32, BindFlags.ConstantBuffer,
                    ResourceUsage.Dynamic, CpuAccessFlags.Write, ResourceOptionFlags.None, 0);
            }
            catch
            {
                Dispose();
                throw;
            }
        }

        public bool TryDraw(ID3D11DeviceContext1 context, Guid viewportId)
        {
            Viewport[] originalViewports = context.RSGetViewports<Viewport>();
            // A single fullscreen triangle cannot address several raster viewports.
            // Preserve the original path for an unsupported host draw configuration.
            if (originalViewports.Length != 1) return false;
            Viewport full = originalViewports[0];
            if (!(full.Width > 0) || !(full.Height > 0)
                || float.IsInfinity(full.Width) || float.IsInfinity(full.Height)) return false;

            int width = Math.Max(1, (int)Math.Ceiling(full.Width * 0.5));
            int height = Math.Max(1, (int)Math.Ceiling(full.Height * 0.5));
            ReducedTarget target = GetTarget(viewportId, width, height);
            // The full raster viewport, not RhinoViewport.Size, determines the
            // low-resolution target. This also handles odd-size captures and origins.
            float[] values =
            {
                full.X, full.Y, full.Width, full.Height,
                full.Width / width, full.Height / height, 1.0f / full.Width, 1.0f / full.Height
            };
            MappedSubresource mapped = context.Map(viewportConstants, MapMode.WriteDiscard, Vortice.Direct3D11.MapFlags.None);
            try { Marshal.Copy(values, 0, mapped.DataPointer, values.Length); }
            finally { context.Unmap(viewportConstants); }

            ID3D11RenderTargetView[] originalTargets = new ID3D11RenderTargetView[8];
            ID3D11DepthStencilView originalDepth = null;
            ID3D11Buffer[] originalConstants = new ID3D11Buffer[1];
            int[] originalFirstConstants = new int[1];
            int[] originalConstantCounts = new int[1];
            ID3D11ShaderResourceView[] originalResources = new ID3D11ShaderResourceView[1];
            ID3D11PixelShader originalShader = null;
            try
            {
                context.OMGetRenderTargets(originalTargets.Length, originalTargets, out originalDepth);
                context.PSGetConstantBuffers1(1, 1, originalConstants, originalFirstConstants, originalConstantCounts);
                context.PSGetShaderResources(5, originalResources);
                originalShader = context.PSGetShader();
                try
                {
                    context.PSSetShaderResource(5, null);
                    context.OMSetRenderTargets(target.RenderTarget, null);
                    context.RSSetViewport(new Viewport(0, 0, width, height, full.MinDepth, full.MaxDepth));
                    context.ClearRenderTargetView(target.RenderTarget, new Color4(0, 0, 0, 0));
                    context.OMSetBlendState(opaqueBlend);
                    context.PSSetConstantBuffer(1, viewportConstants);
                    context.PSSetShader(rayShader);
                    context.Draw(3, 0);

                    // Unbind the off-screen target before sampling it. Filtering
                    // premultiplied color avoids a dark fringe at transparent edges.
                    context.OMSetRenderTargets(originalTargets, originalDepth);
                    context.RSSetViewports(originalViewports);
                    context.OMSetBlendState(premultipliedBlend);
                    context.PSSetShader(compositeShader);
                    context.PSSetShaderResource(5, target.ShaderResource);
                    context.Draw(3, 0);
                    return true;
                }
                finally
                {
                    context.PSSetShaderResource(5, null);
                    context.OMSetRenderTargets(originalTargets, originalDepth);
                    context.RSSetViewports(originalViewports);
                    context.PSSetConstantBuffers1(1, originalConstants, originalFirstConstants, originalConstantCounts);
                    context.PSSetShaderResources(5, originalResources);
                    context.PSSetShader(originalShader);
                    // The owning renderer restores the caller's blend/depth/raster
                    // state; its fallback draw explicitly restores its own blend.
                }
            }
            finally
            {
                foreach (ID3D11RenderTargetView view in originalTargets) if (view != null) view.Dispose();
                if (originalDepth != null) originalDepth.Dispose();
                if (originalConstants[0] != null) originalConstants[0].Dispose();
                if (originalResources[0] != null) originalResources[0].Dispose();
                if (originalShader != null) originalShader.Dispose();
            }
        }

        ReducedTarget GetTarget(Guid viewportId, int width, int height)
        {
            ReducedTarget target;
            if (targets.TryGetValue(viewportId, out target))
            {
                if (target.Width == width && target.Height == height)
                {
                    target.LastUse = ++useSequence;
                    return target;
                }
                target.Dispose();
                targets.Remove(viewportId);
            }
            if (targets.Count >= MaximumCachedViewports)
            {
                Guid oldestId = Guid.Empty;
                long oldestUse = long.MaxValue;
                foreach (KeyValuePair<Guid, ReducedTarget> pair in targets)
                    if (pair.Value.LastUse < oldestUse) { oldestId = pair.Key; oldestUse = pair.Value.LastUse; }
                targets[oldestId].Dispose();
                targets.Remove(oldestId);
            }
            target = new ReducedTarget(device, width, height) { LastUse = ++useSequence };
            targets.Add(viewportId, target);
            return target;
        }

        public void Dispose()
        {
            foreach (ReducedTarget target in targets.Values) target.Dispose();
            targets.Clear();
            if (viewportConstants != null) viewportConstants.Dispose();
            if (opaqueBlend != null) opaqueBlend.Dispose();
            if (premultipliedBlend != null) premultipliedBlend.Dispose();
            if (compositeShader != null) compositeShader.Dispose();
            if (rayShader != null) rayShader.Dispose();
            viewportConstants = null;
            opaqueBlend = null;
            premultipliedBlend = null;
            compositeShader = null;
            rayShader = null;
        }

        sealed class ReducedTarget : IDisposable
        {
            public readonly int Width;
            public readonly int Height;
            public long LastUse;
            ID3D11Texture2D texture;
            public ID3D11RenderTargetView RenderTarget;
            public ID3D11ShaderResourceView ShaderResource;

            public ReducedTarget(ID3D11Device device, int width, int height)
            {
                Width = width;
                Height = height;
                try
                {
                    texture = device.CreateTexture2D(new Texture2DDescription(
                        Format.R16G16B16A16_Float, width, height, 1, 1,
                        BindFlags.RenderTarget | BindFlags.ShaderResource, ResourceUsage.Default,
                        CpuAccessFlags.None, 1, 0, ResourceOptionFlags.None));
                    RenderTarget = device.CreateRenderTargetView(texture);
                    ShaderResource = device.CreateShaderResourceView(texture);
                }
                catch { Dispose(); throw; }
            }

            public void Dispose()
            {
                if (ShaderResource != null) ShaderResource.Dispose();
                if (RenderTarget != null) RenderTarget.Dispose();
                if (texture != null) texture.Dispose();
                ShaderResource = null;
                RenderTarget = null;
                texture = null;
            }
        }
    }
}
