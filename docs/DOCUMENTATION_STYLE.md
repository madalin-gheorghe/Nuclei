# Documentation style

Agreed during the September 2026 GitBook review.

- Lead with what the user can do and explain settings through their practical effects.
- Keep tutorials focused on the supplied definition. Explain connections where the relevant component is introduced; do not add a separate wiring table.
- Show the example result near the beginning and again where the reader compares their result. Preserve the original definition, settings, layout, and group colors.
- Make every image fill the text column while preserving its proportions. Follow [screenshot rules](SCREENSHOT_GUIDELINES.md), including connected output wires and nonvisible accessibility descriptions.
- Component pages use a short purpose, Location, image, usage, input/output tables, relevant behavior, troubleshooting, and related links.
- Visible port tables use names and nicknames, types/access, defaults, and meanings. Keep port indices, GUIDs, implementation names, and build metadata in the collapsed machine-readable reference.
- Put the component's JSON at the end in a GitBook `<details>` block, collapsed by default. Keep it synchronized with the full catalog.
- End each example with a collapsed Grasshopper definition JSON block extracted from its actual `.gh` download. Preserve instance IDs, saved controls, trees, and wire ordering; mark unsupported or omitted data explicitly.
- Both JSON sections link to a downloadable data file, the appropriate shared JSON Schema, and the shared reading guide. Validate schemas, source hashes, graph references, and parity between embedded and downloadable data before publishing.
- Keep image descriptions in alt text only, including in the review preview.
- Example gallery covers use square frames and show the full image without cropping or stretching.
- Avoid repeated version checks, arbitrary sample dimensions, duplicated identity sections, unnecessary negative statements, and internal ordering explanations that do not help the task.
- Keep lengthy saved-control lists and deeper technical explanations expandable. Do not obscure instructions needed to complete the walkthrough.
- Provide an annotatable local preview with optional green highlights before publishing. Publishing requires the user's approval. Do not push or pull GitHub without an explicit request.
