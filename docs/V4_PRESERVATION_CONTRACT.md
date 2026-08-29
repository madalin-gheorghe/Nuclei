# V4 Preservation Contract

> **Voxel preview visual lock:** The final approved voxel preview is defined by
> [V4_VOXEL_PREVIEW_GOLDEN_MASTER.md](V4_VOXEL_PREVIEW_GOLDEN_MASTER.md).
> Architecture changes must preserve that renderer and its appearance exactly.

The immutable pre-split baseline is commit `344818c` and tag
`v4-pre-architecture-verified-2026-08-17`. The architecture refactor passes only
when the existing GH1 plugin remains compatible and the D3D11 backend keeps the
same behavior and steady-state performance, except for the single volume-mesh
smoothing correction recorded below.

## Identity baseline

- Output and assembly identity: `Nuclei4.gha`, `Nuclei4, Version=4.1.0.0`.
- Namespace: `Nuclei4`; V4 source contains no `Nuclei3`. The architecture probe
  names V3 types only inside its explicit cross-version parity harness.
- Grasshopper assembly ID: `a4810f34-10b6-480c-a6d0-607aac4e8d2a`.
- COM typelib GUID: `67e300d2-061e-4987-b6e6-ffbb4810a624`.
- Component identities: 39 `GH_Component` types plus 2 hidden parameter types;
  the sorted class/GUID contract SHA-256 is
  `EAD28352BBF34A775D9C097DFD8F4D60063B4ED3B72A564FC517999AA2877FC8`.
- The two V4-only GUIDs missing from the older V3-to-V4 map are GPU Volume To
  Mesh `2cc99696-1f20-4add-82d5-a317c252edb8` and Particle Trail Preview
  `b17ecf97-0425-4ae2-a6b0-b3f869a5bc72`.
- Grasshopper catalogue identity includes every component's type, GUID, name,
  nickname, description, category, subcategory, exposure, and ordered parameter
  schema. The approved Nuclei4-to-Dendro change intentionally renames the former
  GPU Volume To Mesh component, adds the Continuous/Discrete Method input,
  generalizes Maximum Triangles to Maximum Elements, and returns a generic
  Dendro Volume / Mesh output. The resulting catalogue contains 39 components
  and 218 records; its canonical SHA-256 is
  `8615C7A37CCFB3E580CB227FECA4282FEB62ABF0F1FD441FCDD17807E4152ECD`.
  The user-authorized V3 gradual-diffusion port adds one optional `Gradual`
  input to Voxel Settings Slime, mirroring the V3 component exactly (name
  `Gradual`, nickname `gradual`, default 1.0). It adds no component, changes no
  GUID, and touches no other component. The catalogue becomes 39 components and
  219 records with canonical SHA-256
  `5E2B76D3D5211BAA9FD8A8A174EED337B5F331C10EB5E182B60CA0632BD80A1B`.
  All 24 shader hashes, the 25-resource manifest, the 52 exported public types
  and the 732 public API records are unchanged: the port reuses the existing
  `Keep`, `Diffuse` and `Weights` constant-buffer contract, computes the
  reshaped kernel on the CPU, and applies gradual retention only on the final
  diffusion axis. Legacy definitions that omit the input keep `gradual = 1`,
  which reproduces the original raised-cosine kernel bit-for-bit.

  The user-authorized removal of the vestigial CPU `Solver` component
  (GUID `fb3d6e25-19b6-4673-accb-15c99b8ac33f`) is an intentional, breaking
  identity change, taken on the explicit statement that no Grasshopper
  definition uses it. V4 is GPU-only; `SolverGPU` is the sole solver.
  `BenchmarkSuite1/SolverBenchmark.cs` was removed with it, being its only
  consumer — it reflected into the CPU solver's private diffusion methods.
  The resulting baseline is:

  | Contract | Before | After |
  | --- | --- | --- |
  | Components | 39 | 38 |
  | Component/parameter GUIDs | 41 | 40 |
  | Exported public types | 52 | 51 |
  | Public API records | 732 | 721 |
  | Schema records | 219 | 212 |

  New canonical hashes: component/parameter GUID
  `BA5DD56D2DB434E2FEEC0AD489F1DF481FAFC4FA2E3843C3C21E49DED4DCB126`,
  public API `A5D5D3E875C1DC86BCD1889512F660228B7C41DA56AB367628A3EDE4F6AB323E`,
  component schema `6DE59EFE0708890D275146B0D31D6D4FCB892A18EF9DD208319D51A1CF7A586B`.
  All 24 shader hashes, the 25-resource manifest and content hashes, and both
  GPU parameter-struct layouts are unchanged: the removal touched no GPU binary.

  Any archived definition still containing the removed CPU Solver will fail to
  restore that one component. This is accepted and is the reason the change is
  recorded here rather than treated as a refactor.

  The user-authorized V3 slime/ant food split, food-source projection, and
  random-population port land together, as they did in V3. Behaviour:

  - `Voxel.antFood` is a new public property on the compatibility assembly and
    a new scalar map at index 13 (12 is already `SlimeChemoattractantsV2`). The
    index matches V3 so both toolsets stay value-compatible.
  - The ant-consumable remaining-food channel is now fed by `InitialAntFood`.
    `InitialFood` feeds a new immutable food-source channel that a new
    `ProjectFoodSources` kernel adds into density before diffusion each step,
    so slime food is diffused and decayed by the normal field update. Slime
    sensing no longer takes `max(density, remainingFood)`; that conflation was
    the reason ants ate the slime map.
  - Random division/death add three optional inputs to Particle Settings
    Population and independent probability paths inside `ApplyParticleDeath`
    and `ApplyParticleDivision`. Existing atomic claims still enforce the
    minimum and maximum population. `DynamicPopulation` now also turns on for
    random-only configurations.
  - `Padding2` was claimed for `FoodSourceOffset` and `Padding3` for
    `RandomPopulationFrequency`; two probability floats and two new pads follow.

  | Contract | Before | After |
  | --- | --- | --- |
  | Public API records | 721 | 724 |
  | Schema records | 212 | 215 |
  | Main resources | 25 | 26 |
  | Shaders | 24 | 25 |
  | Nuclei4.Gpu.D3D11 shaders | 19 | 20 |
  | FullSolverParameters size / fields | 400 / 100 | 416 / 104 |

  New canonical hashes: public API
  `AFF00CFDFD61A19DA481F03635B4A186CA8E4E95DA07D1232C43DCF1F5FB9BC1`,
  component schema `83EDE30503D7F16B5EF4788AE0EF7C4E58EA6DFCCB1A1BC0032B5DB08DA2F70F`,
  resource-name `5DFD765D509A50F5942F8E0C8758AD2F8DEC6319AAD2B2A24FF9311798FB77C7`,
  resource-content `86C7F0FE7886C9572056163B7ADE2ECACEB2F1EDB17E53061765DA8CD30F8246`,
  25-shader `727924BC7F95CD0C05321D940DE736EF31351C52E325CAABFEA8600F95637CED`,
  20-shader D3D11 compute
  `81BBCFF703CD211151A694E2923329C132ECBAFB5E9738F5707200E87F2E70A5`.
  The five display shaders are unchanged, so the locked voxel preview renderer
  is untouched.

  **Migration consequence.** A definition authored before the split has its food
  on index 6. That value is now the slime source, not ant food, so ants in such
  a definition find no food until Ant Food is populated. Existing value lists are
  retrofitted in place by `VoxelFoodValueList`, which renames item 6 to
  "Slime Food" and appends "Ant Food".

  **Deliberate divergence from V3.** V3's port also moved food into the static
  preview path via `IsStatic`. V4 keeps food on the dynamic-density path,
  because changing it would alter the appearance locked by
  [V4_VOXEL_PREVIEW_GOLDEN_MASTER.md](V4_VOXEL_PREVIEW_GOLDEN_MASTER.md). Record
  this in `GPU_BEHAVIOR_PARITY.md` if the preview is ever revisited.

  Two corrections followed first in-Rhino testing, and are folded into the
  hashes above:

  - `VoxelPreviewField.HasGpuDensityTexture` now decides which fields get a GPU
    volumetric preview. Slime food and ant food are dynamic — ant food is
    consumed and read back — but they live in the packed deposit buffer, not a
    float density buffer, so the old `IsDynamicDensity` test bound the density
    buffer for them. Ant food therefore rendered the slime chemoattractant
    field, and each food preview allocated a second 256-cubed volumetric atlas.
    Both now use the CPU preview path, which reads the correct maps.
  - `DispatchDynamicPopulation` gated the death and division kernels solely on
    the neighbour-rule switches, so random-only configurations never dispatched.
    Dispatch is now due when either the neighbour rule or the random path is
    active, and the kernels consult the existing `DeathEnabled`/`DivisionEnabled`
    flags so a random-only run cannot trigger the neighbour rule.
- The assembly exports 52 public types. Its 732 declared public/protected API
  records have SHA-256
  `E5773E1D63A1ED4E18227C1FB481DB222900A8401D4B3FAF92DB8DDA4D799081`.
  New APIs may be added in new assemblies, but none of these records may be
  removed or changed from the compatibility assembly.
- Category registration remains `Nuclei4`, short name `N4`, symbol `N`, with the
  current Rhino 9 minimum-version load gate.

Do not move the current public GH1 model types directly into a new assembly.
That changes assembly-qualified identities. In particular,
`Nuclei4.ParticleGroup` owns the public nested Grasshopper Goo and parameter
types. Keep the legacy public model/Goo surface in `Nuclei4.gha`, or prove full
binary and document compatibility with type forwarding. Prefer separate pure
domain state/snapshot types behind the compatibility layer.

## GPU binary and execution baseline

- The GHA has 25 manifest resources: 24 precompiled shaders under
  `Nuclei4.GpuShaders.*` and `Nuclei4.Properties.Resources.resources`.
- The sorted manifest-name SHA-256 is
  `A58F476B1D4B92E2400236B5C324C873054666D3B1B7798F1604C5CD778224E3`.
- The immutable baseline's canonical sorted `resource-name + CSO SHA-256`
  contract hash was
  `BBD3F0049D5A902B774EE45A7B5BACB52C6D20E2C2605C7115144DAB5AE5C88A`.
- The sole approved shader correction is
  `Nuclei4.GpuShaders.SmoothVolumeForMesh.cso`. Its baseline SHA-256 was
  `474A64BA1E9A76713EAACA023B0B740D00DF658D895B0CD38CC4AA9C8BABE61C`;
  the corrected SHA-256 is
  `34129D9DB7C789296F2A55945C58CE580F1A3D8E572DE1065390965C695F5595`.
  The correction uses the existing two-dimensional `LinearIndex256` mapping
  when a smoothing dispatch exceeds 16,776,960 voxels. It changes no public
  API, component identity, resource name, buffer ABI, or other shader.
- The accepted post-correction 24-shader contract hash was
  `49430869B85305B3316834150973D0A64B97D9D6384BDD86B9B80A534F2F8D6A`.
  The user-authorized middle-strength boundary-contact cap candidate changes only
  `DensityPreviewPS.cso` from
  `2AB721A014F9AE71784142A5EF5112B8A3353B422DA185E351C23F5925EF245F`
  to `8A0990EC6CA082044E898F8204F3AFE04FC7ED7891A278E0DE25F545B4DE2E4C`.
  It uses transfer-weighted maximum solidity of 0.28 on the entry face and
  0.168 on the exit face. The earlier subtle 0.18/0.099 candidate was
  preserved separately with shader hash
  `2A149D628FF45217DB9A30C4FD4A44F1DE9D45FD0BEC9BC485A23CCF128D74AD`.
  The rejected 0.80-solidity candidate was preserved separately with shader
  hash `F3A4A16E7128D0C1364414720DB719BC95E01B427FB8185A2BE43DCB5F6F21D9`.
  The user-authorized strict death-range correction changes only
  `ApplyParticleDeath.cso`, from
  `91DE20BB8097DE0E17E19C1BB50F86402002B9ED742CBCA617E2A0AA2B356C0D`
  to `0B59FE4219C60E0C240DF02A38B56C60022648D169C4B4A551A0AA651C3B15BE`,
  so particles at exactly the configured minimum or maximum neighbour count
  survive. Its 24-shader contract hash is
  `81117AADE789D8ED0941D84D08D841D11FCF36E917FB4705AAAC1EA367B57636`;
  the 19-shader D3D11 compute-support hash is
  `81A53477872D464E8F656098640D4EBB5462B396EDF99CF2823E8F43983FF9D4`.
  The candidate five-shader display hash is
  `7962C5E6C8BCAE08EEB74E649239601E884515546EA8E8C926A809224896C695`.
  The full 25-resource GHA content-contract hash is
  `6BE2FBE8F771839133E93027157A784F88B4B22C4B5CCEC9BEB77BFB031C45BC`.
  The subsequent user-authorized V3 behavior port changes exactly three
  compute shaders: `MoveParticlesAndDeposit.cso` is now
  `109548AF0CA0757C2640EDB7E98DAB72C189CFB689C614237105BA7DE4F56FA3`,
  `ApplyParticleDeath.cso` is now
  `B98B6051F77B479F48FE378A41963C4A10F6ABDD77E050EB9F6D988037564802`,
  and `ApplyParticleDivision.cso` is now
  `778212AF3AA5602FCCEEE009CDA72585DBEF416A359160A26047B92A8AF58443`.
  This ports the 0.2 heading weight, true wander-off at zero, `/10` wander
  interval, and unconditional minimum-age gates for neighbour-based death and
  division. The resulting 24-shader hash is
  `19B5A2C0E1740500371EF90FDEC01DE7B6DFACE97BE2E75F29EF65EA10472F10`,
  the 19-shader compute-support hash is
  `89EAE4928BC3A3CEB2E2860BFF5C3784A58CC9F315E9903A2C99C148D5182DBD`,
  and the full 25-resource GHA content hash is
  `C93C20131E72804062F7C075CFCAF5608B8EC7EE2D643D7FAE338681FE2B570E`.
  The global minimum-death-age correction subsequently changes only
  `ApplyParticleDeath.cso`, to
  `DF557E10422426B506AA3C5ADE60E1FE863603A77110886644B44DE01458A64E`.
  The death decision is exclusively minimum-age eligibility followed by the
  configured strict neighbour-range test; invalid or blocked parent state is
  no longer a hidden independent death condition. The
  resulting 24-shader hash is
  `4BED65F16E8665654F3B183578B303DD5DE10C6BFC89F74DF16E9F68BAEFC97F`,
  the 19-shader compute-support hash is
  `70309FDD33AB2B04D9D2B3D084D4DDD1627FC1F0984F1FFA77FEEA38673017C9`,
  and the full 25-resource GHA content hash is
  `252428154E0021251A04EBD13D0F0D7783E12B9FC6B84D2994A06BBD2EFB4E99`.
  The boundary-recovery correction changes only
  `ApplyBoundaryModeTransition.cso` to
  `512D51569D42FC47B21C10F6D974927145FA6123DD47427BAA62F0CEF5996C2C`
  and `MoveParticlesAndDeposit.cso` to
  `20BEE246CC59AC5CEFD5731307A4F05C8E087BE44B7E9C9FAC62715DD5A6FFE1`.
  Recovery excludes the unchanged current voxel, redirects the particle toward
  a valid non-boundary neighbour, and reverses its heading when no candidate is
  available. The resulting 24-shader hash is
  `32D7719F315D6144B5A24C03659B6AD4C5240070B691F11586A1CA2FAD6BC505`,
  the compute-support hash is
  `B850BA628172642B874259D4E8895138F8535E188D51A7D05315366FD24F9432`,
  and the full resource-content hash is
  `3F1258D3C919329EFA0FE95B5515B72893A943E941995A7E0E65B5FA1CEB0383`.
  `Verify-V4Preservation.ps1` locks every shader individually; every unrelated
  CSO retains its approved hash.
- Host/HLSL buffer ABI remains unchanged. `FullSolverParameters` is 400 bytes
  with 100 fields; `GpuMeshParameters` is 48 bytes with 12 fields.
- Preserve the current reset/step order: boundary transition and recount;
  movement/deposit; dynamic population; diffusion/decay/ant fields; requested
  preview production; and only then demand-driven readback.
- Preserve GPU residency. A normal step must not synchronize particles or voxels
  unless the Grasshopper graph requests those CPU outputs. Shared particle,
  trail, density, and mesh paths must remain direct/session-backed.
- Preserve all current fallback and lifecycle behavior: hardware D3D11 then
  WARP, hard and fast reset, live wrap changes, live supported settings, paused
  extraction, hidden-preview handling, disposal, and current GPU-error cleanup.

The settings text protocol is also compatibility data. Keep the tokens
`VoxelSettingsSlime`, `VoxelSettingsAnt`, `SpeciesInteractionSettings`,
`WrapSettings`, `SolverSettings`, `TrailSettings`, `DivisionSettings`,
`DeathSettings`, `PopulationSettings`, and `DiscreteVectors`, including field
order and current parsing behavior:

```text
VoxelSettingsSlime diffuseRate diffuseRange decayRate
VoxelSettingsAnt foodDiffuse foodDecay baseDiffuse baseDecay diffuseRange
SpeciesInteractionSettings slimeToAntFood slimeToAntBase antToSlime
WrapSettings wrap
SolverSettings maxIterations
TrailSettings trailSize trailFrequency
DivisionSettings enabled minimumAge range minimumNeighbours maximumNeighbours frequency
DeathSettings enabled minimumAge range minimumNeighbours maximumNeighbours frequency
PopulationSettings minimumPopulation maximumPopulation
DiscreteVectors x,y,z [x,y,z ...]
```

Keep GH archive keys `Minimum`, `Maximum`,
and `Average`. `Voxels_AND` and `Voxels_OR` default to
`false/false/true`; the point, curve, and mesh attractors default to
`true/false/false`.

## Executable gates

Run from the repository root:

```powershell
dotnet build .\Nuclei-v4\Nuclei4\Nuclei4.csproj -c Release -f net48 --no-incremental -p:SkipGrasshopperInstall=true
dotnet build .\Nuclei-v4\Nuclei4\Nuclei4.csproj -c Release -f net7.0-windows --no-incremental -p:SkipGrasshopperInstall=true
dotnet run --project .\tools\Nuclei.ArchitectureProbe\Nuclei.ArchitectureProbe.csproj -c Release -- --rhino-inside
dotnet run --project .\tools\Nuclei.ArchitectureProbe\Nuclei.ArchitectureProbe.csproj -c Release -- --rhino-inside --gpu
dotnet run --project .\tools\Nuclei.ArchitectureProbe\Nuclei.ArchitectureProbe.csproj -c Release -- --rhino-inside --benchmark-gpu
pwsh -NoProfile -File .\tools\Verify-V4Preservation.ps1
rg -n "Nuclei3" Nuclei-v4
```

The final command must return no matches. The normal probe must retain its large
empty-field allocation, sparse/dense selection, scalar/vector packing, boolean
merge, snapshot isolation, scattering, and adaptive-preview assertions. The GPU
probe must retain D3D initialization, reset, density preview, and live wrap
transition assertions.

The volume-mesh smoothing regression must cross the D3D11 dispatch-row boundary
(at minimum 256x256x256, preferably also 300x300x300) and verify that the
high-index tail is processed. A source-only change is insufficient: both the
GHA and D3D11 support assembly must contain the corrected embedded CSO, and the
per-resource verifier must confirm that their copies are identical.

Before release, add or run golden GPU cases for slime move/deposit/diffuse/decay,
ant food/nest state, division/death, volume meshing, particle/trail/density frame
metadata, demand-driven no-readback steps, and repeated reset/disposal. Run a GH1
document made with the baseline plugin and verify no missing components, lost
wires, changed defaults, or changed saved menu state.

## Performance gate

Use the same Rhino build, GPU, driver, document, visibility state, and settings.
Discard warm-up/first-draw samples and compare at least three steady-state runs.
No scenario may regress by more than 5% in median frame time; investigate any
increase in p95 time, allocations, GPU-to-CPU copies, or resource creation per
step. Reference medians are recorded in `docs/performance/benchmark-summary.csv`
and `docs/performance/solver-frame-comparison.md`, including 34.070 ms for the
comparable 900k-particle, 3000x3000x1 GPU workload and 10.957 ms for 300k
particles on a 350x350x350 field.

For this architecture split, five sustained synchronized runs of the same
262,144-particle, 262,144-voxel D3D11 scene produced a baseline
median-of-medians of 3.530 ms and a split median-of-medians of 3.562 ms. The
measured change was +0.91%. Each run discarded eight warmups and measured seven
32-step synchronized batches, so the comparison includes GPU completion while
reducing sub-millisecond scheduling noise.

Do not use the whole GHA file hash as a gate; rebuild metadata can change it even
when the public API and shader binaries are identical. Compare the contracts
above and observable results instead.

## Ant food plumbing and the GitHub comparison mode

Follow-up corrections after in-Rhino testing of the food split:

- `VoxelGridCombiner` merged every scalar map explicitly and had no `AntFood`
  entry, so Voxel Union silently dropped ant food and everything downstream of
  it, including the solver, saw none.
- `SolverGpuBuffers` gated its voxel scan on `mayHaveFood`, which considered only
  the slime map. When nothing else forced a scan the capture returned early and
  `InitialAntFood` stayed null, so ants saw no food while the preview still drew
  it from `Data.AntFood`.
- `VoxelDensityStore` did not copy `AntFood`, losing it on the legacy voxel path.
- `CreateVoxelFieldPreviewFrame` refused any combined preview without ant
  particles. Ants and Slime is a union, so it now draws whenever either system is
  present; only Ant Pheromones still requires ants.

The temporary index-14 GitHub comparison mode was removed after testing showed the
published build renders the slime chemoattractant volume identically; the boundary
contact caps are not the cause of that preview being hard to read.

## Population claim contention

`TryClaimDeath` and `TryClaimBirth` used a bounded compare-exchange retry loop on a
single population counter. Under the contention a real division pass creates --
hundreds or thousands of particles claiming in the same dispatch -- the 64 attempts
were exhausted and the claims were silently dropped, so the observed birth and
death rates tracked contention rather than the configured rules. Measured with the
`--parity` harness on 2,000 particles over 20 iterations, division produced 2,558
particles with the retry loop and 7,184 with a single atomic reserve-then-undo,
against roughly 38% expected eligibility. Both now reserve with one InterlockedAdd
and undo when the reservation crosses the floor or ceiling.

This is the likely cause of V4 populations tracking far below V3 for the same
Grasshopper settings. An earlier attempt at this fix appeared to break the solver;
that breakage was in fact the HLSL short-circuit defect recorded above, and the
atomic claims are correct once that is fixed.

## CPU hot path in the GH1 output sink

Grasshopper profiling showed the Solver component at over 50 ms per step while the
headless GPU solver measured about 2 ms for the same population, so essentially all
of it was adapter-side. Three changes, none of which alter the public API, any
component identity, or any shader:

- `ParticlePreviewCache` no longer allocates its three Rhino point clouds in field
  initializers. `BeginBuild`/`Invalidate` already assign them and every consumer
  null-checks, so nothing observable changes -- but a `ParticleList` no longer
  touches Rhino native code just to exist. V3 allocated one on every division pass.
  Applied to V3 and V4.
- `Gh1GpuSolverOutputSink` reuses `Voxel` instances through a flat-index cache,
  refreshed at most once per step, instead of calling `VoxelField.CreateVoxel` for
  every particle on every step. At 200,000 particles that was 200,000 allocations
  and a million scalar lookups per step. The cache is keyed by occupied voxel, so it
  is bounded by population rather than grid size. This also matches V3, where a
  particle references the shared grid voxel rather than a private copy.
- The per-particle vector math in that same loop no longer calls
  `Vector3d.Unitize` or the `Plane` constructor, both of which are native
  transitions -- four per particle per step. The axes are already unit length and
  orthogonal by that point, so the replacements are plain arithmetic.

The end-to-end sink cost could not be measured headlessly; one native dependency
remains in that path. The Grasshopper profiler is the measurement of record.

## Volume preview sample budget

The volumetric raymarch already does everything a volume renderer normally would to
stay cheap: 4-voxel occupancy blocks with a slab-based skip to the next block
boundary, early ray termination above 0.985 alpha, an adaptive step scale, trilinear
rather than tricubic sampling, and one shadow ray per pixel rather than per sample.

What remained was the step budget. `automaticVolumeSteps` is
`min(256, max(64, ceil(maxResolution * 1.5)))` over the preview atlas resolution,
which is itself capped at 256 -- so every volume preview on a grid whose atlas
reaches roughly 171 or more runs the maximum 256 samples per ray, and
`PreviewVolumeSampleCount` returned a hardcoded 0 with no way to lower it.

**User-authorized:** the volumetric preview samples 128 rays per pixel by default,
and Voxel Preview gains a **High Resolution** right-click toggle that raises it to
256. The toggle persists through `Write`/`Read` as `HighResolutionPreview` and
defaults off.

A temporary `Volume Samples` input was trialled first, along with a 1024 ceiling.
Testing showed values above 256 gain nothing visible, so both were removed: the
component parameter schema returns to 215 records with its previous canonical
SHA-256 `83EDE30503D7F16B5EF4788AE0EF7C4E58EA6DFCCB1A1BC0032B5DB08DA2F70F`, and the
raymarch limits return to 256 (`configuredSteps`, `maxSteps`, the loop bound and
`remainingIterations` move together, because the loop bound is the real ceiling).
The 25 embedded shaders were verified byte-identical to the pre-trial build.

The default of 128 halves the per-ray sample count against the previous automatic
budget on large grids, so the volume preview is deliberately cheaper and slightly
softer out of the box. This is an intentional appearance change and supersedes the
golden master for the volume preview at default settings; the previous appearance is
reproduced exactly by enabling High Resolution.

`Preview_Voxel` gains four public/protected members for the toggle (`Write`, `Read`,
`AppendAdditionalComponentMenuItems`, `highResolutionHandler`), taking the public API
to 728 records with SHA-256
`1ADB075EA91D2B043F890CF2249A57EDBB62A1076BE295736C10DAAA0F0AA433`. No GUID, resource name or shader changed.

## Post-push V3 parity release evidence

### Trail Settings frequency retirement

**User-authorized:** Particle Trail Settings exposes only `Trail Size`; both V3 and
V4 serialize `TrailSettings <size> 1` internally. Its compatibility reader accepts
the former two-input archive schema and discards the retired frequency parameter,
including its persistent value and source wire. The four shipped definitions that
used the component were migrated directly to the one-input schema. This removes one
GH schema record and adds the `Read` override to the public API; component GUIDs,
shaders, resources, and solver ABI remain unchanged.

The 2026-08-28 V3 parity port preserves demand-driven full-state output: particle
and voxel state are still read back only when the Grasshopper graph requests
them. Dynamic-population steps stage a tiny, nonblocking snapshot containing four
global counters plus one counter per group; this is required to publish V3-exact
group metadata without synchronizing either full state buffer. Ant-only states
select a specialized movement shader that removes unreachable slime work. Input
group kind is part of the state-match invariant, so changing between ant and
slime groups forces a reset before another shader can be required. A focused
probe verified that the generic and ant-only paths produce bit-identical particle
and field state.

The final preservation verifier records:

| Contract | Final value |
| --- | --- |
| Component/parameter GUIDs | 40; `BA5DD56D2DB434E2FEEC0AD489F1DF481FAFC4FA2E3843C3C21E49DED4DCB126` |
| Exported types / GH components | 51 / 38 |
| Public API | 741 records; `24B466B99A06FFEB9F24730E411EA53549639C70C8C4BEDAA17100905F8DE037` |
| GH schema | 214 records; `2C82F4DDC84E50A154F6F48DAB9C1E1C82E3A4700F99146FC2DFF128E8518DDB` |
| Full-solver / mesh ABI | 416 bytes / 104 fields; 48 bytes / 12 fields |
| Main resources | 28; name hash `5A304C5A9EE3117A8D33B999B27677FC0B5B98CA527657FC0A02E44DBE8993A7`; content hash `A76B6974498D3430B6140BE010AF04D570364F3725866B4DD455734F7253D602` |
| Compute shaders | 27; `C005808A049C53BB021251D0D1C0FD16538FA945153E121AE6D7FECF4740BF83` |
| D3D11 GPU resources | 22; `76A587C8C31492F0E597DD47A6A0B55A537F6368F7E460B17D1AFF0D1A7CBA9F` |
| D3D11 display resources | 5; unchanged `7962C5E6C8BCAE08EEB74E649239601E884515546EA8E8C926A809224896C695` |

Four compute binaries changed after the preceding lock, each for an explicit V3
behavioral-parity correction:

| Shader | SHA-256 | Locked behavior |
| --- | --- | --- |
| `ApplyDecay.cso` | `B240272B2F8B628CAB4773110EED4569577ABE9F017C3A4049D9798FB4288A44` | Clears scalar density beneath an occupied active obstacle and preserves V3's field-specific outer-boundary decay. |
| `CountParticles.cso` | `815E7A1AF6EC98A66FECFEC14B7E0026D79E2E3D90308F896B87AAAA5D56B7B9` | Counts particles whose stored parent is active even when that parent is not walkable. |
| `MoveAntParticlesAndDeposit.cso` | `55F5A9AB23D458E9A5B4012E7B9B267C17FF1FFC7BE328862A53BE756DF994EE` | Applies the corrected sequential non-wrap sensor-plane mutation, no-sensor steering sentinel, and active blocked-parent recovery in the ant-only specialization. |
| `MoveParticlesAndDeposit.cso` | `83D9B6B5EF977163ED72A5BA6A128A0A662D8009540370FA09DC600F3E343E42` | Applies those same sensor, sentinel, and blocked-parent corrections in the generic movement kernel. |

All other 23 CSOs retain their previous exact hashes. The verifier checks every
CSO independently in the compatibility assembly and its support assembly, in
addition to the aggregate hashes above.

The final synchronized median/p95 performance comparisons pass the 5% gate:

| Scenario | `535cde6` HEAD | Final | Change |
| --- | ---: | ---: | ---: |
| Default equivalent behavior | 2.854 / 3.063 ms | 2.873 / 3.079 ms | +0.67% / +0.52% |
| Ant-only equivalent behavior | 5.577 / 5.590 ms | 4.995 / 5.336 ms | -10.44% / -4.54% |

The sparse normal-division diagnostic changes from 2.7243 / 2.789 ms to
3.6824 / 4.058 ms (+35.17% / +45.50%). That case is not equivalent behavior:
the baseline skipped V3's required every-step neighbour recount and publication,
while the final build performs it exactly.

The previous alive-only population divergence is closed. Focused ordering tests
lock V3's same-pass ghost-inclusive neighbour publication, while the asynchronous
group counters are checked against both live slots and their stored group tags.
The current GHA SHA-256 values, recorded for artifact traceability rather than
used as preservation gates, are
`1F8A5F1E56E0A5DB47C30757388BE703D1EAD5E47CF57A2C5C8B34D7B34BCACC`
for net7 and
`C995A80D32073AF56BB041EF1FC4F9EC197D3AEA2DEF1F7CDE43E67749E1A943`
for net48.
