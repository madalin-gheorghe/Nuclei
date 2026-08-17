# Nuclei4.GH2 Design Placeholder

This directory reserves the future GPU-only Grasshopper 2 adapter. It is design
documentation only: there is intentionally no project file, GH2 SDK dependency,
build target, package, component registration, or shipped binary here.

When GH2 integration begins, this adapter will:

- keep the `Nuclei4` product namespace (adapter-local implementation types may
  use `Nuclei4.GH2`);
- translate GH2 inputs into `Nuclei4.Core` and
  `Nuclei4.Gpu.Abstractions` contracts;
- expose demanded results through `Nuclei4.Display.Abstractions` and GH2 output
  types;
- select a concrete backend at the composition boundary: D3D11 on Windows and a
  future Metal implementation on macOS;
- remain GPU-only and reuse the same solver behavior and shader/kernel contracts
  rather than copying them into GH2 components.

It must not replace or alter the current GH1 `Nuclei4.gha`. GH1 component GUIDs,
public model/Goo identities, saved-document schemas, and observable behavior stay
under their existing compatibility contract.

Metal compute/display and the GH2 adapter are not implemented by the current V4
architecture migration. See `../../docs/V4_ARCHITECTURE.md` for the planned
boundaries and migration status.
