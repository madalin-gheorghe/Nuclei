# Validation and component catalog

The original five-page pilot was checked against the Nuclei V4 implementation and a locally built assembly. The runtime test uses the **original Slime Intro settings**: 1000 × 1000 × 1 voxels, Voxel Size 1, and 50,000 requested particles.

## Tested environment

| Item | Value |
| --- | --- |
| Nuclei assembly version | 4.1.0.0 |
| Rhino version | 9.0.26251.12303, Windows |
| Validation date | 12 September 2026 |
| Original definition | `01_Slime Intro.gh`, V4 collection |
| Test run | Reset, 200 non-reset steps, output inspection, reset again, and saved-copy reopening |

The assembly version alone does not identify every development build. The exact tested GHA SHA-256 is:

```text
800CC989A3DC930E6D2D5FF6BD3C849797F9835526179D289BF3DA3BDF1EF924
```

## Results and limits

The test passed 30 checks. It confirmed the expected grid, requested particle generation, boundary filtering, movement, retained population, trail data, reset behavior, and preserved objects/wires after reopening a test copy. It also checked flat voxel ordering, image-row tree remapping, single-value mapping, and the mismatch warning on a 2 × 2 × 1 field.

The original example generated **50,000 particles**. Its default reflective boundaries left **49,801** in the solver at reset. This is a measured result for the identified build and definition, not a general guarantee for every population or field.

Testing ran in an isolated Rhino 9 host with the Trigger paused and solutions requested programmatically. A temporary, unsaved Extract Particle Trails component requested CPU-readable results. It was not added to the original download. The test checks a paused Trigger while idle; it is not an interactive test of the Trigger button or Rhino viewport rendering. Timings from this test are not an expected user frame rate.

The original definition and both existing documentation downloads remained byte-identical. No reduced-size beginner definition is added to the guide. The existing [Slime Intro walkthrough](../getting-started/first-slime-simulation.md) remains the teaching example.

## Original runtime-test coverage

- [Construct Voxels](../components/construct-voxels.md)
- [Define Voxel Values](../components/define-voxel-values.md)
- [Construct Slime Particles](../components/construct-slime-particles.md)
- [Nuclei4 Solver GPU](../components/nuclei4-solver-gpu.md)
- [Extract Particle Trails](../components/extract-particle-trails.md)

Port names, indices, types, access modes, optionality, mapping, and stored defaults were extracted from fresh components in the tested assembly. Behavioral explanations also use the implementation; not every documented edge case was executed in this test.

## Machine-readable files

- [Full component port catalog](component-contracts.json)
- [Validation results and saved input values](pilot-validation.json)

The catalog describes component metadata, not executable tool calls. Default entries are Grasshopper display strings; interpret them alongside `ghType`. Component GUIDs identify component types, while canvas InstanceGuids identify particular objects. Registered access and emitted data can differ: Extract Particle Trails registers a list output but emits a tree.

The catalog now covers all **40 public components**. Their port metadata was read from fresh components in an isolated Rhino host on 13 September 2026. Each component page includes its own collapsed JSON reference. This catalog expansion does not mean that every component received the 200-step runtime test above.

Construct Ant Particles and Voxel Settings Ant were subsequently recaptured after the ant input changes. The current catalog identifies that build with SHA-256 `700C1620FD839DD1511E67359961787C8EC08EA595812B2EDED27828F96800C5`. The earlier Slime Intro runtime results remain tied to the original tested build above.

The Ants Intro definition was also opened and initialized to capture its saved controls and connected wires. Its source file and connections were preserved; it was not subjected to the Slime Intro runtime test.

A documentation MCP retrieves reference information; executing a graph still requires a separate Grasshopper integration.
