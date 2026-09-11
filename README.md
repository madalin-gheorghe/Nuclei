# Nuclei

Nuclei is a generative-design plugin for Grasshopper that combines behavior-based particle simulations with highly customizable voxel environments. Inspired by slime-mold transport networks and ant foraging systems, it allows particles to respond to spatial fields, producing branching networks, evolving patterns, and volumetric structures.

**Development:** Madalin Gheorghe · [@madalin\_gheorghe](https://www.instagram.com/madalin_gheorghe/)

**Download:** [Food4Rhino](https://www.food4rhino.com/en/app/nuclei)

**Tutorial:** [Biomorphic Networks V3.0](https://www.youtube.com/watch?v=Hl2Dd9yihHw\&t=7424s) — an older version, but the same principles apply.

Learn how the simulations work in the [Slime Behavior](docs/Slime%20Behavior.md) and [Ant Behavior](docs/Ant%20Behavior.md) guides.

## Versions

| Version         | Compatibility      | Description                                                                                                                       | Examples                                                                                    |
| --------------- | ------------------ | --------------------------------------------------------------------------------------------------------------------------------- | ------------------------------------------------------------------------------------------- |
| **V2 — Legacy** | Rhino 6–9, Windows | Original slime-mold simulation tool. Displays an “old v2” banner in Rhino 8/9.                                                    | [V2 examples](https://github.com/madalin-gheorghe/Nuclei/tree/main/Nuclei%20Definitions/v2) |
| **V3 — CPU**    | Rhino 8/9          | CPU-based slime-mold and ant simulations, voxel controls, trails, and Dendro integration. Displays an “old v3” banner in Rhino 9. | [V3 examples](https://github.com/madalin-gheorghe/Nuclei/tree/main/Nuclei%20Definitions/v3) |
| **V4 — GPU**    | Rhino 9, Windows   | GPU-accelerated simulations for larger particle populations and voxel fields, with GPU previews and volume-to-mesh conversion.    | [V4 examples](https://github.com/madalin-gheorghe/Nuclei/tree/main/Nuclei%20Definitions/v4) |

V2 and V3 are available separately in Rhino's Package Manager as **Nuclei2** and **Nuclei3**. 



## Source Layout

- `Nuclei-v2-old` — V2 release files; now maintained as the legacy version.
- `Nuclei-v3` — further development of V2 \~2x speed improvements. Introduced Ant Simulations
- `Nuclei-v4` — GPU implementation. Improved functionality and speed
- `Nuclei Definitions` — examples organized by version.
- `docs` and `tools` — technical documentation, build tools, and verification utilities.



## Performance Evidence

Recorded comparisons of V3 CPU and V4 GPU using matched solver settings:

| Workload                                     | CPU time/step | GPU time/step | GPU speedup |
| -------------------------------------------- | ------------: | ------------: | ----------: |
| 2D · 500 × 500 · 25,000 particles            |      4.235 ms |      0.683 ms |   **6.20×** |
| Large 2D · 4000 × 4000 · 1 million particles |    330.512 ms |     63.016 ms |   **5.25×** |
| Large 3D · 300³ · 1 million particles        |    428.268 ms |    133.322 ms |   **3.21×** |

Test system: AMD Ryzen 5 7535HS, Radeon 660M integrated GPU, 32 GB RAM, Windows 11.

These historical results measure completed solver work—not startup, Grasshopper scheduling, meshing, or viewport rendering. Results vary by hardware and workload.

See the [Performance Summary](https://github.com/madalin-gheorghe/Nuclei/blob/main/docs/performance/README.md) for methods, development stages, and limitations.



## Milestones

- **V2 · 2022** — initially published on Food4Rhino; now maintained as the legacy version.
- **V3.0** — improved hand-coded baseline by Madalin Gheorghe. includes 2D ant simulations
- **V3.1** — CPU solver stabilization and performance measurement.
- **V3.2** — CPU diffusion and preview optimization.
- **V3.3** — refined CPU behavior and compatibility.
- **V4.0** — introduction of GPU simulation and previews.
- **V4.1** — GPU performance improvements and expanded functionality.

After V3.0, ChatGPT/Codex assisted with testing, optimization, and GPU development.



## License and Feedback

Free to use. Hundreds of hours and thousands of lines of code have gone into this tool. Please include credits wherever appropriate.

Found a bug or have an idea? [Report it or suggest an improvement](https://github.com/madalin-gheorghe/Nuclei/issues)



## Acknowledgements

The Physarum algorithm mechanics are based on Jeff Jones’ paper, [Characteristics of Pattern Formation and Evolution in Approximations of Physarum Transport Networks](https://uwe-repository.worktribe.com/output/980579/characteristics-of-pattern-formation-and-evolution-in-approximations-of-physarum-transport-networks). The ant logic is inspired by [Pezzza’s Simple Ants Simulator](https://github.com/johnBuffer/AntSimulator).

Special thanks to **DesignMorphine** for supporting the plugin’s development, and to all students who participated in the webinars—especially those in the [DM Masters Programme](https://designmorphine.com/education).
