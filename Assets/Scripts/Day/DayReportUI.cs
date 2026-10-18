using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// End of day (open book). Left page: the Day Report. Right page: evening planning with
/// Shop (buy copies), Staff (hire / let go), Upgrades and Log (today's requests) tabs. "Open Day N" starts the next day.
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
        tabs = new[] { doc.Q<Button>("tab-shop"), doc.Q<Button>("tab-staff"), doc.Q<Button>("tab-upgrades"), doc.Q<Button>("tab-log") };
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
        if (gm != null && gm.MorningVisitDue) { HUD.FadeOut(() => gm.RunMorningInterviews(() => { DoStartNextDay(); HUD.FadeIn(); })); return; }
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
        if (gm.WagesDueTomorrow > 0) Row("Staff wages (tomorrow)", $"-{gm.WagesDueTomorrow}");
        Row("Income", d.income.ToString(), true);
        int s = Stars(d);
        stars.text = new string('*', s) + new string('-', 5 - s);
        int delta = gm.Reputation - gm.ReputationAtStart;
        repLabel.text = $"Reputation {gm.Reputation} ({(delta >= 0 ? "+" : "")}{delta})" +
                        (cleanBonus > 0 ? "\nThe library is spotless!" : cleanBonus < 0 ? "\nThe library was left messy." : "");
        nextDay.text = $"Open Day {gm.day + 1}";
    }

    // ---------------- Evening planning ----------------

    void RefreshPlan()
    {
        var gm = GameManager.Instance;
        if (gm == null) return;
        for (int i = 0; i < tabs.Length; i++) tabs[i]?.EnableInClassList("selected", i == tab);
        coinsLabel.text = $"Coins: {gm.Coins}";
        list.Clear();
        filter?.Root.EnableInClassList("hidden", tab != 0);
        if (tab == 0) BuildShop(gm);
        else if (tab == 1) BuildStaff(gm);
        else if (tab == 2) BuildUpgrades(gm);
        else BuildLog(gm);
    }

    void AddRow(string title, string sub, string button, bool enabled, System.Action onClick)
    {
        var row = new VisualElement();
        row.AddToClassList("shelf-row");
        var text = new VisualElement();
        text.AddToClassList("shelf-row-text");
        text.Add(new Label(title) { name = "row-title" });
        text.Add(new Label(sub) { name = "row-sub" });
        row.Add(text);
        var b = new Button(() => { onClick(); if (buySound != null) sfx.PlayOneShot(buySound, 0.7f); else GameAudio.Play(a => a.buy); RefreshPlan(); }) { text = button };
        b.AddToClassList("kit-button");
        b.SetEnabled(enabled);
        row.Add(b);
        list.Add(row);
    }

    int Price(BookData b) => GameManager.OrderPrice(b, false);

    void BuildShop(GameManager gm)
    {
        foreach (var b in gm.Books)
        {
            if (BookRequest.IsRare(b) || !BookRequest.Available(b)) continue; // rare / locked-room books can't be bought
            if (filter != null && !filter.Matches(b)) continue;
            var book = b;
            int price = Price(book);
            AddRow(book.Title, $"{book.FormatName}  ·  {book.Subject}  ·  in stock {book.GoodCopies + book.WornCopies}  ·  {price} coins · arrives tomorrow",
                   "Buy", gm.Coins >= price,
                   () => gm.OrderBook(book, false)); // arrives in the morning
        }
    }

    void BuildStaff(GameManager gm)
    {
        foreach (var role in GameManager.StaffOrder)
        {
            int i = (int)role;
            bool hired = gm.Hired(role), open = gm.StaffUnlocked(role);
            bool booked = gm.InterviewBooked(role);
            AddRow(hired ? $"{GameManager.StaffNames[i]}: {GameManager.StaffPerson[i]}" : GameManager.StaffNames[i],
                   booked ? "Interview booked: two applicants come in tomorrow morning, before the library opens."
                          : $"{GameManager.StaffInfo[i]}  Wage about {GameManager.StaffWage[i]} / day.",
                   hired ? "Let go" : booked ? "Cancel" : open ? "Interview" : $"Day {GameManager.StaffUnlockDay[i]}",
                   hired || open, () => gm.ToggleStaff(role));
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
            AddRow($"{GameManager.UpgradeNames[i]}  (Lv {lvl}/{GameManager.MaxUpgrade})",
                   GameManager.UpgradeInfo[i] + (maxed ? "" : $"  {cost} coins"),
                   maxed ? "Max" : "Buy", !maxed && gm.Coins >= cost, () => gm.BuyUpgrade(kind));
        }
    }
}
