# CPU to GPU Behavior Parity

The V3.x CPU solver is the behavioral reference for V4 GPU implementations.
Every translated feature must be checked for operation order, conditions, state
changes, and side effects. Any intentional or unavoidable difference is recorded
here.

## Ant food and nest cycle

Status: behavior aligned on 2026-08-12.

The GPU implementation now follows the CPU order:

1. Sense using the current `foundFood` state and particle age.
2. Move and deposit using that same state.
3. Check the newly reached voxel for food.
4. Check the new position for a nest visit.
5. Store pickup or nest-visit age as `1`.

This preserves the CPU behavior in which an arriving ant deposits its previous
trail type before changing state. A nest visit resets age while the ant remains
within one movement step, keeping the outward departure force active.

## Known execution differences

- CPU ant selection uses repeated Fisher-Yates list shuffles and list indices.
  GPU selection uses iteration-dependent hashes because GPU particles execute in
  parallel without a shared shuffled order. The probability and intent match,
  but individual ant trajectories are not expected to match exactly.
- CPU food and pheromone fields use floating-point object mutations. GPU food and
  deposits use fixed-point atomic updates so simultaneous particles cannot lose
  updates. Fractional food below one unit is clamped to zero on GPU instead of
  becoming negative as it can on CPU.

## Translation checklist

- Compare against the active V3.x CPU path, not an older archived implementation.
- Record the CPU order of sensing, movement, deposits, state transitions, and age.
- Compare all conditions and threshold inequalities.
- Compare reset state and persistent per-particle state.
- Compare voxel field side effects and particle output state.
- Report intentional differences before considering the translation complete.

## Particle ageing

V3 advances age inside `particleCheckParentVoxel`, which runs once during reset and
again every iteration, so when its population rules are evaluated a particle's age is
`iteration + 1`. V4 advances age only inside the `MoveParticlesAndDeposit` kernel,
which is skipped on iteration 1, giving `iteration - 1`. V4 was therefore two
iterations behind on every age gate: with a minimum age of 10 and a frequency of 5,
V3's first death pass landed on iteration 10 and V4's on iteration 15.

V4 now seeds uploaded ages with those two missing increments
(`V3AgeAlignmentOffset`). Verified with the `--parity` harness: at minimum ages of 4,
10 and 20 both solvers now apply their first population pass on the same iteration.

Divided children are unaffected -- both toolsets create them at age 0 and advance
them on the following iteration.

**Still open: division rate.** The `09_Growth 1` example definitions were compared
directly (`--gh-xml` dumps a .gh to XML): every setting, every toggle state and even
the component instance GUIDs are identical between the V3 and V4 files, so the
population difference is solver behaviour, not configuration.

Replaying those settings through `--parity` localizes it to division alone:

| Configuration | V3 at iteration 120 | V4 at iteration 120 |
| --- | --- | --- |
| death only (`--no-division`) | 2,000 | 2,000 |
| division only (`--no-death`) | 8,478 | 7,679 |

Death agrees exactly. Division does not, and the sign of the difference changes with
iteration count -- V4 trails early and overtakes later -- which is why a
100-iteration comparison in Grasshopper shows V4 ahead while short runs show it
behind.

Widening the division band isolates the cause. With `Minimum Neighbours 0` and an
effectively unbounded maximum, so that nearly every particle qualifies, the two
solvers agree to within 0.3%:

| Iteration | V3 | V4 |
| --- | --- | --- |
| 8 | 4,958 | 4,962 |
| 20 | 23,990 | 24,109 |
| 24 | 40,324 | 40,395 |

So the population machinery itself is in parity: the age gate, the 50% selection,
the birth claim and the population limits all behave identically. The neighbour
counting algorithm also matches -- both seed from per-voxel particle counts and take
a separable box sum of radius `Range`, clamped at the grid edge, then subtract the
particle itself.

What differs is **which particles fall inside a narrow band**. In the `09_Growth 1`
configuration the mean neighbour count is around 43 against a division band of 3 to
12, so eligibility is a rare tail event -- roughly 12% of the population. Small
differences in where particles end up, which are expected between a sequential CPU
solver and a parallel GPU one with different random streams and ordering, are then
amplified into large population differences.

**Practical consequence.** A band sitting far in the tail of the neighbour
distribution will always magnify small movement differences between the two solvers.
Bands closer to the distribution mean reproduce much more consistently.

### Neighbour-count distributions

Comparing the distributions directly rather than the resulting populations (the
`--trace-distribution` option reads V4's per-particle division-neighbour counts back
out of the auxiliary channel):

| Iteration | V3 mean | V3 median | V4 mean | V4 median |
| --- | --- | --- | --- | --- |
| 20 | 40.8 | 43 | **383.9** | **401** |
| 40 | 47.2 | 43 | 114.0 | 116 |
| 60 | 41.0 | 36 | 39.5 | 29 |
| 80 | 39.2 | 32 | 28.8 | 25 |

From identical starting positions V4's particles are far more clustered through the
first tens of iterations, then disperse past V3 and end up more spread out. So the
population difference is downstream of a **spatial** difference, not of the
population rules, which the wide-band test shows are in parity.

Two harness faults distorted the early readings of this and had to be fixed first:
every particle was seeded with the same +X heading, and -- more seriously -- the two
solvers were started from **different positions**. The shared snapshot builder packs
particles into a fixed 17-wide block, while the V3 side spread them across the grid,
so on a 48-cube domain V4 began roughly five times denser. That alone accounted for
the early "V4 clusters more" reading, which was wrong.

With identical positions and identical randomised headings on both sides:

| Iteration | V3 mean | V4 mean | V3 in band | V4 in band | V3 pop | V4 pop |
| --- | --- | --- | --- | --- | --- | --- |
| 2 | 45.52 | **45.52** | 0% | 0% | 2,000 | 2,000 |
| 20 | 24.7 | 17.4 | 16.7% | 19.1% | 4,053 | 5,253 |
| 40 | 27.1 | 27.2 | 7.2% | 5.2% | 5,961 | 7,502 |
| 60 | 30.5 | 32.2 | 3.0% | 2.0% | 7,069 | 8,473 |
| 80 | 33.2 | 35.8 | 1.6% | 1.4% | 7,695 | 9,130 |

**At iteration 2 the two are identical** -- same mean, same percentiles -- which
establishes that the neighbour counting, the initial state and the first movement
step all agree exactly. Divergence begins at iteration 4 and is systematic rather
than noise: V4 disperses faster over roughly the first twenty iterations, which puts
more of its particles inside the 3-to-12 band, so it divides more and pulls ahead.
The distributions converge again by iteration 40, but the population lead established
early persists.

The resulting ratio of 1.18 to 1.30 reproduces what the `09_Growth 1` definitions
show in Grasshopper (31,254 against 23,217, a ratio of 1.35), so the harness now
models the real discrepancy and can be used to validate any fix.

### Root cause: the high-deposit rule is missing from V4

Comparing the density fields directly (`--trace-density`) shows they diverge at the
first deposit, before any movement difference could accumulate:

| Iteration | V3 density sum | V4 density sum | Ratio |
| --- | --- | --- | --- |
| 2 | 142.1 | 289.7 | 2.04 |
| 4 | 1,314.6 | 1,715.7 | 1.31 |
| 6 | 2,338.6 | 3,225.3 | 1.38 |
| 12 | 3,986.8 | 5,244.4 | 1.32 |

(The V3 figure was cross-checked against the voxel objects themselves and agrees
exactly, so this is not a measurement artifact.)

Both solvers deposit only when a particle moves into a voxel that was empty. V3 then
scales that deposit by the particle's `highDeposit` flag, in `depositAtVoxel`:

```
double slimeDeposit = P.highDeposit ? depositValue : depositValue / 4;
```

`highDeposit` is set true when the particle's previous move landed in an empty voxel
and false when it landed in an occupied one (`Solver.cs` around line 5353), so a
particle that has just been travelling through crowded space deposits a quarter of
the normal amount. **V4 has no equivalent: it always deposits the full value.**

`Particle.highDeposit` exists in the V4 compatibility assembly but nothing on the GPU
reads or writes it.

The consequence chain is: V4 lays down a denser chemoattractant trail, so sensing
sees a different field, so particles disperse differently, so a different fraction
falls inside the division neighbour band, so the populations diverge. That is the
whole path from this one missing rule to the 1.2-to-1.35 population ratio seen in
both the harness and the `09_Growth 1` definitions.

**Ported, user-authorized.** V4 now keeps a per-particle high-deposit flag in its own
auxiliary channel (`HighDepositOffset`, claimed from a spare constant-buffer padding
slot, so the layout stays at 416 bytes / 104 fields) and scales the slime deposit by
0.25 when the previous move landed in an occupied voxel. The flag is cleared on reset
so a reused buffer cannot leak state.

Cost: none measurable. At 64 cubed with 200,000 particles -- the particle-sensitive
case -- the step time is unchanged at about 2.0 ms. At 300 cubed the run is
voxel-bound: raising the particle count from 20,000 to 262,144 costs only 2.2 ms in
total, so two extra buffer operations per particle are lost in the noise.

Density parity improves substantially:

| Iteration | Ratio before | Ratio after |
| --- | --- | --- |
| 2 | 2.04 | 1.38 |
| 4 | 1.31 | 1.06 |
| 8 | 1.32 | 0.99 |

**But it does not close the population gap**, which stays at about 1.21 against 1.18
before. So the density difference, although real and now largely corrected, was not
what drove the population divergence. V4 still disperses faster -- mean neighbour
count 10.4 against 17.1 at iteration 8 -- for a reason that has not been identified.

The residual density difference at iteration 2 is a concurrency-ordering effect that
may not be reproducible on a GPU: V3 tests `nextVoxel.particleCount == 0` against a
live count that other particles are mutating during the same parallel pass, whereas
V4 tests a snapshot taken before the move.

**Caveat on the distribution figures.** V4's fifth percentile reads 0 through much of
the run. Newly divided particles carry `ParticleYAxis.w = -1`, which makes the
division kernel return before writing their neighbour count, so they report a stale
zero. The V4 distribution statistics are therefore contaminated by roughly the birth
rate and should not be compared too finely against V3's until that is excluded. Also worth checking: V4's fifth percentile sits at 0 for part of the run,
meaning some particles record no neighbours at all, which may be a stale auxiliary
value for particles that skip the division kernel rather than a real reading.
