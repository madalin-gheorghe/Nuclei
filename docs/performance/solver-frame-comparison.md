# Solver Frame Comparison

This page is the quick-read version of the performance history.

`TimingReporter` already divides elapsed ticks by the sample count before writing `total_ms`, so the values below are median milliseconds per frame/iteration. The FPS column is just `1000 / median_ms_per_frame`.

## CPU Solver

These rows compare the repeated CPU workload that appears throughout the timing log: about `900000` particles on a `3000 x 3000 x 1` voxel field, with wrap on and diffuse range 1.

| Run | Stage | Workload | Samples | Median ms/frame | FPS equivalent | Speedup vs first CPU run |
| --- | --- | --- | ---: | ---: | ---: | ---: |
| `20260606-172546` | First reliable CPU telemetry | 900k particles, 3000x3000x1 field | 5 | 713.596 | 1.40 | 1.00x |
| `20260606-183315` | After sensing and movement cleanup | 900k particles, 3000x3000x1 field | 5 | 554.606 | 1.80 | 1.29x |
| `20260619-162210` | After output and timing reductions | 900k particles, 3000x3000x1 field | 6 | 421.144 | 2.37 | 1.69x |
| `20260619-165127` | Best stable CPU repeated run | 900k particles, 3000x3000x1 field | 11 | 271.026 | 3.69 | 2.63x |
| `20260622-144628` | Generalized diffusion path | 900k particles, 3000x3000x1 field | 35 | 307.598 | 3.25 | 2.32x |

Readable takeaway: for the repeated CPU workload, the best stable run in the log is about `2.63x` faster than the first reliable telemetry run. The generalized diffusion version is a little slower than the best narrow case, but it supports larger diffuse ranges, which became more important than keeping only the range-1 special case.

## GPU Solver

The GPU rows are not one single identical workload, because the GPU work moved from a 2D field into 3D stress tests and larger voxel fields. They are still useful as an interactive scale comparison.

| Run | Stage | Workload | Timing rows | Median ms/frame | FPS equivalent | Speedup vs first GPU run |
| --- | --- | --- | ---: | ---: | ---: | ---: |
| `20260624-145351` | Early 2D GPU solver | 625k particles, 2500x2500x1 | 50 | 31.934 | 31.31 | 1.00x |
| `20260624-162403` | 3D GPU stress run | 1M particles, 100x100x100 | 21 | 23.894 | 41.85 | 1.34x |
| `20260624-164623` | Later 1M-particle 3D GPU run | 1M particles, 100x100x100 | 36 | 22.958 | 43.56 | 1.39x |
| `20260624-164721` | Stable 300k-particle 3D GPU run | 300k particles, 100x100x100 | 28 | 9.148 | 109.31 | 3.49x |
| `20260624-165518` | Large voxel-field 3D GPU run | 300k particles, 350x350x350 | 84 | 10.957 | 91.27 | 2.91x |
| `20260706-115707` | Apples-to-apples GPU run | 900k particles, 3000x3000x1 | 19 | 34.070 | 29.35 | 0.94x |

Readable takeaway: the GPU solver moved the same kind of simulation from hundreds of milliseconds per CPU frame into tens of milliseconds per GPU frame. The best raw GPU timing in the log is `9.148 ms/frame`, while the closest apples-to-apples CPU/GPU workload is `34.070 ms/frame`.

## Overall Progress

This is the high-level story using the comparable `900k` particle, `3000 x 3000 x 1` workload.

| Point in history | Run | Workload | Median ms/frame | FPS equivalent | Speedup vs start | Speedup vs best CPU |
| --- | --- | --- | ---: | ---: | ---: | ---: |
| Starting point | `20260606-172546` | CPU, 900k particles, 3000x3000x1 field | 713.596 | 1.40 | 1.00x | 0.38x |
| Best CPU | `20260619-165127` | CPU, 900k particles, 3000x3000x1 field | 271.026 | 3.69 | 2.63x | 1.00x |
| Apples-to-apples GPU run | `20260706-115707` | GPU, 900k particles, 3000x3000x1 field | 34.070 | 29.35 | 20.94x | 7.95x |

Conclusion: the first reliable CPU timing was `713.596 ms/frame`. The closest apples-to-apples GPU timing is now `34.070 ms/frame`, so the comparable end point is about `20.94x` faster than where the measured optimization history started, and about `7.95x` faster than the best CPU run.

The apples-to-apples GPU row uses the same particle count, voxel grid, wrap setting, diffuse amount, diffuse range, and decay as the CPU baseline. It still includes GPU input/update overhead and GPU field preview was active during the logged segment, so it is a fairer comparison but not necessarily the absolute minimum solver-only GPU time.
