using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// End of day (open book). Left page: the Day Report. Right page: Summary (where the money came from, what went
/// wrong and a tip for tomorrow; shown first), then evening planning with Shop (buy copies), Staff (hire / let go),
/// Upgrades and Log (today's requests) tabs. "Open Day N" starts the next day.
/// </summary>
[RequireComponent(typeof(UIDocument))]
public class DayReportUI : MonoBehaviour
{
    public static DayReportUI Instance { get; private set; }

    [Tooltip("Shop price as a share of the book's value.")]
    public float shopPriceShare = 0.6f;
    public AudioClip buySound;

    VisualElement root, list;
    Label stars, repLabel, coinsLabel;
    VisualElement rows;
    BookFilterBar filter;
    Button nextDay;
    Button[] tabs;
    int tab;
    bool tallied;
    int cleanBonus;
    AudioSource sfx;

    public bool IsOpen => root != null && !root.ClassListContains("hidden");

    void Awake()
    {
        Instance = this;
        sfx = gameObject.AddComponent<AudioSource>();
        sfx.playOnAwake = false;
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
        PlayerLock.Unlock(this);
    }

    void OnEnable()
    {
        var doc = GetComponent<UIDocument>().rootVisualElement;
        root = doc.Q("report");
        list = doc.Q("plan-list");
        rows = doc.Q("report-rows");
        // Search + filters above the shop list (Shop tab only).
        filter = new BookFilterBar(RefreshPlan, false);
        list.parent.Insert(list.parent.IndexOf(list), filter.Root);
        stars = doc.Q<Label>("report-stars");
        repLabel = doc.Q<Label>("report-rep");
        coinsLabel = doc.Q<Label>("plan-coins");
        nextDay = doc.Q<Button>("report-next");
        tabs = new[] { doc.Q<Button>("tab-shop"), doc.Q<Button>("tab-staff"), doc.Q<Button>("tab-upgrades"), doc.Q<Button>("tab-log"), doc.Q<Button>("tab-summary") };
        for (int i = 0; i < tabs.Length; i++)
        {
            int t = i;
            tabs[i]?.RegisterCallback<ClickEvent>(_ => { tab = t; RefreshPlan(); });
        }
        nextDay?.RegisterCallback<ClickEvent>(_ => StartNextDay());
        // No way back into the library after locking up: the only way on is the next day.
        doc.Q<Button>("report-close")?.AddToClassList("hidden");
        root?.AddToClassList("hidden");
    }

    public void Open()
    {
        var gm = GameManager.Instance;
        if (gm == null || root == null) return;
        if (!tallied)
        {
            // Tidy library = happy regulars tomorrow.
            int left = MessSpot.All.Count + (ReturnBin.Instance != null ? ReturnBin.Instance.Count : 0);
            cleanBonus = left == 0 ? 1 : -Mathf.Min(3, left);
            gm.AddReputation(cleanBonus);
            tallied = true;
            GameAudio.Play(a => a.dayReport);
        }
        PlayerLock.Lock(this);
        GameAudio.Play(a => a.panelOpen, 0.6f);
        if (root.ClassListContains("hidden")) tab = SummaryTab; // each evening opens on the summary
        root.RemoveFromClassList("hidden");
        RefreshReport();
        RefreshPlan();
    }

    public void Close()
    {
        if (!IsOpen) return;
        GameAudio.Play(a => a.panelClose, 0.6f);
        root.AddToClassList("hidden");
        PlayerLock.Unlock(this);
    }

    void StartNextDay()
    {
        var gm = GameManager.Instance;
        // Applicants first: the interview happens before the library opens.
        if (gm != null && gm.MorningVisitDue) { HUD.FadeOut(() => gm.RunMorningInterviews(() =>
        {
            DoStartNextDay(); // saves the new day (and that the ending was seen)
            if (StaffInterview.EndingToMenu) { StaffInterview.EndingToMenu = false; gm.ReturnToMenu(); return; }
            HUD.FadeIn();
        })); return; }
        HUD.FadeThen(DoStartNextDay);
    }

    void DoStartNextDay()
    {
        var gm = GameManager.Instance;
        bool archiveBefore = gm.ArchiveOpen, rareBefore = gm.RareOpen;
        // Overnight, anything left lying around goes back where it belongs.
        foreach (var m in MessSpot.All.ToArray()) m.Clean(false);
        gm.StartNextDay();
        tallied = false;
        Close();
        string news = !archiveBefore && gm.ArchiveOpen ? "The Archive is now open to readers!"
                    : !rareBefore && gm.RareOpen ? "The Rare Books room is now open to scholars!" : null;
        if (news != null) GameAudio.Play(a => a.unlock);
        if (news != null && WorldPrompts.Instance != null && RepairDesk.Instance != null) WorldPrompts.Instance.ShowBubble(RepairDesk.Instance, news, 5f);
    }

    // ---------------- Report ----------------

    int Stars(GameManager.DayStats d) => Mathf.Clamp(1 + (d.served + d.booksRead - 2 * d.angry + cleanBonus * 2) / 3, 1, 5);

    /// <summary>The Dean's goal: how close you are, and what tonight means on the last day.</summary>
    static string GoalLine(GameManager gm)
    {
        if (!gm.GoalActive) return "";
        int left = gm.ReputationGoal - gm.Reputation;
        if (gm.day >= GameManager.EndingDay)
            return left <= 0 ? $"\n<color=#D67840>The Dean's goal of {gm.ReputationGoal}: reached!</color> He'll visit in the morning."
                             : $"\nThe Dean's goal was {gm.ReputationGoal}: {left} short. He'll visit in the morning.";
        int daysLeft = GameManager.EndingDay - gm.day;
        return left <= 0 ? $"\n<color=#D67840>The Dean's goal of {gm.ReputationGoal}: reached!</color> Keep it up."
                         : $"\nThe Dean's goal: {gm.ReputationGoal} by Day {GameManager.EndingDay}. {left} to go, {daysLeft} day{(daysLeft == 1 ? "" : "s")} left.";
    }

    void RefreshReport()
    {
        var gm = GameManager.Instance;
        var d = gm.Today;
        root.Q<Label>("report-title").text = $"Day {gm.day} Report  ·  {GameManager.DifficultyNames[(int)gm.difficulty]}";
        // One tidy row per stat: label on the left, number on the right.
        rows.Clear();
        void Row(string label, string value, bool bold = false)
        {
            var r = new VisualElement();
            r.AddToClassList("report-row");
            if (bold) r.AddToClassList("total");
            var l = new Label(label); l.AddToClassList("report-key");
            var v = new Label(value); v.AddToClassList("report-val");
            r.Add(l); r.Add(v);
            rows.Add(r);
        }
        Row("Visitors served", d.served.ToString());
        Row("Left unhappy", d.angry.ToString());
        Row("Books sold", d.booksSold.ToString());
        Row("Books read", d.booksRead.ToString());
        Row("Returns checked in", d.returns.ToString());
        Row("Repairs", $"{d.repairsPerfect + d.repairsNormal}");
        Row("Things tidied", d.cleaned.ToString());
        Row("New members", d.signups.ToString());
        Row("Tips", d.tips.ToString());
        if (DayStory.Instance != null && DayStory.Instance.ReportRow(out var twist, out var outcome)) Row(twist, outcome); // today's twist
        // The money: what you started with, everything in and out, and what you have now.
        Row("Coins at the start of the day", d.coinsAtStart.ToString());
        Row("Money in", $"+{d.coinsIn}");
        Row("Money out", d.coinsOut > 0 ? $"-{d.coinsOut}" : "0");
        Row("Coins now", gm.Coins.ToString(), true);
        if (gm.WagesDueTomorrow > 0) Row("Staff wages tomorrow morning", $"-{gm.WagesDueTomorrow}");
        int s = Stars(d);
        stars.text = new string('*', s) + new string('-', 5 - s);
        int delta = gm.Reputation - gm.ReputationAtStart;
        repLabel.text = $"Reputation {gm.Reputation} ({(delta >= 0 ? "+" : "")}{delta})" +
                        (cleanBonus > 0 ? "\nThe library is spotless!" : cleanBonus < 0 ? "\nThe library was left messy." : "") +
                        GoalLine(gm);
        nextDay.text = $"Open Day {gm.day + 1}";
    }

    // ---------------- Evening planning ----------------

    void RefreshPlan()
    {
        var gm = GameManager.Instance;
        if (gm == null) return;
        for (int i = 0; i < tabs.Length; i++) tabs[i]?.EnableInClassList("selected", i == tab);
        PopularGlow.Ready(tabs[1], gm.StaffReady && tab != 1);
        PopularGlow.Ready(tabs[2], gm.UpgradeReady && tab != 2);
        coinsLabel.text = $"Coins: {gm.Coins}";
        list.Clear();
        filter?.Root.EnableInClassList("hidden", tab != 0);
        if (tab == 0) BuildShop(gm);
        else if (tab == 1) BuildStaff(gm);
        else if (tab == 2) BuildUpgrades(gm);
        else if (tab == SummaryTab) BuildSummary(gm);
        else BuildLog(gm);
    }

    // ---------------- Summary ----------------

    const int SummaryTab = 4;

    void SummaryHead(string text)
    {
        var l = new Label(text);
        l.AddToClassList("summary-head");
        list.Add(l);
    }

    void SummaryRow(string key, int value, bool total = false)
    {
        var r = new VisualElement();
        r.AddToClassList("report-row");
        if (total) r.AddToClassList("total");
        var k = new Label(key); k.AddToClassList("report-key");
        var v = new Label(value > 0 ? value.ToString() : "-"); v.AddToClassList("report-val");
        r.Add(k); r.Add(v);
        list.Add(r);
    }

    void SummaryNote(string text, string cls)
    {
        var l = new Label(text);
        l.AddToClassList(cls);
        list.Add(l);
    }

    /// <summary>Reservations nobody has ordered yet (they'll be let down tomorrow).</summary>
    static int UnorderedReservations(GameManager gm) => gm.reservations.FindAll(r =>
    {
        var b = gm.FindTitle(r.title);
        return b != null && gm.OnOrder(b) == 0 && !BookRequest.InStock(b) && !(DeliveryCart.Instance != null && DeliveryCart.Instance.Has(b));
    }).Count;

    /// <summary>Where the money came from, what went wrong, and one thing to do better tomorrow.</summary>
    void BuildSummary(GameManager gm)
    {
        var d = gm.Today;
        SummaryHead("Where the money came from");
        // Only what actually earned something today, so the rest of the page fits.
        void Source(string key, int value) { if (value > 0) SummaryRow(key, value); }
        Source("Book sales", d.fromSales);
        Source("Reading fees", d.fromReading);
        Source("Library cards", d.fromCards);
        Source("VIP scholars", d.fromVip);
        Source("Staff work", d.fromStaff);
        Source("Tidying, rewards and jobs", d.fromOther + Mathf.Max(0, d.coinsIn - d.income)); // anything not filed under a source
        if (d.coinsIn > 0) SummaryRow("Total in", d.coinsIn, true);
        else SummaryNote("No money came in today.", "summary-note");
        if (d.tips > 0) SummaryNote($"Tips (already in the totals above): {d.tips}", "summary-note");
        if (d.staffKept > 0) SummaryNote($"Your staff kept {d.staffKept} as their cut (not in the totals).", "summary-note");

        SummaryHead("Where the money went");
        int listed = 0;
        for (int i = 0; i < d.spentOn.Count; i++) { SummaryRow(d.spentOn[i], d.spentAmount[i]); listed += d.spentAmount[i]; }
        if (d.coinsOut - listed > 0) SummaryRow("Other", d.coinsOut - listed);
        if (d.coinsOut > 0) SummaryRow("Total out", d.coinsOut, true);
        else SummaryNote("Nothing spent today.", "summary-note");

        SummaryHead("Coins");
        SummaryRow("At the start of the day", d.coinsAtStart);
        SummaryRow("+ Money in", d.coinsIn);
        SummaryRow("- Money out", d.coinsOut);
        SummaryRow("= Coins now", gm.Coins, true);

        SummaryHead("What went wrong");
        int wrong = 0;
        void Bad(string text) { SummaryNote(text, "summary-note"); list[list.childCount - 1].AddToClassList("bad"); wrong++; }
        if (d.angry > 0) Bad($"{d.angry} {(d.angry == 1 ? "visitor" : "visitors")} walked out angry.");
        if (d.wrongBooks > 0) Bad($"{d.wrongBooks} wrong {(d.wrongBooks == 1 ? "book" : "books")} handed over.");
        if (d.letDown > 0) Bad($"{d.letDown} {(d.letDown == 1 ? "person" : "people")} let down (lied to, never ordered for, or left waiting).");
        if (cleanBonus < 0) Bad($"Mess left at closing ({cleanBonus} reputation).");
        int unordered = UnorderedReservations(gm);
        if (unordered > 0) Bad($"{unordered} {(unordered == 1 ? "reservation isn't" : "reservations aren't")} ordered yet.");
        foreach (var role in GameManager.StaffOrder)
            if (gm.Hired(role) && gm.staffMood[(int)role] < 2.5f)
                Bad($"{GameManager.StaffPerson[(int)role]} is {GameManager.MoodWord(gm.staffMood[(int)role])}, and working slower for it.");
        if (wrong == 0) SummaryNote("Nothing! A clean day.", "summary-note");

        SummaryHead("Tip for tomorrow");
        SummaryNote(Tip(gm, unordered), "summary-tip");
    }

    /// <summary>The one thing most worth doing better tomorrow, from what happened today.</summary>
    string Tip(GameManager gm, int unordered)
    {
        var d = gm.Today;
        if (unordered > 0)
            return $"{unordered} {(unordered == 1 ? "reservation still needs" : "reservations still need")} ordering. Do it now in the Shop tab, or someone gets let down tomorrow.";
        if (d.angry > 0)
            return "People walked out. Serve the front of the queue first, and ask for a hint when a request is hard to place.";
        if (d.wrongBooks >= 2)
            return "Check the request on the computer before you hand a book over. Wrong books cost reputation.";
        if (cleanBonus < 0)
            return "Tidy the cups and books before you lock up. A spotless library earns reputation; a messy one loses it.";
        foreach (var role in GameManager.StaffOrder)
            if (gm.Hired(role) && gm.staffMood[(int)role] < 2.5f)
                return $"{GameManager.StaffPerson[(int)role]} is {GameManager.MoodWord(gm.staffMood[(int)role])}. Go over quickly when they need help, pay for proper fixes and stop for a chat. Happy staff work faster.";
        bool repairs = (ReturnBin.Instance != null && ReturnBin.Instance.Count > 0) || gm.Books.Exists(b => b.DamagedCopies > 0 && !BookRequest.IsRare(b));
        if (repairs)
            return "Some books are waiting for repair. Mended books can be sold and lent again, and a perfect repair earns a bonus.";
        if (gm.GoalActive && gm.Reputation < gm.ReputationGoal)
        {
            int left = gm.ReputationGoal - gm.Reputation, days = Mathf.Max(1, GameManager.EndingDay - gm.day);
            return $"The Dean wants {gm.ReputationGoal} reputation by Day {GameManager.EndingDay}. That's {left} more in {days} {(days == 1 ? "day" : "days")}: every visitor you serve well is +1.";
        }
        return "The popular books change every morning. Check the catalogue first thing and stock up on the ones that glow.";
    }

    void AddRow(string title, string sub, string button, bool enabled, System.Action onClick, bool popular = false, bool glow = false)
    {
        var row = new VisualElement();
        row.AddToClassList("shelf-row");
        if (glow) PopularGlow.Mark(row); // something you can afford to do here
        var text = new VisualElement();
        text.AddToClassList("shelf-row-text");
        text.Add(PopularGlow.TitleRow(new Label(title) { name = "row-title" }, popular, row));
        text.Add(new Label(sub) { name = "row-sub" });
        row.Add(text);
        var b = new Button(() => { onClick(); if (buySound != null) sfx.PlayOneShot(buySound, 0.7f); else GameAudio.Play(a => a.buy); RefreshReport(); RefreshPlan(); }) { text = button };
        b.AddToClassList("kit-button");
        b.SetEnabled(enabled);
        row.Add(b);
        list.Add(row);
    }

    int Price(BookData b) => GameManager.OrderPrice(b, false);

    void BuildShop(GameManager gm)
    {
        // Titles the library doesn't have yet are in the list too: ordering one adds it to the library.
        var catalogue = new System.Collections.Generic.List<BookData>(gm.Books);
        catalogue.AddRange(gm.NewTitles);
        catalogue.Sort((x, y) => gm.IsPopular(x) != gm.IsPopular(y) ? (gm.IsPopular(x) ? -1 : 1) : string.Compare(x.Title, y.Title, System.StringComparison.Ordinal));
        foreach (var b in catalogue)
        {
            if (BookRequest.IsRare(b) || !BookRequest.Available(b)) continue; // rare / locked-room books can't be bought
            if (filter != null && !filter.Matches(b)) continue;
            var book = b;
            int price = Price(book);
            var req = gm.reservations.Find(r => r.title == book.Title);
            string who = req != null ? $"  ·  requested by {req.name}" : "";
            AddRow(book.Title, (gm.IsNewTitle(book) ? $"New to the library  ·  {book.SubjectName}  ·  by {book.Author}" : $"{book.TypeName}  ·  {book.SubjectName}  ·  in stock {book.GoodCopies + book.WornCopies}")
                   + $"  ·  {price} coins · arrives tomorrow{who}",
                   "Buy", gm.Coins >= price,
                   () => gm.OrderBook(book, false), gm.IsPopular(book)); // arrives in the morning
        }
    }

    void BuildStaff(GameManager gm)
    {
        foreach (var role in GameManager.StaffOrder)
        {
            var (title, sub, button, enabled) = gm.StaffRow(role);
            AddRow(title, sub, button, enabled, () => gm.ToggleStaff(role), glow: gm.StaffActionReady(role));
        }
    }

    /// <summary>Today's audit log, newest first (the PC keeps earlier days).</summary>
    void BuildLog(GameManager gm)
    {
        var all = RequestLog.All;
        for (int i = all.Count - 1; i >= 0; i--)
            if (all[i].day == gm.day) list.Add(RequestLog.BuildRow(all[i]));
        if (list.childCount > 0) return;
        var empty = new Label("Nothing logged today.");
        empty.AddToClassList("log-empty");
        list.Add(empty);
    }

    void BuildUpgrades(GameManager gm)
    {
        for (int i = 0; i < GameManager.UpgradeNames.Length; i++)
        {
            var kind = (GameManager.UpgradeKind)i;
            int lvl = gm.Upgrade(kind);
            bool maxed = lvl >= GameManager.MaxUpgrade;
            int cost = GameManager.UpgradeCost(lvl);
            bool unlocked = gm.UpgradeUnlocked(kind);
            bool canBuy = gm.CanBuyUpgrade(kind);
            AddRow($"{GameManager.UpgradeNames[i]}  (Lv {lvl}/{GameManager.MaxUpgrade})",
                   GameManager.UpgradeInfo[i] + (maxed ? "" : !unlocked ? $"  Unlocks on Day {GameManager.UpgradeUnlockDay[i]}." : $"  {cost} coins"),
                   maxed ? "Max" : !unlocked ? $"Day {GameManager.UpgradeUnlockDay[i]}" : "Buy", canBuy, () => gm.BuyUpgrade(kind), glow: canBuy);
        }
    }
}
