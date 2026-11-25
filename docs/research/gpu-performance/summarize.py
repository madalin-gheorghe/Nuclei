"""Recompute the research tables from the preserved GPU timestamp batches."""
import json
import statistics
from pathlib import Path

ROOT = Path(__file__).resolve().parent


def stage(name):
    if name.startswith("Diffusion") or name in ("AntPairedFields", "AntFoodField", "AntBaseField", "ScalarDecay"):
        return "Fields"
    if name.startswith("ApplyDeposits"):
        return "Deposits"
    if name.startswith("ClearCounts") or name == "CountParticles":
        return "CountBookkeeping"
    return name


summaries = []
for path in sorted((ROOT / "evidence").glob("*.json")):
    data = json.loads(path.read_text(encoding="utf-8-sig"))
    if "Results" not in data:
        continue
    groups = {}
    for row in data["Results"]:
        key = (row["Species"], row["Particles"], row["Preview"], row["Variant"])
        groups.setdefault(key, []).append(row)
    for (species, particles, preview, variant), rows in groups.items():
        timings = [r["GpuMsPerStep"] for r in rows]
        categories = {}
        for row in rows:
            for name, value in row["PassMsPerStep"].items():
                label = stage(name)
                categories[label] = categories.get(label, 0) + value / len(rows)
        avg = statistics.mean(timings)
        residuals = [r["GpuMsPerStep"] - sum(r["PassMsPerStep"].values()) for r in rows]
        summaries.append(dict(
            Evidence=path.name, Grid=data["Grid"], Species=species,
            Particles=particles, Preview=preview, Variant=variant,
            Batches=len(rows), Warmup=data["Warmup"], StepsPerBatch=data["Steps"],
            RequestedScale=data["Scale"], EffectiveScale=10 if data["Scale"] >= 10 else 1,
            MeanGpuMs=avg, MedianGpuMs=statistics.median(timings),
            MinGpuMs=min(timings), MaxGpuMs=max(timings),
            MeanWallMs=statistics.mean(r["WallMsPerStep"] for r in rows),
            MeanSubmitMs=statistics.mean(r["SubmitMsPerStep"] for r in rows),
            StageMeanMs=categories,
            StagePercentOfMeanGpu={k: 100 * v / avg for k, v in categories.items()},
            UnattributedMeanMs=statistics.mean(residuals),
        ))
(ROOT / "summary.json").write_text(json.dumps(summaries, indent=2) + "\n", encoding="utf-8")
lines = [
    "# GPU profile aggregates", "",
    "Arithmetic means of batch means, in milliseconds per simulation step. Ranges are batch ranges, not confidence intervals. `Residual` is GPU timeline elapsed minus the sum of named intervals; it includes unmarked work and queue gaps. These figures exclude viewport rasterization and Grasshopper output conversion.", "",
    "| Evidence | Grid | Species / particles | Batches | GPU mean (range) | Wall mean | Move | Fields | Deposits | Dynamic | Density preview | Residual |",
    "|---|---:|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|",
]
for row in summaries:
    c = row["StageMeanMs"]
    lines.append(
        f'| [{row["Evidence"]}](evidence/{row["Evidence"]}) | {row["Grid"]}³ | {row["Species"]} / {row["Particles"]:,} | {row["Batches"]} | '
        f'{row["MeanGpuMs"]:.2f} ({row["MinGpuMs"]:.2f}–{row["MaxGpuMs"]:.2f}) | {row["MeanWallMs"]:.2f} | '
        + " | ".join(f"{c.get(key, 0):.2f}" for key in ("Move", "Fields", "Deposits", "DynamicPopulation", "DensityPreview"))
        + f' | {row["UnattributedMeanMs"]:.2f} |'
    )
lines += ["", "Requested preview scale 2 is normalized to 1 by this backend, so the scale-2 run is a repeat control, not a lower-resolution experiment.", ""]
(ROOT / "SUMMARY.md").write_text("\n".join(lines), encoding="utf-8")
print("\n".join(lines))
