# Ant Behavior

## Essential Behavior

Ants search for food, bring it home, and repeat. They leave scent trails called **pheromones** to help each other find the way.

1. **Leave home.** Ants start at the supplied nest points, then spread out and explore.
2. **Look for food.** They check nearby scents and turn toward stronger food scent. When they find a clear scent direction, they focus on following it instead of wandering. If they lose it, they explore again. Food stays in place and gives off this scent.
3. **Leave a trail home.** While searching, ants leave **Base Pheromone**. This helps returning ants find their way home.
4. **Pick up food.** When an ant reaches food, it takes a small amount and starts heading home.
5. **Show others the way to food.** On the return trip, it leaves **Food Pheromone**. Other ants can follow this trail toward the food. The returning ant follows Base Pheromone and also remembers where home is.
6. **Start again.** Once home, the ant goes out on another search.

Scent spreads into nearby space (**diffusion**) and gradually fades (**decay**). Repeated trips keep useful trails strong. When food runs out, it stops giving off scent, and unused trails fade.

**Remember: searching ants leave a trail home; returning ants leave a trail to food.**

<details>
<summary>Detailed behavior</summary>

This explanation describes the current Nuclei V3 and V4 ant simulation in 2D and 3D.

Ants alternate between two states: **searching for food** and **carrying food home**.

| State | Mainly follows | Leaves behind |
|---|---|---|
| Searching | Food Pheromone | Base Pheromone |
| Carrying food | Base Pheromone, assisted by a direction toward home | Food Pheromone |

### 1. Leaving the nest

Ants are created only at the supplied starting points in the nest region. Each ant remembers its own starting point as home. An initial outward force, with sideways variation, spreads ants away from home. This launch force gradually fades and stops when an ant encounters a boundary or obstacle. The first few steps also help orient the ant outward.

Detecting a clear food-scent gradient temporarily reduces the outward and sideways launch forces, early outward steering, and random steering. This lets the ant turn toward food before its launch finishes.

### 2. Searching for food

Ants sample ahead, left and right. In 3D they also sample above and below. Sensor Distance and Sensor Angle control where they sample, while Rotation Angle controls their steering response.

Searching ants steer toward stronger Food Pheromone, with some random exploration. When a sampled location has no food scent, they sometimes use Base Pheromone instead. Ants that search for a long time also develop a gradually increasing pull toward home. Near home, an additional steering rule helps them complete the return.

When valid sensors detect a clear difference in Food Pheromone strength, the strongest food direction takes priority over other sensor signals. Exploration forces drop to 2% of their usual strength while that gradient is present. Flat scent, extremely weak traces, or missing scent leave normal exploration active. The ant checks this every step, so losing the gradient immediately restores normal exploration. Ants already carrying food keep their existing return behaviour.

If ant interaction with slime is enabled, scalar density can also influence their sensing.

**Ant Food stays fixed and emits Food Pheromone.** Each step, its remaining quantity is added to the food-pheromone field after ant consumption and before diffusion and decay. This lets ants detect a diffused scent before reaching the edible food.

### 3. Leaving an outward trail

Searching ants deposit **Base Pheromone** when they successfully enter another voxel. The amount is based on the ant group's Deposit setting and decreases with time since leaving home:

- The age multiplier starts near 100% and reaches 20% at 99 steps.
- Searching ants apply an additional factor of 1.1 where Food Pheromone is present, or 0.9 where it is absent.

This tends to produce stronger base trails near departure and weaker trails farther along a search. The strength depends on elapsed steps, rather than distance alone.

An ant blocked by another particle stays in place and emits no trail that step. Staying within the same voxel also produces no new trail. With non-wrapped boundaries, deposition is excluded from a boundary margin.

### 4. Finding food

When a searching ant reaches a voxel containing edible food, it consumes up to **one food unit** and switches to the carrying-food state. Its trip age resets, and its steering begins to favour returning home.

Deposition happens before pickup within a simulation step. Consequently, the arrival move still uses Base Pheromone; subsequent moves use Food Pheromone.

Food is depleted by consumption, not by scent emission. Once exhausted, a food source stops emitting. Its existing scent continues to diffuse and decay.

### 5. Returning home

Carrying ants follow **Base Pheromone**, assisted by their remembered direction toward home. They retain some random steering, so their return is not a guaranteed direct path or an exact retracing of their outward journey.

On successful moves into another voxel, they deposit **Food Pheromone**. Its age multiplier starts near 100% after pickup and reaches 30% at 99 steps. This tends to leave a stronger signal near the food and a weaker signal later in the return journey.

Searching ants can follow this return trail toward the food. Food sources and returning ants contribute to the same Food Pheromone field.

### 6. Reaching the nest

When an ant comes within its configured base movement speed of its home position—roughly one movement step—it clears its carrying state, resets its trip age and re-enables the outward launch behaviour.

The ant can then begin another search. A nest visit also resets a searching ant that returns without food.

### Diffusion, decay and trail reinforcement

| Field | Sources | Diffusion and decay |
|---|---|---|
| Food Pheromone | Remaining Ant Food and ants carrying food | Food Pheromone settings |
| Base Pheromone | Searching ants | Base Pheromone settings |

Both fields use the **shared ant Falloff and diffusion range**, while keeping their own diffusion and decay rates. Falloff works like slime: 0 keeps local weighted diffusion; 1 averages evenly across the range. Intermediate values gradually change the mixing and signal retention. Ant Food stays fixed; its emitted scent follows these food-pheromone settings. These mechanics apply in 2D and 3D.

Repeated trips reinforce trails. When ants stop using a route, its pheromones fade according to the decay settings. The edible food itself does not spread into neighbouring voxels.


</details>
