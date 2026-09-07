# Solver pause regression probe

This harness loads the built V3/V4 `SolverIterationLimit` helpers and the installed Rhino 9 WIP Grasshopper `GH_Timer`. It uses a windowless Rhino runtime in safe mode, an empty temporary Grasshopper profile, and dummy components in an isolated document. It does not open a user definition or run either simulation.

Run from `C:\Nuclei`:

```powershell
dotnet run --project tools/Nuclei.SolverPauseProbe/Nuclei.SolverPauseProbe.csproj --framework net48 --configuration Release
```

The default assembly paths are the matching Release builds of `Nuclei3.gha` and `Nuclei4.gha`. Two positional arguments can override those paths. The alternative target is `net8.0-windows`, which defaults to the solvers' `net7.0-windows` builds.

## Verified behavior

The .NET Framework 4.8 harness passed against both built solver helpers using Grasshopper `9.0.26244.12303` on 2026-09-07. An unpaused baseline callback expired the dummy solver and its connected downstream component. After pausing, 20 invocations of the captured callback caused no expiration. Tests also passed for manual/shared/unrelated timer exclusions, retained interval and targets, retained dummy state, idempotence, and explicit re-enable/reset.

The fixture uses UTC for the private due-time field, matching Grasshopper. It invokes the real callback directly instead of pumping the UI or waiting for timers.

The City Map regression reproduces two deleted target IDs retained alongside one
live solver target. It failed against the original pause helper and passes against
both corrected helpers: the trigger pauses, queued callbacks stop downstream
expiration, and genuinely shared triggers remain running. Saved target IDs remain
unchanged. This covers the trigger metadata observed in the user's running City
Map definition; it does not measure canvas frame rate.

## Runtime limits

Rhino initialization needs filesystem access to its existing license-manager files. The restricted workspace sandbox denied that access; the successful run used an approved escalation. The .NET 8 runtime path has not been validated.

These tests exercise the helper and timer API only. They do not establish simulation-state preservation, viewport speed, or memory use in a real definition.

## Dendro Update regression

Pass `--dendro` to the executable (or append `-- --dendro` to the run command)
to test the actual V3/V4 converter components inside a Grasshopper document.
The graph uses a tracked voxel payload, a Boolean Toggle, a seeded cached output,
and a downstream component that counts solutions. The original components fail
because Update=false propagates every solver tick. The corrected components
hold their computed output tree without reading voxels or solving downstream,
resume in the same solution on enable, keep propagating while held true, and
freeze again on disable. An explicit downstream refresh still retrieves the cache.
The test also covers the unconnected false default. Actual Dendro volume
generation and viewport timing are outside this regression's scope.
