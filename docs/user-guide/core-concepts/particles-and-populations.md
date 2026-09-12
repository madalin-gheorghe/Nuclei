# Particles and populations

A particle is a moving agent. A **particle group** collects agents that share behavior settings, such as speed, sensor distance, deposit, and color.

## Create a group

Connect the voxel field to the particle constructor. **Construct Slime Particles** accepts initial positions or a positive Particle Count for generated positions. **Construct Ant Particles** requires starting points in the nest region; those points determine the starting population and remembered home positions.

![Construct slime with connected controls and wires.](../assets/construct-slime-clean.png)

The constructor defines starting conditions. The solver's **particles** output carries the evolving simulation. Connect previews and particle extractors to that output.

## What particles sense

Sensor Distance controls how far ahead particles sample. Sensor Angle controls the spread of their sensing directions. Rotation Angle controls their steering response. Changing any one can alter the emerging pattern; compare one setting at a time.

**Deposit** sets the signal added after a successful move into another voxel. **Wander** introduces random changes of direction. Slime and ants interpret their environments differently; read [Slime behavior](slime-behavior.md) and [Ant behavior](ant-behavior.md) for the full loops.

## Multiple groups

Several particle constructors can feed the same solver. Keep their shared voxel environment consistent. Different colors help you identify groups in previews, while different sensing and movement settings let you compare behaviors within one field.

## Population changes

Division, death, and population settings are separate controls. Division and death use local conditions, while population settings include population bounds and random division/death controls. Feed their settings outputs to the solver along with the environment settings.

Particles share the available voxel space. Crowding can restrict movement and prevent new particles from occupying a cell.

## Particle trails and deposited signals

A particle trail is its recent movement history. The deposited field is shared environmental data. Trail Size changes the visible history; diffusion and decay change the shared signal. They serve different purposes and have separate settings.

Pause before extracting large numbers of points or trails. Geometry extraction brings data into ordinary Grasshopper outputs and can add work beyond the simulation itself.
