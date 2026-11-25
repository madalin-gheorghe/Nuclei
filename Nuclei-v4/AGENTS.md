# V4 working rules

Read the root [AGENTS.md](../AGENTS.md), [architecture map](../docs/V4_ARCHITECTURE.md),
[preservation contract](../docs/V4_PRESERVATION_CONTRACT.md), and
[approved preview](../docs/V4_VOXEL_PREVIEW_GOLDEN_MASTER.md) before behavior changes.
Inspect the owning implementation; historical passing results are not current evidence.

- Preserve GH1 component GUIDs, assembly/public/Goo identities, archive keys,
  ordered parameters, defaults, settings tokens, saved definitions and approved preview.
  Intentional contract changes require explicit authorization and updated documentation.
- GH1 composes backends. Core and GPU abstractions have no host dependencies;
  concrete backends depend on contracts. Display abstractions currently use Rhino
  geometry: this is an existing exception, not permission to spread host dependencies.
- Components own sessions and subscriptions; sessions own compute resources;
  renderers own opened display views and render targets. Frames borrow native handles.
  Keep construction-failure cleanup, replacement, reset and disposal explicit.
- Keep resident GPU state and demand-driven readback. Document buffer layouts,
  units, coordinates, dispatch limits and synchronization when modifying them.
- Avoid speculative abstractions, new plugins, arbitrary method-length limits and
  cosmetic rewrites. Establish supported behavior, discovery/reflection, serialization,
  resource and fallback roles before proposing removal.
- Follow [CODING_GUIDE.md](CODING_GUIDE.md) and use [REVIEW_GUIDE.md](REVIEW_GUIDE.md).
  Run affected checks and report limitations; do not refresh hashes merely to pass.

From `C:\Nuclei`, the serial net7 Release build passed during the 2026-11-24 review:

```powershell
dotnet build Nuclei-v4/Nuclei4/Nuclei4.csproj -c Release -f net7.0-windows -m:1 -p:SkipGrasshopperInstall=true
```

Always keep `SkipGrasshopperInstall=true` for verification. The project otherwise
copies output into Grasshopper Libraries. See the review guide for focused checks,
net48 coverage and known baseline limitations. Building is not permission to install,
publish, commit, pull or push.

## Small tasks and handovers

- Work on one bounded outcome at a time, such as reproducing R2 or fixing partial
  constructor cleanup. Before editing, record its ID, expected behavior, likely
  files, exclusions and acceptance checks. Separate investigation from implementation
  when behavior is uncertain.
- Finish the task before starting another. If blocked, record the blocker and
  switch only to independent work. Keep each change independently reviewable and
  reversible; do not mix runtime fixes with formatting or unrelated cleanup.
- Verify affected behavior before marking work complete. Record failed, running
  and unavailable checks explicitly. A handover transfers context, not permission
  to expand scope, change contracts, install, publish or perform Git mutations.
- Create/update [docs/ACTIVE_WORK.md](docs/ACTIVE_WORK.md) before implementation.
  Keep the review report as the findings record; the handover tracks execution.
  Update after meaningful findings, edits, verification results or direction changes,
  and before long builds/probes with their exact command and output-log location.
- Checkpoint often enough that an unexpected interruption loses at most the current
  small step. When usage/context limits approach, if visible, finish the current
  safe step and checkpoint immediately. Do not wait for the final response or start
  work without enough budget to inspect, implement, verify and document it. Never
  invent a remaining-usage estimate; use one only if the environment provides it.
- Record running commands, working directories, logs and session/process IDs when
  available, but assume sessions may not survive restart. Do not store credentials
  or private account information in handovers.
- On resuming, read applicable instructions, the handover and linked finding;
  inspect the working tree and owning implementation. Confirm recorded edits,
  binaries and logs still match, and check whether running work completed before
  restarting it. Continue from the immediate next action. Repeat completed checks
  only when changes or unreliable evidence justify it; correct stale handover facts.
- If delegation is explicitly authorized, assign distinct task IDs and file
  ownership. Concurrent tasks use separate handovers under `docs/handovers/`, linked
  from `ACTIVE_WORK.md`; the coordinator maintains the overall task list. Report
  overlapping edits before proceeding. This rule does not itself authorize delegation.

Every active handover must include:

1. Update date/time with timezone; objective, authorization, exclusions and contract links.
2. Current task ID, status (`investigating`, `implementing`, `verifying`, `blocked`
   or `complete`), acceptance criteria and an exact immediate next action.
3. Branch/starting revision when available, unrelated changes to preserve, task-owned
   files and baseline/backup locations.
4. Confirmed evidence with file/line or log references, decisions with reasons and
   clearly labeled unresolved questions.
5. Exact verification commands, outcomes and evidence paths; running work and how
   to determine completion.
6. Ordered remaining steps, risks, rollback limited to task-owned edits, completed
   outcomes, verification limitations and the next bounded task.
