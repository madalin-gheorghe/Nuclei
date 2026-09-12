# Voxel Selection Intersection

Keep the cells shared by two voxel selections.

**Location:** Nuclei4 → Environment

## Use it

Supply selections derived from the same environment. Use the intersection to restrict a value map to an overlapping region, then preview the result before connecting the solver.

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
