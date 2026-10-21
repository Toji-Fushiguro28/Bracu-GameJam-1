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
/// The rare dealer's VIP request is guaranteed once, staff or not: maybe on Day 3, always on Day 4 if it hasn't come yet.
/// Accepted jobs show in the Reserved panel (NotebookHUD). Added to the GameManager at runtime.
/// </summary>
public class SpecialJobs : MonoBehaviour
{
    public static SpecialJobs Instance { get; private set; }

    enum Job { None, Order, Vip, BookClub }

    const float VipDue = 15f, ClubAt = 16f;
    const int ClubCopies = 3, VipReward = 40, ClubBonus = 15;

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
        job = Job.None;
        offered = accepted = done = false;
        orderTask = null;
        book = null;
        vipKey = vipName = null;
        buying = taskShown = forceDealer = false;
        vipAt = VipDue;
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
        if (GM.day < 2 || !HasStaff) return;

        var options = new List<Job>();
        if (OrderBook() != null) options.Add(Job.Order);
        if (GM.RareOpen && (RareBook() != null || (RareFind() != null && GM.vipPromise == null))) options.Add(Job.Vip);
        if (ClubBook() != null) options.Add(Job.BookClub);
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
        if (GM == null || job == Job.None || GM.CurrentPhase != GameManager.Phase.Open) return;
        float h = GM.Hour;
        if (buying && !taskShown)
        {
            taskShown = true; // added here, after the new day's notebook is set up
            RequestLog.Task("job-vip", vipName, $"{vipName} comes back this afternoon to read {BookRequest.H($"\"{book.Title}\"")} in the Rare Books room. Have it in your hands at the desk.");
        }
        if (!offered && h >= offerAt) { if (Offer() || h >= offerAt + 1.5f) offered = true; return; }
        if (!accepted || done) return;
        if (job == Job.Vip && h >= vipAt) { if (VipReturns() || h >= vipAt + 1.5f) done = true; }
        if (job == Job.BookClub && h >= ClubAt) { done = true; BookClubMeets(); }
    }

    bool Offer()
    {
        var sp = CustomerSpawner.Instance;
        if (sp == null) return true;
        var visit = job == Job.Order ? OrderVisit() : job == Job.Vip ? VipVisit() : ClubVisit();
        if (visit == null) { job = Job.None; return true; }
        // The book club organiser can be anyone; the others wait a while for the right person to be free.
        return sp.SpawnStory(visit, job == Job.BookClub || GM.Hour >= offerAt + 0.75f) != null;
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
            prefabs = profs, teaser = "A word, if you have a moment.", atDesk = true,
            talk = c => new DayStory.Script()
                .Say($"Good morning. I'm giving a paper on Friday, and I need to examine <color=#D67840>\"{b.Title}\"</color> this afternoon.")
                .Say($"Could you have it ready, in your hands, when I come back at {GameManager.FormatHour(VipDue)}? I'd be grateful. Generously grateful.")
                .Choose("It'll be ready.", "yes", "I'm afraid I can't.", "no")
                .Say("Splendid. Until three, then.", "yes", DialogueAsset.End)
                .Say("A pity. Another time, perhaps.", "no")
                .d,
            after = (c, last) =>
            {
                if (last != "yes") { done = true; return; }
                accepted = true;
                vipKey = c.profileKey;
                vipName = NameOf(c);
                RequestLog.Task("job-vip", vipName, $"Have {BookRequest.H($"\"{b.Title}\"")} in your hands at the desk by {GameManager.FormatHour(VipDue)}. It's on the {ShelfInteractable.NameOf(b.ShelfLocation)} shelf.");
                FindAnyObjectByType<HUD>()?.ShowBanner($"VIP request: have <color=#D67840>\"{b.Title}\"</color> in your hands at the desk by {GameManager.FormatHour(VipDue)}. It's on the {ShelfInteractable.NameOf(b.ShelfLocation)} shelf.", 7f);
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
            prefabs = profs, teaser = "A word, if you have a moment.", atDesk = true,
            talk = c => new DayStory.Script()
                .Say($"Good morning. I'm giving a paper next week, and I need to study <color=#D67840>\"{b.Title}\"</color>.")
                .Say($"Your library doesn't hold a copy. But the rare-book dealer has one: it's in your computer's Catalogue, for {cost} coins. It would arrive tomorrow morning.")
                .Say("Order it, and I'll come back tomorrow afternoon to read it in your Rare Books room. I'll repay every coin, with thanks on top. The library keeps the book.")
                .Choose("I'll order it.", "yes", "That's too dear for us.", "no")
                .Say("Splendid. Until tomorrow, then.", "yes", DialogueAsset.End)
                .Say("I understand. Another time, perhaps.", "no")
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
            prefabs = new[] { vipKey }, name = vipName, teaser = "I'm back for the book.", atDesk = true,
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
        if (!accepted || done || book == null) yield break;
        if (job == Job.Vip && !buying)
        {
            bool holding = Holding(book) >= 0;
            string state = holding ? "you have it" : WantedRare != null ? "BUY IT IN THE CATALOGUE" : "NOT IN YOUR HANDS";
            yield return ($"VIP: {vipName} wants <color=#D67840>\"{book.Title}\"</color>", $"In your hands by {GameManager.FormatHour(VipDue)}  ·  {state}", holding);
        }
        else if (job == Job.BookClub)
        {
            int n = OnShelf(book);
            yield return ($"Book club: <color=#D67840>\"{book.Title}\"</color>", $"{ClubCopies} on the shelf by {GameManager.FormatHour(ClubAt)}  ·  have {n}{(GM.OnOrder(book) > 0 ? $", {GM.OnOrder(book)} on order" : "")}", n >= ClubCopies);
        }
    }
}
