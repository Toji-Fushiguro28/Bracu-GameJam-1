using System.Linq;
using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// PC "Members" screen: the card list (access, hearts, visits) and the New Card form. Click a member to open
/// their card: title and perk, visits, and what went wrong for them (their complaints).
/// You type the name the visitor told you, roll their birthday on day / month / year wheels
/// (only real dates can be picked) and choose the access level. A library card under the monitor fills in as you
/// go; a right card gets an ink stamp before the PC logs off.
/// </summary>
public static class PCMembersView
{
    const int NameLimit = 30;
    const int FirstYear = 1930, LastYear = 2010;
    static readonly string[] Months = { "Jan", "Feb", "Mar", "Apr", "May", "Jun", "Jul", "Aug", "Sep", "Oct", "Nov", "Dec" };

    static string typedName = "";
    static int pickDay, pickMonth, pickYear; // 0 = not picked yet
    // The day you rolled to. Rolling the month or year past a shorter month (31 -> April, 29 Feb -> 1987) shows the
    // last day that month has, and your day comes back when the month can hold it again: it's never wiped.
    static int wantDay;
    static AccessLevel? pickAccess;
    static Customer formFor;
    static Wheel dayWheel, monthWheel, yearWheel;
    static VisualElement pass; // the card under the monitor
    static bool issuing; // the stamp is coming down: the form waits
    static string openKey; // the member whose card is open in the list

    public static void Wire(VisualElement root)
    {
        root.Q<Button>("card-issue")?.RegisterCallback<ClickEvent>(_ => Issue(root));
        BuildList(root);
        WireName(root);
        BuildDatePicker(root);
        BuildPass(root);
        BuildForm(root);
    }

    static void BuildList(VisualElement root)
    {
        var list = root.Q<ScrollView>("members-list");
        if (list == null) return;
        list.Clear();
        var count = root.Q<Label>("member-count");
        if (count != null) count.text = Members.All.Count == 1 ? "1 member" : Members.All.Count + " members";
        var no = root.Q<Label>("card-no");
        if (no != null) no.text = $"No. {Members.All.Count + 1:0000}";
        if (Members.All.Count == 0)
        {
            var empty = new Label("No members yet. Visitors who ask for a library card can sign up here.");
            empty.AddToClassList("member-empty");
            list.Add(empty);
        }
        foreach (var m in Members.All)
        {
            var row = new VisualElement();
            row.AddToClassList("mrow");
            string title = m.Title;
            row.EnableInClassList("unhappy", title == "Unhappy");
            row.EnableInClassList("open", openKey == m.key);
            row.Add(Cell(title == "Member" ? m.name : $"{m.name}\n<size=75%>{title}</size>", "name"));
            row.Add(Cell(m.dob, "born"));
            row.Add(Cell(m.access.ToString(), "access"));
            row.Add(Hearts(m.satisfaction));
            row.Add(Cell(m.visits.ToString(), "num"));
            row.Add(Cell(m.spent.ToString(), "num"));
            var key = m.key;
            row.RegisterCallback<ClickEvent>(_ => { openKey = openKey == key ? null : key; BuildList(root); }); // one card open at a time
            list.Add(row);
            if (openKey == m.key) list.Add(MemberCard(m));
        }
    }

    /// <summary>Five pixel hearts: full, half (the kit's half heart) or empty.</summary>
    static VisualElement Hearts(float value)
    {
        var hearts = new VisualElement();
        hearts.AddToClassList("happy");
        for (int i = 0; i < 5; i++)
        {
            var h = new VisualElement();
            h.AddToClassList("heart");
            float left = value - i;
            h.EnableInClassList("half", left >= 0.5f && left < 1f);
            h.EnableInClassList("empty", left < 0.5f);
            hearts.Add(h);
        }
        return hearts;
    }

    /// <summary>The open card under a member's row: title and perk, visits, gifts, and what went wrong.</summary>
    static VisualElement MemberCard(Member m)
    {
        var card = new VisualElement();
        card.AddToClassList("mcard");
        string title = m.Title;
        var top = new VisualElement();
        top.AddToClassList("mcard-top");
        top.Add(Hearts(m.satisfaction));
        top.Add(Line($"<color=#D67840>{title}</color>: {Members.PerkOf(title)}", "mcard-title"));
        card.Add(top);
        card.Add(Line($"{m.visits} {(m.visits == 1 ? "visit" : "visits")} · spent {m.spent} coins" + (m.lastDonationDay > 0 ? $" · donated books on Day {m.lastDonationDay}" : ""), "mcard-line"));
        card.Add(Line("What went wrong", "mcard-head"));
        if (m.complaints == null || m.complaints.Count == 0) card.Add(Line("No complaints. They're happy here.", "mcard-line"));
        else
            for (int i = m.complaints.Count - 1; i >= 0; i--)
                card.Add(Line($"Day {m.complaints[i].day} · {m.complaints[i].text}", "mcard-gripe"));
        if (title == "Unhappy") card.Add(Line("On their next visit they may hand their card back.", "mcard-warn"));
        return card;
    }

    static Label Line(string text, string cls)
    {
        var l = new Label(text);
        l.AddToClassList(cls);
        return l;
    }

    static Label Cell(string text, string col)
    {
        var l = new Label(text);
        l.AddToClassList("mcell");
        l.AddToClassList(col);
        return l;
    }

    // ---------------- Name ----------------

    static void WireName(VisualElement root)
    {
        var field = root.Q<TextField>("card-name");
        var placeholder = root.Q<Label>("card-name-placeholder");
        if (field == null) return;
        field.maxLength = NameLimit;
        void UpdatePlaceholder() { if (placeholder != null) placeholder.style.display = string.IsNullOrEmpty(field.value) ? DisplayStyle.Flex : DisplayStyle.None; }
        field.RegisterValueChangedCallback(e =>
        {
            // Letters, single spaces, hyphens and apostrophes only: anything else never reaches the box.
            string clean = CleanName(e.newValue);
            if (clean != e.newValue) field.SetValueWithoutNotify(clean);
            typedName = clean;
            UpdatePlaceholder();
            UpdateForm(root);
        });
        field.SetValueWithoutNotify(typedName);
        UpdatePlaceholder();
    }

    static string CleanName(string s)
    {
        var sb = new StringBuilder();
        foreach (char c in s ?? "")
        {
            if (char.IsLetter(c) || ((c == '-' || c == '\'') && sb.Length > 0)) sb.Append(c);
            else if (c == ' ' && sb.Length > 0 && sb[sb.Length - 1] != ' ') sb.Append(' ');
            if (sb.Length >= NameLimit) break;
        }
        return sb.ToString();
    }

    /// <summary>"edmund  THORNE " becomes "Edmund Thorne": the way names are written on cards.</summary>
    public static string NormalizeName(string s)
    {
        var chars = CleanName(s).Trim().ToLowerInvariant().ToCharArray();
        for (int i = 0; i < chars.Length; i++)
            if (i == 0 || chars[i - 1] == ' ' || chars[i - 1] == '-' || chars[i - 1] == '\'') chars[i] = char.ToUpperInvariant(chars[i]);
        return new string(chars);
    }

    /// <summary>At least two letters, and it can't end on a hyphen or apostrophe.</summary>
    static bool NameReady(string name)
    {
        int letters = 0;
        foreach (char c in name) if (char.IsLetter(c)) letters++;
        return letters >= 2 && char.IsLetter(name[name.Length - 1]);
    }

    // ---------------- Birthday ----------------

    static int DaysIn(int month, int year) => month == 0 ? 31 : DateTime.DaysInMonth(year == 0 ? 2000 : year, month); // no year yet: allow 29 Feb

    static string Dob => $"{pickDay:00} {Months[pickMonth - 1]} {pickYear}";
    static bool DobReady => pickDay > 0 && pickMonth > 0 && pickYear > 0;

    /// <summary>Day / Month / Year wheels. Day and month wrap round; the year stops at both ends.</summary>
    static void BuildDatePicker(VisualElement root)
    {
        dayWheel = new Wheel(root.Q("dob-day"), "Day", 1, () => pickDay, v => { wantDay = pickDay = v; UpdateForm(root); },
            (v, d) => { int n = DaysIn(pickMonth, pickYear); return (v - 1 + d + n) % n + 1; }, v => v.ToString("00")); // no 31 Apr
        monthWheel = new Wheel(root.Q("dob-month"), "Month", 1, () => pickMonth, v => { pickMonth = v; UpdateForm(root); },
            (v, d) => (v - 1 + d + 12) % 12 + 1, v => Months[v - 1]);
        // Nothing picked yet: start around 1980 rather than at 1930.
        yearWheel = new Wheel(root.Q("dob-year"), "Year", 1980, () => pickYear, v => { pickYear = v; UpdateForm(root); },
            (v, d) => Mathf.Clamp(v + d, FirstYear, LastYear), v => v.ToString());
        DateLabels();
    }

    static void DateLabels()
    {
        dayWheel?.Refresh();
        monthWheel?.Refresh();
        yearWheel?.Refresh();
    }

    /// <summary>One date wheel: scroll it, drag it, or click the row above or below. Each step rolls the numbers
    /// and ticks. 0 means nothing picked: the box shows its name until the first roll.</summary>
    class Wheel
    {
        const float Row = 26f;
        static float lastTick;
        readonly VisualElement box, strip;
        readonly Label[] rows = new Label[5];
        readonly string placeholder;
        readonly int start;
        readonly Func<int> get;
        readonly Action<int> set;
        readonly Func<int, int, int> step;
        readonly Func<int, string> show;
        float drag;

        public Wheel(VisualElement box, string placeholder, int start, Func<int> get, Action<int> set, Func<int, int, int> step, Func<int, string> show)
        {
            this.box = box; this.placeholder = placeholder; this.start = start; this.get = get; this.set = set; this.step = step; this.show = show;
            if (box == null) return;
            var band = new VisualElement { pickingMode = PickingMode.Ignore };
            band.AddToClassList("wheel-band");
            box.Add(band);
            strip = new VisualElement { pickingMode = PickingMode.Ignore };
            strip.AddToClassList("wheel-strip");
            box.Add(strip);
            for (int i = 0; i < rows.Length; i++)
            {
                rows[i] = new Label { pickingMode = PickingMode.Ignore };
                rows[i].AddToClassList("wheel-row");
                rows[i].AddToClassList(i == 2 ? "mid" : i == 1 || i == 3 ? "near" : "far");
                strip.Add(rows[i]);
            }
            box.RegisterCallback<WheelEvent>(e => { if (e.delta.y != 0) Step(e.delta.y > 0 ? 1 : -1); e.StopPropagation(); });
            box.RegisterCallback<PointerDownEvent>(e => { drag = 0; box.CapturePointer(e.pointerId); });
            box.RegisterCallback<PointerMoveEvent>(e =>
            {
                if (!box.HasPointerCapture(e.pointerId)) return;
                drag += e.deltaPosition.y;
                while (drag <= -Row * 0.7f) { drag += Row; Step(1); } // pull up: the next one comes into view
                while (drag >= Row * 0.7f) { drag -= Row; Step(-1); }
            });
            box.RegisterCallback<PointerUpEvent>(e =>
            {
                if (!box.HasPointerCapture(e.pointerId)) return;
                box.ReleasePointer(e.pointerId);
                // A click, not a drag: the row above steps back, the row below steps on, the middle just picks.
                if (Mathf.Abs(drag) < 3f)
                {
                    float y = e.localPosition.y - box.layout.height / 2f;
                    Step(get() == 0 || Mathf.Abs(y) < Row / 2f ? 0 : y > 0 ? 1 : -1);
                }
            });
        }

        void Step(int dir)
        {
            if (issuing || strip == null) return;
            int v = get();
            int next = v == 0 ? start : dir == 0 ? v : step(v, dir);
            if (next == v) return;
            set(next); // the form refreshes every wheel
            if (v != 0)
                strip.experimental.animation.Start(dir * Row, 0f, 110, (e, y) => e.style.translate = new Translate(0, y));
            // now < lastTick: a time left over from an earlier play session (the editor keeps statics), not a real tick.
            if (Time.unscaledTime - lastTick > 0.035f || Time.unscaledTime < lastTick)
            {
                lastTick = Time.unscaledTime;
                GameAudio.Scroll();
            }
        }

        public void Refresh()
        {
            if (strip == null) return;
            int v = get();
            box.EnableInClassList("empty", v == 0);
            for (int i = 0; i < rows.Length; i++)
            {
                int k = i - 2, at = v;
                for (int n = 0; n < Mathf.Abs(k) && at != 0; n++)
                {
                    int was = at;
                    at = step(at, Math.Sign(k));
                    if (at == was) at = 0; // past the year's end: an empty row
                }
                rows[i].text = v == 0 ? (k == 0 ? placeholder : "") : at == 0 ? "" : show(at);
            }
        }
    }

    // ---------------- The card under the monitor ----------------

    /// <summary>A library card (the SM_LibraryPass look) lying under the monitor. It fills in as you type and roll,
    /// and takes the stamp when the card is right. Only on this tab, and only while someone's waiting.</summary>
    static void BuildPass(VisualElement root)
    {
        VisualElement monitor = root;
        while (monitor != null && monitor.name != "monitor") monitor = monitor.parent;
        if (monitor == null) return;
        monitor.Q("libpass")?.RemoveFromHierarchy();
        issuing = false;

        pass = new VisualElement { name = "libpass", pickingMode = PickingMode.Ignore };
        pass.AddToClassList("libpass");
        var head = new VisualElement();
        head.AddToClassList("libpass-head");
        head.Add(Line("THE WHITMORE LIBRARY", "libpass-title"));
        head.Add(Line("", "libpass-no"));
        pass.Add(head);
        var body = new VisualElement();
        body.AddToClassList("libpass-body");
        var left = new VisualElement();
        left.AddToClassList("libpass-left");
        var top = new VisualElement();
        top.AddToClassList("libpass-top");
        var seal = new VisualElement();
        seal.AddToClassList("libpass-seal");
        top.Add(seal);
        top.Add(Line("", "libpass-name"));
        left.Add(top);
        left.Add(Line("", "libpass-line"));
        left.Add(Line("", "libpass-line"));
        body.Add(left);
        var photo = new VisualElement();
        photo.AddToClassList("libpass-photo");
        photo.Add(Line("", "libpass-initial"));
        body.Add(photo);
        pass.Add(body);
        var stamp = new VisualElement { name = "libpass-stamp", pickingMode = PickingMode.Ignore };
        stamp.AddToClassList("libpass-stamp");
        stamp.Add(Line("ISSUED", "stamp-word"));
        stamp.Add(Line("", "stamp-day"));
        pass.Add(stamp);
        foreach (var l in pass.Query<Label>().ToList()) l.pickingMode = PickingMode.Ignore;
        monitor.Add(pass);

        // Leaving the tab (or the PC) takes the card away.
        var page = root.Q(className: "mem");
        var mine = pass;
        page?.RegisterCallback<DetachFromPanelEvent>(_ => { mine.RemoveFromHierarchy(); if (pass == mine) { pass = null; issuing = false; } });
    }

    static void FillPass()
    {
        if (pass == null) return;
        pass.EnableInClassList("hidden", Customer.WaitingForCard == null && !issuing);
        if (issuing) return; // keep what's being stamped
        string name = NormalizeName(typedName).Trim();
        var lines = pass.Query<Label>(className: "libpass-line").ToList();
        pass.Q<Label>(className: "libpass-no").text = $"No. {Members.All.Count + 1:0000}";
        SetPass(pass.Q<Label>(className: "libpass-name"), name, "Name");
        lines[0].text = "Born " + (pickDay > 0 ? pickDay.ToString("00") : "__") + " " + (pickMonth > 0 ? Months[pickMonth - 1] : "___") + " " + (pickYear > 0 ? pickYear.ToString() : "____");
        lines[0].EnableInClassList("blank", !DobReady);
        SetPass(lines[1], pickAccess != null ? pickAccess + " access" : "", "Access");
        pass.Q<Label>(className: "libpass-initial").text = name.Length > 0 ? name.Substring(0, 1) : "";
    }

    static void SetPass(Label l, string value, string empty)
    {
        bool blank = string.IsNullOrEmpty(value);
        l.text = blank ? empty : value;
        l.EnableInClassList("blank", blank);
    }

    /// <summary>The stamp comes down and lands with a thunk, the "issued" chime follows, then the PC logs off.</summary>
    static void StampAndClose()
    {
        var card = pass;
        var stamp = card?.Q("libpass-stamp");
        if (stamp == null) { GameAudio.Play(a => a.cardIssued); UnityEngine.Object.FindAnyObjectByType<PCOverlayTrigger>()?.ForceClose(); return; }
        stamp.Q<Label>(className: "stamp-day").text = "DAY " + (GameManager.Instance != null ? GameManager.Instance.day : 1);
        stamp.AddToClassList("down");
        stamp.experimental.animation.Start(0f, 1f, 160, (e, t) =>
        {
            e.style.scale = new Scale(Vector3.one * Mathf.Lerp(1.9f, 1f, t * t));
            e.style.opacity = t;
        }).OnCompleted(() =>
        {
            GameAudio.Play(a => a.cardStamp);
            card.experimental.animation.Start(5f, 0f, 140, (e, y) => e.style.translate = new Translate(0, y)); // the card takes the hit
            card.schedule.Execute(() => GameAudio.Play(a => a.cardIssued)).ExecuteLater(350);
            card.schedule.Execute(() => UnityEngine.Object.FindAnyObjectByType<PCOverlayTrigger>()?.ForceClose()).ExecuteLater(1200);
        });
    }

    // ---------------- Form ----------------

    static void BuildForm(VisualElement root)
    {
        var who = Customer.WaitingForCard;
        root.Q("card-empty")?.EnableInClassList("hidden", who != null);
        root.Q("card-body")?.EnableInClassList("hidden", who == null);
        if (who == null) return;

        if (formFor != who)
        {
            formFor = who;
            typedName = "";
            pickDay = pickMonth = pickYear = wantDay = 0;
            pickAccess = null;
            root.Q<TextField>("card-name")?.SetValueWithoutNotify("");
            root.Q<Label>("card-feedback").text = "";
        }
        Options(root.Q("card-access"), Enum.GetNames(typeof(AccessLevel)), () => pickAccess?.ToString(),
                v => pickAccess = (AccessLevel)Enum.Parse(typeof(AccessLevel), v), root);
        UpdateForm(root);
    }

    /// <summary>Picked states, days that don't exist in the chosen month, the preview line and the Issue button.</summary>
    static void UpdateForm(VisualElement root)
    {
        int days = DaysIn(pickMonth, pickYear);
        pickDay = wantDay == 0 ? 0 : Mathf.Min(wantDay, days); // 31 Apr can't exist: 30 for now, 31 again in May
        DateLabels();

        bool nameOk = NameReady(typedName.Trim());
        root.Q<Button>("card-issue")?.SetEnabled(!issuing && nameOk && DobReady && pickAccess != null);
        FillPass();
    }

    static void Options(VisualElement box, string[] values, Func<string> get, Action<string> set, VisualElement root)
    {
        if (box == null) return;
        box.Clear();
        foreach (var v in values)
        {
            var value = v;
            // The click sound is played here: the rebuild removes this button before its ClickEvent reaches the hook.
            var b = new Button(() => { GameAudio.Play(a => a.uiClick, 0.5f); set(value); BuildForm(root); }) { text = value };
            b.AddToClassList("pc-button");
            b.AddToClassList("small");
            b.EnableInClassList("primary", get() == value);
            box.Add(b);
        }
    }

    static void Issue(VisualElement root)
    {
        var who = Customer.WaitingForCard;
        if (issuing || who == null || !NameReady(typedName.Trim()) || !DobReady || pickAccess == null) return;
        string name = NormalizeName(typedName);
        FillPass(); // the card shows exactly what was issued
        string wrong = who.ReceiveCard(name, Dob, pickAccess.Value, false); // the chime waits for the stamp
        if (wrong == null)
        {
            formFor = null;
            issuing = true;
            root.Q<Label>("card-empty").text = $"Card issued to {name}!";
        }
        else
        {
            root.Q<Label>("card-feedback").text = $"\"{wrong}\"";
            pass?.experimental.animation.Start(0f, 1f, 320, (e, t) => e.style.translate = new Translate(Mathf.Sin(t * 28f) * 6f * (1f - t), 0)); // a shake: not this one
        }
        BuildList(root);
        BuildForm(root);
        if (wrong == null) StampAndClose(); // done here: stamp it, then back to the library
    }
}
