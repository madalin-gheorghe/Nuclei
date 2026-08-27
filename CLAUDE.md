# Nuclei

Grasshopper/Rhino plugin for voxel-driven particle simulation (slime-mold and ant
systems). Two independent product lines share this repo.

| Line | Runtime | Solver | Output | Solution |
| --- | --- | --- | --- | --- |
| V3.3 | Rhino 8 | CPU | `Nuclei3.gha` | `Nuclei-v3/Nuclei-v3.sln` |
| V4.1 | Rhino 9 WIP, Windows | Direct3D 11 | `Nuclei4.gha` | `Nuclei-v4/Nuclei-v4.sln` |

`Nuclei-v2-old/` is a local-only display companion for comparing against the frozen
Food4Rhino V2 release. It is gitignored and is not part of either product.

## Rules

**V3 is the behavioral reference.** When behavior differs between CPU and GPU, V3 is
correct and V4 is ported to match — never the reverse. Record known differences in
`docs/GPU_BEHAVIOR_PARITY.md`.

**V4 identity is locked.** `docs/V4_PRESERVATION_CONTRACT.md` is the gate. Do not
change, and do not let a refactor incidentally change:

- assembly name/version, the Grasshopper assembly ID, or the COM typelib GUID
- any component GUID, name, nickname, description, category, or parameter schema
- shader resource names or the buffer ABI
- the public API surface of `Nuclei4.gha` (new APIs go in new assemblies)

V3 and V4 keep **separate GUID families**. Never copy a GUID between them.

**Any intentional deviation must be authorized by the user first, then recorded** in
`V4_PRESERVATION_CONTRACT.md` with old and new hashes, and the expected hashes in
`tools/Verify-V4Preservation.ps1` updated in the same change. An unexplained hash
change is a bug, not a passing test.

**The voxel preview appearance is visually locked** to
`docs/V4_VOXEL_PREVIEW_GOLDEN_MASTER.md` and `docs/reference/right-voxel-preview.png`.

**Performance rules for V4** (detail in `docs/V4_ARCHITECTURE.md`): keep particle,
voxel, trail, density and mesh state GPU-resident; a few coarse calls per step, never
per-particle dispatch through an interface; no mandatory GPU→CPU copies — read back
only demanded outputs. Steady-state regression budget is 5%.

**Layering for V4**: `Nuclei4.Core` and the two `*.Abstractions` assemblies must not
reference Grasshopper, Rhino UI, Direct3D or Metal. Backends depend on abstractions,
never the reverse. The GH1 adapter is the composition root. GH2 and Metal are design
targets only — do not implement them unless asked.

## Commands

Build:

```bash
dotnet build Nuclei-v4/Nuclei-v4.sln -c Release
```

Verify V4 preservation — must pass before any change is called a checkpoint:

```bash
pwsh -NoProfile -File tools/Verify-V4Preservation.ps1
```

It defaults to `Nuclei-v4/Nuclei4/bin/Release/net7.0-windows`, so build Release first.
It requires PowerShell 7 (`AssemblyLoadContext`); Windows PowerShell 5.1 cannot run it.
`tools/Nuclei.ArchitectureProbe` holds the structural and GPU-signature checks behind it.

Benchmark the GPU solver headlessly (no Rhino needed) before claiming a perf result:

```bash
dotnet run --project tools/Nuclei.ArchitectureProbe -c Release -- Nuclei-v4/Nuclei4/bin/Release/net7.0-windows --benchmark
```

Compare `WALL ms/step`, not the CPU submission figures — D3D11 dispatch is async.
See `docs/performance/gpu-benchmark.md` for options and recorded results.

## Conventions

- Match the surrounding style: `camelCase` fields, no `_` prefix, comments only where
  intent is non-obvious.
- Back up before risky work — `.codex-backups/<what>-before-<yyyyMMdd-HHmmss>/`, source
  only, exclude `bin`/`obj`/`.vs`.
- Never commit build output, profiler captures (`*.etl`, `*.etlx`, `*.diagsession`),
  or tool binaries.

## Context

`docs/DEVELOPMENT_STATUS.md` is the current checkpoint summary — read it before
assuming what does or does not exist yet. `docs/performance/performance-history.md`
has the optimization history.

The original simulation and voxel-map concepts are the author's own work; AI assistance
(Codex, Claude) is used for profiling, GPU translation, testing and documentation.
Codex reads `AGENTS.md` in this repo — keep the two files consistent if both exist.
