# Voxel Values Blend

Blend a chosen scalar property across nearby voxels.

**Location:** Nuclei4 → Environment

![Voxel Values Blend in the saved 07_Attractor Curves 2 definition.](../assets/components/voxel-values-blend.png)

*Captured from 07_Attractor Curves 2.gh, preserving its component position and connected controls. Example values can differ from the fresh-component defaults below.*

## Use it

Use it to soften transitions in a mapped field before simulation. Choose Type, blend strength, neighborhood range, and number of passes. This preprocessing is separate from the solver’s ongoing diffusion and decay.

## Inputs

| Input | Data | Default | Purpose |
| --- | --- | --- | --- |
| **Voxels** (`voxels`) | Generic Data; item | Supply input | Connects to Voxel Constructor |
| **Type** (`type`) | Integer; item | 0 | Type of Voxel Value |
| **Blend Strength** (`blendStrength`) | Number; item | 0.25 | Strength of Blend. VALUES BETWEEN 0 AND 1 |
| **Blend Range** (`range`) | Integer; item | 1 | The Range of Blend |
| **Blend Iterations** (`iterations`) | Integer; item | 1 | Blend Number of Iterations |
| **Wrap Blend** (`wrap`) | Boolean; item | False | Boundary conditions |

Defaults describe a newly placed component. A saved definition can store other values on an unconnected input.

## Outputs

| Output | Data | Purpose |
| --- | --- | --- |
| **Output Voxels** (`voxels`) | Generic Data; item | Output Voxels |

## In the example collection

- [07_Attractor Curves 2](../examples/07-attractor-curves-2.md)

[Back to the component reference](README.md)
