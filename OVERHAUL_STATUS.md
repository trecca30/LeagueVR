# League VR overhaul: status and next steps

Last updated: 2026-10-12. Every step is a git commit; `git log --oneline` shows the history.

## Done
1. **Git and LFS.** Baseline commit of the project as it was before the overhaul (`cf25513`). Large assets are in Git LFS, and `PrototypeBackups/` is ignored.
2. **Readable code.** All scripts were unfolded from one-line-per-method C#. A token-for-token check confirmed nothing but whitespace changed (`2915693`).
3. **Cleanup.** Removed ten auto-running "request file" editor hooks, the one-off builders, repair tools, old test harnesses, the practice-dummy scripts and about 1.1 GB of unused generated assets (`e0407ce`).
4. **One champion system.** `PlayerChampion` plus one `ChampionKit` class per champion (Gwen, Zoe, Aatrox, Akshan, Brand, Pantheon, Yunara). Stats use League's level-growth curve, death timers follow League, and respawn is at the fountain (`d6778ba`).
5. **Minions and turrets.**
   - Lane paths are computed once per match, with three formation columns.
   - Targeting follows League's call-for-help priority and keeps its target.
   - Wave composition, minion stats and caps, turret damage by tier and minute, and turret shots at minions all match the League wiki for 26.x.
   - Main thread with about 70 minions: 33 ms before, 3.9 ms after (`7fce5eb`).
6. **Comfort vignette setting** in Settings: OFF, VERY LIGHT, LIGHT, MEDIUM or STRONG, saved between sessions (`4365815`).
7. **Input and Gwen's body.** Each hand's buttons are independent, so fast switching between hands never sticks. Gwen has a full first-person body: legs animated by her own clips, body turn, lean and crouch, tracked arms with natural elbows and wrists, and fingers that follow grip and trigger (`5f051f5`).
8. **Gwen's kit for VR.** Physical scissor swings, giant spectral Q scissors, a dome-shaped W mist, a dash E with an afterimage, and R needles readied between the fingers and thrown by hand (`4de0310`).
9. **Shared first-person body, Zoe and Pantheon.**
   - `ChampionBodyBase` (BodyRig, clips, shadow, before-render solve) drives Gwen's body and every prefab champion's body (`ChampionVRAvatar`). The hips stand under the head, so hunched idles such as Pantheon's stand up straight.
   - Zoe and Pantheon have full headless first-person meshes, complete shadows, and props fitted to the controllers (Pantheon's spear along the grip, his shield facing where the fist points).
   - **Zoe** (ranged): soft-lock star bolts, a Paddle Star thrown by hand and paddled toward where you point, spell shards to pick up and throw, a lobbed sleep bubble that bounces into a trap, and an arc-aimed portal. See the `ZoeKit` summary.
   - **Pantheon**: physical spear thrusts, a Comet Spear thrust or javelin throw, Shield Vault aimed with the shield, Aegis Assault that blocks from wherever the real shield faces, and Grand Starfall aimed on a hologram of the Rift with a sky view of the landing. See the `PantheonKit` summary.
   - Settings has a BIG LEAPS option: watch Grand Starfall from the sky, or fade out instead.
   - Every champion's attack damage now grows per level (the data had 0 growth) (`b1c94b5`).
10. **Walking, minions, towers, sound and hands** (`be962a2`).
    - First-person walking no longer plays the run clip, which bobbed the body up and down. The body holds its idle pose, the hips stay level, and the feet step procedurally: one foot at a time, planted between steps, stride scaled to speed. This applies to every champion.
    - Minion lanes bend around turret, inhibitor and Nexus footprints. Minions are also pushed out of structures at runtime, so they no longer walk through them.
    - Every turret faces down its lane toward the enemy; the Nexus turrets face the enemy Nexus. Saved in the scene.
    - Sound: identical sound layers play once, buff loops are trimmed and faded, and distance falloff is League-like (silent beyond 26 m). Zoe and Pantheon have their own synthesized sounds (`ProceduralSfx`). Hit sounds are throttled.
    - Fingers: Pantheon and Zoe make clean fists, with the thumb folding over the fingers. Knuckles close slightly inward as the hand curls.
11. **League-style item shop** (`6b32ed4`).
    - RECOMMENDED tab per champion: starter items, the core build in order with its total, boots, situational items, consumables and trinkets.
    - ALL ITEMS tab in League's tiers, with role filters (Fighter to Support) and tier jumps.
    - Search with an on-panel keyboard, by item name or stat shorthand (ad, ap, crit, hp...).
    - Detail panel: what the item builds into, and a recipe tree with owned parts framed green and the price reduced. Also stats, passives, and the reason a purchase is blocked.
    - Bottom bar: inventory and trinket, UNDO (purchases and sales since arriving at the fountain), SELL, and gold.
    - Select an item twice to buy it. Gold ticks update prices in place instead of rebuilding the panel.

12. **Zeri** (`ZeriKit`, `ZeriEffects`).
    - **Kit:** Burst Fire on the trigger, the Living Battery charged shot on B (it executes), the W pulse from the left palm that becomes a laser through walls, the Spark Surge vault on E, and Lightning Crash on the left trigger.
    - **League rules:**
      - Data Dragon stats and League's numbers: crit rules, excess attack speed turned into damage, the 1.5 attack-speed cap, monster caps, and laser crits on monsters.
      - A tap of W or R cancels for free.
      - A B press during a burst is queued, not swallowed.
    - **Original assets:** Zeri's original sounds and voice lines were decoded from the user's own install (167 clips) and wired to the kit. Her original particle textures are used for the tracers, bolts, laser and nova.
    - **Reviews:** two rounds of agent reviews with ratings (round 2: code 7, fidelity 6.5, effects 6.5, UX 7, audio 6).
13. **Walking, ranks and regen.**
    - Walking: feet now land half a step ahead of where the hips will be. Before, they trailed up to 1.3 m behind and were dragged along; this fix applies to every champion.
    - Ability ranks follow League's skill path per champion (`skillOrder`), and ultimates unlock at level 6.
    - Health and mana regen grow per level.
14. **Summoner's Rift: collision, brush, camps, look.**
    - **Collision** (League VR > Build Rift Collision, `RiftCollisionBuilder`):
      - The map's own meshes are voxelized into a NavMesh. League's piece names decide floor or wall per triangle; stairs and ramps inside wall meshes stay walkable.
      - Only floor connected to the lanes is kept. Its outline becomes double-sided walls as tall as the wall beside them, plus a 25 cm `RiftNavGrid`.
      - Towers, inhibitors and Nexuses get capsule blockers that switch off when they fall.
      - Checked against League's own navigation grid, decoded from the user's install: about 93% of the floor agrees; the differences are edge-thin.
    - **Brush** (`RiftBrushBuilder`, `RiftBrush.shader`, `RiftBrushField`):
      - 37 patches from League's own brush cells, kept on this map's floor. Curved blade meshes sway and bend as you walk through, and blades right at the eyes dissolve.
      - Brush hides the player from enemies outside the patch (not turrets) until they attack or cast.
      - In brush, Y switches to a still view from outside the body (`BrushView`). The stick walks the champion; leaving the brush, attacking or Y returns you to first person.
    - **Camps:**
      - Every camp is at League's position (2014–2016 layout, converted and checked by the map's point symmetry), snapped to open floor and facing out of its clearing. The Dragon moved from inside a wall into its pit.
      - Murk Wolves and Rift Scuttlers were added from League's own models.
    - **Zeri's vault:**
      - The wall is measured on the grid (towers by a ray back), and the time over it uses League's dash speed (600 + move speed).
      - At most 0.3 s over the obstacle is a first-person blink. Longer vaults cut to a view behind and above that follows her over the wall at a fixed distance (her full model vaults with League's animation), then cut back.
      - Both cuts happen behind short fades.
    - **Look** (League VR > Apply Rift Look, `RiftStylized.shader`, `RiftAtmosphere`):
      - Soft banded sunlight, relief from the painted textures, cliff stretch cleanup and a sky rim.
      - A distance haze computed per pixel, a horizon ground over the voids, a patched dragon pit floor, and a plain rock boundary wall.
    - **Map audit:** five Haiku agents audited the lanes and jungles, and five reviewed the result once. The final ratings are 5–6 out of 10 per region.

## Next (planned, in order)
- **Map, from the final review:**
  - Headset check of the brush colour and density.
  - The light level of the stylized look; reviewers find it a little dim.
  - Floating rock islands beyond the NW corner.
  - A shore blend for the blue-side pond at (4, -38).
  - The brush layout is the current League grid's, so a few brushes may differ from 2014–2016.
- **Headset pass:** the bodies, kits and shop were tested in the editor with scripted poses and clicks. Need checking in a headset:
  - throw speeds (1.6 m/s), the spear tilt (40°) and the hologram's position
  - the procedural steps while walking and turning
  - sound volumes
  - shop text size at 2.1 m
- **Other champions:** Aatrox, Akshan, Brand and Yunara still use arms-only meshes and their old kits. They now get the full-body rig, but need first-person meshes (`build_fp_meshes` approach) and VR kits.
- **Audio:** only Gwen uses recorded League sounds. Zoe and Pantheon use synthesized sounds, and the other four use generic synthesized attack and hit sounds.
- **Structures:** turret plating gold before 14:00. Destroyed structures should show the rubble models in `Match/Art/Models` (turret_base_Rubble, inhibitor_Destroyed, nexus_Destroyed) instead of vanishing.
- **Performance:** create world health bars once, not via `GetComponent` every frame. Stop `RiftObjective.Visible()` from running every frame. Pool missiles. Cache item LINQ in `RiftEconomy`.
- **UI:** add a death and respawn countdown to the HUD. In VR, items are taken from the body by holding grip; the shop's EQUIP button only exists on desktop.
- **Tests:** assembly definitions, plus EditMode and PlayMode tests runnable with `unity cmd run_tests`. Then two full test runs with ratings and a report.
- **README:** rewrite, since the current one is out of date.

## How to verify
Open `Assets/Scenes/LeagueVR.unity`, press Play, choose a champion and press Play in the menu. Desktop fallback controls: WASD to move, right mouse to look, left mouse to attack, Q/F/E/R for abilities (hold and release for held spells), P for the shop, B to recall.
