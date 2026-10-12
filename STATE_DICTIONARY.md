# LeagueVR state dictionary (2026-10-12, after commit 4182497)

## Project
- Unity 6000.6.0f1, URP (MSAA 4x, active quality "Low" = Performance URP Config), OpenXR, XRI 3.6.
- Scene: `Assets/Scenes/LeagueVR.unity`. Repo: trecca30/LeagueVR (public). Git LFS is used (`Generated/**/*.asset`, png, wav, obj, glb).
- User rules:
  - No Claude co-author trailer in commits.
  - Never write to `F:\Riot Games` (reading is OK).
  - Ask before downloads.
  - Subagents run on Haiku and get countable budgets (25 tool calls, 10 image reads), then write a State Dictionary file. They cannot measure tokens.
- Not committed: `Assets/TextMesh Pro/.../LiberationSans SDF - Fallback.asset` (font cache) and `Assets/Resources.meta`.

## Coordinates
- 1 m ≈ 103 League units.
- `unity_x = .00973*Lx - 74.5`, `unity_z = .0097*Lz - 64.9`.
- The map is point-symmetric about (-2.2, 7.22).
- Floor height: lanes y≈0, river -1.2, bases +0.45, fountain pads 1.2.

## Champions
- 8 champions:
  - Gwen, Zoe, Pantheon and Zeri: VR kits with full bodies.
  - Aatrox, Akshan, Brand and Yunara: old kits.
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
- **Round-2 review ratings:** code 7, fidelity 6.5, body 4 (the coil glow has since been fixed), effects 6.5, UX 7, audio 6.
- **Tests (old scratchpad):** `zeri_round2_test.py` 13/13 (at level 18).

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

## Open items
- Walls on open floor (2026-10-12 probes): the jungle terrain piece `_chunk_jungle_north_island_h_alpha_3dcuv_atlas_color` is not a floor piece, so its triangles above .55 m become walls (e.g. the blocks around (9–13, -40)). Confirm each block against the map visually; if any is low ground, make it a floor piece or lower its threshold.
- A .44 m ledge at about (2.3, -42.2) stops the walk with nothing in front of it; the grid calls the step walkable (gate .55 m) and the CharacterController's step offset is .5 m. Check on a headset before changing either.
- `Walking into a wall stops at the wall` (vault_test check 1) failed on 2026-10-12 at (-60, -17): the walls there are where the sweep puts them, and the harness's CharacterController was offset about .8 m behind the feet, so the test is unreliable until the harness resets the controller.
- Headset check: brush colour and density, the stylized light level (reviewers say slightly dim), comfort of the vault and brush views, and the vault's follow camera.
- The pit patch disc is still faintly visible.
- Floating NW-corner rocks; a pond shore blend at (4, -38); the horizon ground's hard edge at the SE.
- The brush layout comes from the current League grid; a few brushes may differ from 2014–2016.
- Minions now collide with the new walls; check that waves still path (a full-match smoke test is still to run).
- The smoke test of all 8 champions passed with 0 errors (R is false at level 1 by design).

## Test tools (scratchpad)
- Old scratchpad (`...scratch-2026-10-07-957e10\...\scratchpad`): u.py, compile.py, kitlib.py, povlib.py, champ_smoke.py, zeri_round2_test.py.
- New scratchpad: walklib.py (VR-style walking via CharacterController), walk_test.py, vault_test.py (5/5), brush_view_test.py (5/5), maplib.py, region_capture.py, look_shots.py.
