# GPU profile aggregates

Arithmetic means of batch means, in milliseconds per simulation step. Ranges are batch ranges, not confidence intervals. `Residual` is GPU timeline elapsed minus the sum of named intervals; it includes unmarked work and queue gaps. These figures exclude viewport rasterization and Grasshopper output conversion.

| Evidence | Grid | Species / particles | Batches | GPU mean (range) | Wall mean | Move | Fields | Deposits | Dynamic | Density preview | Residual |
|---|---:|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| [dynamic128.json](evidence/dynamic128.json) | 128³ | ants / 1,436 | 5 | 12.45 (12.17–12.85) | 12.57 | 0.07 | 3.48 | 0.61 | 8.26 | 0.00 | 0.00 |
| [dynamic128.json](evidence/dynamic128.json) | 128³ | slime60 / 90,000 | 5 | 16.36 (16.05–16.73) | 16.47 | 3.63 | 3.02 | 0.51 | 8.67 | 0.00 | 0.00 |
| [dynamic250.json](evidence/dynamic250.json) | 250³ | ants / 1,436 | 5 | 73.06 (72.60–73.47) | 73.47 | 0.51 | 20.76 | 4.64 | 46.66 | 0.00 | 0.00 |
| [dynamic250.json](evidence/dynamic250.json) | 250³ | slime60 / 90,000 | 5 | 78.02 (76.73–79.69) | 78.30 | 4.84 | 22.55 | 2.85 | 46.97 | 0.00 | 0.00 |
| [food250.json](evidence/food250.json) | 250³ | ants / 1,436 | 5 | 21.74 (21.17–23.60) | 22.10 | 0.33 | 21.36 | 0.01 | 0.00 | 0.00 | 0.00 |
| [food250.json](evidence/food250.json) | 250³ | slime60 / 90,000 | 5 | 30.77 (30.19–31.42) | 31.09 | 4.92 | 22.96 | 2.84 | 0.00 | 0.00 | 0.00 |
| [intro250.json](evidence/intro250.json) | 250³ | ants / 1,436 | 10 | 21.53 (20.22–22.78) | 21.61 | 0.07 | 19.83 | 0.01 | 0.00 | 0.00 | 0.00 |
| [intro250.json](evidence/intro250.json) | 250³ | slime / 1,436 | 10 | 27.29 (25.09–29.97) | 27.42 | 0.05 | 22.46 | 2.17 | 0.00 | 0.00 | 0.00 |
| [intro250.json](evidence/intro250.json) | 250³ | slime60 / 90,000 | 10 | 32.54 (31.62–33.36) | 32.66 | 4.53 | 22.50 | 2.64 | 0.00 | 0.00 | 0.00 |
| [preview-trails250-scale2.json](evidence/preview-trails250-scale2.json) | 250³ | ants / 1,436 | 5 | 94.45 (87.72–100.55) | 95.24 | 0.07 | 19.66 | 0.02 | 0.00 | 55.55 | 18.83 |
| [preview-trails250-scale2.json](evidence/preview-trails250-scale2.json) | 250³ | slime60 / 90,000 | 5 | 69.11 (68.10–70.15) | 69.83 | 4.65 | 22.58 | 2.67 | 0.00 | 21.32 | 17.33 |
| [preview-trails250.json](evidence/preview-trails250.json) | 250³ | ants / 1,436 | 5 | 95.82 (95.23–96.92) | 96.74 | 0.08 | 21.63 | 0.02 | 0.00 | 56.00 | 17.41 |
| [preview-trails250.json](evidence/preview-trails250.json) | 250³ | slime60 / 90,000 | 5 | 72.39 (65.82–78.83) | 73.17 | 4.60 | 26.87 | 2.80 | 0.00 | 20.49 | 16.90 |
| [readback-food250.json](evidence/readback-food250.json) | 250³ | ants / 1,436 | 5 | 65.15 (63.70–66.48) | 65.38 | 0.14 | 20.63 | 0.03 | 0.00 | 0.00 | 44.31 |
| [readback-food250.json](evidence/readback-food250.json) | 250³ | slime60 / 90,000 | 5 | 45.54 (43.84–46.77) | 45.72 | 6.56 | 23.69 | 3.61 | 0.00 | 0.00 | 11.63 |
| [static128.json](evidence/static128.json) | 128³ | ants / 1,436 | 5 | 4.18 (3.84–4.85) | 4.38 | 0.29 | 3.82 | 0.07 | 0.00 | 0.00 | 0.00 |
| [static128.json](evidence/static128.json) | 128³ | slime60 / 90,000 | 5 | 8.77 (7.83–9.62) | 8.98 | 4.20 | 3.90 | 0.68 | 0.00 | 0.00 | 0.00 |
| [static250-million.json](evidence/static250-million.json) | 250³ | ants / 1,436 | 5 | 19.65 (19.42–20.11) | 19.81 | 0.31 | 19.24 | 0.09 | 0.00 | 0.00 | 0.00 |
| [static250-million.json](evidence/static250-million.json) | 250³ | slime60 / 1,000,000 | 5 | 71.36 (71.12–71.61) | 71.58 | 45.40 | 22.54 | 3.42 | 0.00 | 0.00 | 0.00 |

Requested preview scale 2 is normalized to 1 by this backend, so the scale-2 run is a repeat control, not a lower-resolution experiment.
