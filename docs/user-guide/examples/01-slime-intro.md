# Slime Intro

Follow a complete slime setup from a flat field to a visible particle network.

![Slime Intro: example simulation result.](../assets/examples/01-slime-intro/result.jpg)

[Download 01_Slime Intro.gh](files/01-slime-intro.gh)

![Slime Intro: Grasshopper definition showing its connected components and controls.](../assets/examples/01-slime-intro/definition-clean.png)

## Follow the definition

Start with the [first slime simulation walkthrough](../getting-started/first-slime-simulation.md), which explains each saved group in detail.

<details>
<summary>Components used</summary>

- [Construct Voxels](../components/construct-voxels.md)
- [Nuclei4 Solver GPU](../components/nuclei4-solver-gpu.md)
- [Construct Slime Particles](../components/construct-slime-particles.md)
- [Voxel Settings Slime](../components/voxel-settings-slime.md)
- [Particle Trail Preview](../components/particle-trail-preview.md)
- [Particle Trail Settings](../components/particle-trail-settings.md)

</details>

<details>
<summary>Saved controls</summary>

Slider and value-list settings saved in this definition. Repeated rows belong to different component instances.

| Component | Input | Saved control |
| --- | --- | --- |
| Construct Voxels | X Voxels | 1000 |
| Construct Voxels | Y Voxels | 1000 |
| Construct Voxels | Z Voxels | 1 |
| Nuclei4 Solver GPU | Reset | True |
| Construct Slime Particles | Particle Count | 50000 |
| Construct Slime Particles | Speed | 1.3 |
| Construct Slime Particles | Sensor Distance | 6 |
| Construct Slime Particles | Sensor Angle | 45 |
| Construct Slime Particles | Rotation Angle | 45 |
| Construct Slime Particles | Deposit | 1 |
| Construct Slime Particles | Wander | 0 |
| Voxel Settings Slime | Diffuse Rate | 0.15 |
| Voxel Settings Slime | Decay Rate | 0.03 |
| Voxel Settings Slime | Falloff | 0 |
| Voxel Settings Slime | Diffuse Range | 5 |
| Particle Trail Settings | Trail Size | 10 |

</details>

## Run the example

Open it in Rhino 9 with Nuclei V4, pause the existing Trigger, and inspect the mapped field. Reset once with True, return reset to False, and start the Trigger. Pause before editing several inputs or extracting a large result.

## Try a comparison

Compare Sensor Distance while keeping the other sliders fixed. Look for changes in the organization of local paths.

## Example result

![Slime Intro: example simulation result.](../assets/examples/01-slime-intro/result.jpg)

[Back to examples](README.md)
