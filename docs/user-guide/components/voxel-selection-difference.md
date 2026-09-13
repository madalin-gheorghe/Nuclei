# Voxel Selection Difference

Remove one voxel selection from another.

**Location:** Nuclei4 → Environment

![Voxel Selection Difference with its connected controls and wires.](../.gitbook/assets/voxel-selection-difference-wired.png)

## Use it

Connect the selection to keep to **V1** and the selection to remove to **V2**. The **voxels** output contains V1 without its overlap with V2.

## Inputs

Defaults describe a newly placed component.

| Input            | Type / access       | Default  | Meaning                 |
| ---------------- | ------------------- | -------- | ----------------------- |
| **Voxel** (`V1`) | Generic Data / item | Required | First voxel selection.  |
| **Voxel** (`V2`) | Generic Data / item | Required | Second voxel selection. |

## Output

| Output                       | Type / access       | Meaning                           |
| ---------------------------- | ------------------- | --------------------------------- |
| **Output Voxels** (`voxels`) | Generic Data / item | Selected or modified voxel field. |

## If something is wrong

| Symptom                  | Action                        |
| ------------------------ | ----------------------------- |
| The wrong region remains | Check the order of V1 and V2. |
| Output is empty          | V2 may cover all of V1.       |

## Continue

[Component reference](./)

<details>

<summary>Machine-readable reference (JSON)</summary>

[Download JSON](../reference/components/voxel-selection-difference.json) · [JSON Schema](../reference/component.schema.json) · [How to read this JSON](../reference/reading-json.md)

Component metadata for scripts and AI tools. Indices are zero-based; defaults are display strings. [Full catalog](../reference/component-contracts.json).

```json
{
  "$schema": "../component.schema.json",
  "schemaVersion": 1,
  "pluginVersion": "4.1.0.0",
  "ghaSha256": "700C1620FD839DD1511E67359961787C8EC08EA595812B2EDED27828F96800C5",
  "component": {
    "name": "Voxel Selection Difference",
    "category": "Nuclei4",
    "subcategory": " Environment",
    "componentGuid": "9fb92daa-e99b-4ac3-985a-b985ad7bcf62",
    "dotnetType": "Nuclei4.Voxels_AND_NOT",
    "inputs": [
      {
        "index": 0,
        "name": "Voxel",
        "nickname": "V1",
        "ghType": "Generic Data",
        "access": "item",
        "optional": false,
        "mapping": "None",
        "defaults": []
      },
      {
        "index": 1,
        "name": "Voxel",
        "nickname": "V2",
        "ghType": "Generic Data",
        "access": "item",
        "optional": false,
        "mapping": "None",
        "defaults": []
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
