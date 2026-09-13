# Extract Voxel Vector

Extract the directions stored in a voxel field.

**Location:** Nuclei4 → Environment

![Extract Voxel Vector with its connected controls and wires.](../assets/components/extract-voxel-vector-wired.png)

## Use it

Connect a field containing mapped vectors. Pair **voxelVector** with [Extract Voxel Positions](extract-voxel-positions.md) from the same field to display or use the directions.

## Inputs

Defaults describe a newly placed component.

| Input | Type / access | Default | Meaning |
| --- | --- | --- | --- |
| **Voxels** (`voxels`) | Generic Data / item | Required | Voxel field or selection to use. |

## Output

| Output | Type / access | Meaning |
| --- | --- | --- |
| **Voxel Vectors** (`voxelVector`) | Vector / list | Mapped directions, in voxel order. |

## If something is wrong

| Symptom | Action |
| --- | --- |
| Vectors are empty or zero | Check the upstream vector mapping. |
| Vectors and positions do not match | Use the same field and preserve output order. |

## Continue

[Component reference](README.md)

<details>
<summary>Machine-readable reference (JSON)</summary>

[Download JSON](../reference/components/extract-voxel-vector.json) · [JSON Schema](../reference/component.schema.json) · [How to read this JSON](../reference/reading-json.md)

Component metadata for scripts and AI tools. Indices are zero-based; defaults are display strings. [Full catalog](../reference/component-contracts.json).

```json
{
  "$schema": "../component.schema.json",
  "schemaVersion": 1,
  "pluginVersion": "4.1.0.0",
  "ghaSha256": "700C1620FD839DD1511E67359961787C8EC08EA595812B2EDED27828F96800C5",
  "component": {
    "name": "Extract Voxel Vector",
    "category": "Nuclei4",
    "subcategory": " Environment",
    "componentGuid": "8f334745-181f-4bf2-a60f-4b11bbdfcc8b",
    "dotnetType": "Nuclei4.Voxel_Extractor_Vector",
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
      }
    ],
    "outputs": [
      {
        "index": 0,
        "name": "Voxel Vectors",
        "nickname": "voxelVector",
        "ghType": "Vector",
        "access": "list",
        "optional": false,
        "mapping": "None",
        "defaults": []
      }
    ]
  }
}
```

</details>
