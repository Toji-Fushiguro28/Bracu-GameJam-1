# Tuning Guide: where each mechanic lives

This guide is organized by *"I want to change ..."*. For each item it tells you where to go:

- **Inspector:** select the object in `MainGame.unity` and edit the component. No coding.
- **Code:** open the `.cs` file and change the value or line named.

Script paths are under `Assets/Scripts/`. After editing code, let Unity recompile. After changing Inspector values, save the scene.

> **Tip:** most Inspector values on runtime-spawned characters (customers, staff) come from the **script defaults**, because those components are added in code. So for `Customer` and `StaffWorker` values, change the default in the `.cs` file.

---

## 1. Time and the day

| I want to change... | Where |
|---|---|
| How long a day lasts (real minutes) | Inspector → `GameManager` → **Real Minutes Per Day** (4). Players can't change it |
| Opening, closing-bell and closing hours | Inspector → `GameManager` → **Open Hour** (9), **Closing Hour** (17.5, the bell), **Close Hour** (18) |
| The clock format (12-hour) | `Core/GameManager.cs` → `ClockText` |
| Which day the Archive / Rare Books open | `Core/GameManager.cs` → `ArchiveDay = 2`, `RareDay = 3`. This also moves the gate signs' text, staff unlocks and banners |
| The pause while menus are open | `Core/GameManager.cs` → `Paused` (uses `PlayerLock.IsLocked`). Timers use `GameManager.Dt` |
| The day rhythm (morning rush, lunch lull...) | Inspector → `CustomerSpawner` → **Busyness** curve (x = hour, y = how busy; 1 = normal) |
| Night colour grade / when dusk starts | Inspector → `DayNightCycle` → **Dusk Start**, **Night Curve**. The look itself is `VP_Night` (next to `VP_Cozy`) |
| The lunch-lull banner and morning banners | `UI/HUD.cs` → `MorningText()` and the lunch check in `Update()` |

## 2. Visitors

| I want to change... | Where |
|---|---|
| How often people arrive | Inspector → `CustomerSpawner` → **Interval** (seconds between arrivals, before busyness and reputation) |
| How many can be inside at once | Inspector → `CustomerSpawner` → **Max Customers** (reputation and Busy mode add more) |
| The visitor mix | Inspector → `CustomerSpawner` → **Browser / Reader / Returner Chance** (the rest are buyers), **Sign Up Chance**, **VIP Chance** |
| How many browsers sit and read vs take the book home | `Customers/Customer.cs` → `readHereChance` (0.75) |
| How long readers read | `Customers/Customer.cs` → `readSeconds` (50–90 s) |
| How long people wait (patience) | `Customers/Customer.cs` → `patienceScale` (1.6 = everyone waits 1.6× the base). Base seconds per request type: `Customers/BookRequest.cs` → `Patience` |
| The wrong-book penalty | `Customers/Customer.cs` → `wrongBookPenalty` (share of patience lost) |
| Walking speed of visitors | `Customers/Customer.cs` → `walkSpeed` |
| Which characters visit | Inspector → `CustomerSpawner` → **Customer Prefabs**. Professors only come as VIPs (`Pick()` in `CustomerSpawner.cs`) |
| Who someone is (name, birthday, card access) | `Customers/Members.cs` → `Profiles` (keyed by prefab name) |
| How much members' hearts boost tips and patience | `Customers/Members.cs` → `TipFactor`, `PatienceFactor` |
| The mood face pictures | `Assets/Art/UI/Mood/T_Mood_*.png` (13×13, Point filter). The thresholds are in `Customer.cs` → `SetMood()` |
| The "?" before you talk to them | `Customers/Customer.cs` → `heard`, `AskMark`. Style: `GameUI.uss` → `.customer-speech.ask` |

## 3. Requests, hints and the puzzle

| I want to change... | Where |
|---|---|
| What people ask for (hint types and lines) | `Customers/BookRequest.cs` → `Create()`, `TagLines`, `About()` |
| Simpler hints on Day 1 | `BookRequest.cs` → `bool simple = day < 2` |
| How often they ask for sold-out books | `Core/GameManager.cs` → `OutOfStockChance` (by mode and day) |
| What a hint says | `Customers/Customer.cs` → `HintText()` |
| What a hint costs | `Customers/Customer.cs` → `HintTipFactor` (0.67 = a third off the tip) |
| The conversation choices at the counter | `Customers/Customer.cs` → `Converse()` |
| What the PC search matches (title/author) and the filters | `UI/PCOverlayTrigger.cs` → `ApplySearchFilter()`, `SubjectFilters`, `WireFilters()` |

## 4. Money

| I want to change... | Where |
|---|---|
| Book values | `Assets/Resources/BookDatabase.asset` → each book's **Base Value**. Everything below is based on it |
| Sale price and tip | `Customers/Customer.cs` → `Sell()` (price = value, 80% if worn; tip up to 40%) and `StampOut()` (browsers, 15% tip) |
| Reading fees | `Customers/Customer.cs` → `CheckIn()`. `share` 40% / 15% / 6% (`RareShare`) and `minFee` 4 / 8 / 15 (`RareMinFee`) for hall / Archive / Rare. VIPs pay ×`VipFeeMultiplier` (1.5). Tip = 60% of the fee × patience × member tip factor |
| "Right shelf" chat bonus | `Customers/Customer.cs` → `Chat()`: 10% of the book's value, 3 to `MaxChatBonus` (10) |
| Tip bonus per hint type | `Customers/BookRequest.cs` → `TipBonus` |
| Card fee | `Customers/Customer.cs` → `ReceiveCard()`: normal 10 + up to 6 tip, VIP `VipCardFee` 50 + up to `VipCardTip` 10 |
| Late fee on returns | `Customers/Customer.cs` → `CheckIn()` (35% chance, 2–5 coins) |
| Book order price and express | `Core/GameManager.cs` → `OrderPrice()` (60% of value, express ×1.5), `ExpressHours` (2) |
| Starting coins | `Core/GameManager.cs` → `DifficultyStartCoins` (100 / 50 / 30) |
| Tidying reward | `Day/MessSpot.cs` → `reward` (2) |
| Reputation gains and losses | Search `AddReputation(` in `Customer.cs`. Clean-library bonus: `Day/DayReportUI.cs` → `Open()` |
| Star rating on the Day Report | `Day/DayReportUI.cs` → `Stars()` |

## 5. Play modes (Cozy / Standard / Busy)

All of it is in `Core/GameManager.cs`, in the *Play mode* section:

- **Descriptions:** `DifficultyNames` and `DifficultyInfo`.
- **Values:** every value is a `Mode(cozy, standard, busy)` call:
  - `ModePrices`, `ModeWrongPenalty`, `PatienceMultiplier`, `VisitorMultiplier`, `ModeExtraVisitors`, `OutOfStockChance`
  - Cozy rules: `NoStormingOut`, `FreeFirstHint`
  - Busy rule: `NoHints`
- **Picker screen:** `Assets/UI Toolkit/MainMenu.uxml` (`menu-mode`) and `MainMenu.uss` (`.mode-card`).

## 6. Upgrades and staff

| I want to change... | Where |
|---|---|
| Upgrade names and descriptions | `Core/GameManager.cs` → `UpgradeNames`, `UpgradeInfo` |
| Upgrade cost and max level | `Core/GameManager.cs` → `UpgradeCost()` (40 / 80 / 120), `MaxUpgrade` (3) |
| The Dean's reputation goal | `Core/GameManager.cs` → `ReputationGoals` (Cozy 40 / Standard 60 / Busy 75), checked at the end of `EndingDay` (5). `GoalTier`: reached, close (60%+), missed |
| Patience by day | `Core/GameManager.cs` → `PatienceDayFactor` (100% Days 1-2, 85% Day 3, 75% after) |
| Reputation changes | `Customers/Customer.cs`: +1 per served visitor, read or card. Wrong book -1, walked out -3, never ordered -3, "we can't get that" when you could -2, waited for nothing -2 |
| Regulars staying away | `Customers/Members.cs` → `LetDown()`: a let-down member skips the next day (`awayUntil`, checked in `CustomerSpawner.Pick`) |
| Day each upgrade unlocks | `Core/GameManager.cs` → `UpgradeUnlockDay` (same order as `UpgradeKind`) |
| Popular books per day | `Core/GameManager.cs` → `PopularPerDay` (4); how much more they are asked for: `Customers/BookRequest.cs` → `Weighted()` (6x) |
| What each upgrade does | Search the upgrade name in `GameManager.cs`: `PatienceMultiplier`, `TipMultiplier`, `ReadingFeeMultiplier`, `VisitorMultiplier`, `BagCapacity`, `ApplyUpgrades()` (shoes, repair speed and chance). Stamina: `Player/PlayerMover.cs` → `MaxSeconds`, `Recovery` |
| Upgrade icons | `Assets/UI Toolkit/PC.uss` → `.upg-0` … `.upg-8` |
| Staff names, wages, unlock days, order | `Core/GameManager.cs` → `StaffNames`, `StaffInfo`, `StaffWage`, `StaffUnlockDay`, `StaffOrder` |
| Staff hire fees and upgrade prices | `Core/GameManager.cs` → `StaffHireFee` (usual one-time price: Archive 250, Main hall 70, Rare 450; candidates ± `dPrice`×5%), `StaffUpgradeCosts`; speed per level in `StaffWorkFactor` (+35%) |
| How often staff need help, and their problems | `Day/StaffHelp.cs` → `ResetTimer` (80–150 s × (1 + level)), `Problems` |
| Interview pay talk | `Day/StaffInterview.cs` → `PayTalk` (offers 60% / 80% / full) and the `floor` range in `CandidatesFor` |
| Which character is which staff, their chairs, typing sound | Inspector → `GameManager` → `StaffManager` |
| How staff behave (walk speed, repair loop, cleaning, sitting) | `Day/StaffWorker.cs` |
| What the Rare Books attendant does for VIPs | `Customers/Customer.cs` → `AttendantServes()` |

## 7. Books, shelves, rooms

| I want to change... | Where |
|---|---|
| Add or edit books (title, author, subject, tags, abstract, shelf, tier, stock) | `Assets/Resources/BookDatabase.asset` (Inspector) |
| Shelf names | Each shelf object → `ShelfInteractable` → **Display Name** / **Shelf Id** |
| How books wear out when read | `Customers/Customer.cs` → `RollWear()` |
| Carry capacity / how held books line up | `Core/GameManager.cs` → `BagCapacity`; `Books/PlayerCarry.cs` → `sideStep` |
| Rope gates (closed and open positions) | `RoomLocks/Gate_*` objects → `RoomGate` → **Open Position / Open Yaw**; gate text in `Day/RoomGate.cs` → `Interact()` |
| Darkness of locked rooms | `Assets/Art/Materials/M_RoomDarkness.mat` → **Tint**; per room: `RoomLocks/Darkness_*` → `RoomDarkness` → **Strength** |
| Opening-day reveal (camera target, distance, timing, sound) | Inspector → `GameManager` → `RoomReveal` → **Archive Focus**, **Rare Focus**, **Closeness**, **Look Seconds**, **Reveal Sound** |
| Front-door rope at night | `RoomGate` with room *FrontDoor* (`Gate_Front_A/B`) |
| Deliveries (cart) | `Books/DeliveryCart.cs`; arrival logic `GameManager.cs` → `DeliverDue()` |
| Return-bin glow | `SM_ReturnBin` → `ReturnBinGlow` → **Color**, **Width**, **Pulse Seconds** |

## 8. Repairs

| I want to change... | Where |
|---|---|
| Perfect chance per tier, repair time, cost | Inspector → `SM_Table_RepairDesk` → `RepairDesk` → **Perfect Chance**, **Repair Seconds**, **Cost Share**, **Max Perfect Chance**, **Minigame Bonus** |
| The minigame (zones, speeds, step text) | `Repair/RepairUI.cs` → `BeginStep()`, `UpdateGame()`, `Titles`, `Hints` |

## 9. Mess and tidying

| I want to change... | Where |
|---|---|
| How often readers leave cups | Inspector → `GameManager` → `Housekeeping` → **Cup Chance**, **Cup Range** |
| How many open books appear at closing | Inspector → `Housekeeping` → **Open Books At Closing** |
| Where cups and books can appear | Any `SM_Teacup` / `SM_Book_Open` with a `MessSpot` component. Duplicate one and move it to add a spot |

## 10. Talking, notices and text

| I want to change... | Where |
|---|---|
| Staff chat lines (Sam, Omar, Arthur) | `Day/StaffTalk.cs` → `Sam`, `Omar`, `Arthur` arrays |
| Reader small talk (35 topics) | `Customers/Customer.cs` → `Topics` (line, reply A, reply B, answer A, answer B; `{0}` title, `{1}` author, `{2}` subject) |
| First reader chat (shelf question) | `Customers/Customer.cs` → `Chat()` |
| "Shh, good part" lines | `Customers/Customer.cs` → `Chat()` (the `Pick(...)` line) |
| Notice board lines and how many per day | `SM_NoticeBoard` → `NoticeBoard` → **Notices**, **Per Day** |
| Main menu title, subtitle, tips | Inspector → `MainMenu` → `MainMenuUI` → **Game Title**, **Subtitle**; tips: `UI/MainMenuUI.cs` → `Tips` |
| How to Play pages | `UI/MainMenuUI.cs` → `HowTo` |
| Credits | `Assets/UI Toolkit/MainMenu.uxml` → Credits section |

## 11. Player

| I want to change... | Where |
|---|---|
| Walk and run speed | Player prefab → `PlayerMover` → **Walk Speed**, **Run Speed** (Running shoes multiply these) |
| Stamina (run length, refill speed and delay) | Player → `PlayerMover` → **Stamina Seconds**, **Stamina Recovery**, **Recovery Delay** |
| Stamina bar height over the head | `UI/HUD.cs` → `BuildStaminaMeter()` (`Vector3.up * 1.55f`) |
| Where the player starts each morning | Move the player in the scene; it's saved on load (`GameManager.ReturnPlayerToStart`) |

## 12. Sound and look

| I want to change... | Where |
|---|---|
| Any gameplay sound | Inspector → `GameManager` → `GameAudio` (one slot per event) |
| Menu hover and start sounds | Inspector → `MainMenu` → `MainMenuUI` → **Hover Sound**, **Start Sound** |
| Typing sound (archivist) | Inspector → `StaffManager` → **Typing Clip** |
| HUD, book screens, dialogue styling | `Assets/UI Toolkit/GameUI.uss` |
| PC screens styling | `Assets/UI Toolkit/PC.uss` |
| Main menu styling | `Assets/UI Toolkit/MainMenu.uss` |
| Coin pop colours | `GameUI.uss` → `#hud-coin-delta` |

## 13. Save

- Everything is saved to PlayerPrefs at the start of each day: `Core/GameManager.cs` → `Save()` / `Load()` (key `LibrarySave`).
- To reset while testing: right-click `GameManager` → **Delete Save**, or untick **Load Save**.
- If you add a new thing that should persist, add it to `SaveData` in `GameManager.cs`, then write it in `Save()` and read it in `Load()`.

## 14. NavMesh (NPC walking)

- After moving furniture: select `Library` → `NavMeshSurface` → **Bake**.
- Furniture blocks walking because of its `NavMeshModifier` (*Not Walkable*). Small props, gates and darkness boxes are set to *Ignore From Build*.
- Agent settings: radius 0.3, step height 0.3 (Navigation → Agents → Humanoid).
