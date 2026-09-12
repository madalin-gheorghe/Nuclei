# Particle Death Settings

Remove particles when their age and neighborhood meet the death conditions.

**Location:** Nuclei4 → Particles

![Particle Death Settings with its connected controls and wires.](../assets/components/particle-death-settings-wired.png)

## Use it

Enable **Die** and connect **dieSettings** to the solver’s **settings** input. Eligible particles die when their neighbor count is below **Minimum Neighbours** or above **Maximum Neighbours**. Counts equal to either limit are inside the range. **Frequency** sets how often the rule is checked.

## Inputs

Defaults describe a newly placed component.

| Input | Type / access | Default | Meaning |
| --- | --- | --- | --- |
| **Die** (`die`) | Boolean / item | False | Enable neighborhood-based death. |
| **Minimum Age** (`minAge`) | Integer / item | 10 | Minimum particle age, in simulation steps. |
| **Die Range** (`dieRange`) | Integer / item | 3 | Neighborhood range used to count particles. |
| **Minimum Neighbours** (`minN`) | Integer / item | 0 | Eligible particles die if they have fewer neighbors than this. |
| **Maximum Neighbours** (`maxN`) | Integer / item | 10 | Eligible particles die if they have more neighbors than this. |
| **Frequency** (`dieFrequency`) | Integer / item | 5 | Check the death rule every this many simulation steps. |

## Output

| Output | Type / access | Meaning |
| --- | --- | --- |
| **Death Settings** (`dieSettings`) | Text / list | Death settings for the solver. |

## If something is wrong

| Symptom | Action |
| --- | --- |
| Population does not shrink | Check Die, Minimum Age, neighbor limits, and population limits. |

## Continue

[Growth 1](../examples/11-growth-1.md) · [Growth 2](../examples/12-growth-2.md) · [Component reference](README.md)

<details>
<summary>Machine-readable reference (JSON)</summary>

Component metadata for scripts and AI tools. Indices are zero-based; defaults are display strings. [Full catalog](../reference/component-contracts.json).

```json
{
  "schemaVersion": 1,
  "pluginVersion": "4.1.0.0",
  "ghaSha256": "700C1620FD839DD1511E67359961787C8EC08EA595812B2EDED27828F96800C5",
  "component": {
    "name": "Particle Death Settings",
    "category": "Nuclei4",
    "subcategory": " Particles",
    "componentGuid": "b8f690ec-1e23-46c8-8fa4-4d3369cacfdf",
    "dotnetType": "Nuclei4.Particle_Settings_Death",
    "inputs": [
      {
        "index": 0,
        "name": "Die",
        "nickname": "die",
        "ghType": "Boolean",
        "access": "item",
        "optional": false,
        "mapping": "None",
        "defaults": [
          "False"
        ]
      },
      {
        "index": 1,
        "name": "Minimum Age",
        "nickname": "minAge",
        "ghType": "Integer",
        "access": "item",
        "optional": false,
        "mapping": "None",
        "defaults": [
          "10"
        ]
      },
      {
        "index": 2,
        "name": "Die Range",
        "nickname": "dieRange",
        "ghType": "Integer",
        "access": "item",
        "optional": false,
        "mapping": "None",
        "defaults": [
          "3"
        ]
      },
      {
        "index": 3,
        "name": "Minimum Neighbours",
        "nickname": "minN",
        "ghType": "Integer",
        "access": "item",
        "optional": false,
        "mapping": "None",
        "defaults": [
          "0"
        ]
      },
      {
        "index": 4,
        "name": "Maximum Neighbours",
        "nickname": "maxN",
        "ghType": "Integer",
        "access": "item",
        "optional": false,
        "mapping": "None",
        "defaults": [
          "10"
        ]
      },
      {
        "index": 5,
        "name": "Frequency",
        "nickname": "dieFrequency",
        "ghType": "Integer",
        "access": "item",
        "optional": false,
        "mapping": "None",
        "defaults": [
          "5"
        ]
      }
    ],
    "outputs": [
      {
        "index": 0,
        "name": "Death Settings",
        "nickname": "dieSettings",
        "ghType": "Text",
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
