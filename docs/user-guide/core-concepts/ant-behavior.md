# Ant Behavior

## Essential Behavior

Ants search for food, bring it home, and repeat. They leave scent trails called **pheromones** to help each other find the way.

1. **Leave home.** Ants start at the supplied nest points, then spread out and explore.
2. **Look for food.** They first turn toward actual food at a sensor location. If no food is detected there, they follow food scent. When they find a clear scent direction, they focus on following it instead of wandering. If they lose it, they explore again. Food stays in place and gives off this scent.
3. **Leave a trail home.** While searching, ants leave **Base Pheromone**. This helps returning ants find their way home.
4. **Pick up food.** When an ant reaches food, it takes a small amount and starts heading home.
5. **Show others the way to food.** On the return trip, it leaves **Food Pheromone**. Other ants can follow this trail toward the food. The returning ant follows Base Pheromone and also remembers where home is. Close to the nest, that homeward direction gradually takes over completely.
6. **Start again.** After delivering food, the ant first moves outward to clear the nest, then follows food scent on its next search.

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

Searching ants first check for remaining edible food at valid sensor locations. Actual food takes priority over any pheromone strength; if several sensors detect food, they prefer the largest remaining quantity. Without actual food at a sensor, they use their existing Food Pheromone sensing, with some random exploration. When a sampled location has no food scent, they sometimes use Base Pheromone instead. Ants that search for a long time also develop a gradually increasing pull toward home. Near home, an additional steering rule helps them complete the return.

Actual-food detection reduces exploration forces to help the ant turn toward the food. Otherwise, when valid sensors detect a clear difference in Food Pheromone strength, the strongest food direction takes priority over other sensor signals. Exploration forces drop to 2% of their usual strength while that gradient is present. Flat scent, extremely weak traces, or missing scent leave normal exploration active. The ant checks this every step, so losing the gradient immediately restores normal exploration. Ants already carrying food keep their existing return behaviour.

If ant interaction with slime is enabled, scalar density can also influence their sensing.

**Ant Food stays fixed and emits Food Pheromone.** Each step, its remaining quantity is added to the food-pheromone field after ant consumption and before diffusion and decay. This lets ants detect a diffused scent before reaching the edible food.

### 3. Leaving an outward trail

Searching ants deposit **Base Pheromone** when they successfully enter another voxel. The amount is based on the ant group's Deposit setting and decreases with time since leaving home:

- The age multiplier starts near 100% and decreases along a smooth curve to 2% over the ant�s calculated launch duration, then stays at 2%. Halfway through that duration, the age multiplier is 19.32%. The curve flattens smoothly into the minimum, using shape 2.5.
- Searching ants apply an additional factor of 1.1 where Food Pheromone is present, or 0.9 where it is absent.

This tends to produce stronger base trails near departure and weaker trails farther along a search. The duration adapts to the ant�s own home position, map size and group speed: approximately 75% of the distance to the farthest map corner divided by speed, rounded up to whole steps. It measures elapsed steps, not actual path length; hitting a boundary does not shorten this duration.

Outside its near-nest region, an ant blocked by another particle stays in place and emits no trail that step. Inside twice its Sensor Distance from home, ants can share voxels so arrivals and departures do not block one another. Staying within the same voxel also produces no new trail. With non-wrapped boundaries, deposition is excluded from a boundary margin.

### 4. Finding food

When a searching ant reaches a voxel containing edible food, it consumes up to **one food unit** and switches to the carrying-food state. Its trip age resets, and its steering begins to favour returning home.

Deposition happens before pickup within a simulation step. Consequently, the arrival move still uses Base Pheromone; subsequent moves use Food Pheromone.

Food is depleted by consumption, not by scent emission. Once exhausted, a food source stops emitting. Its existing scent continues to diffuse and decay.

### 5. Returning home

Carrying ants follow **Base Pheromone**, assisted by their remembered direction toward home. Outside twice their Sensor Distance, their usual sensing and steering remain active.

Inside that distance, the final movement direction gradually blends toward home. The extra takeover is 0% at the outer edge, 50% halfway in, and 100% at the nest; the contribution from all other steering fades to zero. This applies immediately to carrying ants, without an age threshold. Obstacles and field boundaries still restrict movement.

On successful moves into another voxel, they deposit **Food Pheromone**. Its age multiplier starts near 100% after pickup and decreases along a smooth curve to 2% over the same calculated launch duration used for Base Pheromone, then stays at 2%. This tends to leave a stronger signal near the food and a weaker signal later in the return journey.

Searching ants can follow this return trail toward the food. Food sources and returning ants contribute to the same Food Pheromone field.

### 6. Reaching the nest

A carrying ant finishes its last movement step at its remembered home position, provided the destination is walkable. It then clears its carrying state, resets its trip age and re-enables the outward launch behaviour. In 2D, home is projected onto the simulation plane.

After delivering food, the ant reverses its incoming heading and enters a protected departure phase. It moves outward until it clears twice its Sensor Distance from home (with a minimum departure radius of two base-speed steps). During this phase, food trails, homeward steering and other steering forces cannot pull it back into the nest. Obstacles and boundaries still restrict movement, and picking up actual food ends the phase immediately. Outside that region, normal food sensing resumes. This protection applies after food delivery; initial exploration is unchanged. Returning and departing ants may overlap inside the near-nest region. Each ant remains individually counted; sharing does not delete ants, duplicate food pickup, or lose pheromone deposits. A nest visit also resets a searching ant that returns without food.

### Diffusion, decay and trail reinforcement

| Field | Sources | Diffusion and decay |
|---|---|---|
| Food Pheromone | Remaining Ant Food and ants carrying food | Food Pheromone settings |
| Base Pheromone | Searching ants | Base Pheromone settings |

Both fields use the **shared ant Falloff and diffusion range**, while keeping their own diffusion and decay rates. Falloff works like slime: 0 keeps local weighted diffusion; 1 averages evenly across the range. Intermediate values gradually change the mixing and signal retention. Ant Food stays fixed; its emitted scent follows these food-pheromone settings. These mechanics apply in 2D and 3D.

Repeated trips reinforce trails. When ants stop using a route, its pheromones fade according to the decay settings. The edible food itself does not spread into neighbouring voxels.


</details>
