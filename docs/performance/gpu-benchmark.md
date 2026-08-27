# GPU Solver Benchmark

A headless benchmark for the V4 Direct3D 11 solver. It drives the real engine with
no Rhino or Grasshopper host, which is possible because `Nuclei4.Gpu.D3D11` depends
only on Core, the abstractions, and D3D11.

```bash
dotnet run --project tools/Nuclei.ArchitectureProbe -c Release -- Nuclei-v4/Nuclei4/bin/Release/net7.0-windows --benchmark
```

## Options

| Option | Default | Meaning |
| --- | --- | --- |
| `--grid N` | 64 | Cubic grid resolution; 64 gives the 262,144 voxels used by the recorded baseline |
| `--particles N` | 262144 | Particle count |
| `--steps N` | 120 | Timed steps per repeat |
| `--repeats N` | 5 | Repeats; the reported figure is the median of their medians |
| `--warmup N` | 20 | Steps discarded before timing |
| `--gradual X` | 1.0 | Diffusion gradual control |
| `--food` | off | Populate the slime food source map |
| `--ant-food` | off | Populate the ant-consumable food map |
| `--random-population` | off | Enable random division and death |
| `--random-death X` / `--random-division X` | 0.002 | Per-particle probabilities |
| `--frequency N` | 1 | Apply random population every N iterations |
| `--min-population N` / `--max-population N` | derived | Population floor and ceiling |
| `--trace-population N` | off | Print the population over N iterations instead of timing |
| `--density-preview` | off | Build the shared density preview each step |
| `--preview-scale N` | 1 | Density preview scale |

## Reading the output

**Compare `WALL ms/step`.** D3D11 dispatches are asynchronous, so the per-stage
stopwatches inside the engine measure only CPU submission — they will happily report
microseconds while the GPU is saturated. Wall time is measured across a batch and
closed with a real GPU sync, so it includes execution. The CPU submission figures are
still printed, because a change in *those* points at host-side overhead rather than
shader cost.

The harness prints the device. **If it reports `WARP SOFTWARE FALLBACK`, the timings
say nothing about your GPU** and must not be compared against hardware numbers.

Each run prints a repeat spread. Treat a result as usable only when that spread is
well below the effect you are trying to measure. Spread on this machine sits around
10%, which is enough to catch a large regression but *not* enough to police the 5%
preservation gate on its own; raise `--steps` and `--repeats` when a fine margin
matters.

## Recorded results

128³ (2,097,152 voxels), 262,144 particles, 80 steps, 5 repeats, 60 warmup:

| Configuration | Wall ms/step | vs baseline |
| --- | --- | --- |
| baseline | 6.385 | — |
| `--food` | 6.433 | +0.7% |
| `--random-population` | 12.076 | +89% |

The food-source projection costs well under the gate: it is one extra dispatch with
an early-out on empty voxels.

Random population at `Frequency = 1` roughly doubles step cost, because the death and
division kernels then dispatch every iteration over the full particle capacity and
contend on the atomic population counters. This is inherent to the feature; raising
the component's `Frequency` input reduces it proportionally.

An earlier form of that path cost **27.8 ms/step**, because the dispatch rebuilt
neighbour counts — a seed pass plus one pass per axis over the whole grid — twice per
iteration for a random path that ignores neighbour counts entirely. The build is now
skipped unless the neighbour rule itself is due. This benchmark existed specifically
to find that class of mistake, and found it on its first run.

## Density preview cost

| Grid | Solver only | With `--density-preview` |
| --- | --- | --- |
| 200³ (8.0M voxels) | 16.5 ms/step | 21.3 ms/step (+29%) |
| 300³ (27.0M voxels) | 52.8 ms/step | 68.3 ms/step (+29%) |

The solver-only figures are tight (about 1% spread); the preview figures are noisy
(30%+), so treat the +29% as an order of magnitude rather than a precise number.

**This measures only the solver-side preview build.** The other half — raymarching
the atlas in `DensityPreviewPS` — runs in the Rhino display pipeline and cannot be
benchmarked headlessly. That half is the likely dominant cost when the viewport is
heavy while the solver is paused, because a paused solver rebuilds nothing but the
viewport still raymarches every redraw. Step count there is derived from
`travel / voxelSize` and clamped by a budget that saturates at 256 on any large grid,
so every ray runs the maximum 256 samples at 300³. The renderer already skips empty
space with 4-voxel occupancy blocks, terminates rays early above 0.985 alpha, samples
trilinearly rather than tricubically, and casts one shadow ray per pixel rather than
per sample, so the sample budget is the remaining lever. The preview now samples 128 rays per
pixel by default, with a High Resolution right-click toggle on Voxel Preview raising
it to 256.

## Population tracing

`--trace-population` reports the live particle count per iteration, which is how the
random-population semantics were validated against V3: at 1000 particles the observed
deaths per step are about 1, 10 and 50 for probabilities 0.001, 0.01 and 0.05, the
floor holds at `--min-population`, and division grows at the configured rate.
