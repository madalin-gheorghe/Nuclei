# Nuclei Performance History

This folder keeps the performance story in small source-controlled summaries rather than raw profiler captures.

The raw Visual Studio `.diagsession` files are intentionally excluded from Git. In the local benchmark folder they occupy about 2.77 GB across 45 captures, which would make the repository heavy and hard to review. Keep those files as external evidence, or attach selected captures to GitHub Releases if a full profiler trace needs to be shared.

## Files

- [solver-frame-comparison.md](solver-frame-comparison.md) is the quick-read comparison of CPU and GPU median ms/frame.
- [solver-frame-comparison.csv](solver-frame-comparison.csv) is the source CSV for that comparison.
- [diagnostic-index.csv](diagnostic-index.csv) lists every local `.diagsession` capture used during the optimization work.
- [cpu-trace-samples.csv](cpu-trace-samples.csv) records decoded Visual Studio CPU Usage hot-frame sample counts from selected reports.
- [benchmark-summary.csv](benchmark-summary.csv) records representative millisecond timings from the later `NucleiTiming.csv` schema.

## Evidence Sources

Local raw diagnostics:

- Folder: `C:\Nuclei\BenchmarkSuite1`
- Visual Studio captures: `45`
- Total `.diagsession` size: about `2.77 GB`
- Runtime timing CSV: `NucleiTiming.csv`, `32286` rows

The runtime CSV schema evolved while instrumentation was being added. Early rows are useful as historical instrumentation evidence, but the stable millisecond comparisons in this repository use later-schema rows from June 24, 2026.

Important unit note: `TimingReporter` stores `total_ms` as milliseconds per sample/frame. It divides the accumulated stopwatch ticks by `samples` before writing the row, so the comparison tables do not divide by `samples` again.

Component row counts in the local timing CSV:

| Component | Rows |
| --- | ---: |
| `preview_gpu_field` | 11611 |
| `solver_gpu` | 9603 |
| `preview_particle` | 9541 |
| `solver` | 880 |
| `preview_solver_gpu` | 354 |
| `run_start` | 297 |

## Reconstructed Milestones

- `v3.0` - self-coded baseline hand-coded by Madalin Gheorghe; the initial stable Grasshopper/Rhino plugin before AI-assisted optimization work.
- `v3.1` - CPU solver stabilization, wrap/no-wrap behavior fixes, and first structured benchmark workflow.
- `v3.2` - CPU preview and diffusion optimization, separating solver and preview costs and clarifying the limits of CPU-side speedups.
- `v4.0` - first meaningful GPU solver prototype using compute shader based execution and GPU-resident preview work.
- `v4.1` - collaboration checkpoint for speed and main-functionality testing, with fast voxel data work, GPU solver progress, and internal voxel field particle generation.

## CPU Trace Evidence

The CPU trace CSV is based on decoded Visual Studio CPU Usage reports. These are profiler sample counts, not milliseconds. They are still useful because they show where focused time was being spent inside Rhino/Grasshopper during each capture.

Selected observations:

- `Report20260606-1627` was kept as a strong CPU baseline because focused sample pressure was lower than the nearby `1548` and `1611` experiments.
- `Report20260606-1637` captured preview-side work after preview changes; it was useful for behavior comparison, but not selected as the fastest baseline.
- `Report20260606-1655` showed that after preview pressure dropped, movement, sensing, boundary handling, and Grasshopper collection became dominant.
- `Report20260606-1724`, `1758`, and `1823` track the more ambitious sensing/movement optimization pass. The focused sample total moved from about `2.08M` to `1.61M` across those decoded reports, with preview pressure also much lower in `1823`.

Representative focused-frame samples:

| Report | Focused samples | Preview samples | Sense samples | Move samples | Diffusion samples | Grasshopper collection samples |
| --- | ---: | ---: | ---: | ---: | ---: | ---: |
| `1548` | 1734805 | 80836 | 76412 | 57149 | 126461 | 458116 |
| `1611` | 2232492 | 111961 | 102711 | 77738 | 177837 | 565054 |
| `1627-read` | 1390906 | 69671 | 59052 | 44968 | 100036 | 354938 |
| `1637-read` | 2320979 | 108621 | 209478 | 156472 | 359878 | 628432 |
| `1655-read` | 2294572 | 62109 | 204276 | 264463 | 235130 | 711306 |
| `1724-read` | 2078776 | 67163 | 179934 | 140836 | 318740 | 557290 |
| `1758-read` | 1937299 | 83198 | 160713 | 126843 | 276456 | 520924 |
| `1823-read` | 1613329 | 36531 | 151454 | 111726 | 289846 | 410980 |

## Representative GPU Timings

The later GPU timing rows are actual component-level milliseconds from `NucleiTiming.csv`.

| Run | Component | Workload | Median | Average | Notes |
| --- | --- | --- | ---: | ---: | --- |
| `20260624-145351` | `solver_gpu` | 625k particles, 2500x2500x1 field | 31.934 ms | 34.642 ms | 2D GPU solver run |
| `20260624-162403` | `solver_gpu` | 1M particles, 100x100x100 field | 23.894 ms | 24.300 ms | clean 3D stress run |
| `20260624-164721` | `solver_gpu` | 300k particles, 100x100x100 field | 9.148 ms | 9.251 ms | stable 3D run |
| `20260624-164623` | `solver_gpu` | 1M particles, 100x100x100 field | 22.958 ms | 30.767 ms | max includes a spike |
| `20260624-165518` | `solver_gpu` | 300k particles, 350x350x350 field | 10.957 ms | 15.474 ms | stable large voxel-field run |
| `20260624-165518` | `preview_particle` | same run | 0.019 ms | 0.021 ms | GPU-resident preview component overhead |
| `20260624-165518` | `preview_gpu_field` | same run | 0.054 ms | 6.901 ms | max includes first draw spike |

## Interpretation Caveats

- CPU Usage sample counts and runtime milliseconds should not be mixed as the same unit.
- Rhino/Grasshopper state, preview state, wrap/no-wrap settings, voxel resolution, and particle count all affect the captures.
- Some large `max_total_ms` values are spikes from startup, reset, first draw, or GPU synchronization. Median values are usually more representative for interactive use.
- Raw diagnostics and runtime CSV files are intentionally ignored by Git; this folder keeps the durable summary data.
