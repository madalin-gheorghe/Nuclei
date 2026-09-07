# CPU to GPU Behavior Parity

The current V3 CPU solver is the behavioral reference for the V4 GPU solver.
Translation includes operation order, conditions, state changes, and observable
side effects; matching only the final particle count is not sufficient. The
post-push parity work described below is implemented and guarded by focused
regressions. The remaining intentional execution differences are listed
explicitly.

## Ant food and nest cycle

Status: behavior aligned.

The GPU implementation follows the CPU order:

1. Sense using the current `foundFood` state and particle age.
2. Move and deposit using that same state.
3. Check the newly reached voxel for food.
4. Check the new position for a nest visit.
5. Store pickup or nest-visit age as `1`.

An arriving ant therefore deposits its previous trail type before changing
state. Nest visits use the raw signed group speed as V3 does, while close-to-home
alignment and the deposit boundary guard use the raw, unscaled sensor distance.
Midpoint conversion in that guard uses V3/.NET midpoint-to-even rounding. Reset
clears found-food and launch-boundary state and restores the original home plane.

## Intentional execution differences

- CPU ant and population selection follows shuffled sequential particle lists,
  while GPU selections use deterministic, iteration-dependent hashes because
  particles execute in parallel. Eligibility, stage order, inherited state,
  probabilities, and population limits match; individual selected identities
  and trajectories need not.
- CPU fields use floating-point object mutations. GPU food and deposits use
  fixed-point atomic updates so simultaneous writes are not lost. Fractional food
  below one unit becomes zero on GPU instead of becoming negative as it can on
  CPU.

## Post-push V3 parity port (2026-08-28)

V4 mirrors the V3 changes made after commit `535cde6` in the following areas.

### Steering, movement, and particle state

- Slime groups use Classic steering: the strongest positive sensor direction is
  selected, with optional random turns controlled by Wander.
- In non-wrap mode, sensor boundary handling mutates the working particle plane
  sequentially in V3 order. A no-sensor result applies no steering force and
  retains V3's deterministic plane rotation instead of choosing a random turn.
- An active stored parent remains usable even when its maximum density makes it
  non-walkable. V4 reads that parent's authored behavior and vectors, attempts
  recovery from it, and retains it while reversing direction when no valid
  neighbour exists.
- Planar reset preserves the authored origin and axes. The first move is centered
  on the active plane without shifting the reset geometry.
- Ant launch duration scales to the farthest field boundary, uses the inherited
  home-plane wave, stops after boundary contact, and clears that contact at the
  nest.
- `highDeposit`, ant food state, launch state, and home axes survive requested
  particle synchronization. Slime deposits use V3's quarter-strength rule after
  an occupied destination and clear the flag on reset.

### Voxel topology and field evolution

- Non-wrapped outer voxels and holes are solver boundaries with maximum density
  zero. Wrapped outer voxels remain active; holes remain boundaries. Degenerate
  grids use V3's dimension priority: Z, then Y, then X.
- Authored-active and walkable are distinct states. Boundary transitions,
  particle counting, scalar diffusion, ant diffusion, decay, movement, deposits,
  and food projection all receive the active-state binding they require.
- Particle counts include active blocked parents as V3 does. Scalar density under
  an occupied active blocked parent is cleared during decay, while V3's distinct
  scalar, ant-food, and ant-base outer-boundary behavior is preserved.
- Density processing is species-aware rather than being gated only by the
  presence of slime particles. Ant-only and empty simulations therefore still
  diffuse and decay an existing scalar field, matching V3.
- Slime food and ant food remain separate channels. Slime food is projected into
  scalar density before diffusion; ant food remains consumable and its live CPU
  preview synchronizes only on demand.
- Slime diffusion exposes the V3 input order `Diffuse Rate`, `Decay Rate`,
  `Falloff`, `Diffuse Range`, with `Gradual = 1 - Falloff`.

### Ageing and dynamic population

- Reset uploads one V3 age increment. The two no-movement warm-up solutions run
  `AdvanceParticleAges`; subsequent ages advance at the post-move parent check.
  Minimum-age gates therefore observe the same iteration as V3.
- Population stages run in V3 order: normal death, normal division, random death,
  random division. Random stages see normal newborns and the current population
  budget.
- When either normal rule is enabled, both per-particle neighbour fields are
  refreshed every step. Division retains its pre-death counts, a due normal
  division republishes post-division counts before random changes, range zero
  consumes stored state, and negative paired ranges publish zero.
- Death releases occupancy in the same population pass. Neighbour publication
  still follows V3's stage ordering, including the applicable same-pass particle
  set. Global and per-group GPU counters are independently validated against the
  live slots and their stored group tags.
- Death and birth claims use an atomic reserve-then-undo operation, eliminating
  the contention-dependent dropped claims caused by the former bounded retry
  loop.
- Normal-division children start at age zero with cleared ant home/launch state.
  Random-division children inherit age, food state, home plane, and launch state.
- Reset reconstructs fresh particle runtime state, including age, found-food,
  high-deposit, launch, home, walkability, and free-slot state.

### Grasshopper integration and lifecycle

- Runtime particle-group metadata applies the same ant/slime classification and
  V3 population transforms as the CPU solver. Group kind is part of the engine
  state invariant, so switching between ant and slime requires a reset before a
  different movement specialization is selected.
- Legacy three- and four-input Voxel Settings Slime archives migrate in place,
  preserving parameter GUIDs, wires, and persistent data. The slime-group
  constructor separately normalizes historical Exploration metadata to Wander
  and ignores retired probabilistic-steering archive state.
- While Update is true, the Dendro converter rebuilds for every incoming solver
  update. It caches successful output, prefers the native Dendro path, and keeps
  the last successful result while Update is false.
- Reset, live voxel replacement, and disposal detach voxel, particle, preview,
  and mesh callbacks before an engine is replaced. Previously emitted objects can
  no longer call a disposed or unrelated engine.
- Dynamic voxel preview synchronization remains on demand and updates live Ant
  Food rather than retaining reset-time data.

## GPU residency and readback

Full particle and voxel state remains demand-driven: it is synchronized only when
the Grasshopper graph or a CPU preview explicitly requests it. Dynamic-population
steps do stage a tiny snapshot containing four global counters plus one counter
per group. The snapshot uses a three-slot staging ring and is polled on the next
solution with `DoNotWait`; it does not block the GPU or copy the full particle or
voxel buffers. A newer blocking synchronization invalidates older queued counter
snapshots.

Ant-only workloads use `MoveAntParticlesAndDeposit`, a specialization of the
shared movement core that removes unreachable slime branches. The generic and
specialized ant paths are checked for bit-identical particle and field state at
every focused checkpoint.

## Regression evidence

The architecture probe contains focused entry points for the behaviors most
likely to regress:

| Probe | Contract covered |
| --- | --- |
| `--ant-reset-parity` | reset state, raw speed/sensor thresholds, food/nest cycle |
| `--ant-shader-specialization` | generic versus ant-only movement equivalence |
| `--density-species-parity` | scalar/ant field evolution, active targets, ant minimum semantics |
| `--planar-origin-parity` | authored planar reset and first-move centering |
| `--population-ordering` | stage order, atomic limits, neighbour publication, group counters, stale-readback guard |
| `--blocked-parent-parity` | movement, recovery, fallback, counting, and density for active blocked parents |
| `--sparse-active-bindings` | sparse boundary transition and scalar/ant diffusion bindings |
| `--voxel-preview-sync` | on-demand live dynamic-field preview synchronization |
| `--dendro-cache` | V4 held-true updates plus cache replacement and disposal |
| `--dendro-update-v3` | V3 held-true conversion without pulse or self-scheduling state |

The default probe also covers retained group metadata, Dendro continuous-update/cache
behavior, solver-boundary priority, output callback detachment, reset/disposal,
large sparse fields, public compatibility contracts, and embedded shader copies.
The preservation verifier passes the same exact contracts for both net7 and
net48 outputs.

## Validated performance evidence

The existing synchronized median/p95 measurements passed the 5%
equivalent-behavior gate:

| Scenario | `535cde6` HEAD | Final | Change |
| --- | ---: | ---: | ---: |
| Default | 2.854 / 3.063 ms | 2.873 / 3.079 ms | +0.67% / +0.52% |
| Ant-only | 5.577 / 5.590 ms | 4.995 / 5.336 ms | -10.44% / -4.54% |

The sparse normal-division diagnostic is not an equivalent-behavior performance
comparison: HEAD omitted V3's required every-step neighbour recount and
publication. Enabling that required work changes median/p95 from 2.7243 / 2.789
ms to 3.6824 / 4.058 ms (+35.17% / +45.50%). It is the measured cost of the
additional reference behavior, not a regression in an equivalent workload.

No new performance values are inferred from functional probes; the table above
remains the validated measurement record until the benchmark is rerun under the
same hardware and synchronization conditions.

## Final compatibility and artifact record

| Contract | Final value |
| --- | --- |
| Component/parameter GUIDs | 40; `BA5DD56D2DB434E2FEEC0AD489F1DF481FAFC4FA2E3843C3C21E49DED4DCB126` |
| Exported types / GH components | 51 / 38 |
| Public API | 737 records; `CC2F81667507021DC7CCD16452CB0BB6CAE0AD5D9373D12036E91925DF643252` |
| GH schema | 214 records; `4DBBA58BF7AB3C0E3EBA484F917C36032977EDD13FE10F9B2A373846FEDFF365` |
| Main resources | 34; name hash `471155F7F1C2429746C207F91331025BB014654B626DDD875A945576A2CC5AC2`; content hash `7A1503EB442606ADF3FFF63E884BE84C7732E5FD9E69936EE73DE315F58DC590` |
| Embedded shaders | 33; `323B4A5D3D7E43ECF980807D8E6556B086C3106C7B7A0A2999D34D7F4896A9BA` |
| D3D11 GPU resources | 28; `37A557839F7CE2DBE822D5A0EE0D72EE8990689ACD2F0A816749507C0826DC2D` |
| D3D11 display resources | 5; `7962C5E6C8BCAE08EEB74E649239601E884515546EA8E8C926A809224896C695` |
| Full-solver / mesh ABI | 416 bytes / 104 fields; 48 bytes / 12 fields |

The current artifact SHA-256 values, for traceability rather than compatibility
gating, are:

- net7 `Nuclei4.gha`:
  `D3DB5333F2F6708C53C0E931BE6023D27015D4392FB1268DC1F786BD8241FEB5`
- net7 `Nuclei4.Gpu.D3D11.dll`:
  `C54E0E69623BE1E13EDDF5C4FB74DA05BA2586918A5911D6AD27923CA3448A45`
- net48 `Nuclei4.gha`:
  `AD9A2C8DA94909CA0AD29D39A198C049391CD44C4073FA59C79DCB89AA76C044`
- net48 `Nuclei4.Gpu.D3D11.dll`:
  `576C18FF6E762F84329C556A76C5B431080E898C5CF3394F094C1E41C76FA98F`

Per-CSO hashes and their authorized behavioral reasons are locked in
[`V4_PRESERVATION_CONTRACT.md`](V4_PRESERVATION_CONTRACT.md).

## Historical investigation notes (resolved)

Earlier investigations correctly exposed age misalignment, the missing
high-deposit rule, population-claim contention, population-stage ordering, and
neighbour-publication differences. They also contained intermediate claims that
no longer describe the final implementation: that V4 always deposited at full
strength, that V4 age remained two iterations behind, that the division-rate
diagnosis had no resolution, and that zero neighbour samples necessarily
represented a live solver defect.

Some early distribution comparisons were additionally produced with mismatched
starting positions/headings or included newborn auxiliary defaults. Those values
were useful for repairing the harness but are not release evidence. The final
focused tests above replace those intermediate observations.

## Translation checklist

- Compare against the active V3 CPU path, not an older archived implementation.
- Record the CPU order of sensing, movement, deposits, state transitions, and age.
- Compare every condition, threshold inequality, and midpoint conversion.
- Compare reset state and persistent per-particle state.
- Distinguish authored-active voxels from walkable voxels in every shader pass.
- Compare voxel-field side effects, group metadata, and particle output state.
- Validate both demand-driven full-state synchronization and lightweight
  telemetry ordering.
- Record intentional differences before considering a translation complete.
