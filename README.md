# League VR

A fan-made VR take on League of Legends: play a champion in first person on Summoner's Rift, with a full body, physical attacks and abilities built for motion controllers.

> Fan project, non-commercial, not endorsed by Riot Games. League of Legends and all of its models, sounds, icons and names are © Riot Games. Do not sell builds of it.

## What's in it

- **Champions:** Gwen, Zoe, Pantheon and Zeri have full first-person bodies and VR-native kits (swing Gwen's scissors, throw and paddle Zoe's star, thrust Pantheon's spear and aim his shield, fire Zeri's gun and vault walls with her original sounds and effects). Aatrox, Akshan, Brand and Yunara are playable with their earlier kits.
- **Summoner's Rift match:** minion waves, turrets, inhibitors and Nexus with League's numbers, levels, gold and respawns. Ability ranks follow League's skill path and ultimates unlock at level 6.
- **The Rift itself:** walls built from the map's own geometry (they match League's navigation grid closely) and solid towers; League's brush, which hides you from enemies outside it; every jungle camp at League's position, including Murk Wolves and Rift Scuttlers; and a lit, stylized look for VR with a distance haze.
- **Item shop** modelled on League's client: recommended builds, all items by tier and role, search, recipe trees, undo and sell. Bought items are worn on the body and grabbed with grip.
- **Comfort:** an adjustable vignette while moving, and fades or a sky view for big leaps. Long vaults over thick walls are watched from a still spot outside your body, and in brush you can watch yourself from outside (Y).
- **Stream view** for sharing the game window: a smoothed first-person or over-the-shoulder camera (F8).

Progress, decisions and next steps are in `OVERHAUL_STATUS.md`.

## Playing a build

1. Use Windows 10/11 with a PC VR headset and an OpenXR runtime (Quest with Link or Air Link, SteamVR, or Windows Mixed Reality).
2. Make the headset's software the active OpenXR runtime.
3. Run `LeagueVR.exe`. Without a headset the game starts in desktop mode.

The build folder includes `HOW TO PLAY.txt`. In game, open the menu with the right stick press and choose CONTROLS.

## Working on the project

- **Unity 6000.6.0f1** (URP, OpenXR, XR Interaction Toolkit 3.6) with the Windows build support module.
- **Git LFS** is required: models, textures and audio are stored in LFS (about 0.9 GB for the current files). Run `git lfs install` before cloning.
- Open the project in Unity Hub and open `Assets/Scenes/LeagueVR.unity`. Press Play with a headset connected, or use the desktop controls without one: WASD, right mouse to look, left mouse to attack, Q/F/E/R for abilities, P for the shop, B to recall.

### Building

- In the editor: **League VR > Build Windows**. This writes `Builds/LeagueVR-Windows/` and a zip next to it.
- With the editor closed:

```bash
"C:/Program Files/Unity/Hub/Editor/6000.6.0f1/Editor/Unity.exe" -batchmode -quit -buildTarget Win64 -projectPath . -executeMethod LeagueVR.EditorTools.LeagueVRBuild.BuildWindowsBatch -logFile Logs/build.log
```

The version number is in `Assets/_Game/LeagueVR/Editor/LeagueVRBuild.cs`.

## Project layout

| Folder | Contents |
|---|---|
| `Assets/_Game/LeagueVR/Champions` | Champion data, first-person bodies (`Avatar/`), kits (`Kits/`), input, the stream camera |
| `Assets/_Game/LeagueVR/Match` | The match: minions, structures, lanes, economy, item shop and menus (`RiftUI*.cs`), item catalog |
| `Assets/_Game/LeagueVR/Runtime` | Shared pieces: combat, sounds, comfort settings |
| `Assets/_Game/SummonersRIft` | The map and its shaders (`VRRebuild/`) |
| `Assets/_Game/LeagueVR/Generated/RiftNav` | The Rift's collision, navigation grid and brush (rebuilt with League VR > Build Rift Collision) |
| `Assets/Scenes/LeagueVR.unity` | The game scene |
