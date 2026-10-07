from pathlib import Path
import shutil,json
P=Path(r'F:\Vr\VR quirky testing gpt astra\League of legends test\league vr imoport testing')
T=Path(r'C:\Users\Kakad\AppData\Local\Temp\LeagueChampions')
L=P/'Logs/ChampionExpansion'
out=P/'PrototypeSources/ChampionExpansion';out.mkdir(parents=True,exist_ok=True)
for source in T.iterdir():
    if source.is_file() and source.suffix in ('.py','.json'):shutil.copy2(source,out/source.name)
report='''# League VR: seven playable champions

Saved in the existing LeagueVR scene, Unity 6.6. Added Zoe, Aatrox, Akshan, Brand, Pantheon and Yunara beside Gwen. This is a playable VR adaptation with prototype balance and several simplified mechanics; it is not a complete reproduction of League of Legends.

## Start playing

Open `Assets/Scenes/LeagueVR.unity` and press Unity Play. Point the right controller at a champion card and press the right trigger. Choose **Play [champion]** to start, or **New Match** to apply a different selection during a match. **Resume** preserves the current champion. Every new match starts with **10,000 testing gold**.

| Action | VR control |
|---|---|
| Basic attack | Right trigger; aim the right hand at the enemy |
| Q | Right secondary button (B on Quest controllers) |
| W | Left primary button (X on Quest controllers) |
| E | Right primary button (A on Quest controllers) |
| R | Left trigger |
| Menu | Click right stick |
| Recall | Click left stick |
| Shop | Left secondary button (Y), at the fountain |
| Physical item | Grip near its body slot; trigger to use; release grip to return |

Right grip plus a deliberate swing can also attack. Holding a physical item suppresses ability input so drinking or activating it does not also cast a spell. The selected champion's wrist display and ability guide explain their state and abilities. Desktop fallback retains left mouse attack, Q / F / E / R abilities and WASD movement.

## Menu and models

The opening menu now has seven portrait cards, a selected champion panel, passive and ability icons, an ability guide, controls, comfort/audio settings and match controls. The current champion and the selection for the next match are distinct.

All six attached GLBs were copied into `Assets/_Game/LeagueVR/Champions/Art`. Their imported base textures are assigned to editable URP materials. Baked color textures use an unlit shader to preserve their painted appearance. The original attached GLBs remain byte-identical; no original geometry was overwritten.

| Model | Skeleton joints | Imported animation clips |
|---|---:|---:|
| Zoe | 172 | 91 |
| Aatrox | 127 | 103 |
| Akshan | 151 | 112 |
| Brand | 72 | 18 |
| Pantheon | 115 | 88 |
| Yunara | 173 | 66 |

Separate full model and first-person prefabs retain the original skeleton and animation library. Derived first-person meshes display the arms and relevant weapons while removing the head/body from the headset view. The player's arms follow tracked controllers through one pose solver, including before-render updates. Animation clips do not fight controller tracking or rotate the headset camera. Aatrox's sword, Akshan's weapon and Pantheon's spear/shield are calibrated to the hands. Held physical items temporarily hide the weapon in that hand. Brand's flame hands and Yunara's gloves use floating-hand views after their imported arm skinning produced visible wrist gaps. The other champions keep tracked arms and weapons. This avoids showing broken forearm seams in the two affected first-person views.

## Champion gameplay

Shared systems provide health, armor, magic resistance, mana, levels, items, cooldowns, damage, targeting, crowd control, hit feedback and casting events. Each new champion has a passive and four working abilities routed through the existing VR input.

| Champion | Implemented kit and VR adaptation |
|---|---|
| Zoe | Empowered attack after spells; redirectable Paddle Star; collectible Spell Thief shards with Heal/Flash/Ignite variants and missiles; bubble slow followed by sleep and amplified waking damage; one-second Portal Jump with automatic return. |
| Aatrox | Max-health passive attack with healing; three Darkin Blade recasts and sweet spots; chains that slow and pull enemies staying in the area; collision-checked Umbral Dash; World Ender empowerment with takedown extension. |
| Akshan | Double attack and three-hit Dirty Fighting proc; returning Avengerang; camouflage with nearby enemy/tower reveal; terrain grappling implemented as a short controlled reposition and shot; charged, interceptable Comeuppance shots. Scoundrel tracking includes an ally revival hook. |
| Brand | Four-second Blaze and three-stack detonation; Sear stun against burning enemies; delayed Pillar of Flame; Conflagration spread; bouncing Pyroclasm, including harmless player bounces. |
| Pantheon | Five-stack Mortal Will empowerment; aimed thrown/execute spear; targeted Shield Vault with stun; left-hand directional Aegis Assault, vulnerable from behind and to turret attacks; ground-aimed Grand Starfall with warning and landing damage. |
| Yunara | Critical-hit magic damage; eight attacks build Spirit; Q attack speed and splash empowerment; slowing bead or transformed piercing beam; movement boost or transformed dash; timed Transcend One's Self with W/E resets. |

Ranged basic attacks acquire a target from the hand's aiming cone and follow that target. Skill shots continue to collide with real terrain. The owner's physical equipment and hurtbox are excluded from their targeting and projectile collision queries. Movement abilities check terrain and do not rotate the head camera.

## Two completed full test runs

These were controlled Unity Play Mode runs with scripted headset/controller poses and rendered camera captures. They were not a human wearing the headset or a complete match against enemy champion AI. Each run covered all seven selections, menus, materials, hands, basic attacks, Q/W/E/R, passive checks, cooldown/mana gating, shop stats, stasis, recall and preservation checks for existing lanes/structures.

| Stage | Passed | Failed | Assessment |
|---|---:|---:|---|
| Full run 1 | 235 | 21 | The new roster, textures, menu and controller poses initialized. Failures exposed targeting issues and inaccurate test setup. Seven menu failures were a test selector counting the ability-guide button as a champion card. |
| Full run 2 | 252 | 16 | Menu, XR, pose, texture, cooldown, resource, shop and recall checks passed. Basic attack acquisition and related targeted/passive checks remained broken; Zoe's delayed sleep was checked too early. |
| Focused post-run repair verification | 59 | 0 | Rechecked the failing gameplay paths, Gwen weapon restoration, potion handling and Akshan's terrain selection after correcting them. |
| Final hand view check | 6 | 0 | 120 controlled poses for each new champion; six final first-person captures reviewed. |

One interrupted attempt at run 2 was stopped during compilation and retained separately; the table refers to the two completed runs. Compilation errors encountered during implementation were corrected before continuing.

### Corrections driven by the tests

- Corrected the test arena and headset height so test targets were not inside an inhibitor and the camera was at an actual standing height.
- Fixed menu and physical-item return code that restored Gwen's scissors on another champion.
- Found the physical back ward on the world collision layer blocking outgoing attacks. Added consistent ownership filtering for attack acquisition, projectiles, ground aiming, grappling and dash clearance.
- Made ranged basic attacks follow the target already acquired by the aim query.
- Corrected the rear-shield test source position and Akshan's camouflage test enemy distance.
- Replaced a brittle fixed-time sleep assertion with a bounded observation of bubble damage, slow, sleep and wake. Sleep was observed after the slow; it was not removed or bypassed.
- Adjusted first-person sword/spear orientation and Pantheon's shield size after reviewing captures.
- Reviewed wrist gaps in source and first-person captures. Selected floating-hand views for Brand and Yunara, preserving their original hand geometry and textures. A separate focused check verifies all six final hand views across 120 poses each, with less than 0.001 m palm error and no camera rotation.

The passing checks establish functionality under the tested conditions. They do not establish flawless comfort, frame rate or visual quality in every headset pose.

## Preservation and architecture

All **647 pre-existing scene transforms** retain their original position, rotation, scale and parent. The map, towers, lanes and XR hierarchy were not relocated. The scene gained three champion components on the existing player: roster, shared abilities and tracked avatar. Gwen continues through her existing implementation.

New definitions, models, derived meshes, materials, art and code are under `Assets/_Game/LeagueVR/Champions`. Existing adapters changed in `GwenAbilities`, `GwenVRInput`, `GwenFeedback`, `GwenAudio`, `Combatant`, `RiftEconomy`, `RiftMatch`, `RiftMinion`, `RiftUI`, `RiftVRHUD` and `RiftItemRack`.

Pre-expansion backups are in `PrototypeBackups/ChampionExpansion-20261004`. Reports, captures, import counts and preservation hashes are in `Logs/ChampionExpansion`. Preparation scripts are saved in `PrototypeSources/ChampionExpansion`; the live C# files in Assets are authoritative.

## Remaining improvements

1. **Headset comfort and performance:** verify the menu's legibility, arm proportions, shield visibility and dash/blink comfort with the actual headset wearer; capture headset frame timing during minion waves.
2. **League fidelity:** damage numbers, VR ranges, ability ranks, progression and timing need a dedicated balance pass. Skills currently become available automatically; there is no manual League skill-point allocation. Zoe's shard pool is limited. Akshan's grapple uses a short reposition rather than an orbit. Pantheon's ultimate uses a prototype range. Revival hooks need a real allied champion mode to exercise their match impact.
3. **Presentation:** controller-driven hands work, but bespoke first-person casting gestures, richer original effects and champion-specific audio remain to be added. The supplied models contain no audio. Gwen's sounds are suppressed for other champions rather than playing the wrong champion's sounds; existing environmental/minion/tower audio remains.
4. **Complete game systems:** this remains the existing single-player prototype; it does not add multiplayer, enemy champion AI, runes or a complete summoner-spell system.

## Research and asset sources

Champion names, descriptions, base statistics, portraits, spell icons, cooldowns and resource costs were researched from Riot's champion pages and Data Dragon **16.19.1**. VR control decisions and prototype damage/range tuning are custom adaptations.

- [Riot Data Dragon documentation](https://developer.riotgames.com/docs/lol#data-dragon_champions)
- [Zoe](https://www.leagueoflegends.com/en-us/champions/zoe/), [Aatrox](https://www.leagueoflegends.com/en-us/champions/aatrox/), [Akshan](https://www.leagueoflegends.com/en-us/champions/akshan/)
- [Brand](https://www.leagueoflegends.com/en-us/champions/brand/), [Pantheon](https://www.leagueoflegends.com/en-us/champions/pantheon/), [Yunara](https://www.leagueoflegends.com/en-us/champions/yunara/)
- [Yunara ability rundown](https://www.leagueoflegends.com/en-us/news/game-updates/yunara-abilities-rundown/)

The six character models are the files supplied by the user. Exact source paths and SHA-256 hashes are recorded in `Sources.json` and `Preservation.json`.
'''
(L/'Report.md').write_text(report,encoding='utf-8')
print('Saved report and preparation sources.')
