# Extract Particle Neighbour Count

Read the neighbor count stored for each particle by the division rule.

**Location:** Nuclei4 → Particles

![Extract Particle Neighbour Count with its connected controls and wires.](../assets/components/extract-particle-neighbour-count-wired.png)

## Use it

Connect the solver’s **particles** output. Use **particleNC** to inspect the neighborhood information used by particle division. The counting range is set in [Particle Division Settings](particle-division-settings.md).

## Inputs

Defaults describe a newly placed component.

| Input | Type / access | Default | Meaning |
| --- | --- | --- | --- |
| **Particles** (`particles`) | Generic Data / item | Required | Current particle collection from the solver. |

## Output

| Output | Type / access | Meaning |
| --- | --- | --- |
| **Particle Neighbour Count** (`particleNC`) | Number / list | Stored division-neighborhood count for each particle. |

## If something is wrong

| Symptom | Action |
| --- | --- |
| Counts stay unchanged | Check whether the division rule is enabled and when it last updated. |

## Continue

[Component reference](README.md)

<details>
<summary>Machine-readable reference (JSON)</summary>

[Download JSON](../reference/components/extract-particle-neighbour-count.json) · [JSON Schema](../reference/component.schema.json) · [How to read this JSON](../reference/reading-json.md)

Component metadata for scripts and AI tools. Indices are zero-based; defaults are display strings. [Full catalog](../reference/component-contracts.json).

```json
{
  "$schema": "../component.schema.json",
  "schemaVersion": 1,
  "pluginVersion": "4.1.0.0",
  "ghaSha256": "700C1620FD839DD1511E67359961787C8EC08EA595812B2EDED27828F96800C5",
  "component": {
    "name": "Extract Particle Neighbour Count",
    "category": "Nuclei4",
    "subcategory": " Particles",
    "componentGuid": "d077c7a9-1db6-410c-87f9-917a0c3a353d",
    "dotnetType": "Nuclei4.Particle_Extractor_NeighbourCount",
    "inputs": [
      {
        "index": 0,
        "name": "Particles",
        "nickname": "particles",
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
        "name": "Particle Neighbour Count",
        "nickname": "particleNC",
        "ghType": "Number",
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
