using System;
using System.Collections.Generic;
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
        "Omar works at his desk in the Archive. He takes worn and damaged books from the returns and repairs them one by one.",
        "Sam roams the library tidying cups and open books, putting copies back on their shelves.",
        "Arthur looks after VIP scholars in Rare Books: he fetches their rare book and checks them in, so you don't have to.",
    };
    /// <summary>Hiring order: Cleaner from day 1, Archivist when the Archive opens, attendant with Rare Books.</summary>
    public static readonly int[] StaffUnlockDay = { ArchiveDay, 1, RareDay };
    public static readonly StaffRole[] StaffOrder = { StaffRole.Cleaner, StaffRole.Repairer, StaffRole.RareAttendant };
    public bool StaffUnlocked(StaffRole r) => day >= StaffUnlockDay[(int)r];
    public static readonly int[] StaffWage = { 15, 10, 12 };
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

    /// <summary>True when at least one upgrade is affordable right now.</summary>
    public bool UpgradeReady
    {
        get
        {
            for (int i = 0; i < upgrades.Length; i++)
                if (upgrades[i] < MaxUpgrade && Coins >= UpgradeCost(upgrades[i])) return true;
            return false;
        }
    }
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

    // Which work day each staff member's wage is already paid for, and whether they were hired today
    // (so letting them go before they've done a day's work gives the money back).
    [NonSerialized] public int[] wagePaidFor = { -1, -1, -1 };
    [NonSerialized] bool[] hiredToday = new bool[3];
    /// <summary>The work day a hire made right now would cover: today while open, tomorrow after closing.</summary>
    int HireWorkDay => CurrentPhase == Phase.Open ? day : day + 1;
    /// <summary>Wages still to pay tomorrow morning (prepaid hires don't count).</summary>
    public int WagesDueTomorrow { get { int w = 0; for (int i = 0; i < staff.Length; i++) if (staff[i] && wagePaidFor[i] < day + 1) w += StaffWage[i]; return w; } }

    public const int ArchiveDay = 2, RareDay = 3;
    /// <summary>The Archive opens on day 2, Rare Books on day 3 (the rope gates come down).</summary>
    public bool ArchiveOpen => day >= ArchiveDay;
    public bool RareOpen => day >= RareDay;

    /// <summary>The clock (and patience, reading, repairs...) stands still while a menu or chat is open.</summary>
    public bool Paused => PlayerLock.IsLocked;
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

    public void ToggleStaff(StaffRole r)
    {
        if (!staff[(int)r] && !StaffUnlocked(r)) return;
        int i = (int)r;
        if (!staff[i])
        {
            // Hiring pays the first work day up front (today, or tomorrow if the library has closed).
            if (!TrySpend(StaffWage[i])) return;
            wagePaidFor[i] = HireWorkDay;
            hiredToday[i] = true;
            staff[i] = true;
        }
        else
        {
            // Let go before they've worked a day: you get that wage back.
            if (hiredToday[i] && wagePaidFor[i] >= day) AddCoins(StaffWage[i]);
            wagePaidFor[i] = -1;
            hiredToday[i] = false;
            staff[i] = false;
        }
        ApplyUpgrades();
    }

    public bool BuyUpgrade(UpgradeKind u)
    {
        int lvl = upgrades[(int)u];
        if (lvl >= MaxUpgrade || !TrySpend(UpgradeCost(lvl))) return false;
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
        desk.speedMultiplier = 1f + 0.35f * Upgrade(UpgradeKind.RepairTools);
        desk.chanceBonus = 0.07f * Upgrade(UpgradeKind.RestorationKit);
    }

    /// <summary>After the report: pay staff, open the doors on the next morning, save.</summary>
    public void StartNextDay()
    {
        day++;
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
        for (int i = 0; i < hiredToday.Length; i++) hiredToday[i] = false; // from now on they've put in a day
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
    [Serializable] class SaveData { public int day, coins, reputation, difficulty = 1; public bool[] staff; public int[] upgrades; public List<BookSave> books = new List<BookSave>(); public List<Member> members = new List<Member>(); public List<Reservation> reservations = new List<Reservation>(); public List<RequestLog.Entry> log = new List<RequestLog.Entry>(); }

    public void Save()
    {
        var data = new SaveData { day = day, difficulty = (int)difficulty, coins = Coins, reputation = Reputation, staff = staff, upgrades = upgrades, members = members, reservations = reservations, log = RequestLog.All };
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
        if (db != null) Books = db.CreateRuntimeCopy();
        else Debug.LogWarning("[Game] No BookDatabase in Resources — library is empty.", this);
        Coins = startingCoins;
        Hour = openHour;
        CurrentPhase = Phase.Open;
        RequestLog.Reset(); // the save (if any) brings back past days
        Load();
        // A new game picked on the title screen: its mode and starting coins.
        if (PendingDifficulty.HasValue && !HasSave) { difficulty = PendingDifficulty.Value; Coins = DifficultyStartCoins[(int)difficulty]; }
        PendingDifficulty = null;
        ReputationAtStart = Reputation;
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
