# V3 Yak package

Run after committing the V3 source and packaging icon:

```powershell
pwsh -NoProfile -File tools/Build-V3YakPackage.ps1 -Version 3.3.1
```

Requires Git, the SDK selected by `global.json`, package restore access, and Rhino 8's Yak CLI. An alternative CLI path can be supplied with `-YakPath`.

The script exports the committed source into a unique ignored `.publish-work` directory, builds both runtime variants, and creates `nuclei-<version>-rh8_0-any.yak`. It never installs or publishes. The package contains:

- `net48/`: the original Windows .NET Framework build and its runtime dependencies.
- `net7.0/Nuclei3.gha`: the portable modern Windows/Mac build, with the original PNG icons embedded directly and no companion DLLs.
- `manifest.yml` and the approved package icon.

The portable build uses Rhino's Drawing and Windows Forms implementations. This is a packaging adaptation of the same V3 source; component identifiers stay unchanged. Runtime and icon checks, including Mac validation when available, should precede publication.

`build-provenance.json` records the source commit, source and package hashes, icon mappings, and packaging adaptations. The icon is an unmodified copy from the approved Nuclei 3.0.0 Yak package (SHA-256 `B2723987CC1B3F1B916072FA668077170095565635B8C49A8E0C505D8429A16A`).
