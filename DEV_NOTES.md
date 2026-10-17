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
  - **How to Play** (8 short pages: controls and stamina, counter, finding books, sold out, cards and readers, repairs and rooms, staff, end of day).
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
- **Hiring:** pays the first day's wage up front, so you can't hire with an empty purse.
- **Repairs:** always move a copy up one step (Worn → Good; Damaged → Good or Worn).
- **Reminders:**
  - The computer glows gold, the Shop tile says *★ Upgrade ready!*, and a banner appears once a day while an upgrade is affordable. All three stop once you open Upgrades.
  - The delivery cart glows blue, with a sound and a banner, when deliveries arrive.
- **Opening days:** a short reveal plays (`RoomReveal` on GameManager):
  - the camera glides to the still-dark room
  - the lights come up and the rope swings aside, with the *fascinated* sound and a banner
  - the camera glides back

  The clock is paused while it plays. The sign then bobs *NOW OPEN!* all day. A notice also stays pinned at the top of the screen until noon (`HUD` → `hud-pin`).
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
- **`PCShopView.cs`, `PCSettingsView.cs`, `PCMembersView.cs`**: PC screens, using templates in `Assets/Resources/`.
- **`PC.uss` / `GameUI.uss`**: styles for the catalogue filters, shop rows, mood face, VIP plate and banners.

---

## 3. ⚠ Changes inside Game Dev's PC files

Small, contained edits. Please check them when merging:

- **`PCOverlayTrigger.cs`**:
  - The Members, Shop and Settings buttons open the new screens (`ShowView`).
  - Click sounds are re-hooked after every view swap (`RehookBackdrop`).
  - **Catalogue search now matches title and author only.** Subject and Type are filter buttons (`WireFilters`). This is deliberate, so players have to read abstracts to solve vague requests.
- **`PCBooksTemplate.uxml`**: added the filter button row.
- **`PCOverlayTemplate.uxml`**: the "Upgrades" tile now reads **"Shop"** (the element name is still `upgrades-button`).

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
