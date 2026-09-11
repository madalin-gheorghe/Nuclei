# Slime Behavior

## Essential Behavior

Slime is made of moving particles that follow and leave a shared trail. Each particle follows the same simple loop:

1. **Sense.** Check the strength of nearby trails and food signals.
2. **Turn.** Head toward the strongest signal. If the signals are equal, keep moving forward. Occasional wandering helps explore new places.
3. **Move.** Take a step. If another particle blocks the way, wait and try a different direction.
4. **Leave a trail.** After moving into a new space, add to the slime trail so other particles can follow it.
5. **Repeat.** Keep sensing, moving and adding to the trail.

**Slime Food stays in place and keeps giving off a signal.** Particles move toward it, but they do not eat it or carry it home.

The trail spreads into nearby space (**diffusion**) and gradually fades (**decay**). Busy paths get stronger because many particles keep adding to them. Unused paths fade away. This is how networks can form from simple local rules.

**Remember: slime follows trails and makes them stronger as it moves.**

---

## Detailed Behavior

This explanation describes the current Nuclei v4 GPU slime simulation in 2D and 3D.

Slime particles repeat a continuous cycle: **sense → steer → move → deposit**. They have no food-carrying state or return-to-nest behaviour.

| What slime senses | What slime leaves behind |
|---|---|
| Slime density, Slime Food, and optionally ant pheromones | More slime density |

### 1. Starting and exploring

Particles begin at their generated or supplied positions and move in their initial directions. When all sensors read the same value, they prefer moving forward. The **Wander** setting introduces occasional random changes in direction.

### 2. Sensing the environment

Each particle samples ahead, left and right. In 3D, it also samples above and below.

- **Sensor Distance** determines how far ahead it samples.
- **Sensor Angle** determines how widely the sensors spread.
- **Rotation Angle** controls how much it turns toward the strongest reading.

Particles sense the shared slime-density field. They also directly sense Slime Food's strength, and can respond to ant pheromones when those interaction settings are enabled.

### 3. Moving toward stronger signals

The particle steers toward the strongest sensor reading, retaining some of its previous direction. **Speed** controls its movement distance.

Obstacles trigger movement recovery or reversal. If another particle occupies the destination voxel, the particle stays in place, chooses a new orientation and deposits nothing that step.

### 4. Leaving a trail

When a particle successfully enters another voxel, it adds density according to its **Deposit** setting. Other particles can sense this trail and follow it.

Unlike ants, slime deposition does not weaken with trip age. It does have a movement-history adjustment: after a step that did not enter another voxel, the next successful deposit is reduced to **25%** of the normal amount. Deposition is also restricted near non-wrapped boundaries.

Staying within the same voxel produces no new trail.

### 5. Responding to food

**Slime Food remains fixed and continuously adds its strength to the density field.** That signal spreads through diffusion and attracts particles.

Slime particles do **not consume or carry this food**. Reaching it does not change their state—they continue sensing, moving and depositing around it.

### 6. Forming and maintaining networks

Repeated movement reinforces frequently used trails. Diffusion spreads density into neighbouring voxels, while decay reduces it over time. Routes receiving little reinforcement fade; repeatedly used routes persist.

This feedback can form connected trails and networks. There is no explicit shortest-path calculation—the shapes emerge from local sensing, movement, crowding and field updates.

### Dynamic population

If **dynamic population** is enabled, configured division and death rules can also change particle numbers during this cycle.



