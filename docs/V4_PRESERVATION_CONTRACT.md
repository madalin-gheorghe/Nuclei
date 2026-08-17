# V4 Preservation Contract

The immutable pre-split baseline is commit `344818c` and tag
`v4-pre-architecture-verified-2026-08-17`. The architecture refactor passes only
when the existing GH1 plugin remains compatible and the D3D11 backend keeps the
same behavior and steady-state performance.

## Identity baseline

- Output and assembly identity: `Nuclei4.gha`, `Nuclei4, Version=4.1.0.0`.
- Namespace: `Nuclei4`; V4 source, probes, and benchmarks contain no `Nuclei3`.
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
  schema. The baseline contains 39 components and 217 catalogue/schema records;
  its canonical SHA-256 is
  `830A96E32E3AB3097BBA6DC9F873C90ACA3EBE6EFDDD8D0B061808F29C9C7E6F`.
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
- The canonical sorted `resource-name + CSO SHA-256` contract hash is
  `BBD3F0049D5A902B774EE45A7B5BACB52C6D20E2C2605C7115144DAB5AE5C88A`.
  Preserve this exactly while only moving the current D3D11 implementation.
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
dotnet run --project .\tools\Nuclei.ArchitectureProbe\Nuclei.ArchitectureProbe.csproj -c Release
dotnet run --project .\tools\Nuclei.ArchitectureProbe\Nuclei.ArchitectureProbe.csproj -c Release -- --gpu
dotnet run --project .\tools\Nuclei.ArchitectureProbe\Nuclei.ArchitectureProbe.csproj -c Release -- --benchmark-gpu
pwsh -NoProfile -File .\tools\Verify-V4Preservation.ps1
rg -n "Nuclei3" Nuclei-v4 tools\Nuclei.ArchitectureProbe
```

The final command must return no matches. The normal probe must retain its large
empty-field allocation, sparse/dense selection, scalar/vector packing, boolean
merge, snapshot isolation, scattering, and adaptive-preview assertions. The GPU
probe must retain D3D initialization, reset, density preview, and live wrap
transition assertions.

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
