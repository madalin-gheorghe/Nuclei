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
