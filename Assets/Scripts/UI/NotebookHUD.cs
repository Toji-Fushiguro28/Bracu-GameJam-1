using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// The librarian's notebook on the right edge: each request you've heard, its hint and your guesses
/// (wrong ones struck through). Entries leave when the task is done, or with their Clear button.
/// Below it, bottom right: today's reservations (starts collapsed; its tab lights up for a new one).
/// Has its own panel (sort order 25) so it stays readable over the PC screen. Made by <see cref="HUD"/>.
/// </summary>
[RequireComponent(typeof(UIDocument))]
public class NotebookHUD : MonoBehaviour
{
    const string CollapsedKey = "NotebookCollapsed";
    const int SortOrder = 25; // over the PC (20), under the main menu (30)

    /// <summary>The mouse is on the notebook: world clicks and dialogue skips should ignore it.</summary>
    public static bool PointerOver { get; private set; }

    VisualElement layer, book, reserves;
    ScrollView list, reserveList;
    Button tab, reserveTab;
    bool dirty = true;
    bool overBook, overReserves;
    string reserveSignature;
    float nextReserveCheck, nextPulse;
    /// <summary>The first new reservation of a session opens the list by itself; later ones just light the tab.</summary>
    static bool reservesShownOnce;

    /// <summary>Adds the notebook on a copy of the HUD's panel settings.</summary>
    public static void Create(UIDocument hud)
    {
        var tree = Resources.Load<VisualTreeAsset>("Notebook");
        if (tree == null || hud == null || hud.panelSettings == null) return;
        // A root object on purpose: a UIDocument under another one joins that one's panel and sort order.
        var go = new GameObject("Notebook");
        go.SetActive(false);
        var ps = Instantiate(hud.panelSettings);
        ps.name = "PS_Notebook";
        ps.sortingOrder = SortOrder;
        var doc = go.AddComponent<UIDocument>();
        doc.panelSettings = ps;
        doc.visualTreeAsset = tree;
        go.AddComponent<NotebookHUD>();
        go.SetActive(true);
    }

    void OnEnable()
    {
        var root = GetComponent<UIDocument>().rootVisualElement;
        layer = root.Q("notebook-layer");
        book = root.Q("notebook");
        list = root.Q<ScrollView>("notebook-list");
        tab = root.Q<Button>("notebook-tab");
        tab?.RegisterCallback<ClickEvent>(_ => SetCollapsed(!book.ClassListContains("collapsed")));
        book?.RegisterCallback<PointerEnterEvent>(_ => { overBook = true; PointerOver = true; });
        book?.RegisterCallback<PointerLeaveEvent>(_ => { overBook = false; PointerOver = overReserves; });

        reserves = root.Q("reservations");
        reserveList = root.Q<ScrollView>("reservations-list");
        reserveTab = root.Q<Button>("reservations-tab");
        reserveTab?.RegisterCallback<ClickEvent>(_ => SetReservesOpen(reserves.ClassListContains("collapsed")));
        reserves?.RegisterCallback<PointerEnterEvent>(_ => { overReserves = true; PointerOver = true; });
        reserves?.RegisterCallback<PointerLeaveEvent>(_ => { overReserves = false; PointerOver = overBook; });
        reserveSignature = null;
        GameAudio.HookClicks(root);
        book?.EnableInClassList("collapsed", PlayerPrefs.GetInt(CollapsedKey, 0) == 1);
        RequestLog.Changed += MarkDirty;
        dirty = true;
    }

    void OnDisable()
    {
        RequestLog.Changed -= MarkDirty;
        PointerOver = overBook = overReserves = false;
    }

    void Start()
    {
        if (GameManager.Instance != null) GameManager.Instance.Reserved += OnReserved;
    }

    void OnDestroy()
    {
        if (GameManager.Instance != null) GameManager.Instance.Reserved -= OnReserved;
        var ps = GetComponent<UIDocument>().panelSettings;
        if (ps != null) Destroy(ps);
    }

    void MarkDirty() => dirty = true;

    void SetCollapsed(bool collapsed)
    {
        book.EnableInClassList("collapsed", collapsed);
        PlayerPrefs.SetInt(CollapsedKey, collapsed ? 1 : 0);
    }

    void Update()
    {
        if (layer == null) return;
        // Out of the way on the title screen and behind the Day Report.
        bool show = !MainMenuUI.Active && (DayReportUI.Instance == null || !DayReportUI.Instance.IsOpen);
        layer.EnableInClassList("hidden", !show);
        if (!show) PointerOver = overBook = overReserves = false;
        if (dirty) Rebuild();
        // Orders and deliveries change a reservation's status, so look again now and then.
        if (Time.unscaledTime >= nextReserveCheck) { nextReserveCheck = Time.unscaledTime + 0.5f; RebuildReserves(); }
        // A new reservation makes the tab pulse until you open the list.
        if (reserveTab != null && reserveTab.ClassListContains("new") && Time.unscaledTime >= nextPulse)
        {
            nextPulse = Time.unscaledTime + 0.45f;
            reserveTab.ToggleInClassList("pulse");
        }
    }

    // ---------------- Reservations ----------------

    void OnReserved()
    {
        if (reserves == null) return;
        if (!reservesShownOnce) { reservesShownOnce = true; SetReservesOpen(true); }
        else if (reserves.ClassListContains("collapsed")) reserveTab.AddToClassList("new");
        nextReserveCheck = 0f; // show it straight away
    }

    void SetReservesOpen(bool open)
    {
        reserves.EnableInClassList("collapsed", !open);
        if (open) { reserveTab.RemoveFromClassList("new"); reserveTab.RemoveFromClassList("pulse"); }
    }

    /// <summary>Who's coming for what and when, and whether the book is ordered (same rules as the PC Shop).</summary>
    void RebuildReserves()
    {
        var gm = GameManager.Instance;
        if (gm == null || reserveList == null) return;
        var lines = new System.Collections.Generic.List<(string who, string when, bool coming)>();
        foreach (var r in gm.reservations)
        {
            var rb = gm.Books.Find(x => x.Title == r.title);
            bool coming = rb != null && (gm.OnOrder(rb) > 0 || BookRequest.InStock(rb) || (DeliveryCart.Instance != null && DeliveryCart.Instance.Has(rb)));
            if (coming) continue; // ordered (or on the shelf): nothing left to do, so it leaves the list
            string when = r.urgent ? $"Today by {GameManager.FormatHour(r.dueHour)} (express!)" : r.day <= gm.day ? "Today" : "Tomorrow";
            lines.Add(($"{r.name}: \"{r.title}\"", $"{when}  ·  {(coming ? "ready or on its way" : "NOT ORDERED YET")}", coming));
        }
        // Jobs you've taken on today (SpecialJobs): a VIP's book, the book club's copies.
        if (SpecialJobs.Instance != null)
            foreach (var (what, status, ready) in SpecialJobs.Instance.Lines()) lines.Add((what, status, ready));
        string signature = string.Join("|", lines);
        if (signature == reserveSignature) return;
        reserveSignature = signature;
        reserveList.Clear();
        if (reserveTab != null) reserveTab.text = lines.Count > 0 ? $"Reserved ({lines.Count})" : "Reserved";
        if (lines.Count == 0) { Line(reserveList, "nb-empty", gm.reservations.Count > 0 ? "Everything reserved is ordered or on the shelf." : "No reservations or jobs. Sold-out books can be reserved at the counter."); return; }
        foreach (var (who, when, coming) in lines)
        {
            var box = new VisualElement();
            box.AddToClassList("nb-entry");
            Line(box, "nb-line", who);
            Line(box, coming ? "nb-guess" : "nb-warn", when);
            reserveList.Add(box);
        }
    }

    void Rebuild()
    {
        dirty = false;
        if (list == null) return;
        list.Clear();
        var open = RequestLog.Open;
        if (tab != null) tab.text = open.Count > 0 ? $"Notes ({open.Count})" : "Notes";
        if (open.Count == 0)
        {
            Line(list, "nb-empty", "No requests yet. Talk to a visitor at the counter.");
            return;
        }
        for (int i = 0; i < open.Count; i++)
        {
            var e = open[i];
            var box = new VisualElement();
            box.AddToClassList("nb-entry");
            Line(box, "nb-line", $"{i + 1}. {RequestLog.KindName(e.kind)}" + (e.line != null ? $": \"{e.line}\"" : ""));
            if (e.hint != null) Line(box, "nb-hint", $"Hint: {e.hint}");
            foreach (var g in e.guesses) Line(box, "nb-guess", RequestLog.GuessText(g));
            var clear = new Button(() => RequestLog.Clear(e)) { text = "Clear" };
            clear.AddToClassList("kit-button");
            clear.AddToClassList("small");
            clear.AddToClassList("nb-clear");
            box.Add(clear);
            list.Add(box);
        }
    }

    static void Line(VisualElement parent, string cls, string text)
    {
        var l = new Label(text);
        l.AddToClassList(cls);
        parent.Add(l);
    }
}
