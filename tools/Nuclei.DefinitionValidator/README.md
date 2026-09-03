# Rhino 9 definition validation

`RunValidation.ps1` launches the real Rhino 9 WIP host with an isolated
Grasshopper library root, loads the requested V4 build and required third-party
GHAs, then opens and reopens all converted definitions without saving them.

Run from the repository root after the final V4 build:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File tools\Nuclei.DefinitionValidator\RunValidation.ps1 -V4Gha ".\Nuclei-v4\Nuclei4\bin\Release\net7.0-windows\Nuclei4.gha" -ExpectedV4Sha256 FINAL_64_HEX_HASH
```

The isolated profile is the default. `-UseNormalGrasshopperProfile` is an
optional fallback for third-party GHAs that only initialize through the normal
Grasshopper startup scan; it refuses to run if the installed `Nuclei4.gha` has
a different SHA-256 from the requested build.

The run verifies:

- the exact requested GHA SHA-256, mapped assembly name/version, conversion-time
  target hash (when recorded), and every mapped GUID/type;
- all 14 files open and reopen with no missing objects;
- object counts and object InstanceGuids;
- exact wire endpoints after component materialization;
- current Slime, Solver, and Dendro parameter schemas;
- probabilistic Slime mode; and
- no V3 library or component GUID residue.

It also performs one controlled runtime check in `15_3D Intro_v3.gh`: the saved
timer remains locked, the validator resets the GPU solver, executes five
non-reset solver solutions to populate density above the saved Iso 0.5, enables Dendro Update,
then executes another solver solution while Update remains true. It proves the cached output
identity changes on that held-true solution and the output continues to flow as native
`DendroGH.VolumeGOO` -> Smooth Volume -> Rhino Mesh without path errors. The
toggles are restored in memory and the document is disposed without saving.

The durable result is `_rhino9_validation.json`. Transient status is written to
`_rhino9_validation.progress.json` and removed on completion. This is a
structural/load validation plus that explicitly reported targeted runtime path;
it does not claim full behavioral execution of every simulation.

The final non-normalizing gate pins the released GHA hash, verifies that binary
remains byte-identical throughout the run, and records/enforces matching
before/after hashes for every selected definition against the conversion
manifest.
