using UnityEngine;
using UnityEngine.InputSystem;
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

        reserves = root.Q("reservations");
        reserveList = root.Q<ScrollView>("reservations-list");
        reserveTab = root.Q<Button>("reservations-tab");
        reserveTab?.RegisterCallback<ClickEvent>(_ => SetReservesOpen(reserves.ClassListContains("collapsed")));
        reserveSignature = null;
        GameAudio.HookClicks(root);
        current = this;
        tucked = false;
        book?.EnableInClassList("collapsed", PlayerPrefs.GetInt(CollapsedKey, 0) == 1);
        RequestLog.Changed += MarkDirty;
        dirty = true;
    }

    void OnDisable()
    {
        RequestLog.Changed -= MarkDirty;
        PointerOver = false;
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

    /// <summary>Moves the HUD's "in your hands" box onto this panel (above the PC, shelf and repair backdrops).</summary>
    public static void Adopt(VisualElement el)
    {
        var root = current != null ? current.GetComponent<UIDocument>().rootVisualElement : null;
        if (el == null || root == null || el.parent == root) return;
        el.AddToClassList("carry-float");
        root.Add(el);
    }

    float fittedHeight = -1f;

    /// <summary>Both lists as tall as the screen allows (under the coin purse, down to the bottom edge):
    /// Notes and Reserved get half each, minus their titles and padding.</summary>
    void FitLists()
    {
        float h = layer.layout.height;
        if (float.IsNaN(h) || h <= 0f || Mathf.Abs(h - fittedHeight) < 1f) return;
        fittedHeight = h;
        float top = layer.ClassListContains("below-close") ? 280f : 104f; // same as padding-top in Notebook.uss
        float half = (h - top - 24f - 10f) * 0.5f; // the purse (or a work screen's ×) above, a margin at the bottom, the gap between
        if (list != null) list.style.maxHeight = Mathf.Max(140f, half - 100f);
        if (reserveList != null) reserveList.style.maxHeight = Mathf.Max(100f, half - 80f);
    }

    /// <summary>Is the mouse on the notebook or the reservations box?</summary>
    bool MouseOver()
    {
        var panel = book?.panel;
        if (panel == null || Mouse.current == null) return false;
        Vector2 screen = Mouse.current.position.ReadValue();
        screen.y = Screen.height - screen.y;
        Vector2 p = RuntimePanelUtils.ScreenToPanel(panel, screen);
        return book.worldBound.Contains(p) || (reserves != null && reserves.worldBound.Contains(p));
    }

    static NotebookHUD current;
    static bool tucked;

    /// <summary>Folds the notebook away for a moment (the tutorial is pointing at something behind it), without
    /// touching the player's own open/closed setting; false puts it back as they had it.</summary>
    public static void Tuck(bool tuck)
    {
        if (tucked == tuck) return;
        tucked = tuck;
        if (current != null && current.book != null)
            current.book.EnableInClassList("collapsed", tuck || PlayerPrefs.GetInt(CollapsedKey, 0) == 1);
    }

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
        // A work screen's × sits on its top-right corner, right where the notebook starts on narrower screens:
        // while one is open, the notebook drops below it so the × is fully clickable.
        bool working = (ShelfUI.Instance != null && ShelfUI.Instance.IsOpen) || (RepairUI.Instance != null && RepairUI.Instance.IsOpen) || NoticeBoardUI.IsOpen;
        if (working != layer.ClassListContains("below-close")) { layer.EnableInClassList("below-close", working); fittedHeight = -1f; }
        PointerOver = show && MouseOver();
        FitLists();
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
            // Requests are what someone said (in quotes); a "To do" is an instruction.
            Line(box, "nb-line", $"{i + 1}. {RequestLog.KindName(e.kind)}" + (e.line == null ? "" : e.kind == RequestLog.Kind.Task ? $": {e.line}" : $": \"{e.line}\""));
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
