using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

/// <summary>
/// Owns the running game: coins, the live book catalogue and the in-game clock.
/// One per gameplay scene. Runs early so the catalogue exists before any UI asks for it.
/// </summary>
[DefaultExecutionOrder(-100)]
public class GameManager : MonoBehaviour
{
    public static GameManager Instance { get; private set; }

    public enum Phase { Open, Closing, Closed }

    [Header("Economy")]
    public int startingCoins = 50;

    [Header("Day")]
    public int day = 1;
    public float openHour = 9f;
    [Tooltip("Bell rings: no new customers, everyone finishes up.")]
    public float closingHour = 17.5f;
    public float closeHour = 18f;
    [Tooltip("Real minutes a full day (open to close) takes.")]
    public float realMinutesPerDay = 4f;
    public AudioClip closingBell;

    /// <summary>Running totals for the day report.</summary>
    [Serializable]
    public class DayStats
    {
        public int served, angry, booksSold, booksRead, returns, income, tips, repairsPerfect, repairsNormal, cleaned, signups;
    }
    public DayStats Today { get; private set; } = new DayStats();
    public int Reputation { get; private set; }
    /// <summary>Reputation when this day opened (for the report).</summary>
    public int ReputationAtStart { get; private set; }
    public void AddReputation(int amount) => Reputation = Mathf.Max(0, Reputation + amount);

    public int Coins { get; private set; }
    public float Hour { get; private set; }
    public Phase CurrentPhase { get; private set; }
    public List<BookData> Books { get; private set; } = new List<BookData>();
    /// <summary>Books the library doesn't have yet (not on any shelf). Ordering one adds it to the library.</summary>
    public List<BookData> NewTitles { get; private set; } = new List<BookData>();
    [NonSerialized] readonly List<string> addedTitles = new List<string>();

    /// <summary>A title in the library, or one it could order.</summary>
    public BookData FindTitle(string title) => Books.Find(b => b.Title == title) ?? NewTitles.Find(b => b.Title == title);
    public bool IsNewTitle(BookData b) => b != null && NewTitles.Contains(b);

    /// <summary>A brand-new title joins the catalogue and its shelf (no copies until the order arrives).</summary>
    public void AddToLibrary(BookData b)
    {
        if (b == null || !NewTitles.Remove(b)) return;
        Books.Add(b);
        if (!addedTitles.Contains(b.Title)) addedTitles.Add(b.Title);
        NotifyBooksChanged();
    }

    public event Action<int, int> CoinsChanged;   // new total, delta
    public event Action<Phase> PhaseChanged;
    public event Action BooksChanged;
    public event Action DayStarted;

    // ---------------- Progress: staff, upgrades, unlocks ----------------

    public enum StaffRole { Repairer, Cleaner, RareAttendant }
    public enum UpgradeKind { RepairTools, RestorationKit, ComfyQueue, RunningShoes, CozyLamps, ReadingNooks, TownFlyers, BookBag, Stamina }
    public static readonly string[] StaffNames = { "Archivist", "Cleaner", "Rare Books attendant" };
    public static readonly string[] StaffInfo =
    {
        "Works at a desk in the Archive, taking worn and damaged books from the returns and repairing them one by one.",
        "Roams the library tidying cups and open books, putting copies back on their shelves.",
        "Looks after VIP scholars in Rare Books: fetches their rare book and checks them in, so you don't have to.",
    };
    /// <summary>Hiring order: Cleaner from day 1, Archivist when the Archive opens, attendant with Rare Books.</summary>
    public static readonly int[] StaffUnlockDay = { ArchiveDay, 1, RareDay };
    /// <summary>Staff rows on the PC and in the day report (the cleaner is normally hired in the opening interview).</summary>
    public static readonly StaffRole[] StaffOrder = { StaffRole.Cleaner, StaffRole.Repairer, StaffRole.RareAttendant };
    /// <summary>Usual hire price by role, paid once: Archive 250, Main hall 70, Rare Books 450. Applicants ask
    /// around this and can be talked down a little in the interview.</summary>
    public static readonly int[] StaffHireFee = { 250, 70, 450 };
    /// <summary>Upgrade prices by role and level. Each level makes them faster and need less help.</summary>
    public static readonly int[][] StaffUpgradeCosts = { new[] { 150, 300 }, new[] { 35, 70 }, new[] { 450 } };
    public bool StaffUnlocked(StaffRole r) => day >= StaffUnlockDay[(int)r];
    /// <summary>Daily wages: none any more (staff are paid once, when hired). Kept so old saves and reports still work.</summary>
    public static readonly int[] StaffWage = { 0, 0, 0 };
    public static readonly string[] UpgradeNames = { "Repair tools", "Restoration kit", "Comfy queue", "Running shoes", "Cozy lamps", "Reading nooks", "Town flyers", "Book bag", "Stamina" };
    public static readonly string[] UpgradeInfo =
    {
        "Repairs take less time.",
        "+7% Perfect Restoration chance.",
        "Customers wait 15% longer.",
        "You walk and run 10% faster.",
        "Warm light: +10% tips.",
        "Comfier seats: +15% reading fees.",
        "Posters around town bring more visitors.",
        "Carry one more book at a time.",
        "Run longer and catch your breath faster.",
    };
    public const int MaxUpgrade = 3;
    public static int UpgradeCost(int level) => 40 + level * 40;

    [NonSerialized] public List<Member> members = new List<Member>();
    [NonSerialized] public bool[] staff = new bool[3];
    [NonSerialized] public int[] upgrades = new int[9];
    public bool Hired(StaffRole r) => staff[(int)r];
    /// <summary>Upgrade level of each staff member (0 = as hired).</summary>
    [NonSerialized] public int[] staffLevel = new int[3];
    public int StaffLevel(StaffRole r) => staffLevel[(int)r];
    /// <summary>Work-speed multiplier from upgrades (1 = as hired).</summary>
    public float StaffWorkFactor(StaffRole r) => 1f + 0.35f * staffLevel[(int)r];
    /// <summary>Price of the next upgrade, or -1 at the top level.</summary>
    public int StaffUpgradeCost(StaffRole r) { var c = StaffUpgradeCosts[(int)r]; int l = staffLevel[(int)r]; return l < c.Length ? c[l] : -1; }
    public int Upgrade(UpgradeKind u) => upgrades[(int)u];
    // ---------------- Play mode (Cozy / Standard / Busy) ----------------

    public enum Difficulty { Cozy, Standard, Busy }
    public static readonly string[] DifficultyNames = { "Cozy", "Standard", "Busy" };
    public static readonly string[] DifficultyInfo =
    {
        "Patient visitors who never storm off, clearer hints, cheaper books and more coins to start.",
        "The library as it's meant to be run: a steady day, fair hints and a few surprises.",
        "More visitors, shorter tempers, vaguer requests and pricier orders. For a real challenge.",
    };
    public static readonly int[] DifficultyStartCoins = { 100, 50, 30 };
    /// <summary>Set by the title screen just before a new game starts.</summary>
    public static Difficulty? PendingDifficulty;

    [NonSerialized] public Difficulty difficulty = Difficulty.Standard;
    float Mode(float cozy, float standard, float busy) => difficulty == Difficulty.Cozy ? cozy : difficulty == Difficulty.Busy ? busy : standard;
    public float ModePrices => Mode(0.8f, 1f, 1.2f);
    public float ModeWrongPenalty => Mode(0.5f, 1f, 1.5f);
    public int ModeExtraVisitors => difficulty == Difficulty.Busy ? 1 : 0;
    /// <summary>Cozy: visitors wait sadly instead of storming off.</summary>
    public bool NoStormingOut => difficulty == Difficulty.Cozy;
    public bool FreeFirstHint => difficulty == Difficulty.Cozy;
    public bool NoHints => difficulty == Difficulty.Busy;
    public float OutOfStockChance => day < 2 ? 0f : Mode(0.1f, day == 2 ? 0.2f : 0.25f, 0.35f);
    public void SetDifficulty(Difficulty d) => difficulty = d;

    /// <summary>Day each upgrade can first be bought, in UpgradeKind order: it unlocks once the thing it helps
    /// with matters (restoration after your first repairs, lamps and nooks with the Archive's readers, flyers once the rooms fill).</summary>
    public static readonly int[] UpgradeUnlockDay = { 1, 2, 1, 1, 2, 2, 3, 1, 1 };
    public bool UpgradeUnlocked(UpgradeKind u) => day >= UpgradeUnlockDay[(int)u];
    /// <summary>Unlocked, not maxed and affordable.</summary>
    public bool CanBuyUpgrade(UpgradeKind u) => UpgradeUnlocked(u) && upgrades[(int)u] < MaxUpgrade && Coins >= UpgradeCost(upgrades[(int)u]);

    /// <summary>True when at least one upgrade can be bought right now.</summary>
    public bool UpgradeReady
    {
        get
        {
            for (int i = 0; i < upgrades.Length; i++)
                if (CanBuyUpgrade((UpgradeKind)i)) return true;
            return false;
        }
    }

    /// <summary>A staff row has something to do: an interview you can afford, or a hire's upgrade you can afford.</summary>
    public bool StaffActionReady(StaffRole r) => StaffRow(r).enabled && !InterviewBooked(r);
    public bool StaffReady => StaffOrder.Any(StaffActionReady);
    /// <summary>Day the player last looked at the Upgrades tab (stops the reminder glow for that day).</summary>
    [NonSerialized] public int upgradesSeenDay = -1;

    /// <summary>A fresh library (no save) in the chosen mode.</summary>
    public void StartFresh(Difficulty d)
    {
        difficulty = d;
        Coins = DifficultyStartCoins[(int)d];
        CoinsChanged?.Invoke(Coins, 0);
    }

    public float PatienceMultiplier => (1f + 0.15f * Upgrade(UpgradeKind.ComfyQueue)) * Mode(1.5f, 1f, 0.7f);
    public float TipMultiplier => 1f + 0.1f * Upgrade(UpgradeKind.CozyLamps);
    public float ReadingFeeMultiplier => 1f + 0.15f * Upgrade(UpgradeKind.ReadingNooks);
    public float VisitorMultiplier => (1f + 0.25f * Upgrade(UpgradeKind.TownFlyers)) * Mode(0.75f, 1f, 1.3f);
    public int BagCapacity => 1 + Upgrade(UpgradeKind.BookBag);
    public int DailyWages { get { int w = 0; for (int i = 0; i < staff.Length; i++) if (staff[i]) w += StaffWage[i]; return w; } }

    /// <summary>Which work day each staff member's wage is already paid for (the Dean pays the cleaner's first).</summary>
    [NonSerialized] public int[] wagePaidFor = { -1, -1, -1 };
    /// <summary>Wages still to pay tomorrow morning (prepaid hires don't count).</summary>
    public int WagesDueTomorrow { get { int w = 0; for (int i = 0; i < staff.Length; i++) if (staff[i] && wagePaidFor[i] < day + 1) w += StaffWage[i]; return w; } }

    public const int ArchiveDay = 2, RareDay = 3;
    /// <summary>The Archive opens on day 2, Rare Books on day 3 (the rope gates come down).</summary>
    public bool ArchiveOpen => day >= ArchiveDay;
    public bool RareOpen => day >= RareDay;

    /// <summary>The clock (and patience, reading, repairs...) stands still while a menu or chat is open.</summary>
    public bool Paused => PlayerLock.FreezesTime;
    /// <summary>Game-time delta: 0 while paused.</summary>
    public static float Dt => Instance != null && Instance.Paused ? 0f : Time.deltaTime;

    // ---------------- Orders, deliveries, reservations ----------------

    [Serializable]
    public class Reservation
    {
        public string key, name, title;
        public int day;
        public bool ordered;
        /// <summary>A member asked the library to get this book in.</summary>
        public bool requested;
        /// <summary>Wanted today: they come back at dueHour, so it needs express delivery.</summary>
        public bool urgent;
        public float dueHour;
        [NonSerialized] public bool queued;
    }

    /// <summary>"2:30 PM" style time for an in-game hour.</summary>
    public static string FormatHour(float hour)
    {
        int h = Mathf.FloorToInt(hour), m = Mathf.FloorToInt((hour - h) * 60f / 10f) * 10;
        int h12 = h % 12 == 0 ? 12 : h % 12;
        return $"{h12}:{m:00} {(h < 12 ? "AM" : "PM")}";
    }
    class Order { public BookData book; public float arriveHour; public int arriveDay; }

    [NonSerialized] public List<Reservation> reservations = new List<Reservation>();
    readonly List<Order> orders = new List<Order>();
    public event Action<int> Delivered; // copies that just arrived
    public event Action Reserved; // a visitor just reserved a book (the HUD's reservations list lights up)

    public const float ExpressHours = 2f;
    public static int OrderPrice(BookData b, bool express) =>
        Mathf.Max(3, Mathf.RoundToInt(b.BaseValue * 0.6f * (express ? 1.5f : 1f) * (Instance != null ? Instance.ModePrices : 1f)));
    public int OnOrder(BookData b) { int n = 0; foreach (var o in orders) if (o.book == b) n++; return n; }

    /// <summary>Standard orders arrive next morning; express ones a couple of hours later today.</summary>
    public bool OrderBook(BookData b, bool express)
    {
        if (b == null || !TrySpend(OrderPrice(b, express))) return false;
        AddToLibrary(b); // a requested new title becomes part of the library
        bool sameDay = express && CurrentPhase != Phase.Closed && Hour + ExpressHours < closeHour;
        orders.Add(new Order { book = b, arriveDay = sameDay ? day : day + 1, arriveHour = sameDay ? Hour + ExpressHours : openHour });
        foreach (var r in reservations) if (r.title == b.Title) r.ordered = true;
        return true;
    }

    void DeliverDue()
    {
        int n = 0;
        for (int i = orders.Count - 1; i >= 0; i--)
        {
            var o = orders[i];
            if (o.arriveDay > day || (o.arriveDay == day && o.arriveHour > Hour)) continue;
            orders.RemoveAt(i);
            if (DeliveryCart.Instance != null) DeliveryCart.Instance.Add(o.book);
            else o.book.AddCopy(BookCondition.Good);
            n++;
        }
        if (n > 0) { NotifyBooksChanged(); Delivered?.Invoke(n); }
    }

    public Reservation ReservationFor(string key) => reservations.Find(r => r.key == key);

    public void Reserve(string key, string name, BookData b, bool urgent = false, bool requested = false)
    {
        reservations.RemoveAll(r => r.key == key);
        reservations.Add(new Reservation
        {
            key = key, name = name, title = b.Title, ordered = OnOrder(b) > 0, requested = requested, urgent = urgent,
            day = urgent ? day : day + 1, dueHour = urgent ? Hour + 2.5f : openHour,
        });
        Reserved?.Invoke();
    }

    // ---------------- Interviews ----------------

    /// <summary>The librarian's name, typed in at the start of a new game.</summary>
    public static string PlayerName => PlayerPrefs.GetString("PlayerName", "Librarian");

    /// <summary>The story ends after this day: the Dean visits before the next morning. Play carries on after.</summary>
    public const int EndingDay = 5;
    [NonSerialized] public bool endingSeen;
    public bool EndingDue => day >= EndingDay && !endingSeen;
    /// <summary>Something happens in the interview room before the next day (the ending, or applicants).</summary>
    public bool MorningVisitDue => EndingDue || interviews.Count > 0;

    /// <summary>Roles with applicants coming in tomorrow morning, before the library opens.</summary>
    [NonSerialized] public List<int> interviews = new List<int>();
    public bool InterviewBooked(StaffRole r) => interviews.Contains((int)r);

    /// <summary>Staff row button: books (or cancels) an interview for tomorrow morning, or upgrades a hire.
    /// The hire fee is paid in the interview, so booking needs that much in the purse.</summary>
    public void ToggleStaff(StaffRole r)
    {
        int i = (int)r;
        if (staff[i]) { UpgradeStaff(r); return; }
        if (!StaffUnlocked(r)) return;
        var hud = FindAnyObjectByType<HUD>();
        if (interviews.Remove(i)) { if (hud != null) hud.ShowBanner($"Interview for the {StaffNames[i]} cancelled.", 4f); return; }
        if (Coins < StaffHireFee[i]) { if (hud != null) hud.ShowBanner($"You need {StaffHireFee[i]} coins for the {StaffNames[i]}'s hire fee.", 4f); return; }
        interviews.Add(i);
        if (hud != null) hud.ShowBanner($"Interview booked! Two {StaffNames[i]} applicants will come in tomorrow morning, before the library opens.", 6f);
    }

    /// <summary>What a staff row shows (PC Shop and Day Report share it).</summary>
    public (string title, string sub, string button, bool enabled) StaffRow(StaffRole r)
    {
        int i = (int)r;
        if (staff[i])
        {
            int cost = StaffUpgradeCost(r);
            string lvl = $"Level {staffLevel[i] + 1}/{StaffUpgradeCosts[i].Length + 1}";
            return ($"{StaffNames[i]}: {StaffPerson[i]}", $"{StaffInfo[i]}  {lvl}.",
                    cost < 0 ? "Max level" : $"Upgrade {cost}", cost >= 0 && Coins >= cost);
        }
        if (InterviewBooked(r))
            return (StaffNames[i], "Interview booked: two applicants come in tomorrow morning, before the library opens.", "Cancel", true);
        if (!StaffUnlocked(r)) return (StaffNames[i], StaffInfo[i], $"Day {StaffUnlockDay[i]}", false);
        return (StaffNames[i], $"{StaffInfo[i]}  Costs about {StaffHireFee[i]}, paid once: talk it over in the interview.",
                Coins >= StaffHireFee[i] ? $"Interview ({StaffHireFee[i]})" : $"Need {StaffHireFee[i]}", Coins >= StaffHireFee[i]);
    }

    /// <summary>Makes a hire faster (and needing less help). False if maxed or too expensive.</summary>
    public bool UpgradeStaff(StaffRole r)
    {
        int cost = StaffUpgradeCost(r);
        if (cost < 0 || !TrySpend(cost)) return false;
        staffLevel[(int)r]++;
        ApplyUpgrades();
        var hud = FindAnyObjectByType<HUD>();
        if (hud != null) hud.ShowBanner($"{StaffPerson[(int)r]} is now level {staffLevel[(int)r] + 1}: faster, and needs less help.", 4f);
        return true;
    }

    public bool BuyUpgrade(UpgradeKind u)
    {
        int lvl = upgrades[(int)u];
        if (!UpgradeUnlocked(u) || lvl >= MaxUpgrade || !TrySpend(UpgradeCost(lvl))) return false;
        upgrades[(int)u]++;
        ApplyUpgrades();
        return true;
    }

    float baseWalk = -1f, baseRun;

    void ApplyUpgrades()
    {
        var mover = GameObject.FindWithTag("Player")?.GetComponent<PlayerMover>();
        if (mover != null)
        {
            if (baseWalk < 0f) { baseWalk = mover.walkSpeed; baseRun = mover.runSpeed; }
            float shoes = 1f + 0.1f * Upgrade(UpgradeKind.RunningShoes);
            mover.walkSpeed = baseWalk * shoes;
            mover.runSpeed = baseRun * shoes;
        }
        var desk = RepairDesk.Instance;
        if (desk == null) return;
        desk.speedMultiplier = (1f + 0.35f * Upgrade(UpgradeKind.RepairTools)) * (Hired(StaffRole.Repairer) ? StaffWorkFactor(StaffRole.Repairer) : 1f);
        desk.chanceBonus = 0.07f * Upgrade(UpgradeKind.RestorationKit);
    }

    /// <summary>After the report: pay staff, open the doors on the next morning, save.</summary>
    public void StartNextDay()
    {
        day++;
        PickPopularBooks();
        // Morning wages, skipping anyone already paid for today (hired last night).
        int wages = 0;
        for (int i = 0; i < staff.Length; i++) if (staff[i] && wagePaidFor[i] < day) wages += StaffWage[i];
        if (wages > 0)
        {
            if (Coins >= wages)
            {
                AddCoins(-wages);
                for (int i = 0; i < staff.Length; i++) if (staff[i]) wagePaidFor[i] = day;
            }
            else { for (int i = 0; i < staff.Length; i++) if (wagePaidFor[i] < day) staff[i] = false; } // can't pay: they quit
        }
        Today = new DayStats();
        ReputationAtStart = Reputation;
        Hour = openHour;
        SetPhase(Phase.Open);
        ApplyUpgrades();
        DeliverDue(); // last night's orders are waiting in the cart
        ReturnPlayerToStart();
        reservations.RemoveAll(r => r.day < day - 1); // nobody waits forever
        Save();
        DayStarted?.Invoke();
    }

    /// <summary>A new morning starts with the librarian back at their spot behind the counter.</summary>
    void ReturnPlayerToStart()
    {
        var player = GameObject.FindWithTag("Player");
        if (!hasPlayerStart || player == null) return;
        // The CharacterController would undo a plain move, so switch it off for the teleport.
        var cc = player.GetComponent<CharacterController>();
        if (cc != null) cc.enabled = false;
        player.transform.SetPositionAndRotation(playerStartPos, playerStartRot);
        if (cc != null) cc.enabled = true;
    }

    // ---------------- Save (PlayerPrefs, WebGL safe) ----------------

    const string SaveKey = "LibrarySave";
    [Tooltip("Load the last save when the scene starts.")]
    public bool loadSave = true;

    [Serializable] class BookSave { public string title; public int good, worn, damaged; }
    [Serializable] class SaveData { public int day, coins, reputation, difficulty = 1; public bool[] staff; public int[] upgrades; public List<BookSave> books = new List<BookSave>(); public List<Member> members = new List<Member>(); public List<Reservation> reservations = new List<Reservation>(); public List<RequestLog.Entry> log = new List<RequestLog.Entry>(); public List<int> interviews = new List<int>(); public bool endingSeen; public int[] staffLevel; public List<string> added = new List<string>(); }

    public void Save()
    {
        var data = new SaveData { day = day, difficulty = (int)difficulty, coins = Coins, reputation = Reputation, staff = staff, upgrades = upgrades, members = members, reservations = reservations, log = RequestLog.All, interviews = interviews, endingSeen = endingSeen, staffLevel = staffLevel, added = addedTitles };
        foreach (var b in Books) data.books.Add(new BookSave { title = b.Title, good = b.GoodCopies, worn = b.WornCopies, damaged = b.DamagedCopies });
        // Copies out of the shelves (returns, repair table, hands) still belong to the library.
        void Count(BookData b, BookCondition c)
        {
            var s = data.books.Find(x => x.title == b.Title);
            if (s == null) return;
            if (c == BookCondition.Good) s.good++; else if (c == BookCondition.Worn) s.worn++; else s.damaged++;
        }
        if (ReturnBin.Instance != null) foreach (var (b, c) in ReturnBin.Instance.Copies) Count(b, c);
        if (RepairDesk.Instance != null)
        {
            foreach (var j in RepairDesk.Instance.Waiting) Count(j.book, j.condition);
            foreach (var j in RepairDesk.Instance.Working) Count(j.book, j.condition);
            foreach (var (b, c) in RepairDesk.Instance.Done) Count(b, c);
        }
        var carry = GameObject.FindWithTag("Player")?.GetComponent<PlayerCarry>();
        if (carry != null) foreach (var (b, c) in carry.Items) Count(b, c);
        if (DeliveryCart.Instance != null) foreach (var b in DeliveryCart.Instance.Copies) Count(b, BookCondition.Good);
        foreach (var o in orders) Count(o.book, BookCondition.Good); // paid for: it arrives with the save
        PlayerPrefs.SetString(SaveKey, JsonUtility.ToJson(data));
        PlayerPrefs.Save();
    }

    void Load()
    {
        if (!loadSave || !PlayerPrefs.HasKey(SaveKey)) return;
        var data = JsonUtility.FromJson<SaveData>(PlayerPrefs.GetString(SaveKey));
        if (data == null) return;
        day = data.day; Coins = data.coins; Reputation = data.reputation;
        difficulty = (Difficulty)Mathf.Clamp(data.difficulty, 0, 2);
        if (data.staff != null && data.staff.Length == 3) staff = data.staff;
        if (data.upgrades != null) Array.Copy(data.upgrades, upgrades, Mathf.Min(data.upgrades.Length, upgrades.Length));
        if (data.members != null) members = data.members;
        if (data.reservations != null) reservations = data.reservations;
        if (data.interviews != null) interviews = data.interviews;
        endingSeen = data.endingSeen;
        if (data.staffLevel != null && data.staffLevel.Length == 3) staffLevel = data.staffLevel;
        // Titles you ordered in earlier days are part of the library now.
        if (data.added != null) foreach (var t in data.added) { var nb = NewTitles.Find(b => b.Title == t); if (nb != null) AddToLibrary(nb); }
        RequestLog.Reset(data.log);
        foreach (var s in data.books)
        {
            var b = Books.Find(x => x.Title == s.title);
            if (b == null) continue;
            b.GoodCopies = s.good; b.WornCopies = s.worn; b.DamagedCopies = s.damaged;
        }
    }

    public bool HasSave => PlayerPrefs.HasKey(SaveKey);

    /// <summary>Back to the title screen (progress since this morning's save is lost).</summary>
    public void ReturnToMenu()
    {
        PlayerLock.Reset();
        UnityEngine.SceneManagement.SceneManager.LoadScene(gameObject.scene.name);
    }

    [ContextMenu("Delete Save")]
    public void DeleteSave() { PlayerPrefs.DeleteKey(SaveKey); Debug.Log("[Game] Save deleted."); }

    /// <summary>Wipes the save and starts again from day 1.</summary>
    public void NewGame()
    {
        DeleteSave();
        PlayerLock.Reset();
        UnityEngine.SceneManagement.SceneManager.LoadScene(gameObject.scene.name);
    }

    void Awake()
    {
        Instance = this;
        var db = Resources.Load<BookDatabase>(BookDatabase.ResourceName);
        if (db != null) { Books = db.CreateRuntimeCopy(); NewTitles = db.CreateNewTitlesCopy(); }
        else Debug.LogWarning("[Game] No BookDatabase in Resources — library is empty.", this);
        Coins = startingCoins;
        Hour = openHour;
        CurrentPhase = Phase.Open;
        RequestLog.Reset(); // the save (if any) brings back past days
        Load();
        // A new game picked on the title screen: its mode and starting coins.
        if (PendingDifficulty.HasValue && !HasSave) { difficulty = PendingDifficulty.Value; Coins = DifficultyStartCoins[(int)difficulty]; }
        PendingDifficulty = null;
        if (!HasSave) LeanStartingStock();
        PickPopularBooks();
        ApplyInterviewHire();
        ReputationAtStart = Reputation;
    }

    /// <summary>Books everyone wants today: visitors ask for them far more often, and they glow on the
    /// computer and the shelves. A new set each day (the same set if you reload that day).</summary>
    public readonly HashSet<string> PopularToday = new HashSet<string>();
    public const int PopularPerDay = 4;
    public bool IsPopular(BookData b) => b != null && PopularToday.Contains(b.Title);

    void PickPopularBooks()
    {
        PopularToday.Clear();
        var pool = Books.Concat(NewTitles).Where(b => b.Sellable && !BookRequest.IsArchive(b) && !BookRequest.IsRare(b))
                        .Select(b => b.Title).Distinct().OrderBy(t => t, StringComparer.Ordinal).ToList();
        var rng = new System.Random(day * 7919 + 101);
        while (PopularToday.Count < PopularPerDay && pool.Count > 0)
        {
            int i = rng.Next(pool.Count);
            PopularToday.Add(pool[i]);
            pool.RemoveAt(i);
        }
    }

    /// <summary>A new library starts thin: one copy of each book for sale and none of the best sellers,
    /// so you have to buy the books people actually want. Archive and Rare Books are as listed.</summary>
    void LeanStartingStock()
    {
        foreach (var b in Books)
        {
            if (!b.Sellable || BookRequest.IsArchive(b) || BookRequest.IsRare(b)) continue;
            bool bestSeller = (b.Tag ?? "").IndexOf("best", StringComparison.OrdinalIgnoreCase) >= 0;
            b.GoodCopies = bestSeller ? 0 : Mathf.Min(b.GoodCopies + b.WornCopies, 1);
            b.WornCopies = 0;
            b.DamagedCopies = Mathf.Min(b.DamagedCopies, 1); // a little repair work still turns up
        }
    }

    // ---------------- Who you hired in interviews ----------------

    static readonly string[] DefaultPerson = { "Omar", "Sam", "Arthur" };
    static readonly string[] DefaultPrefab = { "Scholar_Omar", "Janitor_Sam", "Visitor_Arthur" };
    /// <summary>First name of each role's hire (by StaffRole).</summary>
    public static readonly string[] StaffPerson = (string[])DefaultPerson.Clone();
    /// <summary>Prefab name part of each hire, e.g. "Writer_Finn".</summary>
    public static readonly string[] StaffPrefab = (string[])DefaultPrefab.Clone();
    /// <summary>Walk-speed multiplier from their Speed stars (3 stars = normal).</summary>
    public static readonly float[] StaffSpeed = { 1f, 1f, 1f };

    /// <summary>Reads each role's interview pick (PlayerPrefs) into the staff tables.</summary>
    public static void LoadHires()
    {
        foreach (StaffRole r in Enum.GetValues(typeof(StaffRole)))
        {
            int i = (int)r;
            var parts = PlayerPrefs.GetString("HiredStats_" + r, "").Split(',');
            bool ok = parts.Length == 4 && int.TryParse(parts[0], out int speed) & int.TryParse(parts[3], out int wage);
            StaffSpeed[i] = ok ? 0.7f + 0.1f * int.Parse(parts[0]) : 1f;
            StaffWage[i] = 0;
            StaffPerson[i] = ok ? PlayerPrefs.GetString("Hired_" + r, DefaultPerson[i]).Split(' ')[0] : DefaultPerson[i];
            StaffPrefab[i] = ok ? PlayerPrefs.GetString("HiredPrefab_" + r, DefaultPrefab[i]) : DefaultPrefab[i];
        }
    }

    void ApplyInterviewHire()
    {
        LoadHires();
        PlayerPrefs.DeleteKey("InterviewNewHire"); // old intro flag
    }

    /// <summary>Before the next day opens: the booked interviews (in their own room, faded in and out),
    /// then the chosen people join the staff. Their first wage is paid with the morning wages.</summary>
    public void RunMorningInterviews(Action then) => StartCoroutine(MorningInterviews(then));

    System.Collections.IEnumerator MorningInterviews(Action then)
    {
        var roles = new List<StaffRole>();
        foreach (int i in interviews) if (!staff[i]) roles.Add((StaffRole)i);
        interviews.Clear();
        bool ending = EndingDue;
        if (roles.Count == 0 && !ending) { then?.Invoke(); yield break; }
        if (ending)
        {
            // What the Dean will talk about.
            var names = new List<string>();
            foreach (StaffRole r in Enum.GetValues(typeof(StaffRole))) if (staff[(int)r]) names.Add(StaffPerson[(int)r]);
            StaffInterview.Ending = true;
            StaffInterview.EndingDays = day;
            StaffInterview.EndingMembers = members.Count;
            StaffInterview.EndingReputation = Reputation;
            StaffInterview.EndingStaff = names;
        }

        PlayerLock.Lock(this);
        DayNightCycle.ForceDay = true; // it's the next morning in there, not last night
        List<StaffRole> hired = null;
        StaffInterview.Queue = roles;
        StaffInterview.Coins = Coins;
        StaffInterview.Finished = h => hired = h;
        yield return UnityEngine.SceneManagement.SceneManager.LoadSceneAsync(StaffInterview.SceneName, UnityEngine.SceneManagement.LoadSceneMode.Additive);
        // The interview room has its own camera, UI and dialogue box.
        var cam = Camera.main != null && Camera.main.gameObject.scene == gameObject.scene ? Camera.main.gameObject : null;
        if (cam != null) cam.SetActive(false);
        // Hide every library UI (HUD, notebook, labels) while the interview room is up.
        var docs = new List<UnityEngine.UIElements.UIDocument>();
        foreach (var d in FindObjectsByType<UnityEngine.UIElements.UIDocument>())
            if (d.gameObject.scene == gameObject.scene && d.rootVisualElement != null && d.rootVisualElement.style.display != UnityEngine.UIElements.DisplayStyle.None)
            {
                docs.Add(d);
                d.rootVisualElement.style.display = UnityEngine.UIElements.DisplayStyle.None;
            }
        while (hired == null) yield return null;

        foreach (var d in docs) if (d != null) d.rootVisualElement.style.display = UnityEngine.UIElements.StyleKeyword.Null; // still black: the HUD fade stays on
        if (cam != null) cam.SetActive(true);
        yield return UnityEngine.SceneManagement.SceneManager.UnloadSceneAsync(StaffInterview.SceneName);
        StaffInterview.Queue = null;
        if (ending) endingSeen = true;
        StaffInterview.Ending = false;
        LoadHires();
        foreach (var r in hired)
        {
            // The price you agreed in the interview (or what they asked), paid once.
            var parts = PlayerPrefs.GetString("HiredStats_" + r, "").Split(',');
            int price = parts.Length == 4 && int.TryParse(parts[3], out int p) ? p : StaffHireFee[(int)r];
            if (TrySpend(price)) { staff[(int)r] = true; staffLevel[(int)r] = 0; }
            else FindAnyObjectByType<HUD>()?.ShowBanner($"Not enough coins to hire the {StaffNames[(int)r]} ({price}).", 5f);
        }
        PlayerLock.Unlock(this);
        then?.Invoke();
        DayNightCycle.ForceDay = false;
        var hud = FindAnyObjectByType<HUD>();
        if (hud != null && hired.Count > 0) hud.ShowBanner($"{string.Join(" and ", hired.ConvertAll(r => StaffPerson[(int)r]))} started work today!", 5f);
    }

    // Where the librarian starts the day (their spot in the scene).
    Vector3 playerStartPos;
    Quaternion playerStartRot;
    bool hasPlayerStart;

    void Start()
    {
        var player = GameObject.FindWithTag("Player");
        if (player != null) { playerStartPos = player.transform.position; playerStartRot = player.transform.rotation; hasPlayerStart = true; }
        GameSettings.Apply();
        ApplyUpgrades();
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    void Update()
    {
        if (CurrentPhase == Phase.Closed || Paused) return;
        if (orders.Count > 0) DeliverDue();
        float hoursPerSecond = (closeHour - openHour) / Mathf.Max(0.1f, realMinutesPerDay * 60f);
        Hour = Mathf.Min(closeHour, Hour + hoursPerSecond * Time.deltaTime);

        if (CurrentPhase == Phase.Open && Hour >= closingHour) SetPhase(Phase.Closing);
        else if (CurrentPhase == Phase.Closing && Hour >= closeHour) SetPhase(Phase.Closed);
    }

    void SetPhase(Phase phase)
    {
        CurrentPhase = phase;
        if (phase == Phase.Closing && closingBell != null)
            AudioSource.PlayClipAtPoint(closingBell, Camera.main ? Camera.main.transform.position : Vector3.zero, 0.8f);
        PhaseChanged?.Invoke(phase);
    }

    /// <summary>"09:30" style clock text.</summary>
    public string ClockText
    {
        get
        {
            int h = Mathf.FloorToInt(Hour), m = Mathf.FloorToInt((Hour - h) * 60f / 10f) * 10;
            // 12-hour clock: 9:00 AM ... 6:00 PM
            int h12 = h % 12 == 0 ? 12 : h % 12;
            return $"{h12}:{m:00} {(h < 12 ? "AM" : "PM")}";
        }
    }

    public void AddCoins(int amount)
    {
        if (amount == 0) return;
        Coins += amount;
        CoinsChanged?.Invoke(Coins, amount);
    }

    public bool TrySpend(int amount)
    {
        if (amount > Coins) return false;
        AddCoins(-amount);
        return true;
    }

    /// <summary>Call after changing any book's stock or condition so UIs refresh.</summary>
    public void NotifyBooksChanged() => BooksChanged?.Invoke();
}
