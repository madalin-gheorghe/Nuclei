# Solver Frame Comparison

This page is the quick-read version of the performance history.

`TimingReporter` already divides elapsed ticks by the sample count before writing `total_ms`, so the values below are median milliseconds per frame/iteration. The FPS column is just `1000 / median_ms_per_frame`.

## CPU Solver

These rows compare the repeated CPU workload that appears throughout the timing log: about `900000` particles on a `3000 x 3000 x 1` voxel field, with wrap on and diffuse range 1.

| Run | Stage | Samples | Median ms/frame | FPS equivalent | Speedup vs first CPU run |
| --- | --- | ---: | ---: | ---: | ---: |
| `20260606-172546` | First reliable CPU telemetry | 5 | 713.596 | 1.40 | 1.00x |
| `20260606-183315` | After sensing and movement cleanup | 5 | 554.606 | 1.80 | 1.29x |
| `20260619-162210` | After output and timing reductions | 6 | 421.144 | 2.37 | 1.69x |
| `20260619-165127` | Best stable CPU repeated run | 11 | 271.026 | 3.69 | 2.63x |
| `20260622-144628` | Generalized diffusion path | 35 | 307.598 | 3.25 | 2.32x |

Readable takeaway: for the repeated CPU workload, the best stable run in the log is about `2.63x` faster than the first reliable telemetry run. The generalized diffusion version is a little slower than the best narrow case, but it supports larger diffuse ranges, which became more important than keeping only the range-1 special case.

## GPU Solver

The GPU rows are not one single identical workload, because the GPU work moved from a 2D field into 3D stress tests and larger voxel fields. They are still useful as an interactive scale comparison.

| Run | Stage | Workload | Samples | Median ms/frame | FPS equivalent | Speedup vs first GPU run |
| --- | --- | --- | ---: | ---: | ---: | ---: |
| `20260624-145351` | Early 2D GPU solver | 625k particles, 2500x2500x1 | 50 | 31.934 | 31.31 | 1.00x |
| `20260624-162403` | 3D GPU stress run | 1M particles, 100x100x100 | 21 | 23.894 | 41.85 | 1.34x |
| `20260624-164623` | Later 1M-particle 3D GPU run | 1M particles, 100x100x100 | 36 | 22.958 | 43.56 | 1.39x |
| `20260624-164721` | Stable 300k-particle 3D GPU run | 300k particles, 100x100x100 | 28 | 9.148 | 109.31 | 3.49x |
| `20260624-165518` | Large voxel-field 3D GPU run | 300k particles, 350x350x350 | 84 | 10.957 | 91.27 | 2.91x |

Readable takeaway: the GPU solver moved the same kind of simulation from hundreds of milliseconds per CPU frame into roughly `9-24 ms/frame` for the representative GPU runs in the timing log. The clean 1M-particle 3D GPU run is about `22.958 ms/frame`.

## CPU vs GPU Scale

The cleanest CPU and GPU rows are not exactly the same workload, so this should be read as scale rather than a strict scientific benchmark:

| Comparison | Median ms/frame | Relative scale |
| --- | ---: | ---: |
| First reliable CPU repeated run | 713.596 | 1.00x |
| Best stable CPU repeated run | 271.026 | 2.63x faster than first CPU |
| Clean 1M-particle 3D GPU run | 22.958 | 11.81x faster than best CPU row; 31.08x faster than first CPU row |
| Large voxel-field 3D GPU run | 10.957 | 24.74x faster than best CPU row; 65.13x faster than first CPU row |

The GPU rows have different voxel dimensions and use a different architecture, so the ratios are best used as a directional project history: CPU optimization helped a lot, then GPU architecture changed the performance class.

