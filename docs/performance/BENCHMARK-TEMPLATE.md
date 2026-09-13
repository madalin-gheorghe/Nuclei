# Nuclei benchmark template

Use this format for CPU and GPU benchmarks. Keep tables compact: workload, before timing, after timing, then a bold speedup multiplier in the rightmost column (for example **×1.56**). Keep paragraphs on single source lines so the preview wraps naturally.

## Reporting rules

- **Speedup = before time / after time.** Display it as **×1.56**, with two decimal places, at the far right. Values below ×1.00 indicate a slowdown. Avoid additional percentage or time-reduction columns; put them in prose only when useful.

- **Throughput improvement (%) = (before time / after time − 1) × 100.** Positive means faster; negative means slower. A change from 10 to 5 ms/step is +100% throughput, not +50%.
- If time reduction is useful, label it separately: `(1 − after / before) × 100`. Do not call it throughput improvement or use an unlabeled “faster” percentage.
- Use one row per matched workload and measured scope. Identify field-only, movement-only, particle-plus-field, complete solver, preview publication or application-frame measurements. Do not mix their gains.
- Prefer ms/step for solver work. Preserve ms/frame, ms/update or other original units when that is what was measured; do not relabel historical data. State whether timing is completed GPU work, synchronized wall time or CPU elapsed time. GPU submission time is not GPU execution time.
- Compute gains from unrounded timings using the same statistic for both variants. Default presentation: two decimals for speedup multipliers and three decimals for milliseconds. Optional percentages in prose use one decimal place. Identify the statistic used; do not silently substitute means for medians. Keep ranges and repeat counts in the method summary.
- When a headline spans several workloads, show the range of paired gains and link to individual timing rows. Do not combine unrelated minimum/maximum times into a fabricated comparison. Use “not measured” where data is unavailable.
- Preserve regressions and inconclusive results in the detailed report. The Major breakthroughs summary includes only gains strictly above +50% throughput; this filter does not apply to the evidence archive.
- Do not add or multiply gains across experiments without a matched combined benchmark. Keep correctness, performance and runtime eligibility separate. A failed check and its investigation belong in the report.

## Copyable report

### [Optimization name] — [date]

[One sentence describing the implementation change and the workloads where it applies.]

| Workload and measured scope | Before (ms/step) | After (ms/step) | Speedup |
| --- | ---: | ---: | ---: |
| [Species, dimensions, particle count; measured stages] | [X.XXX] | [X.XXX] | **×[X.XX]** |

**Comparison:** [Baseline and candidate versions/hashes; same-build switches or separate builds.]

**Workload:** [Boundary conditions, diffusion/decay/range, sources, population mode, speed/sensor scale, preview/output settings and seed(s).]

**Measurement:** [CPU/GPU/wall timing method; included and excluded work; mean or median and how aggregated; warmup, measured steps, repeats and variant order. Report both variants' spread and any excluded runs with reasons.]

**Test system:** [CPU, GPU, memory, OS, runtime/driver and power mode relevant to the result.]

**Validation:** [Tests and outcomes, tolerance or exact equality, small/large networks, 2D/3D coverage, long-run behavior, failed checks and unresolved uncertainty. State when a category was not tested.]

**Decision and limits:** [Keep, experimental or reject; eligible configurations and fallbacks; memory tradeoffs; whether installed. Avoid claims about unmeasured viewport FPS or different hardware.]

**Evidence and reproduction:** [Links to raw timings, correctness results, commands/scripts, build hashes and backup/rollback details.]

### Summary entry

| Stage / measured scope | Before → after | Speedup |
| --- | --- | ---: |
| [Optimization and scope] | [X.XXX → X.XXX ms/step] | **×[X.XX]** |

For cross-workload summaries, replace a single timing pair with a link to the individual paired rows. For field-only or frame measurements, change the unit in the headers and identify the scope explicitly.

Use **×1.62–1.83** for a range of paired speedups. Use an em dash for unpaired timing ranges or unavailable comparisons; if a pass is eliminated, write **Removed** instead of an infinite multiplier. Keep one primary measured scope per table; move secondary pass breakdowns to a separate table or the detailed evidence.
