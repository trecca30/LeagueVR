# League VR overhaul: status and next steps

Last updated: 2026-10-13. Every step up to Zeri and the Rift is a git commit; `git log --oneline` shows the history. Gragas (step 16) is not committed yet.

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

15. **Gwen's original kit, Zeri's vault preview, test bots.**
    - **Gwen:** her own particle textures (decoded from the user's install) now draw the passive flare, Q scratch marks and centre cut, W ground ring and threads, E dash streak and R needle threads. Her 18 English voice events and 19 more ability sound events are imported and wired with League-like chances and cooldowns. The R recast window is League's 6 s. Four Haiku reviewers rated the result (visuals 6, code 7, fidelity and audio 7, VR UX 4), and their fixes were applied.
    - **Zeri:** Spark Surge vaults any wall or tower in the dash's path, as in League, and shows where it will land before you press: a yellow arc for a vault, red when the wall cannot be cleared.
    - **Test setup:** Pantheon bots march mid and fight (replaced by super minions in item 17), every round starts at level 12 with class items, and minion waves come 1.7 times as often.

16. **Gragas, the Rabble Rouser** (`GragasKit`, `GragasEffects`; not committed).
    - **League's own assets:** his model, icons, 13 particle textures and 26 sound events (15 effects, 11 English voice lines, 82 clips) are decoded from the user's install. No base-skin audio exists for his passive, barrel ferment, recall or kill lines, so those are left out.
    - **Kit:** keg attacks in the right hand; Happy Hour heals 5.5% of max health after each cast; Q Barrel Roll (aim with the right palm, ferments, bursts); W Drunken Rage (the arm lifts the keg to the mouth while he drinks, damage reduction, empowered splash); E Body Slam (a real charge at League's speed since item 17, a knock-up and a 1 s stun, 40% cooldown refund; a wall that stops the charge hits nobody); R Explosive Cask (aim with the left palm, throw, knockback).
    - **Shared changes:** knockbacks (`UnitDisplacement`, which honours stuns), percentage damage reduction (`IDamageReduction`), a grip override and fist curl for the drink, a per-kit weapon sweep radius, and a sweep pause after dashes and snap turns.
    - **Checks:** `gragas_round2_test` 8/8 and the nine-champion smoke test with 0 console errors (scratchpad). Two rounds of five agent reviews (eyes, outside view, League, VR, robustness), with the fixes applied after each round.
    - **Assumed or open:** the Happy Hour level breakpoints and when Drunken Rage's 2.5 s starts; the research file still has a conflicting "Data Dragon 0" attack-damage line (the asset uses the wiki's 3.5 per level); the melee trigger reaches 2 m (a VR choice; League's is about 1.2 m).

17. **Hands for every champion, Gragas's animations, super minions** (not committed).
    - **Fingers:** every champion's fingers follow the Meta controllers' touch sensors, like Meta's own hands: lift the index off the trigger to point, lift the thumb off the buttons and stick for a thumbs-up, squeeze the grip for a fist; Touch Pro's trigger curl is read too. Works for numbered rigs, Brand's older rig and Gragas's mittens.
    - **Brand and Yunara** have real first-person bodies (arms, torso) instead of floating hands, so their arms and fingers move like everyone else's.
    - **Gragas:** the keg is held like League's big mug. Body Slam is a real charge: you rush along with him behind a comfort vignette and stop on the first enemy your belly meets (BLINK CHARGES in Comfort settings brings back the blink). Drunken Rage's empowered hit is League's two-handed smash: both hands lift the keg over his head and slam it down toward the target, and a fountain of brew bursts out of it.
    - **Menu:** the left stick click opens the menu (recall moved to the right stick click), and the pointer shows a visible laser and dot over the menu and shop.
    - **Super minions** replace the Pantheon bots: two at the start, two more every second wave, up to eight alive.
    - **Gragas's basic attacks** play League's own: holding the trigger, he swings the keg at the enemy in front himself (the Attack1 hook and the Attack2 bonk in turn, the Crit swing with League's crit sounds), and the hit lands when the keg does.
    - **Body Slam taps:** the rig's built-in jump sat on A, the same button as E, so every E press also jumped; it is off now. A tap charges where you walk or look; holding still aims with the right hand. An enemy right at your belly still gets a short lunge.
    - **Smoke test:** runs after every change on the three finished champions (Gwen, Zeri, Gragas), with the head held at standing height.

18. **Zeri and Gwen animations** (roadmap of 2026-10-15; not committed).
    - **Zeri:** her basic attack is League's own: the left hand flicks a spark at the enemy. Ultrashock Laser is thrown from the left hand, the laser is as wide as it hits and green-gold instead of white. Spark Surge rushes in first person (dashes and short vaults through towers and thin walls, behind a quick flash of her electricity); long vaults are cleaned up; the preview arrow is smoother, with chevrons and an arrowhead.
    - **Gwen:** animated basic attacks after League's three attacks (thrust, sweep, backhand, a lunge on a crit), two-handed Snip Snip!, a spinning-scissors flourish for Hallowed Mist that blooms from her palm, Skip 'n Slash as a real dash ending in a two-handed slash, and a thread from the scissors to the needles for Needlework.
    - **Checks:** Gwen capture test 24/24, Zeri captures reviewed, smoke test 0 errors, Haiku 5.5 reviews of both.
    - **Review fixes applied (2026-10-16):** Gwen's blade must pass the target (3D, 0.6 m) to hit, so the strike poses are at waist height (the blade had passed over small minions at head height); the E dash is smoothstep, ramps its hold out, and the two-handed slash starts from the hold; the dash works without rushing too; Needlework's needles stay in the fingers until the flick, and the left hand readies them at chest height; the mist grows from the palm and blooms again on recast, with a stronger dome; the Q scissors are further out and smaller; the trails run only in the strike window. Zeri: the short-vault cover holds longer and fully covers the view; the long vault has the comfort vignette and a level camera; the pulse hits what it shows (0.34-0.55 m); the throw draws back 25 cm and thrusts 30 cm; the preview's landing arc is thinner and its ring larger. Checks: Gwen 12/12 attacks and 12/12 Q/W/E/R; Zeri captures reviewed; smoke test 0 errors.

## Next (planned, in order)
- **User roadmap (2026-10-15), in this order; each champion gets Haiku 5.5 review agents (works? adapts? makes sense? looks right?):**
  - **Zeri** and **Gwen**: done (item 18); headset check pending.
  - **Hwei**: done (see STATE_DICTIONARY.md); Haiku 5.5 reviews applied, headset check and the no-sound problem pending.
- **Map, from the final review:**
  - Headset check of the brush colour and density.
  - The light level of the stylized look; reviewers find it a little dim.
  - Floating rock islands beyond the NW corner.
  - A shore blend for the blue-side pond at (4, -38).
  - The brush layout is the current League grid's, so a few brushes may differ from 2014–2016.
- **Headset pass:** the bodies, kits and shop were tested in the editor with scripted poses and clicks. Need checking in a headset:
  - Gragas: the drink (the keg rises to the lower right of the view), the keg's size and weight in the fist, the comfort of the Body Slam rush, the two-handed smash, and the cask throw
  - the finger poses on real Touch / Touch Plus / Touch Pro controllers
  - throw speeds (1.6 m/s), the spear tilt (40°) and the hologram's position
  - the procedural steps while walking and turning
  - sound volumes
  - shop text size at 2.1 m
- **Other champions:** Aatrox and Akshan still use their old first-person meshes, and all four (Aatrox, Akshan, Brand, Yunara) their old kits; they need VR kits.
- **Audio:** only Gwen uses recorded League sounds. Zoe and Pantheon use synthesized sounds, and the other four use generic synthesized attack and hit sounds.
- **Structures:** turret plating gold before 14:00. Destroyed structures should show the rubble models in `Match/Art/Models` (turret_base_Rubble, inhibitor_Destroyed, nexus_Destroyed) instead of vanishing.
- **Performance:** create world health bars once, not via `GetComponent` every frame. Stop `RiftObjective.Visible()` from running every frame. Pool missiles. Cache item LINQ in `RiftEconomy`.
- **UI:** add a death and respawn countdown to the HUD. In VR, items are taken from the body by holding grip; the shop's EQUIP button only exists on desktop.
- **Tests:** assembly definitions, plus EditMode and PlayMode tests runnable with `unity cmd run_tests`. Then two full test runs with ratings and a report.
- **README:** rewrite, since the current one is out of date.

## How to verify
Open `Assets/Scenes/LeagueVR.unity`, press Play, choose a champion and press Play in the menu. Desktop fallback controls: WASD to move, right mouse to look, left mouse to attack, Q/F/E/R for abilities (hold and release for held spells), P for the shop, B to recall.
