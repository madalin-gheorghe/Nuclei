# Define Voxel Vectors

Assign movement-influencing vectors to voxels.

**Location:** Nuclei4 → Environment

![](../assets/components/define-voxel-vectors-wired.png)


## Use it

Supply vectors corresponding to the selected cells. Frequency controls how often the vector field influences movement; lower values increase its influence according to the component’s input definition. Inspect directions before running the solver.

## Inputs

| Input | Data | Default | Purpose |
| --- | --- | --- | --- |
| **Voxels** (`voxels`) | Generic Data; item | Supply input | Connects to Voxel Constructor |
| **Voxel Vector** (`vector`) | Vector; list | Supply input | Vector Assigned to Voxel |
| **Frequency** (`frequency`) | Integer; list | 1 | The Smaller the Frequency, the Bigger the Vectorfield Impact on Simulation |

Defaults describe a newly placed component. A saved definition can store other values on an unconnected input.

## Outputs

| Output | Data | Purpose |
| --- | --- | --- |
| **Output Voxels** (`voxels`) | Generic Data; item | Output Voxels |

## In the example collection

- [08_Vector Fields 1](../examples/08-vector-fields-1.md)
- [09_Vector Fields 2](../examples/09-vector-fields-2.md)

[Back to the component reference](README.md)
