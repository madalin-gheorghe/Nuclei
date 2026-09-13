# Volume renderer quality modes

The existing **Voxel Preview** component keeps both renderers and its original five inputs. Choose quality through the right-click **High Resolution (3D)** checkbox:

- **Unchecked (default):** fast renderer at half viewport width and height, reconstructed to the viewport size, with 128 samples per ray.
- **Checked:** original full-resolution renderer, with 256 samples per ray.

The temporary Renderer input and automatic dropdown creation have been removed. Existing five-input definitions reopen directly. Definitions saved with six inputs discard only the retired Renderer parameter/wire during loading, preserving inputs 0–4 and the saved high-resolution checkbox. Old Renderer values do not override the checkbox. Disconnected source value lists are retained because they may be shared or user-customized. No saved definition is rewritten by the update. Planar and CPU previews remain on their existing paths.

## Scope

Ant Food Pheromones, Ant Base Pheromones, Ant Pheromones, and Ants and Slime now use the same gradient-based 3D shading as slime in both quality modes. Gradients follow the selected pheromone channels and remaining-food background; switching Type refreshes the gradient even while paused. Each requested field owns a cached gradient texture so simultaneous previews cannot overwrite each other's lighting; disabling or resizing the preview releases those textures. The existing green/magenta palette, combined-channel colours, thresholds, and single-field custom colours are preserved. Slime's gradient calculation and planar rendering are unchanged. If a gradient texture is unavailable, the existing renderer fallback remains active.

The fast renderer changes screen resolution; the quality checkbox additionally selects the 128/256 per-ray sample budget. Simulation settings, density atlas, occupancy, transfer function and lighting are unchanged. The fast renderer uses a separate cached RGBA16-float render target and premultiplied-alpha filtering/compositing. It does not use temporal history. Thin-strand refinement is intentionally deferred; small detail may soften or disappear.

This follows established reduced-resolution rendering architecture described in [NVIDIA's dynamic-fluid rendering chapter](https://developer.nvidia.com/gpugems/gpugems3/part-v-physics-simulation/chapter-30-real-time-simulation-and-rendering-3d-fluids) and demonstrated by [VTK's volume mapper](https://github.com/Kitware/VTK/blob/master/Rendering/VolumeOpenGL2/vtkOpenGLGPUVolumeRayCastMapper.cxx). The D3D11 integration here is project-specific; it does not import VTK or a CPU volume-loading pipeline.

Targets follow the actual D3D raster viewport, including odd-size captures, and are bounded to four cached viewports per preview source. Mode changes and unregister/device changes release resources. The new path preserves pixel constant-buffer slot 1 including D3D11.1 subranges. Unsupported raster configurations or a new-path failure fall back to the original renderer and leave a diagnostic in the existing density-renderer status log.

## Safety and testing

The 2026-09-11 ant preview correction passed 135 GPU frame checks plus five checks after a solver step, including field isolation and texture cleanup. All density atlases, all 63 planar frames, and all 24 existing slime gradients matched the pre-fix build byte for byte. The component/menu/archive probe passed 710 assertions across nine cases. Synthetic green/magenta trails rendered successfully in Rhino 9 using both the fast 128-sample and high-resolution 256-sample modes. These captures exercise the unchanged display binary; the separate GPU probe validates the new gradient generation. Local reports are under `C:/Nuclei/.codex-temp/ant-volume-preview-*`.

Before implementation, 988 files were copied and SHA256-verified under `C:/Nuclei/.codex-backups/before-new-volume-renderer-20260908`: V4 including build outputs, all definitions, installed Nuclei4 files, and the existing capture tools.

Before the subsequent menu/default change, 963 files covering all V4 source/build outputs, definitions and installed Nuclei4 files were copied and SHA256-verified under `C:/Nuclei/.codex-backups/before-volume-renderer-menu-20260908`.

Build with `SkipGrasshopperInstall=true`. The initial renderer deployment used `C:/Nuclei/tools/Install-NewVolumeRenderer.ps1`. The menu-only update uses `C:/Nuclei/tools/Install-VolumeRendererMenu.ps1`, changing only the GHA while leaving all 13 other installed files unchanged. It verifies backups and the tested build hash, refuses while Rhino is running, and verifies installed hashes.

Both net7 and net48 builds passed. The isolated component/menu/archive probe passed 614 assertions across eight cases against the repository build and again against the installed GHA. Results are in `C:/Nuclei/.codex-temp/volume-renderer-mode-20260908-installed/results.json`. All 54 saved definition assets and 264 backed-up shader files remained byte-identical. These checks verify menu selection and archive compatibility, not a new GPU performance measurement.

The opt-in diagnostic is documented in `C:/Nuclei/tools/Nuclei.TrailCaptureProbe/MixedResolutionREADME.md`. Synthetic-volume timings are not measurements of a user's definition. The actual-file frozen-state benchmark for V4's 15_3D Intro is recorded in `C:/Nuclei/output/benchmarks/15_3D-Intro-old-vs-new-renderer-20260908.md`; both renderers used 128 samples in that comparison. It does not measure the newly configured original-renderer 256-sample mode or whole-simulation FPS.
