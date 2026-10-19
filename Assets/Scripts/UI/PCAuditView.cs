using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// PC "Log": every request heard and every coin in or out, one day at a time (today first, newest at the top).
/// Each row: time, a dot for how it went (a coin for money-only lines), what happened, the outcome and +/- coins.
/// </summary>
public static class PCAuditView
{
    static int shownDay;

    public static void Wire(VisualElement root)
    {
        var gm = GameManager.Instance;
        shownDay = gm != null ? gm.day : 1;
        var days = root.Q("audit-days");
        if (days != null)
            for (int d = 1; d <= shownDay; d++)
            {
                int day = d;
                var b = new Button(() => { shownDay = day; Refresh(root); }) { name = "audit-day-" + d, text = d == shownDay ? $"Day {d} (today)" : "Day " + d };
                b.AddToClassList("pc-button");
                b.AddToClassList("small");
                b.AddToClassList("audit-day");
                days.Add(b);
            }
        Refresh(root);
    }

    static void Refresh(VisualElement root)
    {
        root.Query<Button>(className: "audit-day").ForEach(b => b.EnableInClassList("primary", b.name == "audit-day-" + shownDay));
        var list = root.Q<ScrollView>("audit-list");
        if (list == null) return;
        list.Clear();
        var all = RequestLog.All;
        for (int i = all.Count - 1; i >= 0; i--)
            if (all[i].day == shownDay) list.Add(Row(all[i]));
        if (list.childCount > 0) return;
        var empty = new Label("Nothing logged on this day.");
        empty.AddToClassList("audit-empty");
        list.Add(empty);
    }

    static VisualElement Row(RequestLog.Entry e)
    {
        var row = new VisualElement();
        row.AddToClassList("logrow");
        row.Add(Text(GameManager.FormatHour(e.hour), "log-time"));

        bool money = e.kind == RequestLog.Kind.Money;
        string state = Outcome(e);
        var mark = new VisualElement();
        mark.AddToClassList(money ? "log-coin" : "log-mark");
        if (!money) mark.style.backgroundColor = (Color)(state == "bad" ? new Color32(142, 59, 50, 255) : state == "warn" ? new Color32(168, 112, 28, 255) : new Color32(79, 107, 58, 255));
        row.Add(mark);

        var what = new VisualElement();
        what.AddToClassList("log-what");
        what.Add(Text(money || e.kind == RequestLog.Kind.Card ? e.line : $"\"{e.line}\"", "log-line"));
        string sub = string.IsNullOrEmpty(e.who) ? RequestLog.KindName(e.kind) : $"{e.who}  ·  {RequestLog.KindName(e.kind)}";
        if (e.kind == RequestLog.Kind.Book && e.hint != null) sub += e.usedHint ? "  ·  asked for a hint" : "  ·  free hint";
        what.Add(Text(sub, "log-sub"));
        foreach (var g in e.guesses) what.Add(Text(RequestLog.GuessText(g), "log-sub"));
        foreach (var n in e.notes) what.Add(Text(n, "log-sub"));
        row.Add(what);

        var outBox = new VisualElement();
        outBox.AddToClassList("log-out");
        var outcome = Text(e.outcome ?? "still waiting", "log-outcome");
        if (!money) outcome.AddToClassList(state);
        outBox.Add(outcome);
        if (e.coins != 0)
        {
            var amt = new VisualElement();
            amt.AddToClassList("log-amt");
            var coin = new VisualElement();
            coin.AddToClassList("coin-icon");
            var n = Text(RequestLog.CoinText(e.coins), "log-amt-text");
            n.AddToClassList(e.coins > 0 ? "ok" : "bad");
            amt.Add(n);
            amt.Insert(0, coin);
            outBox.Add(amt);
        }
        row.Add(outBox);
        return row;
    }

    /// <summary>Green if it went well, amber if it's still pending, red if someone was let down.</summary>
    static string Outcome(RequestLog.Entry e)
    {
        string o = e.outcome ?? "";
        if (o.Length == 0 || o.Contains("Reserved") || o.Contains("back tomorrow") || o.Contains("Not here")) return "warn";
        if (o.Contains("angry") || o.Contains("Never") || o.Contains("never") || o.Contains("Sent") || o.Contains("Told") || o.Contains("Left") || o.Contains("didn't")) return "bad";
        return "ok";
    }

    static Label Text(string text, string cls)
    {
        var l = new Label(text) { enableRichText = true };
        l.AddToClassList(cls);
        return l;
    }
}
