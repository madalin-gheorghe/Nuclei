# Nuclei 2 — Rhino 8/9 compatibility and old v2 banner

GitHub release **v2.0.5** matches **Nuclei2 2.0.5** on Yak.
The [ready-to-install GHA](release/nuclei2.gha), manifest and icon in `release/`
are unchanged from the published package. GHA SHA-256:
`BF12A1EB0C5832413E38BEF5ED7C2AC3199097820B25238F7AAA36F99A2FC9E4`.
The historical assembly version stays 1.0.0.0 to preserve binary identity;
2.0.5 is the distribution version. Examples are in [Nuclei Definitions/v2](../Nuclei%20Definitions/v2).

Nuclei 2 loads in Rhino 6, 7, 8 and 9. In Rhino 8 and 9 only,
the single `nuclei2.gha` paints a red **old v2** strip above every Grasshopper
component whose runtime assembly name is `Nuclei2`. Rhino 6/7 keep their
original appearance. Other Rhino versions remain blocked by the version guard.
The compact Grasshopper category tab reads **N2** (previously **Nuc**).
On Rhino 8/9, installed Nuclei tabs are kept together as **N2, N3, N4**, preserving
the relative order of other plug-in tabs and the selected tab. This uses GH1's
exposed ribbon-tab list because it has no dedicated reordering API.

The original V2 source is unavailable. The [version guard](../tools/Nuclei2VersionGuard/README.md)
patches only the loading hook of the verified original 2.0.0 binary. Component
GUIDs, original resources, serialization and solver code are preserved. The banner
library is embedded as a private resource and loaded directly into memory only in
Rhino 8/9. No companion file is installed or extracted. It does not replace component
attributes or participate in solving. It targets .NET Framework 4.8 for the modern and legacy Windows
runtimes. macOS has not been validated.

Build a fresh distribution directory from the original published binary:

```powershell
./tools/Build-V2Rhino8.ps1 -OriginalGha C:/path/to/original/nuclei2.gha -OutputDirectory C:/Nuclei/output/Nuclei2-Rhino8-OLD
```

Install only `nuclei2.gha` in Grasshopper's Libraries folder and restart Rhino.
Replace any existing Nuclei2 installation to avoid duplicate registrations.
Remove `Nuclei2.OldBanner.gha` if you installed the previous two-file build.
The same single file also supports Rhino 6/7 without loading the embedded banner.

This builds local files only. It does not install or publish a release.
The N2 abbreviation and tab grouping changes are included in Yak 2.0.5.

Published on Yak as **Nuclei2 2.0.5** on 2026-09-10, with Windows distributions
`rh6_10-win`, `rh7_0-win`, `rh8_0-win` and `rh9_0-win`. All four contain the same single GHA.
Search for **Nuclei2** in Package Manager to install this legacy build.
The package contains only `nuclei2.gha`, the manifest and the package icon.
The former shared **Nuclei** listings are hidden. **Nuclei2** and **Nuclei3** are
separate packages so both can be installed and updated independently. Uninstall
the old shared **Nuclei** package before installing the separate packages, then
restart Rhino. V2 shows **old v2** in Rhino 8/9 only.

The current build was validated on 2026-09-10 in isolated Windows Rhino
8 and 9 hosts: each registered all 29 objects and rendered **old v2** above all
28 components, without a companion file. Direct version checks accept exactly
Rhino 6/7/8/9. Binary verification preserved 63 types, 459 unchanged original
methods, the original loading-hook body except its Nuc-to-N2 tab abbreviation,
resources and metadata. Full simulation
and Rhino 6/7 runtime regression tests were not run.
The updated tab behavior also passed seven ordering cases in both Rhino 8 and 9,
including missing versions, repeated grouping, and preservation of the selected
tab and existing tab objects.
