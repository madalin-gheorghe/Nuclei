# Trail capture regression probe

This standalone diagnostic starts a hidden Rhino 9 WIP process and creates an unsaved, deterministic scene. It never opens user Grasshopper definitions. The fixture contains 27 short trail segments at different positions and depths. GPU mode constructs a shared D3D11 texture and calls the built V4 `ParticleTrailD3DRenderer.TryDraw` through reflection.

Run from the repository root:

```powershell
dotnet build tools/Nuclei.TrailCaptureProbe/Nuclei.TrailCaptureProbe.csproj -c Release -m:1 -p:BuildInParallel=false -nr:false
dotnet tools/Nuclei.TrailCaptureProbe/bin/Release/net8.0-windows/Nuclei.TrailCaptureProbe.dll --compile-shaders

# First capture the native reference and compare the old/new projection calculations.
dotnet tools/Nuclei.TrailCaptureProbe/bin/Release/net8.0-windows/Nuclei.TrailCaptureProbe.dll --full-rhino --projection-only --case parallel-1200x1200 --output .codex-temp/trail-capture-square

# In a new process, capture the actual GPU renderer and compare against that saved reference.
dotnet tools/Nuclei.TrailCaptureProbe/bin/Release/net8.0-windows/Nuclei.TrailCaptureProbe.dll --full-rhino --gpu-only --case parallel-1200x1200 --output .codex-temp/trail-capture-square
```

Use `--renderer <absolute-or-repository-relative-dll-path>` to test a preserved baseline or another build. Defaults to the V4 display project's Release/net7.0-windows output. Runtime dependencies beside that assembly are resolved dynamically; RhinoCommon is always loaded from the installed Rhino 9 WIP runtime.

Available cases combine `parallel-` or `perspective-` with `960x640`, `1920x1280`, `1200x1200`, `1800x600`, or `600x1800`. Add `--large` for `5000x3000`. Camera and native geometry are deterministic across processes, so use the same case when generating a reference and its GPU comparison. A reference is never replaced in `--gpu-only` mode.

Each GPU comparison requires at least 50 native and GPU pixels and 97% bidirectional coverage within three pixels, allowing line antialiasing differences. PNGs and JSON reports are written to the ignored output directory. Exit status is 0 for pass, 1 for alignment failure, 2 for unavailable GPU drawing, or 3 for a managed error/watchdog timeout. `--compile-shaders` compiles the actual renderer's vertex/pixel shader strings without starting Rhino.

Use one `--case` per process. On this installed WIP, a second hidden `ViewCaptureToBitmap` or subsequent RhinoCore teardown can stall. After a single-case report is flushed, the probe exits its own process directly; the OS releases the synthetic document and GPU resources. This is a diagnostic-host limitation. Running Rhino also needs access to its installed runtime and license files, which can require sandbox escalation.

Reports include the capture pipeline projection and `IsInTiledDraw` results. A large output is not proof that Rhino used tiled drawing. The projection-only path is diagnostic evidence for untiled captures; GPU mode is the end-to-end rendering check.

Validated on Rhino 9 WIP `9.0.26244.12303` on 2026-09-07:

| Actual GPU capture | Baseline native/GPU coverage | Corrected native/GPU coverage |
| --- | --- | --- |
| Parallel 960 × 640 | 100% / 100% | Not separately rerun |
| Parallel 1200 × 1200 | 5.33% / 5.49% | 100% / 100% |
| Perspective 1800 × 600 | 0% / 0% | 100% / 100% |

Both custom-resolution regressions drew through the actual shared-texture D3D11 renderer. Independent projection-only comparisons also matched native lines at 100% with the corrected matrix. Both trail shaders compiled successfully. These runs were untiled; portrait, scaled same-aspect capture, and tiled high-resolution capture have not been verified. Projection-only mode returns unavailable for tiled captures or an unavailable tiling API, since projecting only the last tile across the whole image would give a misleading comparison.

## Particle and voxel capture fixtures

Use `--preview particles`, `particle-cpu`, `density-plane`, or `density-volume` to exercise the other V4 D3D11 previews. These fixtures also require one capture per process:

```powershell
dotnet tools/Nuclei.TrailCaptureProbe/bin/Release/net8.0-windows/Nuclei.TrailCaptureProbe.dll --preview particles --native-reference --case parallel-1200x1200 --output .codex-temp/particle-capture-square
dotnet tools/Nuclei.TrailCaptureProbe/bin/Release/net8.0-windows/Nuclei.TrailCaptureProbe.dll --preview particles --gpu-only --case parallel-1200x1200 --output .codex-temp/particle-capture-square
```

Particle fixtures draw 27 magenta particles and compare them with Rhino's native circular points. GPU particles use a shared texture; `particle-cpu` uses the renderer's point-cloud vertex-buffer path. The primary check compares the complete native/GPU masks in both directions. Additional checks require native/GPU component centers within 1.5 pixels and round GPU particles 10–12 pixels across for the requested 12-pixel diameter (allowing antialiased edge pixels). A pair can overlap in some views, so the saved native capture projection identifies isolated samples for these extra centroid/diameter checks; at least 20 isolated particles must pass. Placement is measured against native raster centroids. Keep the native JSON beside its PNG when copying particle references.

Voxel fixtures use a constant scalar texture and custom magenta color. `density-plane` compares the planar texture with a native filled rectangle. `density-volume` compares a 32×32×32 atlas volume with a native solid-box silhouette. Volume fixtures use renderer version 2. These uniform fixtures check framing and silhouette; they do not establish preservation of internal density gradients or material appearance.

Reports record Rhino capture/printing flags, the actual D3D raster viewport, viewport object dimensions, pipeline projection and tiling state. All reference and GPU PNGs and JSON reports include the preview kind and case in their filenames. Fancy GPU captures and reports additionally include `-fancy`; they share the same native reference with the plain volume fixture. Use separate output directories (copy the appropriate native reference first) to preserve baseline and corrected results. `--compare-images` recalculates metrics from saved native/GPU PNGs without starting Rhino; include `--fancy` when comparing Fancy captures. The voxel mask uses a lower chroma threshold than particles to retain dim faces in Rhino's shaded reference. `--compile-shaders --preview particles` compiles both particle vertex paths and their pixel shader without starting Rhino.

Additional baseline checks on Rhino 9 WIP `9.0.26244.12303`, 2026-09-07:

| Actual baseline GPU preview | Capture | Native/GPU coverage |
| --- | --- | --- |
| Particles | Parallel 1200 × 1200 | 0% / 0%; particles stretched to 14–15 × 22–23 pixels |
| Planar voxel density | Perspective 1800 × 600 | 100% / 100% |
| Plain voxel volume | Parallel 1200 × 1200 | 100% / 100% |
| Plain voxel volume | Perspective 1800 × 600 | 100% / 100% |

These checks show that the standard voxel projection already tracks custom capture resolution. The tested captures were untiled.

The particle correction passed these actual-renderer checks:

| Corrected particle path | Capture | Native/GPU coverage | Isolated round particles | Maximum center error |
| --- | --- | --- | --- | --- |
| Shared GPU texture | Parallel 1200 × 1200 | 100% / 100% | 27 | 0.99 px |
| Shared GPU texture | Perspective 1800 × 600 | 100% / 100% | 25 | 0.82 px |
| CPU point-cloud buffer | Parallel 1200 × 1200 | 100% / 100% | 27 | 0.99 px |
| CPU point-cloud buffer | Parallel 958 × 638 | 100% / 100% | 25 | 0.82 px |

The CPU baseline also failed the square custom-resolution case at 0% coverage in both directions. At the actual interactive viewport dimensions, 958 × 638, the corrected CPU capture PNG is byte-for-byte identical to its baseline (SHA256 `FD75BA432DE2CEBDA0BB7687954F7B2B8E7EA2109986EC841AC914445859FA83`). These checks used `ScaleScreenItems=false` and untiled captures. They do not cover printed output or tiled high-resolution captures.
