"""Validate and summarize completed matched runs; never use diagnostic runs."""
import hashlib
import json
import statistics
import sys
from collections import defaultdict
from pathlib import Path

root = Path(sys.argv[1] if len(sys.argv) > 1 else "docs/performance/latest-cpu-gpu-20260913-validated")
groups = defaultdict(list)
for path in sorted(root.glob("*-round*.json")):
    row = json.loads(path.read_text(encoding="utf-8-sig"))
    assert row["Particles"] == row["FinalParticles"]
    assert row["MovedParticles"] >= row["Particles"] / 2
    assert row["Warmup"] == 300 and row["Steps"] == 30 and row["Repeats"] == 5
    key = (row["Species"], row["X"], row["Y"], row["Z"], row["Particles"], row["Scale"])
    groups[key].append(row)
assert len(groups) == 8, f"Incomplete suite: {len(groups)} of 8 cases"

summary = []
for key, runs in sorted(groups.items(), key=lambda pair: (pair[0][0] == "ants", pair[0][3] != 1, pair[0][1], pair[0][-1])):
    assert len(runs) == 4, f"Incomplete rounds: {key}"
    assert len({r["FixtureSha256"] for r in runs}) == 1
    item = dict(zip(("Species", "X", "Y", "Z", "Particles", "Scale"), key))
    for backend in ("CPU", "GPU"):
        matching = [r for r in runs if r["Backend"] == backend]
        assert len(matching) == 2
        assert len({r["BinarySha256"] for r in matching}) == 1
        path = Path(matching[0]["Binary"])
        assert hashlib.sha256(path.read_bytes()).hexdigest().upper() == matching[0]["BinarySha256"], "Binary changed during suite"
        samples = [v for r in matching for v in r["MillisecondsPerStep"]]
        item[backend] = {"MedianMs": statistics.median(samples), "MinimumMs": min(samples), "MaximumMs": max(samples),
                         "RoundMediansMs": [r["MedianMs"] for r in matching], "BinarySha256": matching[0]["BinarySha256"],
                         "MeanFieldAcrossRuns": statistics.mean(r["FieldMean"] for r in matching)}
    item["Speedup"] = item["CPU"]["MedianMs"] / item["GPU"]["MedianMs"]
    summary.append(item)

(root / "summary.json").write_text(json.dumps(summary, indent=2) + "\n", encoding="utf-8")
lines = ["| Simulation | V3 CPU: ms/step | V4 GPU: ms/step | Speedup |", "| --- | ---: | ---: | ---: |"]
for r in summary:
    shape = f'{r["X"]} × {r["Y"]}' if r["Z"] == 1 else f'{r["X"]}³'
    scale = "small network" if r["Scale"] == 1 else "large network"
    label = f'{r["Species"].title()}, {shape}, {r["Particles"]:,} particles' + (f', {scale}' if r["Species"] == "slime" else '')
    lines.append(f'| {label} | {r["CPU"]["MedianMs"]:.3f} | {r["GPU"]["MedianMs"]:.3f} | **×{r["Speedup"]:.2f}** |')
(root / "table.md").write_text("\n".join(lines) + "\n", encoding="utf-8")
print("\n".join(lines))
print(f"Validated {sum(map(len, groups.values()))} runs, 14,400 simulation steps.")
