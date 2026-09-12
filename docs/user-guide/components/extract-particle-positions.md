# Extract Particle Positions

Extract particle positions as ordinary Rhino points.

**Location:** Nuclei4 → Particles

![](../assets/components/extract-particle-positions-wired.png)

## Use it

Use the solver output for current positions. Extraction can add data transfer and downstream computation, so use Particle Preview for interactive viewing when you do not need point geometry.

## Inputs

| Input | Data | Default | Purpose |
| --- | --- | --- | --- |
| **Particles** (`particles`) | Generic Data; item | Supply input | Input Particles |

Defaults describe a newly placed component. A saved definition can store other values on an unconnected input.

## Outputs

| Output | Data | Purpose |
| --- | --- | --- |
| **Particle Positions** (`particlePos`) | Point; list | Particle Positions |

## Related workflow

Use the input and output roles above with the [voxel-field](../core-concepts/voxels-and-fields.md) or [particle](../core-concepts/particles-and-populations.md) workflow.

[Back to the component reference](README.md)
