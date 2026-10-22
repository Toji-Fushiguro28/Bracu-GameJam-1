using System.Collections.Generic;
using System.Linq;
using UnityEngine;

/// <summary>
/// Jobs for the librarian once staff take care of the busywork (any hire, from Day 2): one a day, offered by
/// someone at the desk, which you can take or turn down.
///  Special order - a regular wants a book in by tomorrow and pays extra when they collect it (a reservation with a bonus).
///  VIP request   - a scholar wants a rare book in your hands when they come back at 3:00 PM, and reads it in the Rare
///                  Books room. Sometimes the library doesn't hold it: you order it from the rare dealer (PC Catalogue,
///                  arrives tomorrow) and they come back tomorrow afternoon to read it, repaying you. Not ordered: -3
///                  reputation. Not in your hands: they read another rare book instead.
///  Book club     - a club needs 3 copies of a book on the shelves at 4:00 PM, and buys them all.
/// The second week brings four more, one a day, staff or not (Day 10 and after: any job at random):
///  Day 6 Collector    - a VIP's rare book is put on the repair desk damaged: mend it and have it in your hands at
///                       3:00 PM. Perfect: full fee; mended but worn: half; not ready: -2 reputation.
///  Day 7 Reading list - a teacher needs a book on each of 3 subjects on the shelves at 4:00 PM, and buys one of each.
///  Day 8 Rare loan    - a museum borrows a rare book for the night: 100 coins now, back on the cart tomorrow,
///                       sometimes scuffed (then it's in the returns bin for the repair desk).
///  Day 9 Restoration  - a visitor's own damaged book goes on the repair desk; they collect it at 4:30 PM and pay
///                       by how well it's mended. It's theirs: it never joins the library.
/// The rare dealer's VIP request is guaranteed once, staff or not: maybe on Day 3, always on Day 4 if it hasn't come yet.
/// Accepted jobs show in the Reserved panel (NotebookHUD). Added to the GameManager at runtime.
/// </summary>
public class SpecialJobs : MonoBehaviour
{
    public static SpecialJobs Instance { get; private set; }

    enum Job { None, Order, Vip, BookClub, Collector, ReadingList, RareLoan, Restoration }

    const float VipDue = 15f, ClubAt = 16f, CollectorDue = 15f, ListAt = 16f, OwnerBack = 16.5f;
    const int ClubCopies = 3, VipReward = 40, ClubBonus = 15, CollectorFee = 60, ListBonus = 20, LoanFee = 100, RestoreFee = 35;

    /// <summary>The second week's jobs, one a day (a day that can't run its job falls back to the usual pick).</summary>
    static Job Planned(int day) => day == 6 ? Job.Collector : day == 7 ? Job.ReadingList : day == 8 ? Job.RareLoan : day == 9 ? Job.Restoration : Job.None;
    static readonly Job[] SecondWeek = { Job.Collector, Job.ReadingList, Job.RareLoan, Job.Restoration };

    bool Possible(Job j)
    {
        switch (j)
        {
            case Job.Collector: return GM.RareOpen && RareBook() != null && RepairDesk.Instance != null;
            case Job.ReadingList: return ListTopics() != null;
            case Job.RareLoan: return GM.RareOpen && RareBook() != null && loanBook == null;
            case Job.Restoration: return RestoreBook() != null && RepairDesk.Instance != null;
            default: return false;
        }
    }

    static GameManager GM => GameManager.Instance;

    Job job;
    float offerAt;
    bool offered, accepted, done;
    BookData book;
    int bonus;
    string vipKey, vipName;
    bool buying;      // today is the VIP's return for a rare book you were asked to order
    float vipAt = VipDue; // when the VIP comes back today
    bool taskShown;
    bool forceDealer; // today is the guaranteed rare dealer request

    /// <summary>The rare book a VIP asked you to order, while the dealer still has it (else null).</summary>
    public BookData WantedRare
    {
        get
        {
            var p = GM != null ? GM.vipPromise : null;
            return p != null ? GM.RareFinds.Find(b => b.Title == p.title) : null;
        }
    }
    public string VipName => GM != null && GM.vipPromise != null ? GM.vipPromise.name : vipName;

    /// <summary>A title in the library or still at the rare dealer.</summary>
    static BookData Title(string t) => GM.FindTitle(t) ?? GM.RareFinds.Find(b => b.Title == t);

    void Awake() => Instance = this;

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
        if (GM != null) GM.DayStarted -= SetUpDay;
        if (RepairDesk.Instance != null) RepairDesk.Instance.Finished -= OnRepaired;
    }

    void Start()
    {
        if (GM == null) return;
        GM.DayStarted += SetUpDay;
        SetUpDay();
    }

    /// <summary>Has anyone on the staff (so you've time for bigger jobs)?</summary>
    static bool HasStaff => GM.staff.Any(s => s);

    void SetUpDay()
    {
        ClearDeskJob(job == Job.Restoration); // anything left from yesterday
        job = Job.None;
        offered = accepted = done = false;
        orderTask = null;
        book = null;
        vipKey = vipName = null;
        buying = taskShown = forceDealer = false;
        vipAt = VipDue;
        holdSaid = remindSaid = false;
        dueNow = false;
        topics = null;
        ownerKey = ownerName = null;
        if (GM == null) return;

        // A VIP you promised a rare book to yesterday comes back this afternoon to read it.
        var p = GM.vipPromise;
        if (p != null && p.day < GM.day) GM.vipPromise = p = null; // a day that got missed
        if (p != null && p.day == GM.day && Title(p.title) != null)
        {
            job = Job.Vip;
            offered = accepted = buying = true;
            book = Title(p.title);
            vipKey = p.key; vipName = p.name;
            vipAt = Random.Range(13.5f, 15.5f);
            return;
        }
        // The rare dealer comes once whether or not you have staff: a coin flip on Day 3, for sure on Day 4.
        if (!GM.rareDealerSeen && GM.RareOpen && GM.vipPromise == null && GM.RareFinds.Count > 0
            && (GM.day == GameManager.RareDay + 1 || (GM.day == GameManager.RareDay && Random.value < 0.5f)))
        {
            job = Job.Vip;
            forceDealer = true;
            offerAt = Random.Range(10f, 11.75f);
            return;
        }
        // Days 6-9: the second week's jobs, one each, staff or not.
        var planned = Planned(GM.day);
        if (planned != Job.None && Possible(planned))
        {
            job = planned;
            offerAt = Random.Range(10f, 11.75f);
            return;
        }
        if (GM.day < 2 || !HasStaff) return;

        var options = new List<Job>();
        if (OrderBook() != null) options.Add(Job.Order);
        if (GM.RareOpen && (RareBook() != null || (RareFind() != null && GM.vipPromise == null))) options.Add(Job.Vip);
        if (ClubBook() != null) options.Add(Job.BookClub);
        if (GM.day >= 10) foreach (var j in SecondWeek) if (Possible(j)) options.Add(j); // from Day 10: all of them
        if (options.Count == 0) return;
        job = options[Random.Range(0, options.Count)];
        offerAt = Random.Range(10f, 11.75f);
    }

    // ---------------- Picking the book ----------------

    /// <summary>Something they can't get anywhere: a title the library hasn't got yet, or one that's sold out.</summary>
    static BookData OrderBook()
    {
        var pool = GM.NewTitles.Where(b => BookRequest.Available(b) && !Reserved(b)).ToList();
        if (pool.Count == 0)
            pool = GM.Books.Where(b => b.Sellable && !BookRequest.IsRare(b) && BookRequest.Available(b) && !BookRequest.InStock(b) && GM.OnOrder(b) == 0 && !Reserved(b)).ToList();
        return pool.Count > 0 ? pool[Random.Range(0, pool.Count)] : null;
    }

    /// <summary>A rare book on the shelf in a fit state to hand over.</summary>
    /// <summary>A rare book the library could buy from the dealer, or null.</summary>
    static BookData RareFind() => GM.RareFinds.Count > 0 ? GM.RareFinds[Random.Range(0, GM.RareFinds.Count)] : null;

    static BookData RareBook()
    {
        var pool = GM.Books.Where(b => BookRequest.IsRare(b) && b.GoodCopies + b.WornCopies > 0).ToList();
        return pool.Count > 0 ? pool[Random.Range(0, pool.Count)] : null;
    }

    /// <summary>A book for sale in the main hall that you'll probably need to order more of.</summary>
    static BookData ClubBook()
    {
        var pool = GM.Books.Where(b => b.Sellable && b.Format == BookFormat.Book && !BookRequest.IsRare(b) && !BookRequest.IsArchive(b)
                                       && BookRequest.Available(b) && b.BaseValue <= 30).ToList();
        return pool.Count > 0 ? pool[Random.Range(0, pool.Count)] : null;
    }

    static bool Reserved(BookData b) => GM.reservations.Exists(r => r.title == b.Title);
    static int OnShelf(BookData b) => b == null ? 0 : b.GoodCopies + b.WornCopies;

    // ---------------- The day ----------------

    BookData orderTask;

    void Update()
    {
        if (orderTask != null && GM != null && (GM.OnOrder(orderTask) > 0 || BookRequest.InStock(orderTask)))
        {
            RequestLog.TaskDone("job-order", "Ordered");
            orderTask = null;
        }
        if (GM == null) return;
        if (loanBook != null && GM.CurrentPhase == GameManager.Phase.Open) CheckLoanBack();
        // Closing time: a visitor's own book still on the repair desk goes home with them (it never joins the library).
        if (deskJob != null && GM.CurrentPhase != GameManager.Phase.Open) ClearDeskJob(job == Job.Restoration);
        if (job == Job.None || GM.CurrentPhase != GameManager.Phase.Open) return;
        float h = GM.Hour;
        if (buying && !taskShown)
        {
            taskShown = true; // added here, after the new day's notebook is set up
            RequestLog.Task("job-vip", vipName, $"Take {BookRequest.H($"\"{book.Title}\"")} off the book cart and {Red("keep it in your hands")}. {vipName} comes back around {GameManager.FormatHour(vipAt)}: talk to them with the book in your hands.");
        }
        if (!offered && h >= offerAt) { if (Offer() || h >= offerAt + 1.5f) offered = true; return; }
        if (!accepted || done) return;
        if (job == Job.Vip || job == Job.Collector) GuideVip();
        if (job == Job.Vip && h >= vipAt) { if (VipReturns() || h >= vipAt + 1.5f) done = true; }
        if (job == Job.BookClub && h >= ClubAt) { done = true; BookClubMeets(); }
        if (job == Job.Collector && h >= vipAt) { if (CollectorReturns() || h >= vipAt + 1.5f) { done = true; ClearDeskJob(false); } }
        if (job == Job.ReadingList && (h >= ListAt || dueNow)) { done = true; ListCheck(); }
        if (job == Job.Restoration && (h >= OwnerBack || dueNow)) { if (OwnerReturns() || h >= OwnerBack + 0.75f) { done = true; ClearDeskJob(true); } }
    }

    bool dueNow; // Dev Tools: today's deadline is now

#if UNITY_EDITOR
    /// <summary>Dev Tools only: one of the second week's jobs is offered at the desk now, replacing today's job.
    /// 0 Collector, 1 Reading list, 2 Rare loan, 3 Restoration.</summary>
    public void DevJobNow(int which)
    {
        ClearDeskJob(job == Job.Restoration);
        job = SecondWeek[Mathf.Clamp(which, 0, SecondWeek.Length - 1)];
        offered = accepted = done = buying = taskShown = forceDealer = false;
        book = null;
        offerAt = GM.Hour;
        vipAt = VipDue;
        holdSaid = remindSaid = false;
    }

    /// <summary>Dev Tools only: today's deadline (Collector, reading list, restoration owner) is now.</summary>
    public void DevJobDueNow()
    {
        if (!accepted || done) return;
        if (job == Job.Collector) vipAt = GM.Hour;
        if (job == Job.ReadingList || job == Job.Restoration) dueNow = true;
    }
#endif

#if UNITY_EDITOR
    /// <summary>Dev Tools only: a VIP comes to the desk now with a rare-book request, replacing today's job.
    /// dealer: the library hasn't got the book, so it's ordered from the rare dealer. Needs Rare Books open.</summary>
    public void DevVipNow(bool dealer)
    {
        job = Job.Vip;
        offered = accepted = done = buying = taskShown = false;
        book = null;
        forceDealer = dealer;
        offerAt = GM.Hour;
        vipAt = VipDue;
        holdSaid = remindSaid = false;
    }

    /// <summary>Dev Tools only: the VIP you said yes to comes back now instead of at 3:00 PM.</summary>
    public void DevVipBackNow() { if (job == Job.Vip && accepted) vipAt = GM.Hour; }
#endif

    /// <summary>Red, for what matters most (the notebook's warning red).</summary>
    static string Red(string s) => $"<color=#AA3C28>{s}</color>";

    // Play testers missed that the VIP's book has to stay in your hands: say so when you pick it up,
    // and remind you half an hour before they're back if you aren't holding it.
    bool holdSaid, remindSaid;

    void GuideVip()
    {
        if (book == null) return;
        string t = BookRequest.H($"\"{book.Title}\"");
        if (Holding(book) >= 0)
        {
            if (!holdSaid) { holdSaid = true; FindAnyObjectByType<HUD>()?.ShowBanner($"{Red("Keep holding")} {t} until {vipName} comes back at {GameManager.FormatHour(vipAt)}. Don't put it down or hand it to anyone else.", 7f); }
            return;
        }
        holdSaid = false;
        if (!remindSaid && GM.Hour >= vipAt - 0.5f)
        {
            remindSaid = true;
            FindAnyObjectByType<HUD>()?.ShowBanner($"{vipName} is back at {GameManager.FormatHour(vipAt)}! Go and get {t} and {Red("keep it in your hands")}.", 7f);
        }
    }

    bool Offer()
    {
        var sp = CustomerSpawner.Instance;
        if (sp == null) return true;
        DayStory.Visit visit;
        switch (job)
        {
            case Job.Order: visit = OrderVisit(); break;
            case Job.Vip: visit = VipVisit(); break;
            case Job.BookClub: visit = ClubVisit(); break;
            case Job.Collector: visit = CollectorVisit(); break;
            case Job.ReadingList: visit = ListVisit(); break;
            case Job.RareLoan: visit = LoanVisit(); break;
            case Job.Restoration: visit = RestoreVisit(); break;
            default: visit = null; break;
        }
        if (visit == null) { job = Job.None; return true; }
        // The book club organiser, the teacher and the restoration visitor can be anyone; the others wait a while
        // for the right person to be free.
        bool anyone = job == Job.BookClub || job == Job.ReadingList || job == Job.Restoration;
        return sp.SpawnStory(visit, anyone || GM.Hour >= offerAt + 0.75f) != null;
    }

    static string NameOf(Customer c) => Members.TryProfile(c.profileKey, out var p) ? p.name : "A visitor";
    static string FirstNameOf(Customer c) => NameOf(c).Split(' ')[0];

    // ---------------- Special order ----------------

    DayStory.Visit OrderVisit()
    {
        book = OrderBook();
        if (book == null) return null;
        bonus = Mathf.Clamp(15 + book.BaseValue / 2, 15, 40);
        var b = book;
        // Regulars with no reservation already (someone who'll come back for it).
        var keys = Members.All.Where(m => !m.key.Contains("Professor") && !m.key.Contains("Dean") && GM.ReservationFor(m.key) == null)
                              .Select(m => m.key).OrderBy(_ => Random.value).ToArray();
        return new DayStory.Visit
        {
            prefabs = keys, teaser = "Could I ask you a favour?", atDesk = true,
            talk = c => new DayStory.Script()
                .Say($"Could I ask a favour? I've been after <color=#D67840>\"{b.Title}\"</color> by {b.Author} for months. Nobody in town has it.")
                .Say($"If you could have it here for me tomorrow, I'll pay {bonus} coins on top. For the trouble.")
                .Choose("I'll get it in for you.", "yes", "Sorry, I can't promise that.", "no")
                .Say("Wonderful. Tomorrow, then. I'm holding you to it!", "yes", DialogueAsset.End)
                .Say("Fair enough. It was worth asking.", "no")
                .d,
            after = (c, last) =>
            {
                if (last != "yes") return;
                accepted = done = true; // the reservation does the rest
                GM.Reserve(c.profileKey, FirstNameOf(c), b, false, true);
                var r = GM.ReservationFor(c.profileKey);
                if (r != null) r.bonus = bonus;
                FindAnyObjectByType<HUD>()?.ShowBanner($"Special order: get <color=#D67840>\"{b.Title}\"</color> in by tomorrow (order it from the computer's Catalogue). {FirstNameOf(c)} pays +{bonus} on top.", 7f);
                orderTask = b;
                RequestLog.Task("job-order", FirstNameOf(c), $"Order {BookRequest.H($"\"{b.Title}\"")} in the Catalogue. {FirstNameOf(c)} collects it tomorrow and pays +{bonus} on top.");
            },
        };
    }

    // ---------------- VIP request ----------------

    DayStory.Visit VipVisit()
    {
        // Now and then (or when nothing rare is on the shelves) the book isn't in the library yet.
        var find = GM.vipPromise == null ? RareFind() : null; // one dealer order at a time
        book = RareBook();
        bool fromDealer = find != null && (forceDealer || book == null || Random.value < 0.45f);
        if (fromDealer) book = find;
        if (book == null) return null;
        var b = book;
        var profs = new[] { "PF_NPC_Professor_Hazel", "PF_NPC_Professor_Edmund", "PF_NPC_Professor_Victor", "PF_NPC_Professor_Ingrid" }
                    .OrderBy(_ => Random.value).ToArray();
        if (fromDealer) return BuyRareVisit(b, profs);
        return new DayStory.Visit
        {
            prefabs = profs, teaser = "A word, if you have a moment.", atDesk = true, vip = true,
            talk = c => new DayStory.Script()
                .Say($"Forgive me. I'm told your Rare Books room has a copy of <color=#D67840>\"{b.Title}\"</color>. I've been chasing one footnote in it since the spring.")
                .Say($"I'm lecturing this morning. Could you have it in your hands when I come back at {GameManager.FormatHour(VipDue)}? I've lost a whole afternoon before, waiting on a book that was \"somewhere on the shelf\".")
                .Choose("It'll be ready.", "yes", "I'm afraid I can't.", "no")
                .Say("Thank you. Three o'clock, then.", "yes", "tip")
                .Say("Then I'll try the county archive. Good day.", "no", DialogueAsset.End)
                .Me($"Take it from the Rare Books section and {Red("keep holding it")}. {Red("Don't put it back or give it to anyone else")}. When they come back at {GameManager.FormatHour(VipDue)}, talk to them with the book {Red("in your hands")}", "tip")
                .d,
            after = (c, last) =>
            {
                if (last != "tip") { done = true; return; }
                accepted = true;
                vipKey = c.profileKey;
                vipName = NameOf(c);
                RequestLog.Task("job-vip", vipName, $"Take {BookRequest.H($"\"{b.Title}\"")} from the Rare Books section and {Red("keep it in your hands")}. {vipName} comes back at {GameManager.FormatHour(VipDue)}: talk to them with the book in your hands.");
                FindAnyObjectByType<HUD>()?.ShowBanner($"VIP request: take <color=#D67840>\"{b.Title}\"</color> from the Rare Books section and {Red("keep it in your hands")} until {vipName} comes back at {GameManager.FormatHour(VipDue)}.", 8f);
            },
        };
    }

    /// <summary>The VIP wants a rare book the library hasn't got: order it from the dealer in the PC Catalogue today
    /// (no rush, it arrives tomorrow morning) and they come back tomorrow afternoon to read it, repaying you.</summary>
    DayStory.Visit BuyRareVisit(BookData b, string[] profs)
    {
        int cost = GameManager.RarePrice(b);
        return new DayStory.Visit
        {
            prefabs = profs, teaser = "A word, if you have a moment.", atDesk = true, vip = true,
            talk = c => new DayStory.Script()
                .Say($"Good morning. There's a book called <color=#D67840>\"{b.Title}\"</color>. I've wanted to see a copy for twenty years.")
                .Say($"No library in the county has one. The rare-book dealer does: it's in your computer's Catalogue, for {cost} coins, and it would be here tomorrow morning.")
                .Say("Buy it, and I'll come in tomorrow afternoon to read it in your Rare Books room. I'll pay you back, and a little more. The book stays here, of course.")
                .Choose("I'll order it.", "yes", "That's too dear for us.", "no")
                .Say("Then I'll keep tomorrow afternoon free. Thank you.", "yes", DialogueAsset.End)
                .Say("I understand. Twenty years. I can wait a little longer.", "no")
                .d,
            after = (c, last) =>
            {
                done = true; // nothing more today: they're back tomorrow
                GM.rareDealerSeen = true; // offered, taken or not
                if (last != "yes") return;
                accepted = true;
                vipName = NameOf(c);
                GM.vipPromise = new GameManager.VipPromise { key = c.profileKey, name = vipName, title = b.Title, day = GM.day + 1, cost = cost };
                RequestLog.Task("job-vip", vipName, $"Order {BookRequest.H($"\"{b.Title}\"")} from the rare dealer in the PC Catalogue ({cost} coins, any time today). {vipName} comes back tomorrow afternoon to read it, and repays you.");
                FindAnyObjectByType<HUD>()?.ShowBanner($"VIP request: order <color=#D67840>\"{b.Title}\"</color> from the rare dealer in the PC Catalogue ({cost} coins). {vipName} comes back tomorrow afternoon to read it.", 8f);
            },
        };
    }

    /// <summary>The scholar is back (3:00 PM, or tomorrow afternoon for a book from the dealer). In your hands? They
    /// take it to the Rare Books room and read it. If not, they read another rare book instead of leaving.</summary>
    bool VipReturns()
    {
        var sp = CustomerSpawner.Instance;
        if (sp == null) return true;
        var b = book;
        var p = buying ? GM.vipPromise : null;
        bool neverOrdered = p != null && GM.RareFinds.Exists(x => x.Title == p.title);
        var visit = new DayStory.Visit
        {
            prefabs = new[] { vipKey }, name = vipName, teaser = "I'm back for the book.", atDesk = true, vip = true,
            talk = c =>
            {
                var s = new DayStory.Script();
                if (Holding(b) >= 0)
                    s.Say($"<color=#D67840>\"{b.Title}\"</color>. And in good order. You've no idea what this means.")
                     .Say(p != null ? "Here, as promised: the dealer's price, and something for your trouble." : "Here. For the library. Don't spend it all on glue.")
                     .Me("They carry it off to the Rare Books room to read");
                else if (neverOrdered)
                    s.Say("You never ordered it? I came all this way on your word.")
                     .Say("Well. I'll make do with whatever's on your rare shelves.");
                else
                    s.Say("Not in your hands? I did ask quite specifically.")
                     .Say("Never mind. I'll read something else in the Rare Books room while I'm here.");
                return s.d;
            },
            after = (c, _) =>
            {
                GM.vipPromise = null; // kept or not, the promise is settled
                int i = Holding(b);
                var carry = PlayerCarry();
                bool given = i >= 0 && carry != null;
                RequestLog.TaskDone("job-vip", given ? "Done" : neverOrdered ? "Never ordered" : "Not in your hands");
                if (given)
                {
                    var (copy, condition) = carry.TakeAt(i);
                    int reward = VipReward + (p != null ? p.cost : 0); // they repay the dealer's price
                    GM.AddCoins(reward);
                    GM.Today.Earn(GameManager.DayStats.Source.Vip, reward);
                    RequestLog.Money("VIP request", p != null ? "Rare book ordered for them (price repaid)" : "Rare book ready on time", "VIP reward", reward);
                    GM.AddReputation(2);
                    HUD.SkillPop(c.transform.position + Vector3.up * 2.2f, $"VIP request done! +{reward} coins, +2 reputation");
                    c.ReadNow(copy, condition); // off to the Rare Books room with it
                    return;
                }
                GM.AddReputation(neverOrdered ? -3 : -1);
                GM.Today.letDown++;
                HUD.SkillPop(c.transform.position + Vector3.up * 2.2f, neverOrdered ? "Never ordered: -3 reputation" : "Not in your hands: -1 reputation");
                ReadAnotherRare(c, b);
            },
        };
        return sp.SpawnStory(visit, true) != null;
    }

    /// <summary>The VIP didn't get their book: they take another rare book off the shelf and read it (or leave if none).</summary>
    static void ReadAnotherRare(Customer c, BookData not)
    {
        var pool = GM.Books.Where(x => BookRequest.IsRare(x) && x != not && BookRequest.InStock(x)).ToList();
        if (pool.Count == 0) return; // nothing to read: they leave
        var other = pool[Random.Range(0, pool.Count)];
        var cond = other.GoodCopies > 0 ? BookCondition.Good : BookCondition.Worn;
        if (!other.TakeCopy(cond)) return;
        GM.NotifyBooksChanged();
        c.ReadNow(other, cond);
    }

    static PlayerCarry PlayerCarry() => GameObject.FindWithTag("Player")?.GetComponent<PlayerCarry>();

    /// <summary>Index in your hands of a copy of it in a fit state, or -1.</summary>
    static int Holding(BookData b)
    {
        var carry = PlayerCarry();
        return carry == null || b == null ? -1 : carry.Find((x, c) => x == b && c != BookCondition.Damaged);
    }

    // ---------------- Book club ----------------

    DayStory.Visit ClubVisit()
    {
        book = ClubBook();
        if (book == null) return null;
        var b = book;
        return new DayStory.Visit
        {
            teaser = "Are you the one in charge?", atDesk = true,
            talk = c => new DayStory.Script()
                .Say("Hello! I run a little book club. Seven of us, two of whom have actually read the book.")
                .Say($"This month it's <color=#D67840>\"{b.Title}\"</color>. Could you have {ClubCopies} copies on the shelf by {GameManager.FormatHour(ClubAt)}? We'll buy them all.")
                .Choose("Leave it with me.", "yes", "Not today, sorry.", "no")
                .Say("Lovely! Four o'clock. We'll bring biscuits.", "yes", DialogueAsset.End)
                .Say("Oh well. Next month, maybe.", "no")
                .d,
            after = (c, last) =>
            {
                if (last != "yes") { done = true; return; }
                accepted = true;
                RequestLog.Task("job-club", "Book club", $"Have {ClubCopies} copies of {BookRequest.H($"\"{b.Title}\"")} on the shelf by {GameManager.FormatHour(ClubAt)}. They buy them all.");
                FindAnyObjectByType<HUD>()?.ShowBanner($"Book club at {GameManager.FormatHour(ClubAt)}: have {ClubCopies} copies of <color=#D67840>\"{b.Title}\"</color> on the shelf (you have {OnShelf(b)}). Express orders take 2 hours.", 8f);
            },
        };
    }

    /// <summary>Four o'clock: enough copies, and they buy the lot. Not enough, and they're disappointed.</summary>
    void BookClubMeets()
    {
        var b = book;
        var hud = FindAnyObjectByType<HUD>();
        RequestLog.TaskDone("job-club", OnShelf(b) < ClubCopies ? "Not enough copies" : "They bought them all");
        if (OnShelf(b) < ClubCopies)
        {
            GM.AddReputation(-1);
            GM.Today.letDown++;
            hud?.ShowBanner($"The book club came, but there were only {OnShelf(b)} copies of <color=#D67840>\"{b.Title}\"</color>. They left a bit put out. -1 reputation.", 6f);
            return;
        }
        int paid = 0;
        for (int i = 0; i < ClubCopies; i++)
        {
            var cond = b.GoodCopies > 0 ? BookCondition.Good : BookCondition.Worn;
            if (!b.TakeCopy(cond)) break;
            paid += Mathf.RoundToInt(b.SaleValue * (cond == BookCondition.Worn ? 0.8f : 1f));
            GM.Today.booksSold++;
        }
        paid += ClubBonus;
        GM.NotifyBooksChanged();
        GM.AddCoins(paid);
        GM.Today.Earn(GameManager.DayStats.Source.Sales, paid);
        RequestLog.Money("Book club", $"Bought {ClubCopies} copies of <color=#D67840>\"{b.Title}\"</color>", "Book club", paid);
        GM.AddReputation(2);
        GameAudio.Play(a => a.served);
        hud?.ShowBanner($"The book club bought {ClubCopies} copies of <color=#D67840>\"{b.Title}\"</color> (and left biscuits). +{paid} coins, +2 reputation.", 6f);
    }

    // ---------------- The repair desk (Collector, Restoration) ----------------

    RepairDesk.Job deskJob;   // the copy this job put on the repair desk
    bool deskFinished;
    BookCondition deskResult; // Good (a perfect repair) or Worn

    void PutOnDesk(BookData b)
    {
        var desk = RepairDesk.Instance;
        deskJob = desk.DropOff(b, BookCondition.Damaged);
        deskFinished = false;
        desk.Finished -= OnRepaired;
        desk.Finished += OnRepaired;
    }

    void OnRepaired(RepairDesk.Job j, bool perfect)
    {
        if (j == null || j != deskJob) return;
        deskFinished = true;
        deskResult = perfect ? BookCondition.Good : BookCondition.Worn; // from damaged: perfect is like new
        // A visitor's own book is set aside for them, not left for you (or the staff) to shelve.
        if (job == Job.Restoration && RepairDesk.Instance != null && RepairDesk.Instance.TakeDone(j.book, deskResult))
            FindAnyObjectByType<HUD>()?.ShowBanner($"{ownerName}'s <color=#D67840>\"{j.book.Title}\"</color> is {(perfect ? "good as new" : "mended (a bit worn)")}. It's set aside for when they come back at {GameManager.FormatHour(OwnerBack)}.", 6f);
    }

    /// <summary>Forget this job's repair. ownersBook: it goes home with its owner, so it leaves the desk too.</summary>
    void ClearDeskJob(bool ownersBook)
    {
        if (deskJob != null && ownersBook && RepairDesk.Instance != null) RepairDesk.Instance.Withdraw(deskJob);
        deskJob = null;
        deskFinished = false;
    }

    /// <summary>The best fit copy of it in your hands: Good before Worn, or null.</summary>
    static BookCondition? HeldCondition(BookData b)
    {
        var carry = PlayerCarry();
        if (carry == null || b == null) return null;
        if (carry.Find((x, c) => x == b && c == BookCondition.Good) >= 0) return BookCondition.Good;
        if (carry.Find((x, c) => x == b && c == BookCondition.Worn) >= 0) return BookCondition.Worn;
        return null;
    }

    static string[] Professors() => new[] { "PF_NPC_Professor_Hazel", "PF_NPC_Professor_Edmund", "PF_NPC_Professor_Victor", "PF_NPC_Professor_Ingrid" }
                                     .OrderBy(_ => Random.value).ToArray();

    // ---------------- Collector (Day 6) ----------------

    DayStory.Visit CollectorVisit()
    {
        book = RareBook();
        if (book == null) return null;
        var b = book;
        return new DayStory.Visit
        {
            prefabs = Professors(), teaser = "Might I trouble you?", atDesk = true, vip = true,
            talk = c => new DayStory.Script()
                .Say($"I'm compiling a register of every surviving copy of <color=#D67840>\"{b.Title}\"</color>. Yours is one of eleven.")
                .Say("I looked at it last week. The spine has split, and I haven't slept properly since.")
                .Say($"Have it mended and in your hands by {GameManager.FormatHour(CollectorDue)}. A perfect repair is worth {CollectorFee} coins to me. A decent one, {CollectorFee / 2}.")
                .Choose("I'll have it mended.", "yes", "I can't spare the time today.", "no")
                .Say("Splendid. Three o'clock. Do be gentle with it.", "yes", "tip")
                .Say("A pity. I'll have to write 'fragile, unrepaired' beside it. It pains me.", "no", DialogueAsset.End)
                .Me($"The copy is on the {Red("repair desk")} now. Repair it, pick it up, and {Red("keep it in your hands")} until they're back at {GameManager.FormatHour(CollectorDue)}", "tip")
                .d,
            after = (c, last) =>
            {
                if (last != "tip") { done = true; return; }
                // The split copy comes off the Rare Books shelf and goes straight onto the repair desk.
                var cond = b.GoodCopies > 0 ? BookCondition.Good : BookCondition.Worn;
                if (RepairDesk.Instance == null || !b.TakeCopy(cond)) { done = true; return; }
                GM.NotifyBooksChanged();
                PutOnDesk(b);
                accepted = true;
                vipKey = c.profileKey;
                vipName = NameOf(c);
                vipAt = CollectorDue;
                RequestLog.Task("job-collector", vipName, $"Repair {BookRequest.H($"\"{b.Title}\"")} at the repair desk and {Red("keep it in your hands")}. {vipName} is back at {GameManager.FormatHour(CollectorDue)}: perfect pays {CollectorFee}, mended pays {CollectorFee / 2}.");
                FindAnyObjectByType<HUD>()?.ShowBanner($"VIP request: <color=#D67840>\"{b.Title}\"</color> is on the {Red("repair desk")}. Mend it and {Red("keep it in your hands")} until {vipName} comes back at {GameManager.FormatHour(CollectorDue)}.", 8f);
            },
        };
    }

    /// <summary>Three o'clock: a perfect copy in your hands earns the full fee, a mended (worn) one half.</summary>
    bool CollectorReturns()
    {
        var sp = CustomerSpawner.Instance;
        if (sp == null) return true;
        var b = book;
        var visit = new DayStory.Visit
        {
            prefabs = new[] { vipKey }, name = vipName, teaser = "Is it mended?", atDesk = true, vip = true,
            talk = c =>
            {
                var s = new DayStory.Script();
                var held = HeldCondition(b);
                if (held == BookCondition.Good)
                    s.Say("Not a crease. You'd never know. That's going in the register with a star beside it.")
                     .Say("As promised. Thank you.");
                else if (held == BookCondition.Worn)
                    s.Say("Mended. Not invisibly, but honestly. I can work with honest.")
                     .Say("Half, as we agreed.");
                else
                    s.Say("Not ready? Or simply not here.")
                     .Say("I'll come back another year. Do look after it in the meantime.");
                return s.d;
            },
            after = (c, _) =>
            {
                var held = HeldCondition(b);
                var carry = PlayerCarry();
                int i = held == null || carry == null ? -1 : carry.Find((x, cc) => x == b && cc == held.Value);
                RequestLog.TaskDone("job-collector", held == BookCondition.Good ? "Perfect repair" : held == BookCondition.Worn ? "Mended" : "Not ready");
                if (i >= 0)
                {
                    bool perfect = held == BookCondition.Good;
                    int fee = perfect ? CollectorFee : CollectorFee / 2;
                    var (copy, condition) = carry.TakeAt(i);
                    GM.AddCoins(fee);
                    GM.Today.Earn(GameManager.DayStats.Source.Vip, fee);
                    RequestLog.Money("Collector", perfect ? "Perfect repair of a rare book" : "Mended rare book", "VIP reward", fee);
                    GM.AddReputation(perfect ? 2 : 1);
                    HUD.SkillPop(c.transform.position + Vector3.up * 2.2f, $"{(perfect ? "Perfect!" : "Mended.")} +{fee} coins, +{(perfect ? 2 : 1)} reputation");
                    c.ReadNow(copy, condition); // a last look in the Rare Books room; it stays in the library
                    return;
                }
                GM.AddReputation(-2);
                GM.Today.letDown++;
                HUD.SkillPop(c.transform.position + Vector3.up * 2.2f, "Not ready: -2 reputation");
            },
        };
        return sp.SpawnStory(visit, true) != null;
    }

    // ---------------- Reading list (Day 7) ----------------

    string[] topics;

    static IEnumerable<BookData> ListBooks() =>
        GM.Books.Where(b => b.Sellable && b.Format == BookFormat.Book && !BookRequest.IsRare(b) && BookRequest.Available(b));

    /// <summary>Three subjects for the class, at least one with nothing on the shelves (so it takes an order).</summary>
    static string[] ListTopics()
    {
        var groups = ListBooks().GroupBy(b => b.SubjectName).ToList();
        if (groups.Count < 3) return null;
        var empty = groups.Where(g => !g.Any(BookRequest.InStock)).OrderBy(_ => Random.value).Take(1).ToList();
        var rest = groups.Except(empty).OrderBy(_ => Random.value);
        return empty.Concat(rest).Take(3).Select(g => g.Key).ToArray();
    }

    static BookData OnShelfFor(string topic) => ListBooks().Where(b => b.SubjectName == topic && BookRequest.InStock(b)).OrderBy(b => b.SaleValue).FirstOrDefault();

    int ListStocked() => topics == null ? 0 : topics.Count(t => OnShelfFor(t) != null);

    string TopicList => topics == null ? "" : $"{BookRequest.H(topics[0])}, {BookRequest.H(topics[1])} and {BookRequest.H(topics[2])}";

    DayStory.Visit ListVisit()
    {
        topics = ListTopics();
        if (topics == null) return null;
        return new DayStory.Visit
        {
            teaser = "Are you the librarian?", atDesk = true,
            talk = c => new DayStory.Script()
                .Say("I teach at the grammar school. My lot have a project due on Monday and not a book between them.")
                .Say($"Could you have something on {TopicList} on your shelves by {GameManager.FormatHour(ListAt)}? I'll buy one of each for the classroom.")
                .Choose("They'll be there.", "yes", "Not today, I'm afraid.", "no")
                .Say("You're a lifesaver. Four o'clock.", "yes", DialogueAsset.End)
                .Say("Back to the encyclopaedia, then. They'll be thrilled.", "no")
                .d,
            after = (c, last) =>
            {
                if (last != "yes") { done = true; return; }
                accepted = true;
                RequestLog.Task("job-list", "Reading list", $"Have a book on {TopicList} on the shelves by {GameManager.FormatHour(ListAt)}. The teacher buys one of each.");
                FindAnyObjectByType<HUD>()?.ShowBanner($"Reading list at {GameManager.FormatHour(ListAt)}: a book on {TopicList} on the shelves (you have {ListStocked()} of 3). Check the Catalogue; express orders take 2 hours.", 8f);
            },
        };
    }

    /// <summary>Four o'clock: all three on the shelves and the teacher buys one of each; any missing and they're let down.</summary>
    void ListCheck()
    {
        var hud = FindAnyObjectByType<HUD>();
        int have = ListStocked();
        RequestLog.TaskDone("job-list", have < 3 ? $"{have} of 3 on the shelves" : "They bought one of each");
        if (have < 3)
        {
            GM.AddReputation(-1);
            GM.Today.letDown++;
            hud?.ShowBanner($"The teacher came for the reading list, but only {have} of the 3 subjects were on the shelves. -1 reputation.", 6f);
            return;
        }
        int paid = ListBonus;
        foreach (var t in topics)
        {
            var b = OnShelfFor(t);
            var cond = b.GoodCopies > 0 ? BookCondition.Good : BookCondition.Worn;
            if (!b.TakeCopy(cond)) continue;
            paid += Mathf.RoundToInt(b.SaleValue * (cond == BookCondition.Worn ? 0.8f : 1f));
            GM.Today.booksSold++;
        }
        GM.NotifyBooksChanged();
        GM.AddCoins(paid);
        GM.Today.Earn(GameManager.DayStats.Source.Sales, paid);
        RequestLog.Money("Reading list", $"A book each on {TopicList}", "Reading list", paid);
        GM.AddReputation(2);
        GameAudio.Play(a => a.served);
        hud?.ShowBanner($"The teacher bought a book on each subject for the class. +{paid} coins, +2 reputation.", 6f);
    }

    // ---------------- Rare loan (Day 8) ----------------

    BookData loanBook;  // out at the museum tonight
    int loanBackDay;

    DayStory.Visit LoanVisit()
    {
        book = RareBook();
        if (book == null) return null;
        var b = book;
        return new DayStory.Visit
        {
            prefabs = Professors(), teaser = "I come with an odd request.", atDesk = true, vip = true,
            talk = c => new DayStory.Script()
                .Say("I'm from the County Museum. We open a small exhibition tonight, and we're one book short.")
                .Say($"Lend us your <color=#D67840>\"{b.Title}\"</color> for the night. {LoanFee} coins now, and it's back on your book cart tomorrow morning.")
                .Say("We'll take every care. Mostly.")
                .Choose("It's yours till morning.", "yes", "It doesn't leave the building.", "no")
                .Say("Wonderful. I'll carry it like a newborn.", "yes", DialogueAsset.End)
                .Say("Quite right, really. I'd say the same.", "no")
                .d,
            after = (c, last) =>
            {
                done = true; // nothing more today
                if (last != "yes") return;
                var cond = b.GoodCopies > 0 ? BookCondition.Good : BookCondition.Worn;
                if (!b.TakeCopy(cond)) return;
                accepted = true;
                GM.NotifyBooksChanged();
                GM.AddCoins(LoanFee);
                GM.Today.Earn(GameManager.DayStats.Source.Vip, LoanFee);
                RequestLog.Money("County Museum", $"Lent \"{b.Title}\" for the night", "Rare loan", LoanFee);
                GM.ReturnTomorrow(b);
                loanBook = b;
                loanBackDay = GM.day + 1;
                FindAnyObjectByType<HUD>()?.ShowBanner($"Rare loan: <color=#D67840>\"{b.Title}\"</color> is at the museum tonight. +{LoanFee} coins. It comes back on the book cart tomorrow morning.", 7f);
            },
        };
    }

    /// <summary>The loaned book is back on the cart: now and then the museum wasn't as careful as promised.</summary>
    void CheckLoanBack()
    {
        if (GM.day > loanBackDay + 1) { loanBook = null; return; } // missed (a reload): it just came back
        if (GM.day < loanBackDay || DeliveryCart.Instance == null || !DeliveryCart.Instance.Has(loanBook)) return;
        var b = loanBook;
        loanBook = null;
        float roll = Random.value;
        var hud = FindAnyObjectByType<HUD>();
        if (roll >= 0.4f || ReturnBin.Instance == null)
        {
            hud?.ShowBanner($"<color=#D67840>\"{b.Title}\"</color> is back from the museum without a mark. It's on the book cart.", 6f);
            return;
        }
        var cond = roll < 0.1f ? BookCondition.Damaged : BookCondition.Worn;
        DeliveryCart.Instance.Take(b);
        ReturnBin.Instance.Add(b, cond);
        GM.NotifyBooksChanged();
        hud?.ShowBanner($"The museum sent <color=#D67840>\"{b.Title}\"</color> back {(cond == BookCondition.Damaged ? "with a torn page" : "with a scuffed corner")}. It's in the returns bin, ready for the repair desk.", 7f);
    }

    // ---------------- Restoration (Day 9) ----------------

    string ownerKey, ownerName;

    static BookData RestoreBook()
    {
        var pool = GM.Books.Where(b => b.Format == BookFormat.Book && !BookRequest.IsRare(b)).ToList();
        return pool.Count > 0 ? pool[Random.Range(0, pool.Count)] : null;
    }

    DayStory.Visit RestoreVisit()
    {
        book = RestoreBook();
        if (book == null || RepairDesk.Instance == null) return null;
        var b = book;
        return new DayStory.Visit
        {
            teaser = "Can you fix books here?", atDesk = true,
            talk = c => new DayStory.Script()
                .Say($"This was my grandmother's copy of <color=#D67840>\"{b.Title}\"</color>. The dog got to it.")
                .Say($"Could your repair desk save it by {GameManager.FormatHour(OwnerBack)}? If it's like new, {RestoreFee} coins. If it's just mended, I'll still pay something.")
                .Choose("Leave it with me.", "yes", "Sorry, we're full up today.", "no")
                .Say("Thank you. She'd have liked you.", "yes", "tip")
                .Say("I understand. I'll keep it in a drawer a while longer.", "no", DialogueAsset.End)
                .Me($"It's on the {Red("repair desk")}. Repair it before {GameManager.FormatHour(OwnerBack)}. It's theirs, so it won't go on a shelf", "tip")
                .d,
            after = (c, last) =>
            {
                if (last != "tip") { done = true; return; }
                PutOnDesk(b);
                accepted = true;
                ownerKey = c.profileKey;
                ownerName = FirstNameOf(c);
                RequestLog.Task("job-restore", ownerName, $"Repair {ownerName}'s {BookRequest.H($"\"{b.Title}\"")} at the repair desk by {GameManager.FormatHour(OwnerBack)}. Like new pays {RestoreFee}, mended pays {RestoreFee / 2}.");
                FindAnyObjectByType<HUD>()?.ShowBanner($"Restoration: {ownerName}'s <color=#D67840>\"{b.Title}\"</color> is on the {Red("repair desk")}. Mend it by {GameManager.FormatHour(OwnerBack)}.", 8f);
            },
        };
    }

    /// <summary>Half past four: the owner collects their book and pays by how well it's mended.</summary>
    bool OwnerReturns()
    {
        var sp = CustomerSpawner.Instance;
        if (sp == null) return true;
        var b = book;
        var visit = new DayStory.Visit
        {
            prefabs = new[] { ownerKey }, name = ownerName, teaser = "Is my book ready?", atDesk = true,
            talk = c =>
            {
                var s = new DayStory.Script();
                if (deskFinished && deskResult == BookCondition.Good)
                    s.Say("Oh. Oh, look at it. That's how it looked on her shelf.").Say("Here. Every coin.");
                else if (deskFinished)
                    s.Say("You can tell it's been mended. But it holds together, and that's what matters.").Say("Here, for your trouble.");
                else
                    s.Say("Not done? Never mind. I'll take it home as it is.");
                return s.d;
            },
            after = (c, _) =>
            {
                RequestLog.TaskDone("job-restore", !deskFinished ? "Not repaired" : deskResult == BookCondition.Good ? "Good as new" : "Mended");
                if (!deskFinished)
                {
                    GM.AddReputation(-1);
                    GM.Today.letDown++;
                    HUD.SkillPop(c.transform.position + Vector3.up * 2.2f, "Not repaired: -1 reputation");
                    return;
                }
                bool perfect = deskResult == BookCondition.Good;
                int fee = perfect ? RestoreFee : RestoreFee / 2;
                GM.AddCoins(fee);
                GM.Today.Earn(GameManager.DayStats.Source.Other, fee);
                RequestLog.Money(ownerName, perfect ? "Restoration, good as new" : "Restoration, mended", "Restoration", fee);
                GM.AddReputation(perfect ? 2 : 1);
                GameAudio.Play(a => a.served);
                HUD.SkillPop(c.transform.position + Vector3.up * 2.2f, $"+{fee} coins, +{(perfect ? 2 : 1)} reputation");
            },
        };
        return sp.SpawnStory(visit, false) != null;
    }

    // ---------------- For the Reserved panel ----------------

    /// <summary>Accepted jobs still to do today, for the Reserved panel: (what, when and how it's going, all set).</summary>
    public IEnumerable<(string what, string status, bool ready)> Lines()
    {
        if (GM == null) yield break;
        // A rare book promised to a VIP: order it today, have it in your hands when they're back tomorrow.
        var p = GM.vipPromise;
        if (p != null)
        {
            bool ordered = !GM.RareFinds.Exists(x => x.Title == p.title);
            if (GM.day < p.day)
                yield return ($"VIP: {p.name} wants <color=#D67840>\"{p.title}\"</color>", ordered ? "Ordered  ·  arrives tomorrow morning" : "ORDER IT IN THE CATALOGUE (rare dealer)", ordered);
            else
            {
                bool holding = Holding(Title(p.title)) >= 0;
                yield return ($"VIP: {p.name} reads <color=#D67840>\"{p.title}\"</color> today", $"Back this afternoon  ·  {(holding ? "you have it" : ordered ? "NOT IN YOUR HANDS" : "NEVER ORDERED")}", holding);
            }
        }
        if (loanBook != null && GM.day < loanBackDay)
            yield return ($"On loan: <color=#D67840>\"{loanBook.Title}\"</color>", "At the museum  ·  back on the book cart tomorrow morning", true);
        if (!accepted || done) yield break;
        if (job == Job.ReadingList && topics != null)
        {
            int n = ListStocked();
            yield return ($"Reading list: {TopicList}", $"One of each on the shelves by {GameManager.FormatHour(ListAt)}  ·  have {n} of 3", n >= 3);
            yield break;
        }
        if (book == null) yield break;
        if (job == Job.Collector)
        {
            var held = HeldCondition(book);
            string state = held == BookCondition.Good ? "perfect, you have it" : held == BookCondition.Worn ? "mended, you have it"
                         : deskFinished ? "READY ON THE REPAIR DESK" : "ON THE REPAIR DESK";
            yield return ($"VIP: {vipName} wants <color=#D67840>\"{book.Title}\"</color> mended", $"In your hands by {GameManager.FormatHour(vipAt)}  ·  {state}", held != null);
        }
        else if (job == Job.Restoration)
            yield return ($"{ownerName}'s <color=#D67840>\"{book.Title}\"</color>", $"Mend it by {GameManager.FormatHour(OwnerBack)}  ·  {(deskFinished ? "done" : "ON THE REPAIR DESK")}", deskFinished);
        else if (job == Job.Vip && !buying)
        {
            bool holding = Holding(book) >= 0;
            string state = holding ? "you have it" : "TAKE IT FROM RARE BOOKS";
            yield return ($"VIP: {vipName} wants <color=#D67840>\"{book.Title}\"</color>", $"In your hands by {GameManager.FormatHour(VipDue)}  ·  {state}", holding);
        }
        else if (job == Job.BookClub)
        {
            int n = OnShelf(book);
            yield return ($"Book club: <color=#D67840>\"{book.Title}\"</color>", $"{ClubCopies} on the shelf by {GameManager.FormatHour(ClubAt)}  ·  have {n}{(GM.OnOrder(book) > 0 ? $", {GM.OnOrder(book)} on order" : "")}", n >= ClubCopies);
        }
    }
}
