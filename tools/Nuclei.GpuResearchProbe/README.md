# Nuclei GPU research probe

Standalone diagnostic extension of `Nuclei.AntPerformanceProbe`. It loads the existing GPU backend and GHA by reflection, requires hardware D3D11 and checks retained population. It does not rebuild or install Nuclei.

```powershell
dotnet build tools/Nuclei.GpuResearchProbe/Nuclei.GpuResearchProbe.csproj -c Release
dotnet tools/Nuclei.GpuResearchProbe/bin/Release/net8.0-windows/Nuclei.GpuResearchProbe.dll --intro-profile --species slime60 --preview off --dynamic-scan --warmup 200 --steps 20 --repeats 5 --json profile.json
```

Additional research switches:

- `--dynamic-scan`: run population rules and neighbour publication with thresholds that prevent mutation; radius 3, death enabled, division disabled.
- `--cpu-fields`: raw blocking field readback after each step; excludes Grasshopper output conversion.
- `--sources N`: deterministic source-presence fixture.
- `--trails`: publish a 32-sample shared trail history.
- `--motion-scale S`: jointly multiply slime movement speed and sensor distance (intro defaults 3 and 9). This is separate from preview scale.
- New backend variants: `generic-movement`, `legacy-preview`, `radius1-direct`, `radius1-tiled` isolate individual changes; `before-optimizations` selects original movement, preview atlas and radius-one diffusion paths together. Preview diagnostic logging stays off in both variants. `current` uses production routing. Results preserve the requested variant label in JSON.

Use an ABBA variant sequence (for example `before-optimizations,current,current,before-optimizations`), long warmup, and sequential GPU runs. Inspect medians, ranges and individual pass timings as well as means; this laptop's shared GPU clocks can vary.

Existing options include `--grid`, `--particles`, `--slime-particles`, `--particle-preview`, `--preview off|single|three`, `--warmup`, `--steps`, `--repeats`, `--variants` and `--nuclei-output`. The current backend normalizes preview scale 2 to 1; it does not downsample the preview.

Raw field readbacks and gradient generation requested after `Step` are outside its named GPU pass intervals. Use total elapsed GPU timeline and wall time as well as pass sums; the difference is not automatically GPU execution time for a particular kernel. Shared textures are published without a viewport consumer, and the probe does not measure rendering, meshing or actual population churn.

See the [research report](C:/Nuclei/docs/research/gpu-performance/GPU-PERFORMANCE-RESEARCH.md) and [evidence manifest](C:/Nuclei/docs/research/gpu-performance/evidence/README.md) for the completed profiles, detailed fixture semantics and reproduction commands.
