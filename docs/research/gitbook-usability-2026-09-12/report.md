# Making the Nuclei GitBook easier to use

Nuclei should prioritize precise component behavior and reproducible workflows. Its GitBook already delivers documentation in formats that AI agents can retrieve. The largest remaining gaps are the information required to choose the right component, wire it correctly, understand its data, and verify that a simulation worked.

This assessment covers human readers and AI agents equally. Evidence includes the published Nuclei site, its Markdown export and documentation MCP, current local V4 source, and primary documentation from GitBook, McNeel, W3C, and relevant Grasshopper projects. Observations were made on September 12, 2026. Local source contains ongoing changes and is not treated as a verified installed release. Proposed examples and schemas below require release-specific validation before publication.

## 1. What is already working

The published index contains **75 pages, including 40 component reference pages and 18 example pages**. The guide has installation instructions, separate slime and ant tutorials, core concepts, component categories, examples, and troubleshooting. Component tables already distinguish names and nicknames, data types, item/list/tree access, and fresh defaults. The tutorial carefully explains reset and Trigger behavior. These are assets to refine rather than replace. [1](https://nuclei.gitbook.io/docs/llms.txt)

Direct endpoint checks confirmed that the published site provides `llms.txt`, `llms-full.txt`, and the home page as Markdown. The documentation MCP completed initialization, exposed its tool schemas, returned a complete component page, and returned search results containing the requested component. The basic delivery infrastructure is functioning. The [saved evidence](C:/Nuclei/docs/research/gitbook-usability-2026-09-12/evidence.md) records the scope of those checks.

| Existing strength | Improvement that builds on it |
| --- | --- |
| Structured component tables | Add exact semantics, constraints, custom payloads, and stable identity. |
| Original downloadable definitions | Add small teaching examples and explicit success checks. |
| Slime and ant walkthroughs | Make every value and connection reconstructable from text. |
| Screenshots and result images | Supply meaningful alternatives and text descriptions of wiring. |
| Component categories | Add routes organized around the result someone wants to make. |
| Version overview | Identify the precise builds used to validate examples and contracts. |
| Working AI retrieval | Evaluate whether retrieved information produces correct instructions. |

The desktop layout inspected is orderly, with prominent search, a sidebar, and an on-page outline. On the home page, the introductory image pushes the start links below the initial viewport. Move a compact “Run your first simulation” link and compatibility statement above that image. This is an editorial recommendation, not evidence that the existing layout prevents completion.

## 2. Corrections to the advice about AI

**Documentation retrieval is different from model training.** GitBook describes indexing and retrieving content for answers; its AI policy says customer content is not used to train its models. An external agent can read a page into its current context without acquiring permanent knowledge of Nuclei. Use wording such as “AI agents can retrieve the Nuclei reference and use it while building a workflow.” [2](https://gitbook.com/docs/policies/policies/gitbook-ai-policy)

**GitBook already creates the delivery formats.** Its automatic Markdown pages, `llms.txt`, and full export remove the need to start by maintaining duplicate hand-written versions. `llms.txt` is a proposed convention, not a guarantee that every agent will discover or use it. Accurate contents and a visible entry point remain necessary. [3](https://gitbook.com/docs/getting-started/llm-ready-docs)

**The documentation MCP and an execution bridge have different jobs.** GitBook’s site MCP serves published documentation; its organizational MCP is a separate tool for managing GitBook content. Neither inherently manipulates a Grasshopper canvas. The Nuclei site’s live tool list also includes feedback submission, so “read-only documentation access” should not be interpreted as “every exposed operation has no side effects.” Only retrieval operations were exercised for this assessment. [4](https://gitbook.com/docs/ai-for-your-readers/mcp-servers-for-published-docs.md) [5](https://gitbook.com/docs/docs-as-code/gitbook-mcp.md)

**The evidence does not establish that Grasshopper workflows have shifted entirely to this architecture.** The cited McNeel post describes one author’s tested workflow, using skills and Cordyceps to build and verify a compiled plugin. It establishes feasibility, with a stated Rhino 8/Windows test scope, rather than universal adoption. Swiftlet’s own documentation describes deliberately defined tools, parameter schemas, JSON arguments, and responses. That is a useful integration pattern, not automatic support for every installed plugin. [6](https://discourse.mcneel.com/t/claude-code-writing-compiled-grasshopper-plugins-two-agent-skills/221307) [7](https://github.com/enmerk4r/Swiftlet/wiki/MCP)

**Separate geometry types from data structure.** “Surface (`GH_Structure`)” mixes a value type with a tree container. A contract needs both the value type and its access mode. McNeel documents `GH_Structure<T>` as a tree whose branches have paths; item, list, and tree processing are distinct. [8](https://developer.rhino3d.com/api/grasshopper/html/T_Grasshopper_Kernel_Data_GH_Structure_1.htm) [9](https://developer.rhino3d.com/guides/grasshopper/csharp-essentials/1-grasshopper-csharp-component/)

The current GitBook skills repository concerns documentation authoring and maintenance. The older `GitbookIO/skills` repository is archived. Neither is evidence that an agent can execute Nuclei components. [10](https://github.com/GitbookIO/gitbook-skills) [11](https://github.com/GitbookIO/skills)

## 3. Make component pages answer operational questions

The existing tables are a good reference skeleton. However, “Generic Data,” “Type of Voxel Value,” and “keep data ordering consistent” leave important questions unanswered. A person may discover the answers by experimenting on the canvas; an agent may invent a plausible answer instead. The reference should make those decisions explicit.

### A consistent component page

Use this order on each component page:

1. **Purpose and result:** one sentence explaining what it does, followed by when to use it.
2. **Minimal example:** a small image, exact connections, and the expected result.
3. **Inputs and outputs:** exact names/nicknames, indices, type, access, required/default behavior, units, limits, and meaning.
4. **Behavior:** input precedence, tree handling, ordering, initialization, menus, and persistent state.
5. **Failures and recovery:** recognizable symptoms or exact runtime messages, their causes, and specific remedies.
6. **Compatibility and identity:** tested build, dependencies, stable GUID, and source reference.
7. **Related tasks:** what to do before and after this component.

Keep the first screen approachable. Put the GUID and implementation type in a clearly labeled technical section. Critical restrictions, such as Construct Voxels allowing only one instance in a document, belong in the normal usage instructions too. Do not hide necessary behavior exclusively in images, expandable panels, or downloadable JSON.

### Define Voxel Values: a high-value pilot

The published page identifies `Type` as an integer with default `0`, and `Multiplier Value` as a number tree. It does not enumerate the codes or explain the detailed mapping rules. The current local source exposes the following selectable mapping values. Publish them only against the build in which they are verified. [12](https://nuclei.gitbook.io/docs/components/voxel-creation-and-mapping/define-voxel-values) [Local enum source](C:/Nuclei/Nuclei-v4/Nuclei4/VoxelTypeChoices.cs:44)

| Code | Mapping property |
| --- | --- |
| 0 | Minimum Density |
| 1 | Maximum Density |
| 2 | Speed |
| 3 | Sensor Distance |
| 4 | Sensor Angle |
| 5 | Rotation Angle |
| 6 | Slime Food |
| 13 | Ant Food |

The jump to `13` illustrates why “choose the named Type” is insufficient for programmatic use. Explain the valid choices for each component separately: mapping and preview components do not necessarily accept the same set.

The local implementation also distinguishes a single multiplier, a full-grid value set, and an active-selection value set. It recognizes certain row-organized trees and remaps their order. A mismatched nonempty count generates a warning rather than applying the requested replacement map. These rules should be expressed with a tiny worked example and a failure example. The empty-input case needs a precise explanation that distinguishes optional input handling from the map operation itself. [Mapping implementation](C:/Nuclei/Nuclei-v4/Nuclei4/Voxel_Values.cs:149)

For example, after release verification, document that the internal full-grid flat index is `x × ResY × ResZ + y × ResZ + z`. For a `2 × 2 × 1` grid, indices `0, 1, 2, 3` correspond to `(0,0,0), (0,1,0), (1,0,0), (1,1,0)`. Present image-row input separately because its automatic remapping changes the interpretation. This simple example is more useful than an unexplained large JSON array. [Index implementation](C:/Nuclei/Nuclei-v4/Nuclei4/VoxelGridData.cs:733)

### Explain custom data and tree structure

Add a short “Data flowing through Nuclei” page. Distinguish a voxel field, a particle group supplied to the solver, evolving solver outputs, settings, and extracted Rhino geometry. State which outputs can connect to which inputs. The current Construct Voxels implementation outputs one `Nuclei4.VoxelField`; “Generic Data” is the Grasshopper port registration, not a useful description of its contents. Do not imply that a .NET object is automatically serializable or publicly supported as a scripting API. [Voxel field source](C:/Nuclei/Nuclei-v4/Nuclei4/VoxelField.cs:14)

For tree-producing components, show paths, branch meaning, item order, and empty-branch behavior. The existing Extract Particle Trails page correctly warns that a list-registered output actually emits a tree. Extend that explanation with the current branch shape `{groupIndex;globalParticleIndex}` and a two-particle example. Explain why preserving branches matters when constructing polylines. [13](https://nuclei.gitbook.io/docs/components/previews-and-extraction/extract-particle-trails) [Trail implementation](C:/Nuclei/Nuclei-v4/Nuclei4/Particle_Extractor_TrailPoints.cs:58)

McNeel’s own documentation demonstrates that the same values arranged in different branches can produce different results. Consequently, flattening or grafting is part of a component’s usage contract when it changes the intended workflow. It should never be a generic troubleshooting instruction applied everywhere. [14](https://developer.rhino3d.com/guides/grasshopper/gh-algorithms-and-data-structures/advanced-data-structures/)

## 4. Give beginners a smaller first success

The first slime tutorial is detailed and preserves the original definition, but it starts with **1,000,000 voxels and 50,000 particles**. Its Voxel Size row says to use the stored value without stating the value. This is adequate for inspecting that saved file, but incomplete for rebuilding it from text. Keep that walkthrough and introduce a separate small starter. [15](https://nuclei.gitbook.io/docs/getting-started/first-slime-simulation)

A candidate starter could use a `100 × 100 × 1` field, voxel size `1`, and approximately `1,000` particles. These are proposed trial values, not a tested performance guarantee. Validate them on the minimum supported environment and adjust until they produce a useful visible result quickly. Make every persistent input explicit; keep the original example available as the next step.

Each starter should include supported Rhino/Nuclei versions, model units, any required external files, initial toggle states, all connections, and a defined stopping point. Internalize suitable geometry or bundle the required assets. Name external dependencies and explain whether they are needed for the simulation itself or only an optional extraction step.

A text wiring table should identify individual component instances, especially when a graph contains duplicates:

| From | To | Meaning |
| --- | --- | --- |
| `field.voxels` | `slime.voxels` | Use the same starting domain for particle generation. |
| `field.voxels` | `solver.voxels` | Supply the simulation environment. |
| `slime.particles` | `solver.particles` | Supply the generated population. |
| `trailSettings.trailSettings` | `solver.settings` | Retain history for trail display. |
| `voxelSettings.voxelSettings` | `solver.settings` | Supply slime field behavior settings. |
| `solver.particles` | `trailPreview.particles` | Preview evolving particles. |

These instance labels are proposed teaching notation, not callable identifiers. Include Trigger targeting and reset sequencing as separate control steps. Preserve multiple settings wires and explain how to add them in Grasshopper without replacing an existing connection.

State what success means: the expected component set is loaded, runtime errors are absent, the field has the intended dimensions, particles advance, trails appear after sufficient steps, and pausing stops advancement. Exact geometry should not be promised for a stochastic simulation without a verified mechanism for controlling randomness. A reference image demonstrates the kind of result, while measurable invariants determine whether the example worked.

This approach follows Diátaxis guidance that tutorials should produce visible results and guide learners through concrete steps. It is a design recommendation, not a measured claim that it will reduce Nuclei onboarding time by a particular percentage. [16](https://diataxis.fr/tutorials/)

## 5. Organize routes around what people want to make

The existing structure already separates tutorials, concepts, reference, and examples. Preserve those distinctions. Add a small “Build a workflow” route that gives a user a destination without requiring them to understand the component taxonomy first. Diátaxis describes how-to guides as organized around user goals. [17](https://diataxis.fr/start-here/) [18](https://diataxis.fr/how-to-guides/)

Useful first routes are:

- Make an image-guided slime network.
- Guide particles around an excluded region.
- Set up ants with a colony and food.
- Extract trails as separate curves.
- Combine multiple populations in one field.
- Convert a simulation field into a volume or mesh.
- Reduce the cost of a large simulation.

Turn the example index into a compact comparison table with outcome, 2D/3D, difficulty, dependencies, and approximate workload. Describe workload using dimensions and particle counts; label timings as measured on stated hardware when available. Readers can then choose an appropriate example without opening 18 pages.

Add plain-language synonyms to useful introductory sentences: “environment/domain,” “particle trails/paths,” and “export/extract.” Explain ambiguous terms such as field, density, trail history, deposited signal, and population. Searchable error messages and direct answers are more valuable than keyword repetition.

The Ladybug Direct Sun Hours reference is a useful model for explaining constraints, units, defaults, and output structure. Borrow that operational detail while retaining Nuclei’s own layout and adding runnable examples. [19](https://docs.ladybug.tools/ladybug-primer/components/3_analyzegeometry/direct_sun_hours)

## 6. Make screenshots useful without requiring vision

The captured full Markdown export contains **85 image references with empty labels**. Earlier individual page captures returned some descriptive labels and different image assets. Publishing timing, caching, or representation differences may explain this. The evidence supports an export-specific finding and a need to check synchronization, not a claim that every rendered image lacks an accessible description.

Give meaningful images a short description, then place essential relationships in visible text. A canvas diagram needs an explanation of connections and settings; alt text alone should not contain an entire tutorial. W3C recommends short identification plus a longer textual equivalent for complex images. [20](https://www.w3.org/WAI/tutorials/images/complex/)

For each component image, identify the component, the input controls shown, and the workflow it demonstrates. For a result image, describe the pattern and connect it to the relevant example. Do not use color alone to identify a group: pair “purple group” with its name. Avoid making screenshot resolution the only way to read a slider value.

Keep real text tables, descriptive download links, concise headings, and written alternatives to videos. Check keyboard navigation, zoom, narrow screens, table scrolling, and both available color modes on the published site. These are recommended usability checks; this assessment does not certify WCAG conformance or claim that all those configurations have been tested. [21](https://www.w3.org/WAI/tips/writing/) [22](https://www.w3.org/WAI/tutorials/tables/)

## 7. Publish a small, honest AI entry point

Add “Use Nuclei documentation with AI” with links to the component index, the version policy, and these existing endpoints:

- [Documentation index](https://nuclei.gitbook.io/docs/llms.txt)
- [Complete Markdown export](https://nuclei.gitbook.io/docs/llms-full.txt)
- [Documentation MCP endpoint](https://nuclei.gitbook.io/docs/~gitbook/mcp)

Explain that the MCP endpoint belongs in an HTTP-capable MCP client; it is not a normal page to inspect in a browser. GitBook requires Page actions to remain enabled for this endpoint to work. Nuclei’s inspected configuration already includes Markdown and MCP page actions. [4](https://gitbook.com/docs/ai-for-your-readers/mcp-servers-for-published-docs.md)

Give a first retrieval task: “Find Construct Voxels, report its fresh defaults, and cite the component page. Distinguish fresh defaults from the Slime Intro file’s saved values.” In one live search, the correct page appeared fifth behind attractor pages. That single observation is not a benchmark, but it demonstrates why an agent should inspect the index and retrieve a complete page rather than trust the first excerpt.

Current GitBook pricing includes Markdown, LLM files, and site MCP across its plans. Richer site AI features have separate tier requirements. Avoid choosing an expensive tier solely to make external-agent retrieval possible; Nuclei’s basic endpoints are already working. Plan details can change. [23](https://www.gitbook.com/pricing)

If a live Grasshopper bridge is later supported, give it a separate integration guide naming the tested bridge and versions, exact tool schemas, component lookup method, reset/step/pause behavior, error reporting, and output checks. An illustrative component catalog does not become an executable API merely because it is JSON. A bridge must map those descriptions onto real operations and handle persistent component state such as image selection. Swiftlet’s explicitly declared tool model is one possible pattern to evaluate. [7](https://github.com/enmerk4r/Swiftlet/wiki/MCP)

## 8. Generate facts and maintain explanations

Nuclei already has a component GUID map and a preservation verifier that reflects component metadata. Extend that infrastructure to export a release-specific catalog. Generate stable identity, ordered ports, access, defaults, and optionality from the pinned assembly. Review units, meanings, constraints, and workflow advice as authored documentation. [GUID map](C:/Nuclei/docs/versioning/v4-guid-map.md:3) [Existing reflection](C:/Nuclei/tools/Verify-V4Preservation.ps1:187)

The following is a proposed documentation format for one real component. It is **not an executable MCP request or a supported Nuclei serialization format**. It reflects the inspected local source, which must be checked against the release selected for publication. [Component registration](C:/Nuclei/Nuclei-v4/Nuclei4/Voxel_Constructor.cs:30)

```json
{
  "name": "Construct Voxels",
  "componentGuid": "a3940a4d-9015-411c-9ffa-e38ecc90d394",
  "dotnetType": "Nuclei4.VoxelConstructor",
  "inputs": [
    {"index": 0, "name": "Voxel Size", "nickname": "voxelSize", "ghType": "Number", "access": "item", "default": 1.0},
    {"index": 1, "name": "X Voxels", "nickname": "xVoxels", "ghType": "Integer", "access": "item", "default": 100},
    {"index": 2, "name": "Y Voxels", "nickname": "yVoxels", "ghType": "Integer", "access": "item", "default": 100},
    {"index": 3, "name": "Z Voxels", "nickname": "zVoxels", "ghType": "Integer", "access": "item", "default": 1}
  ],
  "outputs": [
    {"index": 0, "name": "Output Voxels", "nickname": "voxels", "ghType": "Generic Data", "runtimeType": "Nuclei4.VoxelField", "access": "item"}
  ]
}
```

The production catalog should additionally include its schema version, plugin build, source revision, documentation URL, optionality, units, constraints, enum choices, and behavior notes. A component GUID identifies its type; a canvas instance has a separate identity. Preserve this distinction when describing graphs with repeated components.

Show “Applies to” and “Tested with” metadata on examples and reference pages. “Last updated” alone does not identify compatible plugin behavior. Publish examples with release-stable downloads and a small manifest recording their file hashes, assets, starting values, and validation status. Do not infer release behavior from the local assembly’s `4.1.0.0` metadata alone.

Choose an explicit documentation source of truth and review content changes with the corresponding software release. Check that authored Markdown, rendered pages, full exports, and MCP retrieval all expose the same important facts after publication. Hidden legacy pages can still appear through GitBook’s full export and MCP, so hiding old instructions is not a reliable version boundary. [3](https://gitbook.com/docs/getting-started/llm-ready-docs)

Before reorganizing navigation, capture existing public URLs and arrange redirects. GitBook page paths follow navigation structure rather than simply mirroring Git Sync file paths. This matters here: local component Markdown lives directly under `components`, while public pages are nested under component categories. [24](https://gitbook.com/docs/create-content/content-structure/page)

## 9. An implementation sequence and acceptance criteria

The following is a proposed sequence, not an estimated delivery schedule.

| Priority | Work | Completion evidence |
| --- | --- | --- |
| P0 | Pilot complete contracts for Construct Voxels, Define Voxel Values, Construct Slime Particles, Solver GPU, and Extract Particle Trails | A reviewer can answer defaults, valid inputs, tree behavior, and version questions without reading code. |
| P0 | Create and validate a small slime starter | A new user rebuilds or opens it, runs it, pauses it, and confirms documented outcomes. |
| P0 | Add exact enum choices, ordering rules, and custom data explanations | Source and release tests agree with the published reference. |
| P1 | Complete image alternatives and text wiring | Essential steps can be followed with images hidden; Markdown preserves the same meaning. |
| P1 | Add task routes and an example comparison table | Users can choose the correct workflow from a stated goal. |
| P1 | Add the AI entry page and version metadata | An external client retrieves the correct reference and identifies its compatibility scope. |
| P1 | Generate the release catalog and drift checks | A port/default change produces a reviewable documentation difference. |
| P2 | Expand tested bridge integrations and advanced examples | Each advertised integration completes a bounded live task and checks its results. |

Keep a small evaluation set that can be repeated after meaningful documentation or release changes:

| Task | Required behavior |
| --- | --- |
| Find fresh Construct Voxels defaults | Reports `1, 100, 100, 1`; does not copy the large example’s saved settings. |
| Map Ant Food | Finds the component-specific code and its documented release scope. |
| Explain a multiplier-count mismatch | States the actual accepted shapes and component behavior rather than suggesting random flattening. |
| Build the starter | Uses correct component identities, wires, values, and control sequencing. |
| Diagnose no movement | Checks reset, Trigger, inputs, and runtime messages in a sensible order. |
| Extract separate trails | Preserves particle branches and avoids joining unrelated trails. |
| Use Image Mapper | Identifies planar-field requirements and image-selection state. |
| Follow a legacy example | Recognizes the version mismatch and finds compatible instructions. |
| Ask for an unsupported API | States the limitation instead of inventing a callable method. |
| Find a named component | Retrieves the correct complete page, with a valid source link. |

For humans, record completion, time to first useful result, confusing steps, and requests for help. For agents, separately score retrieval, factual accuracy, correct graph construction, successful execution where a bridge exists, and appropriate uncertainty. Use the same tasks before and after improvements; do not treat a plausible written answer as proof of a working simulation.

GitBook site analytics can expose search and agent traffic, while AI Insights can help identify unanswered questions and relevant source pages where the feature is available. Use those signals to select documentation work, then verify improvements with task completion. Traffic alone is not a measure of correct Nuclei use. [25](https://gitbook.com/docs/analytics/insights.md) [26](https://gitbook.com/docs/analytics/ai-insights)

## Sources

Sources without a stated publication date were accessed September 12, 2026. Nuclei endpoint captures are preserved beside this report. Local source links refer to the working copy inspected, not an immutable release.

1. Nuclei. [Published documentation index](https://nuclei.gitbook.io/docs/llms.txt).
2. GitBook. [GitBook AI Policy](https://gitbook.com/docs/policies/policies/gitbook-ai-policy).
3. GitBook. [LLM-ready docs](https://gitbook.com/docs/getting-started/llm-ready-docs).
4. GitBook. [MCP servers for published docs](https://gitbook.com/docs/ai-for-your-readers/mcp-servers-for-published-docs.md).
5. GitBook. [GitBook MCP](https://gitbook.com/docs/docs-as-code/gitbook-mcp.md).
6. Daniel Nunes Locatelli. [Claude Code writing compiled Grasshopper plugins: two agent skills](https://discourse.mcneel.com/t/claude-code-writing-compiled-grasshopper-plugins-two-agent-skills/221307). July 29, 2026.
7. Swiftlet project. [MCP wiki](https://github.com/enmerk4r/Swiftlet/wiki/MCP).
8. McNeel. [GH_Structure<T> API reference](https://developer.rhino3d.com/api/grasshopper/html/T_Grasshopper_Kernel_Data_GH_Structure_1.htm).
9. McNeel. [Grasshopper C# Component](https://developer.rhino3d.com/guides/grasshopper/csharp-essentials/1-grasshopper-csharp-component/).
10. GitbookIO. [gitbook-skills](https://github.com/GitbookIO/gitbook-skills).
11. GitbookIO. [Archived skills repository](https://github.com/GitbookIO/skills). Archived June 26, 2026.
12. Nuclei. [Define Voxel Values](https://nuclei.gitbook.io/docs/components/voxel-creation-and-mapping/define-voxel-values).
13. Nuclei. [Extract Particle Trails](https://nuclei.gitbook.io/docs/components/previews-and-extraction/extract-particle-trails).
14. Rajaa Issa / McNeel. [Advanced Data Structures](https://developer.rhino3d.com/guides/grasshopper/gh-algorithms-and-data-structures/advanced-data-structures/). Updated July 15, 2024.
15. Nuclei. [Your first slime simulation](https://nuclei.gitbook.io/docs/getting-started/first-slime-simulation).
16. Daniele Procida / Diátaxis. [Tutorials](https://diataxis.fr/tutorials/).
17. Daniele Procida / Diátaxis. [Start here](https://diataxis.fr/start-here/).
18. Daniele Procida / Diátaxis. [How-to guides](https://diataxis.fr/how-to-guides/).
19. Ladybug Tools. [Direct Sun Hours](https://docs.ladybug.tools/ladybug-primer/components/3_analyzegeometry/direct_sun_hours).
20. W3C WAI. [Complex Images](https://www.w3.org/WAI/tutorials/images/complex/). Updated April 8, 2026.
21. W3C WAI. [Writing for Web Accessibility](https://www.w3.org/WAI/tips/writing/).
22. W3C WAI. [Tables Tutorial](https://www.w3.org/WAI/tutorials/tables/).
23. GitBook. [Pricing](https://www.gitbook.com/pricing). Current feature table at access date.
24. GitBook. [Pages](https://gitbook.com/docs/create-content/content-structure/page).
25. GitBook. [Site analytics](https://gitbook.com/docs/analytics/insights.md).
26. GitBook. [AI insights](https://gitbook.com/docs/analytics/ai-insights).
