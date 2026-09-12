# Extract Parent Voxel

Find the voxel associated with supplied points in a field.

**Location:** Nuclei4 → Utility

## Use it

Use points in the same coordinate system as the voxel environment. Keep the field connected so the component can relate positions to the correct grid.

## Inputs

| Input | Data | Default | Purpose |
| --- | --- | --- | --- |
| **Points** (`points`) | Point; tree | Supply input | Input Points |
| **Voxels** (`voxels`) | Generic Data; item | Supply input | Connects to Voxel Constructor |

Defaults describe a newly placed component. A saved definition can store other values on an unconnected input.

## Outputs

| Output | Data | Purpose |
| --- | --- | --- |
| **Point Parent Voxel** (`parentVoxel`) | Number; list | Point Parent Voxel |

## Related workflow

Use the input and output roles above with the [voxel-field](../core-concepts/voxels-and-fields.md) or [particle](../core-concepts/particles-and-populations.md) workflow.

[Back to the component reference](README.md)
