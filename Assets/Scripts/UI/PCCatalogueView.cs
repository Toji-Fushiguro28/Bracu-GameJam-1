using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// PC "Catalogue": every title (owned ones and new titles you could order), with Type / Subject / Show filters,
/// and a details card where you add it to your note or order copies (Order: tomorrow; Express: 2 hours, +50%).
/// Search matches titles, authors and tags only, so vague requests still take reading abstracts.
/// </summary>
public static class PCCatalogueView
{
    static int type, show;
    static string subject;
    static string search = "";
    static BookData selected;
    static string msg;
    static bool msgGood;

    static VisualElement root;
    static ListView list;
    static List<BookData> shown = new List<BookData>();
    static VisualTreeAsset rowTemplate;

    /// <summary>A normal open of the computer: All books, empty search, nothing picked.</summary>
    public static void ResetFilters()
    {
        type = show = 0;
        subject = null;
        search = "";
        selected = null;
        msg = null;
    }

    static GameManager GM => GameManager.Instance;

    /// <summary>Copies can be ordered: not Rare Books, and not a room that's still locked.</summary>
    static bool CanOrder(BookData b) => b != null && !BookRequest.IsRare(b) && BookRequest.Available(b);

    /// <summary>The rare book a VIP asked you to buy: the rare dealer sells it here until you do.</summary>
    static bool Dealer(BookData b) => b != null && SpecialJobs.Instance != null && SpecialJobs.Instance.WantedRare == b;
    static bool NotOwned(BookData b) => GM != null && (GM.IsNewTitle(b) || Dealer(b));

    public static void Wire(VisualElement page)
    {
        root = page;
        rowTemplate = Resources.Load<VisualTreeAsset>("PCBookItem");
        var field = root.Q<TextField>("book-search");
        var placeholder = root.Q<Label>("search-placeholder");
        if (field != null)
        {
            field.SetValueWithoutNotify(search);
            void Placeholder() { if (placeholder != null) placeholder.style.display = string.IsNullOrEmpty(field.value) ? DisplayStyle.Flex : DisplayStyle.None; }
            field.RegisterValueChangedCallback(e => { search = e.newValue ?? ""; Placeholder(); Apply(); });
            Placeholder();
        }

        list = root.Q<ListView>("books-list");
        if (list != null)
        {
            list.makeItem = () =>
            {
                var row = rowTemplate.CloneTree();
                row.style.flexGrow = 1;
                // Clicking a row always shows it, even if the list thinks it's already selected.
                row.RegisterCallback<ClickEvent>(_ => { if (row.userData is BookData b) { msg = null; ShowDetails(b); } });
                return row;
            };
            list.bindItem = BindRow;
            // Rows grow when a long title wraps onto a second line.
            list.virtualizationMethod = CollectionVirtualizationMethod.DynamicHeight;
            list.fixedItemHeight = 104;
            list.selectionType = SelectionType.Single;
        }

        WireFilters();
        root.Q<Button>("detail-select")?.RegisterCallback<ClickEvent>(_ =>
        {
            if (selected == null) return;
            RequestLog.AddToNote(selected);
            msg = $"Noted: \"{selected.Title}\" ({ShelfInteractable.NameOf(selected.ShelfLocation)})";
            msgGood = true;
            ShowDetails(selected);
        });
        root.Q<Button>("detail-buy")?.RegisterCallback<ClickEvent>(_ => Order(false));
        root.Q<Button>("detail-express")?.RegisterCallback<ClickEvent>(_ => Order(true));
        Apply();
        ShowDetails(selected);
    }

    static void Order(bool express)
    {
        var gm = GM;
        var b = selected;
        if (!express && Tutorial.ExpressOnly) return;
        if (gm == null) return;
        if (Dealer(b))
        {
            if (!gm.BuyRare(b)) return;
            GameAudio.Play(a => a.buy);
            msg = "Ordered from the rare dealer! It arrives on the book cart tomorrow morning.";
            msgGood = true;
            Apply();
            ShowDetails(b);
            return;
        }
        if (!CanOrder(b)) return;
        if (!gm.OrderBook(b, express)) return;
        GameAudio.Play(a => a.buy);
        msg = express ? $"Express ordered! It reaches the book cart at {GameManager.FormatHour(gm.Hour + GameManager.ExpressHours)}."
                      : $"Ordered! It arrives on the book cart tomorrow morning ({gm.OnOrder(b)} on order).";
        msgGood = true;
        Apply();
        ShowDetails(b);
    }

    // ---------------- Filters and list ----------------

    static void WireFilters()
    {
        var typeButton = root.Q<Button>("filter-type");
        var subjectButton = root.Q<Button>("filter-subject");
        var showButton = root.Q<Button>("filter-show");
        void Labels()
        {
            if (typeButton != null) typeButton.text = "Type: " + BookFilterBar.TypeNames[type];
            if (subjectButton != null)
            {
                subjectButton.text = "Subject: " + BookFilterBar.SubjectLabel(subject);
                subjectButton.SetEnabled(true); // no need to pick a type first
            }
            if (showButton != null) showButton.text = "Show: " + BookFilterBar.Shows[show];
        }
        typeButton?.RegisterCallback<ClickEvent>(_ => PickList.Open(typeButton, BookFilterBar.TypeNames, type, i => { type = i; subject = null; Labels(); Apply(); }, true));
        subjectButton?.RegisterCallback<ClickEvent>(_ => BookFilterBar.OpenSubjects(subjectButton, BookFilterBar.TypeNames[type], subject, s => { subject = s; Labels(); Apply(); }, true));
        showButton?.RegisterCallback<ClickEvent>(_ => PickList.Open(showButton, BookFilterBar.Shows, show, i => { show = i; Labels(); Apply(); }, true));
        Labels();
    }

    static void Apply()
    {
        var gm = GM;
        string q = search.Trim().ToLowerInvariant();
        bool Popular(BookData b) => gm != null && gm.IsPopular(b);
        bool New(BookData b) => NotOwned(b);
        shown = BookFilterBar.Everything.Where(b =>
                (q.Length == 0 || Matches(b, q)) &&
                BookFilterBar.TypeMatches(b, BookFilterBar.TypeNames[type], subject) &&
                BookFilterBar.ShowMatches(show, b))
            .OrderBy(b => Dealer(b) ? -1 : New(b) ? 1 : 0).ThenBy(b => Popular(b) ? 0 : 1) // a VIP's rare book on top, popular next, titles you don't have yet last
            .ToList();
        var count = root.Q<Label>("book-count");
        if (count != null) count.text = shown.Count == 1 ? "1 book" : shown.Count + " books";
        if (list == null) return;
        // A new search: drop the old row index, but keep the picked book highlighted if it's still listed.
        list.SetSelectionWithoutNotify(System.Array.Empty<int>());
        list.itemsSource = shown;
        list.Rebuild();
        int keep = selected != null ? shown.IndexOf(selected) : -1;
        if (keep >= 0) list.SetSelectionWithoutNotify(new[] { keep });
    }

    /// <summary>The search looks at the title, author, tags, subject and the description (so a word from an
    /// "about" request finds the book).</summary>
    static bool Matches(BookData b, string q) =>
        (b.Title ?? "").ToLowerInvariant().Contains(q) || (b.Author ?? "").ToLowerInvariant().Contains(q) ||
        (b.Tag ?? "").ToLowerInvariant().Contains(q) || (b.SubjectName ?? "").ToLowerInvariant().Contains(q) ||
        (b.Abstract ?? "").ToLowerInvariant().Contains(q);

    static void BindRow(VisualElement el, int i)
    {
        if (i < 0 || i >= shown.Count) return;
        var gm = GM;
        var b = shown[i];
        el.userData = b;
        var row = el.Q("book-item");
        bool popular = gm != null && gm.IsPopular(b);
        bool notOwned = NotOwned(b);
        row.EnableInClassList("epic", b.Tier == BookTier.Epic);
        row.EnableInClassList("legendary", b.Tier == BookTier.Legendary);
        row.EnableInClassList("popular", popular && b.Tier == BookTier.Normal);

        el.Q<Label>("book-title").text = b.Title;
        var badges = el.Q("book-badges");
        badges.Clear();
        if (popular) badges.Add(Badge("Popular", null));
        var res = gm != null ? gm.reservations.Find(r => r.title == b.Title) : null;
        if (res != null) badges.Add(Badge(res.urgent && res.day == gm.day ? "Reserved · TODAY" : "Reserved", res.urgent && res.day == gm.day ? "urgent" : null));
        if (Dealer(b)) badges.Add(Badge("VIP request", "urgent"));
        else if (notOwned) badges.Add(Badge("New title", "new"));

        el.Q<Label>("book-sub").text = $"{b.Author}  ·  {b.SubjectName}";
        el.Q<Label>("meta-shelf").text = ShelfInteractable.NameOf(b.ShelfLocation);
        bool priced = b.Sellable && !BookRequest.IsRare(b); // rare books are read here, never sold
        Show(el, "meta-price-sep", priced); Show(el, "meta-coin", priced); Show(el, "meta-price", priced);
        if (priced) el.Q<Label>("meta-price").text = b.SaleValue.ToString(); // what a copy sells for
        bool dmg = b.DamagedCopies > 0;
        Show(el, "meta-dmg-sep", dmg); Show(el, "meta-dmg-icon", dmg); Show(el, "meta-dmg", dmg);
        if (dmg) el.Q<Label>("meta-dmg").text = $"{b.DamagedCopies} damaged";

        var n = el.Q<Label>("stock-n");
        var sub = el.Q<Label>("stock-sub");
        int onOrder = gm != null ? gm.OnOrder(b) : 0;
        if (notOwned) { n.text = ""; sub.text = "not in library"; }
        else
        {
            n.text = b.Quantity.ToString();
            sub.text = onOrder > 0 ? $"+{onOrder} coming" : "on shelf";
        }
        n.EnableInClassList("bad", !notOwned && b.Quantity == 0);
        n.EnableInClassList("warn", !notOwned && b.Quantity > 0 && (b.Quantity == 1 || b.WornCopies + b.DamagedCopies > 0));
        n.EnableInClassList("ok", !notOwned && b.Quantity > 1 && b.WornCopies + b.DamagedCopies == 0);
    }

    static void Show(VisualElement el, string name, bool on)
    {
        var e = el.Q(name);
        if (e != null) e.style.display = on ? DisplayStyle.Flex : DisplayStyle.None;
    }

    static Label Badge(string text, string cls)
    {
        var l = new Label(text) { pickingMode = PickingMode.Ignore };
        l.AddToClassList("badge");
        if (cls != null) l.AddToClassList(cls);
        return l;
    }

    static Label Chip(string text)
    {
        var l = new Label(text) { pickingMode = PickingMode.Ignore };
        l.AddToClassList("chip");
        return l;
    }

    // ---------------- Details card ----------------

    static void ShowDetails(BookData b)
    {
        selected = b;
        if (b != null) PCOverlayTrigger.LastViewed = b;
        var empty = root.Q("detail-empty");
        var body = root.Q("detail-body");
        var pane = root.Q("book-details");
        if (empty == null || body == null) return;
        empty.style.display = b == null ? DisplayStyle.Flex : DisplayStyle.None;
        body.style.display = b == null ? DisplayStyle.None : DisplayStyle.Flex;
        pane?.EnableInClassList("epic", b != null && b.Tier == BookTier.Epic);
        pane?.EnableInClassList("legendary", b != null && b.Tier == BookTier.Legendary);
        if (b == null) return;

        var gm = GM;
        bool notOwned = NotOwned(b);
        bool dealer = Dealer(b);
        bool popular = gm != null && gm.IsPopular(b);
        bool soldOut = !notOwned && !BookRequest.InStock(b);

        Set("detail-callno", b.ShelfLocation);
        Set("detail-title", b.Title);
        Set("detail-author", "by " + b.Author);
        var stamp = root.Q<Label>("detail-stamp");
        string stampText = dealer ? null : notOwned ? "NEW TITLE" : soldOut ? "SOLD OUT" : popular ? "POPULAR" : null;
        stamp.style.display = stampText != null ? DisplayStyle.Flex : DisplayStyle.None;
        stamp.text = stampText ?? "";
        stamp.EnableInClassList("gold", stampText != "SOLD OUT");

        var tags = root.Q("detail-tags");
        tags.Clear();
        tags.Add(Chip(b.TypeName));
        tags.Add(Chip(b.SubjectName));
        foreach (var t in (b.Tag ?? "").Split(',').Select(t => t.Trim()).Where(t => t.Length > 0)) tags.Add(Chip(t));
        if (b.Tier != BookTier.Normal)
        {
            var tier = Badge(b.Rarity, b.Tier == BookTier.Epic ? "epic" : "legendary");
            tier.style.marginLeft = 0;
            tags.Add(tier);
        }

        Set("detail-shelf", ShelfInteractable.NameOf(b.ShelfLocation));
        Set("detail-stock", notOwned ? "not in library" : b.Quantity > 0 ? b.Quantity + " on shelf" : "none left", soldOut ? "bad" : null);
        Set("detail-condition", b.Quantity > 0 ? b.ConditionSummary : "-", b.DamagedCopies > 0 ? "bad" : b.WornCopies > 0 ? "warn" : null);
        Set("detail-sale", !b.Sellable ? "read here only" : b.SaleValue + " coins" + (b.NewRelease ? " (new title +30%)" : ""), !b.Sellable ? "warn" : b.NewRelease ? "ok" : null);
        root.Q("detail-condition-icon")?.EnableInClassList("damaged", b.DamagedCopies > 0);
        // Status row (only when there's something to say): who's waiting for it, what's on its way, if it's in demand.
        var status = new List<string>();
        var who = gm != null ? gm.reservations.Find(r => r.title == b.Title) : null;
        if (who != null) status.Add($"reserved for {who.name}");
        int arriving = gm != null ? gm.OnOrder(b) : 0;
        if (arriving > 0) status.Add($"{arriving} on order");
        if (dealer) status.Add("VIP request");
        if (popular) status.Add("popular today");
        var statusRow = root.Q("detail-status-row");
        if (statusRow != null) statusRow.style.display = status.Count > 0 ? DisplayStyle.Flex : DisplayStyle.None;
        Set("detail-status", string.Join(", ", status), who != null && arriving == 0 && !BookRequest.InStock(b) ? "bad" : null);
        Set("detail-abstract", b.Abstract);

        // Someone's waiting for it: who, by when, and whether it's on its way.
        var res = gm != null ? gm.reservations.Find(r => r.title == b.Title) : null;
        var reserve = root.Q<Label>("detail-reserve");
        bool coming = res != null && !PCHomeView.NeedsOrder(res);
        reserve.style.display = res != null ? DisplayStyle.Flex : DisplayStyle.None;
        if (res != null)
        {
            bool today = res.urgent && res.day == gm.day;
            reserve.text = $"{res.name} reserved this. " + (today ? $"Needs it TODAY by {GameManager.FormatHour(res.dueHour)}, so use Express." : "Picks it up tomorrow.")
                           + (coming ? " On its way." : " NOT ORDERED YET.");
            reserve.EnableInClassList("done", coming);
        }

        // Buttons
        bool closed = gm != null && gm.CurrentPhase == GameManager.Phase.Closed;
        bool orderable = gm != null && (CanOrder(b) || dealer) && Tutorial.AllowsOrder(b); // the dealer delivers tomorrow, so any time today
        int price = dealer ? GameManager.RarePrice(b) : orderable ? GameManager.OrderPrice(b, false) : 0, express = orderable && !dealer ? GameManager.OrderPrice(b, true) : 0;
        var note = root.Q<Button>("detail-select");
        note.SetEnabled(!notOwned && !RequestLog.IsNoted(b));
        var buy = root.Q<Button>("detail-buy");
        var exp = root.Q<Button>("detail-express");
        buy.style.display = orderable ? DisplayStyle.Flex : DisplayStyle.None;
        exp.style.display = orderable && !dealer ? DisplayStyle.Flex : DisplayStyle.None; // the dealer's courier is one price
        root.Q<Label>("buy-price").text = price.ToString();
        root.Q<Label>("express-price").text = express.ToString();
        buy.SetEnabled(orderable && gm.Coins >= price && !Tutorial.ExpressOnly); // the tutorial's order is Express
        exp.SetEnabled(orderable && !closed && gm.Coins >= express);
        bool needs = res != null && !coming;
        buy.EnableInClassList("primary", dealer || (needs && !(res.urgent && res.day == gm.day)));
        exp.EnableInClassList("primary", needs && res.urgent && res.day == gm.day && !closed);
        exp.tooltip = closed ? "Express only runs while the library is open." : "Arrives in 2 hours.";

        // The line under the buttons
        string text = msg;
        if (text == null)
        {
            int onOrder = gm != null ? gm.OnOrder(b) : 0;
            text = dealer ? $"Rare dealer: {SpecialJobs.Instance.VipName} asked for this (VIP request) and repays you when they come to read it. It arrives on the book cart tomorrow morning, and stays in Rare Books."
                 : BookRequest.IsRare(b) ? "Rare Books stay in the library: they can't be ordered."
                 : !BookRequest.Available(b) ? "This room isn't open yet, so copies can't be ordered."
                 : notOwned ? "New title: costs a little more to order, and sells for more."
                 : closed && orderable ? "The library is closed: Express is off until tomorrow. Normal orders still arrive in the morning."
                 : onOrder > 0 ? $"{onOrder} on order. Normal orders arrive tomorrow on the book cart."
                 : RequestLog.IsNoted(b) ? "Already in your notebook."
                 : RequestLog.Current != null ? "Add to note: goes on the visitor's request in your notebook."
                 : "Add to note: nobody's asking, so it goes on your To Shelve list.";
            if (orderable && gm.Coins < price) text += $" Ordering needs {price} coins.";
        }
        var line = root.Q<Label>("detail-select-note");
        line.text = text;
        line.EnableInClassList("good", msg != null && msgGood);
    }

    static void Set(string name, string text, string state = null)
    {
        var l = root.Q<Label>(name);
        if (l == null) return;
        l.text = text;
        l.EnableInClassList("warn", state == "warn");
        l.EnableInClassList("bad", state == "bad");
        l.EnableInClassList("ok", state == "ok");
    }
}
