# Nuclei V3.3 definition converter

This tool recreates the top-level `.gh` files in `Nuclei Definitions\v3` with
the mapped Nuclei4 component identities. It never edits the source files and
requires the target directory not to exist.

Run from the repository root:

```powershell
dotnet build tools\Nuclei.DefinitionConverter\Nuclei.DefinitionConverter.csproj -c Release
dotnet run --project tools\Nuclei.DefinitionConverter\Nuclei.DefinitionConverter.csproj -c Release --no-build -- --source ".\Nuclei Definitions\v3" --target ".\Nuclei Definitions\v4_updated" --map ".\tools\Nuclei.DefinitionConverter\v3.3-to-v4.json" --v4-gha ".\Nuclei-v4\Nuclei4\bin\Release\net48\Nuclei4.gha"
```

The converter fails closed for an unmapped Nuclei3 object, unexpected schema,
existing target, malformed archive, lost object ID, changed wire endpoint, or
unapproved persistent-data change. When `--v4-gha` is supplied, it also fails if
the map's target assembly name/version disagrees with that binary.
`_conversion_manifest.json` records the pinned target assembly identity and hash,
definition hashes, counts, exact wire-connection hashes, converted object IDs,
and schema adapters.

The same executable can migrate the retired Trail Frequency input into a new,
fail-closed directory before V3-to-V4 conversion:

```powershell
Nuclei.DefinitionConverter.exe --remove-trail-frequency --source SOURCE --target TARGET --component-guid COMPONENT_GUID
```

This mode accepts only the former `[Trail Size, Trail Frequency]` schema, removes
the frequency parameter and its incoming wire, applies the native one-input
layout, and verifies all other objects, IDs, and wire endpoints after GH_IO reload.
It writes `_trail_settings_migration.json` with the exact removed connections.

Schema adapters:

- Dendro keeps Voxels, Iso Value, Update, and output 0. It drops the obsolete
  Type and Dendro Settings connections and adds Method `0`, Maximum Elements
  `5,000,000`, and Smoothing Iterations `1`.
- Solver keeps outputs 0 and 1 IDs/data/wires, serializes output 2 GPU Status
  with a deterministic parameter ID, and applies the native three-output V4
  layout at the original component pivot. This prevents Grasshopper from
  reporting a missing output chunk (and opening an interactive archive-warning
  dialog).
- Slime Particle Group renames legacy input 8 Wander metadata to Exploration
  while retaining its parameter ID, source, value, and probabilistic mode.
- Current Voxel Settings Slime archives are accepted only in the order
  `[Diffuse Rate, Decay Rate, Falloff, Diffuse Range]`. A legacy unwired Gradual
  value is inverted to `Falloff = 1 - Gradual`. A wired legacy Gradual input is
  rejected because it needs an explicit inversion node.

After conversion, run the Rhino 9 validator in the sibling
`Nuclei.DefinitionValidator` folder. Its report verifies loading and structure
and performs the documented targeted Dendro runtime path; it does not execute or
benchmark every complete simulation.
