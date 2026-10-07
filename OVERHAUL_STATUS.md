# League VR overhaul: status and next steps

Last updated: 2026-10-07. Every step is a git commit; `git log --oneline` shows the history.

## Done
1. **Git and LFS.** Baseline commit of the project exactly as ChatGPT left it (`cf25513`). Large assets are in Git LFS, and `PrototypeBackups/` is ignored.
2. **Readable code.** All scripts were unfolded from one-line-per-method C#. A token-for-token check confirmed nothing but whitespace changed (`2915693`).
3. **Cleanup.** Removed ten auto-running "request file" editor hooks, the one-off builders, repair tools, old test harnesses, the practice-dummy scripts and about 1.1 GB of unused generated assets (`e0407ce`).
4. **One champion system.** `PlayerChampion` plus one `ChampionKit` class per champion (Gwen, Zoe, Aatrox, Akshan, Brand, Pantheon, Yunara). Stats now use League's level-growth curve, death timers follow League, and respawn is at the fountain. All seven champions cast QWER with zero errors (`d6778ba`).
5. **Minions and turrets.**
   - Lane paths are computed once per match, with three formation columns.
   - Targeting follows League's call-for-help priority and keeps its target.
   - Separation only checks minions in the same lane.
   - Wave composition, minion stats and caps, turret damage by tier and minute, and turret shots at minions all match the League wiki for 26.x.
   - Recall is no longer cancelled by physically leaning; locomotion cancels it.
   - Main thread with about 70 minions: 33 ms before, 3.9 ms after.

## Next (planned, in order)
- **Structures:** turret plating gold before 14:00. Destroyed structures should show the rubble models in `Match/Art/Models` (turret_base_Rubble, inhibitor_Destroyed, nexus_Destroyed) instead of vanishing. Check that every turret faces down its lane, using a top-down capture.
- **Avatar:** merge `GwenAvatar` and `ChampionVRAvatar` into one solver. Apply poses in `InputSystem.onAfterUpdate` (BeforeRender) so the hands can't lag the head, which is the likely source of the jitter. Smooth the body yaw.
- **Performance:** create world health bars once, not via `GetComponent` every frame. Stop `RiftObjective.Visible()` from running every frame. Pool missiles. Cache item LINQ in `RiftEconomy`.
- **UI:** the shop re-renders whenever passive gold ticks; update labels in place instead. Add a death and respawn countdown to the HUD.
- **Tests:** assembly definitions, plus EditMode and PlayMode tests runnable with `unity cmd run_tests`. Then two full test runs with ratings and a report.
- **README:** rewrite, since the current one is out of date.

## How to verify
Open `Assets/Scenes/LeagueVR.unity`, press Play, then press Play in the menu. Desktop fallback controls: WASD to move, right mouse to look, left mouse to attack, Q/F/E/R for abilities, P for the shop, B to recall.
