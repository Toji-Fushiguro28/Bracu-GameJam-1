# Handoff: work on branch `EMon_New` (uncommitted)

These notes are for the next Claude session. Read `CLAUDE.md`, then the matching sections of `DEV_NOTES.md` ("Player name", "The night letter"). All of the work below is **uncommitted** in the working tree.

## How the user likes to work
- Plan first and talk before implementing when they say "plan" or "talk to me". Keep replies short.
- Think about edge cases. Dialogue should sound natural, not like AI.
- Don't commit, push or build unless asked. Avoid editing scenes and prefabs (two people use the repo).
- Never call `GameManager.StartNextDay` on a real save. Before any Play test, back up these PlayerPrefs and restore them afterwards: `LibrarySave`, `TutorialDone`, `TutorialGift`, `PlayTime`, `PlayerName`, `PlayerLook`, `GhostLetterLive`, `GhostLetterGiven`.
- No long bot play-tests. Compile and do quick checks only. Stop Play mode before editing code (recompiling during Play breaks the session).
- No unasked changes to the shader or lighting. No new UI or sound assets.
- Update `DEV_NOTES.md` when a system changes, and check the console has 0 errors.

## 1. Choosing your librarian (done)
- `Core/PlayerLooks.cs` and `Resources/PlayerLooks.asset` hold two bodies: the Librarian and `PF_CH_Main_Librarian_Female`.
  - They share a 36-bone rig, and `Apply` swaps only the skinned mesh.
  - The choice is saved in PlayerPrefs `PlayerLook` (default 0).
- The picker is on the left page of the name spread in `InterviewUI.uxml`: ◀ ▶, your name under the preview, "1 of 2", and "That's me".
  - `UI/LookPreview.cs` films a copy at y −200 on layer 31 into a RenderTexture.
  - The preview has light probes off (they caused a dark "beard").
  - The name box must still be filled in.
- To add a body: put it on the shared rig, make a prefab, and add it to `PlayerLooks.asset`.

## 2. The night letter: a secret story quest (done, keep it secret in public text)
See `DEV_NOTES.md` › "The night letter" for every detail. Code:
- `Day/GhostLetter.cs` holds the state, trail, delivery, Elias's visit and outcomes.
- `Day/GhostLetterData.cs` and `Resources/GhostLetter.asset` hold the prefabs and styles.
- `UI/GhostLetterUI.cs` and `UI Toolkit/GhostLetter.uss` are the paper, the 3-wheel lock and the typewriter.

**The trail:**
- A letter waits at the return bin, and a scroll icon sits by the desk plate.
- The clock plate reads "E. Harrow, 1891". The book "Notes on Clockwork" holds a pressed note.
- The bust, lectern and globe give the code **4 9 2**. Entering it on the display-case lock reveals a card; the case never opens.
- At night, after you click the fireplace, glowing words appear above it.
- Typing **"Ada Whitmore"** on the typewriter orders the folio, which arrives on the delivery cart.

**Elias's visit:**
- From the night the book arrives, after closing and once every visitor and staff member has gone (2.5 s of quiet), Elias (`PF_NPC_Mysterious_Visitor`) walks in alone to the desk.
- He stays until you talk to him (`DayStory.Visit.staysAfterClosing`).

**The outcomes:**
- **Give:** +10 reputation, the shelf sign reads "Whitmore", he fades out near the exit, and the ghosts plus Ada (shelving and reading) come out the same night.
- **Keep:** no ghosts.
- **Sell:** 1000 coins, from the PC Home page.

**The front ropes** (`EndDayDoor`) refuse while `GhostLetter.StrangerDue` is true and say "The ropes won't latch. Someone is still coming tonight." Once he has left, they lock as usual. `StrangerDue` is false if he can't be spawned or left without an answer, so the night can't softlock.

**Hooks in other files:**
- `InspectInteractable.Intercept`, `ReturnBin`, `Customer.TryGive` and `HandleClosed`.
- `CustomerSpawner.SpawnStory(visit, prefab)` and `GhostNight` (waits for Elias).
- The `BookData.QuestOnly` flag keeps quest books out of every pool (BookRequest, DeliveryCart, SpecialJobs).
- `GameManager.quest` (save), `PCHomeView` (sell row), `MainMenuUI` (tip), `HUD.BarRect`, `ShelfUI` (cart subtitle).

**Assets:**
- `NPC_Mysterious_Visitor` (the hat fix mesh is `_HatFixed.asset`).
- `SM_DisplayCase_2part.fbx` was imported but is **unused**, and is safe to remove.

## 3. Earlier in this branch (done)
- PC Home page decluttered: "Needs you" sits at the top, then 4 stats, the timeline and reputation.
- The tutorial points to the Shop's Upgrades and Staff pages.
- Notes and Reserved tabs are vertical (the kit button, rotated).
- Upgrades are in one grid: your own first, locked ones last. Prices are 60 to 90.
- Perfect-repair +1 reputation only comes from repairs done by hand.
- Fixed three player bugs: the reservation hand-over, wheel scrolling on WebGL, and reservations counted per person.
- Mac ghost artifact fix.

## Devlogs
`Devlog/devlog-bugfixes.html` and `Devlog/devlog-characters.html` (plus `-itch.html`). The published itch post only says "a story quest" and gives no details.

## Open items
- Nothing is committed. Ask the user before committing.
- Unused: `SM_DisplayCase_2part.fbx`.
