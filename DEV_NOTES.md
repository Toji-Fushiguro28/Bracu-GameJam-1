# Dev Notes: Gameplay Update (Days 4–7 + polish)

What changed in `MainGame.unity` and `Assets/Scripts`, and where. Nothing here changes the toon shader, the baked lighting or the `VP_Cozy` grade.

---

## 0. Main menu (title screen)

- Lives **inside `MainGame.unity`**, so there is only one scene to load (good for WebGL).
- `MainMenu` object: `UIDocument` (`MainMenu.uxml` / `MainMenu.uss`, panel `PS_Menu` sort order 30) plus `MainMenuUI.cs`.
- It's an open TravelBook book over the live library, while the `CM_Menu` Cinemachine camera drifts slowly over the hall.
- The game is paused behind it (PlayerLock), and the HUD is hidden.
- **Buttons:**
  - **Continue** (only when a save exists; shows Day, coins and members).
  - **New Game** (asks for confirmation if a save exists; wipes it and reloads straight into Day 1).
  - **Start** (when there's no save).
  - **How to Play** (9 short pages: controls and stamina, counter, finding books, your notebook, sold out and reservations, cards and readers, repairs and rooms, staff, end of day and the audit log).
  - **Settings** (volume, music, fullscreen; same values as the PC Settings tab).
  - **Credits**.
  - **Quit** (hidden on WebGL).
- Pressing play fades the book out and Cinemachine blends down to the follow camera. A glockenspiel sweep plays, and buttons pop softly on hover.
- **Esc during play** opens it again as a pause screen (Continue becomes **Resume**; Esc again resumes). Esc that closes the PC, a shelf or a dialogue doesn't also open it.
- **PC → Settings tab → Main menu** returns to the title screen (asks first; progress since the morning save is lost).
- The title and subtitle are fields on `MainMenuUI` (`gameTitle`, `subtitle`). Team credits are in `MainMenu.uxml` (Credits page).
- **Build Settings:** `MainGame.unity` is now first; `SampleScene` is unchecked.

## 0b. Play modes

- Picked when starting a new game. **Standard is the intended way to play**: it's marked *★ Recommended*, it's selected by default, and How to Play says so.

| | Cozy | **Standard** | Busy |
|---|---|---|---|
| Patience | x1.5, visitors never storm out | x1 | x0.7 |
| Visitors | x0.75 | x1 | x1.3, +1 at once |
| Sold-out requests (Day 2+) | 10% | 20-25% | 35% |
| Hints | first hint is free | ask once | none |
| Starting coins | 100 | 50 | 30 |
| Order prices | -20% | x1 | +20% |
| Wrong-book penalty | x0.5 | x1 | x1.5 |

- Code: `GameManager.Difficulty`; all the numbers come from `Mode(...)` in `GameManager`.
- The mode is saved and shown in the menu and the Day Report title. It is only picked when a new game starts: neither the PC nor the main menu Settings can change it.
- **Return bin glow:** `ReturnBinGlow` on `SM_ReturnBin` (material `M_BinGlow`) gives the bin a pulsing gold outline whenever it has books in it.

## 1. How a day plays now

1. **Morning.** A banner shows the day, room openings and any reservation pickups. Last night's book orders are waiting in the **Delivery cart**..
2. **Visitors** come in through the front doors, with a busy morning, a quiet lunch, a busy afternoon and a slow evening:

   | Visitor | Share | What the player does |
   |---|---|---|
   | Browser | 40% | Picks a book off a shelf themselves. 75% sit and read it (then **Check in**, reading fee; you can **Chat**); 25% take it home (**Stamp out** at the counter). Tune with `Customer.readHereChance` |
   | Reader | 30% | Find the book, they read on a seat, you can **Chat**, then **Check in** for a reading fee |
   | Buyer | rest | Find the book from the hint; if it's sold out, **reserve** it and order it |
   | Returner | 8% | **Check in** (sometimes a late fee) |
   | Sign-up | 15% of non-members | Talk to them, then fill the card in on **PC → Members** |
   | VIP scholar | 8%, Day 3+ | Professors with a gold name plate; need a **Rare** card, then read in Rare Books |
   | Pickup | at their own random time of day (9:30 to 4:15, one slot each) | Comes for yesterday's reservation |

3. **Closing.** The bell rings at 17:30 and the library closes at 18:00. The last visitors leave, then the **front ropes appear** across the doorway.
4. **Tidy up.** Teacups and open books appear on tables, and the return bin may have copies in it.
5. **Lock up.** Click the front ropes or doors to open the **Day Report**, which has the Shop, Staff and Upgrades tabs. Then start the next day; the game saves at that point.
6. **Next morning.** The librarian is put back at their starting spot behind the counter (wherever the player is placed in the scene). See `GameManager.ReturnPlayerToStart`.

### Rooms unlock by day
- **Archive:** Day 2. **Rare Books:** Day 3. Constants are in `GameManager.ArchiveDay` / `RareDay`.
- Until then a rope gate (`SM_PassGate_Rope`) blocks the doorway, a sign says *"opens Day N"*, and the room is darkened.
- Clicking the gate says *"Not open yet…"*.
- NPCs never sit, browse or leave mess in a locked room.

### Cozy pacing rules
- The **clock pauses** whenever a menu, the PC or a dialogue is open (`PlayerLock.IsLocked`). Patience, reading, repairs, staff and spawning all pause with it.
  - Use `GameManager.Dt` instead of `Time.deltaTime` for any game timer.
- Patience is shown as a **mood face** (happy → okay → sad), not a bar. Everyone waits about 1.6× longer than before.

---

## 2. Systems added or changed

### Customers (`Assets/Scripts/Customers/`)
- **`Customer.cs`**: rewritten.
  - New kinds: `Browser` and `Pickup`.
  - Clicking a waiting customer opens a **conversation** (`DialogueUI`). Options:
    - Offer a held book.
    - Ask for a hint (once; it never gives the answer).
    - "It's sold out, I'll order it" (reserves it).
    - "We don't have that".
    - "Let me find it".
  - If you're holding a matching book, it's handed over straight away with no dialogue.
  - Lying about stock costs reputation.
  - Overhead UI: name plate (members), gold **VIP** plate, mood face.
- **`BookRequest.cs`**:
  - New `Title` hint.
  - Sold-out requests: `OutOfStockChance` = 20% on Day 2, 25% from Day 3.
  - Day 1 uses only simple hints (author, subject).
  - `InStock()` and `OutOfStock()` helpers.
- **`CustomerSpawner.cs`**: rewritten.
  - The visitor mix above.
  - A `busyness` curve by hour (the day rhythm).
  - Professors only come as VIPs (Day 3+).
  - Reservation pickups are queued first each morning.
- **`Members.cs`**: profiles (name, date of birth, access level) for the 12 visitors, and membership satisfaction, which scales tips and patience.
- **`SeatPoint.cs`**: skips seats inside locked rooms.

- **Counter requests:** a visitor waiting at the counter only shows a small orange **?** bubble until you talk to them. After the conversation, their request (plus any hint) stays above their head as a reminder. This is `Customer.heard`.
- **Hints cost a third of the tip:** the choice reads *"Could you give me a hint? (smaller tip)"* (`Customer.usedHint`). Cozy's free hint has no cost.
- **Repaired books wait on the repair table** (`RepairDesk.Done`) and the desk glows green.
  - Click the desk (or **Take** in the repair screen) to pick them up, then shelve them yourself.
  - The archivist also collects them and walks each one back to its shelf.
  - Repairs keep progressing while the repair screen is open.
- **Staff chat:** click Sam, Omar or Arthur (`StaffTalk`, 8 lines each, two replies each). They stop and face you while you talk.
- **Name tags** only show for VIPs. Members' names appear in the conversation.
- **Staff shifts:** staff walk in through the front door at 9:00 and visitors start at 9:10. At the closing bell they say goodbye and leave (`StaffManager.SendHome` / `BringIn`).
- **Staff jobs:** the archivist also shelves good copies from the returns. Staff tidying earns the tidy reward.
- **Hiring:** pays the first work day up front (today while open, tomorrow if hired at night), so you can't hire with an empty purse.
  - Letting someone go before they've worked a day refunds it.
  - Morning wages skip anyone already paid for that day (`GameManager.wagePaidFor`).
- **Day Report:** has no close button. After locking up, the only way on is the next day.
- **Repairs:** always move a copy up one step (Worn → Good; Damaged → Good or Worn).
- **Reminders:**
  - The computer glows gold, the Shop tile says *★ Upgrade ready!*, and a banner appears once a day while an upgrade is affordable. All three stop once you open Upgrades.
  - The delivery cart glows blue, with a sound and a banner, when deliveries arrive.
- **Opening days:** a short reveal plays (`RoomReveal` on GameManager):
  - the camera glides to the still-dark room
  - the lights come up and the rope swings aside, with the *fascinated* sound and a banner
  - the camera glides back

  The clock is paused while it plays. The sign then bobs *NOW OPEN!* all day. A notice also stays pinned at the top of the screen until 10 AM (the sign switches off at 10 too) (`HUD` → `hud-pin`).
- **At least one VIP** comes each day once Rare Books is open.
- **Fades:** the screen fades to black at lock-up. The Day Report sits on solid black, and the next morning fades in (`HUD.FadeThen`).
- **Shops:** both the PC Shop and the end-of-day shop have search and Subject/Type filters (`BookFilterBar`). The catalogue filters reset every time you open the PC.
- **Small talk:** students only get the exam/CGPA/advising topics (`Customer.StudentTopics`).
- **Reader chats:**
  - The first chat with a seated reader is the "where would I find more like this?" shelf question, which earns a tip bonus.
  - The second chat is one bit of small talk (two replies). Topics come from a shared pool of 35 in `Customer.Topics` (12 general + all 23 notice-board jokes), so different readers say different things, with no repeats until all are used. After that the reader just says "Shh... I'm at a good part!"-type lines.
- **Reading time:** 50–90 s (`Customer.readSeconds`).
- **Dean Whitmore** visits as a regular customer (profile *Edgar Whitmore*, Archive card). The fixed scene copies of the Dean and Maya under `NPCs` are switched off so they don't appear twice.

### Books and ordering
- **`PlayerCarry.cs`** is now a **bag**.
  - It holds `GameManager.BagCapacity` books (1 + the Book bag upgrade).
  - Books are held **side by side** in the hands.
  - API: `Items`, `Count`, `IsFull`, `Find()`, `TakeAt()`. `Carried` and `CarriedCondition` still work and return the top book.
- **`ShelfUI.cs`**: **Put back** shelves every held book that belongs to that shelf. With a full bag, Take swaps.
- **`DeliveryCart.cs`** (new): ordered books arrive here. Reserved titles come out first.
- **`GameManager.cs`**:
  - Ordering: `OrderBook(book, express)` and `OrderPrice`.
    - Standard arrives next morning; express costs +50% and arrives in 2 in-game hours.
    - Express shows on the PC Shop's Books tab during opening hours only.
  - `reservations`, `Paused` and `Dt`.
  - Unlock days and the `BookBag` upgrade.
  - The save now includes members, reservations, and copies sitting in the cart or on order.
- **PC Shop → Books**: a reservations list at the top (flags *NOT ORDERED YET*), plus Order and Express buttons and an "on order" count.

### Days (`Assets/Scripts/Day/`)
- **`RoomGate.cs`** (new): rope gate for `Archive`, `RareBooks` and `FrontDoor`.
  - Room gates open on their day.
  - The front ropes are invisible while the library is open and appear in place at night once all visitors are gone; clicking them locks up (same as `EndDayDoor`).
- **`RoomDarkness.cs`** (new) and the **`SH_RoomDarkness`** shader: a multiply box that darkens a locked room and fades out when it opens.
- **`MessSpot.cs`**: uses the **placed** teacups and open books in the scene. They're hidden in the morning and appear when someone leaves them behind.
- **`Housekeeping.cs`**: readers leave a teacup (60%) at the nearest placed cup; 1–3 open books appear at closing (these hold real copies to reshelve). Cleaner and Repairer staff.
- **`EndDayDoor.cs`**: moved to the **front entrance**, labelled "Lock up for the night".
- **`DayReportUI.cs`**: Day Report plus the Shop, Staff and Upgrades tabs. Its Shop tab now **orders** books for the next morning.
- **`NoticeBoard.cs`**: 23 "Today's Notice" lines, 6 per day, cycled by clicking.

### Repair desk (`Assets/Scripts/Repair/`)
- `RepairDesk.cs` and `RepairUI.cs`:
  - A queue with Repair and Repair All.
  - A 3-step minigame (glue, press, cover) that adds up to +20% Perfect chance.
  - Perfect Restoration returns a Good copy; Normal Repair returns a Worn one.
- Dropping off puts every worn or damaged book you're carrying on the desk.

### Core (`Assets/Scripts/Core/`)
- **`DayNightCycle.cs`** (new): blends a night volume (`VP_Night`) in from 16:00 to full at closing, and fades the window light shafts. `VP_Cozy` stays the daytime grade.
- **`GameAudio.cs`** (new): one place for gameplay sounds, assigned in the Inspector. It covers coins, customers, wrong book, tidy, cards, closing, the report, new day, unlock, panel open and close, and PC open and close. It also adds click sounds to the game UI and the PC.
- **Upgrades (9):** Repair tools, Restoration kit, Comfy queue, Running shoes, Cozy lamps, Reading nooks, Town flyers, **Book bag**, **Stamina**. Listed as rows on the PC Shop, each with a TravelBook kit icon (`.upg-N` in `PC.uss`).
- **Stamina** (`PlayerMover`): running drains a meter (3 s base, +1.5 s per Stamina level). Empty means a cooldown until it refills. A small energy bar floats over the librarian while running or recovering (`HUD`).
- **Staff are real characters** (`StaffManager` on GameManager + `StaffWorker`). Hiring order: **Cleaner** (Day 1), **Archivist** (Day 2), **Rare Books attendant** (Day 3).
  - **Cleaner: Janitor Sam.** Hidden until hired. Roams the hall, walks to each cup or open book and tidies it; open books go back to their shelf.
  - **Archivist: Omar.** Types at `SM_Chair_Wood_Archive staff` (typing sound). Fetches **one** worn or damaged copy at a time from the return bin, repairs it at the repair desk (he pays the repair cost), then goes back to typing.
  - **Rare Books attendant: Arthur.** Reads at `SM_Chair_Wood_Rare Staff`. VIP scholars with a Rare card skip the counter: he hands them a rare book and checks them in, and the fee still comes to you.
  - Both staff chairs have `SeatPoint.staffOnly`, so visitors never take them. Staff characters don't also turn up as customers while hired.

### UI
- **`HUD.cs`**:
  - Banners: morning info, closing, lunch lull, deliveries.
  - "(paused)" on the clock.
  - The carried list shows bag contents.
- **`PCShopView.cs`, `PCSettingsView.cs`, `PCMembersView.cs`, `PCAuditView.cs`**: PC screens, using templates in `Assets/Resources/`.
- **New library card form (`PCMembersView`):**
  - You **type the name** (30 characters max). Only letters, single spaces, hyphens and apostrophes get into the box.
  - On Issue the name is normalized to how cards save names: lower case, then a capital at the start of each word ("edmund THORNE" becomes "Edmund Thorne").
  - The **birthday** comes from a Day / Month / Year picker (years 1930–2010). Days that don't exist in the chosen month are greyed out, and a day that stops existing is cleared. The result is written like the profiles, for example "04 Feb 1958".
  - Access is still three buttons.
  - Issue only unlocks with a valid name (2+ letters), a full date and an access level. A wrong name or date is still rejected by the visitor (`ReceiveCard`).
- **Notebook & Audit Log**: see the section at the end.
- **`PC.uss` / `GameUI.uss`**: styles for the catalogue filters, shop rows, mood face, VIP plate and banners.

---

## 3. ⚠ Changes inside Game Dev's PC files

Small, contained edits. Please check them when merging:

- **`PCOverlayTrigger.cs`**:
  - The Members, Shop and Settings buttons open the new screens (`ShowView`).
  - Click sounds are re-hooked after every view swap (`RehookBackdrop`).
  - **Catalogue search now matches title and author only.** Subject and Type are filter buttons (`WireFilters`). This is deliberate, so players have to read abstracts to solve vague requests.
- **`PCBooksTemplate.uxml`**: added the filter button row. The gold `pc-requests` box was removed (the notebook replaces it), and the details pane has an **Add to note** button (`detail-select`).
- **`PCOverlayTemplate.uxml`**: the "Upgrades" tile now reads **"Shop"** (the element name is still `upgrades-button`). A wide **Audit Log** tile (`audit-button`) was added, and tiles are now 30% high so three rows fit.

---

## 4. Scene changes (`MainGame.unity`)

| Object | What |
|---|---|
| `RoomLocks/Gate_Archive_A`, `Gate_Archive_B`, `Gate_RareBooks` | Rope gates in the doorways (x = 10 and x = 30) |
| `RoomLocks/Gate_Front_A`, `Gate_Front_B` | Front entrance ropes (closed at night) |
| `RoomLocks/Darkness_Archive`, `Darkness_RareBooks` | Darkness boxes (`M_RoomDarkness`) |
| `RoomLocks/Sign_ArchiveOpens`, `Sign_RareOpens` | "opens Day 2/3" signs (Jersey 15, `M_SignText`) |
| `RoomLocks/EndDayDoor` | Lock-up click target at the front entrance |
| `Props/DeliveryCart` | Copy of `SM_BookCart` with `DeliveryCart` |
| `DayNightCycle` | Night volume (`VP_Night`, priority 10) |
| `GameManager` | Added `Housekeeping` and `GameAudio` |
| `GameUI` | Added `RepairUI` and `DayReportUI` |
| `SM_Table_RepairDesk` | `RepairDesk` |
| `SM_NoticeBoard` | `NoticeBoard` (replaced `InspectInteractable`) |
| 4× `SM_Teacup`, 5× `SM_Book_Open` | `MessSpot`, a small collider, NavMesh-ignored, not batched |
| CounterQueue `entrance`/`exit` | Moved to x = 20 (middle of the doorway) |

**NavMesh**
- Humanoid agent: radius **0.3**, step height **0.3**. This keeps doorways connected and NPCs off tables.
- All furniture has a `NavMeshModifier` set to *Not Walkable*. The gates, darkness boxes, cups and books are *ignored* when baking.
- **Rebake after moving furniture** (NavMeshSurface on `Library` → Bake). Last rebake: with the delivery cart at (16.4, 0, 5.0). All 39 NPC destinations (counter, shelves, seats) are reachable.

**Build Settings:** `MainGame.unity` was added after `SampleScene`. Decide which scene should load first before the WebGL build.

---

## 5. Testing tips
- **Fresh start:** right-click the `GameManager` component → **Delete Save** (or untick `loadSave`). The save loads automatically on Play.
- **Day length:** fixed at 4 real minutes (`realMinutesPerDay` on GameManager). Players can't change it.
- **Room unlocks:** start a new game and play through Day 1 → 2 → 3.
- **Tuning:**
  - `CustomerSpawner`: visitor mix and the `busyness` curve.
  - `BookRequest.OutOfStockChance`: sold-out frequency.
  - `Customer.patienceScale`: how long visitors wait.

## Hints, requests, express wishes
- Hints are gold (`<color=#D67840>`) in the speech bubble and the dialogue. The typewriter skips over the rich-text tags.
- What waiting visitors want is in the notebook on the right of the screen (it replaced the PC's gold box and `Customer.PCNote`).
- New members: 60% ask the library to get a book in (`Customer.PickWish`). This creates a `requested` reservation, and the shop marks the book with a gold "Requested" badge.
- Sold-out reservations: 40% need the book today. They return at Hour + 2.5 (`Reservation.urgent` / `dueHour`), so it has to be ordered with Express (2 h). If it hasn't arrived, they come back tomorrow. The rest pick up next morning.

## Notebook & Audit Log
- **`RequestLog`** (`Core/RequestLog.cs`, static) holds every entry: day, time, who, kind (Book / Pickup / Card), the request line, the hint, guesses (wrong ones struck through), notes and the outcome.
  - `Open` is what the notebook shows. `All` is the whole log; it is saved in `GameManager.SaveData.log` and reset on each scene load.
- **When entries change** (hooks in `Customer.cs`):
  - Begin: the first time you talk to a visitor (`LogRequest`; for library cards, what they tell you: name, birthday, line), or when you hand over a book without talking first.
  - Hint: added when asked for, or straight away for Cozy's free hint.
  - Wrong book handed over (`TryGive`): `Rejected` strikes the guess, or adds a struck "Handed:" line.
  - Library cards: every card submitted from the PC is a line (`ReceiveCard` → `CardAttempt`). Wrong ones show the visitor's reason. A new member's wish is a note.
  - Finish: sold, reading, reserved, sent away, left angry, never ordered, card issued, closing time. `Leave()` closes anything left as "Left". A finished entry leaves the notebook.
- **`NotebookHUD`** (`UI/NotebookHUD.cs`, `Resources/Notebook.uxml`, `UI Toolkit/Notebook.uss`):
  - Created at runtime by `HUD.Start` on a copy of PS_Game with **sort order 25**, so it stays above the PC (20) and its dark backdrop, and under the main menu (30). It must be a **root** GameObject: a UIDocument under another UIDocument joins that one's panel and ignores its own sort order.
  - Sits on the right edge, vertically centred. The tab button collapses it, and the state is remembered in PlayerPrefs `NotebookCollapsed`.
  - Each entry has a Clear button, which removes it from the notebook but not from the log.
  - Hidden on the title screen and behind the Day Report.
  - `NotebookHUD.PointerOver` stops clicks on it reaching the world (`InteractionController`) or skipping dialogue (`DialogueUI`).
- **Reservations panel** (bottom right, in the same `Notebook.uxml`):
  - Lists `GameManager.reservations`: who, which book, when, and whether it's ordered. The status uses the same rules as the PC Shop, which is unchanged.
  - Starts collapsed every session.
  - `GameManager.Reserved` fires on each new reservation. The first one in a session opens the panel by itself. Later ones make the tab gold and pulsing (`.notebook-tab.new`) until you open it.
- **PC catalogue: Add to note** writes `Noted: "Title" (Shelf)`.
  - With a book request open, it goes on the newest request. A new pick replaces an untested one; after a wrong one it adds a new line. It's only a note: any matching book still works.
  - With no request open, it goes on a **To Shelve** page, for books from the return bin, tables and so on. Putting a book back on its shelf (`ShelfUI.PutBack` → `RequestLog.Shelved`) ticks it off. The page isn't part of the audit log.
- **Audit Log:**
  - PC → **Audit Log** (`PCAuditView`) has a button for each day so far, with today selected. Newest entries are at the top.
  - Day Report → **Log** tab shows today's entries.
  - Both use `RequestLog.BuildRow`, styled by `.log-*` classes in `PC.uss` and `GameUI.uss`.

## Clock, staff chat, new members
- The clock only stops for talking, cutscenes and menus (`PlayerLock.FreezesTime`). While you use the PC, a shelf or the repair desk, time keeps running and visitors' patience keeps draining.
- Staff give one real chat, then a "busy" line for 3.5 game hours (`StaffTalk.TalkCooldownHours`).
- New members without a book wish stay 60% of the time (`Customer.StayAsMember`): they browse, or queue again to read or buy.

## Opening interview (InterviewScene)
- When you pick a mode for a fresh game, or start over, the save is deleted and `InterviewScene` loads (`MainMenuUI.introScene`, empty = the old direct start).
- Dean Whitmore interviews you. Every answer works, and he hands the library over.
- Then you interview three cleaners (Sam, Finn, Leo), with 3 of 5 questions each. Hiring loads `MainGame`, skips the title screen and starts Day 1.
- The hire is saved in PlayerPrefs: `Hired_Cleaner`, `HiredPrefab_Cleaner` and `HiredStats_Cleaner` (speed,care,charm,wage).
- `GameManager.ApplyInterviewHire` puts the hired cleaner on staff for Day 1, with that day's wage already paid, and uses their wage.
- Speed stars scale the cleaner's walk speed (`CleanerSpeed`). Care and Manner only show in the interview; they have no gameplay effect yet.
- If you hire Finn or Leo, `StaffManager.cleanerCandidates` spawns that character instead of the scene's Sam. `StaffTalk` uses their name but still Sam's chat lines.
- "Skip intro" (top right) hires Sam.
- `StaffInterview.prologue = false` turns the scene into a plain interview for any role (PlayerPrefs `InterviewRole`).

## Hiring by interview
- The Staff lists (PC Shop and Day Report) show only the Archivist and the Rare Books attendant (`GameManager.StaffOrder`). The cleaner comes from the opening interview.
- **Interview** books applicants for the next morning (`GameManager.interviews`, which is saved), and a banner tells the player. **Cancel** unbooks. **Let go** still works straight away.
- On "Open next day":
  1. The screen fades out and `RunMorningInterviews` loads `InterviewScene` on top of the library. The room is moved 300 m away, and the library's camera and all its UI are hidden.
  2. Each booked job has 2 applicants. You hire one of them.
  3. The room fades out and unloads, the day starts, and the screen fades back in.
- New hires get paid with the normal morning wages.
- Per-role hire data lives in `GameManager.StaffPerson`, `StaffPrefab`, `StaffSpeed` and `StaffWage`, loaded from PlayerPrefs by `LoadHires`.
- `StaffManager.staffCandidates` holds the prefabs for Finn, Kai and Clara.
- Applicants: Cleaner = Sam or Finn, Archivist = Omar or Kai, Rare Books attendant = Arthur or Clara.

## Hiring by interview
- The Staff lists (PC Shop and Day Report) show only the Archivist and the Rare Books attendant (`GameManager.StaffOrder`). The cleaner comes from the opening interview.
- **Interview** books applicants for the next morning (`GameManager.interviews`, which is saved), and a banner tells the player. **Cancel** unbooks. **Let go** still works straight away.
- On "Open next day":
  1. The screen fades out and `RunMorningInterviews` loads `InterviewScene` on top of the library. The room is moved 300 m away, and the library's camera and all its UI are hidden.
  2. Each booked job has 2 applicants. You hire one of them.
  3. The room fades out and unloads, the day starts, and the screen fades back in.
- New hires get paid with the normal morning wages.
- Per-role hire data lives in `GameManager.StaffPerson`, `StaffPrefab`, `StaffSpeed` and `StaffWage`, loaded from PlayerPrefs by `LoadHires`.
- `StaffManager.staffCandidates` holds the prefabs for Finn, Kai and Clara.
- Applicants: Cleaner = Sam or Finn, Archivist = Omar or Kai, Rare Books attendant = Arthur or Clara.

## Ending (after Day 5)
- On Day 5's "Open next day", `GameManager.EndingDue` (`day >= EndingDay` and not `endingSeen`) loads the interview room on top of the library.
- The Dean walks in and sits in the seat you sat in on Day 1, while you take the interviewer's chair.
- He talks through your real numbers (members, staff names, days, reputation). You answer one question with 3 replies to pick from.
- Then a **The End** page shows with your stats. **Keep running the library** fades back, and play carries on into Day 6.
- `endingSeen` is saved, so it only plays once. If interviews were booked for the same morning, they run straight after the farewell.

## Staff: fees, upgrades, help calls, reports
- **Hiring:**
  - The one-time hire fee (`GameManager.StaffHireFee`: Archive 250, Main hall 70, Rare 650) is paid when you pick someone in the morning interview. Booking needs that much in the purse.
  - The Dean pays the cleaner's fee in the intro.
  - The small daily wage is still paid each morning, and staff quit if you can't pay.
- **Upgrade** replaces "Let go":
  - Prices are in `StaffUpgradeCosts`: Main hall 35 → 70, Archive 150 → 300, Rare 650. Levels are saved in `staffLevel`.
  - Each level gives +35% work speed (`StaffWorkFactor`): walk speed, cleaning time, and the repair desk speed for the archivist. Help calls also come less often.
  - The staff rows (PC Shop and Day Report) share `GameManager.StaffRow`.
- **Help calls (`StaffHelp`):**
  - Every 80–150 s of game time (× (1 + level)) a staff member stops work. A "Help!" pointer appears over them, pinned to the screen edge with an arrow when they're off screen (`WorldPrompts.Pin`).
  - Click them to pick a fix:
    - paid (coins): they're back to work at once
    - free: they wait 8 more seconds
  - The Rare Books attendant can't serve VIPs while stuck.
- **Reports:**
  - At the closing bell, staff stop and wait with a "Report" pointer. Clicking them gives today's numbers (repaired, shelved, cleaned, scholars served, times you helped), then they go home.
  - If you lock up first, they just leave.
- **Interview:**
  - The wage question is gone. "Why the Whitmore?" now shows whether they'll bargain.
  - After each answer, an orange thought line shows the stat. With 3 picks from 4 questions, one thing stays "?".
  - At hire time you haggle: offer −4, −2 or agree. Flexible people go down to their ask − 3 and counter once. Push again and they insist on the full price. Firm people won't budge.

## Update: pay is talked over in the interview, and the intro hires nobody
- The intro (`InterviewScene` on its own) is only the talk with the Dean. Nobody is hired, and every role is hired from the computer: Interview → next morning, after the Day Report.
- No daily wages any more (`StaffWage` is all 0). Each applicant asks a one-time price around `StaffHireFee` (70 / 250 / 650).
- In the interview, "Let's talk about pay" (free, once per applicant):
  1. They name their price, and you offer 60%, 80% or the full price.
  2. If the offer is at or above their private `floor` (random each interview: keen people 55–80%, others 80–95%), they agree politely.
  3. If it's lower, they say the lowest they can go, and you can accept that or think it over.
- The agreed price (or their asking price) is shown in their notes and on the Hire button, and paid when you hire.
- This replaces the post-hire bargaining and the daily-wage text in the section above.
- Reports: at the closing bell, staff walk to the main gate and wait there side by side (`StaffManager.arrivePoint` ± 1.2 m).
- **Reports pay out and are required:**
  - Staff keep the coins their work earns during the day and hand them over with their report:
    - cleaner: each mess's reward
    - archivist: `perRepair` 4 per repaired book and `perShelved` 1 per shelved book
    - attendant: the reading fee and tip of each scholar they check in
  - The front ropes (`EndDayDoor`) won't lock up while anyone is still waiting to report (`StaffManager.PendingReport`).

## Player name
- Before the intro starts, a name box asks your name (`name-entry` in `InterviewUI.uxml`, max 16 characters, OK or Enter).
- Saved as PlayerPrefs `PlayerName` and read with `GameManager.PlayerName` (default "Librarian").
- It replaces "You" as your speaker name in all interview dialogue, and the Dean uses it in his lines and in the ending.

## More visitors (15 recoloured NPCs)
- `Assets/Prefabs/Characters/Variants/PF_NPC_*` are Prefab Variants of the original NPCs. Each one only swaps in a recoloured mesh (`Assets/Art/Models/Characters/Variants/SM_NPC_*`).
- The recolouring moves palette UVs, so they use the same texture and material as the originals.
- They're in `CustomerSpawner.customerPrefabs` (MainGame, 28 visitors) and have member profiles in `Members.Profiles`.
- Visitors whose prefab name contains "Professor" (Victor, Ingrid) come as VIP scholars.
- A review row of all 15 is in `Library Management.unity` (`NPC_Variants_Review`).

## Book requests: "Can you order it?" (from Day 1)
- `BookDatabase.newTitles` holds 12 books the library doesn't own yet (main-hall subjects).
  - At runtime they live in `GameManager.NewTitles`.
  - `OrderBook` on one calls `AddToLibrary`, which moves it into `Books` with 0 copies until the delivery arrives. It's saved in SaveData `added`.
- **Seeker visitors** (`Customer.Kind.Seeker`):
  - They pick a new title (or a sold-out book), check 1–2 shelves (the right one first), then queue and say they couldn't find it.
  - Only on seeker days (`CustomerSpawner.SeekerDay`): Day 1 always, then about one day in three (20% a day, the same days for a given game via `storySeed`), never more than 3 days in a row without one. Just one that day, at a random time between 10:15 and 2:30.
- **At the counter:** "When do you need it?" → they answer "As soon as possible" or "I'll come back tomorrow", and you pick "I'll order it for you" or "Sorry".
  - **ASAP** (`ReserveAndWait`): they sit in the library (`State.WaitingForBook`).
    - When the ordered copy arrives (in stock or on the cart), they come to the desk to collect it.
    - About 30 minutes past their time (or at closing): if it's on order they come back tomorrow; if you never ordered it they leave unhappy (−reputation).
  - **Tomorrow:** a normal reservation and a pickup the next morning.
- Sold-out buyers use the same "When do you need it?" flow, and it now works from Day 1.
- The PC Shop and the Day Report shop list requested new titles (gold "NEW · Requested" badge) so you can order them. Members' wishes pick new titles first.
- Shelves show damaged copies in red (title, count and the "Damaged" button).
- The Repair desk's left page also lists worn and damaged copies still out in the library (shelf and returns bin).
- **Closing:**
  - `Customer.HandleClosed` runs every frame once the library is closed. Readers (even ones still walking to a seat) stop and queue to check their book in, and anyone holding a book waits for you; everyone else goes home.
  - Readers who sit down after the closing bell only read for about 8 s.
- **Spawner:** the same character is never in the library twice at once, and pickups wait if that person is already inside. The Dean visits at most once a day.
- **Saying "Sorry, we can't get that"** removes any reservation for that visitor.
- **New members' wishes are no longer auto-reserved:** the member asks you at the desk (same "When do you need it?" flow), and you decide.
- **Shops:** not-yet-owned titles are in the normal, searchable book list ("New to the library"), not only under Reservations.

## Staff and VIP polish
- **Interview:** Hire costs the last price you settled on: the agreed price, or the lowest one they mentioned (`FinalPrice`).
- **Staff problems:**
  - While a problem is open, the person can't do their job (`StaffWorker.CanWork`). The cleaner roams without tidying; the archivist and attendant sit at their desks. They remind you every 30 s.
  - The paid fix gets them working at full speed. The free fix gets them working, but slower for the rest of the day (`Slow`).
- **Archivist:** also fetches worn and damaged copies from the shelves. If the till can't pay for a repair, they say so (bubble and banner).
- **Staff pay:**
  - Archivist: 5 + 40% of the book's value per repair, 2 per book shelved.
  - Cleaner: 5 per cup, 6 per book, 2 per returned copy shelved. The cleaner now also shelves good returns.
- **Staff hours:** they work until the library closes at 6, then report.
- **Rare Books attendant:** costs 450, and the upgrade costs 450.
- **VIPs:** cards cost 50 (+ up to 10 tip) and reading fees are ×1.5 (see the VIP income note below). VIPs stay after getting a card and go to read.
- **Dialogue:** half-typed rich text is auto-closed, so coloured hints type in instead of popping in.
- **Notebook and Reserved lists:** they scroll instead of running off the screen.
- **Click guard** (`Interaction/ClickGuard.cs`): for 0.25 s after a panel opens (`PlayerLock.Lock`), after choices appear, or after the interview hire and end pages show, button presses are swallowed, so one click can't also press a button that just appeared under the cursor. It's attached to the game UI, the PC overlay and the interview UI.
- **Interview:** pay is discussed automatically after the questions ("Before you go, let's talk about pay."); it's no longer in the question menu.

## Popular books, lean start and request icons
- Popular today: `GameManager.PopularToday` holds 4 books for sale (`PopularPerDay`), picked at random each day (seeded by the day, so a reload keeps the same set). `BookRequest.Create` asks for them 6x as often, including when they're sold out. They glow gold with a "Popular" tag in the catalogue, the Shop, the end-of-day shop and on shelves (`PopularGlow`). Readers only ask for books that are in stock. On Day 1, in-stock books count double.
- New game only (`!HasSave`): `GameManager.LeanStartingStock` cuts each book for sale to 1 copy, and to 0 for Best Sellers. Archive and Rare Books keep their stock.
- Request icons: `Customer.UpdateRequestIcon` puts an icon to the left of the prompt at the desk. The classes (`req-card/buy/read/return/new/rare/collect`) are in `GameUI.uss` and `PC.uss`, and the PC tiles use the same ones. The kit has no book icon, so `RequestIcon` draws the "read" book in code.
- Text overflow: in Play mode, open a screen and run Tools > Check UI Text Overflow (`UITextAudit`). It lists any text that doesn't fit its box.
- Catalogue fix: a new search clears the ListView's old selection index, and each row has its own click handler, so clicking a single result always fills the details panel.
- Drop-downs: `PickList.Open(button, options, current, pick, pcStyle)` shows a list under a button (it stays inside the panel around the button and closes on a click outside, or when its screen closes). It's used by the catalogue filters (Subject / Type / Show), the shop filters (`BookFilterBar`) and the Members birthday (Day / Month / Year). "Show: Reserved" lists books someone has reserved.
- Visitors don't say "express" any more. The computer (Shop, notebook) still marks urgent orders as express.
- Upgrades unlock by day: `GameManager.UpgradeUnlockDay` (in `UpgradeKind` order). Restoration kit, Cozy lamps and Reading nooks unlock on Day 2, Town flyers on Day 3, and the rest on Day 1. Locked rows show "Day N", and `BuyUpgrade` refuses them. Levels already bought in an old save are kept.
- Glow for things you can do: affordable upgrades and staff rows (an interview you can afford, or a hire's upgrade) glow in the PC Shop and the Day Report. Their tabs and the computer's Shop tile glow too (`PopularGlow.Ready`, `GameManager.UpgradeReady` / `StaffReady`).

## Play-test fixes (Days 1-5, scripted run)
- Delivery cart: clicking it now hands you the book the visitor at the desk is waiting for first, then other reserved titles (it used to give the first reserved book in the list).
- Pickups: if you say "it isn't here yet" for a book that was ordered and is already on the cart, they come back tomorrow. They're no longer told it was "never ordered", which made them leave angry and cost reputation.
- Reader chat: "where would I find more like this?" no longer offers shelves in rooms that are still locked.
- Archivist pay: 5 per repair plus 40% of the book's value, capped at `StaffWorker.maxRepairBonus` (20). Rare books used to pay up to 200 per repair (489 in one day).
- Delivery cart page: clicking the cart opens the shelf screen in cart mode (`ShelfUI.OpenCart`). Each title shows how many copies came and who it's for (someone at the desk, a reservation, or which shelf it goes on), with a Take button. Put back returns copies that came off the cart (`DeliveryCart.Take`, `PutBack`, `CameFromCart`). An express delivery that arrives while it's open shows up straight away.

## VIP income rebalance
- From Day 3 money used to explode (156 to 1,938 coins in one play-tested day): VIP rare reads paid 73-198 each, and cards 80 + 20.
- Now: card 50 + up to 10 tip. Rare reading fee 6% of the book's value (minimum 15), ×1.5 for VIPs. The "right shelf" chat bonus is capped at 10. A rare read pays about 43-77 with tip, still 3-6× a normal reader (about 13).
- The numbers are constants at the top of `Customer.cs` (`VipCardFee`, `VipCardTip`, `VipFeeMultiplier`, `RareShare`, `RareMinFee`, `MaxChatBonus`). The table is in `TUNING_GUIDE.md`.

## The Dean's goal, consequences and the ending
- **Goal:** reach `GameManager.ReputationGoal` (Cozy 40 / Standard 60 / Busy 75) by the end of Day 5. The Dean sets it at the end of the new-game intro. It shows on the HUD bar (star, "37/60", gold once reached, with a +/- pop), in the morning banner and in the Day Report (how many to go and how many days left). After the ending the HUD shows just the reputation.
- **Ending:** `GoalTier` (2 reached, 1 close at 60%+, 0 missed) changes the Dean's farewell: the board signs the library over, wants one more season, or is worried. The end page shows "Reputation X of Y" and the result. The game carries on either way.
- **Consequences:** wrong book -1 reputation. Walked out -3, never ordered -3, said "we can't get that" when you could -2, waited for nothing -2. A member let down in those ways stays away the next day (`Member.awayUntil`, saved with the members). Pickups still come.
- **Patience:** `PatienceDayFactor` cuts patience to 85% on Day 3 and 75% from Day 4 (all modes; Cozy visitors still never walk out).
- **End page:** besides "Keep running the library" there's "Back to main menu" (`StaffInterview.EndingToMenu`). The new day is started and saved first (so the ending isn't replayed), then `GameManager.ReturnToMenu()`; Continue on the title screen picks up from there.
- **Visitor tags** sit at 1.4 m above the visitor (was 1.75) so they don't hide the counter computer. Card applicants' name and birthday are highlighted in their line and in the notebook.

## The first week's story and daily twists (`Day/DayStory.cs`)
- Runs on Days 1 to 5 only (`GameManager.GoalActive`). After the ending, days are ordinary again. `GameManager` adds the component at runtime (no scene change).
- **One twist a day**, in a random order per game (`GameManager.storySeed`, saved; never a power cut on Day 1): exam rush, book thief, leaky roof, inspection, book signing, power cut. Each has 4-5 beats (someone at the desk, a banner, a pointer, a popup) and a real effect:
  - **Exam rush**: more students, mostly readers, after one subject, a bit less patient.
  - **Thief**: a "Stranger in a long coat" browses, pockets a book and walks slowly to the door. Click them (`Customer.Caught`) to get it back; if they get out, the copy is lost.
  - **Leaky roof**: a shelf gets a "Leak!" pointer; until you click it (`ShelfInteractable` → `DayStory.FixLeak`) copies on it turn Damaged.
  - **Inspection**: Mr. Pryce looks around 1:00-2:30; mess on the floor and walk-outs decide the verdict.
  - **Book signing**: one of the library's own authors; their two books are popular all day and asked for more during the signing; sales decide the thank-you.
  - **Power cut**: 12:00-2:00 the PC won't open (`PCOverlayTrigger`), the library goes dark (`DayNightCycle` uses `DayStory.Darkness`), visitors are more patient.
- **The week's own beats** (the same every game): Walter's first card (Day 1), the car-park rumour (Day 1), the Archive historian and the lost glasses (Day 2), the Board's letter and the Archive cat (Day 3), the writer and the man with the clicker (Day 4), the Dean's note and the regulars' card (Day 5).
- **Story visitors** are normal `Customer`s with `story` set (`CustomerSpawner.SpawnStory`): they queue, wait without patience, and play a `DayStory.Visit` scene when clicked. Afterwards they can ask for a book like anyone else (`ContinueWithRequest`), sign up (`ContinueAsSignUp`), look round the shelves and come back for a second scene (`lingerUntil` + `next`), or leave. If their character is busy (working here, already inside) an alternative steps in.
- **Letting the player know**: the morning banner names the day's twist, the notice board pins a notice for it first, banners and pointers mark each moment, and the Day Report has a row for it.
- **Small talk**: the first time you talk to a visitor, some mention their day first (`DayStory.Opener`), often about the twist. Days 1 to 5 only: the 28 everyday lines are shared out across those days (5 or 6 each, no repeats), and after the ending there's no small talk.
- **Pickups** no longer all come at opening: each gets its own time across the day (`CustomerSpawner.QueuePickups`).
- To test a twist without playing to it: in Play mode set `GameManager.day`, pick a `storySeed` whose `DayStory.TwistFor` is the twist you want, call `SetUpDay` and move `GameManager.Hour` (nothing is saved until the next day starts).

## Coin purse (HUD)
- Coins have their own purse in the top right (`#hud-purse` in `GameUI.uxml`), out of the bottom bar.
- Getting paid (`HUD.OnCoins`): 3 to 9 coins fly on a curve from the librarian up into the purse; each one that lands shakes the purse and updates the total; a big green "+12" floats up under it. Spending: a shake and a red "-40". The coin sound still comes from `GameAudio`.
- Effects are skipped (just the number changes) until the HUD has been laid out, e.g. on the first frame.

## Stuck visitors
- Visitors heading to a shelf aim for a slightly random spot beside it (`Customer.ShelfSpot`), so two of them don't fight over one point.
- `Customer.Watchdog()` runs every frame: a visitor who is walking but hasn't moved 20 cm in 3 s (game time) is freed. Within 1.5 m of where they're going, they count as arrived; after 6 s they hop (NavMeshAgent.Warp) to their destination. In the queue they hop to their place in line.

## Small fixes (catalogue buying, freezing, VIP seats, reservations, staff help sound)
- **Catalogue buying**: "Buy books" in the catalogue header opens the Shop's Books tab with the same search (`PCShopView.OpenOnBooks`); its Back returns to the catalogue. "Buy (price)" beside "Add to note" orders one copy (normal delivery, tomorrow). It's hidden for Rare Books and greyed out when you can't afford it.
- **Nobody walks during a conversation**: while `GameManager.Paused` (dialogue), customers' and staff's NavMeshAgents are stopped (`Customer.Update`, `StaffWorker.Update`); staff resume, or stay put if on hold.
- **VIPs read in Rare Books** whatever the book, and wait there for express orders; a main hall chair only if every rare seat is taken.
- **Reserved panel** (right edge) lists only reservations still to order; once ordered (or in stock / on the cart) they leave it.
- **Staff help** has its own sound (`GameAudio.staffHelp` = `Audio/SFX/staff help.mp3`), rung 3 times 0.7 s apart (`StaffHelp.Ring`), not the impatient counter bell.

## Staff mood, problems and special jobs (`StaffHelp`, `StaffTalk`, `GameManager`, `Day/SpecialJobs.cs`)
- **Problems**: 8 per role, no repeats in the same day (`StaffHelp.usedToday`).
- **Mood** (`GameManager.staffMood`, 0-5, saved, 3 on hiring): speed x(0.7 + 0.1 x mood) via `StaffWorkFactor` (agent speed, cleaner timer, repair desk). Paid fix +0.5 and they come in **rested** tomorrow (x1.25 that day, `staffRestedDay`); free fix -0.3 (and slower for the rest of today, as before); coming over within 20 s +0.2; each 30 s reminder while you leave them waiting -0.15; first chat of the day +0.15; nobody talked to them all day -0.3 at the report. A banner says when their mood word changes (happy / content / grumpy / fed up). Shown in the Staff rows and in their end-of-day report.
- **Special jobs** (once anyone is hired, from Day 2; one a day, offered at the desk around 10-11:45, can be turned down):
  - Special order: a regular wants a title by tomorrow; becomes a "Requested" reservation with `Reservation.bonus` (15-40) paid on collection (+1 reputation).
  - VIP request: a professor wants a rare book in your hands at 3:00 PM (+40 coins, +2 rep; not ready: -1 rep).
  - Book club: 3 copies of a main-hall book on the shelves at 4:00 PM; they buy them all (+15 bonus, +2 rep; too few: -1 rep).
  - Accepted VIP / book club jobs show in the Reserved panel with progress.

## Feedback polish
- **Patience**: bigger mood face with a draining bar beside it (`.mood-bar`), green / amber / red; the face throbs when they're about to give up. Angry exits (`Customer.StormOff`): a stomp on the spot with a shaking red face and red "Hmph!", the impatient bell, then they march out at 1.8x speed.
- **Skill rewards** (`HUD.SkillPop`, gold text rising from the spot): right shelf in a reader chat (bonus paid on the spot, +1 rep), perfect repair (+5 coins, +1 rep), special order collected, VIP request done.
- **Day Report Summary tab** (opens first each evening): income by source (`DayStats.Earn`: sales, reading fees, cards, VIP, staff, other), what went wrong (walk-outs, wrong books, people let down, mess, unordered reservations, unhappy staff) and one tip for tomorrow picked from the day's biggest problem.

## Tutorial and first-time tips (`UI/Tutorial.cs`, added to the HUD at runtime)
- **Guided Day 1** on a new game (Day 1, no save, `PlayerPrefs` "TutorialDone" unset; a new game / `DeleteSave` resets it and the tips). A card on the left says what to do, an arrow (`DayStory.Point`) shows where, and the camera zooms in (a temporary Cinemachine camera, 0.7x size) on the computer, the right shelf and the front door the first time each is needed.
- Steps: talk to a buyer (asks for an author with a book in stock), look it up (catalogue, `PCOverlayTrigger.LastViewed`), take it from the shelf, hand it over, talk to a card sign-up, fill in the card (Members), talk to a seeker, order it (`GameManager.OrdersPlaced`). Then the day runs; at closing it shows how to lock up, and ends when the Day Report opens. "Skip tutorial" any time.
- While guiding (`Tutorial.HoldsClock`): the clock waits (so the story's beats wait too), only the tutorial's own visitors come (`CustomerSpawner.SpawnTutorial`), patience x2 and nobody storms out, no staff problems, no small talk. A visitor who leaves early just moves it on to the next step.
- The notebook folds away (`NotebookHUD.Tuck`, not saved) while the arrow points at something behind it.
- **First-time tips** (`Tutorial.Show(key, text)`): once per game each, never while the tutorial guides you or a screen/conversation is open; queued and shown one at a time on the same card (10 s, or "Got it"). Keys: seeker, soldout, damaged, staffhelp, pickup, vip, delivery, mess, reader, checkout, returns, wrongbook, express.

## Mouse cursor (`UI/GameCursor.cs`)
- TravelBook `Cursor01c` normally, `Cursor01d` over something clickable (`InteractionController.HoveringSomething`, or a UI Toolkit button under the mouse on any panel) and while the left button is held.
- The 14x16 art is scaled up in whole pixels for the screen (`Screen.height / 540`, 2x at 1080p) at runtime, so the two textures are imported Read/Write, uncompressed, point filter.
- Each click plays `MouseCursorClick01a` frames 1-3 (`GameCursor.click`, 0.06 s a frame, hotspot (8,6) so the tip doesn't jump), then back to the arrow. Those frames are imported Read/Write, uncompressed, point filter too.
- On its own root object `GameCursor` in MainGame; `DontDestroyOnLoad`, so it carries into the intro scene; a reloaded scene's copy destroys itself.

## PC redesign (tabs, pixel monitor, catalogue buying)
- **Size:** the monitor is 1040 x 782 (the old PC size), so it stays clear of the notebook on the right edge; maximise makes it 1460 x 1040 but keeps it left of the notebook.
- **Colours (oxblood leather + brass):** the screen is leather (`--page` rgb(94,43,38)) with light text on it (`--desk-ink`, `--desk-dim`) and a gold line under the tabs. Everything else sits on brass: cards and the sheets behind the book list, members table, log and settings (`T_PC_Card`, rgb(227,201,143)). Inputs, dropdowns and tooltips are lighter (`--input`, `T_PC_Input`, `T_PC_Menu`), buttons are cream (`T_PC_Button`), and the picked book is cream with a 3px orange outline. Disabled buttons are solid (`T_PC_ButtonOff`), never transparent, so they don't show the leather through; locked upgrades use `T_PC_CardLocked` and affordable ones `T_PC_CardReady` (orange edge). The tabs are pixel buttons; the open one is the orange `T_PC_ButtonHot`.
- **Look:** a cream pixel monitor (WHITMORE plate, two knobs and a power light on the chin) with a parchment screen. The pixel corners come from small 9-sliced sprites in `Assets/Art/UI/PC` (`T_PC_Button`, `T_PC_ButtonHot`, `T_PC_Card` + Epic / Legendary / Warm, `T_PC_Input`, `T_PC_Menu`, `T_PC_Badge` (tinted), `T_PC_Chip`, `T_PC_Stamp` (tinted), `T_PC_Case`), drawn at 1 pixel per unit and scaled with `-unity-slice-scale`. All are Point filter, uncompressed, no mips. Styles are in `PC.uss`.
- **Frame:** `PCOverlayTemplate.uxml` is now the monitor: title bar (close = log off, minimise = log off but come back to the same tab, maximise = a bigger monitor), "WHITMORE OS", and Day · clock · coins. Under it are the tabs **Home, Catalogue, Members, Shop, Log, Settings**; there's no hub and no Back buttons. `PCOverlayTrigger.ShowTab(name)` clones the tab's template from `Resources` into `#page` and calls its view's `Wire(page)`. A 0.45 s ticker keeps the clock, coins and tab counts live.
- **Tab counts:** Catalogue shows reservations nobody has ordered yet (red if one is due today), Members shows 1 while someone waits for a card. The Shop tab shows the kit star (it pops) on **every** tab while an upgrade or a hire is affordable, and its name glows gold unless you're on it.
- **Home (`PCHomeView`):** six numbers (titles, copies on shelf, sold out, visitors served, coins earned today, members); **Needs you** (max 3, only things you can fix on the PC: unordered reservations → *Order* opens a fresh Catalogue so you search for it yourself, a card waiting → Members, an affordable upgrade or staff action → Shop); the day bar (9:00 to closing, a "now" marker, and dots for urgent pickups, today's pickups (`CustomerSpawner.PickupTimes`), Express deliveries (`GameManager.ArrivingToday`) and closing; hover a dot for a tooltip, things in the same 10 minutes share one dot); reputation toward the goal; today's popular books; and the Today / Collection / Library ledgers. It rebuilds only when its numbers change.
- **Catalogue (`PCCatalogueView`, moved out of `PCOverlayTrigger`):** lists every title, including new titles you don't own yet (at the bottom, "New title"). Filters: **Type** (Book, Newspaper, Magazine, Journal, Research paper, Archive, Rare Books) → **Subject** (greyed out until a type is picked; books are grouped by shelf) → **Show** (adds "Not in library"). Rows: title + badges, "author · subject", then shelf icon, coin + order price, and the damaged icon if any; stock on the right. Epic and Legendary rows/cards get an indigo / plum edge and badge. The card has **Add to note**, **Order** and **Express** (greyed out after closing); the old Shop "Books" tab and "Buy books" are gone.
- **Shop (`PCShopView`):** Upgrades (cards with pixel icons and level pips) and Staff (one row per role, icons: broom, books, key). Price buttons read "Buy - (coin) 80".
- **Members:** a table (name, born, access, kit hearts, visits, spent) and the library card form; Day / Month / Year lists show 6 rows (`PickList` `maxVisible`), Issue card is full width at the bottom.
- **Log (`PCAuditView`):** time, a dot for how it went (a coin for money lines), what happened, the outcome and +/- coins.
- **Settings:** volume and music are pixel staircases; **Mode is gone** (see Play modes).
- `PickList` now has group headings and a row cap, and the parchment look on the PC.

## Subjects by type (`BookData.Topic`)
- Every title has a **Topic** (shown as "Subject" on the PC): finer subjects for books (Astronomy, Biology, Trade, Poetry, Probability...), and their own for newspapers (Local news...), magazines, journals, research papers, the Archive (Town records, Letters & logs...) and Rare Books (Manuscripts, Maps & charts...). `Subject` (Science / History / Philosophy / Math) is still the section and shelf, and the exam rush still uses it.
- `BookData.TypeName` is the PC's type: Archive / Rare Books come from the shelf prefix (like `BookRequest.IsArchive` / `IsRare`), the rest from the format. `SubjectName` is the topic, falling back to the section.
- Visitor subject requests use the topic ("I need a good book on Astronomy.", "I need a journal on Physics."); a request made with a section name (the exam rush) still matches.
- `BookFilterBar` (also used by the Day Report shop) has the same Type → Subject filters.

## New titles cost more, sell for more
- Titles from `BookDatabase.newTitles` are flagged `NewRelease` at runtime; `SaleValue` = BaseValue × 1.3 (`BookData.NewReleaseMarkup`). Order prices and sale prices (counter sales, browsers, the book club) use `SaleValue`, so a new release costs about 30% more to order and sells for about 30% more. Rare books are still far above both.

## Money in the Audit Log
- `RequestLog.Entry.coins` (+ earned, - spent) and a new `Kind.Money` for lines that aren't a visitor's request. `RequestLog.Paid(customer, amount, ...)` adds to the visitor's entry (sales, fees, cards, chat bonus, special orders), or makes its own line if they have none. `RequestLog.Money(who, line, outcome, amount)` logs everything else: book orders and Express, upgrades, staff hires and training, wages, paid staff fixes, repairs, perfect-repair donations, tidy rewards, staff reports, VIP and book-club rewards, story rewards.
- Outcome strings no longer end in "+N"; the amount is its own column on the PC and in the Day Report log.

## One button style everywhere
- Every button in the game uses the computer's pixel button (`Assets/Art/UI/PC`): `T_PC_Button` (cream, dark outline, bevel), `T_PC_ButtonHot` (orange) on hover and for the selected one, `T_PC_ButtonOff` (flat, greyed) when disabled. They're 9-sliced at 3 px per art pixel (2 px for small buttons) and pressing moves them down 2 px.
- That's `.kit-button` and the dialogue `.choice` in `GameUI.uss`, so it covers the main menu, How to Play, mode cards, settings, Day Report (its tabs: the selected one is orange), shelves, repair desk, notebook tabs (a new reservation turns the tab orange), tutorial and the interview scene. The PC uses the same sprites through `.pc-button` in `PC.uss`.
- Hover text turns light (rgb 255,246,232) so it reads on orange; `MainMenu.uss` no longer turns hovered text orange.
- The round cross close buttons (`.close-button`) and the hover prompt / speaker name tag (not buttons) keep their TravelBook art.

## Story visitors who only want a chat wait around the library (`Customer`, `DayStory.Visit.atDesk`)
- **Regular visitors are unchanged:** everyone queues at the counter as before.
- **Story visits** (DayStory, SpecialJobs) are split: `atDesk = true` for the essential ones (each twist's introduction, the inspector and his verdict, the visiting author, special-job offers and the VIP's return), which queue at the counter. The rest only want a chat: they walk somewhere (`GoRoam` → `State.ToSpot` → `WaitHere`): a bookshelf, the newspaper / magazine racks, a chair (they sit) or the notice board, and wait with a "?" bubble and a silent "?" arrow at the screen edge (`DayStory.Point`) until you talk to them. They turn to face you when you come close. If they then ask for a book or a card (`ContinueWithRequest` / `ContinueAsSignUp`), they do it where they are.
- **Conversation order** (at the counter): books in your hands first, then "Let me find it", "Could you give me a hint?", "When do you need it?" (sold out / seekers), "Sorry, we can't get that".
- First-time tip "ask" explains the "?".

## Finding books
- **Highlights** (`BookRequest.H`, #D67840): titles, authors, subjects, the tag words and the key word of "about" requests, in visitors' lines (and so the notebook), story / job dialogue and banners.
- **"About" requests** highlight their most telling word (`BookRequest.KeyWord`: 4+ letters, not common, found in the fewest other descriptions). The catalogue search now also matches **descriptions** and subjects, so typing that word finds the book.
- **Shelf screen:** click a book's row to read its description on the right page (`ShelfUI.Pickable`).

## Notebook "To do" (`RequestLog.Task` / `TaskDone` / `EndTasks`)
- Today's events and accepted jobs are notebook entries ("To do"): exam rush, book thief, leak (which shelf), inspection, book signing, power cut, special order (until ordered), VIP request, book club. Ticked off with how it went (shown in the Log); anything left ends with the day.

## Other fixes
- PC: every open, after closing or minimising, resets the catalogue's search and filters (minimise still reopens on the same tab).
- Members table: wider Access column and a gap before the hearts.
- Settings: the volume steps are small pixel bars (10 px wide, 6-24 px tall, 2 px borders) with small -/+ buttons.
- The book thief is always one of the men (`CustomerSpawner.ThiefLooks`), as the rumours describe him.
- **Coins:** every coin landing in the purse plays `coin add sound` (`GameAudio.coinAdd`); more coins for more money (3 to 24, faster when there are many), so a big payday jingles away.
- **Staff notes** (`HUD.StaffNote`): when a staff member tidies, mends, shelves or brings a scholar a book, a short line slides in on the left (a little above the middle) and fades after 2 s; up to 3 stack.
- **The cleaner unloads deliveries:** with no mess and no good returns to shelve, Sam takes delivered copies off the book cart and shelves them (`StaffWorker.TakeFromCart`, `DeliveryCart.TakeUnreserved`). Normal and express orders both; any title someone has **reserved** (special orders too) stays on the cart for you to hand over. A "Sam shelved ..." note shows as usual.

## The counter queue bends along the front wall (`CounterQueue.path`)
- Spots are laid 0.9 m apart along a path: straight out from the desk between the ropes, then (`QueueBend`, z 1.9, just past the rope ends) right along the front wall to `QueueEnd` (x 27.2), about 1.2 m short of column 3 so people can walk round the end of the line. That's 11 spots; a longer line carries on the same way. Move the two child markers under the queue object to reshape it (gizmos show the spots and the path).
- People in the line face the person ahead (only the front one faces the desk), and stand at a lower avoidance priority (80, others 50), so anyone walking past steers round them instead of getting stuck. The stuck watchdog still hops a queuer to their spot if needed.
- NavMesh rebaked after the plants by the front door were removed (all 46 test routes complete).
- **Show: Damaged** in the catalogue (and the Day Report shop) filters: titles with at least one damaged copy (`BookFilterBar.ShowMatches` case 5).
- **Stamina recovers while you talk / use a screen:** `PlayerLock` switches `PlayerMover` off, so it notes when (`OnDisable`) and adds the recovered breath back when it's switched on again; `Stamina01` shows it filling meanwhile.
- **Running again after running out:** once the meter is back to half (`PlayerMover.RunAgainAt` = 0.5), you can run on what's there; it used to wait for a full meter. While it's above zero and you're not exhausted, you can always run.

## Subject filter (catalogue + Day Report shop)
- Subject no longer needs a Type first. The list shows every group as a pickable orange row ("Science shelf: all", "History shelf: all", "Archive: all", "Newspapers: all"...), each with its detailed topics indented under it.
- Pick a group row to see the whole shelf/type at once, or a topic to narrow down. Picking a Type is still there as an extra filter and trims the Subject list to that type (and clears Subject).
- Values (`BookFilterBar.SubjectMatches`): `@Book:Science` = a book shelf, `@Archive` = a whole type, `Book|Astronomy` = one topic of one type. `SubjectLabel` turns them into button text.
- `PickList.Open(..., group:)` marks rows that are pickable but styled like headings (`.picklist-group` in `PC.uss`).

- Dialogue choices: while a choice is hovered (orange button) its <color> tags are stripped so the whole line reads white; they come back on leave (`DialogueUI.OnLineTyped`).

## Honest requests and hints, rare dealer
- Hints say what an item really is and where it lives (`BookRequest.Kind` / `KindHint`): "a Math book" only for the main shelves; Archive items say "an old record from the Archive, filed under Old maths"; Rare Books, newspapers, magazines, journals and papers say so too. Format hints name the shelf.
- A little fuzziness, never wrong: from Day 3, 20% of section hints are "Science... or was it History?" (the right one is always one of the two).
- Items with no real author (Unknown, Anonymous, Various, Whitmore Library: `BookRequest.HasAuthor`) are never asked for "by" that author; Archive/Rare topic requests say "from the Archive" / "one of your rare books". The exam rush only counts main-shelf books.
- Rare books: 8 on the shelves now; `BookDatabase.rareFinds` holds 5 more the library can buy, one at a time.
- VIP job (SpecialJobs): the scholar wants a rare book. Owned: have it in your hands at the desk at 3:00 PM. Not owned (45%, or always when no rare book is on a shelf, and only if no dealer order is pending): order it from the rare dealer in the PC Catalogue (top of the list, "VIP request" badge, Order only, no Express, any time that day, `GameManager.RarePrice` = a third of its value). It arrives on the book cart next morning (the cleaner leaves rare books on the cart). The promise is saved (`GameManager.vipPromise`) and the VIP comes back the next afternoon (1:30-3:30 PM).
- When the VIP is back: in your hands -> they repay the dealer's price (if any) + 40, +2 reputation, then take it to the Rare Books room and read it (`Customer.ReadNow`), checking it back in like any reader (with the Rare Books attendant on duty, the attendant checks them in at their seat, so they don't come back to the counter). Ordering, taking it off the cart and handing it over are always the player's job. Never ordered -> -3 reputation. Ordered/owned but not in your hands -> -1. Either way they read another rare book in the Rare Books room instead of leaving (they leave only if none is on a shelf).
- No second copy of a rare book can ever be bought: the dealer sells each once, the Catalogue/Day Report never order rare books, and `GameManager.OrderBook` refuses them.
- Catalogue rows show the sale price (what a copy sells for); order prices stay on the Buy/Express buttons.
- Renamed titles are mapped on load (`GameManager.RenamedTitles`) so old saves keep their copies. `BOOK_LIST.md` lists every title for checking.

## Staff cut
- At the end-of-day report each staff member keeps 25% of what their work earned (`StaffTalk.CutPercent`), never 0: at least 1 coin, and from 2 coins up they always hand at least 1 over. They say it: "My work brought in 60 coins. I've kept my 25% (15), so here's 45 coins for you." Only the handed-over part reaches your coins; the kept part is `DayStats.staffKept` (shown as a note in the Summary).

## Day Report money
- `DayStats` keeps a coin ledger: `coinsAtStart` (set when the day begins, before morning wages), `coinsIn` / `coinsOut` (every `AddCoins`), and `Spend(what, amount)` per kind. Spend through `GameManager.TrySpend(amount, "Book orders" / "Rare dealer" / "Upgrades" / "Staff hires" / "Staff training" / "Staff fixes" / "Repairs")`; wages are filed as "Staff wages". Anything spent without a label shows as "Other".
- Report page ends with: Coins at the start of the day, Money in, Money out, **Coins now** (+ wages due tomorrow). Summary tab: where the money came from, where it went, and start + in - out = coins now. Buying in the evening refreshes both.

## Text size
- UI text was raised across `GameUI.uss`, `PC.uss`, `Notebook.uss`, `MainMenu.uss` and `Interview.uss`: +4px for anything up to 24px, +2px for 25-30px (body text is now 21-28px). Dialogue (`.line` 34, `.choice` 27, `.speaker` 26) and big titles/pops (over 30px) were left as they were. The panels scale to 1920x1080, so on smaller windows everything shrinks a little.
- Wrapping after the size bump: book titles (Catalogue rows, Home "Popular today", evening shop rows) and member cells wrap onto a second line instead of being cut with "..."; badges drop below a long title. Catalogue rows grow with their text (`ListView` dynamic height, `.book-row` min-height 104). Short labels that must stay on one line ("on shelf") are `nowrap` in a wider stock column. Day Report tabs wrap if they ever do not fit; mode cards never shrink.
- `Tools > Check UI Text Overflow` (`Editor/UITextAudit.cs`) now also reports text cut short with "...". Open a screen in Play mode and run it. Known harmless flags: the floating "+1" reputation pop, the day bar's 9 AM / 5 PM end ticks and the dialogue speaker tag (all placed outside their box on purpose).
- Tag requests ("I need a proper textbook"): any item with that tag counts, and only those. The hint now names the tag ("It's tagged \"Textbook\" on the computer. I think it's a Science book."), and a wrong book gets a reason (`BookRequest.WrongLine`: not by X / not about X / not tagged X / wanted a newspaper / not the one I meant).
- The "?" over a visitor's head (and on the screen-edge arrow to a story visitor waiting somewhere) is a pixel-art icon now: `Assets/Art/UI/PC/T_IconAsk.png` (12x16, UI palette: orange fill, light highlight, dark outline; Point filter, uncompressed), shown 4x via `.customer-speech.ask` / `#staff-pointer-label.ask`. `Customer.SetSpeech` swaps the text for the icon whenever the line is "?".
