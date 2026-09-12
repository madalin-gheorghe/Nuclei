# Ants Complex

Explore multiple food regions with an additional restrictive density map.

![Ants Complex: example simulation result.](../assets/examples/14-ants-complex/result.jpg)

[Download 14_Ants Complex.gh](files/14-ants-complex.gh)

![Ants Complex: Grasshopper definition showing its connected components and controls.](../assets/examples/14-ants-complex/definition-clean.png)

## Follow the definition

Two mapping branches assign **Ant Food** with multiplier 1. A separate branch assigns **Maximum Density** with multiplier 0. Follow the selections and unions carefully to distinguish food from the region that restricts occupancy.

<details>
<summary>Components used</summary>

- [Construct Voxels](../components/construct-voxels.md)
- [Point Attractor for Voxels](../components/point-attractor-for-voxels.md)
- [Curve Attractor for Voxels](../components/curve-attractor-for-voxels.md)
- [Define Voxel Values](../components/define-voxel-values.md)
- [Voxel Selection Union](../components/voxel-selection-union.md)
- [Nuclei4 Solver GPU](../components/nuclei4-solver-gpu.md)
- [Particle Preview](../components/particle-preview.md)
- [Voxel Preview](../components/voxel-preview.md)
- [Construct Ant Particles](../components/construct-ant-particles.md)
- [Voxel Settings Ant](../components/voxel-settings-ant.md)

</details>

<details>
<summary>Saved controls</summary>

Slider and value-list settings saved in this definition. Repeated rows belong to different component instances.

| Component | Input | Saved control |
| --- | --- | --- |
| Construct Voxels | X Voxels | 800 |
| Construct Voxels | Y Voxels | 800 |
| Construct Voxels | Z Voxels | 1 |
| Point Attractor for Voxels | Maximum Range | 5 |
| Curve Attractor for Voxels | Maximum Range | 2 |
| Point Attractor for Voxels | Maximum Range | 50 |
| Point Attractor for Voxels | Maximum Range | 30 |
| Define Voxel Values | Type | Ant Food |
| Define Voxel Values | Multiplier Value | 1 |
| Define Voxel Values | Type | Ant Food |
| Define Voxel Values | Multiplier Value | 1 |
| Define Voxel Values | Type | Maximum Density |
| Define Voxel Values | Multiplier Value | 0 |
| Nuclei4 Solver GPU | Reset | True |
| Voxel Preview | Type | Ants and Slime |
| Construct Ant Particles | Speed | 3 |
| Construct Ant Particles | Sensor Distance | 9 |
| Construct Ant Particles | Sensor Angle | 45 |
| Construct Ant Particles | Rotation Angle | 45 |
| Construct Ant Particles | Deposit | 10 |
| Construct Ant Particles | Wander | 0.15 |
| Voxel Settings Ant | Food Pheromones Diffuse Rate | 0.1 |
| Voxel Settings Ant | Food Decay Rate | 0.0005 |
| Voxel Settings Ant | Base Pheromones Diffuse Rate | 0.1 |
| Voxel Settings Ant | Base Decay Rate | 0.05 |
| Voxel Settings Ant | Diffuse Range | 4 |

</details>

## Run the example

Open it in Rhino 9 with Nuclei V4, pause the existing Trigger, and inspect the mapped field. Reset once with True, return reset to False, and start the Trigger. Pause before editing several inputs or extracting a large result.

## Try a comparison

Change one food region while keeping the restrictive map fixed. Inspect both Types separately before comparing foraging routes.

## Example result

![Ants Complex: example simulation result.](../assets/examples/14-ants-complex/result.jpg)

[Back to examples](README.md)
