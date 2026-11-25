# V4 review checklist

Start with [AGENTS.md](AGENTS.md), the linked preservation documents and
[architecture map](../docs/V4_ARCHITECTURE.md). Use [CODING_GUIDE.md](CODING_GUIDE.md)
for implementation conventions. [2026-11-24 review](REVIEW_2026-11-24.md) records
the current findings and verification limits.

## Scope and paths

Record revision/dirty files, requested behavior and excluded work. Trace affected
GH components through `Solver_GPU`, `SolverGpuBuffers`, output-demand traversal,
`GpuFullSlimeSolverEngine`, output sink and preview frames/renderers. For authoring
components, follow `VoxelGridData` transformations and their standalone GPU helpers.
Read callers, callbacks, shaders, embedded-resource manifests and reflective users.

## Checks

- Correctness: empty/invalid inputs, reset/live changes, iteration cap, paused
  extraction, fault recovery, cache invalidation and two independent solvers/documents.
- Compatibility: GUID/catalogue, public and Goo identities, archive roundtrips,
  legacy parameter migration, settings tokens/defaults, assembly and resource names.
- Lifecycle: partial construction, repeated reset, replacement, disconnect/delete,
  document switch/close, callbacks/events, borrowed handles, map/unmap and mutexes.
- GPU: managed/HLSL ABI, bounds/tail dispatch, plane/flattening/units, ownership
  atomics, pass order/barriers, no unwanted readbacks, resource reuse and WARP fallback.
- Readability: focused ownership, state names, hidden globals, duplicated policy,
  obsolete comments. Establish runtime/discovery/serialization/fallback roles before
  calling anything dead. Prefer a small local correction to an unmeasured abstraction.
- Documentation/scripts: current versus intended boundaries, safe build defaults,
  explicit build directory/framework, independent expected evidence and honest results.

## Verification selection

Run from `C:\Nuclei`; build before testing. Never run installation scripts as checks.

```powershell
dotnet build Nuclei-v4/Nuclei4/Nuclei4.csproj -c Release -f net7.0-windows -m:1 -p:SkipGrasshopperInstall=true
dotnet build Nuclei-v4/Nuclei4/Nuclei4.csproj -c Release -f net48 -m:1 -p:SkipGrasshopperInstall=true
pwsh -NoProfile -File tools/Verify-V4Preservation.ps1
dotnet run --project tools/Nuclei.ArchitectureProbe/Nuclei.ArchitectureProbe.csproj -c Release -- --voxel-value-fields
dotnet run --project tools/Nuclei.ArchitectureProbe/Nuclei.ArchitectureProbe.csproj -c Release -- --exclusive-occupancy-gpu-core
dotnet run --project tools/Nuclei.ArchitectureProbe/Nuclei.ArchitectureProbe.csproj -c Release -- --population-ordering
dotnet run --project tools/Nuclei.AntDiffusionProbe/Nuclei.AntDiffusionProbe.csproj -c Release -- --case live-sparse-auto
```

Both frameworks apply to build/ABI changes. Preservation verification currently
fails against historical expectations; classify deltas, never bless current output
automatically. Field checks apply to map/cache/selection changes; GPU checks cover
occupancy/population and one sparse live-ant fixture, not all simulation behavior.
Use the full AntDiffusionProbe matrix for diffusion changes; read its README for
food/nest/trail cases. Pass `--nuclei-output` when using another build directory.
Pause/Dendro, image mapping, periodic surfaces, volume mode and capture tools live
under root `tools`; inspect their project/README and host requirements before use.
Many need installed Rhino and native/license access. Report blocked checks.

Preview acceptance needs the same definition/camera/frame and approved captures.
Performance changes need matched warm steady-state runs and the preservation
contract's 5% gate. Serial GPU tests; concurrent workloads invalidate timings.

## Evidence and report

- P0: immediate catastrophic loss/security failure; P1: major wrong result or
  lifecycle failure; P2: bounded defect/verification gap; P3: low-impact maintainability.
  Severity reflects consequence, not file size or personal style.
- Confirmed: cite the complete reachable path or reproducible failed check.
  Question: state the missing evidence and smallest resolving check.
  Optional improvement: state the benefit and measurement/compatibility cost.
- Format: `ID | priority | confirmed/question/optional | file:line`; then trigger,
  evidence, practical consequence, proposed correction and validating check.
- Lead with prioritized actionable findings; list documentation changes, commands
  and outcomes/limitations, then small ordered fixes. Do not imply bug-free software.
