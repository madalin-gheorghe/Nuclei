# Construct Slime Particles

Create a slime population that senses and reinforces a shared environmental signal.

**Location:** Nuclei4 → Particles

![](../assets/components/construct-slime-particles-wired.png)


## Use it

Connect voxels, then supply initial positions or a positive count. The default count is zero. Send the group to the solver; use its output for previewing the evolving particles. Compare Sensor Distance, Sensor Angle, Rotation Angle, and Wander one at a time.

## Inputs

| Input | Data | Default | Purpose |
| --- | --- | --- | --- |
| **Voxel Field** (`voxels`) | Generic Data; item | Supply input | Voxel field used for internal particle generation |
| **Initial Particle Positions** (`particlePos`) | Point; list | Optional | Initial Particle Positions |
| **Particle Count** (`count`) | Integer; item | 0 | Number of particles to generate at random voxel centers when no initial positions are supplied |
| **Speed** (`speed`) | Number; item | 1.3 | Speed of particle movement |
| **Sensor Distance** (`sensorDistance`) | Number; item | 6 | Maximum distance for sensing surrounding voxel values |
| **Sensor Angle** (`sensorAngle`) | Number; item | 45 | Angle of sensing surrounding voxel values |
| **Rotation Angle** (`rotationAngle`) | Number; item | 45 | Angle of rotation for the particles |
| **Deposit** (`deposit`) | Number; item | 1 | The Amount of Chemoattractants Each Particle Deposits in the Environment |
| **Wander** (`wander`) | Number; item | 0 | Frequency of random directions from 0 (off) to 1 (most frequent) |
| **Colour** (`colour`) | Colour; item | 220,255,0 (125) | The Display Color of The Particles |

Defaults describe a newly placed component. A saved definition can store other values on an unconnected input.

## Outputs

| Output | Data | Purpose |
| --- | --- | --- |
| **Output Particle Group** (`particles`) | Particle Group; item | OutputParticles |

## In the example collection

- [01_Slime Intro](../examples/01-slime-intro.md)
- [02_Gradient Map](../examples/02-gradient-map.md)
- [03_Minimizing Transport Networks 1](../examples/03-minimizing-transport-networks-1.md)

[Back to the component reference](README.md)
