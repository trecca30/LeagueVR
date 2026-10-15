# LeagueVR state dictionary (2026-10-15; last commit 7a5f792; Gragas, fingers, menu and super minions uncommitted)

## Project
- Unity 6000.6.0f1, URP (MSAA 4x, active quality "Low" = Performance URP Config), OpenXR, XRI 3.6.
- Scene: `Assets/Scenes/LeagueVR.unity`. Repo: trecca30/LeagueVR (public). Git LFS is used (`Generated/**/*.asset`, png, wav, obj, glb).
- User rules:
  - No Claude co-author trailer in commits.
  - Never write to `F:\Riot Games` (reading is OK).
  - Ask before downloads.
  - Subagents run on Haiku and get countable budgets (25 tool calls, 10 image reads), then write a State Dictionary file. They cannot measure tokens.
  - Design reference: the user finds STARVAULT (first-person 5v5 VR MOBA) close to League; look at how it solves a problem in first person when building new champions or features.
- Not committed: `Assets/TextMesh Pro/.../LiberationSans SDF - Fallback.asset` (font cache) and `Assets/Resources.meta`.

## Coordinates
- 1 m ≈ 103 League units.
- `unity_x = .00973*Lx - 74.5`, `unity_z = .0097*Lz - 64.9`.
- The map is point-symmetric about (-2.2, 7.22).
- Floor height: lanes y≈0, river -1.2, bases +0.45, fountain pads 1.2.

## Champions
- 9 champions:
  - Gwen, Zoe, Pantheon, Zeri and Gragas: VR kits with full bodies.
  - Aatrox, Akshan, Brand and Yunara: old kits.
  - The user counts only Gwen, Zeri and Gragas as working (2026-10-15): checks and smoke tests cover those three.
- **Ranks:** `ChampionDefinition.Rank` follows League's path by `skillOrder`:
  - first ability ranks up at 4/5/7/9, second at 8/10/12/13, third at 14/15/17/18
  - all three basic abilities start at rank 1
  - R: rank 0 below level 6, then ranks up at 6/11/16
  - `PlayerChampion.Unlocked` blocks R; the HUD shows "LV 6".
- **Regen growth:** `healthRegenGrowth` and `manaRegenGrowth` from Data Dragon.
- **Walking fix** (`BodyRig.StepFeet`):
  - step length lerps .45→.95 m with pace
  - step time = clamp(stepLength / speed, .22, .42)
  - landing target = Home + v·duration·1.5
  - threshold = .5·v·duration
  - hips lag at most .02 m while moving
- **`ChampionBodyBase.Hidden`** hides the first-person body and its shadow.
- **`PlayerChampion`:**
  - `FeetOverride` and `BrushView`
  - `CanAct` is false while the brush view is active
  - `HiddenFrom` = kit OR brush (not turrets; reveal 1.5 s after attack, cast or damage)
- **`ChampionPuppet.Spawn(player, at, rotation)`:** the full `Definition.model` without scripts or colliders; `Play(loop, seconds, clipNames...)`.

## Zeri (ZeriKit, ZeriEffects)
- **Inputs:** RT burst (7 rounds, 3 when Overcharged), B battery shot (queued 1.6 s when the gun is busy), X hold W (tap cancels), A E, LT hold R.
- **E vault:**
  - `PlanSurge` measures the wall: `RiftNavGrid.CrossWall` for map walls; for towers a reverse ray against the collider.
  - Dash speed = (600 + MS)/100 m/s.
  - Over the obstacle ≤ 0.3 s, or the BIG LEAPS setting is FADE: blink.
  - Otherwise `VaultFromOutside`:
    - fade, rig to a vantage point (behind the path along the head yaw, 4.5+ m back, 2.4 m+ up); the view keeps that offset from her for the whole vault (it follows her arc)
    - puppet plays `Spell3_Vault_Short/Med/Long`
    - fade back and `MoveFeet(landing)`
  - `Vaulting` makes her untargetable and blocks damage.
  - League rule (since 2026-10-12): a wall or structure anywhere in the dash's path is vaulted (`SurgeWallReach` = dash distance − .3), up to 12 m.
- **Surge preview** (`PlanPreview`, `ZeriVisuals.SurgePreview`): E is held (`HoldToCast` slot 2): the preview shows only while A is held, release dashes. Same plan as the cast, 20 Hz. Yellow arc + ring = vault; red = no room or a wall too thick to clear; green = plain dash. Drawn through walls. Test: `surge_hold_test.py` 6/6.
- **W laser** half-width 3 m (was 1.8); pulse hit radius +.9 m (was +.45).
- **Round-2 review ratings:** code 7, fidelity 6.5, body 4 (the coil glow has since been fixed), effects 6.5, UX 7, audio 6.
- **Tests:** `zeri_round2_test.py` 13/13 (old scratchpad); `vault_test.py` 5/5 and `surge_preview_test.py` 6/6 (2026-10-12).
- **Roadmap pass (2026-10-15), all with League's clips as reference (`clip_strips.py`, e.g. Attack1a, Spell2, Spell3_Vault_*):**
  - **Battery shot = League's left-hand attack:** B starts a flick of the left hand through `GripOverride`/`HandOverride` (`ZeriVisuals.LeftGrip`, `LeftFingers`): cocked just under eye height out to the left (in view), snapped out at the target; the shot leaves the fingers at `FlickSnap` 0.14 s (`FireBattery`; a dead or escaped target refunds the attack timer), the hand is back at 0.34 s. Sparks gather between the fingers while it winds up (bigger for a charged or executing shot). Battery haptics moved to the left hand. The gun no longer kicks on B.
  - **W throw:** the release draws the palm back a hand's width and thrusts it out along its own aim line; the pulse leaves at `ThrowRelease` 0.1 s from the aim line (from shoulder height it flew over low walls), with a small ring and lightning around the forearm. The pulse is green-gold in a blue halo, starts at 30% size and grows over 2.5 m, two sparks orbit it (double helix).
  - **W laser:** as wide as its hit area (`LaserHalfWidth` 3 m): a translucent green band, bright crackling edges, a golden core and a pale centre (no white); the telegraph shows the whole hit area (faint band, edge lines, centre line).
  - **E rush:** a plain dash and a short vault (thin wall or tower, ≤ 0.3 s over it) rush in first person at her dash speed behind `RushVignette` (`SurgeRush`, distance-driven); a short vault flashes her electricity fully over the view at 0.5 m before the obstacle, moves her through while covered (`FadeCoveredIn`), and she rushes out the far side. Long vaults still use the outside view; its fades are now 0.05 s quick flashes (the old yellow wash lasted half the vault), the body hides before the view moves, her model plays League's take-off clip at its own speed and then `Spell3_Vault_Long_Loop`, and trails green light (`VaultTrail`). With RUSH DASHES off (`ComfortSettings.RushCharges`, now covering all dashes) dashes and short vaults blink as before.
  - **E preview:** re-planned every frame (was 20 Hz); chevrons run along the arc toward the landing and a ground arrowhead points on along the dash; all pieces hide on release.
  - Tests: `zeri_now.py <label> [vault,tower,dash,laser,flick,throw,preview]` (slow-motion strips), `laser_probe.py` (follows the pulse).

## Gwen animations (2026-10-15, `GwenMoves`)
- **`GwenMoves`** holds every first-person animation; `GwenAvatar` asks it for the scissors' pose (`ScissorsOverride`), the blade opening (`BladeOpenOverride`, the blades now turn apart about the pivot so the handles spread like real scissors), the trail (`TrailOverride`), and both arms' grips and fingers (`GripOverride`, `HandOverride`). Poses are eye-relative (right, forward, up) scaled by arm length (0.56 m reference); blending between holds moves the fist straight and turns the scissors about it. Measured scissors geometry (root space, unscaled): pivot (0, -0.022, 0.08), blade B's handle ring (-0.07, 0.1, -0.32).
- **Basic attacks:** the trigger's attack (`BasicAttack`) cycles League's Attack1 thrust, Attack2 sweep, Attack3 backhand; a critical strike (rolled at the start via `RollNextCritical`) is a lunge that snips twice. 60% of the attack time (0.3-0.5 s); the hit lands at the strike (50%) as the blades snap (`LandSwing`, target within reach + 1 m, else the timer refunds). Physical swings still hit at once. Abilities cut a swing short (`CancelSwing`).
- **Q:** the left hand takes blade B's handle (`SnipSetup` 0.1 s), both hands hold the scissors level in front and snip once per snip, the last one pushed forward; the spectral scissors and damage are unchanged.
- **W:** the scissors rise on the right, open wide and spin a full turn, then snap shut; the left palm opens upward and the mist blooms from it (flare and star sprites; the dome grows over 0.4 s).
- **E:** a real dash (`Dash`, 0.25 s, ease-out, `RushVignette`) with both hands on the scissors, then an automatic two-handed slash on the enemy in front (`DashDone` → `BasicAttack`); while empowered every attack is a two-handed snip (`TwinSnip`). Blink with RUSH DASHES off.
- **R:** while held, a glowing thread runs from the scissors' tips to the needles (`GwenAvatar.ThreadNeedles`); a plain release flicks the left hand (needles leave at `ThrowRelease` 0.1 s), a real throw keeps the hand; the scissors snip the thread as the needles go.
- **Tests:** `gwen_now.py <label> [attacks,q,w,e,r]` 24/24 (each move plays, hits land partway, scissors' centre line ≥ 0.29 m from the eyes, left hand on the second handle in Q and E at 0.00 and 0.06 m, the dash moves her, the thread shows). League clip strips: `clip_strips.py` on `Assets/_Game/Gwen/gwen.glb` (follows her hips in open air; the clips carry root motion).

## Gwen original kit (2026-10-12)
- **Textures:** 9 of her 71 decoded base-skin particle textures in `Champions/Art/Gwen/Resources/GwenFx` (loaded as `"GwenFx/<name>"` through `ZeriFx.Tex`; sprites and beams use the shared `ZeriSprite`/`ZeriBeam`). Passive flare, Q scratch + centre cut, W ground ring + holy-string threads, E dash streak, R thread trail. Decodes and index: `scratchpad/gwen_vfx`.
- **Audio:** 18 voice events (105 clips) + 19 missing SFX events (40 clips) in `Audio/OriginalLeague` and the bank. Voice through `ChampionAudio.Voice` (chance + cooldown), kill lines on champion kills, move lines after standing, recall line + channel. Decodes: `scratchpad/gwen_audio` (144 voice clips; 17 champion-specific kill lines not imported).
- **Kit:** R recast window 6 s, lockout 1 s, slow 1.5 s from `GwenTuning`. R stage resets when the window expired while held. The audio counts the R cast in progress as RStage + 1 (the signal comes before the kit updates).
- **Review round 1:** visuals 6, code 7, fidelity + audio 7, VR UX 4 (fixes applied after; `scratchpad/gwen_review/round1_scores.md`). Test: `gwen_round_test.py` 9/9.

## Match test setup (2026-10-12)
- **Super minions instead of the Pantheon bots (2026-10-15):** `RiftMatch.SpawnSupers` sends enemy super minions down mid from their base: `startingSupers` 2 at round start, `superGroupSize` 2 more every `superGroupWaves` 2 waves, at most `maxSupers` 8 alive (the fields carry the old bot values via `FormerlySerializedAs`). They are ordinary `RiftMinion`s (kind Super). `RiftBot` (a kitless champion bot) is no longer spawned; the class is kept for future champion bots. Test: `supers_test.py` (2 at the start, 6 alive by wave 9, no bots).
- **Test loadout** (`RiftMatch.testLoadout`, on): level 12 and 6 random finished items for the champion's class (marksman: crit / attack speed / on-hit without health, resistances or AP; mage: AP; others: health or resistances).
- **Minion waves** 1.7× as often (`RiftRules.minionSpawnRate`).
- **Wrist display:** wider gate with hysteresis, 0.1 s dwell, fade in/out, larger layout.

## Rift collision (League VR > Build Rift Collision, `RiftCollisionBuilder`)
- **Input pieces:** all of "Summoner's Rift/VR rebuilt map" except `bk_distant_horizon_a`, `Boundary foundations` and `Foundation collision`.
- **Floor pieces:** names starting `grnd_terrain`, `Foundation`, `mage_spawn_pad`, `pally_platform`, `_chunk_base_north_platform`.
- **Wall-piece triangles count as walkable when:**
  - flat (|ny|>.8) and less than .55 m above the floor raster, or
  - less than .5 m above it and less than .3 m tall (stair risers).
- The export is mirrored, so walkable triangles are rewound to face up.
- **NavMesh settings:** radius .1, height 1.4, climb .5, slope 45, voxel .075, tile 128, bounds centre (0, -4, 8) size (180, 44, 184).
- **Grid first, then walls (fixed 2026-10-12):** the NavMesh's tiles (9.6 m) have T-junctions, so triangle-edge flood fills and outlines broke at every seam and left walls in open floor. Now: a cell is navigable when a NavMesh triangle covers its centre; floor is kept by 8-connected flood from the 9 seeds, stepping at most .55 m; walls are the sides of kept cells that face a non-kept cell.
- **Floor kept:** only cells connected to 9 seeds (both fountains, lanes, centre).
- **Outputs:**
  - `Generated/RiftNav/RiftWalls.asset`: double-sided quads on the grid's cell edges, height = map top (1.4–7 m).
  - `RiftNavGrid.asset`: origin (-90, -84), cell .25, 720×736; arrays walkable/floor(cm)/brush(patch id).
  - `RiftNavMesh.asset`.
  - Scene object "Rift Collision" (LeagueWorld layer, `MeshCollider` + `RiftCollision`, which sets `RiftNavGrid.Active`).
  - A "Movement blocker" capsule on every `RiftStructure` (radius = Footprint·.75).
  - Preview: `Temp/claude-pov/rift_nav/navgrid.png`.
- **League grid comparison:**
  - League's navgrid (`ASSETS/Maps/NavGrid/Map11/AIPath.aimesh_ngrid` in Map11.wad.client): v7 header; cells 295×296 of 50 units; 48-byte cells; then u16 flags at 39+48·N.
  - Flags: 0x1 brush, 0x2 wall, 0x4/0x46 structures.
  - Agreement with our grid about 93%.
  - Decoded copy and comparison: `scratchpad/navgrid/`.

## Brush (League VR > Build Rift Brush, `RiftBrushBuilder`)
- **Mask:** `Generated/RiftNav/LeagueBrush.bytes` (720×736, from League's brush cells AND our walkable cells).
- **Patches:** 37 (8-connected, ≥10 cells).
- **Blades:** 4 per cell, 4 segments, height .6–1.45 m (shorter at edges).
- **Shader** `LeagueVR/Rift Brush`: wind, push (`_RiftBrushPush`, set by `RiftBrushField` from `PlayerChampion.Feet`), dither-dissolve within .55 m of the eye. Tip (.36, .48, .17).
- **Palette:** dark teal.
- **`BrushView`** (Y in brush; T on desktop):
  - still vantage 3.4 m behind and 2.1 m up, puppet idle/run
  - the stick moves the champion on the grid
  - leaving the brush, an action press, or Y returns to first person

## Camps (League Match/Jungle & Objectives)

| Camp | Blue side | Red side |
|---|---|---|
| Blue Sentinel | (-37, 12) | (33.1, 2.9) |
| Red Brambleback | (.3, -26.2) | (-5.2, 41.1) |
| Gromp | (-52, 17.8) | (47.6, -2.9) |
| Raptors | (-6.6, -15.2) | (1.7, 27.7) |
| Krugs | (7, -41.5) | (-12.4, 54.9) |
| Murk Wolves | (-37.5, -1.7) | (33.1, 16.6) |

- Rift Scuttler: (-31.7, 28.2) and (27.3, -13.8).
- Dragon: (21.5, -22). Baron and Herald: (-25.9, 36.4).
- Models: `Match/Art/Models/sru_murkwolf`, `sru_murkwolfmini`, `sru_crab` (OBJ, globalScale .01), extracted from Map11.wad.client.

## Look (League VR > Apply Rift Look, `RiftLookBuilder`)
- **Map materials:** `LeagueVR/Rift Stylized` (`SummonersRIft/VRRebuild/RiftStylized.shader`), tuned in `RiftLookBuilder.Tune`:
  - _Light .5, _Bands 3, _Relief 1.2 (fades by distance only implicitly)
  - _Steep .4 (cliffs only), _Grain .25, _Rim .12
- **Unity fog is off.** The haze is per pixel, from `RiftAtmosphere` on "Rift horizon ground" (start 90, end 240, strength .4, colour (.7, .79, .86)). The Foundation atlas shader also reads `_RiftHaze`.
- **Repairs:** "Dragon pit floor patch" (radius 1.85 at (21.4, -21.1), atlas sampled shifted by (3.6, .6)); the Boundary wall uses "Sealed boundary rock".

## Map audit
- First audits and final reviews: `scratchpad/map_review/*.json` and `*_final.json`.
- **Final review ratings (overall):**

  | Region | Overall |
  |---|---|
  | Top lane | 6 |
  | Mid lane and bases | 5 |
  | Bot lane | 6 |
  | Top jungle | 6 |
  | Bot jungle | 6 |

- Collision rated 6–7 in every region.
- Reviewers' "black sky" is a capture artifact: the review camera clears to a solid colour.

## Gragas (2026-10-13; GragasKit, GragasEffects)
- **Assets:** League's model from `gragas.glb` (Art/Gragas, idle `Idle1`, one material `MAT_gragas` → URP Unlit `Gragas-MAT_gragas`), prefabs `Gragas` and headless `Gragas-FP` (head and its children removed: 3120 → 1905 triangles), icons, 13 of his particle textures in `Art/Gragas/Resources/GragasFx`, and 26 original sound events (15 effects + 11 English voice, 82 clips: effects ADPCM decompress-on-load, voice Vorbis in memory, all mono) from the user's install. No base-skin passive, ferment, recall or kill audio exists. Data: `Data/Gragas.asset` (Data Dragon 16.20.1, AD growth 3.5 from the wiki), skill order QEW, roster entry 9, shop page.
- **Rig:** his shoulders are collar bones (`L_Shoulder` → `L_Uparm`) and his hands are mittens (Thumb + Finger): `BodyRig` detects both (only Brand shares the collar layout).
- **Keg:** the model's `weapon` bone (child of `R_hand`), posed every frame from the controller grip in `Body.Posed`: 0.42 m wide, held the way League's Gragas holds it in every clip (measured on his model): like a big mug, palm flat on its side, its axis along the thumb (`HoldOffset` (-.213, .027, .017) in grip space; the palm sits at the keg's middle instead of League's bottom so it never rises past the face). From the eyes it sits in the lower middle of the view; with the arm hanging straight down it sinks into his belly. The fist closes on it (`ChampionBodyBase.KitCurl`). Swing sweep radius 0.24 m (`ChampionKit.WeaponRadius`). Check: `keg_views.py`.
- **Inputs:** RT/swing keg attacks, B hold Q (right palm aims, League ring), X W, A hold E (walk direction, else right palm; gold ring on the first enemy), LT hold R (cask in the left palm, ring where the left hand points). Previews only while held.
- **Kit:** Happy Hour 5.5% max HP after a cast (12/10/8/6 s at levels 1/6/11/16, assumed). Q ferments 2 s (+50% damage and slow), bursts 4 s after stopping or on B (queued while rolling), minions 70%. W drinks 0.75 s (no attacks or casts; held aims are cancelled), 10–26% (+4%/100 AP) less damage for 2.5 s from the cast (source-less damage excluded via `IDamageReduction`), next hit within 5 s splashes flat + 7% max HP (monsters capped at 300, structures only the target at 50%, wards do not use it). E (2026-10-15) is a real charge (`Charge`). A tap (released within `ETap` 0.25 s) charges where you walk or look; held longer, the preview shows and the right hand aims it (`PlanSlam(aimWithHand)`). It runs to the first enemy in its path (the "auto focus"), stopping 0.7 m short of its centre, but an enemy already at his belly still gets a 0.35 m lunge (`ELunge`; contact only counts after it). The floor is followed from the height he is at (`Ground` at that height, at most 0.3 m change), never stepping up onto something beside the path. The charge: the view rushes along the planned line at League's 900 units/s (0.67 s for 600 units) behind a comfort vignette (`PlayerChampion.RushVignette`, 15% tighter than the stick's at the saved strength, none when the vignette is off), locomotion frozen; the first enemy his belly meets (`Contact`: up to 0.85 m ahead, within the charge's width; it may step into the line) stops him and is slammed (`LandSlam`); a stun or root stops him with no slam. 0.6 m shove + 0.6 m hop + 1 s stun, 40% cooldown refund on hit. Dust behind the strides, pushed-air rings riding 1.1 m ahead of his belly, "Stride" haptics every 0.16 s. The comfort setting BLINK CHARGES (`ComfortSettings.RushCharges` off) restores the old fade-and-move. R 0.5 s throw, 8.7 m knockback away from the centre (`UnitDisplacement`, grid-bounded).
- **Drink:** the right arm lifts the keg by its rim until its top is at the mouth from the lower right (`ChampionBodyBase.GripOverride` → `GragasVisuals.DrinkGrip`); peak at 90% of the way, keg centre ≥ 0.43 m from the eyes, fist on the keg throughout (`palmInGrip`, gap 0.05 m).
- **Basic attacks = League's Attack1, Attack2 and Crit (2026-10-15):** the trigger's attack (`BasicAttack`) starts a swing of the right arm through `GripOverride` (`GragasVisuals.SwingGrip`) instead of an instant hit: the Attack1 hook from his right side and the Attack2 overhand bonk in turn, the Crit swing when the hit will crit (`RiftEconomy.RollNextCritical` rolls it at the start; the hit then uses that roll). About 60% of the attack time (0.35–0.62 s): wind-up to 0.35, strike to 0.5 (the hit lands then, on the target if it is within reach + 1 m; otherwise the attack timer resets), follow-through to 0.7, back to the controller. Key poses are eye-relative (right, forward, up for a 0.71 m arm) with the keg following the posed palm; the bonk's wind-up goes out to the right side first and every strike curves out in front, so the keg stays at least 0.34 m from the eyes. A swing makes him Busy (no next attack), abilities cut it short. Physical swings still hit at once. Sounds: League's `Crit` swing ("CritSwing") and `CritHit`; the smash plays League's `WEmpoweredCast` ("WSmashCast"). Tests: `swing_test.py` 9/9, `swing_probe.py` (distances along each swing), League clip strips `gragas_clips_zoom.py`.
- **Empowered hit = League's `Attack_Passive` (2026-10-15):** with Drunken Rage up, the trigger's attack or a keg swing into an enemy starts `BeginSmash` instead of an instant hit. Timeline (`GragasKit.Smash*`): lift 0.24 s, down 0.14 s (the hit and the W splash land at 0.38 s on the target if it is still within reach + 1.5 m; a miss keeps Drunken Rage), hold 0.15 s, return 0.22 s. Both arms are driven through `GripOverride` (`GragasVisuals.SmashGrip`): the right hand goes with the keg at once; the left joins once it is overhead, rising up its own side (reaching across swept the arm over the eyes). The keg path (`SmashKeg`) is laid out around the eyes and scaled by arm length (0.71 m measured): up past the right shoulder, across the top of the head, down the right of the view to stand upright in front; keg centre at least 0.33 m from the eyes (Gragas's shoulders are 0.3–0.4 m behind his eyes, so a swing down the middle passed through the face). Effects: a pink streak behind the keg on the way down (only beyond 0.6 m from the eyes), then a fountain of brew bursting up and out toward the target over his fists, a flare, ale squirts, bubbles, and a foam shockwave on the ground; "WSmash" thud and haptics in both hands. Attacks and casts wait for the whole 0.75 s. The arms crossing the view for an instant on the way down is expected. Tests: `gragas_ew_test.py` (charge 5/5, smash 6/6), geometry probe `smash_probe.py`, League clip strips `gragas_clips.py`.
- **Shared changes made for him:** `UnitDisplacement` (knock-ups/knockbacks, bots honour stuns), `IDamageReduction` in `Combatant`, `PlayerChampion.UpdateWeaponSweep` pauses 2 frames after a rig jump > 0.5 m (a dash used to count as a keg swing), `ResetPractice` cancels holds, Gragas haptics in `ChampionFeedback`.
- **Tests (scratchpad `gragas/`):** `gragas_kit_test.py` (every ability, signals and sounds, captures), `gragas_round2_test.py` 7/7, `keg_diag.py`, `champ_smoke9.py` (all 9 champions, 0 errors). Reviews: `gragas/review/*.md`.

## Hands and fingers (2026-10-14/15)
- **`FingerSense`** (Avatar): Meta Touch / Touch Plus / Touch Pro capacitive sensing, bound by usage (`{TriggerTouch}`, `{PrimaryTouch}`, `{SecondaryTouch}`, `{Primary2DAxisTouch}`, `{ThumbrestTouch}`, Touch Pro `triggerCurl`): the index off the trigger points (resting .32; Touch Pro curl in between), a thumb off every surface stands up (.5 resting, .7 pressing), middle/ring/pinky follow the grip (open .1/.14/.18), smoothed. `OverrideLeft/Right` for tests; `XRPoses.Scripted` makes the body follow scripted transforms with a headset connected.
- **`ChampionBodyBase.HandOverride`** (2026-10-15): a kit animation can set a hand's whole finger pose (Zeri's flick, Gwen's grip on the second handle); null keeps the sensed fingers.
- **`HandCurl`** has five fingers (the 3-argument constructor stays for weapon grips). `BodyRig` finds numbered fingers (`Index1..3`), older rigs' plain + `_b` joints (Brand) and mittens (Gragas: Thumb + Finger; the block closes as the four fingers' average).
- **Brand and Yunara** now have head-less first-person bodies like the others (`Meshes/<name> first person body.asset`, built like `build_fp_meshes`; Yunara keeps body and trousers without her bead weapon, her shadow keeps everything). Their old floating "tracked hand" meshes were removed.
- **Touch Pro** profile enabled on Standalone too.
- **Gotcha:** a mesh asset overwritten in place (`EditorUtility.CopySerialized`) keeps drawing its old GPU buffers for the rest of the editor session: set its triangles again (`SetTriangles` + `UploadMeshData`) after rebuilding.
- **Check:** `hands_test.py <label> [champs]` (five poses, eyes/right/left per champion, palm-framed): all 9 champions show distinct open, relaxed, point, thumbs-up and fist.

## Rig input (2026-10-15)
- The XRI starter rig's `JumpProvider` was bound to the right controller's A button (`XRI Right Locomotion/Jump`), the same button as every champion's E, so each E press also jumped (with a short Body Slam the jump was most of what showed). `PlayerChampion.Awake` turns every `JumpProvider` on the rig off; League has no jumping. Test: `jump_test.py` (A pressed through XRI's simulated controller: no rise).

## Menu input and pointer (2026-10-14)
- **Stick clicks** bind by usage: menu `<XRController>{LeftHand}/{Primary2DAxisClick}`, recall the right stick click. OpenXR controllers name the control `thumbstickClicked` (no `primary2DAxisClick` name or alias), so the old name bindings never fired in the headset. Checked with XRI's simulated controllers (`XRSimulatedControllerState.WithButton`): 5/5.
- **Menu laser** (`RiftUI`): 8 mm to 6 mm, own overlay material (queue 4000) sorted after the canvas (order 40 painted over it before); ends on the button or anywhere on the panel (`PanelPoint`); a round dot marks the spot (cyan, gold over a button; a line with round caps, since the overlay shader takes its colour from vertices).
- Unity may reopen on `SampleScene` with unsaved changes: don't discard them; tests load LeagueVR additively and close it afterwards (`scratchpad/with_rift.py`).

## Open items
- Headset checks from 2026-10-12: the surge preview's thumb-on-A trigger, the preview arc's readability, the wrist gate, Gwen's new effects and voice levels.
- Gwen: attack-damage growth (+3 in game, 0 in the project's Data Dragon copy) and the Q minion execute (League gives bonus damage) are unresolved.
- Walls on open floor (2026-10-12 probes): the jungle terrain piece `_chunk_jungle_north_island_h_alpha_3dcuv_atlas_color` is not a floor piece, so its triangles above .55 m become walls (e.g. the blocks around (9–13, -40)). Confirm each block against the map visually; if any is low ground, make it a floor piece or lower its threshold.
- A .44 m ledge at about (2.3, -42.2) stops the walk with nothing in front of it; the grid calls the step walkable (gate .55 m) and the CharacterController's step offset is .5 m. Check on a headset before changing either.
- `Walking into a wall stops at the wall` (vault_test check 1) failed on 2026-10-12 at (-60, -17): the walls there are where the sweep puts them, and the harness's CharacterController was offset about .8 m behind the feet, so the test is unreliable until the harness resets the controller.
- Headset check: brush colour and density, the stylized light level (reviewers say slightly dim), comfort of the vault and brush views, and the vault's follow camera.
- The pit patch disc is still faintly visible.
- Floating NW-corner rocks; a pond shore blend at (4, -38); the horizon ground's hard edge at the SE.
- The brush layout comes from the current League grid; a few brushes may differ from 2014–2016.
- Minions now collide with the new walls; check that waves still path (a full-match smoke test is still to run).
- The smoke test of all 9 champions passed with 0 errors on 2026-10-13 (R is false at level 1 by design).

## Test tools (scratchpad)
- Old scratchpad (`...scratch-2026-10-07-957e10\...\scratchpad`): u.py, compile.py, kitlib.py, povlib.py, champ_smoke.py, zeri_round2_test.py.
- New scratchpad: walklib.py (VR-style walking via CharacterController), walk_test.py, vault_test.py (5/5), brush_view_test.py (5/5), maplib.py, region_capture.py, look_shots.py.
- Session scratchpad (`F--Vr-...\b8fd66df-...\scratchpad`): with_rift.py (runs a test with LeagueVR loaded), hands_test.py, keg_views.py, gragas_clips.py, gragas_ew_test.py, smash_probe.py, swing_test.py, swing_probe.py, etap_test.py, jump_test.py, supers_test.py, fix_diag.py.
- **Smoke test (run after every change): `smoke3.py`** — Gwen, Zeri and Gragas each attack and press QWER at a minion for three rounds, in a rotating order (W's drink, E's charge and the smash block the casts after them), with the error count after every round. It stands the head at 1.62 m first and waits for the CharacterController to take that height: a headset lying on the desk puts the head at the floor, the controller's step offset becomes invalid ("Step Offset must be less or equal to..."), and the editor's Error Pause then freezes play mode (cooldowns stop, nothing casts). Last run: 0 errors.
