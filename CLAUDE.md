# The Whitmore Library (BracU Game Jam)
A cozy 3D library-management game with a top-down camera. Built in Unity 6 URP and targeting **WebGL** (low tris, few materials, baked light).
The main scene is `Assets/Scenes/MainGame.unity`. Read `DEV_NOTES.md` (systems and how to test them) and `TUNING_GUIDE.md` (balance numbers) before changing gameplay.
Read `HANDOFF.md` at the start of a session: recent uncommitted work, the user's working rules and the secret story quest (keep its details out of public text).

## Working rules
- Keep changes small and match the surrounding code style: short `///` summaries, plain-English comments, no extra abstractions.
- Don't rewire the toon shader or retune lights, ambient light or the colour grade unless asked.
- Don't commit, push or build WebGL unless asked. Two people work on this repo, so avoid editing scenes and prefabs you don't need to touch.
- After a code change, check that the Unity console has 0 errors. Never test by calling `GameManager.StartNextDay` on a real save, because it advances the player's day.
- UI uses the TravelBook UI kit, the Jersey fonts (`Assets/UI Toolkit/Fonts`) and the audio in `Assets/Audio`. Don't add new UI or sound assets.
- Update `DEV_NOTES.md` when you add or change a system.

## Game loop
- Each day is 9:00 to 17:30 in game time, about 4 real minutes (`GameManager.realMinutesPerDay`).
- Visitors queue at the counter and ask for a book: by author, subject, tag or an abstract hint.
- The player looks the book up on the PC, takes it from a shelf and hands it over.
- The visitor then buys it, or reads it at a seat and returns it.
- Other jobs:
  - Returns that need repairs go to the Repair desk.
  - Sold-out books get reserved and ordered in the PC Shop. Normal delivery arrives tomorrow, express in 2 hours.
  - Some visitors need the book today.
  - New members may ask for a book; it shows a gold "Requested" badge in the Shop.
- At closing, tidy the mess spots, leave by the door, and a Day Report appears. The only way on from the report is the next day.
- Rooms open on a schedule: the Archive on Day 2, Rare Books on Day 3 (`RoomGate`, `RoomReveal`). There are 7 days. Difficulty modes are set in `GameManager.Mode`; Standard is the intended mode.
- Saves use PlayerPrefs (`GameManager` SaveData), which works on WebGL.

## Code map (`Assets/Scripts`)
- `Core/`: `GameManager` (clock, coins, reputation, orders, reservations, staff wages, save), `GameAudio`, `DayNightCycle`.
  - Use `GameManager.Dt` for gameplay timers; it pauses while `PlayerLock` is locked.
- `Customers/`: `Customer` (state machine, dialogue, patience, pay), `CustomerSpawner` (visitor mix, pickups), `BookRequest` (request rules), `Members`, `CounterQueue`, `SeatPoint`.
- `Books/`: `BookDatabase` (`Assets/Resources/BookDatabase.asset`), shelves, `PlayerCarry`, `ReturnBin`, `DeliveryCart`.
- `Day/`: `DayReportUI`, `EndDayDoor`, `MessSpot` and `Housekeeping`, `RoomGate`, `RoomReveal`, `RoomDarkness`, `StaffManager`, `StaffWorker`, `StaffTalk`.
- `Dialogue/`: `DialogueAsset` (branching lines), `DialogueUI` (typewriter that supports rich text).
- `Interaction/`: `InteractionController` (all click and hover), `ClickInteractable`, `HoverOutline`, `PlayerLock`.
- `UI/`: `HUD`, `MainMenuUI` (play modes, How to Play), PC views (`PCOverlayTrigger` catalogue, `PCShopView`, `PCMembersView`, `PCSettingsView`), `BookFilterBar`.
- `Repair/`: `RepairDesk`, `RepairUI`.

## UI
- UI Toolkit everywhere. Layouts (`.uxml`) are in `Assets/Resources`; styles are in `Assets/UI Toolkit` (`PC.uss` and others).
- Panel sort order: PS_Game 10, PC 20, PS_Menu 30.
- Labels use rich text. Highlight colour: `#D67840`; gold boxes: `rgb(235,200,120)`.

## Art
- Source art is in `E:\Work\Game Projects\BracU Gamejam 2026\`: Models.blend, Props.blend, Characters2.blend, Textures and FBX.
- Name prefixes: `SM_` mesh, `T_` texture, `M_` material, `PF_` prefab, `CH_`/`NPC_` characters.
- `T_Palette.png` (256², Point filter, no mips, no compression) has 16 gradient bands, top to bottom:
  - walnut, oak, oxblood, green
  - navy, parchment, brass, stone
  - marble, iron, candle-glow (emissive), moon-glass
  - skin-light, skin-deep, tweed, plum
- On the palette, UV v is the band centre and u is the shade.
- Props and characters use the palette material; architecture uses the 1024 atlas material.
- Rooms are on a 2 m grid: Main Hall 16×12, Archive 8×12, Rare Books 8×12.

## Characters
- All 15 characters share one Auto-Rig Pro rig. Meshes stay at the origin on the rig; never offset them.
- Rig type is Humanoid. NPCs and animations use "Copy From Other Avatar" (CH_Main_Librarian).
- Animations are in place at 30 fps. Turn on Loop Time for loops, and set Root Transform to Bake Into Pose.
- Characters move on a NavMesh (agent radius 0.3, step height 0.3). `CharacterAnimator` drives Talk, SitRead, Carry, Stamp, Impatient and the other states.

## Unity import
- Models: Scale 1, Mesh Compression Off, Read/Write off, Materials None.
- Lightmap UVs only on architecture and big static props. Characters and small props use Light Probes.

## Rendering
- The toon shader is `SG_ToonBaked`. Its properties: `_BaseMap`, `_EmissionMap`, `_EmissionColor`, `_ShadowTint`, `_LightTint`, `_Steps`, `_LightBoost`.
- Lighting is baked only, with non-directional lightmaps. `DayNightCycle.cs` fakes day and night over a single bake.
