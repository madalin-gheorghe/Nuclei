# Nuclei project context

## Purpose

Nuclei is a Grasshopper generative-design plugin for particle simulations and voxel environments.

## Project layout

- `Nuclei-v2-old` — legacy V2 release material.
- `Nuclei-v3` — CPU implementation and ongoing V3 work.
- `Nuclei-v4` — GPU implementation and ongoing V4 work.
- `Nuclei Definitions` — versioned Grasshopper examples.
- `docs` — user and technical documentation, including GitBook-facing material.
- `tools` — build, verification, and maintenance utilities.

## Working conventions

1. Read `AGENTS.md` and the relevant documentation before editing or publishing.
2. Check Git status before release work; do not overwrite unrelated changes.
3. Keep durable, shareable decisions in repository documentation. Keep personal account context in `.local-context/`, which is Git-ignored.
4. Treat GitHub, Yak, and GitBook authentication as external account configuration; never commit credentials.

## Publishing guardrail

Before publishing a release or documentation update, verify the target version, package contents, user-facing documentation, and relevant tests. Publishing requires explicit user authorization.
