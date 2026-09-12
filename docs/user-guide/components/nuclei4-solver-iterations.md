# Nuclei4 Solver Iterations

Set a maximum iteration count for the solver. Use a limit to compare runs at the same number of simulation steps.

**Location:** Nuclei4 → Solver

![](../assets/components/nuclei4-solver-iterations-wired.png)


## Use it

Connect the settings output to the solver alongside your other settings. When the limit is reached, a dedicated automatic Trigger can pause. Reset or raise the limit and re-enable the Trigger to continue.

## Inputs

| Input | Data | Default | Purpose |
| --- | --- | --- | --- |
| **Iterations** (`iterations`) | Integer; item | 1 | Maximum Number of Iterations |

Defaults describe a newly placed component. A saved definition can store other values on an unconnected input.

## Outputs

| Output | Data | Purpose |
| --- | --- | --- |
| **Solver Settings** (`solverSettings`) | Text; list | Settings For Solver |

## In the example collection

- [02_Gradient Map](../examples/02-gradient-map.md)
- [03_Minimizing Transport Networks 1](../examples/03-minimizing-transport-networks-1.md)
- [04_Minimizing Transport Networks 2](../examples/04-minimizing-transport-networks-2.md)

[Back to the component reference](README.md)
