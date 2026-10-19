using System.Collections.Generic;
using System.Linq;
using UnityEngine;

/// <summary>
/// Jobs for the librarian once staff take care of the busywork (any hire, from Day 2): one a day, offered by
/// someone at the desk, which you can take or turn down.
///  Special order - a regular wants a book in by tomorrow and pays extra when they collect it (a reservation with a bonus).
///  VIP request   - a scholar wants a rare book in your hands when they come back at 3:00 PM.
///  Book club     - a club needs 3 copies of a book on the shelves at 4:00 PM, and buys them all.
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
        book = null;
        vipKey = vipName = null;
        if (GM == null || GM.day < 2 || !HasStaff) return;

        var options = new List<Job>();
        if (OrderBook() != null) options.Add(Job.Order);
        if (GM.RareOpen && RareBook() != null) options.Add(Job.Vip);
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

    void Update()
    {
        if (GM == null || job == Job.None || GM.CurrentPhase != GameManager.Phase.Open) return;
        float h = GM.Hour;
        if (!offered && h >= offerAt) { if (Offer() || h >= offerAt + 1.5f) offered = true; return; }
        if (!accepted || done) return;
        if (job == Job.Vip && h >= VipDue) { if (VipReturns() || h >= VipDue + 1.5f) done = true; }
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
            prefabs = keys, teaser = "Could I ask you a favour?",
            talk = c => new DayStory.Script()
                .Say($"Could I ask a favour? I've been after \"{b.Title}\" by {b.Author} for months. Nobody in town has it.")
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
                FindAnyObjectByType<HUD>()?.ShowBanner($"Special order: get \"{b.Title}\" in by tomorrow (order it from the computer's Catalogue). {FirstNameOf(c)} pays +{bonus} on top.", 7f);
            },
        };
    }

    // ---------------- VIP request ----------------

    DayStory.Visit VipVisit()
    {
        book = RareBook();
        if (book == null) return null;
        var b = book;
        var profs = new[] { "PF_NPC_Professor_Hazel", "PF_NPC_Professor_Edmund", "PF_NPC_Professor_Victor", "PF_NPC_Professor_Ingrid" }
                    .OrderBy(_ => Random.value).ToArray();
        return new DayStory.Visit
        {
            prefabs = profs, teaser = "A word, if you have a moment.",
            talk = c => new DayStory.Script()
                .Say($"Good morning. I'm giving a paper on Friday, and I need to examine \"{b.Title}\" this afternoon.")
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
                FindAnyObjectByType<HUD>()?.ShowBanner($"VIP request: have \"{b.Title}\" in your hands at the desk by {GameManager.FormatHour(VipDue)}. It's on the {ShelfInteractable.NameOf(b.ShelfLocation)} shelf.", 7f);
            },
        };
    }

    /// <summary>Three o'clock: the scholar is back. In your hands? A generous thank-you. If not, they're not pleased.</summary>
    bool VipReturns()
    {
        var sp = CustomerSpawner.Instance;
        if (sp == null) return true;
        var b = book;
        var visit = new DayStory.Visit
        {
            prefabs = new[] { vipKey }, name = vipName, teaser = "I'm back for the book.",
            talk = c =>
            {
                var s = new DayStory.Script();
                if (Holding(b) >= 0)
                    s.Say($"\"{b.Title}\". And in good order. You've no idea what this means.")
                     .Me("They spend a quiet hour with it at the desk, then hand it back with great care")
                     .Say("Here. For the library. Don't spend it all on glue.");
                else
                    s.Say("Not ready? I did ask quite specifically.")
                     .Say("Never mind. I'll find it somewhere else.");
                return s.d;
            },
            after = (c, _) =>
            {
                int i = Holding(b);
                var carry = PlayerCarry();
                if (i >= 0 && carry != null)
                {
                    var (copy, condition) = carry.TakeAt(i);
                    copy.AddCopy(condition); // examined, then back on its shelf
                    GM.NotifyBooksChanged();
                    GM.AddCoins(VipReward);
                    GM.Today.Earn(GameManager.DayStats.Source.Vip, VipReward);
                    RequestLog.Money("VIP request", "Rare book ready on time", "VIP reward", VipReward);
                    GM.AddReputation(2);
                    HUD.SkillPop(c.transform.position + Vector3.up * 2.2f, $"VIP request done! +{VipReward} coins, +2 reputation");
                }
                else
                {
                    GM.AddReputation(-1);
                    GM.Today.letDown++;
                }
            },
        };
        return sp.SpawnStory(visit, true) != null;
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
            teaser = "Are you the one in charge?",
            talk = c => new DayStory.Script()
                .Say("Hello! I run a little book club. Seven of us, two of whom have actually read the book.")
                .Say($"This month it's \"{b.Title}\". Could you have {ClubCopies} copies on the shelf by {GameManager.FormatHour(ClubAt)}? We'll buy them all.")
                .Choose("Leave it with me.", "yes", "Not today, sorry.", "no")
                .Say("Lovely! Four o'clock. We'll bring biscuits.", "yes", DialogueAsset.End)
                .Say("Oh well. Next month, maybe.", "no")
                .d,
            after = (c, last) =>
            {
                if (last != "yes") { done = true; return; }
                accepted = true;
                FindAnyObjectByType<HUD>()?.ShowBanner($"Book club at {GameManager.FormatHour(ClubAt)}: have {ClubCopies} copies of \"{b.Title}\" on the shelf (you have {OnShelf(b)}). Express orders take 2 hours.", 8f);
            },
        };
    }

    /// <summary>Four o'clock: enough copies, and they buy the lot. Not enough, and they're disappointed.</summary>
    void BookClubMeets()
    {
        var b = book;
        var hud = FindAnyObjectByType<HUD>();
        if (OnShelf(b) < ClubCopies)
        {
            GM.AddReputation(-1);
            GM.Today.letDown++;
            hud?.ShowBanner($"The book club came, but there were only {OnShelf(b)} copies of \"{b.Title}\". They left a bit put out. -1 reputation.", 6f);
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
        RequestLog.Money("Book club", $"Bought {ClubCopies} copies of \"{b.Title}\"", "Book club", paid);
        GM.AddReputation(2);
        GameAudio.Play(a => a.served);
        hud?.ShowBanner($"The book club bought {ClubCopies} copies of \"{b.Title}\" (and left biscuits). +{paid} coins, +2 reputation.", 6f);
    }

    // ---------------- For the Reserved panel ----------------

    /// <summary>Accepted jobs still to do today, for the Reserved panel: (what, when and how it's going, all set).</summary>
    public IEnumerable<(string what, string status, bool ready)> Lines()
    {
        if (!accepted || done || book == null || GM == null) yield break;
        if (job == Job.Vip)
        {
            bool holding = Holding(book) >= 0;
            yield return ($"VIP: {vipName} wants \"{book.Title}\"", $"In your hands by {GameManager.FormatHour(VipDue)}  ·  {(holding ? "you have it" : "NOT IN YOUR HANDS")}", holding);
        }
        else if (job == Job.BookClub)
        {
            int n = OnShelf(book);
            yield return ($"Book club: \"{book.Title}\"", $"{ClubCopies} on the shelf by {GameManager.FormatHour(ClubAt)}  ·  have {n}{(GM.OnOrder(book) > 0 ? $", {GM.OnOrder(book)} on order" : "")}", n >= ClubCopies);
        }
    }
}
