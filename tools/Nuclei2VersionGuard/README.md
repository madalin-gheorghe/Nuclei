# Nuclei 2 Rhino version guard

Adds a Rhino 6/7/8/9 loading guard to the original, unsigned Nuclei 2 assembly. The
original V2 source is not available in this repository. The tool accepts only the
published 2.0.0 binary with SHA-256
`A146083ADB37AB945CE7B3F22AA27CB4F08378A73CB7A7ED4C95461D4354C645`.

Run with the original `.gha`, a **new** output path and the compiled banner library:

```powershell
dotnet run --project tools/Nuclei2VersionGuard -c Release -- original/nuclei2.gha output/nuclei2.gha Nuclei-v2-old/Nuclei2.OldBanner/bin/Release/net48/Nuclei2.OldBanner.dll
```

The guard aborts Grasshopper registration outside Rhino 6/7/8/9 and writes guidance
to Rhino's command history. It retains the original assembly version, component
GUIDs, resources and solver code. Before writing, it verifies the serialized
output preserves all existing metadata and method bodies except the new prefix
on `PriorityLoad` and its category short-name argument, changed from `Nuc` to `N2`.
The verifier permits exactly that argument change and preserves the rest of the original body.
One private `IsSupportedRhino(int)` helper enables direct host-version tests.
A second private helper loads the embedded banner from memory only in Rhino 8/9,
once per application domain. The tool verifies the embedded bytes and preserves
every original resource; the output requires no companion GHA or DLL.

This prepares a binary only: it does not install, publish or modify the input.
Yak still permits installing older releases in newer Rhino versions; the guard
prevents loading. Previously published 2.0.0 files are unaffected.

The original guard accepted only Rhino 6/7. This revision also accepts Rhino 8/9.
Use the [Rhino 8 build script](../Build-V2Rhino8.ps1) to build and embed the display-only
**old v2** banner, which activates only in Rhino 8/9. Rhino 6/7 runtime testing
remains outstanding; their original loading path and all component code are
preserved.

Publish changed binaries under a new Yak version; do not reuse published 2.0.4. The original
assembly version remains 1.0.0.0, matching the original public V2 binary; package
and assembly versions are separate. Distribute only the generated `nuclei2.gha`.
Publishing is a separate, explicit step.
Use the package name **Nuclei2**, never the former shared **Nuclei** name, so V2
can be installed alongside the separate **Nuclei3** package.
