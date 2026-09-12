# Extract Particle Vectors

Extract particle direction vectors for inspection or downstream geometry.

**Location:** Nuclei4 → Particles

## Use it

Pair directions with particle positions from the same simulation state. Pause first when comparing those outputs.

## Inputs

| Input | Data | Default | Purpose |
| --- | --- | --- | --- |
| **Particles** (`particles`) | Generic Data; item | Supply input | Input Particles |

Defaults describe a newly placed component. A saved definition can store other values on an unconnected input.

## Outputs

| Output | Data | Purpose |
| --- | --- | --- |
| **Particle Vectors** (`particleVec`) | Vector; list | Particle Vectors |

## Related workflow

Use the input and output roles above with the [voxel-field](../core-concepts/voxels-and-fields.md) or [particle](../core-concepts/particles-and-populations.md) workflow.

[Back to the component reference](README.md)
