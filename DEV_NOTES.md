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
  - The first chat with a seated reader (books, journals, magazines and newspapers alike): they gush about what they're reading and let slip what they love (`Customer.Gush`, `TopicLoves`: "I'm a sucker for the night sky."). Two times in three they then ask "What should I read next?" with 3 real titles (`NextReadOptions`): one that suits them (same topic or a close neighbour in `RelatedTopics`; tags aren't used, they're too general) and two from other topics; no newspapers offered, nothing from locked rooms, rare books only for VIPs. A good pick earns the chat tip (10% of value, 3-`MaxChatBonus`, "Good pick!" pop, member +0.5); a miss gets a "huh, not really my thing" reaction with no penalty (member +0.05); "Enjoy your read!" = +0.15. The other third just gush and go back to reading. If the library can't make a fair three, they just gush. Every catalogue item has a fair match with all rooms open. (Replaced the old "where would I find more like this?" shelf question, whose answer was given away by the item itself.)
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
- **`DayReportUI.cs`**: Day Report plus the Shop, Staff and Upgrades tabs. Its Shop tab **orders** books for the next morning through the same code as the PC (`PCCatalogueView.Buy` / `ForSale`): the rare dealer's book on top while a VIP wants it, "on order" and "on the cart" counts in each row, and the PC's confirmation line above the list. The five tabs fit on one row.
- **`NoticeBoard.cs`** + **`NoticeBoardUI.cs`**: clicking the board opens it up close: a cork board in a wooden frame (`UI_TravelBook_BookCover01a` tinted to the model's brown, brass corners; cork `rgb(139,90,43)`) with real papers (`Popup01a`) pinned on it, each slightly crooked (same angles all day), with coloured pins. Styles `.nb-*` in GameUI.uss; its own panel at sort 20. Papers (`NoticeBoard.TodaysPapers`): the big "TODAY · Day N" paper with the day's event notices (DayStory.Notice, one per twist) or a welcome; then only what applies: the Archive / Rare Books opening, pickups due today (urgent ones with their time), deliveries (on the cart, express on the way), popular today, the Dean's goal (until the ending); then 3-4 joke notices from the `notices` list (`jokesPerDay`, a fixed shuffle, new ones each day). Up to 9 papers. A work screen like the PC: time runs slower (`PlayerLock.IsWorkScreen`), not paused. Closes with ×, a click outside, or Esc. The board glows each morning (a pulsing gold outline, `HoverOutline.SetGlow`, separate from the hover outline) until you open it that day; not during the tutorial or the title menu.

### Repair desk (`Assets/Scripts/Repair/`)
- `RepairDesk.cs` and `RepairUI.cs`:
  - A queue with Repair and Repair All.
  - A 3-step minigame (glue, press, cover) that adds up to +20% Perfect chance.
  - Perfect Restoration returns a Good copy; Normal Repair returns a Worn one.
- Dropping off puts every worn or damaged book you're carrying on the desk.

### Core (`Assets/Scripts/Core/`)
- **`DayNightCycle.cs`** (new): blends a night volume (`VP_Night`) in from 16:00 to full at closing, and fades the window light shafts. `VP_Cozy` stays the daytime grade.
- **`GameAudio.cs`** (new): one place for gameplay sounds, assigned in the Inspector. It covers coins, customers, wrong book, tidy, cards, closing, the report, new day, unlock, panel open and close, and PC open and close. It also adds click sounds to the game UI and the PC.
  - **Book select sound:** `GameAudio.BookSelect()` plays the `bookSelect` clip when you pick a different book row in the shelf, return bin and delivery cart (`ShelfUI.Pickable`) or click a row in the PC catalogue.
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
  - The **birthday** comes from three wheels, Day / Month / Year (`PCMembersView.Wheel`, years 1930–2010). Scroll, drag, or click the row above or below to roll one step; each step slides the numbers and clicks (`GameAudio.Scroll`: a random one of the 10 clicks cut from "scroll sound of date" into `Audio/SFX/ScrollClicks`, never the same twice running, small pitch nudge; throttled). Day and month wrap round, the year stops at its ends; an empty wheel starts at 1 / Jan / 1980. The day wheel only rolls through days that exist, and rolling the month or year past a shorter month never wipes the day: it shows the last day that month has (31 → 30 in April) and goes back to yours when the month allows (`wantDay`). The result is written like the profiles, for example "04 Feb 1958".
  - Access is still three buttons.
  - Issue only unlocks with a valid name (2+ letters), a full date and an access level. A wrong name or date is still rejected by the visitor (`ReceiveCard`), and the card shakes.
  - **The card under the monitor** (`BuildPass` / `FillPass`, `.libpass` in `PC.uss`): a library card in the SM_LibraryPass colours peeks out from the monitor's left edge while someone is waiting, and fills in live (name, born, access, initial, card number). Hidden when the PC is maximised. It belongs to the Members tab and goes away with it.
  - **Stamp:** a right card gets an ISSUED stamp (with the day) that drops onto it, then `GameAudio.cardStamp` thunks, the `cardIssued` chime follows 0.35 s later, and the PC logs off 1.2 s after the stamp. `ReceiveCard(..., chime: false)` happens at the click, so closing the PC mid-stamp still issues the card.
- **Typing sound (anywhere):** `GameAudio.HookTyping(root)` (also run by `HookClicks`, and on the staff name box) plays `GameAudio.typing` for each printable character, Backspace and Delete typed into any `TextField`, but only when the text would change (no sound for Backspace on an empty box, or typing into a full one). Each press gets a random pitch (0.86–1.18, never within 0.05 of the last) and loudness (75–100%), the spacebar is a touch lower (0.8–0.84). A static pool of 8 sources so pitches don't bend each other; the intro (InterviewScene, no GameAudio) types with `StaffInterview.typingSound` via `GameAudio.UseIntroKey`. Volumes: typing 0.5, wheel clicks `scrollVolume` 0.3.
- **Coin deduct:** `GameAudio.coinSpend` (clinking coins, 0.35) plays whenever the coin total goes down (`CoinsChanged` with a negative delta). It replaces the purchase sound (`buy`, the report's `buySound`) when both would play together (`GameAudio.JustSpent`, 0.25 s).
- **Volume settings** (Main Menu and PC Settings): Volume (master, `AudioListener`), Music, Effects and UI sounds, 0–10 each, saved in PlayerPrefs (`set_sfx`, `set_ui`). `GameAudio.Play` scales UI clips (clicks, panel / PC open and close) by UI and everything else by Effects; typing and wheel clicks are UI; loops use `GameSettings.SetBase`. Every other AudioSource in the scene is swept every second (`GameSettings.ScaleAllSources`) and scaled by Effects, or by UI if its script marked it (`MarkUi`: menu, dialogue blips, shelf, repair, report, intro). Music sources are skipped (`Skip`). The Recommended badge lost its ★ (the web font has no star).
- Search boxes on the PC (catalogue and shop) take 40 characters at most. The card form's Access buttons click when picked (played in the button, since the rebuild removes it before the click hook sees it).
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
  - The Notes and Reserved tabs stand upright (the normal kit button turned -90° inside a `.notebook-tab-holder`; `NotebookHUD.FitTab` sizes the holder to the turned button on every geometry change, so longer text like "Reserved (3)" still fits) so they never reach over the computer.
  - `GameManager.Reserved` fires on each new reservation. The first one in a session opens the panel by itself. Later ones make the tab gold and pulsing (`.notebook-tab.new`) until you open it.
- **PC catalogue: Add to note** writes `Noted: "Title" (Shelf)`.
  - With a book request open, it goes on the newest request. Each new pick adds its own line, up to 3 untested answers (a 4th drops the oldest untested one). A book already noted and untested is greyed out ("Already in your notebook"). It's only a note: any matching book still works.
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
- **Choosing your look** (the left page of the same name spread, which is up before the intro fades in, so the room and the librarian in the chair are never seen without it): a still preview of the librarian, ◀ ▶ arrows, your name under it as you type ("Your name" until then) and "1 of 2". The name box still starts empty and must be filled.
  - `Core/PlayerLooks.cs` + `Resources/PlayerLooks.asset`: the list of bodies (`looks`: name + a character prefab on the shared rig; now the librarian and `PF_CH_Main_Librarian_Female`) and `previewRig` (the female prefab, a plain rig with no player scripts). Saved as PlayerPrefs `PlayerLook` (default 0, so old saves keep the original librarian).
  - `PlayerLooks.Apply` only swaps the skinned mesh (and its bounds) on the character: every body shares the librarian's 36-bone skeleton, so animations, hands, carrying and collisions don't change. It refuses a mesh whose bone count differs. Applied to the Player on every scene load, and to the intro's `StaffInterview.librarian` (live while you browse, and at Start for later visits).
  - `UI/LookPreview.cs`: the preview copy stands at y −200 on layer 31, filmed by its own camera into a 512×640 RenderTexture with a see-through background. It stands still facing straight at you, seen a little from above (camera at 1.45 m looking down to 0.66 m), and waves on each switch. Light probes are off on it (scene ambient light), so the face is lit evenly (the room's probes made a dark "beard" on the lower face). Destroyed when you press "That's me".
  - To add a body: export it on the shared rig, make a prefab like `PF_CH_Main_Librarian_Female`, and add it to `PlayerLooks.asset`. No dialogue calls the librarian he or she (checked), so any body works with every line.
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
  - **ASAP** (`ReserveAndWait`): they sit in the library (`State.WaitingForBook`). They get up and queue as soon as a good copy is on a shelf, on the book cart or in your hands (the "ordered" flag no longer matters; before, a copy taken off the cart first, or one delivered before they reserved, left them sitting for ever).
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
- **Reservations are counted per person** (`GameManager.ReservationCovered`): copies on order + on shelves + on the cart, handed out in reservation order, so two people after one title need two copies. Used by the Reserved list (everyone stays listed until they collect: covered = grey, missing = red), `PCHomeView.NeedsOrder` (Home, PC tab count, catalogue) and the Day Report's unordered count.
- **Web scrolling** (`UI/WebScroll.cs`, made at runtime): browsers send ~100 per wheel notch and UI Toolkit multiplies by 18, so lists jumped top to bottom. On WebGL a trickle-down wheel handler on every UI panel moves the list under the mouse by |delta|×0.5, clamped 18–60 px, and stops the default. The library-card date wheels (`.wheel`) are left alone. `WebScroll.ForceOn` tries it in the editor.
- **Click guard** (`Interaction/ClickGuard.cs`): for 0.25 s after a panel opens (`PlayerLock.Lock`), after choices appear, or after the interview hire and end pages show, button presses are swallowed, so one click can't also press a button that just appeared under the cursor. It's attached to the game UI, the PC overlay and the interview UI.
- **Interview:** pay is discussed automatically after the questions ("Before you go, let's talk about pay."); it's no longer in the question menu.

## Popular books, lean start and request icons
- Popular today: `GameManager.PopularToday` holds 4 books for sale (`PopularPerDay`), picked at random each day (seeded by the day, so a reload keeps the same set). `BookRequest.Create` asks for them 6x as often, including when they're sold out. They glow gold with a "Popular" tag in the catalogue, the Shop, the end-of-day shop and on shelves (`PopularGlow`). Readers only ask for books that are in stock. On Day 1, in-stock books count double.
- New game only (`!HasSave`): `GameManager.LeanStartingStock` cuts each book for sale to 1 copy, and to 0 for Best Sellers. Archive and Rare Books keep their stock.
- Request icons: `Customer.UpdateRequestIcon` puts an icon to the left of the prompt at the desk. The classes (`req-card/buy/read/return/new/rare/collect`) are in `GameUI.uss` and `PC.uss`, and the PC tiles use the same ones. The kit has no book icon, so `RequestIcon` draws the "read" book in code.
- Text overflow: in Play mode, open a screen and run Tools > Check UI Text Overflow (`UITextAudit`). It lists any text that doesn't fit its box.
- Catalogue fix: a new search clears the ListView's old selection index, and each row has its own click handler, so clicking a single result always fills the details panel.
- Drop-downs: `PickList.Open(button, options, current, pick, pcStyle)` shows a list under a button (it stays inside the panel around the button and closes on a click outside, or when its screen closes). It's used by the catalogue filters (Subject / Type / Show) and the shop filters (`BookFilterBar`). (The Members birthday used it too; it now uses wheels.) "Show: Reserved" lists books someone has reserved.
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
- **Goal:** reach `GameManager.ReputationGoal` (Cozy 60 / Standard 100 / Busy 125) by the end of Day 5. The Dean sets it at the end of the new-game intro. It shows on the HUD bar (star, "37/60", gold once reached, with a +/- pop), in the morning banner and in the Day Report (how many to go and how many days left). After the ending the HUD shows just the reputation.
- **Ending:** `GoalTier` (2 reached, 1 close at 60%+, 0 missed) changes the Dean's farewell: the board signs the library over, wants one more season, or is worried. The end page shows "Reputation X of Y" and the result. The game carries on either way.
- **Consequences:** wrong book -1 reputation. Walked out -3, never ordered -3, said "we can't get that" when you could -2, waited for nothing -2. A member let down in those ways stays away the next day (`Member.awayUntil`, saved with the members). Pickups still come.
- **Patience:** `PatienceDayFactor` cuts patience to 85% on Day 3 and 75% from Day 4 (all modes; Cozy visitors still never walk out).
- **Second week (Days 6-10):** in the farewell the Dean passes on the board's next number (`GameManager.SecondGoal`: Cozy 100 / Standard 150 / Busy 200, due at the end of `SecondEndingDay` = 10; `StaffInterview.EndingNextGoal`). `GameManager.SecondWeek` makes `GoalActive`, `ReputationGoal` and `GoalDay` follow it, so the HUD star, morning banner, Day Report, PC home and notice board all show it ("by Day 10", "Last day!", "He'll write to you in the morning"). The morning after Day 10, `DayStory.SeasonLetter` shows a letter from the Dean (reached / close / short, `GameManager.TierFor`), sets `secondEndingSeen` (saved) and saves; from then on there's no goal.
- **Playtime** (`GameManager.playSeconds`): real seconds while a game is under way (`MainMenuUI.Playing`: not the title screen or Esc pause, not with the window unfocused). Saved with the game and every 30 s under the `PlayTime` key (also on Main Menu and on quit; loading takes the larger of the two, so quitting mid-day loses little); `DeleteSave` clears both. Shown as "Played for 38 min" / "1 h 24 m" (`FormatPlaytime`) under the title on the title screen and the pause screen, in place of the old "a cozy library tale" subtitle (nothing before your first game); on the Day 5 End page; and spoken by the Dean in the Day 11 letter ("about an hour and a quarter", `DayStory.Spoken`).
- **Pause menu (Esc):** Resume, New Game, How to Play, Settings, Credits, **Main Menu** (asks "Sure? Click again" first, then `GameManager.ReturnToMenu`; hidden on the real title screen), Quit (not on WebGL).
- **End page:** besides "Keep running the library" there's "Back to main menu" (`StaffInterview.EndingToMenu`). The new day is started and saved first (so the ending isn't replayed), then `GameManager.ReturnToMenu()`; Continue on the title screen picks up from there.
- **Visitor tags** sit at 1.4 m above the visitor (was 1.75) so they don't hide the counter computer. Card applicants' name and birthday are highlighted in their line and in the notebook.

## The story and daily twists (`Day/DayStory.cs`)
- Runs every day. The week's own beats are Days 1 to 5; twists carry on after (below). `GameManager` adds the component at runtime (no scene change).
- **After Day 5:** Days 6-10 get all six twists again, reshuffled (one a day, two on Day 10; `DayStory.WeekTwo`, a seed derived from `storySeed`). From Day 11 each day rolls 0 (30%), 1 (50%) or 2 (20%) safe twists, never one from yesterday and never three quiet days running (`DayStory.EndlessDay`, worked out day by day from Day 11 so it's the same on every reload). The first week's schedule is unchanged.
- **Twists on Days 2 to 5** (`DayStory.TwistsFor`): Day 1 has none. All six (exam rush, book thief, leaky roof, inspection, book signing, power cut) happen once per game in a random order (`GameManager.storySeed`, saved), one each on Days 2 and 3 and two each on Days 4 and 5 (`DayStory.Schedule`, reshuffled until valid). Only `SafePairs` share a day: thief + leak (both at 11:00), exam rush or signing + inspection (crowds during it) and exam rush or inspection + power cut are never paired, and the thief is never on Day 4 (the week's story uses its writer and Bernard). With a power cut too, Walter's leak chat moves to 11:30 (he's the 1:00 power-cut visitor); on an exam rush day the leak avoids shelves with exam-subject books. The notice board, morning banner and Day Report show every twist of the day. Each has 4-5 beats (someone at the desk, a banner, a pointer, a popup) and a real effect:
  - **Exam rush**: more students, mostly readers, after one subject, a bit less patient. From 10:00 to 1:30, 3 more visitors are allowed in at once (`DayStory.ExtraCap`), and 70% of non-VIP requests are for an in-stock main-shelf book in the exam section, asked for by section (`DayStory.ExamSubject`, `BookRequest.Create`).
  - **Thief**: a "Stranger in a long coat" browses, pockets a book and walks slowly to the door. Click them (`Customer.Caught`) to get it back; if they get out, the copy is lost.
  - **Leaky roof**: a shelf gets a "Leak!" pointer; until you click it (`ShelfInteractable` → `DayStory.FixLeak`) copies on it turn Damaged.
  - **Inspection**: Mr. Pryce looks around 1:00-2:30; mess on the floor and walk-outs decide the verdict.
  - **Book signing**: one of the library's own authors; their two books are popular all day and asked for more during the signing; sales decide the thank-you.
  - **Power cut**: 12:00-2:00 the PC won't open (`PCOverlayTrigger`), the library goes dark (`DayNightCycle` uses `DayStory.Darkness`), visitors are more patient.
- **The week's own beats** (the same every game): Walter's first card (Day 1), the car-park rumour (Day 1), the Archive historian and the lost glasses (Day 2), the Board's letter and the Archive cat (Day 3), the writer and the man with the clicker (Day 4), the Dean's note and the regulars' card (Day 5).
- **Story visitors** are normal `Customer`s with `story` set (`CustomerSpawner.SpawnStory`): they queue, wait without patience, and play a `DayStory.Visit` scene when clicked. Afterwards they can ask for a book like anyone else (`ContinueWithRequest`), sign up (`ContinueAsSignUp`), look round the shelves and come back for a second scene (`lingerUntil` + `next`), or leave. If their character is busy (working here, already inside) an alternative steps in.
- **Letting the player know**: the morning banner names the day's twist, the notice board pins a notice for it first, banners and pointers mark each moment, and the Day Report has a row for it.
- **Small talk**: the first time you talk to a visitor, some mention their day first (`DayStory.Opener`), often about the twist. Every day: the 28 everyday lines are shared out across five days (5 or 6 each, no repeats in a day), and each new week starts one share along so Day 6 doesn't repeat Day 1.
- **Pickups** no longer all come at opening: each gets its own time across the day (`CustomerSpawner.QueuePickups`).
- To test a twist without playing to it: in Play mode set `GameManager.day`, pick a `storySeed` whose `DayStory.TwistsFor` is the twist you want, call `SetUpDay` and move `GameManager.Hour` (nothing is saved until the next day starts). Easier: Dev Tools → Start a twist today. Note the Day 11 letter saves when it plays.

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
- **Book signing table** (`SigningTable` on the `BookSigning` object: chair + desk, both NavMesh not-walkable): after paying the table fee at 2:00 the author walks to the chair and sits until 3:30 (`DayStory.Visit.linger` → `Customer.SitAtTable`, state `AtTable`), then comes back to the front desk for the farewell. Anyone who buys one of the author's books during the signing (`DayStory.SigningTitle`, from `StampOut` and `Sell`) joins the signing line instead of leaving (`Customer.GoToSigning`, state `ToSigning`): at most 3 (`maxLine`), running out into the hall along `lineDirection` (+z), clear of the front desk's line. At the front: fan line, author's reply, the fan's own sign-off, off home (`SigningTable.NextChat`: 14 matched chats, shuffled, no repeats until all are used). VIP scholars never join the signing line. Own-copy fans: during the signing about a third of ordinary arrivals (buyers, browsers, readers; not VIPs, pickups, seekers or returners) bring their own copy (`DayStory.SigningBook`) and go straight to the signing line from the door (`Customer.Start`), no prop or animation; if the line's full or the author isn't seated they come in as normal. Each fan who gets their book signed (bought here or their own copy) earns the library a bonus from the author (`SigningTable.fanBonus`, 5 coins: "Signed! +5 coins" pop, logged as "Signing bonus", counted in today's Other income). Edge cases: line full or author not seated yet = they just go home; author gone at 3:30 = fans still waiting say so and leave; closing time = everyone stands up and leaves; a fan who leaves or is destroyed is taken out of the line; a stuck fan hops to their spot like the counter line.
- **Rare Books and Archive bookcases** are `SM_RareBookcase_Tall_2m_Filled` (RARE-I, RARE-II) and `SM_ArchiveBookcase_Tall_2m_Filled` (ARC-A, B, C); ARC-D is still the 1 m bookcase (no 1 m version). Same shelf ids/settings and signs, backs against the wall, native size (the Rare ones are narrower than the old stretched ones). Lit by light probes (Contribute GI off) so no lightmap rebake was needed. NavMesh rebaked.
- **Staff show the books they carry** (`StaffWorker.LateUpdate`): one `SM_Book_Thick` (the librarian's carry prop, taken from `PlayerCarry.bookPropPrefab`) per book in their arms (`carried` + `extra`), side by side between the hands like the librarian's. More upgrades (`StaffCarry`) = more books shown.
- **Seated readers hold an open book** (`OpenBook2`, `CustomerSpawner.openBookPrefab`): made when they sit (`Customer.SitDown`), placed each frame between their hands, tipped `openBookTilt` (40°) towards the face plus `openBookOffset`, hidden when they stand up or stop reading. Staff and ghosts that use SitRead don't get one.
- **Dev Tools** (`Tools > Dev Tools`, `Assets/Scripts/Editor/DevTools.cs`, Play mode only, editor only): jump the clock, start any twist today (its moments follow the clock), leak / thief / visitor now, power off / on, VIP now (book on shelf / from the rare dealer; Rare Books must be open) and VIP comes back now (`SpecialJobs.DevVipNow`/`DevVipBackNow`), +100 coins, ±5 reputation, play each event sound. The game-side hooks (`GameManager.DevSetHour`, `CustomerSpawner.DevSpawnNow`, `DayStory.DevStartTwist`/`DevLeakNow`/`DevPower`/`DevThiefNow`) are inside `#if UNITY_EDITOR`, so none of it reaches a build. It never starts the next day, so it never saves by itself.
- **Listener on the librarian:** `GameAudio` turns off the camera's `AudioListener` (the camera is ~30 m away) and hears from an "Ears" object at the librarian's head, turned the way the camera faces. That's what makes the 3D sounds audible: the fireplace (`FX_Fire`, 4–16 m) and the staff typing.
- **Event sounds** (`GameAudio` "Events", each a `Sfx` = clip + its own volume, set a little under the music: BGM_Day measures ~0.012 RMS in game): power off / power on and a candle-crackle loop (`fire-place`) during the power cut; a rain loop on the leaky-roof day (`rain loop.wav`: 30 s mono cut from `rain sound (1)` with a 2 s crossfade, Vorbis compressed in memory) until Gus clears the gutter (stopped at closing and on a new day); soaked book; inspection tannoy + `eventDone` at the verdict; thief alert / thief caught; signing bell + `eventDone` at the farewell; exam-rush bell + `eventDone` when it ends; page turn when a visitor sits down to read; wood creak when you lock the front doors; a pickup visitor arriving; a tutorial step ticked. Loops go through `GameAudio.Loop(pick, on)`.
- **Staff help** has its own sound (`GameAudio.staffHelp` = `Audio/SFX/staff help.mp3`), rung 3 times 0.7 s apart (`StaffHelp.Ring`), not the impatient counter bell.

## Staff mood, problems and special jobs (`StaffHelp`, `StaffTalk`, `GameManager`, `Day/SpecialJobs.cs`)
- **Problems**: 8 per role, no repeats in the same day (`StaffHelp.usedToday`).
- **Mood** (`GameManager.staffMood`, 0-5, saved, 3 on hiring): speed x(0.7 + 0.1 x mood) via `StaffWorkFactor` (agent speed, cleaner timer, repair desk). Paid fix +0.5 and they come in **rested** tomorrow (x1.25 that day, `staffRestedDay`); free fix -0.3 (and slower for the rest of today, as before); coming over within 20 s +0.2; each 30 s reminder while you leave them waiting -0.15; first chat of the day +0.15; nobody talked to them all day -0.3 at the report. A banner says when their mood word changes (happy / content / grumpy / fed up). Shown in the Staff rows and in their end-of-day report.
- **Special jobs** (once anyone is hired, from Day 2; one a day, offered at the desk around 10-11:45, can be turned down):
  - Special order: a regular wants a title by tomorrow; becomes a "Requested" reservation with `Reservation.bonus` (15-40) paid on collection (+1 reputation).
  - VIP request: a professor wants a rare book in your hands at 3:00 PM (+40 coins, +2 rep; not ready: -1 rep). Saying yes adds a librarian thought spelling out "take it and keep holding it", with the key words in the notebook's warning red (`SpecialJobs.Red`, #AA3C28), and the task, banners and Reserved panel ("TAKE IT FROM THE … SHELF") say the same; picking it up shows "keep holding it"; half an hour before they're back, a reminder banner if you aren't holding it (`SpecialJobs.GuideVip`). No pointer.
  - VIP texts say "the Rare Books section", never a shelf name; the Reserved panel says "TAKE IT FROM RARE BOOKS". The two VIP asks open differently (the in-hands one is chasing a footnote; the dealer one has wanted the book for twenty years).
  - Special-job VIPs are flagged VIP (`DayStory.Visit.vip` → `Customer.vip`): gold name card, "(VIP)" after the name, and a gold dialogue box and name tab (`DialogueAsset.vip`, `.dialogue-box.vip` / `.speaker.vip` in GameUI.uss), the same as any VIP.
  - Book club: 3 copies of a main-hall book on the shelves at 4:00 PM; they buy them all (+15 bonus, +2 rep; too few: -1 rep).
  - **Second week (one a day, staff or not; a day that can't run its job falls back to the usual pick; from Day 10 all seven are in the random pick):**
    - Day 6 **Collector** (VIP): a rare book comes off the shelf and goes onto the repair desk as damaged. Mend it and have it in your hands at 3:00 PM: perfect (Good) 60 coins +2 rep, mended (Worn) 30 +1 rep, not ready -2 rep. It stays in the library (the VIP reads it, `ReadNow`). Uses the same hold banners as the VIP request.
    - Day 7 **Reading list**: a teacher names 3 subjects (at least one with nothing on the shelves, so it needs an order). One book of each on the shelves at 4:00 PM: they buy one of each (+20 bonus, +2 rep); any missing: -1 rep.
    - Day 8 **Rare loan** (VIP): the County Museum borrows a rare book for the night, +100 coins now. It comes back on the book cart tomorrow morning (`GameManager.ReturnTomorrow`, saved like an order); 30% of the time scuffed (Worn) and 10% torn (Damaged), then it goes to the returns bin instead. While it's away it's off the shelf, so VIP jobs don't pick it.
    - Day 9 **Restoration**: a visitor's own damaged book goes on the repair desk. Once repaired it's set aside for them (`RepairDesk.TakeDone(book, condition)`), so it never joins the library; at 4:30 PM they pay 35 (like new) or 17 (mended), or -1 rep if not repaired. Anything of theirs still on the desk at closing is withdrawn (`RepairDesk.Withdraw`), before the next save.
    - `RepairDesk.DropOff` now returns the job so the jobs can follow it (`Finished`). Reserved panel lines for all four. Dev Tools: "Second-week jobs" (offer now, deadline now).
  - Accepted VIP / book club jobs show in the Reserved panel with progress.

## Feedback polish
- **Patience**: bigger mood face with a draining bar beside it (`.mood-bar`), green / amber / red; the face throbs when they're about to give up. Angry exits (`Customer.StormOff`): a stomp on the spot with a shaking red face and red "Hmph!", the impatient bell, then they march out at 1.8x speed.
- **Skill rewards** (`HUD.SkillPop`, gold text rising from the spot): a good recommendation in a reader chat (bonus paid on the spot, +1 rep), perfect repair (+5 coins; +1 rep only for a repair you started yourself on the repair screen, `RepairDesk.Job.byHand`, not Repair all or the archivist), special order collected, VIP request done.
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
- **Home (`PCHomeView`):** **Needs you** comes first: a slim green "All clear" line with a tick when nothing waits, otherwise the warm `needs-hot` card (orange title, a count, one row per job with a pixel icon: clock = needed today, cart = order, scroll = card, hammer = upgrade, broom = staff) (max 3, only things you can fix on the PC: unordered reservations → *Order* opens a fresh Catalogue so you search for it yourself, a card waiting → Members, an affordable upgrade or staff action → Shop); the day bar (9:00 to closing, a "now" marker, and dots for urgent pickups, today's pickups (`CustomerSpawner.PickupTimes`), Express deliveries (`GameManager.ArrivingToday`) and closing; hover a dot for a tooltip, things in the same 10 minutes share one dot); reputation toward the goal; today's popular books; and the Today / Collection / Library ledgers. Below it four tiles with icons (visitors served, coins today, copies on shelf, sold out); then TODAY (clock icon) and REPUTATION (star) on the left, POPULAR TODAY (books) on the right. Icons are the `.si si-*` classes in PC.uss. The old Today / Collection / Library ledgers and the titles/members tiles were removed (they repeated the tiles). It rebuilds only when its numbers change.
- **Catalogue (`PCCatalogueView`, moved out of `PCOverlayTrigger`):** lists every title, including new titles you don't own yet (at the bottom, "New title"). Filters: **Type** (Book, Newspaper, Magazine, Journal, Research paper, Archive, Rare Books) → **Subject** (greyed out until a type is picked; books are grouped by shelf) → **Show** (adds "Not in library"). Rows: title + badges, "author · subject", then shelf icon, coin + order price, and the damaged icon if any; stock on the right. Epic and Legendary rows/cards get an indigo / plum edge and badge. The card has **Add to note**, **Order** and **Express** (greyed out after closing); the old Shop "Books" tab and "Buy books" are gone.
- **Shop (`PCShopView`):** Upgrades (cards with pixel icons and level pips, one grid in `PCShopView.UpgradeOrder`: your own first (shoes, bag, stamina), then comfy queue, town flyers, lamps, nooks, repair tools, restoration kit; ones not unlocked yet move to the end) and Staff (one row per role, icons: broom, books, key). Price buttons read "Buy - (coin) 80".
- **Members:** a table (name, born, access, kit hearts, visits, spent) and the library card form; the birthday is three wheels, Issue card is full width at the bottom.
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
- The rare dealer request is guaranteed once, staff or not (`SpecialJobs.SetUpDay`, `GameManager.rareDealerSeen`, saved): a 50% chance on Day 3, forced on Day 4 if it hasn't come yet. It counts as seen once the VIP has asked (yes or no). After that it only comes through the normal staff job pick.
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
- A general +4px font bump was tried and reverted (it broke the look): every font size in `GameUI.uss`, `PC.uss`, `Notebook.uss`, `MainMenu.uss` and `Interview.uss` is back to its original value. The dialogue box (line 34px, choices 27px, speaker 26px) was never changed and stays big.
- Wrapping (kept after the revert): book titles (Catalogue rows, Home "Popular today", evening shop rows) and member cells wrap onto a second line instead of being cut with "..."; badges drop below a long title. Catalogue rows grow with their text (`ListView` dynamic height, `.book-row` min-height 104). Short labels that must stay on one line ("on shelf") are `nowrap` in a wider stock column. Day Report tabs wrap if they ever do not fit; mode cards never shrink.
- `Tools > Check UI Text Overflow` (`Editor/UITextAudit.cs`) now also reports text cut short with "...". Open a screen in Play mode and run it. Known harmless flags: the floating "+1" reputation pop, the day bar's 9 AM / 5 PM end ticks and the dialogue speaker tag (all placed outside their box on purpose).
- Tag requests ("I need a proper textbook"): any item with that tag counts, and only those. The hint now names the tag ("It's tagged \"Textbook\" on the computer. I think it's a Science book."), and a wrong book gets a reason (`BookRequest.WrongLine`: not by X / not about X / not tagged X / wanted a newspaper / not the one I meant).
- The "?" over a visitor's head (and on the screen-edge arrow to a story visitor waiting somewhere) is a pixel-art icon now: `Assets/Art/UI/PC/T_IconAsk.png` (12x16, UI palette: orange fill, light highlight, dark outline; Point filter, uncompressed), shown 4x via `.customer-speech.ask` / `#staff-pointer-label.ask`. `Customer.SetSpeech` swaps the text for the icon whenever the line is "?".
- The pixel "?" is only for story visitors waiting around the library. Ordinary visitors at the desk show just their request icon until you talk to them (`Customer.WaitingText`). The screen-edge "?" arrow is edge-only (`WorldPrompts.Pin(..., edgeOnly)`), so on screen there is one "?", not two.
- A roaming story visitor whose chat turns into a book request or a library card (`ContinueWithRequest` / `ContinueAsSignUp`) says "I'll see you at the desk!", gets up, queues, and asks at the desk (`Customer.ComeToDesk`, via `presetRequest`). Nobody asks for a book or a card from a chair any more.
- Book signing: the author pays the library 30 coins for her table when she arrives at 2:00 PM (`DayStory.SigningFee`), shown in her line, a pop and the Log.

## Book pages overhaul (Catalogue, shelf, hands)
- New 12x12 pixel icons in `Art/UI/PC`: `T_IconBook` / `T_IconBookWorn` / `T_IconBookDamaged`, `T_IconMagnifier`, `T_IconClock`, `T_IconScroll` (Point filter, uncompressed). Reused: `T_IconBooks` (stock), `T_IconShelf`, `T_IconDamaged`, the kit coin, `T_IconKey`.
- Book cart stock: copies waiting on the delivery cart aren't counted as on the shelf, so the Catalogue says where they are. Row: "0 / on shelf" with "[cart] 1 on cart" under it; details card Stock: "0 on shelf · [cart] 1 on book cart" (`PCCatalogueView.OnCart`, new 12x10 icon `T_IconCart`, `.icon-cart`).
- Catalogue details (`PCBooksTemplate.uxml`, `PC.uss` `.fact*`, `.about-box`): one row per fact, icon -> name -> value (Shelf, Stock, Condition (magnifier, or the damaged icon), Sells for, and a Status row only when reserved / on order / VIP request / popular), striped; the abstract sits in an "About this book" box.
- Shelf rows (`ShelfUI.BookRow`): title (+ Popular, + "Reserved for X" tag), author + subject, then one dot per copy (green good, amber worn, red damaged), the price (or a key + "read here"), and "+N coming" with a clock. Left edge shows the tier (Epic / Legendary). When only damaged copies are left the main button says "Damaged".
- Right page: your hands as slots (one per book you can carry, pixel book coloured by condition, empty outline), then the "back cover" of the book you clicked (abstract + type, subject, tags, tier, read-here-only chips).
- HUD carry slot shows the same pixel book for the top book in your hands.

## Slowed time on work screens
- With the computer, a shelf / the delivery cart, or the repair desk open, game time runs slower instead of full speed: the clock, visitor patience, spawns, staff timers and story timers all use `GameManager.Dt` / `TimeScale`. Speed = `ScreenTimeScale`: Cozy 0.3, Standard 0.4, Busy 1 (no slow-down). Dialogue, menus and the Day Report still pause (`PlayerLock.FreezesTime` wins over `SlowsTime`). The HUD clock shows "(slowed)". Minimising/closing the PC returns to full speed. Walking speed is unchanged (only timers slow). Repairs you watch at the desk still run at real speed, as before.

## Notification feed
- `HUD.ShowBanner(text, seconds)` no longer shows one big banner: it feeds the top-left notification list. Each line is its own card (pixel icon + text, newest on top, up to 6); lines over ~60 letters split at a sentence break. Icons are picked from the text (`HUD.IconFor`: clock, key, coin, star, books, broom, scroll). Messages of 7 s or more are urgent: orange edge, stay 25 s, click to dismiss; others fade after 14 s. Staff notes (`HUD.StaffNote`) join the feed for 4 s with their role icon. Every banner card is also kept in the Log as a "Notice" (`RequestLog.News`, `Kind.News`); staff notes are not (too frequent). Clicks on a card do not reach the library (`HUD.PointerOverFeed`). Cards are queued and appear one every 3 s, and wait while a tutorial card or hint is on screen (`Tutorial.CardShowing`); they go in the Log at once.
- Tips (`Tutorial.Show` hints) now appear in the feed (`HUD.Tip`): lamp icon, gold edge, "TIP · click to close", and they stay until clicked (never pushed out by other cards). The guided Day 1 tutorial keeps its own card.

## Queue, leaving, second requests, return bin
- Queue: someone already standing in the line overtakes the person ahead if that person is still more than 3 m from their spot (`CounterQueue.MoveUp`), so nobody walking in from afar holds up the desk.
- Leaving from the desk area: walk to `CounterQueue.LeaveVia` (front spot + `leaveViaOffset`, left of the desk) first, then the exit, so they do not walk back down the L-shaped line. Nobody leaves with a "?" over their head.
- Talking to a story visitor waiting with a "?": +1 reputation (once).
- Second requests (a new member right after their card, a VIP after a Rare card): they stay at the desk and ask after their thank-you (2.5 s) with another bell ring (`Customer.AskAgainAtDesk`), instead of going round the line again. Clicks during that pause are ignored.
- Return bin: clicking it opens the book screen (like the cart): one row per returned copy, its condition and where it goes (its shelf, or the repair desk), a Take button per copy, and Put back for copies you took out (`ReturnBin.Take/PutBack/CameFromBin`). Refreshes when staff or visitors change the bin while it is open.
- Goals: Cozy 60, Standard 100, Busy 125 (about what a strong player can earn by Day 5 in Busy).
- Ending: the Dean no longer says "it was eleven, nine of them my bridge club"; he says there were far fewer when he left (or a gentler line for 3 or fewer members).

## Tutorial and small fixes
- Tutorial practice: coins and reputation are snapshot when the guided steps begin and restored when they end (done or skipped), and today's numbers restart from there (`GameManager.ResetAfterTutorial`). Patience doesn't drain while the tutorial holds the clock. Step 2 says Catalogue; step 7 says to order on the computer.
- Patience bar: 5 pixel blocks (green, amber, red) instead of a smooth bar.
- Tips highlight key words (computer, Catalogue, shelf, order, Express, return bin, repair desk...; `HUD.TipWords`).
- Leaving: back to walking straight to the exit (the left-of-desk detour was removed); the stuck watchdog is unchanged.
- "+1 reputation" for a story chat starts just above the visitor's head.

## Tutorial overhaul
- `UI/Tutorial.cs`: a tour, then three visitors, then a look at the Shop (`Step.Upgrades`: open the computer, it opens on Shop → Upgrades, press Next; `Step.Staff`: the Staff tab is framed, open it, press Got it; Shop buy, interview and training buttons are disabled while `Tutorial.HoldsClock`), then "You're ready!" and a fade into Day 1. Runs once, on a new game's Day 1 morning. There's no way to replay it (the title menu's Tutorial button and the PC Settings Replay row were removed; `Tutorial.RequestReplay` is unused).
- Tour (7 stops: front desk, computer, a shelf, book cart, return bin, your notes, front ropes): a tour camera glides to each, the animated `UI_TravelBook_SlotCursor01a` frame (4 frames, `.tut-spot.f1-f4`) marks it with a short name label, and the card explains it with highlighted key words. Next / Enter, Skip tutorial.
- Visitors (only these come in; clock held, patience frozen): book by author (Catalogue, shelf, hand over); library card (Members); a book the library doesn't have (they wait in the library; reserve, order; `GameManager.DeliverOrdersNow` makes it arrive on the cart at once - tutorial only - then hand it over). A checklist on the card ticks each one. While the computer is open the frame marks the tab or button to press (search box / Catalogue tab, Members tab, Order button).
- End (done or skipped, fade out/in): tutorial visitors removed (requests, reservations, seats), coins, reputation and today's numbers put back (`GameManager.ResetAfterTutorial` with a `DayStats.Copy`), members who signed up in practice removed, and the books put back as they were (`GameManager.RememberBooksForTutorial` / `RestoreBooksAfterTutorial`: every copy, the catalogue and new titles, orders, the delivery cart and your hands), then +20 coins welcome gift the first time it's completed (`TutorialGift`). The closing-time lock-up guidance is now a tip ("lockup").
- The tutorial card and frame live on their own panel (`PS_Tutorial`, sort 28: above the computer and notebook, under the title menu), so screens' dark backdrops never cover them.
- Day 2: after the Archive reveal the camera glides to the repair desk and frames it ("Mend damaged books at the repair desk"; `Tutorial.Spotlight`).
- Tutorial is linear: no penalties during it (wrong card or book costs no patience or reputation), the dialogue only offers the way forward (no hint, "Sorry, we can't get that", "isn't here yet", "can't get that one"; the buyer gets no "sold out" option), the card visitor leaves after their card (no extra request), only an order placed in the order step counts, and a tutorial visitor who leaves before their step is done is replaced by a new one for the same step (`Tutorial.Respawn`).
- Only the next thing works (`Tutorial.Allows`, checked by `InteractionController`): the tour and the last card allow no clicks; each step allows only its visitor, the computer, the book's shelf or the cart. The PC opens on the step's tab and greys the others (`Tutorial.NeededTab`); shelf and cart Take work only for the step's one book (`AllowsBook`; no Worn/Damaged buttons); ordering only for the seeker's book in the order step (`AllowsOrder`). The buyer asks for one exact title. The tour waits for the title-menu camera glide to finish and measures from the follow camera (`CameraMoving`, `FollowCam`).
- Tutorial polish: the HUD reads "Day 0 - Training day" while it runs; no camera zoom to the computer, the shelf or the book cart (the pixel frame shows them; the tutorial's zoom helper is gone); no notification cards or tips are raised during it (they came all at once after); the Order button goes away for good once the order is placed (`AllowsOrder` checks `OrdersPlaced`). Wrong library-card details are only said by the visitor, not written in the notebook. `NotebookHUD.PointerOver` is now checked from the mouse position every frame (Clear removed the element under the mouse, the leave event was lost and world clicks stayed blocked).

## Playtest fixes (UI)
- Issuing a library card stamps the card under the monitor, then closes the computer (`PCMembersView.Issue` → `StampAndClose`).
- Notebook: talking to a visitor again after you cleared their note puts the same note back, never twice (`RequestLog.Begin`). It comes back without the books you'd noted for it; books the visitor turned down (struck through) stay.
- Pause menu: pressing Esc again while the menu was still fading out (650 ms) reopened it and then the fade's delayed hide fired anyway, leaving a locked game with no HUD and no menu. The delayed hide now skips if the menu is showing again (`MainMenuUI.BeginPlay`).
- "In your hands" box moves onto the notebook's panel (sort 25) at runtime (`NotebookHUD.Adopt`), so it shows above the PC, shelf and repair backdrops. Books are listed one per row: slot icon, name on the right (24px).
- Notes and Reserved sit top right, stacked under the coin purse (`Notebook.uss`). While a shelf, cart, return bin, repair desk or the notice board is open they drop lower (`.below-close`, padding-top 280) so the screen's × in its top-right corner isn't half covered by the notebook on narrower screens.
- With Notes collapsed, the notebook is only the tab's height (`#notebook.collapsed { max-height: 80px; }`), so Reserved moves up right under it; opening Notes pushes Reserved down.
- Notes and Reserved pages are 430px wide. `NotebookHUD.FitLists` sizes the lists from the screen height: Notes and Reserved get half each of the space below the coin purse. A folded box takes only its tab's height.

## Staff page, hiring, Day Report, birthday (playtest round 2)
- PC Shop > Staff: one card per role (`PCShopView.StaffCard`): icon, name/role, level pips and mood (hired) or the hire price tag (not hired), "What they do" (`GameManager.StaffDoes`, icon|text, icons `.si-*` in PC.uss), "Next: Level N" (35% faster, needs less help, +1 book per trip), or "Upgrades later" with the prices; button Interview (price) / Upgrade (price) / Booked / Day N / Max level.
- Hiring is paid up front: `ToggleStaff` takes the full `StaffHireFee` when the interview is booked (`hirePaid[]`, saved; it can't be cancelled). After the morning interview, `MorningInterviews` hands back the difference (hired for less) or the whole budget (nobody hired), logged as "Hire refund" with a feed notice. The interview's Hire buttons see coins + the budgets. Old saves with an unpaid booking still pay at hire time.
- Archivist and cleaner carry `GameManager.StaffCarry` books per trip (1 + level): the cleaner takes that many good returns and shelves them one by one; the archivist takes that many worn/damaged copies (bin or shelf) and leaves them on the repair desk to mend one by one. At closing, a problem or going home, everything in their arms goes back to the return bin (`StaffWorker.DropAll`).
- Cleaner upgrades cost 50 / 100 (were 35 / 70).
- Day Report left page: today's twist in a gold box, then nine tiles (icon, number, name; zero = faded). The coins ledger (start, in, out, coins now, wages) is the first block of the Summary tab.
- Library card birthday: Day / Month / Year are now wheels (see the Members form); the old `.dd` drop-down styles are gone.
- Coin purse counts one coin at a time (`HUD.TickCoins`): earning adds each flying coin's share as it lands (with its clink), so the number reaches the total on the last clink; spending counts down quietly in about half a second. Loading, a new day or an unready HUD snaps straight to the total (`SnapCoins`). The PC header and Day Report show the real total at once.
- Tutorial: the checklist is now three step cards (Find a book / Library card / Order a book; book, key, scroll icons): orange edge on the current one, the kit's tick on finished ones (`.tut-stepcard`). The order visitor comes straight to the desk (no shelf search in the tutorial), and the "Talk" arrow shows only once they're waiting there. The order step is Express only: the frame is on the Express button, the normal Order button is greyed out (`Tutorial.ExpressOnly`), the card says they need it today, the visitor answers "Today, please!", and practice coins are topped up to the express price if needed.
- Tutorial, first visitor: once their book is picked in the Catalogue, the frame moves to **Add to note**, the card says to press it, and nothing else works (`Tutorial.NoteOnly`: the left column is greyed out and the PC can't be closed, except by `ForceClose`). Pressing it closes the PC and moves on to the shelf. Order step: pressing Express closes the PC too.
- Tutorial "You're ready!": **Start Day 1** is the orange `primary` kit button, framed and focused; Enter also starts the day. During the tutorial the gold glows (computer upgrade, bin, desk, cart) only show on what the step lets you click (`ReturnBinGlow` checks `Tutorial.Allows`).
- Tutorial labels no longer stack: the arrow over a visitor sits just above their bubble (`DayStory.Point(..., above: Customer.Overhead)`, placed by `WorldPrompts` after the other followers), the hover prompt hides on whatever the arrow points at (`Tutorial.IsPointingAt`), the cart shows only the frame and the "Book cart" arrow (no "Orders arrive here" label, no delivery bubble during the tutorial).

## Music (menu, day, night)
- `Assets/Audio/Music`: `BGM_Menu.mp3` (was BGM), `BGM_Day.mp3` (was BGM1_Compressed), `BGM_Night.mp3` (was BGM2_Compressed). Renamed in Unity, so GUIDs and scene references are unchanged; import settings are as the files came in.
- New-game intro (InterviewScene on its own, the talk with Dean Whitmore): plays `BGM_Menu` (`StaffInterview.music`).
- `DayNightMusic` (Core) sits on `Audio_BGM` in MainGame and only swaps the clip on its AudioSource, with a fade (`fadeSeconds`, 4 s out, then 4 s in, back to the same volume). Which track:
  - `menuMusic` (BGM_Menu): the title screen (`MainMenuUI.OnTitle`; not the Esc pause screen) and the morning staff interviews (`StaffInterview.OverlayOpen` without `Ending`).
  - `nightMusic` (BGM_Night): from closing time, and the ending/outro with the Dean (`StaffInterview.Ending`).
  - otherwise the source's own clip (BGM_Day), including the tutorial.
- At closing (`Phase.Closing`, 5:30 PM) the grandfather clock strikes (`GameManager.closingBell`, 11.6 s): the day track fades out under it and the night track only fades in once the strikes are over.
- The first track is picked on the first frame without a fade (the title screen starts straight on BGM_Menu; a reload after closing on BGM_Night). Loop, volume and the Music setting (`GameSettings`) are untouched.

## Queue: staying at the desk keeps first place
- Someone who stays at the front desk for a second request (a VIP with a fresh Rare card and no attendant hired, a new member who meant to browse but found no shelf, any "ask again at the desk") goes back in line at the front (`CounterQueue.JoinFront`), not the back. Joining at the back let the next visitor walk onto the desk spot and stand inside them.

## Night ghosts (`Day/GhostNight.cs`, made at runtime)
- After closing, once every visitor and staff member has gone (no active `Customer` or `StaffWorker`), or at the latest 20 real seconds after closing (`LateSeconds`, so a stray visitor can't cancel the night), ghosts fade in: at least one in every open hall (Main Hall, Archive, Rare Books), up to `MaxGhosts` (5). Each keeps to its own hall (`HallAt`: the two `RoomDarkness` boxes, everything else is the Main Hall) and only uses floor spots the librarian can walk to (`Reachable`), so none end up on shelf tops or in the closed-off back room. About half are readers: they take an empty floor-level chair in their hall and read (`SitRead`); the rest drift from shelf to shelf in their hall, pausing as if browsing. They float slightly and shimmer.
- The manager is made on every scene load (`sceneLoaded`), not just the first: a new game goes menu → intro → MainGame again, which used to leave that MainGame with no ghosts at all. `GameSettings.Apply` (volumes) hooks scene loads the same way.
- Built from a random visitor prefab under an inactive parent: every script except `CharacterAnimator`, all colliders, obstacles and audio, and the blob shadow are removed; all renderers use `Resources/M_Ghost` (`Art/Shaders/SH_Ghost.shader`: unlit, see-through, brighter rim, depth prepass; both passes use `TransformObjectToHClip` and the colour pass has `Offset -1,-1` so depths match on Mac/Metal and the ghost does not flicker), no shadows. Their NavMeshAgent has no avoidance and the lowest priority, so the librarian walks straight through and nobody steps aside. Not clickable, not saved.

### The night letter (`Day/GhostLetter.cs`, `UI/GhostLetterUI.cs`, `Resources/GhostLetter.asset`)
Ada Whitmore, the founder, never finished her last book. Added to the GameManager at runtime; no scene edits.
- **Day 3 morning:** a folded paper (two thin palette-parchment slabs) lies on the return bin. No tip or arrow; the bin's hover text is unchanged. Clicking the bin reads it first (`ReturnBin.Interact` → `GhostLetter.TakeLetter`). A tip says the scroll beside the desk plate re-opens it.
- **The scroll** (`GhostLetterUI`, its own panel, sort 26) sits right of the desk plate (`HUD.BarRect`/`BarShown`) from the letter until the book has gone. It always opens the newest paper: letter → pressed note → case card → fireplace words → typewriter page → the slip in the book. The signature fills in: `????????????` → `A?? W???????` → `Ada W???????` → `Ada Whitmore`. Papers are a tilted sheet of old, yellowed paper with brown ink (`UI Toolkit/GhostLetter.uss`), Esc / × / click outside to close; they freeze the clock like dialogue.
- **The trail:** the grandfather clock's plate "E. Harrow, 1891" (every other click before the letter, always after it) → search Harrow: *Notes on Clockwork* (`ARC-B`, `QuestOnly`, not sellable) → taking it off the shelf drops the pressed note → only then the bust (4), lectern (9) and globe (2) show their numbers → the display case lock (always there: three `PCMembersView.Wheel`s, any number of tries, wrong = "The lock won't turn." + shake) → 4-9-2 unlocks it (the case itself never opens; the card's words become the latest paper) → after closing, click the fireplace: its line shows in a bubble, and from then on (each night until her name is typed) ember-orange words glow on the chimney breast in four short lines (a TextMesh in the signs' font, nudged along the wall away from the grandfather clock so the clock never hides them) and clicking the fireplace says them → the Archive typewriter (`SM_Typewriter`): wrong name = the keys type it, X it out by themselves and type "no." (every third try: one more letter of her name); right name (case and spaces don't matter) = "thank you." typed on its own, then the page asking you to shelve the book.
- **The book** (*The Last Chapter*, on the cart page as "The empty case in the Archive", a runtime `BookData`, `QuestOnly`, shelf id `WHITMORE` so no real shelf takes it) is on the book cart the morning after. Its slip points to the empty Archive bookcase (`SM_Bookcase_Tall_2m`): click it holding the book to stand `SM_Book_Folio` in it, click with empty hands to take it back. Where it is (cart / shelf / hands) is saved; hands overnight means back on the cart, and it can never be lost (`TrackBook`).
- **Elias Harrow** (`PF_NPC_Mysterious_Visitor`, name "???") walks in at night: from the night the book arrives, once the library is Closed and every visitor and staff member has gone home (2.5 s of quiet), he comes through the front doors alone and waits at the desk (no line, no patience). The front ropes stay open for him: until he has been and gone they say "The ropes won't latch. Someone is still coming tonight." (`EndDayDoor` checks `GhostLetter.StrangerDue`), and once he's inside, "Wait until the last visitor has left". He never goes home before you've talked to him (`DayStory.Visit.staysAfterClosing`). After he leaves, the ropes lock as usual. `StrangerDue` is false if he can't be brought in at all (missing prefab, no queue) or if he came and went without an answer, so the night can never get stuck; he then comes the next night. The night ghosts wait for him (`GhostLetter.StrangerDue` in `GhostNight`): give him the book and they come out as soon as he's through the doors, Ada shelving included; keep it and none come. Without the book: "It's on the cart / her shelf. I'll wait." (back in the line). With it: **Give** → he says Ada can rest now and that the returns may be back on their shelves by morning; +10 reputation; he walks away from the desk as himself and only fades (M_Ghost) over the last two metres to the front doors (`CounterQueue.exit`); the shelf's own sign turns on reading "Whitmore", the title menu tip becomes "They're still reading." for good, and Ada (`PF_NPC_Professor_Ingrid` as a ghost) shelves the good returns at night from then on, one book at a time, slowly (while carrying she drifts at 60% speed and her walk plays at 0.6x). **Keep** → "Greedy little thing, aren't you?", he leaves normally, the ghosts never come again (`GhostNight.Spawn` stops), and the PC Home "Needs you" list offers "Sell" for 1000 coins.
- The night before he comes, if the book is on its shelf, Ada stands there holding it.
- **Kept out of everything:** `BookData.QuestOnly` → `BookRequest.Available` false and `Create` skips it (no requests, seekers, readers, jobs, mess books), the cleaner never takes it off the cart, Restoration never picks it, and offering one to a visitor gets "That's not mine" with no penalty (`Customer.TryGive`).
- **Elias's model:** `NPC_Mysterious_Visitor.fbx` exports its hat with no bone weights (60 vertices). The prefab uses `NPC_Mysterious_Visitor_HatFixed.asset`, a copy of the mesh with the hat on the head bone. If the model is re-exported, weight the hat to `head.x` in Blender and point the prefab back at the FBX mesh.
- **Delivery** is checked when the day starts and again every few frames (`DeliverIfDue`), and the scenery setup can't stop it (it runs in a try/catch).
- **Saving:** puzzle steps save at once (PlayerPrefs `GhostLetterLive`, merged on load, deleted by `DeleteSave`); the book, the visit and the choice save with the library (`SaveData.quest`). Old saves start with nothing done. `GhostLetterGiven` (the menu tip) is kept across new games on purpose.
- They fade out when the next day opens. `SeatPoint.All` / `IsFree` were added so they only take empty chairs.

## Member hearts with gameplay (`Customers/Members.cs`, `Customer.cs`)
- Hearts 0-5 in halves (the Members page shows half hearts with the kit's `IconHeart01e`). A new card starts at 3, or 2.5 if any wrong details were tried first (`cardMistakes`); no sign-up bonus. A visitor who storms off before the card is issued gets no card (Issue then answers "Nobody is waiting for a card").
- Titles: 4+ Friend (25% per visit, max 2 a day, brings a stranger who wants a card: `CustomerSpawner.SpawnFriend`, line "X said I should get a card here!"), 5 Patron (every 3 days at most, 50% per visit: comes to the desk, names 1-2 books and they go straight onto their shelves; ordinary open-room titles, emptiest first, never past 3 copies, each title once a day; +1 reputation), 1 or less Unhappy (red row).
- Complaints (`Members.Complain`, last 3 kept, saved): ignored "?", wrong book, wrong shelf in a chat, walked out, never ordered, "can't get that" (when it was there), waited for an express book, waited at the desk to check in/out.
- Ignored "?": a member waiting unheard with a "?" for 1.5 game hours (only while open; the clock stops on screens and in talks) loses half a heart, says "Never mind, I'll manage." and leaves.
- Card back: an Unhappy member with at least one complaint comes to the desk on their next visit and says their two latest complaints; then they leave the list, reservations cancelled, -2 reputation, a notice and a Log line. They can't sign up for 2 days (`GameManager.returnedCards`, saved). If they give up waiting at the desk the card is left anyway. Old saves (no complaints) never trigger it.
- Every heart gained is scaled by `Members.GainScale` (0.7), losses aren't: a well-treated regular becomes a Friend around Day 2-3 and a Patron around Day 3-4 (a perfect reader visit can still make a Friend in one go). A single visit costs at most 1.5 hearts (`Member.visitLoss`). Hearts popping over the head (`HUD.SkillPop` with `heart-up` / `heart-down`, with `GameAudio.heartUp` ("heart up sound") / `heartDown` ("heart down")) when a half-heart line is crossed; notices for new Friend / Patron / Unhappy. None of this applies in the tutorial or to the Dean; VIP scholars do count (the thief never gains or loses any).
- Members page: click a row to open their card (title and perk, visits, gifts, "What went wrong" newest first). Day Report Summary: a "Members" line (hearts won/lost, new titles, gifts, cards returned).
- **Interview Skip:** `SkipIntro` stops the interview mid-way, so it now removes every character the interview made (`ClearSpawned`). Characters are also moved into the interview's own scene when made, so none can be left behind in the library (the morning interview is an overlay on MainGame): before, an applicant skipped mid-interview sat in the interview chair for the rest of the session, and later applicants and the Dean sat on top of them.

## Signing, inspector, search, repair desk, click-outside, gold stars
- **Story visits can skip the desk** (`DayStory.Visit.direct`): no queue and no talk; `after` runs as they walk in, then they go straight to their `linger` and come to the desk for `next`. **`Visit.unheard`** runs instead of the scene if the library closes before you hear them (they still settle up), then they leave.
- **Book signing:** 9:18 the author chats at the desk, then walks to the signing table, looks it over ("This'll do nicely.", `Customer.PeekThenLeave`, state `Peek`) and leaves. 2:00 PM: straight to the table and sits (direct). 3:30 PM: one trip to the desk to pay the table fee (30) plus the thank-you (25 if 3+ sold) together. Not at the desk by closing: the money is left on the desk with a banner, then they go.
- **Inspection:** the Dean warns at 9:18 as before; Mr. Pryce introduces himself at the desk around 10:15 and leaves; at 1:00 PM he comes straight in and starts his rounds (direct, looks round the shelves till 2:30); then the verdict at the desk. Closing before the verdict: his report is left on the desk (same verdict and reputation).
- **Search everything** (`BookFilterBar.SearchMatches`, used by the PC catalogue and the Day Report shop): every word typed must appear somewhere in the book's text (title, author, subject, topic, tags, description, type, shelf code and name, room, tier), any order; case, apostrophes and punctuation ignored.
- **Day Report shop filters reset each evening** (`BookFilterBar.Reset` on a fresh open): yesterday's "Show: Reserved" no longer sticks.
- **Shelf rows always show the copy count** ("x3") beside the dots, like the catalogue.
- **Repair desk:** clicking it only drops off worn/damaged books in your hands and opens the screen; finished books stay on the table until you press **Take** (or **Take all**, as many as your hands hold). It no longer fills your hands when you just check on a repair.
- **Click outside to close:** the shelf, cart, return bin and repair desk close on a click on the backdrop outside the book (not the click that opened them; never during the repair minigame).
- **Gold stars** (`UI/GoldStars.cs`, `.gold-star` in GameUI.uss): the kit's `IconStar01a` instead of text stars. Interview card (Speed / Care / Manner, faded stars for the rest, "?" until known; the card bumps when an answer updates it; the answer lines no longer print stars), the hire list, the Day Report rating, and the HUD's "room is open" pin.
- **Desk stamp sound** (`GameAudio.DeskStamp`): check-ins (returns, readers bringing a book back) and stamp-outs (a visitor bringing a book they took themselves) play the card stamp sound at the same volume (`cardStamp`), 0.22 s after the 0.5 s Stamp animation starts (its down-hit). Handing over a requested book keeps just the animation and the served chime.
- **Member heart pops** show just the heart (rising pink or falling grey), no "+½" text: the game font has no ½ and it showed as "+0".
- **New titles start with no copies** (`BookDatabase.CreateNewTitlesCopy`): 22 entries in the asset carry 2-3 copies, which used to appear on the shelf the moment you ordered the title (a Seeker's book then looked "in stock", and asking when they need it was treated as a false "sold out"). The asset is untouched; only the runtime copy is zeroed.
- **Power cut and library cards:** no new card visitors arrive while the power is out (`CustomerSpawner`). A card visitor already queueing or waiting when it goes (VIPs too) says they'll come back after two and leaves with no penalty (`Customer.ComeBackAfterPower`); they return to the desk at 2:15 PM (`DayStory.CardVisitorAfterPower`) and carry on as a card visitor.
- **Restoration owner, fixed:** the repair record was cleared the moment the owner walked back in, so a finished book always counted as "not done". Now it stays until they collect it. If it isn't finished when they talk to you, they ask "Is it ready?": **Let me check** puts them back in line at the desk with the same scene (repair it, then talk again), **Sorry, it isn't done** sends them home with it as it is (-1 rep). Holding a library copy of the same title gets "That's not mine". Closing while they wait: they take it home, no fee.
- **Notebook click-through:** the Notes and Reserved boxes (`notebook`, `reservations` in Notebook.uxml) ignore clicks themselves, so only their tab and paper catch clicks. Empty space around them reaches whatever is underneath (closing the shelf / PC on a backdrop click).
- **Leave warning (web build):** `Core/LeaveGuard.cs` + `Plugins/WebGL/LeaveGuard.jslib` add a `beforeunload` handler at startup, so closing the tab (often a stray Ctrl+W while walking, which browsers won't let a page block) asks "Leave site?" first. Web build only; does nothing in the editor. Browsers show it once the player has clicked in the page.
- **Clock bar and coins above the work screens** (`HUD.BuildTopLayer`): the `.hud-bar` and `#hud-purse` move onto their own panel ("HUD Top", sort order 22: above the PC 20 and the shelf / repair backdrops, below the notebook 25 and menus 30), so they stay bright while those screens are open. Display only (clicks pass through). Hidden whenever the game HUD is (title / pause menu, interview), during the Day Report and screen fades; the clock bar also hides while the PC is maximised (`PCOverlayTrigger.MaximisedOpen`).
- **Desk plate (bottom bar)** (GameUI.uxml `.hud-bar`, `.plate-*` in GameUI.uss, `HUD.UpdatePlate`): all pixel art from the kit, 9-sliced at 4px: the dark bordered slot (`Slot01b`) as the plate, three windows of the frame (`Frame01a`) tinted gold (no white): **DAY 3** with "Week N" (bumps on a new day; "0 / Training" in the tutorial), the **clock** (42 px with the clock icon, no bar, no colour changes or blinking (no rush), and a small tag for slowed / paused / closed), and **reputation** (42 px, the star bounces on change, a pixel goal bar (`Bar01a` + `Fill01a`) and "Goal: Day N" / "Goal met!"; no bar once the Dean's goals end). On Day 1 it glows for 20 s with a one-time tip.
- The desk plate sits 8 px from the bottom; the dialogue box (`.dialogue` padding-bottom 150), the book screens (`.modal` padding-bottom 136) and the PC monitor (`#container` padding-bottom 136, none when maximised) sit above it so nothing overlaps. The interview room keeps its usual dialogue height (Interview.uss).
- **Tips and the notification feed:** all 16 first-time tips are one short line. Tips come **one at a time**, at least **45 s apart** (`Tutorial.TipGap`), never while 3+ visitors queue, and **fade after 8 s** (`HUD.TipSeconds`; clicking still closes one early). The feed shows at most **3 cards** (`HUD.MaxNotes`); older ones pushed out show as "+N more in the Log" (`HUD.ShowMore`), and every event card is in the PC Log anyway.
- **Coffee machine:** `SM_CoffeeMachine` in MainGame has a box collider and an `InspectInteractable` with 5 lines (in order), like the typewriter and globe.
