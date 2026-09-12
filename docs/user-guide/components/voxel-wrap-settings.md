# Voxel Wrap Settings

Choose whether the simulation wraps at its boundaries.

**Location:** Nuclei4 → Environment

![Voxel Wrap Settings in the saved 02_Gradient Map definition.](../assets/components/voxel-wrap-settings.png)

*Captured from 02_Gradient Map.gh, preserving its component position and connected controls. Example values can differ from the fresh-component defaults below.*

## Use it

Connect the settings to the solver. Compare wrapped and non-wrapped boundaries from a fresh reset to understand their effect on movement at the domain edges.

## Inputs

| Input | Data | Default | Purpose |
| --- | --- | --- | --- |
| **Wrap** (`wrap`) | Boolean; item | False | Boundary Conditions |

Defaults describe a newly placed component. A saved definition can store other values on an unconnected input.

## Outputs

| Output | Data | Purpose |
| --- | --- | --- |
| **Wrap Settings** (`wrapSettings`) | Text; list | Settings for the Boundary Condition |

## In the example collection

- [02_Gradient Map](../examples/02-gradient-map.md)
- [05_City Map](../examples/05-city-map.md)
- [05_City Map2](../examples/05-city-map2.md)

[Back to the component reference](README.md)
