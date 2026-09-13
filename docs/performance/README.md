# Nuclei Performance

Nuclei's biggest speed gains come from processing chemical maps more efficiently and avoiding unnecessary work. The latest comparison found the **GPU faster in all eight tested workloads**. Earlier before/after tests measured roughly **×3** improvement for CPU slime with food, **×1.6–1.8** for CPU ants, and **×1.21–1.22** for the combined GPU slime improvements.

## How to read the results

**×2.00 means twice as much simulation work per second**, or half the time for the same work. Lower times are better. A simulation step is one update of the particles and their environment; **ms/step** means milliseconds per update. A 3D grid such as **128³** contains 128 × 128 × 128 small cells, called voxels.

These timings measure calculations, not the complete Rhino experience. Drawing previews, creating meshes and updating Grasshopper can add time. Each table identifies what was measured.

## CPU versus GPU — latest builds

Run using the latest local V3 CPU and V4 GPU Release builds. These times measure **particle and chemical-map calculations**, excluding previews, trail bookkeeping, Grasshopper updates and setup.

| Simulation | V3 CPU: ms/step | V4 GPU: ms/step | Speedup |
| --- | ---: | ---: | ---: |
| Slime, 500 × 500, 25,000 particles, small network | 17.012 | 1.026 | **×16.58** |
| Slime, 500 × 500, 25,000 particles, large network | 15.652 | 0.977 | **×16.02** |
| Slime, 64³, 1,572 particles, small network | 4.487 | 0.713 | **×6.29** |
| Slime, 64³, 1,572 particles, large network | 5.199 | 1.010 | **×5.15** |
| Slime, 128³, 12,582 particles, small network | 14.044 | 5.467 | **×2.57** |
| Slime, 128³, 12,582 particles, large network | 11.842 | 5.785 | **×2.05** |
| Ants, 500 × 500, 25,000 particles | 17.511 | 1.453 | **×12.05** |
| Ants, 64³, 1,572 particles | 9.164 | 1.120 | **×8.18** |

Small and large slime networks use matching CPU/GPU settings: particle speed and sensor distance are both doubled for the larger network. Each environment has eight food sources.

Results are medians of ten timed batches per backend, from two runs of **450 steps** each; the first 300 steps allow the simulation to develop before timing. CPU calculations use normal parallel processing. GPU times include waiting for the work to finish. **Timings varied substantially between runs, so treat these ratios as indicative.**

## Major breakthroughs

Only improvements above **×1.50** appear here. Each row compares a particular change with its own earlier version; the gains cannot be added or multiplied together.

| Improvement and measured work | Before → after | Speedup |
| --- | --- | ---: |
| Earlier CPU optimization | 713.596 → 271.026 ms/frame | **×2.63** |
| CPU slime with food: particles + chemical map | [Results below](#faster-slime-on-the-cpu) | **×2.77–3.01** |
| CPU ants: chemical maps only | [Results below](#faster-ants-on-the-cpu) | **×2.49–3.03** |
| CPU ants: particles + chemical maps | [Results below](#faster-ants-on-the-cpu) | **×1.62–1.83** |
| First comparable GPU version versus the best CPU run at that time | 271.026 → 34.070 ms/frame | **×7.96** |
| GPU: spreading chemicals and applying decay together | 152.053 → 90.050 ms/step | **×1.69** |
| GPU ants: shared map processing and unnecessary work removed | 68.349 → 22.775 ms/step | **×3.00** |
| GPU ants: faster preparation of 3D preview data, including simulation | 96.61 → 59.81 ms/step | **×1.61** |

The first CPU/GPU milestone used a different historical workload from the matched comparisons above. Its original ms/frame units are preserved. Preparing preview data is not the same as drawing it: the preview result does not claim a matching increase in viewport frame rate.

## Faster slime on the CPU

Slime follows a chemical map that spreads and fades over time. The new implementation updates several cells at once using **SIMD**, a CPU feature for doing the same calculation on multiple values. It also avoids copying information that is already up to date.

With food sources, the measured particle and chemical-map calculations became roughly **3 times faster**. Both smaller and larger networks were tested:

| Simulation with food | Before: ms/step | After: ms/step | Speedup |
| --- | ---: | ---: | ---: |
| 64³, small network | 11.35 | 3.82 | **×2.97** |
| 64³, large network | 11.31 | 3.82 | **×2.96** |
| 128³, small network | 100.49 | 36.02 | **×2.79** |
| 128³, large network | 98.95 | 35.06 | **×2.82** |

Without food sources, the gain was smaller: **×1.14–1.24**. That calculation path was already cheaper, leaving less work to remove. This does not mean adding food makes a simulation run faster.

This optimization currently applies to suitable full, cubic 3D grids with wrapping boundaries, diffusion radius 1 and gradual setting 1, on modern .NET. Wrapping means particles and signals can continue across opposite edges of the environment. Other settings keep the existing calculation method.

## Faster slime and previews on the GPU

The GPU also gained speed from simpler slime movement, more efficient 3D preview preparation and faster chemical-map processing. The combined change improved slime throughput by **×1.21–1.22** in the recorded small- and large-network tests.

| Improvement and simulation | Before: ms/step | After: ms/step | Speedup |
| --- | ---: | ---: | ---: |
| Combined changes, 250³, 90,000 slime particles, smaller network | 31.568 | 26.154 | **×1.21** |
| Combined changes, 250³, 90,000 slime particles, larger network | 31.768 | 26.070 | **×1.22** |
| Movement change alone, 250³, 1 million slime particles | 72.38 | 61.00 | **×1.19** |
| Slime 3D preview preparation, 250³, 90,000 particles | 78.78 | 66.76 | **×1.18** |
| Ant 3D preview preparation, 250³, 1,436 particles | 96.61 | 59.81 | **×1.61** |

These September 12 comparisons measure whole simulation steps, with preview-data preparation included only in the preview rows. Actual viewport drawing is excluded. Each isolated change has its own comparison; do not multiply these speedups together. Only the ant preview result exceeds the ×1.50 threshold for major breakthroughs.

## Faster ants on the CPU

Ants use separate chemical maps to find food and return home. Both maps now benefit from SIMD while keeping their own spreading and fading settings. Edible food itself stays in place until consumed; its scent is what spreads.

| Simulation | Chemical-map speedup | Particles + maps speedup |
| --- | ---: | ---: |
| 64³, small networks | ×2.54–2.56 | **×1.78–1.82** |
| 64³, large networks | ×2.49–2.51 | **×1.64–1.66** |
| 128³, small networks | ×2.77–3.03 | **×1.74–1.83** |
| 128³, large networks | ×2.55 | **×1.62–1.71** |

The complete particle-plus-map gain is smaller because speeding up the maps does not remove the time spent moving particles. The optimization supports suitable full 2D and rectangular 3D grids with wrapping boundaries on modern .NET. It uses additional memory: about **64 MiB for a 128³ grid**.

An earlier, separate CPU update also improved chemical-map processing in closed environments:

| Chemical-map update only | Before: ms/update | After: ms/update | Speedup |
| --- | ---: | ---: | ---: |
| 3D, 100³ cells | 80.286 | 27.942 | **×2.87** |
| 2D, 512 × 512 cells | 19.391 | 7.915 | **×2.45** |

These are different tests and versions, not extra multipliers to apply to the SIMD results.

## Faster ants on the GPU

A large environment can take time to update even when it contains relatively few ants. Processing the two scent maps together, skipping unused slime-map updates and handling deposits without scanning every empty cell reduced that cost.

In the 250³ test with approximately 1,436 ants, the measured solver time changed as follows. Voxel previews were hidden; particle-preview data preparation was included.

| Improvement | Before: ms/step | After: ms/step | Speedup |
| --- | ---: | ---: | ---: |
| More efficient chemical spreading and fading | 96.811 | 67.308 | **×1.44** |
| Shared map processing and unnecessary work removed | 68.349 | 22.775 | **×3.00** |

Each row comes from a separate comparison. The small difference between the first row's ending time and the second row's starting time reflects normal measurement variation.

## Do the simulations still behave correctly?

The CPU SIMD comparisons matched the original chemical maps and particle positions throughout **600 iterations**, across small and large networks: 16 slime pairs and eight ant pairs. Additional checks covered boundaries, food consumption, different spreading and fading settings, and fallback behavior.

The latest CPU/GPU suite completed **32 runs and 14,400 simulation steps**, retaining the full populations and finite chemical fields. It checked for movement, but did not establish identical CPU/GPU behavior.

Those controlled CPU SIMD tests make particle updates reproducible. They do not promise identical trajectories between CPU and GPU or identical networks in every normal run. The production speed gains described here use dense grids, **not octree coarsening**.

## What to expect on your computer

The tests used an **AMD Ryzen 5 7535HS, Radeon 660M integrated GPU and 32 GB DDR5-4800 memory**, running Windows 11 in Balanced power mode.

- Larger chemical maps make diffusion and decay more expensive; more particles increase movement and sensing work.
- Previews, mesh generation and changing the population add work beyond the timings shown here.
- A chemical-map speedup applies only to updating the maps. Moving particles, drawing previews and updating Grasshopper still take time, so the overall speedup is smaller. For example, if maps originally took 60 ms and everything else took 40 ms, making the maps ×3 faster reduces the total from 100 ms to 60 ms—not to 33 ms.

The historical CPU improvement tests use serial particle updates for repeatable comparisons and parallel map updates; the latest CPU/GPU table uses normal CPU parallelism. GPU timings measure completed work rather than just the time taken to submit commands. Detailed methods, raw timings and build hashes are retained locally in `docs/performance/latest-cpu-gpu-20260913-validated`; the reusable runner is in `tools/Nuclei.CpuGpuBenchmark`. Use the [benchmark template](BENCHMARK-TEMPLATE.md) when recording new results.
