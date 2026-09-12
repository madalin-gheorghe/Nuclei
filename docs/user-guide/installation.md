# Installation & compatibility

This guide uses **Nuclei V4 in Rhino 9 on Windows**. V4 runs its simulation on the GPU and provides GPU previews for working with particles and voxel fields.

## Choose the matching version

| Version | Environment | Use with this guide |
| --- | --- | --- |
| V4 — GPU | Rhino 9, Windows | Yes: use the Nuclei4 components and V4 examples. |
| V3 — CPU | Rhino 8/9 | Earlier version; component layouts and behavior can differ. |
| V2 — Legacy | Rhino 6–9, Windows | Legacy examples and workflows. |

## Install with Rhino Package Manager (recommended)

1. Open **Rhino 9 on Windows**.
2. Run the **PackageManager** command.
3. Search for **Nuclei** and select the **V4** package. Check the version before installing; Nuclei2 and Nuclei3 are the older editions.
4. Install the package and restart Rhino when prompted.
5. Open Grasshopper and check for the **Nuclei4** tab.

## Install from Food4Rhino

Alternatively, visit [Nuclei on Food4Rhino](https://www.food4rhino.com/en/app/nuclei), choose the V4 download for Rhino 9, and follow the instructions supplied with that download. Restart Rhino after installation, then check the Nuclei4 tab.

Use one installation route for a given version to avoid duplicate plugin copies. If the components are missing, first confirm that Rhino 9 loaded the V4 package.

## Check your setup

Open Grasshopper in Rhino 9 and look for the **Nuclei4** tab. Confirm that you can find these components:

- **Construct Voxels**
- **Construct Slime Particles**
- **Nuclei4 Solver GPU**
- **Particle Trail Preview**

Use the V4 examples rather than examples from the V2 or V3 folders. If Grasshopper reports missing components when opening a definition, check the installed version before continuing.

If the solver reports an error, read its runtime message and include that message when asking for help. The current solver exposes particles and voxels outputs; there is no status output.

## Ready to start

Continue to [Your first slime simulation](getting-started/first-slime-simulation.md). It follows the original Slime Intro definition, including its saved groups, controls, and flat simulation field.
