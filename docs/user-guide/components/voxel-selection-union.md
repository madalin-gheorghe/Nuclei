# Voxel Selection Union

Combine two voxel selections into one selection.

**Location:** Nuclei4 → Environment

![](../assets/components/voxel-selection-union-wired.png)


## Use it

Use this to bring separate mapped regions together in one field workflow. Feed selections from the same domain and inspect the property you intend to use downstream.

## Inputs

| Input | Data | Default | Purpose |
| --- | --- | --- | --- |
| **Voxel** (`V1`) | Generic Data; item | Supply input | Connects to Voxels |
| **Voxel** (`V2`) | Generic Data; item | Supply input | Connects to Voxels |

Defaults describe a newly placed component. A saved definition can store other values on an unconnected input.

## Outputs

| Output | Data | Purpose |
| --- | --- | --- |
| **Output Voxels** (`voxels`) | Generic Data; item | Output Voxels |

## In the example collection

- [03_Minimizing Transport Networks 1](../examples/03-minimizing-transport-networks-1.md)
- [04_Minimizing Transport Networks 2](../examples/04-minimizing-transport-networks-2.md)
- [05_City Map](../examples/05-city-map.md)

[Back to the component reference](README.md)
