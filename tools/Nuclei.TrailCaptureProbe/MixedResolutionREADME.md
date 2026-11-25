# Mixed-resolution volume-renderer check

This opt-in extension starts its own hidden Rhino process and uses an unsaved synthetic scene. It does not open, save, or modify any user Grasshopper definition, and it does not close another Rhino process.

The fixture is a deterministic 96 x 96 x 96 filament density atlas with a diagnostic CPU-generated gradient texture. Rendering calls the actual built `GpuDensityFieldD3DRenderer.TryDraw` on Rhino's D3D11 device and immediate context. It is not a solver benchmark or proof of appearance on a real definition.

```powershell
dotnet build tools/Nuclei.TrailCaptureProbe/Nuclei.TrailCaptureProbe.csproj -c Release -m:1 -p:BuildInParallel=false -nr:false

dotnet tools/Nuclei.TrailCaptureProbe/bin/Release/net8.0-windows/Nuclei.TrailCaptureProbe.dll --mixed-resolution --preview density-volume --renderer-mode baseline --case parallel-1201x801 --label baseline --output .codex-temp/mixed-resolution-check

dotnet tools/Nuclei.TrailCaptureProbe/bin/Release/net8.0-windows/Nuclei.TrailCaptureProbe.dll --mixed-resolution --preview density-volume --renderer-mode new --case parallel-1201x801 --label new --reference .codex-temp/mixed-resolution-check/volume-parallel-1201x801-baseline.png --output .codex-temp/mixed-resolution-check
```

Use `--renderer <dll>` to test a staged build or a preserved baseline. The default is `Nuclei-v4/Nuclei4.Display.D3D11/bin/Release/net7.0-windows/Nuclei4.Display.D3D11.dll`. Baseline mode also works with the old frame assembly that has no `UseNewRenderer` field. New or toggle mode requires that field and refuses to silently fall back to an old assembly.

Available projection prefixes are `parallel` and `perspective`; the size suffix may be `native` or `WIDTHxHEIGHT`. By default all warmup, measured and final image draws run inside one real Rhino capture callback at the requested raster size. Before every draw, a GPU copy restores the original background outside the timed interval, so translucent copies do not accumulate. JSON records the actual raster dimensions for every draw. These are controlled repeated GPU draws, not interactive frame times. Run one capture per process because this Rhino WIP can stall on a second capture. The probe flushes evidence then terminates only itself.

Options:

- `--volume-fixture ant-pheromones`: synthetic green food trails and magenta base trails; defaults to combined field 10. Use `--value-index 8`, `9`, or `11` for individual or mixed previews. Gradients follow the selected channels; these CPU diagnostic gradients test display behavior separately from the GPU atlas checks in `tools/Nuclei.AntVolumePreviewProbe`.
- `--warmup-draws 8 --timing-draws 24`: request more controlled renderer calls; defaults are 4 and 12.
- `--interactive-timing`: request ordinary hidden-view redraws instead of a capture batch. This installed WIP can issue no callbacks for those requests; the probe then fails timing validation rather than fabricating samples.
- `--static-version`: do not refresh the frame version. By default the same density data gets a new version each draw so the occupancy update cost is included; this is not genuinely evolving simulation data.
- `--samples 128` or `256`: keep the same along-ray sample budget in both modes.
- `--reference <png>`: report image differences without requiring pixel equality. The half-resolution path is intentionally approximate.
- `--expect-identical`: require exact pixel equality against a reference. Use only for checking the unchanged original renderer against its preserved build.
- `--renderer-mode toggle`: alternate old/new mode each draw to check switching and resource cleanup. This is a lifecycle regression check, not a representative performance comparison.
- `--label <unique-name>`: preserve repeat runs in the same output directory. The probe refuses to overwrite existing PNG or JSON evidence.

Hardware timestamp queries surround the complete `TryDraw` call on the actual Rhino draw context. They include its occupancy work and, for the new renderer, its reduced-resolution pass and reconstruction. They exclude the solver, density/gradient atlas generation, presentation, CPU submission time, background restoration, and image capture/readback. Disjoint or invalid GPU-clock samples fail validation. Warmup and final image draws are kept in the report but excluded from the measured median. The report includes the GPU adapter, renderer, shader-source and original compiled pixel-shader hashes, all raw timings, and a state-restoration check for viewports, render targets, depth target, pixel constant buffers 0/1, pixel resources 0-7, samplers 0/1, blend/rasterizer objects, and vertex/pixel shaders. A deliberate b1 subrange (first constant 16, count 16) checks both buffer identity and slice restoration; the host's original b1 binding is restored afterward. New mode also verifies that the reduced-target helper was actually used, so a silent fallback to the old renderer cannot pass.

The state check records native binding identities, not the contents of buffers or textures. It does not prove every possible D3D state or all tiled/multiview captures. A successful synthetic result must not be described as a measured speedup for a user's Grasshopper file.
`--volume-fixture ant-pheromones --food-shapes` renders a thin food slab, an L-shaped patch and a small low-strength block. Compare `--value-index 13` with `11` using `--expect-identical` to check shared food rendering. `--food-overlap` adds stronger pheromones at the same food voxels. Food fixtures validate visible neutral pixels rather than requiring the chromatic colours used by trail fixtures.
