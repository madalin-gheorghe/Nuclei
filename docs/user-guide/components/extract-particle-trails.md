# Extract Particle Trails

Extract the points of recorded particle trails for further curve or path construction.

**Location:** Nuclei4 → Particles

## Use it

Connect the solver’s particles output and provide trail settings to retain history. The output is organized by particle group and particle in a data tree. Preserve branches when building polylines so unrelated trails are not connected.

## Inputs

| Input | Data | Default | Purpose |
| --- | --- | --- | --- |
| **Particles** (`particles`) | Generic Data; item | Supply input | Input Particles |

Defaults describe a newly placed component. A saved definition can store other values on an unconnected input.

## Outputs

| Output | Data | Purpose |
| --- | --- | --- |
| **Particle Positions** (`trailPos`) | Point; list | Particle Positions |

Although its parameter is registered as a list, trail points are emitted as a tree with a branch for each particle.

## Related workflow

Use the input and output roles above with the [voxel-field](../core-concepts/voxels-and-fields.md) or [particle](../core-concepts/particles-and-populations.md) workflow.

[Back to the component reference](README.md)
