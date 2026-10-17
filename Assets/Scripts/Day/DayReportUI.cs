using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// End of day (open book). Left page: the Day Report. Right page: evening planning with
/// Shop (buy copies), Staff (hire / let go) and Upgrades tabs. "Open Day N" starts the next day.
/// </summary>
[RequireComponent(typeof(UIDocument))]
public class DayReportUI : MonoBehaviour
{
    public static DayReportUI Instance { get; private set; }

    [Tooltip("Shop price as a share of the book's value.")]
    public float shopPriceShare = 0.6f;
    public AudioClip buySound;

    VisualElement root, list;
    Label stats, stars, repLabel, coinsLabel;
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
        stats = doc.Q<Label>("report-stats");
        stars = doc.Q<Label>("report-stars");
        repLabel = doc.Q<Label>("report-rep");
        coinsLabel = doc.Q<Label>("plan-coins");
        nextDay = doc.Q<Button>("report-next");
        tabs = new[] { doc.Q<Button>("tab-shop"), doc.Q<Button>("tab-staff"), doc.Q<Button>("tab-upgrades") };
        for (int i = 0; i < tabs.Length; i++)
        {
            int t = i;
            tabs[i]?.RegisterCallback<ClickEvent>(_ => { tab = t; RefreshPlan(); });
        }
        nextDay?.RegisterCallback<ClickEvent>(_ => StartNextDay());
        doc.Q<Button>("report-close")?.RegisterCallback<ClickEvent>(_ => Close());
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
        stats.text =
            $"Visitors served      {d.served}\n" +
            $"Left unhappy         {d.angry}\n" +
            $"Books sold           {d.booksSold}\n" +
            $"Books read           {d.booksRead}\n" +
            $"Returns checked in   {d.returns}\n" +
            $"Repairs              {d.repairsPerfect} perfect, {d.repairsNormal} normal\n" +
            $"Things tidied        {d.cleaned}\n" +
            $"New members          {d.signups}\n" +
            $"Tips                 {d.tips}\n" +
            $"Income               {d.income}\n" +
            (gm.DailyWages > 0 ? $"Staff wages (morning) -{gm.DailyWages}\n" : "");
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
        if (tab == 0) BuildShop(gm);
        else if (tab == 1) BuildStaff(gm);
        else BuildUpgrades(gm);
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
            AddRow(GameManager.StaffNames[i], $"{GameManager.StaffInfo[i]}  Wage {GameManager.StaffWage[i]} / day.",
                   hired ? "Let go" : open ? "Hire" : $"Day {GameManager.StaffUnlockDay[i]}", hired || open, () => gm.ToggleStaff(role));
        }
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
