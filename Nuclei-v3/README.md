# Nuclei V3.4

Nuclei V3.4 is the CPU implementation for Rhino 8 and 9 and the behavioral reference
for GPU translations. Its assembly and component GUID family are intentionally
separate from both the original V3.0 plugin and Nuclei V4.

V3.4 is a major CPU-performance and ant-behavior milestone. Dense SIMD diffusion
accelerates suitable slime and ant fields while preserving fallback behavior for
other grid types. The ant solver also gains improved food sensing, persistent food
scent, refined deposit falloff, safer nest handling, and reduced unused field work.

In Rhino 9 only, every V3 component displays **old v3** above its body. Rhino 8
keeps its existing appearance. The banner is built into `Nuclei3.gha` and does
not change component attributes, GUIDs, serialization or solver behavior.

The previous public Yak release is **Nuclei3 3.3.2**. V3.4 source is versioned
independently here; publishing it to Yak is a separate release step.

Current features include CPU particle generation inside voxel fields, scalar-array
solver paths, wrap/no-wrap behavior, balanced diffusion passes, ant and slime
behavior, static and dynamic voxel previews, and the Nuclei-to-Dendro bridge.
With Convert held true, the Dendro bridge rebuilds for every incoming solver
update; when false, it retains the last successful volume and stops solver ticks
from recomputing the bridge or its downstream components.
Particle occupancy is exclusive: initialization is sampled without replacement,
movement and division atomically claim empty voxels, and blocked moves stay in
place without depositing before choosing a new heading.
GPU solver and Direct3D components are not included in V3.x. The V3 source and
deployment are also free of the dormant GPU engines and Vortice dependencies.

The current ant implementation includes V4's improved food-gradient following in
3D and XY/XZ/YZ, persistent food-scent emission, and nonnegative food consumption.
Food/base pheromones share a deterministic line-based diffusion traversal with
independent rates and fused decay; ant-only runs skip unused slime-field updates.
Measured CPU field updates improved by 2.87× in 3D and 2.45× in 2D on the recorded
fixtures. See [performance results](../docs/performance/README.md) for scope and
validation; these figures do not measure whole-simulation frame rates.

The current source also accelerates slime diffusion and decay with SIMD on modern
.NET builds. It applies to complete periodic cubic 3D grids, disabled density limits,
radius one and gradual one, while preserving native food-aware sensing. Other
settings retain the existing path. Recorded 600-step comparisons found roughly
2.77–3.01× faster complete slime steps with food on supported grids. Octree
coarsening remains experimental and is not part of the production solver.

CPU ant food and base pheromones now also use reusable dense SIMD buffers on modern
.NET for complete periodic 2D/3D grids with no density limits (at least 4096 voxels).
Each channel retains its own diffusion and decay settings. Sparse, bounded, custom
density-limit and legacy .NET Framework cases retain the existing algorithm.
Edible ant food remains stationary and emits food pheromone; it is not diffused.
Recorded complete-step improvements are approximately 1.62–1.83× on the tested
ant workloads. See the [performance summary](../docs/performance/README.md).

At Max Iterations, the solver pauses any automatic Grasshopper Timer/Trigger
targeting only that solver, preventing continued downstream preview and extraction
updates. The result stays available. Reset or raise the limit and re-enable the
trigger to run again. Shared and manual triggers keep their existing behavior.
Deleted target references saved inside a trigger do not prevent it from pausing.

## Build

Open `Nuclei-v3.sln`, or validate without installing into Grasshopper:

```powershell
dotnet build .\Nuclei3\Nuclei3.csproj -c Release -f net7.0-windows -p:SkipGrasshopperInstall=true
```
