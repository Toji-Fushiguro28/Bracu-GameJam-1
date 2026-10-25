using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// PC "Home": six numbers at a glance, "Needs you" (only things you can fix from the PC, each with a button
/// to the right tab), the day bar (pickups, Express deliveries and closing as dots with a tooltip), reputation,
/// today's popular books, and the Today / Collection / Library ledgers.
/// </summary>
public static class PCHomeView
{
    const int MaxNeeds = 3;
    static ScrollView home;
    static string signature;
    static VisualElement tip;

    static GameManager GM => GameManager.Instance;

    /// <summary>A reservation with no copy for this person (none on order, on the shelf or the cart): it needs you.</summary>
    public static bool NeedsOrder(GameManager.Reservation r)
    {
        var gm = GM;
        return gm != null && gm.FindTitle(r.title) != null && !gm.ReservationCovered(r);
    }

    public static void Wire(VisualElement page)
    {
        home = page.Q<ScrollView>("home");
        signature = null;
        Build();
        // Rebuild when something on the page changed; the "now" marker just slides.
        home.schedule.Execute(() =>
        {
            if (Signature() != signature) Build();
            else UpdateNow();
        }).Every(1000);
    }

    /// <summary>Everything the page shows apart from the clock: rebuild only when this changes.</summary>
    static string Signature()
    {
        var gm = GM;
        if (gm == null) return "";
        var t = gm.Today;
        var spawner = CustomerSpawner.Instance;
        return string.Join("|", gm.Coins, gm.Reputation, t.served, t.income, t.booksSold, t.booksRead, t.returns, t.angry, Members.All.Count,
            gm.Books.Count, gm.Books.Sum(b => b.Quantity), gm.Books.Sum(b => gm.OnOrder(b)), gm.reservations.Count, gm.reservations.Count(NeedsOrder),
            Customer.WaitingForCard != null, gm.UpgradeReady, gm.StaffReady, GhostLetter.CanSell, spawner != null ? spawner.PickupTimes.Count : 0, gm.ArrivingToday.Count());
    }

    static void Build()
    {
        var gm = GM;
        if (home == null || gm == null) return;
        HideTip();
        float scroll = home.scrollOffset.y;
        home.Clear();
        signature = Signature();

        // "Needs you" first: the one thing on this page worth reading every time
        home.Add(NeedsPanel());

        // Four numbers, each with its pixel icon
        var owned = gm.Books;
        var tiles = Row("tiles4");
        tiles.Add(Tile(gm.Today.served.ToString(), "visitors served", "si-heart"));
        tiles.Add(Tile(gm.Today.income.ToString(), "coins today", "si-coin", "ok"));
        tiles.Add(Tile(owned.Sum(b => b.Quantity).ToString(), "copies on shelf", "si-shelf"));
        int soldOut = owned.Count(b => b.Sellable && !BookRequest.InStock(b));
        tiles.Add(Tile(soldOut.ToString(), "sold out", "si-cart", soldOut > 0 ? "bad" : null));
        home.Add(tiles);

        // Day bar and reputation on the left, popular books on the right
        var mid = Row("mid");
        var left = new VisualElement();
        left.AddToClassList("mid-left");
        left.Add(DayPanel());
        left.Add(ReputationPanel());
        mid.Add(left);
        var right = new VisualElement();
        right.AddToClassList("mid-right");
        right.Add(PopularPanel());
        mid.Add(right);
        home.Add(mid);

        home.schedule.Execute(() => home.scrollOffset = new Vector2(0, scroll));
    }

    // ---------------- Needs you ----------------

    static VisualElement NeedsPanel()
    {
        var gm = GM;
        var items = new List<(string icon, string text, string button, Action go)>();
        var pc = PCOverlayTrigger.Current;
        foreach (var r in gm.reservations.Where(NeedsOrder))
        {
            bool today = r.urgent && r.day == gm.day;
            items.Add((today ? "si-clock" : "si-cart",
                $"{r.name} needs <b>\"{r.title}\"</b> {(today ? "by " + GameManager.FormatHour(r.dueHour) : "tomorrow")}  <color=#7D6650>· not ordered yet{(today ? " · use Express" : "")}</color>",
                "Order", () => pc?.ShowTab("catalogue", true)));
        }
        if (Customer.WaitingForCard != null)
            items.Add(("si-scroll", "A visitor is waiting for a library card  <color=#7D6650>· at the counter</color>", "Fill in card", () => pc?.ShowTab("members")));
        if (gm.UpgradeReady)
            items.Add(("si-hammer", $"An upgrade is affordable  <color=#7D6650>· {gm.Coins} coins</color>", "See upgrades", () => pc?.ShowShop(0)));
        if (gm.StaffReady)
            items.Add(("si-broom", "Staff: an interview or training you can afford", "See staff", () => pc?.ShowShop(1)));
        if (GhostLetter.CanSell)
            items.Add(("si-coin", $"A collector will pay <b>1000 coins</b> for <b>\"{GhostLetter.ChapterTitle}\"</b>", "Sell", GhostLetter.Sell));

        // Nothing to do: one slim green line. Something to do: the warm card, bigger, with a count.
        if (items.Count == 0)
        {
            var clear = Row("needs-clear");
            clear.AddToClassList("panel");
            clear.Add(Icon("si-tick"));
            var l = new Label("All clear. Nothing needs you right now.");
            l.AddToClassList("allclear");
            clear.Add(l);
            return clear;
        }
        var panel = Panel("NEEDS YOU", items.Count + " to do", true);
        panel.AddToClassList("needs-hot");
        foreach (var it in items.Take(MaxNeeds))
        {
            var row = Row("need");
            row.Add(Icon(it.icon));
            var text = new Label(it.text) { enableRichText = true };
            text.AddToClassList("need-text");
            row.Add(text);
            var go = it.go;
            var b = new Button(() => go()) { text = it.button };
            b.AddToClassList("pc-button");
            b.AddToClassList("small");
            row.Add(b);
            panel.Add(row);
        }
        if (items.Count > MaxNeeds) panel.Add(Small($"+{items.Count - MaxNeeds} more"));
        return panel;
    }

    // ---------------- Day bar ----------------

    static VisualElement fill, now;
    static Label left;

    static float Open => GM != null ? GM.openHour : 9f;
    static float Close => GM != null ? GM.closingHour : 17.5f;
    static float X(float hour) => Mathf.Clamp01((hour - Open) / (Close - Open)) * 100f;

    static string LeftText()
    {
        var gm = GM;
        if (gm == null) return "";
        if (gm.CurrentPhase != GameManager.Phase.Open || gm.Hour >= Close) return "Closed: tidy up, then lock up at the front ropes";
        int mins = Mathf.FloorToInt((Close - gm.Hour) * 60f / 10f) * 10;
        return $"{mins / 60}h {mins % 60}m until closing";
    }

    static VisualElement DayPanel()
    {
        var gm = GM;
        var panel = Panel("TODAY", null, false, "si-clock");
        left = Small(LeftText());
        panel.Q(className: "panel-head").Add(left);
        var tl = new VisualElement();
        tl.AddToClassList("tl");
        var track = new VisualElement();
        track.AddToClassList("tl-track");
        fill = new VisualElement();
        fill.AddToClassList("tl-fill");
        track.Add(fill);
        tl.Add(track);
        for (int h = Mathf.CeilToInt(Open); h <= Mathf.FloorToInt(Close); h++)
        {
            var tick = new Label($"{(h % 12 == 0 ? 12 : h % 12)} {(h < 12 ? "AM" : "PM")}");
            tick.AddToClassList("tl-tick");
            tick.style.left = Length.Percent(X(h));
            tl.Add(tick);
        }
        now = new VisualElement();
        now.AddToClassList("tl-now");
        tl.Add(now);

        // Events, grouped by 10-minute slot so a busy day never piles up
        var events = new List<(float hour, string colour, string who, string what, string state)>();
        foreach (var r in gm.reservations.Where(r => r.urgent && r.day == gm.day && !r.queued))
        {
            bool ok = !NeedsOrder(r);
            events.Add((r.dueHour, ok ? "ok" : "bad", $"{r.name} picks up", $"\"{r.title}\"", ok ? "On its way" : "Not ordered yet: use Express"));
        }
        if (CustomerSpawner.Instance != null)
            foreach (var (r, hour) in CustomerSpawner.Instance.PickupTimes)
            {
                var b = gm.FindTitle(r.title);
                bool ready = b != null && (BookRequest.InStock(b) || (DeliveryCart.Instance != null && DeliveryCart.Instance.Has(b)));
                events.Add((hour, ready ? "ok" : "bad", $"{r.name} picks up", $"\"{r.title}\"", ready ? "Ready on the shelf" : "Not on the shelf yet"));
            }
        foreach (var (book, hour) in gm.ArrivingToday)
            events.Add((hour, "ok", "Express delivery", $"\"{book.Title}\"", "Goes to the book cart"));
        events.Add((Close, "warn", "Closing time", "Tidy up, then lock up at the front ropes", null));

        foreach (var slot in events.GroupBy(e => Mathf.FloorToInt(e.hour * 6f)).OrderBy(g => g.Key))
        {
            var list = slot.ToList();
            var dot = new VisualElement();
            dot.AddToClassList("tl-dot");
            string worst = list.Any(e => e.colour == "bad") ? "bad" : list.Any(e => e.colour == "warn") ? "warn" : "ok";
            dot.style.backgroundColor = Colour(worst);
            dot.style.left = Length.Percent(X(list[0].hour));
            if (list.Count > 1)
            {
                var n = new Label(list.Count.ToString());
                n.AddToClassList("tl-count");
                dot.Add(n);
            }
            float hour = slot.Key / 6f;
            dot.RegisterCallback<PointerEnterEvent>(_ => ShowTip(dot, hour, list));
            dot.RegisterCallback<PointerLeaveEvent>(_ => HideTip());
            tl.Add(dot);
        }
        panel.Add(tl);
        UpdateNow();
        return panel;
    }

    static void UpdateNow()
    {
        var gm = GM;
        if (gm == null || now == null) return;
        float x = X(gm.Hour);
        now.style.left = Length.Percent(x);
        fill.style.width = Length.Percent(x);
        if (left != null) left.text = LeftText();
    }

    static void ShowTip(VisualElement dot, float hour, List<(float hour, string colour, string who, string what, string state)> list)
    {
        HideTip();
        var layer = dot.panel?.visualTree.Q("container");
        if (layer == null) return;
        tip = new VisualElement { pickingMode = PickingMode.Ignore };
        tip.AddToClassList("tip");
        var time = new Label(GameManager.FormatHour(hour));
        time.AddToClassList("tip-time");
        time.AddToClassList("font-title");
        tip.Add(time);
        bool first = true;
        foreach (var e in list)
        {
            var ev = new VisualElement();
            ev.AddToClassList("tip-ev");
            if (first) ev.AddToClassList("first");
            first = false;
            ev.Add(TipLine(e.who, null));
            ev.Add(TipLine(e.what, "dim"));
            if (e.state != null) ev.Add(TipLine(e.state, e.colour));
            tip.Add(ev);
        }
        var b = layer.WorldToLocal(dot.worldBound);
        tip.style.left = Mathf.Clamp(b.center.x - 130f, 8f, layer.layout.width - 270f);
        tip.style.top = b.yMax + 10f;
        layer.Add(tip);
    }

    static Label TipLine(string text, string cls)
    {
        var l = new Label(text);
        l.AddToClassList("tip-line");
        if (cls != null) l.AddToClassList(cls);
        return l;
    }

    public static void HideTip()
    {
        tip?.RemoveFromHierarchy();
        tip = null;
    }

    // ---------------- Reputation and popular ----------------

    static VisualElement ReputationPanel()
    {
        var gm = GM;
        var panel = Panel("REPUTATION", gm.GoalActive ? $"goal {gm.ReputationGoal}" : null, false, "si-star");
        if (!gm.GoalActive) { panel.Add(Small($"{gm.Reputation} reputation")); return panel; }
        var meter = new VisualElement();
        meter.AddToClassList("meter");
        var bar = new VisualElement();
        bar.AddToClassList("meter-fill");
        bar.style.width = Length.Percent(Mathf.Clamp01(gm.Reputation / (float)gm.ReputationGoal) * 100f);
        meter.Add(bar);
        var tier = new VisualElement();
        tier.AddToClassList("meter-tier");
        tier.style.left = Length.Percent(60f); // "close" starts at 60% of the goal
        meter.Add(tier);
        panel.Add(meter);
        var line = Row("meter-l");
        line.Add(Small($"{gm.Reputation} now"));
        int toGo = gm.ReputationGoal - gm.Reputation;
        line.Add(Small(toGo > 0 ? $"{toGo} to go · {Mathf.Max(0, gm.GoalDay - gm.day + 1)} days left" : "Goal reached!"));
        panel.Add(line);
        return panel;
    }

    static VisualElement PopularPanel()
    {
        var gm = GM;
        var panel = Panel("POPULAR TODAY", "sell well today", false, "si-books");
        bool first = true;
        foreach (var title in gm.PopularToday)
        {
            var b = gm.FindTitle(title);
            if (b == null) continue;
            var row = Row("pop-row");
            if (first) row.AddToClassList("first");
            first = false;
            var text = new VisualElement();
            text.AddToClassList("pop-text");
            var t = new Label(b.Title);
            t.AddToClassList("pop-title");
            text.Add(t);
            var s = new Label($"{b.SubjectName} · {ShelfInteractable.NameOf(b.ShelfLocation)}");
            s.AddToClassList("pop-sub");
            text.Add(s);
            row.Add(text);
            var n = new Label(gm.IsNewTitle(b) ? "-" : b.Quantity.ToString());
            n.AddToClassList("pop-n");
            n.AddToClassList("font-title");
            if (b.Quantity == 0) n.AddToClassList("bad");
            row.Add(n);
            panel.Add(row);
        }
        if (first) panel.Add(Small("Nothing in particular today."));
        return panel;
    }

    // ---------------- Pieces ----------------

    static VisualElement Row(string cls)
    {
        var e = new VisualElement();
        e.AddToClassList(cls);
        return e;
    }

    static Label Small(string text)
    {
        var l = new Label(text);
        l.AddToClassList("panel-small");
        return l;
    }

    static VisualElement Icon(string cls)
    {
        var i = new VisualElement { pickingMode = PickingMode.Ignore };
        i.AddToClassList("si");
        i.AddToClassList(cls);
        return i;
    }

    static VisualElement Panel(string title, string small, bool strong, string icon = null)
    {
        var panel = new VisualElement();
        panel.AddToClassList("panel");
        var head = Row("panel-head");
        if (icon != null) head.Add(Icon(icon));
        var t = new Label(title);
        t.AddToClassList("panel-title");
        t.AddToClassList("font-title");
        if (strong) t.AddToClassList("strong");
        head.Add(t);
        if (small != null) head.Add(Small(small));
        panel.Add(head);
        return panel;
    }

    static VisualElement Tile(string n, string label, string icon, string cls = null)
    {
        var tile = new VisualElement();
        tile.AddToClassList("stile");
        var i = Icon(icon);
        i.AddToClassList("stile-icon");
        tile.Add(i);
        var text = new VisualElement();
        var big = new Label(n);
        big.AddToClassList("stile-n");
        big.AddToClassList("font-title");
        if (cls != null) big.AddToClassList(cls);
        text.Add(big);
        var l = new Label(label);
        l.AddToClassList("stile-l");
        text.Add(l);
        tile.Add(text);
        return tile;
    }

    static Color Colour(string state) => state == "bad" ? new Color32(142, 59, 50, 255)
                                        : state == "warn" ? new Color32(168, 112, 28, 255)
                                        : new Color32(79, 107, 58, 255);
}
