# V4 coding guide

Use [AGENTS.md](AGENTS.md) for boundaries and preservation rules.

- Match nearby C# style: four spaces and braces on separate lines in maintained
  backend code. Preserve existing public names, including legacy underscores and
  casing. Prefer descriptive PascalCase types/methods and camelCase locals for new
  internal code. Do not rename compatibility types for consistency.
- Keep host translation, graph-demand decisions, compute dispatch and drawing in
  their owning layers. Improve focused responsibilities only for a demonstrated
  change; do not split large shader/session files just to meet a size limit.
- Make state changes explicit: reset, live input update, step, paused extraction,
  fault and teardown. State/cache keys must represent every input affecting the
  result; explain reference-identity caches and mutation assumptions.
- An owner must clean up partially initialized resources as well as successful
  instances. Unmap in finally, release acquired mutexes, detach callbacks before
  disposing their targets, and unsubscribe document/canvas events at their owner.
  Borrowed frame handles do not transfer resource ownership.
- Report actionable errors at the host boundary. A swallowed exception needs an
  intentional fallback and diagnostic policy. Explain whether a fault retains the
  last output and how the user can recover. Avoid silent retries in each draw/step.
- Keep immediate-context access serialized by the established execution path.
  Do not assume a dictionary lock protects GPU work or native-handle lifetime.
  Document any new cross-thread handoff and its reset/disposal ordering.
- For GPU changes, record managed/HLSL field order, widths, offsets/alignment,
  registers and stride together. Specify world/grid units, angle units, plane axes,
  flattening order, sentinel values and float-bit integer encoding where applicable.
  Preserve barriers, ping-pong selection, pass ordering and readback fences.
- Reuse resources in steady state. Measure allocations/readbacks and synchronized
  GPU time before claiming an optimization. First-build, first-draw and correctness
  readback timings are not steady-state performance evidence.
- Comments should explain invariants, intent, synchronization or compatibility.
  Remove misleading claims only after checking their owning paths. Update the
  architecture/contract when changing an established behavior; keep historical
  benchmark results labeled with their original scope.

No analyzer or automatic formatting changes are enabled by this guide.
