# Gwen VR prototype

Open `Assets/Scenes/LeagueVR.unity` in the existing Unity project and press Play. The original `SampleScene.unity` and saved copies in `PrototypeBackups` are preserved.

## Controls

| Action | VR controller | Desktop preview |
|---|---|---|
| Basic attack | Right trigger, or right grip plus a physical swing | Left mouse |
| Q — Snip Snip! | Right secondary button (B) | Q |
| W — Hallowed Mist | Left primary button (X) | F |
| E — Skip 'n Slash | Right primary button (A) | E |
| R — Needlework | Left trigger; press again for each volley | R |
| Move and turn | Existing XR thumbstick controls | WASD, right mouse to look; Z/C snap turn |
| Teleport | Existing XR teleport gesture/input | Use VR controllers |

Combat input pauses while an XR teleport ray is active. Desktop fallback runs only when no active headset is present. Tracking loss does not transfer camera control to the mouse. Controller labels assume an Oculus-style layout; actions can be rebound in `Assets/_Game/LeagueVR/Data/GwenVR.inputactions`.

The wrist display shows health, cooldowns, Q stacks, R recasts and defeated targets. The practice echoes use the imported Gwen model; they are sparring opponents, not additional playable champions. One is passive, while the others exercise damage, mist protection, slowing and respawning.

## Gameplay implemented

- A Thousand Cuts: additional damage based on target maximum health and healing from champion targets.
- Snip Snip!: two snips plus up to four basic-attack stacks; stronger final snip and a true-damage center.
- Hallowed Mist: a timed protective area, bonus resistances and one reposition; outside attackers are blocked.
- Skip 'n Slash: a short collision-checked step, empowered attacks and a cooldown refund on the first empowered hit.
- Needlework: three piercing volleys containing 1, 3 and 5 needles, with damage and slowing.
- Health, physical/magic/true damage, teams, hit detection, world obstruction, cooldowns, death and respawn.
- Imported animation hooks, simple thread effects and controller haptics.

Mechanics follow the [official Gwen overview](https://www.leagueoflegends.com/en-gb/champions/gwen/) and [ability rundown](https://www.leagueoflegends.com/en-us/event/gwen-abilities-rundown/). Values are prototype tuning for VR, not a claim of current League patch accuracy. Adjust them in `Assets/_Game/LeagueVR/Data/Gwen.asset`.

## Map correction

The export's visual orientation is restored to its imported 270-degree X rotation. Its floor triangles face downward for collision, so 118 separate collision meshes reverse triangle winding while leaving the original visible meshes intact. Map scale is now 6, with an unscaled XR rig and a grounded spawn.

Three map parts use an untextured `Merged_materials` material in the source GLB. A named fallback material reuses an existing Rift ground texture on those parts. This covers the white surfaces; it cannot restore artwork absent from the export. Detached scenery and some incomplete geometry remain in the imported map. No replacement map was downloaded.

Black capsule targets were replaced by animated Gwen practice echoes. The large tutorial placard was removed, and status text was made readable. Generated scissors and needle meshes now contain only their referenced vertices, with corrected bounds and needle centering.

## Assets and XR

Gwen contains one skinned mesh with five material slots, a 141-joint skeleton and 96 imported legacy animation clips. The prototype reuses its body, scissors, needle, materials and animation component. First-person body geometry hides head triangles to reduce face clipping. Arm alignment is an initial two-bone approximation; headset fitting still needs testing.

The Rift contains 118 mesh renderers and 100 materials, using predominantly double-sided unlit rendering. It has no animation rig.

The existing XR Origin, tracked camera/controllers, hand support, locomotion providers, teleportation and OpenXR setup are retained. The scene adds combat components and world collision layers. Original OpenXR settings and both XR rig prefab assets are unchanged against the saved baseline. Demo environment, interactables and tutorial roots are removed from the playable derivative scene; dependency assets remain available.

## Code organization

All additions are under `Assets/_Game/LeagueVR`:

| Part | Responsibility |
|---|---|
| `Combatant`, `CombatProjectile` | Reusable health, mitigation, damage guards, projectile collision and slowing |
| `GwenAbilities`, `GwenTuning` | Gwen's ability rules and adjustable values |
| `GwenVRInput` | Independent combat bindings and desktop preview |
| `GwenAvatar`, `GwenFeedback`, `ChampionHitbox` | Body/weapon presentation, effects, HUD, haptics and hurtbox alignment |
| `TrainingSentinel` | Simple practice opponent behavior |
| `Editor` | Explicit build, map repair, inspection and validation commands |
| `Generated` | Derived body/weapon meshes, collision meshes, materials and needle prefab |

Use **Tools → League VR → Run Gameplay Smoke Tests** to check the scene and run combat tests. The test runner restores Edit Mode afterward. Reports are written under the project's `Logs` folder. Repair and rebuild commands are explicit actions; opening Unity does not rebuild or reposition this scene automatically.

## Verification — October 2, 2026

The corrected scene compiled and ran in Unity 6.6. All 16 scene checks and 27 Play Mode gameplay checks passed after isolating test input. Checks cover spawn grounding, preserved XR references, map orientation/scale/material fallback, attack rate and damage, passive healing, Q stacks and sequence, mist protection, dash movement and refund, R recasts/projectile hits/slow, wall obstruction, death and respawn. The first-person Game view was visually inspected after the map and target corrections. Four baseline file comparisons confirmed the original OpenXR settings and XR rig prefab assets are unchanged.

## Current limits

This is a local combat prototype. It does not yet include a full match, minion waves, towers, objectives, progression, networking or additional champion kits. The map export still needs art cleanup and performance work. Real headset/controller ergonomics, stereo rendering, teleport behavior and standalone headset performance have not been physically verified. The existing editor's Unity AI subscription message is unrelated to the gameplay scripts.

The source map metadata identifies Jenioss's “Summoner rift 3d export” as a study asset with CC-BY-NC-ND 4.0 metadata. Original imported assets remain intact.
