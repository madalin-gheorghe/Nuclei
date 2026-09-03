# Nuclei Performance

This is the canonical summary of Nuclei's major CPU and GPU performance results.
Raw benchmark data remains local.

## CPU versus GPU

Tests used matched solver settings and measured completed solver work.

| Workload | V3 CPU | V4 GPU | Speedup |
| --- | ---: | ---: | ---: |
| Standard 2D: 500 × 500 × 1, 25k particles | 4.235 ms/step | 0.683 ms/step | **6.20×** |
| High 2D: 4000 × 4000 × 1, 1M particles | 330.512 ms/step | 63.016 ms/step | **5.25×** |
| High 3D: 300³, 1M particles | 428.268 ms/step | 133.322 ms/step | **3.21×** |

GPU execution was measured using D3D11 hardware timestamps and synchronization
fences. Software GPU fallback was rejected.

These results measure solver throughput. They exclude startup, Grasshopper
scheduling, output conversion, meshing, and Rhino viewport rendering.

## Major breakthroughs

| Stage | Result |
| --- | --- |
| CPU optimization | 713.596 → 271.026 ms/frame: **2.63× faster** |
| First comparable GPU workload | 34.070 ms/frame: **7.95× faster than the best CPU run** |
| V4 architecture separation | **+0.91%**, within the 5% regression limit |
| Tiled 3D diffusion and fused decay | 152.053 → 90.050 ms/step: **40.78% faster** |
| Persistent particle counts | **4.72% controlled improvement** |
| Final GPU binary comparison | 90.109 → 83.632 ms/step: **7.19% faster** |

Persistent counts eliminated a full 27-million-voxel clear and particle recount,
saving approximately 5.582 ms/step. A particle-based deposit alternative was
correct but slower, so the coalesced voxel implementation remained in production.

## Additional costs

- High-load particle-preview generation added approximately 1–3%.
- Density-preview buffer generation added approximately 29% on large 3D grids.
- In an earlier benchmark, a full random-population pass every step added
  approximately 89%.
- Optimizing unnecessary neighbour-count rebuilding reduced the random-population
  path from 27.8 to 12.076 ms/step.

Viewport drawing is not included in these figures.

The current particle initializer uses a deterministic pseudo-random permutation
without replacement. That improves distribution and avoids duplicate initial
voxels; it is separate from the historical dynamic-population cost above.

## Important limitations

- Grasshopper measures GPU command submission, not completed GPU execution.
  Submission can appear instantaneous while the GPU continues working.
- V3 CPU and V4 GPU implement equivalent behavior using different execution
  strategies; their particle trajectories are not bit-identical.
- Results from different workloads or benchmark generations must not be combined.
- The latest optimized GPU result of 83.632 ms/step cannot be combined with the
  older high-3D CPU result because particle generation changed between tests.
- A new matched CPU/GPU run is required for an updated official high-3D speedup.

## Test system

Results were recorded on:

- AMD Ryzen 5 7535HS
- AMD Radeon 660M integrated GPU
- 32 GiB DDR5-4800
- Windows 11 Pro
- Balanced power plan

These ratios describe this machine and are not universal CPU-versus-GPU
expectations.

The local evidence archive contains 45 Visual Studio profiler captures,
hardware-timestamp samples, and raw A/B logs. Only authoritative summaries belong
in this document.
