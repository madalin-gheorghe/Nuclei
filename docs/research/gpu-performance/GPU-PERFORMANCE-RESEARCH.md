# Nuclei GPU Performance: Bottlenecks and Optimization Experiments

Nuclei has several different GPU bottlenecks, depending on particle count, population rules and requested outputs. **Diffusion is the main cost for sparse, static populations. Dynamic population processing overtakes it when neighbour rules are enabled. Movement becomes the main cost at one million slime particles. Full 3D preview publication and blocking CPU outputs can each cost more than the simulation kernels.** The best first change therefore depends on which of these modes matters most.

For simulation throughput, the strongest initial experiment is a cheaper neighbour-count path that queries the existing occupancy grid around particles when the population is sparse. For interactive use, full-resolution preview generation deserves equal priority: its memory access pattern and repeated publication work offer concrete targets. Dense slime requires a separate movement investigation. An octree would not address all three costs.

These conclusions combine fresh measurements of the current D3D11 backend with source inspection and published GPU research. The measurements establish expensive stages; explanations of the hardware causes remain hypotheses until isolated benchmarks or counters confirm them. No production optimization or numerical threshold change is included in this report.

## Measurements and scope

The benchmark machine is an AMD Ryzen 5 7535HS with Radeon 660M integrated graphics, 32 GiB memory configured at 4800 MT/s, Windows 11 Pro build 26200 and AMD driver `32.0.21030.11004`. The Windows Balanced power plan was active. Measurements were recorded on 12 September 2026. Hardware D3D11 execution was required; the benchmark rejected WARP software fallback. The capability object did not populate its adapter-name field, so hardware identity was separately checked through Windows.

The tested backend has SHA-256 `1AFE1EA96D2BA699DB3AFA79137ED8E52DC15DA1E2A755765A4848968A6A0D8F`, matching the DLL in the installed `Grasshopper/Libraries/Nuclei4` directory. The benchmark loads the build-directory assembly explicitly. This is the current GPU implementation, independently of the earlier CPU v3 SIMD work. Source and binary hashes, settings and reproduction commands accompany the report.[^1]

All fresh scenarios are **3D**, using 128³ or 250³ voxels. Initial positions are deterministic and dispersed, with unique interior parent voxels and varied headings. Initial slime density, or both ant pheromone fields, spans 0.100–0.355. Standard motion settings are speed 3, sensor distance 9 and sensor/rotation angles 45 degrees. Slime uses diffusion range 1, diffusion 0.15, gradual weighting 0.5, decay 0.01 and wrapped boundaries; ants use range 2, diffusion 0.1 for both pheromones, decays 0.0001/0.0005 and closed boundaries.

The longer static profiles run 600 warm-up steps before 100 measured steps. Dynamic profiles run 200 warm-up steps before 100 measured steps. Output profiles use shorter warm-ups, documented in the evidence manifest. These are evolved synthetic workloads, **not a validation that a particular network morphology has formed**. No morphology or sensor-error comparison was performed because no alternative simulation algorithm was introduced.

GPU timestamps surround batches and named stages. Wall time includes submission and a completion fence. Table entries are arithmetic means of batch means, with five batches per scenario unless stated otherwise. The original particle-preview baseline has ten batches across two fresh engines. The ranges in the companion [aggregate table](C:/Nuclei/docs/research/gpu-performance/SUMMARY.md) describe variation between batches, not statistical confidence intervals.[^1]

| Scenario | Grid | Particles | GPU timeline, ms/step | Main measured costs, ms/step |
|---|---:|---:|---:|---|
| Static ants, outputs off | 250³ | 1,436 | **19.65** | Fields 19.24, about 98% |
| Static slime, particle preview only | 250³ | 1,436 | **27.29** | Fields 22.46; deposits 2.17 |
| Static slime, particle preview only | 250³ | 90,000 | **32.54** | Fields 22.50; movement 4.53; deposits 2.64 |
| Static slime, outputs off | 250³ | 1,000,000 | **71.36** | Movement 45.40, about 64%; fields 22.54 |
| Dynamic rules, ants | 250³ | 1,436 | **73.06** | Dynamic stage 46.66, about 64%; fields 20.76 |
| Dynamic rules, slime | 250³ | 90,000 | **78.02** | Dynamic stage 46.97, about 60%; fields 22.55 |
| Static ants, outputs off | 128³ | 1,436 | **4.18** | Fields 3.82 |
| Static slime, outputs off | 128³ | 90,000 | **8.77** | Movement 4.20; fields 3.90 |
| Dynamic rules, ants | 128³ | 1,436 | **12.45** | Dynamic stage 8.26 |
| Dynamic rules, slime | 128³ | 90,000 | **16.36** | Dynamic stage 8.67 |

The dynamic fixture enables death checks with radius 3 and thresholds that prevent deaths; division is disabled. Both observable neighbour fields are refreshed by the current implementation. Thus it measures the cost of enabling this processing, including dense neighbour-grid work, rule checks and population bookkeeping, **without actual births or deaths**. The 46–47 ms figure belongs to the whole marked dynamic stage, not an independently measured neighbour-sum kernel. The dense grid passes are the leading explanation from code inspection.[^2]

Output demand changes the picture substantially:

| Output scenario, 250³ | Ants, 1,436 particles | Slime, 90,000 particles |
|---|---:|---:|
| 4,096 source cells; no preview or CPU field output | 22.10 ms wall | 31.09 ms wall |
| Same source setup; blocking raw field readback each step | **65.38 ms wall** | **45.72 ms wall** |
| Full-resolution field preview, particles and 32-sample trail publication; no sources | **96.74 ms wall** | **73.17 ms wall** |

The matching raw-readback runs differ by approximately 43.28 ms/step for ants and 14.63 ms/step for slime. They were run sequentially, rather than randomized paired repetitions, so these are diagnostic differences, not precise causal estimates. The ant fixture deliberately reads scalar density, both pheromone fields and remaining food; it is not representative of every possible connected output.

For the full preview run, the named density-publication interval averages 56.00 ms for ants and 20.49 ms for slime. Another 17.41/16.90 ms of GPU timeline lies outside the named intervals. Gradient generation occurs after `Step` and is one contributor to this residual, alongside queue gaps and other unmarked work. Blocking raw readbacks similarly occur outside `Step`; their cost must not be inferred from the near-zero named `Readback` marker. None of these intervals measures viewport rasterization, volume ray marching, mesh generation or Grasshopper object conversion.

A requested preview scale of 2 produced no resolution experiment: `NormalizeDensityPreviewScale` maps it to 1. That run is retained as a repeat control, with ant/slime GPU timelines of 94.45/69.11 ms. It provides no evidence of a speedup from downsampling. The observed 3D atlas was 4000×4000 pixels.

## Existing implementation and avoidable work

The GPU already uses parallel compute shaders, flat buffers and ping-pong fields. CPU SIMD intrinsics do not translate into an additional automatic GPU speedup. The transferable principle is reducing memory traffic and unnecessary work while giving neighbouring threads useful, regular accesses.

Several optimizations are already present: paired ant-pheromone diffusion, decay fused into the last diffusion axis, tiled diffusion for supported ranges, persistent voxel occupancy, sparse source projection and a sparse deposit resolver for qualifying static ant populations. Recommendations to introduce these generally would duplicate existing work. Earlier performance notes document their development, but their historical measurements are not substituted for the fresh results here.[^2][^3]

| Pipeline stage | Current work | Relevant scaling |
|---|---|---|
| Movement and sensing | Read state, construct sensors, sample fields, steer, claim occupancy, record pending deposits | Particle capacity; locality, branches and contention affect cost |
| Deposit resolution | Dense voxel scan, or sparse ant particle scan | Voxel count or particle capacity |
| Population processing | Recount bookkeeping, dense neighbour grids, publish counts, apply rules | Grid size, rule radii, particle capacity and actual churn |
| Food projection | Update listed source indices | Number of source cells |
| Diffusion and decay | Separable axis passes over active fields | Grid size, radius, boundaries and field count |
| Preview publication | Convert field buffers to shared atlases, build gradients, publish particles/trails | Atlas size, field demand, particle capacity and publication cadence |
| CPU outputs | Copy to staging resources, map, copy into managed arrays, convert outputs | Requested data volume and synchronization frequency |
| Meshing and drawing | Active-cell/triangle generation, readbacks and display rendering | Surface complexity, screen size, renderer and output demand; not measured here |

Named diffusion entries must be summed across axes: the final axis changes with iteration, so names such as `DiffusionX` and `DiffusionX+ScalarDecay` occur in one batch. They do not imply six axis passes every step. Similarly, CPU stopwatches around dispatch submission are not measurements of completed GPU work.

### Dynamic neighbour counting

`DispatchBuildNeighbourCounts` seeds a dense grid and applies three separable box sums. `SumNeighbourAxis` assigns a whole line to one thread, which advances a running window sequentially. This avoids recomputing every neighbourhood from scratch, but still processes the full volume. For the Z pass, adjacent threads own adjacent lines while Z is the contiguous coordinate, creating strided accesses across threads.[^2]

The counts are needed at particle locations. With 1,436 particles and radius 3, a direct 7×7×7 occupancy query requires at most **492,548 voxel samples** before clipping, compared with a 15,625,000-voxel domain and several dense passes. This is a work-count argument, not a predicted runtime ratio. With 90,000 particles the direct query reaches 30.87 million samples, so a dense or tiled method may win. The crossover depends on radius, clustering, cache reuse and which count fields are required.

The first candidate is therefore a **sparse/dense dispatcher**. Reuse the existing voxel occupancy array; do not build an octree or hash table merely to perform these small box queries. A sparse kernel can compute both positive-radius counts in one pass, preserving their separate radii. Dense cases can retain the existing method while a parallel tiled scan or a cooperative sliding-window implementation is evaluated.

Parallel scan literature supplies a sound building block. GPU Gems presents work-efficient scans and their use in summed-area tables. GPUPrefixSums provides explicit reduce, scan and propagation stages in HLSL, useful for studying ownership of intermediate results and synchronization.[^4][^5] Its modern wave operations require an SM5-compatible adaptation for this D3D11 backend. A multi-dispatch scan is a safer starting point than inter-group spin waiting that assumes every group can make progress.

Correctness is more specific than matching a mathematical box sum. Current neighbour ranges clip to domain boundaries, even when particle wrapping is enabled. Counts exclude the particle itself. Both observable fields can update when only one rule is enabled or when a mutation frequency is not due. Zero and negative ranges have compatibility behaviour, including preserved values in some paths. Normal division can trigger a second count publication before later random mutations. These contracts rule out simply computing counts less often.[^2]

### Full-volume preview publication

The preview path exposes a concrete memory-layout mismatch. Field indexing is `x * ResY * ResZ + y * ResZ + z`, so Z is contiguous. In the volume atlas kernel, neighbouring horizontal threads vary X. At 250³, an X increment changes a scalar address by **62,500 floats, or 250,000 bytes**. This is a strong locality concern, though the timestamp alone cannot prove how much of the interval it causes.[^2]

The dynamic preview uses an RGBA32F atlas, including channels for slime, ant food pheromone, ant base pheromone and remaining food. A 4000² atlas occupies 256 MB before considering reads. Its RGBA16F gradient companion occupies another 128 MB, and the gradient kernel samples near and far neighbours. Gradients are already cached by field and source version; the issue is their cost when the source changes every step, not a complete absence of caching.

A directly relevant published example is NVIDIA's matrix-transpose study: reorganizing global accesses through a padded shared-memory tile improved measured bandwidth from 18.8 to 99.5 GB/s on a Tesla M2050 and from 55.3 to 144.3 GB/s on a K20c. Those are historical CUDA results, not predicted Radeon gains.[^6] The transferable experiment is to read contiguous voxel segments cooperatively and transpose through group-shared memory into the atlas layout. Merely swapping read and write strides could move the bottleneck instead of removing it.

Other candidates are publishing once per displayed frame rather than every internal iteration, generating gradients only when a consumer needs them, and avoiding unused channel work with a compatible preview contract. An explicit lower-resolution preview could reduce display work without coarsening simulation fields, but the existing scale-2 setting does not do that. Keep full-resolution extraction available and treat preview quality changes separately from simulation correctness.

There are also CPU-side costs inside the preview path: multiple synchronous `File.AppendAllText` status writes per publication, several `Flush` calls and shared-resource coordination. These can introduce queue gaps inside an elapsed GPU interval. Microsoft documents that `Flush` has overhead and does not itself wait for GPU completion; shared-resource visibility still has to be preserved.[^7] Timestamp atlas generation and gradients separately, and measure CPU logging/flush costs before attributing the entire 56 ms to a shader.

### Movement at high particle counts

At one million slime particles, movement takes 45.40 of 71.36 ms. This pass performs five 3D sensor samples, orientation work, normalization, trigonometry, boundary handling, random decisions, occupancy claims and pending-deposit writes. The current timing cannot distinguish memory latency, arithmetic, register pressure or atomic contention. A particle count increase alone does not identify which instruction class dominates.[^2]

FLAME GPU 2 shows that spatial ordering can improve agent-data locality, and that the best sorting frequency depends on how quickly agents move between spatial bins. Its chaotic 200-step benchmark favoured sorting every step.[^8] The repository's spatial-message implementation builds a histogram, performs an exclusive scan and reorders messages; this is concrete code for the indexing machinery, although it targets CUDA.[^9]

For Nuclei, first test modest shader specialization for stable conditions such as no voxel behaviour/vector maps, wrapped boundaries and fixed group parameters. Slime and ant entry points already exist, so generic species specialization alone is not a new proposal. Hoisting group-constant trigonometry is worth measuring only where maps do not vary it, and only after checking rounding and the sensing contract.

Spatially ordered **work indices** are a second candidate. Keeping particle storage and identity stable reduces the disruption to random seeds, trails and external references. Sorting still costs time and may replace coherent particle-state reads with scattered ones; the net benefit must include both effects. Changing dispatch order can also change winners of atomic occupancy claims even with stable IDs. A locality optimization therefore needs collision-free exact checks and collision-rich behavioural comparisons, not just a faster movement kernel.

The near-neighbour research by Chisholm, Maddock and Richmond reinforces the value of reducing scattered queries: their combined optimizations achieved peak speedups of 1.27× in 2D and 1.34× in 3D searches, but 1.21× in complete FLAMEGPU simulations.[^10] Nuclei's ordinary sensing samples fields; it is not an all-pairs particle neighbour search. Those factors cannot be applied to its movement stage.

### Diffusion and decay

Fields remain the dominant target for sparse static simulations. One float field at 250³ is 62.5 MB. Three axis passes imply at least 375 MB of logical read/write traffic per step, even before accounting for stencil neighbours, metadata and preview work. Two pheromone fields double that minimum. Actual external-memory traffic depends on cache reuse and cannot be recovered from these timings alone.

AN5D studies register streaming, shared-memory reuse and temporal blocking. Its implementation generates register-held planes and double-buffered shared storage. EBISU subsequently demonstrates that larger tiles and deeper reuse can outperform a policy of maximizing occupancy.[^11][^12] These are useful design lessons, but their CUDA execution features and hardware budgets do not map directly onto D3D11/SM5.

For Nuclei, the compatible first experiment is **fusion within one simulation step**, such as retaining intermediate axis results in shared memory while reproducing the current axis order, clamps, boundary handling and final decay. Register use, redundant halo calculations and metadata reads can offset the saved traffic. Benchmark direct, existing tiled and fused variants by radius instead of assuming one kernel is universally best.

Batching several complete diffusion timesteps while particles remain frozen would change the algorithm: particles currently sense and deposit between updates. Likewise, combining separable passes into a different stencil while dropping intermediate clamps is not an equivalent optimization. The CPU SIMD results do not establish either transformation as valid on the GPU.

### Deposits, food and population bookkeeping

Deposits currently cost about 3.42 ms in the million-slime profile, versus 45.40 ms for movement. They are worth improving, but have a much smaller immediate ceiling. Static sparse ants already use a particle-driven resolver when capacity is at most `voxelCount / 1024`. Enabling dynamic population processing disables this choice; the ant deposit stage rises to 4.64 ms in the dynamic 250³ fixture. Re-evaluating that restriction is a secondary opportunity, provided pending deposits remain associated with the correct snapshot through mutations.[^2]

Gao and colleagues found severe particle-to-grid transfer costs in material-point simulations. Their grouped transfer achieved 23× the performance of atomics-only scattering and 15× that of their GVDB gather comparison for P2G-MLS; P2G's share fell from over 90% to around 40%.[^13] Their code combines shared tiles, shuffle reductions and block-level atomic accumulation, with histogram sorting and index mappings.[^14]

Nuclei differs materially: it normally deposits into an owned voxel, rather than scattering weighted contributions to many grid nodes. Its occupancy compare-exchange is also a conflict-resolution rule, not an associative sum. Thus warp-aggregated deposits or a sorted reduction may help only after contention is demonstrated. Preserve fixed-point deposit quantization and the separation between sensing and deposit resolution; writing deposits directly into a field being sensed would expose same-step deposits to other particles.

Projection of 4,096 food sources was below 0.1 ms/step in these cases. It is not a current priority. Capacity-wide population recounts and free-slot management deserve separate timings during genuine birth/death churn; the current no-mutation fixture does not characterize their worst case. Scan/compaction techniques from FLAME GPU are relevant there, but free-slot order and population limits are observable semantics.

### CPU transfers, meshing and rendering

Full field readback uses staging copies followed immediately by blocking `Map` and `Marshal.Copy`. Microsoft describes why this pattern can serialize CPU and GPU execution; an integrated GPU does not automatically eliminate the API synchronization or managed copying.[^15] Nuclei already has asynchronous rings for some particle previews and population counters. The remaining question is which field outputs truly require an up-to-date CPU snapshot every iteration.

A useful experiment is batching requested field copies before mapping, then testing staging rings for consumers that permit delayed snapshots. Demand-driven selection, a final exact snapshot when pausing, and GPU-resident display can avoid unnecessary transfers. If a downstream Grasshopper computation requires the current field each step, a delayed ring cannot be advertised as equivalent; it changes latency semantics and may offer little overlap.

Intel's Galactic Civilizations 3 case study provides an instructive D3D11 example: resolving repeated `Map` stalls reduced frame time from approximately 21 to 16 ms. Its cause was dynamic-buffer renaming during uploads, not Nuclei's staging readback, so its buffer-size fix is not directly applicable. The shared lesson is to inspect CPU/GPU timelines, since kernel timings alone miss synchronization losses.[^16]

The source also contains mesh extraction with active-cell counting, append-count readbacks and triangle readback. Rendering includes volume shaders and shared particle/trail consumers. These paths were inspected but not benchmarked here. Their contribution to an actual Grasshopper definition remains an open measurement. If surface extraction dominates, GPU compaction and indirect drawing are candidates; if volume ray marching dominates, empty-space skipping may be valuable. GVDB's actual `rayCast` implementation traverses a sparse hierarchy and skips empty regions, providing a relevant rendering reference.[^17]

## Published comparisons and applicability

The following figures describe the source workloads. They are evidence that the techniques can matter, **not speedup forecasts for Nuclei**.

| Work | Reported comparison | What transfers | Main limit |
|---|---|---|---|
| Harris, matrix transpose, 2013 | M2050: 18.8 → 99.5 GB/s; K20c: 55.3 → 144.3 GB/s | Cooperative transposition and coalescing for field atlases | CUDA matrices on older NVIDIA GPUs; texture layout differs |
| Chisholm et al., 2020 | Peak 1.27×/1.34× for 2D/3D neighbour search; 1.21× complete simulation | Measure query coverage, locality and indexing overhead | Ordinary Nuclei sensing is a field query |
| Gao et al., 2018 | P2G-MLS 23× versus atomics-only scattering; 15× versus GVDB gathering | Aggregate suitable transfers; amortize block metadata | MPM has a different multi-node deposition pattern |
| AN5D, 2020 | Best evaluated V100 stencil results; some P100 cases favour competitors | Register/shared-memory trade-offs and tuning | Results vary by architecture and stencil |
| EBISU, 2023 | Up to 2.53×; geometric mean 1.49× over the best compared implementation per benchmark | Reuse and tile size can matter more than high occupancy | A100, CUDA cooperative features; peak of ten kernel trials, not coupled simulation time |
| Intel/Stardock D3D11 case | Approximately 21 → 16 ms/frame | Find queue gaps and blocking API calls | Upload-buffer aliasing, not the same readback mechanism |

These comparisons also explain why generic recommendations conflict. Sorting helps when later accesses recover its cost. Large shared-memory tiles help until register pressure, redundant halos or occupancy losses dominate. Sparse structures help only if saved work exceeds classification, allocation, lookup and synchronization costs. AN5D's architecture-dependent results and the smaller whole-model gain in the neighbour-search paper are particularly relevant cautions.[^6][^10][^11][^12][^13][^16]

## Recommended experiments

All experiments should keep the current implementation as a selectable reference. First use exact structural optimizations; evaluate approximate field thresholds separately. The priority is workload-specific:

| Priority | Experiment | Primary beneficiary | Decision criterion |
|---|---|---|---|
| 1A | Sparse occupancy queries versus dense neighbour grids | Dynamic slime and ants, especially sparse populations | Faster complete steps with identical published counts and rule semantics |
| 1B | Coalesced atlas generation; isolate gradient and publication costs | Interactive 3D field previews | Lower publication/frame time with identical values and correct shared-resource visibility |
| 2 | Movement specialization, then spatial work ordering | High-particle-count slime; later dense ants | Lower total step time including indexing, with behaviour validation |
| 3 | Demand-driven field transfers and publication cadence | Definitions requesting CPU fields or many internal steps per displayed frame | Less wall time while preserving the output freshness contract |
| 4 | Within-step stencil fusion and radius-specific tuning | Static sparse simulations | Faster field and whole-step time without numerical/behavioural regression |
| 5 | Deposit strategy crossover and dynamic free-slot bookkeeping | Dynamic ants and heavily populated/overallocated cases | Benefit after sorting, clearing and compaction costs are included |
| 6 | Active bricks or approximate coarsening | Large, demonstrably sparse fields | Classification, halos and maintenance included in the measured win |

**Experiment 1A: neighbour counts.** Add timings for seed, each axis, count publication, death checks and population-copy submission. Compare direct per-particle boxes, cooperative tiled boxes and the existing running-window implementation across radii 1, 3, 6 and 12, particle densities and clustered distributions. For dense cases, compare segmented scans or tiled running windows with the full-line method. Keep an empirical crossover rule rather than choosing from operation count alone.

Counts should use a representation with adequate exact integer range. A whole-volume floating-point summed-volume table can lose integer precision above 2²⁴, even if each local neighbourhood is small; a parallel sum must also avoid integer overflow. These representation questions are separate from field diffusion precision. No global spin-wait scheme should depend on unguaranteed thread-group scheduling.

**Experiment 1B: preview.** Separate atlas dispatch, gradient dispatch, CPU logging, `Flush`, resource acquisition and the actual draw. Compare a reference atlas kernel against group-shared transposition with the same mapping and channels. Then measure one publication per rendered frame while keeping every simulation step. Test display-only downsampling as a distinct quality option, since the current scale setting is not a downsampling control. Do not infer that trail rendering is cheap from the small trail-publication interval.

**Experiment 2: movement.** Before sorting, collect cache, instruction, register and contention evidence on supported tooling; add diagnostic subvariants that preserve meaningful inputs. Benchmark stable-setting specialization independently from spatial ordering. Sweep sorting intervals 1, 4, 16 and 64 steps and several bin sizes. Include the build/sort cost, ID indirection, and the effects of changes in sensor distance and speed. Keep state and trails associated with original IDs.

**Experiments 3–5: transfers, fields and deposits.** For transfers, compare identical output demands and report latency as well as throughput. For field fusion, start with radius 1 and the two ant fields under matching parameters, retaining the already optimized reference. For deposits, measure dense scanning against sparse touched-voxel or particle lists with all list construction/clearing included. Birth/death tests must cover reused slots and deposits from the pre-mutation state.

**Experiment 6: sparse fields.** Prefer an active-brick prototype before a full adaptive octree. Dense values inside bricks retain regular accesses and amortize sparse directory lookups, a lesson also supported by GPUMPM's block-oriented design.[^13][^14] An exact-zero scheme must include stencil halos, sources, pending deposits and any boundary/clamp rules that can generate values. Every affected region must be active before the update that needs it. Initially dispersed particles may activate almost the whole grid, so retain a dense fallback.

The earlier **0.05 threshold is not active in these GPU baselines**. Using it to merge or skip field regions would be an approximation, independent of the decay coefficient. A low absolute field value can still determine which sensor wins. Such a variant needs its own error and behaviour budget; it should not be bundled with an otherwise exact memory-layout optimization.

### Realistic speed ceilings

Amdahl's law helps prioritize experiments without predicting results: if a stage occupies fraction `f` of runtime and becomes `s` times faster, overall speedup is `1 / ((1 - f) + f / s)`. Assuming other costs stay fixed, a 2× faster stage gives these illustrative outcomes:

| Measured case | Target share | Whole-step speedup if that stage becomes 2× faster | Ceiling if its cost vanished |
|---|---:|---:|---:|
| Million-particle slime: movement | 63.6% | 1.47× | 2.75× |
| Million-particle slime: fields | 31.6% | 1.19× | 1.46× |
| Dynamic ants: dynamic stage | 63.9% | 1.47× | 2.77× |
| Sparse static ants: fields | 97.9% | 1.96× | About 48× |

The last ceiling is only algebra: moving the bottleneck makes it unattainable in practice. In particular, the dynamic-stage share is not all removable count work, and improving GPU scheduling can change adjacent measured intervals. No factor in this table is a benchmarked optimization result.

## Behaviour and benchmark validation

Start with frozen-state component checks. Compare every neighbour count against the reference, including boundaries and radius edge cases. For preview transposition, compare atlas values, channel meanings and coordinates before image-level checks. For field kernels, compare intermediate axis outputs, final fields, extrema, boundary values and sensor samples. Retain the same precision and arithmetic order where practical, and explicitly measure differences introduced by reassociation.

Then compare complete simulation runs. A single maximum voxel error is insufficient: record the fraction of sensors choosing a different winning direction, near-tie cases, movement success, occupancy consistency, food collection and population changes. For slime, compare network connectivity, branch density, occupied volume, field distributions and time to connect sources. These quantify behaviour without requiring every stochastic trajectory to remain identical.

**Small and large network scales are required.** Scale slime speed and sensor distance together as intended for the network, for example at 0.5×, 1×, 2× and 4× the baseline, with geometrically suitable domains. Do not confuse this with changing particle count alone. Test both a fixed number of voxels with altered sensing and geometrically scaled domains with comparable particle density. Wider sensors change memory locality and amplify the consequences of field approximations.

Run representative cases for 600–2,000 steps, checking early dispersion, emerging networks and established networks. Preserve snapshots at several stages and use multiple initial seeds. Current profiling reaches hundreds of steps but does not replace this comparison. Because occupancy claims can depend on parallel scheduling, first measure repeat-to-repeat variation in the unchanged backend; require an optimized variant to preserve invariants and remain within a defined behavioural tolerance relative to that baseline. Collision-free fixtures should support stricter equality checks.

| Required dimension | Coverage for optimization acceptance |
|---|---|
| Geometry | 2D and 3D; small/large grids; obstacles and irregular active domains |
| Species | Slime first; then ants and mixed groups for shared changes |
| Scale | Small/large networks, jointly scaled speed and sensors |
| Fields | Food/no food; separate ant pheromones; different diffusion and decay settings |
| Boundaries | Wrapped/closed boundaries and runtime boundary transitions |
| Population | Static, normal/random deaths and divisions, near capacity, many unused slots |
| Outputs | Off, particles, field previews, trails, CPU values, meshing and actual viewport drawing |

A slime speedup does not by itself validate ants. They share some field machinery but differ in pheromone selection, food consumption, home behaviour, boundaries and deposits. Shared low-level changes need ant regression checks even if slime is the first optimization target.

For performance acceptance, use restored identical starting snapshots and serial, counterbalanced baseline/candidate runs. Avoid simultaneous benchmarks on this integrated GPU. Report warm-up, batch size, sustained wall time, GPU intervals, variability, memory demand and any output latency. Recheck the complete workload after a component win; stop pursuing a variant whose maintenance work or regressions consume the gain.

D3D11 timestamps remain useful for stage ranking. AMD GPU Performance API explicitly supports D3D11 and Radeon APUs, with supported sample types queried at runtime; actual counter availability on this driver still needs verification.[^18] Radeon GPU Profiler's documented capture APIs are DX12, Vulkan, OpenCL and HIP, so it should not be prescribed as a direct profiler for this D3D11 backend.[^19] CUDA-only repository results likewise require algorithm adaptation, not a library installation on the Radeon 660M.

The immediate decision is to choose **neighbour counting for dynamic simulation throughput, atlas publication for interactive preview performance, or movement for dense slime**. The evidence supports all three as substantial targets. It does not yet establish a single replacement algorithm, an octree speedup, or preserved behaviour for any unimplemented variant.

## Sources

[^1]: Nuclei local benchmark evidence, 12 September 2026. [Reproduction and environment record](C:/Nuclei/docs/research/gpu-performance/evidence/README.md), [full aggregate table](C:/Nuclei/docs/research/gpu-performance/SUMMARY.md), [aggregate JSON](C:/Nuclei/docs/research/gpu-performance/summary.json), [file hashes](C:/Nuclei/docs/research/gpu-performance/evidence/file-hashes.json). These are the sources for all Nuclei timing claims; raw batch files are linked in the aggregate table.

[^2]: Nuclei current local source, inspected 12 September 2026. [Step ordering](C:/Nuclei/Nuclei-v4/Nuclei4.Gpu.D3D11/GpuFullSlimeSolverEngine.cs:1432), [deposit selection](C:/Nuclei/Nuclei-v4/Nuclei4.Gpu.D3D11/GpuFullSlimeSolverEngine.cs:1800), [dynamic rules](C:/Nuclei/Nuclei-v4/Nuclei4.Gpu.D3D11/GpuFullSlimeSolverEngine.cs:1911), [neighbour-grid construction](C:/Nuclei/Nuclei-v4/Nuclei4.Gpu.D3D11/GpuFullSlimeSolverEngine.cs:2119), [preview publication](C:/Nuclei/Nuclei-v4/Nuclei4.Gpu.D3D11/GpuFullSlimeSolverEngine.cs:2558), [gradient generation](C:/Nuclei/Nuclei-v4/Nuclei4.Gpu.D3D11/GpuFullSlimeSolverEngine.cs:2613), [readback](C:/Nuclei/Nuclei-v4/Nuclei4.Gpu.D3D11/GpuFullSlimeSolverEngine.cs:3243), [movement shader](C:/Nuclei/Nuclei-v4/Nuclei4.Gpu.D3D11/GpuFullSlimeSolverEngine.cs:7646), [neighbour axis shader](C:/Nuclei/Nuclei-v4/Nuclei4.Gpu.D3D11/GpuFullSlimeSolverEngine.cs:8220), [combined preview shader](C:/Nuclei/Nuclei-v4/Nuclei4.Gpu.D3D11/GpuFullSlimeSolverEngine.cs:9509). Local source hash is recorded in source 1; line numbers may move with subsequent edits.

[^3]: Nuclei, [historical GPU performance notes](C:/Nuclei/docs/performance/README.md), local record. Used to distinguish already implemented optimizations from new candidates. Some historical raw-data directories referenced there are absent in this checkout; their headline results are not treated as newly verified runs.

[^4]: Mark Harris, Shubhabrata Sengupta and John D. Owens. [Parallel Prefix Sum (Scan) with CUDA](https://developer.nvidia.com/gpugems/gpugems3/part-vi-gpu-computing/chapter-39-parallel-prefix-sum-scan-cuda). GPU Gems 3, chapter 39, 2007. Primary explanation and implementation of parallel scans, summed-area tables and associated transpositions.

[^5]: Thomas Smith / b0nes164. [GPUPrefixSums](https://github.com/b0nes164/GPUPrefixSums), inspected at commit `98d93a4e9ed2f3c8353119515bf9be90a2e137ad` (10 December 2024). Actual code: [ReduceThenScan.hlsl](https://github.com/b0nes164/GPUPrefixSums/blob/98d93a4e9ed2f3c8353119515bf9be90a2e137ad/GPUPrefixSumsD3D12/Shaders/ReduceThenScan.hlsl). Reduce/scan/propagation and wave operations were inspected; this is not a drop-in D3D11 implementation.

[^6]: Mark Harris / NVIDIA. [An Efficient Matrix Transpose in CUDA C/C++](https://developer.nvidia.com/blog/efficient-matrix-transpose-cuda-cc/), 18 February 2013. Performance table and shared-memory kernels; accompanying [transpose.cu](https://github.com/NVIDIA-developer-blog/code-samples/blob/master/series/cuda-cpp/transpose/transpose.cu). The article's full implementation discussion, not its later automated summary, supports the analysis.

[^7]: Microsoft. [ID3D11DeviceContext::Flush](https://learn.microsoft.com/en-us/windows/win32/api/d3d11/nf-d3d11-id3d11devicecontext-flush), documentation accessed 12 September 2026. Command submission, overhead and completion semantics.

[^8]: Paul Richmond et al. [FLAME GPU 2: A framework for flexible and performant agent based simulation on GPUs](https://onlinelibrary.wiley.com/doi/10.1002/spe.3207). Software: Practice and Experience, 2023. Section 4.1 and Figure 8, especially the sorting-frequency comparison. DOI: 10.1002/spe.3207.

[^9]: FLAMEGPU team. [FLAMEGPU2](https://github.com/FLAMEGPU/FLAMEGPU2), inspected at commit `5909ecccf90f6b05d9b2cb09de0d50b612420f9f` (22 July 2026). Actual code: [MessageSpatial3D.cu](https://github.com/FLAMEGPU/FLAMEGPU2/blob/5909ecccf90f6b05d9b2cb09de0d50b612420f9f/src/flamegpu/runtime/messaging/MessageSpatial3D.cu), including histogram, CUB exclusive scan and message reordering; [CUDAScanCompaction.cu](https://github.com/FLAMEGPU/FLAMEGPU2/blob/5909ecccf90f6b05d9b2cb09de0d50b612420f9f/src/flamegpu/simulation/detail/CUDAScanCompaction.cu), including reusable scan-buffer management. The latter is support code, not a complete compaction kernel.

[^10]: Robert Chisholm, Steve Maddock and Paul Richmond. [Improved GPU near neighbours performance for multi-agent simulations](https://eprints.whiterose.ac.uk/id/eprint/153625/). Journal of Parallel and Distributed Computing 137, 53–64, March 2020; online November 2019. [Author-deposited manuscript](https://eprints.whiterose.ac.uk/id/eprint/153625/7/1-s2.0-S0743731519301340-main.pdf), sections on strips, proportional bin width, construction/query costs and experiments. The institutional record supplies the explicit 1.27×/1.34×/1.21× comparison. DOI: 10.1016/j.jpdc.2019.11.002.

[^11]: Kazuaki Matsumura, Hamid Reza Zohouri, Mohamed Wahib, Toshio Endo and Satoshi Matsuoka. [AN5D: Automated Stencil Framework for High-Degree Temporal Blocking on GPUs](https://arxiv.org/abs/2001.01473), CGO 2020; [full paper](https://arxiv.org/pdf/2001.01473), sections 7.1–7.3. Actual repository code: [cuda_stencil.c](https://github.com/khaki3/AN5D/blob/21e1f52041545c65452915f348be99fdefc940ec/cuda_stencil.c), commit `21e1f52041545c65452915f348be99fdefc940ec` (27 November 2019), especially register-plane generation and double-buffered shared-memory declarations near `print_kernel`. DOI: 10.1145/3368826.3377904.

[^12]: Lingqi Zhang et al. [Revisiting Temporal Blocking Stencil Optimizations](https://arxiv.org/html/2305.07390v1), ICS 2023. Sections 3–4 and 7, including A100 hardware, peak-of-ten timing, differing temporal depths and the comparison with AN5D, STENCILGEN, ARTEMIS and DRSTENCIL. DOI: 10.1145/3577193.3593716.

[^13]: Ming Gao et al. [GPU Optimization of Material Point Methods](https://kuiwuchn.github.io/GPU_MPM/GPU_MPM.pdf), ACM Transactions on Graphics 37(6), article 254, November 2018. Sections on transfer, sorting and sparse grids; Figure 6, section 7 and limitations. The reported 23×/15× values concern P2G-MLS, not complete Nuclei-like steps.

[^14]: Kui Wu and collaborators. [GPUMPM](https://github.com/kuiwuchn/GPUMPM), inspected at commit `2947161d5e33517a987ef5c1942fc88bb00da898` (13 September 2020). Actual code: [P2GKernels.cu](https://github.com/kuiwuchn/GPUMPM/blob/2947161d5e33517a987ef5c1942fc88bb00da898/src/Simulation/TimeIntegrator/P2GKernels.cu), shared tiles, shuffle reductions and atomic scatter; [ParticleDomain.cu](https://github.com/kuiwuchn/GPUMPM/blob/2947161d5e33517a987ef5c1942fc88bb00da898/src/Simulation/Particle/ParticleDomain.cu), histogram sorting, scans and persistent index mappings. CUDA-specific implementation.

[^15]: Microsoft. [Copying and accessing resource data (Direct3D 10 and 11)](https://learn.microsoft.com/en-us/windows/uwp/graphics-concepts/copying-and-accessing-resource-data), documentation accessed 12 September 2026. Staging resources, asynchronous copies and synchronization caused by mapping before copies complete.

[^16]: Raja Bala / Intel, with Stardock. [Removing CPU-GPU sync stalls in Galactic Civilizations 3](https://www.intel.com/content/www/us/en/developer/articles/case-study/removing-cpu-gpu-sync-stalls-in-galactic-civilizations-3.html), page updated 3 February 2022; case study concerns development before the 2015 release. Timeline diagnosis and approximately 21-to-16 ms frame-time result.

[^17]: NVIDIA. [GVDB Voxels](https://github.com/NVIDIA/gvdb-voxels), inspected at commit `8fa911ffbfd22c088cd5f003742b209bec83fb0c` (6 January 2022). Actual code: [cuda_gvdb_raycast.cuh](https://github.com/NVIDIA/gvdb-voxels/blob/8fa911ffbfd22c088cd5f003742b209bec83fb0c/source/gvdb_library/kernels/cuda_gvdb_raycast.cuh), `rayCast` near line 543, hierarchical traversal and empty-region skipping. This supports the rendering technique, not a measured Nuclei diffusion speedup.

[^18]: AMD GPUOpen. [GPU Performance API](https://github.com/GPUOpen-Tools/gpu_performance_api), official repository documentation accessed 12 September 2026. D3D11/APU support and runtime query of supported sample types; no counter-based profile is claimed in this report.

[^19]: AMD GPUOpen. [Radeon GPU Profiler manual](https://gpuopen.com/manuals/rgp_manual/), official documentation accessed 12 September 2026. Supported capture APIs and distinction from the GPU Performance API.
