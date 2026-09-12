# Particle Settings Slime Ant Interaction

Control how slime and ant signals influence the other particle type. Use it in a mixed simulation with both populations.

**Location:** Nuclei4 → Particles

![](../assets/components/particle-settings-slime-ant-interaction-wired.png)

## Use it

Connect its settings output to the solver. The three inputs control slime response to food pheromone, slime response to base pheromone, and ant response to slime signal. Values range from zero to one; compare each interaction separately.

## Inputs

| Input | Data | Default | Purpose |
| --- | --- | --- | --- |
| **Slime -> Ant Food** (`slime ~ antFood`) | Number; item | 0.5 | Interaction between SLIME Particles and ANT FOOD Pheromones. VALUES FROM 0 TO 1 |
| **Slime -> Ant Base** (`slime ~ antBase`) | Number; item | 0.5 | Interaction between SLIME Particles and ANT BASE Pheromones. VALUES FROM 0 TO 1 |
| **Ant -> Slime** (`ant ~ slime`) | Number; item | 0.5 | Interaction between ANT Particles and SLIME Chemoattractants. VALUES FROM 0 TO 1 |

Defaults describe a newly placed component. A saved definition can store other values on an unconnected input.

## Outputs

| Output | Data | Purpose |
| --- | --- | --- |
| **Interaction Settings** (`interactionSettings`) | Text; list | Settings Controlling Species Interactions |

## Related workflow

Use the input and output roles above with the [voxel-field](../core-concepts/voxels-and-fields.md) or [particle](../core-concepts/particles-and-populations.md) workflow.

[Back to the component reference](README.md)
