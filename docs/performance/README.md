# Nuclei Performance

This is the canonical summary of Nuclei's major CPU and GPU performance results. Raw benchmark data remains local.


## CPU versus GPU

Tests used matched solver settings and measured completed solver work.

| Workload | V3 CPU | V4 GPU | Speedup |
| --- | ---: | ---: | ---: |
| Standard 2D: 500 × 500 × 1, 25k particles | 4.235 ms/step | 0.683 ms/step | **×6.20** |
| High 2D: 4000 × 4000 × 1, 1M particles | 330.512 ms/step | 63.016 ms/step | **×5.24** |
| High 3D: 300³, 1M particles | 428.268 ms/step | 133.322 ms/step | **×3.21** |

GPU execution was measured using D3D11 hardware timestamps and synchronization fences. Software GPU fallback was rejected. These results measure solver throughput. They exclude startup, Grasshopper scheduling, output conversion, meshing, and Rhino viewport rendering.

## Major breakthroughs

Only improvements exceeding 50% more throughput (greater than 1.5×) are listed here. Each result refers to its stated workload and measured stages.

| Stage / measured scope | Before → after | Speedup |
| --- | ---: | ---: |
| CPU optimization | 713.596 → 271.026 ms/frame | **×2.63** |
| CPU slime SIMD, native food sources; particle + field | [By workload](#v3-cpu-simd-diffusion-and-decay-2026-09-12) | **×2.77–3.01** |
| CPU ant pheromone SIMD; field updates | [By workload](#ant-pheromones) | **×2.49–3.03** |
| CPU ant pheromone SIMD; particle + field | [By workload](#ant-pheromones) | **×1.62–1.83** |
| First comparable GPU workload versus best CPU run | 271.026 → 34.070 ms/frame | **×7.96** |
| Tiled 3D diffusion and fused decay; solver step | 152.053 → 90.050 ms/step | **×1.69** |
| Paired ant fields, species gating and sparse deposits; solver step | 68.349 → 22.775 GPU ms/step | **×3.00** |
| Coalesced 3D preview atlas, ants; simulation + preview publication | 96.61 → 59.81 GPU ms/step | **×1.61** |

Speedup = before time / after time. **×1.56** means 56% more completed work per second. Historical frame timings retain their original unit; preview publication does not measure viewport FPS. Ranges cover different paired workloads and link to their individual results rather than implying one shared before/after time.

Use the [benchmark template](BENCHMARK-TEMPLATE.md) for new results and when standardizing older sections. The rightmost column consistently shows the speedup for the measured scope; percentages and method details stay outside the tables.

## Ant pheromone diffusion (2026-09-11)

The saved `13_Ants Intro_3D.gh` and `15_3D Intro.gh` definitions both use a 250³ grid (15,625,000 voxels), with approximately 1,436 ants versus 90,000 slime particles. Both voxel previews are hidden. The ant workload is dominated by full-grid pheromone processing, rather than particle movement: two pheromone fields use diffusion radius 2, while the slime example uses one field with radius 1. This initial fix retained scalar-field processing; the follow-up below removes that work when no slime is present.

The ant fields now use the same tiled diffusion dispatch as slime for radii 2–16. Fusing decay into the final axis removes two additional full-grid passes per ant step. Radius 0/1 and 17+ retain direct diffusion with fused decay; disabled diffusion retains separate decay. No simulation controls or particle behavior were simplified to improve the timings.

The controlled comparison used the same candidate binary with private validation switches selecting legacy direct/separate and current tiled/fused execution. Each fresh engine received 120 warm-up steps and five 20-step measurement batches, in legacy/current/current/legacy order. All ten samples per variant were retained:

| GPU time per step | Legacy | Optimized | Speedup |
| --- | ---: | ---: | ---: |
| Total median | 96.811 ms | **67.308 ms** | **×1.44** |
| Total mean | 99.259 ms | **67.248 ms** | **×1.48** |
| Total sample range | 93.971–107.373 ms | 66.082–67.843 ms | — |
| Both pheromone fields, median | 68.602 ms | **39.287 ms** | **×1.75** |

Synchronized wall-time means were 99.492 and 67.406 ms. Ant movement remained approximately 0.072 ms per step. Device timing varied, particularly in the legacy samples; the complete sample ranges are shown rather than discarding slow runs. The separate four-variant diagnostic confirmed that both tiling and decay fusion contribute to the improvement. The optimized backend SHA-256 is `50B07A8AF39B2AEA60128AC13B2DD0DE22DAF4AD339EBAD574CC3998AAB42306`.

`tools/Nuclei.AntPerformanceProbe --intro-profile` reproduces the saved grid, particle counts and solver controls with synthetic initial state. Timings use D3D11 hardware timestamps, a synchronization fence, hidden voxel preview, and enabled shared particle-preview generation. They exclude Grasshopper scheduling, Rhino rasterization, trails, CPU output synchronization, and the original definitions' food geometry; they are solver diagnostics, not measured document frame rates. Correctness is checked separately: 24 ant fixtures compare four execution variants bit-for-bit over eight steps, and scalar tiled-diffusion and 135 voxel-preview frame regressions also pass.

## Paired ant fields and unused-work removal (2026-09-11)

The follow-up combines both pheromone fields into one neighbour traversal. Food and home retain independent diffusion strengths and decay rates, with one shared diffusion range. Tile loads share neighbour indexing and validity checks; target updates share limit reads. If only one field diffuses, only that field runs a stencil. If neither diffuses, a paired decay-only pass preserves their distinct boundary rules.

Ant-only solvers now leave authored scalar slime density static. Mixed and slime-only solvers continue updating it. Fixed ant-only populations also use the existing particle-driven deposit resolver when particle capacity is at most `voxelCount / 1024`, avoiding a scan of millions of empty voxels. Larger, mixed, and dynamic populations keep coalesced voxel deposits. At 250³, the threshold is 15,258 slots; the particle path improved both the 1,436-ant intro workload and a 15,000-ant comparison. This conservative threshold is not a universal crossover.

The final comparison uses the same compiled backend and private validation switches to reproduce the previous optimized path. It uses the same synthetic intro fixture and measurement scope above: 250³, 1,436 ants, hidden voxel preview, enabled particle-preview texture generation. Each fresh engine receives 120 warm-up steps and five 20-step batches in previous/current/current/previous order. All ten samples per variant are retained:

| GPU time per step | Previous optimized | Final | Speedup |
| --- | ---: | ---: | ---: |
| Median | 68.349 ms | **22.775 ms** | **×3.00** |
| Mean | 68.891 ms | **22.642 ms** | **×3.04** |
| Sample range | 67.117–72.289 ms | 21.668–23.877 ms | — |

Synchronized wall-time means were 69.060 and 22.720 ms. Mean GPU pass costs were:

| Work | Previous optimized | Final | Speedup |
| --- | ---: | ---: | ---: |
| Food and home pheromones | 40.205 ms | 21.445 ms | **×1.87** |
| Unused scalar updates | 22.802 ms | 0 ms | Removed |
| Deposit collection | 4.369 ms | 0.016 ms | **≈×273** |
| Ant movement | 0.070 ms | 0.070 ms | **×1.00** |

Every final sample used the automatic sparse-ant deposit path, with the forced experimental switch disabled. A separate 15,000-ant diagnostic reduced deposit processing from 4.401 to 0.206 ms and total mean time from 26.803 to 23.840 ms. These are solver-throughput measurements, not Rhino viewport frame rates.

The final backend SHA-256 is `D10D33815611AF31A4DE9399396A918A7BA673657CC9F91E70F030DCBFBD0EE8`. The full-state regression passes 33 fixtures × 10 variants (2,940 steps), including actual mixed species, independent settings, live range/wrap changes, static ant-only scalar values, and an observed automatic sparse-deposit dispatch. All 135 voxel-preview frames also pass. Raw evidence remains local under `.codex-temp/ant-paired/`. The scalar regression also passes with its zero-particle fixtures explicitly advancing density, plus an assertion that the first step changes the field; this prevents species gating from turning stencil parity into a frozen-field comparison.


## V3 ant behavior parity and CPU field updates (2026-09-11)

V3 now follows V4's improved food-scent behavior in **3D and all three planar orientations (XY, XZ, YZ)**. Searching ants prioritize a useful food gradient and reduce exploration forces to 2%; losing the gradient restores exploration. Fixed food emits its remaining quantity after pickup, through the Food Pheromone field's diffusion and decay settings. Sensor readings no longer leak between ants sharing a worker thread. Returning ants retain their homeward behavior.

The CPU field kernel snapshots both pheromones per axis line before writing, removing the previous concurrent read/write race. Both fields reuse the same neighbor traversal while retaining independent rates, with decay fused into the last axis. Slime-density processing is skipped when no slime is present; source projection visits only cached food-source voxels.

**These are CPU field-update timings: food-source projection plus diffusion and decay. They exclude particle sensing/movement, Grasshopper, output conversion and viewport rendering. They are not total simulation timings or V3-versus-V4 speedups.**

| Synthetic ant-only workload | Previous V3 | Updated V3 | Speedup |
| --- | ---: | ---: | ---: |
| 3D: 100 × 100 × 100, 1,000,000 voxels | 80.286 ms | **27.942 ms** | **×2.87** |
| 2D XY: 512 × 512 × 1, 262,144 voxels | 19.391 ms | **7.915 ms** | **×2.45** |

Each shape used four fresh processes in updated/previous/previous/updated order, 12 warm-up steps and 36 measured steps per process. Values above are the median of the two process medians. The two 3D baseline medians were 87.008 and 73.564 ms; updated medians were 27.947 and 27.938 ms. The two 2D baseline medians were 18.339 and 20.444 ms; updated medians were 7.771 and 8.060 ms. All runs were retained. The ratios describe this workload and machine, not a guaranteed frame rate.

Settings: non-wrapped dense grids, ant radius 2, food/base diffusion 0.1/0.1, food/base decay 0.031/0.073, scalar diffusion 0.15 at radius 1, scalar decay 0.01. Scalar settings remain configured so the comparison includes removal of unused scalar work. Initial fields are nonuniform; every 997th voxel holds 10 units of fixed ant food. The updated build additionally emits food scent, which the old build lacked. No particles or previews run in this field benchmark.

Validation covers 320 CPU field fixtures × 6 steps, plus 96 actual V4 hardware field comparisons × 8 steps. It includes 3D/XY/XZ/YZ, independent and disabled rates, radius 0–17, repeated wrapping, sparse holes, obstacles, limits and decay. Maximum V3-double/V4-float field difference was **0.000002291**. Particle checks cover every sensor direction (including up/down), gradient acquisition/loss, food pickup/depletion, mixed-species food sources and nest reset. Eighteen movement fixtures cover every active axis, both boundary modes, searching/returning deposition, and rejection of deposits on same-voxel or occupied moves. Random particle trajectories are not expected to be identical across CPU and GPU.

The packaged modern-runtime assembly passed the same regressions and all 37 component constructor/icon checks. Both modern and .NET Framework variants built successfully and were installed locally into the existing Rhino 8/9 packages, with backups and matching file hashes. This was a local update, not a Yak release.

Reproduction: `tools/Nuclei.V3AntProbe/README.md`. Baseline assembly, raw timings, regression logs, build hashes and the benchmark summary remain local under `.codex-temp/v3-ants/`. These smaller field-only workloads must not be combined with the earlier 250³ GPU ant results or historical full-solver CPU measurements.

## V3 CPU SIMD diffusion and decay (2026-09-12)

The CPU research produced an exact dense SIMD implementation for slime and a subsequent SIMD implementation for ant food/base pheromones. Contiguous arrays, batched parallel rows and final-pass decay fusion reduce field-update costs. Native slime food deposits already update the shared density array, allowing a redundant full-field synchronization copy to be skipped when that binding is valid. Native food-aware sensing is preserved.

These are **CPU stage measurements**, not Grasshopper FPS or updated CPU/GPU comparisons. Coupled tests use serial native particle operations for reproducible occupancy/deposition and normal parallel field updates. Rendering, output conversion and validation are outside the measured stages.

### Slime

Final production comparisons cover 64³ and 128³, small and large network scales, seeds 17/89, with and without native food sources. All **16 paired 600-iteration runs** match exactly in every field and particle-position comparison. The food table averages both seeds over iterations 301–600:

| Food-aware workload | Before: particle + field | After: particle + field | Speedup |
| --- | ---: | ---: | ---: |
| 64³, small network | 11.35 ms | **3.82 ms** | **×2.97** |
| 64³, large network | 11.31 ms | **3.82 ms** | **×2.96** |
| 128³, small network | 100.49 ms | **36.02 ms** | **×2.79** |
| 128³, large network | 98.95 ms | **35.06 ms** | **×2.82** |

Across individual food-aware pairs, particle-plus-field throughput improves **2.77–3.01×**. Without food sources it improves **1.14–1.24×**. The previous food-free scalar path was already substantially cheaper; the food-aware path removes more original work. The larger food-aware ratio is not evidence that adding food makes a simulation faster. Original 128³ timings vary between processes, so the paired results and their full spread are retained.

The specialization applies on modern .NET with hardware SIMD to complete, periodic cubic 3D grids of at least 16 cells per side, disabled density limits, radius 1, gradual 1, diffusion in (0,1] and finite nonnegative decay. Other configurations and net48 retain the existing algorithm. Numerical checks cover 93 production field configurations plus 21 experimental configurations; the exact comparisons have zero discrepancy. Adaptive/octree policies remain experimental and did not establish an advantage over optimized dense execution at acceptable behavioral agreement. No 0.05 coarsening threshold is used here.

### Ant pheromones

Both food and base pheromone maps use reusable dense buffers while retaining independent diffusion and decay. Edible ant food is consumed and emits scent; it has no diffusion pass. Coupled timings include field gathering/scattering, exclude first-use allocation, and omit the first 20 of 600 iterations:

| Coupled workload | Field speedup | Particle + field speedup |
| --- | ---: | ---: |
| 64³, small networks, seeds 17/89 | **×2.54–2.56** | **×1.78–1.82** |
| 64³, large networks, seeds 17/89 | **×2.49–2.51** | **×1.64–1.66** |
| 128³, small networks, seeds 17/89 | **×2.77–3.03** | **×1.74–1.83** |
| 128³, large networks, seeds 17/89 | **×2.55** | **×1.62–1.71** |

Standalone field-only comparisons also cover 2D: 512 × 512 × 1, radius 1, falls from **10.376 to 4.876 ms (2.13×)**; 128³, radius 1, falls from **114.697 to 43.785 ms (2.62×)**. These standalone fixtures differ from the coupled workloads above and the earlier September 11 CPU field benchmarks; their speedups must not be multiplied together.

All eight coupled ant cases match every step, including both pheromones, remaining food, particle state and food pickup/return counts. Validation also includes 480 paired field configurations, eight mixed/limits/reuse cases, 48 independent-reference production-GHA cases and existing native regressions. The measured SIMD path requires modern .NET, complete periodic grids, no custom density limits and at least 4096 voxels; rectangular 3D and 2D are supported. Other configurations retain the existing implementation. Four persistent double arrays add 32 bytes per voxel (64 MiB at 128³).

The CPU update, including the earlier slime SIMD work, was installed and hash-verified in Rhino 8/9 on September 12 with full pre-install backups. The September 13 shared ant Falloff change below has separate correctness coverage; these SIMD benchmarks were not rerun for that behavior/control change.

Local sources and reproduction notes (not published): `tools/Nuclei.CpuOctreeProbe/RESEARCH-EXPERIMENT-RESULTS.md`, `RESEARCH-PRODUCTION-TABLES.md`, and `ANT-SIMD-RESULTS.md`.

## Additional costs

Persistent counts eliminated a full 27-million-voxel clear and particle recount, saving approximately 5.582 ms/step. A particle-based deposit alternative was correct but slower, so the coalesced voxel implementation remained in production.

- High-load particle-preview generation added approximately 1–3%.
- Density-preview buffer generation added approximately 29% on large 3D grids.
- In an earlier benchmark, a full random-population pass every step added approximately 89%.
- Optimizing unnecessary neighbour-count rebuilding reduced the random-population path from 27.8 to 12.076 ms/step.

Viewport drawing is not included in these figures.

The current particle initializer uses a deterministic pseudo-random permutation without replacement. That improves distribution and avoids duplicate initial voxels; it is separate from the historical dynamic-population cost above.

## Important limitations

- Grasshopper measures GPU command submission, not completed GPU execution. Submission can appear instantaneous while the GPU continues working.
- V3 CPU and V4 GPU implement equivalent behavior using different execution strategies; their particle trajectories are not bit-identical.
- Results from different workloads or benchmark generations must not be combined.
- The historical optimized GPU result of 83.632 ms/step cannot be combined with the older high-3D CPU result because particle generation changed between tests.
- A new matched CPU/GPU run is required for an updated official high-3D speedup.

## Test system

Results were recorded on:

- AMD Ryzen 5 7535HS
- AMD Radeon 660M integrated GPU
- 32 GiB DDR5-4800
- Windows 11 Pro
- Balanced power plan

These ratios describe this machine and are not universal CPU-versus-GPU expectations.

The local evidence archive contains 45 Visual Studio profiler captures, hardware-timestamp samples, and raw A/B logs. Only authoritative summaries belong in this document.

## Shared ant Falloff (2026-09-13)

V3 CPU and V4 GPU now share one Falloff control for food and base pheromones, with independent diffusion and decay rates. This applies in 3D and all three 2D planes. Falloff 0 preserves the previous diffusion behavior. Higher values use the same mixing, weighting and final-axis retention mechanics as slime.

The paired neighbor traversal, CPU SIMD path, GPU tiled passes and fused decay remain in use. Ant-only simulations still skip slime-density updates. This is a behavior/control change; no new throughput improvement is claimed.

Validation covered 960 independent-reference field fixtures, 144 production SIMD fixtures and 288 V3/V4 hardware GPU comparisons, with Falloff 0, 0.4 and 1. Each CPU fixture ran six axis-order steps; each GPU comparison ran eight steps. The maximum CPU/GPU field difference was 0.000002291 (tolerance 0.00002). These correctness checks do not measure complete simulation or viewport speed. Local evidence: `.codex-temp/ant-schema/cpu-tests.json`, `gpu-parity.json`, and `packaged-cpu-tests.json`.
