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
