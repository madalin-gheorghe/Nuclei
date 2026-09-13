# Nuclei

Nuclei is a generative-design plugin for Grasshopper that combines behavior-based particle simulations with highly customizable voxel environments. Inspired by slime-mold transport networks and ant foraging systems, it allows particles to respond to spatial fields, producing branching networks, evolving patterns, and volumetric structures.

**Development:** Madalin Gheorghe · [@madalin\_gheorghe](https://www.instagram.com/madalin_gheorghe/)

**Download:** [Food4Rhino](https://www.food4rhino.com/en/app/nuclei)

**Tutorial:** [Biomorphic Networks V3.0](https://www.youtube.com/watch?v=Hl2Dd9yihHw\&t=7424s) — an older version, but the same principles apply.

Learn how the simulations work in the [Slime Behavior](docs/Slime%20Behavior.md) and [Ant Behavior](docs/Ant%20Behavior.md) guides.

## User documentation

The [Nuclei V4 guide](https://nuclei.gitbook.io/docs/) includes installation instructions, reviewed slime and ant walkthroughs, core concepts, a 40-component reference, and a visual gallery of 18 downloadable examples. Component pages document inputs, outputs, Value List choices, and troubleshooting, with a collapsed machine-readable JSON reference. Optimized screenshots show connected wires and aligned controls; full definition diagrams preserve the original Grasshopper layouts.

The V3 and V4 example folders follow the revised example order and include refreshed definitions and result images. Every V4 example also provides its saved components, controls, and connections as downloadable JSON, with shared JSON Schemas and a reading guide.

The GitBook content lives in [docs/user-guide](docs/user-guide/README.md), with navigation maintained in `SUMMARY.md`. Internal development notes remain separate from the user guide.

## Versions

| Version         | Compatibility      | Description                                                                                                                       | Examples                                                                                    |
| --------------- | ------------------ | --------------------------------------------------------------------------------------------------------------------------------- | ------------------------------------------------------------------------------------------- |
| **V2 — Legacy** | Rhino 6–9, Windows | Original slime-mold simulation tool. Displays an “old v2” banner in Rhino 8/9.                                                    | [V2 examples](https://github.com/madalin-gheorghe/Nuclei/tree/main/Nuclei%20Definitions/v2) |
| **V3.4 — CPU**  | Rhino 8/9          | Major CPU-speed milestone with SIMD field updates, refined ant behavior, voxel controls, trails, and Dendro integration. Displays an “old v3” banner in Rhino 9. | [V3 examples](https://github.com/madalin-gheorghe/Nuclei/tree/main/Nuclei%20Definitions/v3) |
| **V4.2 — GPU**  | Rhino 9, Windows   | Major GPU-speed and feature milestone with scalable previews, image mapping, periodic surfaces, expanded voxel tools, and volume-to-mesh conversion. | [V4 examples](https://github.com/madalin-gheorghe/Nuclei/tree/main/Nuclei%20Definitions/v4) |

V2 and V3 are available separately in Rhino's Package Manager as **Nuclei2** and **Nuclei3**. 



## Source Layout

- `Nuclei-v2-old` — V2 release files; now maintained as the legacy version.
- `Nuclei-v3` — further development of V2 \~2x speed improvements. Introduced Ant Simulations
- `Nuclei-v4` — GPU implementation. Improved functionality and speed
- `Nuclei Definitions` — examples organized by version.
- `docs` and `tools` — technical documentation, build tools, and verification utilities.



## Performance Evidence

Recorded comparisons of the V3.4 CPU and V4.2 GPU implementations using matched solver settings:

| Workload | V3.4 CPU | V4.2 GPU | GPU speedup |
| --- | ---: | ---: | ---: |
| Slime · 500 × 500 · 25,000 particles | 17.012 ms | 1.026 ms | **16.58×** |
| Slime · 64³ · 1,572 particles | 4.487 ms | 0.713 ms | **6.29×** |
| Ants · 500 × 500 · 25,000 particles | 17.511 ms | 1.453 ms | **12.05×** |
| Ants · 64³ · 1,572 particles | 9.164 ms | 1.120 ms | **8.18×** |

Test system: AMD Ryzen 5 7535HS, Radeon 660M integrated GPU, 32 GB RAM, Windows 11.

These historical results measure completed solver work—not startup, Grasshopper scheduling, meshing, or viewport rendering. Results vary by hardware and workload.

See the [Performance Summary](https://github.com/madalin-gheorghe/Nuclei/blob/main/docs/performance/README.md) for methods, development stages, and limitations.



## Milestones

- **V2 · 2022** — initially published on Food4Rhino; now maintained as the legacy version.
- **V3.0** — improved hand-coded baseline by Madalin Gheorghe. includes 2D ant simulations
- **V3.1** — CPU solver stabilization and performance measurement.
- **V3.2** — CPU diffusion and preview optimization.
- **V3.3** — refined CPU behavior and compatibility.
- **V3.4** — SIMD-accelerated slime and ant fields, improved ant mechanics, refreshed examples, and expanded documentation.
- **V4.0** — introduction of GPU simulation and previews.
- **V4.1** — GPU performance improvements and expanded functionality.
- **V4.2** — faster GPU simulation and previews, image mapping, periodic surfaces, expanded voxel tools, and a new volume renderer.

After V3.0, ChatGPT/Codex assisted with testing, optimization, and GPU development.



## License and Feedback

Free to use. Hundreds of hours and thousands of lines of code have gone into this tool. Please include credits wherever appropriate.

Found a bug or have an idea? [Report it or suggest an improvement](https://github.com/madalin-gheorghe/Nuclei/issues)



## Acknowledgements

The Physarum algorithm mechanics are based on Jeff Jones’ paper, [Characteristics of Pattern Formation and Evolution in Approximations of Physarum Transport Networks](https://uwe-repository.worktribe.com/output/980579/characteristics-of-pattern-formation-and-evolution-in-approximations-of-physarum-transport-networks). The ant logic is inspired by [Pezzza’s Simple Ants Simulator](https://github.com/johnBuffer/AntSimulator).

Special thanks to **DesignMorphine** for supporting the plugin’s development, and to all students who participated in the webinars—especially those in the [DM Masters Programme](https://designmorphine.com/education).
