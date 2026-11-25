# Ant diffusion GPU equivalence probe

Builds deterministic small D3D11 fixtures without opening Rhino. Every fixture runs ten variants of the actual solver: direct/tiled diffusion × separate/fused decay, first with separate food/base processing (reference), then with paired food/base processing, plus paired/tiled/fused variants using forced particle-driven deposits and normal automatic deposit selection. Every variant compares the entire packed deposit buffer against the reference. Reference variants disable adaptive deposits when the validation flag is available. Private validation flags select the variants; production defaults remain unchanged.

```powershell
dotnet build tools/Nuclei.AntDiffusionProbe/Nuclei.AntDiffusionProbe.csproj -c Release -m:1 -p:BuildInParallel=false -nr:false
dotnet tools/Nuclei.AntDiffusionProbe/bin/Release/net8.0-windows/Nuclei.AntDiffusionProbe.dll --nuclei-output Nuclei-v4/Nuclei4/bin/Release/net7.0-windows --json .codex-temp/ant-diffusion.json
```

The 33 fixtures cover odd, noncubic 3D and all three planar layouts; wrapping and closed boundaries; radii 0, 1, 2, 5, 16 and 17 (the tiled limit and fallback); zero and independent food/base diffusion; zero and positive decay; partial voxel selections; obstacles; and minimum/maximum limits including minimum greater than maximum. Eight steps exercise the warm-up and all six 3D axis orders. Five live fixtures run fourteen steps while changing rates, decay, radius and wrapping, explicitly exercising different current buffers for food and base. Deterministic, separated particles move, deposit, carry food, and consume available food without contested destinations.

The `live-sparse-auto` fixture uses a 67×35×19 grid with ten ants. Its automatic variant records actual GPU pass markers at the first movement step and requires `ApplyDeposits.ParticleSparseAnt`, then compares complete state through the live settings and wrap changes. This verifies the dispatched path and its resulting state rather than duplicating the automatic threshold expression in the test.

Ant-only fixtures keep a nonzero authored scalar field and enable ant sensing of it; the scalar field must remain byte-identical to reset. Mixed ant/slime fixtures include both species and both directions of species interaction. Their reference restores the previous always-update scalar condition while the other variants use species gating; full-state parity and an assertion that the scalar changed verify that mixed simulations continue to advance normally.

After reset and every step, bitwise comparisons check scalar density, both pheromone fields, remaining food, particle positions, directions, axes, homes, ages, ant state, population counters, voxel ownership and the entire packed deposit/auxiliary buffer. Expected pass counts verify that pairing and decay fusion actually remove passes; one enabled field correctly keeps its independent path. The decay-only fixtures independently check the asymmetric food/base boundary rule and confirm that disabled diffusion does not apply density clamps. GPU readbacks intentionally synchronize each step; this is a correctness test, not a performance benchmark.

The JSON includes the backend DLL hash and final full-state hashes. `--case NAME` runs one fixture for debugging. Requires a hardware D3D11 adapter; WARP is rejected. Coordinate execution with performance probes because simultaneous GPU work invalidates timings.

`--food-source-emission` runs focused fixed-source tests against manually preseeded field references. It covers 3D and all planar layouts, wrapped/closed boundaries, zero/positive Food Pheromone diffusion, different Food/Base decay, overlapping mixed-species sources, unchanged food quantity, depleted sources, relocated sources after fast reset, and empty-source dispatch elimination. The main 33 fixtures also include emission during live particle consumption and all diffusion implementations.
`--food-following --json PATH` checks V4 food-gradient acquisition and loss through actual GPU movement. `--baseline` records the prior implementation without requiring its launch to follow scent. Compare baseline/candidate JSON to verify changed gradient-following and unchanged absent/flat/trace scent, invalid sensors beside flat scent, carrying ants, and slime movement.

`--nest --json PATH` checks 20 gradual homeward steering comparisons, 12 shared pickup/death/obstacle cases, and 32 overlapping arrival/departure cases in 3D and XY/XZ/YZ. It covers off-plane authored nests, independent coalesced/sparse deposits, and persistent/dynamic counts. These are correctness checks, not timing measurements.

`--trail --json PATH` checks actual GPU deposit amounts against map-scaled launch durations in 3D and all planar layouts, for both trail types before and after their shared 2% floor.

`--departure --json PATH` checks post-delivery propulsion against a strong nest-centred food gradient at iteration 2000, in 3D and all planar layouts, using generic/specialized ant kernels. It verifies departure exit, resumed food sensing, food pickup and obstacles.
`--actual-food --json PATH` checks actual food taking priority over stronger pheromone in 34 GPU fixtures, including depleted/blocked food, returning ants, mixed execution and vertical 3D sensing.
