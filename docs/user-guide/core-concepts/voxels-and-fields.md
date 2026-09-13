# Voxels and fields

A **voxel** is one cell of Nuclei's environment. The voxel field gives particles a place to move and stores values that influence their behavior.

## Build the domain

**Construct Voxels** sets cell size and cell counts along X, Y, and Z. The number of cells is X × Y × Z. Keep Z at 1 for an XY study; increasing all three dimensions creates a volume.

![Construct voxels with connected controls and wires.](../.gitbook/assets/construct-voxels-clean.png)

Voxel Size sets each cell’s edge length in model units. The grid starts at the world origin; its dimensions are the cell counts multiplied by Voxel Size.

## Select cells before assigning values

An attractor selects cells near points, curves, meshes, or a function surface. Selection operations combine those sets: union keeps either selection, intersection keeps their overlap, and difference removes one from another.

A selection does not itself specify food or speed. Connect it to **Define Voxel Values** and choose the property you want to assign. Preview the same property to inspect the result before running the solver.

## Static maps and evolving signals

Maps describe local conditions such as speed multipliers, sensing settings, food, and density limits. Evolving signals include slime chemoattractants and ant pheromones, which change as particles move and the field diffuses and decays.

For moving or evolving results, use the solver's **voxels** output. A preview connected to the original input field shows that input, not the solver's changing state.

## Food has two meanings

**Slime Food** is a stationary signal that slime senses. Slime does not carry it home. **Ant Food** is consumed by ants; it also emits food pheromone so ants can detect it before arriving.

Choose the named Type from the component's value list. Keep Type consistent between mapping, preview, and extraction. Numerical values alone do not tell you which field you are looking at.

## A useful working order

Create the field, select cells, assign values, preview the mapped field, and only then connect the solver. This lets you distinguish a mapping issue from a simulation issue.

See [the component reference](../components/) for inputs, outputs, and examples of each tool.
