# Define Discrete Vectors

Provide a set of discrete direction vectors as solver settings.

**Location:** Nuclei4 → Environment

![](../assets/components/define-discrete-vectors-wired.png)

## Use it

The supplied vectors are normalized by the component. Connect its settings output to the solver. Use meaningful nonzero directions; this tool produces settings rather than a voxel-field output.

## Inputs

| Input | Data | Default | Purpose |
| --- | --- | --- | --- |
| **Voxel Discrete Vectors** (`discreteVectors`) | Vector; list | Supply input | Vector Assigned to Voxel |

Defaults describe a newly placed component. A saved definition can store other values on an unconnected input.

## Outputs

| Output | Data | Purpose |
| --- | --- | --- |
| **Discrete Vector Settings** (`discreteSettings`) | Text; list | Settings For Discrete Vectors |

## Related workflow

Use the input and output roles above with the [voxel-field](../core-concepts/voxels-and-fields.md) or [particle](../core-concepts/particles-and-populations.md) workflow.

[Back to the component reference](README.md)
