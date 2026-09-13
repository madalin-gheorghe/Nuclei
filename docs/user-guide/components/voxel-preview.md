# Voxel Preview

Display a property of the voxel field in the Rhino viewport.

**Location:** Nuclei4 → Preview

![Voxel Preview with its connected controls and wires.](../assets/components/voxel-preview-wired.png)

## Use it

Connect a field to **voxels** and select the property with **Type**. For evolving slime or ant signals, use the solver’s **voxels** output. For a static map, use the mapping component’s output.

Minimum and Maximum Value set the displayed value range. For more detailed 3D previews, right-click and enable **High Resolution (3D)**.

Slime Chemoattractants, the ant pheromone views, and Ants and Slime also show blocked voxels (`maxDensity = 0`) in dark purple. This obstacle layer stays visible independently of the displayed signal range.

These views draw food last, above signals and obstacles. Slime food sources and remaining ant food stay visible independently of the signal thresholds; consumed ant food disappears as the simulation advances.

## Inputs

Defaults describe a newly placed component.

| Input | Type / access | Default | Meaning |
| --- | --- | --- | --- |
| **Voxels** (`voxels`) | Generic Data / item | Required | Voxel field or selection to use. |
| **Type** (`type`) | Integer / item | 0 | Property to use; see **Type choices** below. |
| **Minimum Treshold** (`min`) | Number / item | Optional; 0 | Lower displayed value. |
| **Maximum Treshold** (`max`) | Number / item | Optional; 1 | Upper displayed value. |
| **Colour** (`colour`) | Colour / item | Optional; 0,0,0 (0) | Display color. |

### Type choices

| Value | Choice |
| --- | --- |
| 0 | Minimum Density |
| 1 | Maximum Density |
| 2 | Speed |
| 3 | Sensor Distance |
| 4 | Sensor Angle |
| 5 | Rotation Angle |
| 6 | Slime Food |
| 13 | Ant Food |
| 7 | Slime Chemoattractants |
| 8 | Ant Food Pheromones |
| 9 | Ant Base Pheromones |
| 10 | Ant Pheromones |
| 11 | Ants and Slime |

## Output

Displays directly in the Rhino viewport.

## If something is wrong

| Symptom | Action |
| --- | --- |
| Nothing appears | Check Type, the displayed range, and Grasshopper preview. |
| The preview stays unchanged | Use the solver output when viewing evolving signals. |

## Continue

[Gradient Map](../examples/02-gradient-map.md) · [Minimizing Transport Networks 1](../examples/03-minimizing-transport-networks-1.md) · [Minimizing Transport Networks 2](../examples/04-minimizing-transport-networks-2.md) · [Component reference](README.md)

<details>
<summary>Machine-readable reference (JSON)</summary>

[Download JSON](../reference/components/voxel-preview.json) · [JSON Schema](../reference/component.schema.json) · [How to read this JSON](../reference/reading-json.md)

Component metadata for scripts and AI tools. Indices are zero-based; defaults are display strings. [Full catalog](../reference/component-contracts.json).

```json
{
  "$schema": "../component.schema.json",
  "schemaVersion": 1,
  "pluginVersion": "4.1.0.0",
  "ghaSha256": "700C1620FD839DD1511E67359961787C8EC08EA595812B2EDED27828F96800C5",
  "component": {
    "name": "Voxel Preview",
    "category": "Nuclei4",
    "subcategory": "Preview",
    "componentGuid": "fb2ea9fc-5963-4587-b09b-0422f61174db",
    "dotnetType": "Nuclei4.Preview_Voxel",
    "inputs": [
      {
        "index": 0,
        "name": "Voxels",
        "nickname": "voxels",
        "ghType": "Generic Data",
        "access": "item",
        "optional": false,
        "mapping": "None",
        "defaults": []
      },
      {
        "index": 1,
        "name": "Type",
        "nickname": "type",
        "ghType": "Integer",
        "access": "item",
        "optional": false,
        "mapping": "None",
        "defaults": [
          "0"
        ]
      },
      {
        "index": 2,
        "name": "Minimum Treshold",
        "nickname": "min",
        "ghType": "Number",
        "access": "item",
        "optional": true,
        "mapping": "None",
        "defaults": [
          "0"
        ]
      },
      {
        "index": 3,
        "name": "Maximum Treshold",
        "nickname": "max",
        "ghType": "Number",
        "access": "item",
        "optional": true,
        "mapping": "None",
        "defaults": [
          "1"
        ]
      },
      {
        "index": 4,
        "name": "Colour",
        "nickname": "colour",
        "ghType": "Colour",
        "access": "item",
        "optional": true,
        "mapping": "None",
        "defaults": [
          "0,0,0 (0)"
        ]
      }
    ],
    "outputs": []
  }
}
```

</details>
