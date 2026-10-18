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
  - **Settings** (volume, music, fullscreen; same values as the PC Settings screen).
  - **Credits**.
  - **Quit** (hidden on WebGL).
- Pressing play fades the book out and Cinemachine blends down to the follow camera. A glockenspiel sweep plays, and buttons pop softly on hover.
- **Esc during play** opens it again as a pause screen (Continue becomes **Resume**; Esc again resumes). Esc that closes the PC, a shelf or a dialogue doesn't also open it.
- **PC → Settings → Main menu** returns to the title screen (asks first; progress since the morning save is lost).
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
- The mode is saved, shown in the menu and the Day Report title, and can be changed in **Settings** (PC or menu).
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
   | Pickup | first thing in the morning | Comes for yesterday's reservation |

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
