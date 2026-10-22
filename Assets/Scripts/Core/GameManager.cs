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
        /// <summary>Where the money came from (they add up to income; tips are inside each).</summary>
        public int fromSales, fromReading, fromCards, fromVip, fromStaff, fromOther;
        /// <summary>What went wrong: wrong books handed over, regulars let down (lied to, never ordered, waited for nothing).</summary>
        public int wrongBooks, letDown;
        /// <summary>The coin ledger: coins when the day began, every coin in and out since, and what staff kept as their cut.
        /// coinsAtStart + coinsIn - coinsOut is always what you have now.</summary>
        public int coinsAtStart, coinsIn, coinsOut, staffKept;
        /// <summary>Where the money went, by kind (book orders, upgrades, wages...), in the order it was first spent.</summary>
        public List<string> spentOn = new List<string>();
        public List<int> spentAmount = new List<int>();
        /// <summary>Members' hearts won and lost today, and their news (new Friend, gift, card returned).</summary>
        public float heartsUp, heartsDown;
        public List<string> memberNews = new List<string>();

        /// <summary>A copy (the tutorial keeps one to put back afterwards).</summary>
        public DayStats Copy()
        {
            var c = (DayStats)MemberwiseClone();
            c.spentOn = new List<string>(spentOn); // its own lists, not shared
            c.spentAmount = new List<int>(spentAmount);
            c.memberNews = new List<string>(memberNews);
            return c;
        }

        public void Spend(string what, int amount)
        {
            if (amount <= 0) return;
            int i = spentOn.IndexOf(what);
            if (i < 0) { spentOn.Add(what); spentAmount.Add(amount); }
            else spentAmount[i] += amount;
        }

        public enum Source { Sales, Reading, Cards, Vip, Staff, Other }

        /// <summary>Counts money in, by where it came from.</summary>
        public void Earn(Source source, int amount)
        {
            if (amount <= 0) return;
            income += amount;
            switch (source)
            {
                case Source.Sales: fromSales += amount; break;
                case Source.Reading: fromReading += amount; break;
                case Source.Cards: fromCards += amount; break;
                case Source.Vip: fromVip += amount; break;
                case Source.Staff: fromStaff += amount; break;
                default: fromOther += amount; break;
            }
        }
    }
    public DayStats Today { get; private set; } = new DayStats();
    public int Reputation { get; private set; }
    /// <summary>Reputation when this day opened (for the report).</summary>
    public int ReputationAtStart { get; private set; }
    /// <summary>After the tutorial's practice steps: coins and reputation go back to what they were, and today's
    /// numbers start from here (practice doesn't count).</summary>
    public void ResetAfterTutorial(int coins, int reputation, DayStats today)
    {
        int dc = coins - Coins, dr = reputation - Reputation;
        Coins = coins; Reputation = reputation;
        if (dc != 0) CoinsChanged?.Invoke(Coins, dc);
        if (dr != 0) ReputationChanged?.Invoke(Reputation, dr);
        Today = today ?? new DayStats { coinsAtStart = Coins };
    }

    // The tutorial's books: every copy, the catalogue, orders, the cart and your hands, to go back to afterwards.
    List<(BookData book, int good, int worn, int damaged)> tutorialCopies;
    List<BookData> tutorialBooks, tutorialNew, tutorialRare, tutorialCart;
    List<string> tutorialAdded;
    List<Order> tutorialOrders;
    List<(BookData, BookCondition)> tutorialCarry;

    /// <summary>The tutorial starts: remember the books as they are, so practice can't change them.</summary>
    public void RememberBooksForTutorial()
    {
        tutorialCopies = Books.Concat(NewTitles).Concat(RareFinds).Select(b => (b, b.GoodCopies, b.WornCopies, b.DamagedCopies)).ToList();
        tutorialBooks = new List<BookData>(Books); tutorialNew = new List<BookData>(NewTitles); tutorialRare = new List<BookData>(RareFinds);
        tutorialAdded = new List<string>(addedTitles);
        tutorialOrders = orders.Select(o => new Order { book = o.book, arriveDay = o.arriveDay, arriveHour = o.arriveHour }).ToList();
        tutorialCart = DeliveryCart.Instance != null ? new List<BookData>(DeliveryCart.Instance.Copies) : null;
        var carry = GameObject.FindWithTag("Player")?.GetComponent<PlayerCarry>();
        tutorialCarry = carry != null ? new List<(BookData, BookCondition)>(carry.Items) : null;
    }

    /// <summary>The tutorial ends: copies, catalogue, orders, the cart and your hands go back to how they were.</summary>
    public void RestoreBooksAfterTutorial()
    {
        if (tutorialCopies == null) return;
        foreach (var (b, good, worn, damaged) in tutorialCopies) { b.GoodCopies = good; b.WornCopies = worn; b.DamagedCopies = damaged; }
        Books.Clear(); Books.AddRange(tutorialBooks);
        NewTitles.Clear(); NewTitles.AddRange(tutorialNew);
        RareFinds.Clear(); RareFinds.AddRange(tutorialRare);
        addedTitles.Clear(); addedTitles.AddRange(tutorialAdded);
        orders.Clear(); orders.AddRange(tutorialOrders);
        if (tutorialCart != null && DeliveryCart.Instance != null) DeliveryCart.Instance.SetCopies(tutorialCart);
        var carry = GameObject.FindWithTag("Player")?.GetComponent<PlayerCarry>();
        if (tutorialCarry != null && carry != null) { carry.Clear(); foreach (var (b, c) in tutorialCarry) carry.PickUp(b, c); }
        tutorialCopies = null;
        NotifyBooksChanged();
    }

    /// <summary>The tutorial only: every order on its way arrives now (on the book cart).</summary>
    public void DeliverOrdersNow()
    {
        foreach (var o in orders) { o.arriveDay = day; o.arriveHour = Hour; }
        DeliverDue();
    }

    public void AddReputation(int amount)
    {
        int before = Reputation;
        Reputation = Mathf.Max(0, Reputation + amount);
        if (Reputation != before) ReputationChanged?.Invoke(Reputation, Reputation - before);
    }
    public event Action<int, int> ReputationChanged; // new total, change

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
    /// <summary>Rare books the library could still acquire (a VIP request offers one at the rare dealer).</summary>
    public List<BookData> RareFinds { get; private set; } = new List<BookData>();

    /// <summary>A brand-new title joins the catalogue and its shelf (no copies until the order arrives).</summary>
    public void AddToLibrary(BookData b)
    {
        if (b == null || !(NewTitles.Remove(b) || RareFinds.Remove(b))) return;
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
    /// <summary>Staff rows on the PC and in the day report. Every role is hired the same way: book an interview.</summary>
    public static readonly StaffRole[] StaffOrder = { StaffRole.Cleaner, StaffRole.Repairer, StaffRole.RareAttendant };
    /// <summary>Usual hire price by role, paid once: Archive 250, Main hall 70, Rare Books 450. Applicants ask
    /// around this and can be talked down a little in the interview.</summary>
    public static readonly int[] StaffHireFee = { 250, 70, 450 };
    /// <summary>Upgrade prices by role and level. Each level makes them faster and need less help
    /// (and the archivist and cleaner carry one more book per trip).</summary>
    public static readonly int[][] StaffUpgradeCosts = { new[] { 150, 300 }, new[] { 50, 100 }, new[] { 450 } };
    /// <summary>What each role does, for the staff cards: "icon|text" (icons: .si-NAME in PC.uss).</summary>
    public static readonly string[][] StaffDoes =
    {
        new[] { "hammer|Repairs worn and damaged books", "books|Fetches them from the returns and the shelves", "coin|Each repair earns coins" },
        new[] { "broom|Tidies cups and open books", "shelf|Shelves good books from the return bin" },
        new[] { "key|Looks after VIP scholars in Rare Books", "book|Fetches their rare book and checks them in" },
    };
    /// <summary>Books a staff member carries per trip: one, plus one per upgrade (archivist and cleaner).</summary>
    public int StaffCarry(StaffRole r) => r == StaffRole.RareAttendant ? 1 : 1 + staffLevel[(int)r];
    public static bool StaffCarries(StaffRole r) => r != StaffRole.RareAttendant;
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
    public static int UpgradeCost(int level) => 60 + level * 40; // 60 / 100 / 140: the first one is just out of reach of the starting coins

    [NonSerialized] public List<Member> members = new List<Member>();
    /// <summary>Cards handed back, and when (they can't sign up again for two days). Saved.</summary>
    [NonSerialized] public List<ReturnedCard> returnedCards = new List<ReturnedCard>();
    [NonSerialized] public bool[] staff = new bool[3];
    [NonSerialized] public int[] upgrades = new int[9];
    public bool Hired(StaffRole r) => staff[(int)r];
    /// <summary>Upgrade level of each staff member (0 = as hired).</summary>
    [NonSerialized] public int[] staffLevel = new int[3];
    public int StaffLevel(StaffRole r) => staffLevel[(int)r];
    /// <summary>Work-speed multiplier from upgrades (1 = as hired).</summary>
    public float StaffWorkFactor(StaffRole r) => (1f + 0.35f * staffLevel[(int)r]) * StaffMoodFactor(r) * (StaffRested(r) ? 1.25f : 1f);

    // ---------------- Staff mood ----------------
    /// <summary>How each hire feels about the job, 0 (fed up) to 5 (happy). Saved. Starts at 3.</summary>
    [NonSerialized] public float[] staffMood = { 3f, 3f, 3f };
    /// <summary>The day a paid fix pays off: they come in rested and work faster that day.</summary>
    [NonSerialized] public int[] staffRestedDay = { 0, 0, 0 };
    /// <summary>Happy staff work faster, fed-up ones slack off: 0.7x at 0, 1x at 3, 1.2x at 5.</summary>
    public float StaffMoodFactor(StaffRole r) => 0.7f + 0.1f * staffMood[(int)r];
    public bool StaffRested(StaffRole r) => staffRestedDay[(int)r] == day;
    public static string MoodWord(float mood) => mood >= 4f ? "happy" : mood >= 2.5f ? "content" : mood >= 1.2f ? "grumpy" : "fed up";

    /// <summary>Treated well (or not): their mood changes, and with it how fast they work.</summary>
    public void ChangeStaffMood(StaffRole r, float by)
    {
        float before = staffMood[(int)r];
        staffMood[(int)r] = Mathf.Clamp(before + by, 0f, 5f);
        ApplyUpgrades(); // the repair desk works at the archivist's pace
        if (MoodWord(before) != MoodWord(staffMood[(int)r]))
            FindAnyObjectByType<HUD>()?.ShowBanner($"{StaffPerson[(int)r]} is {MoodWord(staffMood[(int)r])} now{(by > 0 ? "!" : ".")} " +
                (by > 0 ? "Happy staff work faster." : "Unhappy staff work slower: help them out, pay for proper fixes, have a chat."), 5f);
    }
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
    public bool NoStormingOut => difficulty == Difficulty.Cozy || Tutorial.HoldsClock; // nobody walks out on a learner
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

    public float PatienceMultiplier => (1f + 0.15f * Upgrade(UpgradeKind.ComfyQueue)) * Mode(1.5f, 1f, 0.7f) * PatienceDayFactor * DayStory.PatienceFactor * (Tutorial.HoldsClock ? 2f : 1f);
    /// <summary>Visitors get less patient as the library gets known: full patience on Days 1-2, 85% on Day 3, 75% after.</summary>
    public float PatienceDayFactor => day <= 2 ? 1f : day == 3 ? 0.85f : 0.75f;
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
    public static float Dt => Time.deltaTime * (Instance != null ? Instance.TimeScale : 1f);

    /// <summary>How fast game time runs: 0 while paused (dialogue, menus, reports), slower while a work screen is open
    /// (computer, shelf, repair desk) so looking things up costs a little time but isn't punishing, else 1.</summary>
    public float TimeScale => Paused ? 0f : PlayerLock.SlowsTime ? ScreenTimeScale : 1f;
    /// <summary>Game-time speed with a work screen open, by mode (Cozy is gentlest).</summary>
    public float ScreenTimeScale => Mode(0.3f, 0.4f, 1f); // Busy: no slow-down
    public bool Slowed => !Paused && PlayerLock.SlowsTime && ScreenTimeScale < 1f;

    // ---------------- Orders, deliveries, reservations ----------------

    /// <summary>A VIP who asked you to order a rare book from the dealer: they come back on `day` to read it
    /// in the Rare Books room (SpecialJobs). Saved, so it survives the night.</summary>
    [Serializable]
    public class VipPromise { public string key, name, title; public int day, cost; }
    [NonSerialized] public VipPromise vipPromise;
    /// <summary>The rare dealer's VIP request has been offered once (SpecialJobs guarantees it on Day 3 or 4). Saved.</summary>
    [NonSerialized] public bool rareDealerSeen;

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
        /// <summary>A special order (SpecialJobs): the extra they pay when they collect it.</summary>
        public int bonus;
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
        Mathf.Max(3, Mathf.RoundToInt(b.SaleValue * 0.6f * (express ? 1.5f : 1f) * (Instance != null ? Instance.ModePrices : 1f)));
    public int OnOrder(BookData b) { int n = 0; foreach (var o in orders) if (o.book == b) n++; return n; }
    /// <summary>Express copies still to arrive today, and when (the PC's day bar shows them).</summary>
    public IEnumerable<(BookData book, float hour)> ArrivingToday
    {
        get { foreach (var o in orders) if (o.arriveDay == day && o.arriveHour > Hour) yield return (o.book, o.arriveHour); }
    }

    /// <summary>Standard orders arrive next morning; express ones a couple of hours later today.</summary>
    /// <summary>Orders placed since the game started (the tutorial watches for your first).</summary>
    [NonSerialized] public int OrdersPlaced;

    public bool OrderBook(BookData b, bool express)
    {
        if (b == null || BookRequest.IsRare(b)) return false; // one copy of each rare book, ever (the dealer sells it once)
        int price = OrderPrice(b, express);
        if (!TrySpend(price, "Book orders")) return false;
        RequestLog.Money("You", $"{(express ? "Express order" : "Ordered")} \"{b.Title}\" ({(express ? "arrives in 2 hours" : "arrives tomorrow")})", express ? "Express order" : "Book order", -price);
        OrdersPlaced++;
        AddToLibrary(b); // a requested new title becomes part of the library
        bool sameDay = express && CurrentPhase != Phase.Closed && Hour + ExpressHours < closeHour;
        orders.Add(new Order { book = b, arriveDay = sameDay ? day : day + 1, arriveHour = sameDay ? Hour + ExpressHours : openHour });
        foreach (var r in reservations) if (r.title == b.Title) r.ordered = true;
        return true;
    }

    /// <summary>A rare book lent out for the night (SpecialJobs rare loan): it comes back on the book cart tomorrow
    /// morning, free. Saved like any order, so a quit overnight can't lose it.</summary>
    public void ReturnTomorrow(BookData b)
    {
        if (b != null) orders.Add(new Order { book = b, arriveDay = day + 1, arriveHour = openHour });
    }

    /// <summary>What the rare dealer asks for a rare book (a third of its value).</summary>
    public static int RarePrice(BookData b) =>
        Mathf.Max(20, Mathf.RoundToInt(b.BaseValue / 3f * (Instance != null ? Instance.ModePrices : 1f)));

    /// <summary>Orders a rare book from the dealer (no express, any time of day): it joins Rare Books for good,
    /// and arrives on the book cart tomorrow morning.</summary>
    public bool BuyRare(BookData b)
    {
        if (b == null || !RareFinds.Contains(b) || !TrySpend(RarePrice(b), "Rare dealer")) return false;
        RequestLog.Money("You", $"Ordered the rare \"{b.Title}\" from the dealer (arrives tomorrow morning)", "Rare book", -RarePrice(b));
        AddToLibrary(b);
        orders.Add(new Order { book = b, arriveDay = day + 1, arriveHour = openHour });
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
    /// <summary>Shuffles the order of the first week's daily twists (DayStory). 0 = not picked yet.</summary>
    [NonSerialized] public int storySeed;
    public bool EndingDue => day >= EndingDay && !endingSeen;

    /// <summary>The Dean's goal for the first week: this reputation by the end of Day 5 (Cozy / Standard / Busy).</summary>
    public static readonly int[] ReputationGoals = { 60, 100, 125 }; // Cozy, Standard, Busy (Busy: about the most a strong player can earn by Day 5)

    /// <summary>The second week: the board's "one more season", set in the Dean's farewell. Ends after this day,
    /// with a letter from the Dean the next morning; after that every day is open-ended (random events).</summary>
    public const int SecondEndingDay = 10;
    public static readonly int[] SecondReputationGoals = { 100, 150, 200 }; // Cozy, Standard, Busy
    public int SecondGoal => SecondReputationGoals[(int)difficulty];
    [NonSerialized] public bool secondEndingSeen;
    /// <summary>Days 6 to 10, after the farewell and before the Dean's letter.</summary>
    public bool SecondWeek => endingSeen && !secondEndingSeen && day > EndingDay && day <= SecondEndingDay;

    /// <summary>Today's target: the first week's, or the second week's.</summary>
    public int ReputationGoal => SecondWeek ? SecondGoal : ReputationGoals[(int)difficulty];
    /// <summary>The day the current goal is due (5, or 10 in the second week).</summary>
    public int GoalDay => SecondWeek ? SecondEndingDay : EndingDay;
    /// <summary>Still working towards a goal: the first week (until the farewell) or the second (until Day 10).</summary>
    public bool GoalActive => (!endingSeen && day <= EndingDay) || SecondWeek;
    /// <summary>How the week went against its goal: 2 = reached, 1 = close (60% or more), 0 = short of it.</summary>
    public int GoalTier => TierFor(ReputationGoal);
    public int TierFor(int goal) => Reputation >= goal ? 2 : Reputation >= Mathf.CeilToInt(goal * 0.6f) ? 1 : 0;
    /// <summary>Something happens in the interview room before the next day (the ending, or applicants).</summary>
    public bool MorningVisitDue => EndingDue || interviews.Count > 0;

    /// <summary>Roles with applicants coming in tomorrow morning, before the library opens.</summary>
    [NonSerialized] public List<int> interviews = new List<int>();
    /// <summary>The hire price paid when each interview was booked (what isn't spent comes back after it). Saved.</summary>
    [NonSerialized] public int[] hirePaid = new int[3];
    public bool InterviewBooked(StaffRole r) => interviews.Contains((int)r);

    /// <summary>Staff button: books an interview for tomorrow morning (paying the full hire price now; it can't be
    /// cancelled, and whatever the hire doesn't cost comes back after it), or upgrades a hire.</summary>
    public void ToggleStaff(StaffRole r)
    {
        int i = (int)r;
        if (staff[i]) { UpgradeStaff(r); return; }
        if (!StaffUnlocked(r)) return;
        var hud = FindAnyObjectByType<HUD>();
        if (interviews.Contains(i)) { if (hud != null) hud.ShowBanner($"The {StaffNames[i]} interview is booked: applicants come in tomorrow morning.", 4f); return; }
        int fee = StaffHireFee[i];
        if (!TrySpend(fee, "Staff hires")) { if (hud != null) hud.ShowBanner($"You need {fee} coins to book the {StaffNames[i]} interview.", 4f); return; }
        hirePaid[i] = fee;
        interviews.Add(i);
        RequestLog.Money(StaffNames[i], $"Hire budget for the {StaffNames[i]} interview", "Staff hire", -fee);
        if (hud != null) hud.ShowBanner($"Interview booked: {fee} coins set aside for the {StaffNames[i]}", 6f); // one sentence: one notice card
    }

    /// <summary>What a staff row shows (PC Shop and Day Report share it).</summary>
    public (string title, string sub, string button, bool enabled) StaffRow(StaffRole r)
    {
        int i = (int)r;
        if (staff[i])
        {
            int cost = StaffUpgradeCost(r);
            string lvl = $"Level {staffLevel[i] + 1}/{StaffUpgradeCosts[i].Length + 1}";
            string mood = $"Mood: {MoodWord(staffMood[i])}" + (StaffRested(r) ? ", rested (faster today)" : "");
            return ($"{StaffNames[i]}: {StaffPerson[i]}", $"{StaffInfo[i]}  {lvl}.  {mood}.",
                    cost < 0 ? "Max level" : $"Upgrade {cost}", cost >= 0 && Coins >= cost);
        }
        if (InterviewBooked(r))
            return (StaffNames[i], $"Interview booked: {hirePaid[i]} coins set aside. Two applicants come in tomorrow morning; what you don't spend comes back.", "Booked", false);
        if (!StaffUnlocked(r)) return (StaffNames[i], StaffInfo[i], $"Day {StaffUnlockDay[i]}", false);
        return (StaffNames[i], $"{StaffInfo[i]}  Hire: {StaffHireFee[i]} coins, paid when you book the interview. Haggle in it and the rest comes back.",
                Coins >= StaffHireFee[i] ? $"Interview ({StaffHireFee[i]})" : $"Need {StaffHireFee[i]}", Coins >= StaffHireFee[i]);
    }

    /// <summary>Makes a hire faster (and needing less help). False if maxed or too expensive.</summary>
    public bool UpgradeStaff(StaffRole r)
    {
        int cost = StaffUpgradeCost(r);
        if (cost < 0 || !TrySpend(cost, "Staff training")) return false;
        staffLevel[(int)r]++;
        RequestLog.Money(StaffPerson[(int)r], $"{StaffNames[(int)r]} trained to level {staffLevel[(int)r] + 1}", "Staff upgrade", -cost);
        ApplyUpgrades();
        var hud = FindAnyObjectByType<HUD>();
        if (hud != null) hud.ShowBanner($"{StaffPerson[(int)r]} is now level {staffLevel[(int)r] + 1}: faster, needs less help" +
                                        (StaffCarries(r) ? $", and carries {StaffCarry(r)} books per trip." : "."), 4f);
        return true;
    }

    public bool BuyUpgrade(UpgradeKind u)
    {
        int lvl = upgrades[(int)u];
        if (!UpgradeUnlocked(u) || lvl >= MaxUpgrade || !TrySpend(UpgradeCost(lvl), "Upgrades")) return false;
        upgrades[(int)u]++;
        RequestLog.Money("You", $"Bought {UpgradeNames[(int)u]} (level {upgrades[(int)u]})", "Upgrade", -UpgradeCost(lvl));
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
        Today = new DayStats { coinsAtStart = Coins };
        ReputationAtStart = Reputation;
        PickPopularBooks();
        // Morning wages, skipping anyone already paid for today (hired last night).
        int wages = 0;
        for (int i = 0; i < staff.Length; i++) if (staff[i] && wagePaidFor[i] < day) wages += StaffWage[i];
        if (wages > 0)
        {
            if (Coins >= wages)
            {
                AddCoins(-wages);
                Today.Spend("Staff wages", wages);
                RequestLog.Money("Staff", "Morning wages", "Wages", -wages);
                for (int i = 0; i < staff.Length; i++) if (staff[i]) wagePaidFor[i] = day;
            }
            else { for (int i = 0; i < staff.Length; i++) if (wagePaidFor[i] < day) staff[i] = false; } // can't pay: they quit
        }
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
    [Serializable] class SaveData { public int day, coins, reputation, difficulty = 1; public bool[] staff; public int[] upgrades; public List<BookSave> books = new List<BookSave>(); public List<Member> members = new List<Member>(); public List<Reservation> reservations = new List<Reservation>(); public List<RequestLog.Entry> log = new List<RequestLog.Entry>(); public List<int> interviews = new List<int>(); public bool endingSeen, secondEndingSeen; public float playSeconds; public int[] staffLevel; public List<string> added = new List<string>(); public int storySeed; public float[] staffMood; public int[] staffRestedDay; public VipPromise vip; public int[] hirePaid; public bool rareDealerSeen; public List<ReturnedCard> returnedCards; }

    public void Save()
    {
        var data = new SaveData { day = day, difficulty = (int)difficulty, coins = Coins, reputation = Reputation, staff = staff, upgrades = upgrades, members = members, reservations = reservations, log = RequestLog.All, interviews = interviews, endingSeen = endingSeen, secondEndingSeen = secondEndingSeen, playSeconds = playSeconds, staffLevel = staffLevel, added = addedTitles, storySeed = storySeed, staffMood = staffMood, staffRestedDay = staffRestedDay, vip = vipPromise, hirePaid = hirePaid, rareDealerSeen = rareDealerSeen, returnedCards = returnedCards };
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
        returnedCards = data.returnedCards ?? new List<ReturnedCard>();
        if (data.reservations != null) { reservations = data.reservations; foreach (var r in reservations) r.title = Renamed(r.title); }
        if (data.interviews != null) interviews = data.interviews;
        hirePaid = data.hirePaid != null && data.hirePaid.Length == 3 ? data.hirePaid : new int[3];
        endingSeen = data.endingSeen;
        secondEndingSeen = data.secondEndingSeen;
        // The save is from this morning; the half-minute key may be later (quit mid-day).
        playSeconds = Mathf.Max(data.playSeconds, PlayerPrefs.GetFloat(PlayTimeKey, 0f));
        vipPromise = data.vip != null && !string.IsNullOrEmpty(data.vip.title) ? data.vip : null;
        rareDealerSeen = data.rareDealerSeen;
        storySeed = data.storySeed;
        if (data.staffLevel != null && data.staffLevel.Length == 3) staffLevel = data.staffLevel;
        if (data.staffMood != null && data.staffMood.Length == 3) staffMood = data.staffMood;
        if (data.staffRestedDay != null && data.staffRestedDay.Length == 3) staffRestedDay = data.staffRestedDay;
        // Titles you ordered in earlier days are part of the library now.
        if (data.added != null) foreach (var old in data.added) { string t = Renamed(old); var nb = NewTitles.Find(b => b.Title == t) ?? RareFinds.Find(b => b.Title == t); if (nb != null) AddToLibrary(nb); }
        RequestLog.Reset(data.log);
        foreach (var s in data.books)
        {
            string t = Renamed(s.title);
            var b = Books.Find(x => x.Title == t);
            if (b == null) continue;
            b.GoodCopies = s.good; b.WornCopies = s.worn; b.DamagedCopies = s.damaged;
        }
    }

    /// <summary>Old title -> new title, for books renamed in the BookDatabase, so older saves keep their copies.</summary>
    static readonly Dictionary<string, string> RenamedTitles = new Dictionary<string, string>
    {
        { "The Clockwork Atlas", "The Clockwork Compass" },
        { "The Salt Road", "Camels and Caravans" },
    };
    static string Renamed(string title) => title != null && RenamedTitles.TryGetValue(title, out var now) ? now : title;

    public bool HasSave => PlayerPrefs.HasKey(SaveKey);

    /// <summary>Back to the title screen (progress since this morning's save is lost).</summary>
    public void ReturnToMenu()
    {
        FlushPlaytime(); // the title screen shows it
        PlayerLock.Reset();
        UnityEngine.SceneManagement.SceneManager.LoadScene(gameObject.scene.name);
    }

    [ContextMenu("Delete Save")]
    public void DeleteSave() { PlayerPrefs.DeleteKey(SaveKey); PlayerPrefs.DeleteKey(PlayTimeKey); playSeconds = 0f; Tutorial.ResetProgress(); Debug.Log("[Game] Save deleted."); } // a new game: tutorial and tips again

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
        if (db != null) { Books = db.CreateRuntimeCopy(); NewTitles = db.CreateNewTitlesCopy(); RareFinds = db.CreateRareFindsCopy(); }
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
        Today = new DayStats { coinsAtStart = Coins };
        ApplyInterviewHire();
        ReputationAtStart = Reputation;
        if (GetComponent<DayStory>() == null) gameObject.AddComponent<DayStory>(); // the first week's twists and story
        if (GetComponent<SpecialJobs>() == null) gameObject.AddComponent<SpecialJobs>(); // bigger jobs once staff do the busywork
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
            StaffInterview.EndingGoal = ReputationGoal;
            StaffInterview.EndingTier = GoalTier;
            StaffInterview.EndingNextGoal = SecondGoal;
            StaffInterview.EndingPlaySeconds = playSeconds;
            StaffInterview.EndingStaff = names;
        }

        PlayerLock.Lock(this);
        DayNightCycle.ForceDay = true; // it's the next morning in there, not last night
        List<StaffRole> hired = null;
        StaffInterview.Queue = roles;
        int budget = Coins;
        foreach (var r in roles) budget += hirePaid[(int)r]; // set aside when the interviews were booked
        StaffInterview.Coins = budget;
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
        var morningHud = FindAnyObjectByType<HUD>();
        foreach (var r in roles)
        {
            int i = (int)r, paid = hirePaid[i];
            hirePaid[i] = 0;
            bool got = hired.Contains(r);
            // The price you agreed in the interview (or what they asked), paid once.
            var parts = PlayerPrefs.GetString("HiredStats_" + r, "").Split(',');
            int price = !got ? 0 : parts.Length == 4 && int.TryParse(parts[3], out int p) ? p : StaffHireFee[i];
            if (paid <= 0)
            {
                // Booked before hire budgets existed (an older save): pay now, as it used to work.
                if (got && TrySpend(price, "Staff hires")) RequestLog.Money(StaffPerson[i], $"Hired as {StaffNames[i]}", "Staff hire", -price);
                else if (got) { got = false; morningHud?.ShowBanner($"Not enough coins to hire the {StaffNames[i]} ({price}).", 5f); }
            }
            else
            {
                int back = paid - price;
                if (back > 0)
                {
                    AddCoins(back);
                    RequestLog.Money(got ? StaffPerson[i] : StaffNames[i], "Hire budget returned", "Hire refund", back);
                    morningHud?.ShowBanner(got ? $"Hiring refund: +{back} coins ({StaffPerson[i]} agreed to {price} of {paid})"
                                               : $"Hiring refund: +{back} coins (no {StaffNames[i]} hired)", 7f);
                }
            }
            if (got) { staff[i] = true; staffLevel[i] = 0; staffMood[i] = 3f; staffRestedDay[i] = 0; }
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

    // ---------------- Playtime ----------------

    /// <summary>Real seconds spent playing this save (not the title screen or pause, not with the window in the
    /// background). Saved with the game, and every half minute under its own key so quitting mid-day loses little.</summary>
    [NonSerialized] public float playSeconds;
    const string PlayTimeKey = "PlayTime";
    float playFlushAt;

    void CountPlaytime()
    {
        if (!MainMenuUI.Playing || !Application.isFocused) return;
        playSeconds += Time.unscaledDeltaTime;
        if (Time.unscaledTime >= playFlushAt) FlushPlaytime();
    }

    void FlushPlaytime()
    {
        playFlushAt = Time.unscaledTime + 30f;
        PlayerPrefs.SetFloat(PlayTimeKey, playSeconds);
        PlayerPrefs.Save();
    }

    void OnApplicationQuit() => FlushPlaytime();

    /// <summary>"38 min", "1 h 24 m", or "under a minute".</summary>
    public static string FormatPlaytime(float seconds)
    {
        int min = Mathf.FloorToInt(seconds / 60f);
        if (min < 1) return "under a minute";
        return min < 60 ? $"{min} min" : $"{min / 60} h {min % 60} m";
    }

    void Update()
    {
        CountPlaytime();
        if (CurrentPhase == Phase.Closed || Paused) return;
        if (Tutorial.HoldsClock) return; // the guided first day: the clock waits for you
        if (orders.Count > 0) DeliverDue();
        float hoursPerSecond = (closeHour - openHour) / Mathf.Max(0.1f, realMinutesPerDay * 60f);
        Hour = Mathf.Min(closeHour, Hour + hoursPerSecond * Time.deltaTime * TimeScale); // slower while a work screen is open

        if (CurrentPhase == Phase.Open && Hour >= closingHour) SetPhase(Phase.Closing);
        else if (CurrentPhase == Phase.Closing && Hour >= closeHour) SetPhase(Phase.Closed);
    }

#if UNITY_EDITOR
    /// <summary>Dev Tools only: jump the clock (anything due before then happens right away).</summary>
    public void DevSetHour(float hour)
    {
        if (CurrentPhase == Phase.Closed) return;
        Hour = Mathf.Clamp(hour, openHour, closeHour);
        if (CurrentPhase == Phase.Open && Hour >= closingHour) SetPhase(Phase.Closing);
        if (CurrentPhase == Phase.Closing && Hour >= closeHour) SetPhase(Phase.Closed);
    }
#endif

    void SetPhase(Phase phase)
    {
        CurrentPhase = phase;
        if (phase == Phase.Closing && closingBell != null)
            AudioSource.PlayClipAtPoint(closingBell, Camera.main ? Camera.main.transform.position : Vector3.zero, 0.4f);
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
        if (amount > 0) Today.coinsIn += amount; else Today.coinsOut -= amount; // the day's ledger
        CoinsChanged?.Invoke(Coins, amount);
    }

    /// <param name="what">What the money went on, for the Day Report.</param>
    public bool TrySpend(int amount, string what = "Other")
    {
        if (amount > Coins) return false;
        AddCoins(-amount);
        Today.Spend(what, amount);
        return true;
    }

    /// <summary>Call after changing any book's stock or condition so UIs refresh.</summary>
    public void NotifyBooksChanged() => BooksChanged?.Invoke();
}
