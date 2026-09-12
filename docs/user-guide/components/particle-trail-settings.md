# Particle Trail Settings

Set how much recent movement history is retained for particle trails.

**Location:** Nuclei4 → Particles

![](../assets/components/particle-trail-settings-wired.png)


## Use it

Connect trailSettings to the solver and its particles output to Particle Trail Preview. Increasing trail length changes visible movement history, not the diffusion or decay of the deposited field.

## Inputs

| Input | Data | Default | Purpose |
| --- | --- | --- | --- |
| **Trail Size** (`trailSize`) | Integer; item | 5 | Size Of Particle Trail |

Defaults describe a newly placed component. A saved definition can store other values on an unconnected input.

## Outputs

| Output | Data | Purpose |
| --- | --- | --- |
| **Trail Settings** (`trailSettings`) | Text; list | Settings For Particle Trail |

## In the example collection

- [01_Slime Intro](../examples/01-slime-intro.md)
- [02_Gradient Map](../examples/02-gradient-map.md)
- [03_Minimizing Transport Networks 1](../examples/03-minimizing-transport-networks-1.md)

[Back to the component reference](README.md)
