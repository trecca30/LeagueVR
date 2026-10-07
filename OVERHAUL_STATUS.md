# League VR overhaul: status and next steps

Last updated: 2026-10-07. Every step is a git commit; `git log --oneline` shows the history.

## Done
1. **Git and LFS.** Baseline commit of the project exactly as ChatGPT left it (`cf25513`). Large assets are in Git LFS, and `PrototypeBackups/` is ignored.
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
   - Every champion's attack damage now grows per level (the data had 0 growth).

## Next (planned, in order)
- **Headset pass:** the bodies and kits were tested in the editor with scripted poses. Throw speeds (1.6 m/s), the spear tilt (40°) and the hologram's position need checking in a headset.
- **Other champions:** Aatrox, Akshan, Brand and Yunara still use arms-only meshes and their old kits. They now get the full-body rig, but need first-person meshes (`build_fp_meshes` approach) and VR kits.
- **Audio:** only Gwen has sounds.
- **Structures:** turret plating gold before 14:00. Destroyed structures should show the rubble models in `Match/Art/Models` (turret_base_Rubble, inhibitor_Destroyed, nexus_Destroyed) instead of vanishing. Check that every turret faces down its lane, using a top-down capture.
- **Performance:** create world health bars once, not via `GetComponent` every frame. Stop `RiftObjective.Visible()` from running every frame. Pool missiles. Cache item LINQ in `RiftEconomy`.
- **UI:** the shop re-renders whenever passive gold ticks; update labels in place instead. Add a death and respawn countdown to the HUD.
- **Tests:** assembly definitions, plus EditMode and PlayMode tests runnable with `unity cmd run_tests`. Then two full test runs with ratings and a report.
- **README:** rewrite, since the current one is out of date.

## How to verify
Open `Assets/Scenes/LeagueVR.unity`, press Play, choose a champion and press Play in the menu. Desktop fallback controls: WASD to move, right mouse to look, left mouse to attack, Q/F/E/R for abilities (hold and release for held spells), P for the shop, B to recall.
