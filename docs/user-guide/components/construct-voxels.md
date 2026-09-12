# Construct Voxels

Create the voxel field that defines the simulation's size and resolution.

**Location:** Nuclei4 → Environment

![Construct Voxels with dimension controls, an incoming size input, and a voxel-field output wire.](../assets/components/construct-voxels-wired.png)

## Use it

Place **one Construct Voxels per Grasshopper document**. Connect its **voxels** output to the particle constructor and solver. Insert selection or mapping components where your workflow needs a modified field.

## Inputs

Defaults apply to a newly placed component; saved definitions can store different values on unconnected inputs.

| Input | Type / access | Default | Meaning |
| --- | --- | --- | --- |
| **Voxel Size** (`voxelSize`) | Number / item | 1 | Cell edge length in Rhino model units, equal on all axes. |
| **X Voxels** (`xVoxels`) | Integer / item | 100 | Cells along X. |
| **Y Voxels** (`yVoxels`) | Integer / item | 100 | Cells along Y. |
| **Z Voxels** (`zVoxels`) | Integer / item | 1 | Cells along Z; use 1 for an XY layer. |

All inputs have stored defaults. Counts below 1 become 1; a nonpositive Voxel Size becomes 1. Use positive values deliberately rather than relying on these corrections.

## Output

| Output | Type / access | Meaning |
| --- | --- | --- |
| **Output Voxels** (`voxels`) | Generic Data / item | One Nuclei field containing the grid and its field data. |

This is a **single field object**, not a list of cell centers. Use **Extract Voxel Positions** for ordinary Grasshopper points.

## Size, origin, and ordering

The domain begins at the world origin. Its extents are `X × Voxel Size`, `Y × Voxel Size`, and `Z × Voxel Size`. Cell `(x,y,z)` has center `((x+0.5), (y+0.5), (z+0.5)) × Voxel Size`. A one-cell XY layer with size 1 therefore has centers at Z = 0.5.

The full-grid count is `X × Y × Z`. Doubling all three dimensions makes eight times as many cells. For per-cell maps, follow the [Define Voxel Values ordering rules](define-voxel-values.md#value-count-and-order).

## If something is wrong

| Symptom | Action |
| --- | --- |
| A second constructor disappears with an error dialog | Reuse the existing constructor; only one is allowed. |
| Geometry does not line up with the field | Check world coordinates, model units, and field extents. |
| Solver retains fewer particles than requested | Check boundaries and excluded cells; see [particle generation](construct-slime-particles.md#requested-and-retained-particles). |
| Increasing 3D resolution makes the graph slow | Check the multiplied cell count before increasing all dimensions. |

## Continue

[Slime Intro](../getting-started/first-slime-simulation.md) · [Gradient Map](../examples/02-gradient-map.md) · [Component reference](README.md)

<details>
<summary>Machine-readable reference (JSON)</summary>

Runtime output: `Nuclei4.VoxelField`.

Component metadata for scripts and AI tools. Indices are zero-based; defaults are display strings. This describes the component, not an executable API. [Full catalog](../reference/component-contracts.json).

```json
{
  "schemaVersion": 1,
  "pluginVersion": "4.1.0.0",
  "ghaSha256": "700C1620FD839DD1511E67359961787C8EC08EA595812B2EDED27828F96800C5",
  "component": {
    "name": "Construct Voxels",
    "category": "Nuclei4",
    "subcategory": " Environment",
    "componentGuid": "a3940a4d-9015-411c-9ffa-e38ecc90d394",
    "dotnetType": "Nuclei4.VoxelConstructor",
    "inputs": [
      {
        "index": 0,
        "name": "Voxel Size",
        "nickname": "voxelSize",
        "ghType": "Number",
        "access": "item",
        "optional": false,
        "mapping": "None",
        "defaults": [
          "1"
        ]
      },
      {
        "index": 1,
        "name": "X Voxels",
        "nickname": "xVoxels",
        "ghType": "Integer",
        "access": "item",
        "optional": false,
        "mapping": "None",
        "defaults": [
          "100"
        ]
      },
      {
        "index": 2,
        "name": "Y Voxels",
        "nickname": "yVoxels",
        "ghType": "Integer",
        "access": "item",
        "optional": false,
        "mapping": "None",
        "defaults": [
          "100"
        ]
      },
      {
        "index": 3,
        "name": "Z Voxels",
        "nickname": "zVoxels",
        "ghType": "Integer",
        "access": "item",
        "optional": false,
        "mapping": "None",
        "defaults": [
          "1"
        ]
      }
    ],
    "outputs": [
      {
        "index": 0,
        "name": "Output Voxels",
        "nickname": "voxels",
        "ghType": "Generic Data",
        "access": "item",
        "optional": false,
        "mapping": "None",
        "defaults": []
      }
    ]
  }
}
```

</details>
