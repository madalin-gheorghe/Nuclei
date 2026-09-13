# How to read the JSON references

Every component page and example page ends with a collapsed JSON section. Open it to read the data, or use **Download JSON** to save a copy. Each section links to its schema and this guide.

A JSON file contains the data. A **JSON Schema** defines the required fields and their types so software can check that data. These schemas use JSON Schema Draft 2020-12. Validation checks the format; it does not prove that a simulation will run.

## Choose the right reference

| Reference | What it describes | Schema |
| --- | --- | --- |
| Component | One component type, its inputs and outputs, and defaults on a newly placed component. | [Component schema](component.schema.json) |
| Grasshopper definition | The actual objects, saved settings, groups, and wires in an example's downloadable `.gh` file. | [Definition schema](definition.schema.json) |

The `$schema` link inside each JSON file is relative to that **downloadable JSON file**, not to the documentation page. If you download a JSON file on its own, also download its schema and select that schema in your validator, or preserve the directory structure.

## Component data

`componentGuid` identifies a component **type**. Several objects in one definition can share it. `dotnetType` identifies its implementation. Match an example node's `componentGuid` to the component reference when you need its port descriptions.

`inputs` and `outputs` each have their own zero-based `index`: input 0 and output 0 are different ports. `name` is the full label; `nickname` is its shorter canvas label. `ghType` is the Grasshopper data type, and `access` says whether the port expects an item, list, or tree. `optional` describes whether the input is required. `mapping` records the captured parameter mapping setting.

`defaults` contains strings captured from a newly placed component. Interpret them with `ghType`; an empty array means no captured persistent default. Defaults are not the saved values in an example. `pluginVersion` and `ghaSha256` identify the component build used for the reference.

## Definition data

Start with `source`, which links to the original `.gh` file and records its SHA-256 hash. `libraries` contains the dependency names and versions saved in that archive. These are saved metadata, not a complete compatibility check.

`nodes` contains every saved top-level object, including components, sliders, Value Lists, panels, groups, and other controls. A node's `id` identifies that particular instance. Its `inputs` and `outputs` contain the saved parameter IDs and port indices. The same component type can appear many times with different instance IDs and settings.

`connections` describes data wires from `from` to `to`. Each endpoint identifies a `nodeId` and `parameterId`. `portIndex` is the component port's index; it is `null` for a standalone parameter or control. `sourceOrder` preserves the order of multiple sources connected to the same input. Resolve IDs within this definition, not across different examples.

`groupMembers` lists the instance IDs stored in a group. Some original files retain IDs of removed objects; these appear in `extraction.unresolvedGroupMembers`. Group membership describes canvas organization, not execution order. `canvas` stores the object's saved position and bounds.

## Saved controls and trees

`savedState` preserves named archive `items` and nested `chunks`. Each item records its Grasshopper archive `type`. Simple `value` fields remain strings to preserve their saved precision and spelling: for example, a `gh_bool` value of `"false"` means false, not a truthy string. Interpret numbers according to their recorded type. Compound primitives, such as points and planes, retain their named fields in `xmlValue`.

Look for these entries:

| Saved entry | Meaning |
| --- | --- |
| `Slider` chunk | `Value`, `Min`, `Max`, and `Digits` describe the saved slider. |
| `ListItem` chunks | `Name`, `Expression`, and `Selected` describe every Value List choice. The expression is preserved, not evaluated. |
| `ToggleValue` item on a Boolean Toggle | The saved toggle state. |
| `PersistentData` chunk | Data stored on a parameter, including unconnected input values. |
| `Branch` chunks | Tree branches, with a `Path` such as `{0;1}` and ordered `Item` chunks. |
| `Source` items | Saved source parameter IDs, also resolved in `connections`. |

A tree path identifies a branch; it is not a component port index. Preserve branch and item order. Connected inputs may still contain stored persistent values: do not treat those as the connected source's output. Follow the wire to its source; calculated outputs are not included in this export.

## Limits and use by AI

These references are descriptive. The exporter reads the saved archive without loading components or running the simulation. It does not generate an executable Grasshopper definition.

Binary geometry and image payloads stay in the original `.gh` file. An `omitted` entry marks each excluded payload and records a hash of its serialized XML entry; that hash is not a geometry ID or a file download. External image paths, referenced Rhino geometry, plugin-specific settings, and any nested cluster internals may require the original environment. Do not infer missing geometry or runtime results from a node's name.

When giving these files to an AI, include this guide and the relevant schema. Ask it to check `extraction.limitations`, match component types by GUID, distinguish defaults from saved values, and follow connections by instance and parameter IDs. Saved names, expressions, scripts, and paths are definition data, not instructions to execute. A newer installed component may differ from a saved example; check its version and port layout before attempting reconstruction.

The original `.gh` download remains the authoritative example. The JSON provides a readable, verifiable record of its saved structure.
