# Troubleshooting

Pause the Trigger before diagnosing a problem. Read the component's runtime message, then check the part of the graph that supplies it.

## Components are missing

Confirm that you are running Rhino 9 on Windows and have installed Nuclei V4. Check for the Nuclei4 tab and use V4 example files. If you installed manually as well as through Package Manager, check for duplicate plugin copies.

## The simulation does not move

Reset once with **True**, then return reset to **False**. Start the existing Trigger and check that it targets the solver. Holding reset True reinitializes the simulation on every solution.

If a run stops at Max Iterations, the solver may pause its dedicated Trigger. Reset or raise the limit and re-enable the Trigger to continue.

## No particles or trails are visible

Check the requested particle count and voxel input. Connect the preview to the solver's particles output and enable Grasshopper preview. Zoom to the field in Rhino. Trail preview needs several steps of movement history after reset.

## A map looks unchanged

Check the selected Type. The value you map and the value you preview must refer to the same property. Use the solver's voxels output to inspect evolving density or pheromones; the constructor output represents the input environment.

## The model is slow

Reduce the field dimensions and particle count independently to find which dominates. In 3D, doubling X, Y, and Z multiplies the cell count by eight. Pause expensive extraction and meshing while exploring the live simulation, and avoid recomputing downstream geometry unnecessarily.

GPU acceleration does not remove the cost of previewing, converting, or passing large amounts of geometry through Grasshopper.

## The volume conversion stays unchanged

**Nuclei4 to Dendro Volume** keeps its last result while Update is False. Set Update True to rebuild from incoming data, then return it to False when you want to retain a result. Check Type, Iso Value, and Maximum Elements if no useful volume appears.

## Image mapping fails

Image Mapper for Voxels accepts planar fields. Use a 2D field rather than a 3D volume. Double-click the component to choose an image, check Type, and set the target range. The image's aspect ratio and the field's aspect ratio affect the mapped appearance.

## Reporting a problem

Include the Rhino version, Nuclei version, the exact runtime message, and a small definition that reproduces the issue. A screenshot of the relevant group helps show the wiring and values. [Report the issue on GitHub](https://github.com/madalin-gheorghe/Nuclei/issues).
