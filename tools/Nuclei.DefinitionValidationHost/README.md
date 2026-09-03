# Nuclei Definition Validation Host

This is an isolated Rhino 9 WIP in-process probe for the converted V4
Grasshopper definitions. It avoids Rhino.exe startup and Grasshopper's normal
user-library scan by creating `RhinoCore` with no window and redirecting the
Grasshopper app-data root to a fresh temporary directory.

Normal mode relies on the private Grasshopper profile. For diagnosing Rhino RHP
startup interference, `--safe-mode` provides process-only isolation: it asserts
safe mode, asserts all three installed Rhino-MCP plug-in GUIDs are unloaded,
and initializes Grasshopper's headless ComponentServer directly because Rhino
correctly rejects RHP loads in safe mode. GHA and D3D11 compute loading remain
explicit; no persistent load-protection or scheme setting is changed.

The executable has a BCL-only bootstrap entry point. It registers every Rhino
9 WIP `System` and `System\netcore` assembly/dependency root, updates the native
DLL search path, and explicitly pins the installed netcore `RhinoCommon.dll`
in the default load context before the CLR is allowed to JIT the separate class
that references Rhino, Grasshopper, GH_IO, or Nuclei. This prevents an earlier
NuGet/runtime copy of RhinoCommon from winning assembly resolution.

The default probe loads, in order:

1. the installed Pufferfish GHA;
2. the installed Dendro GHA;
3. the installed ghgl and MeshEdit GHAs;
4. the exact V4 GHA from this repository;
5. `Nuclei Definitions\v4_updated\15_3D Intro_v3.gh` from the discovered repository root.

It then rejects unresolved objects, verifies every resolved `Nuclei4` object
came from the requested GHA path and hash, and prints a JSON result. It never
saves or modifies the Grasshopper definition. A per-stage watchdog terminates
only the validation-host process if a Rhino or GHA load blocks indefinitely.

Build:

```powershell
dotnet build .\tools\Nuclei.DefinitionValidationHost\Nuclei.DefinitionValidationHost.csproj -c Release
```

Run only when no other Rhino validation is using the GPU:

```powershell
dotnet run --project .\tools\Nuclei.DefinitionValidationHost\Nuclei.DefinitionValidationHost.csproj -c Release --no-build
```

Run the authoritative repository validator for the focused runtime graph:

```powershell
dotnet run --project .\tools\Nuclei.DefinitionValidationHost\Nuclei.DefinitionValidationHost.csproj -c Release --no-build -- --validator --only-file "15_3D Intro_v3.gh" --skip-extras "ghgl.gha,Meshedit2000.gha" --expected-v4-sha256 FINAL_64_HEX_HASH --stage-timeout-seconds 900
```

Run the authoritative validator for all 14 definitions (including ghgl and
MeshEdit):

```powershell
dotnet run --project .\tools\Nuclei.DefinitionValidationHost\Nuclei.DefinitionValidationHost.csproj -c Release --no-build -- --validator --expected-v4-sha256 FINAL_64_HEX_HASH --stage-timeout-seconds 900
```

Validator mode executes the repository's authoritative `ValidateInRhino.py`.
It mirrors that script's atomic progress file to the host watchdog, requires its durable
report to identify the exact V4 hash and expected file count, and independently
checks the eight runtime stages, held-true Dendro output identity replacement,
native Dendro volume flow, final Rhino mesh,
zero path errors, the pinned final GHA hash, and unchanged hashes for every
saved definition covered by the report.

`Nuclei.DefinitionValidationHost.NetFx.csproj` is the compatibility matrix host
for the installed 2022-era Dendro build. It starts Rhino 9 WIP under CLR 4.8,
uses safe-mode MCP isolation and a direct ComponentServer, and targets the final
net48 V4 GHA. Rhino safe mode intentionally prevents the IronPython RHP from
loading, so when `PythonScript.Create()` returns null the host constructs the
installed `RhinoPython.PythonScriptScope` directly and seeds IronPython's `Lib`
search path; safe mode and the three unloaded Rhino-MCP plug-ins are asserted
again before any repository script executes.

For warning-free archive diagnostics, `--structural-roundtrip INPUT
--structural-roundtrip-output OUTPUT` uses standard `GH_DocumentIO.Open`, saves
a temporary copy, reopens it, and verifies the SolverGPU 4-input/3-output schema,
string-typed GPU Status output, non-overlapping materialized parameter layout,
wire/object counts, and zero unresolved objects. `--dump-component-schema GUID
--dump-component-schema-output XML` emits a fresh component from the pinned GHA
inside a Grasshopper document and serializes its exact GH_IO schema.
`--legacy-trail-roundtrip INPUT --legacy-trail-roundtrip-output OUTPUT` is a
focused compatibility check: it opens a former two-input Trail Settings archive,
asserts that the current component materializes with only Trail Size, saves it,
reopens it, and rejects any retired frequency parameter serialization.
The net48 host also accepts `--v4-gha`, `--trail-component-guid`,
`--rhino-system`, and `--grasshopper`, allowing the same focused check to run
against Nuclei3 in Rhino 8 and Nuclei4 in Rhino 9.

Both normal and safe-mode net8 and net48 experiments have shown a
repeatable Dendro `LoadGHA` deadlock after one earlier successful load, so the
60-second Dendro watchdog remains mandatory. The net48 route uses the same
Python validator, isolated profile, report gates, and exact runtime assertions;
it must not be treated as passed unless a durable `success=true` report exists.

Paths can be overridden with `--rhino-system`, `--grasshopper`, `--v4-gha`,
`--pufferfish-gha`, `--dendro-gha`, `--ghgl-gha`, `--mesh-edit-gha`,
`--definitions`, `--definition`, `--map`, and `--validator-script`. The default
watchdog interval is 180 seconds per stage and can be changed with
`--stage-timeout-seconds` (15 through 1800).
