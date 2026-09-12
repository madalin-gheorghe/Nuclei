# Voxel Selection Difference

Remove the second voxel selection from the first.

**Location:** Nuclei4 → Environment

## Use it

Input order matters: V1 minus V2 is different from V2 minus V1. Use selections from the same environment and inspect the resulting region.

## Inputs

| Input | Data | Default | Purpose |
| --- | --- | --- | --- |
| **Voxel** (`V1`) | Generic Data; item | Supply input | Connects to Voxels |
| **Voxel** (`V2`) | Generic Data; item | Supply input | Connects to Voxels |

Defaults describe a newly placed component. A saved definition can store other values on an unconnected input.

## Outputs

| Output | Data | Purpose |
| --- | --- | --- |
| **Output Voxels** (`voxels`) | Generic Data; item | Output Voxels |

## Related workflow

Use the input and output roles above with the [voxel-field](../core-concepts/voxels-and-fields.md) or [particle](../core-concepts/particles-and-populations.md) workflow.

[Back to the component reference](README.md)
