using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// PC "Members" screen: the card list (access, hearts, visits) and the New Card form.
/// You type the name the visitor told you, pick their birthday on a day / month / year picker
/// (only real dates can be picked) and choose the access level.
/// </summary>
public static class PCMembersView
{
    const int NameLimit = 30;
    const int FirstYear = 1930, LastYear = 2010;
    static readonly string[] Months = { "Jan", "Feb", "Mar", "Apr", "May", "Jun", "Jul", "Aug", "Sep", "Oct", "Nov", "Dec" };

    static string typedName = "";
    static int pickDay, pickMonth, pickYear; // 0 = not picked yet
    static AccessLevel? pickAccess;
    static Customer formFor;
    static readonly List<Button> dayButtons = new List<Button>(), monthButtons = new List<Button>(), yearButtons = new List<Button>();

    public static void Wire(VisualElement root, Action back)
    {
        root.Q<Button>("back-button")?.RegisterCallback<ClickEvent>(_ => back());
        root.Q<Button>("card-issue")?.RegisterCallback<ClickEvent>(_ => Issue(root));
        BuildList(root);
        WireName(root);
        BuildDatePicker(root);
        BuildForm(root);
    }

    static void BuildList(VisualElement root)
    {
        var list = root.Q<ScrollView>("members-list");
        if (list == null) return;
        list.Clear();
        if (Members.All.Count == 0)
            list.Add(new Label("No members yet. Visitors who ask for a library card can sign up here.") { name = "member-empty" });
        foreach (var m in Members.All)
        {
            var row = new VisualElement();
            row.AddToClassList("pc-book");
            row.Add(new Label(m.name) { name = "book-title" });
            row.Add(new Label($"Born {m.dob}  ·  {m.access} access  ·  {m.visits} visits  ·  spent {m.spent}") { name = "book-sub" });
            var hearts = new VisualElement();
            hearts.AddToClassList("member-hearts");
            for (int i = 0; i < 5; i++)
            {
                var h = new VisualElement();
                h.AddToClassList("member-heart");
                h.EnableInClassList("empty", i >= m.Hearts);
                hearts.Add(h);
            }
            row.Add(hearts);
            list.Add(row);
        }
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

    static void BuildDatePicker(VisualElement root)
    {
        var box = root.Q("card-dob");
        if (box == null) return;
        box.Clear();
        Column(box, root, "Day", "day", 31, i => (i + 1).ToString("00"), i => pickDay = i + 1, dayButtons);
        Column(box, root, "Month", "month", 12, i => Months[i], i => pickMonth = i + 1, monthButtons);
        var years = Column(box, root, "Year", "year", LastYear - FirstYear + 1, i => (FirstYear + i).ToString(), i => pickYear = FirstYear + i, yearButtons);
        // Open the year list around the selected year (or the middle of the range).
        int start = pickYear > 0 ? pickYear - FirstYear : 1980 - FirstYear;
        years.schedule.Execute(() => years.ScrollTo(yearButtons[Mathf.Clamp(start, 0, yearButtons.Count - 1)])).ExecuteLater(50);
    }

    static ScrollView Column(VisualElement box, VisualElement root, string title, string cls, int count, Func<int, string> label, Action<int> pick, List<Button> buttons)
    {
        var col = new VisualElement();
        col.AddToClassList("date-col");
        col.AddToClassList(cls);
        var head = new Label(title);
        head.AddToClassList("date-head");
        col.Add(head);
        var list = new ScrollView();
        list.AddToClassList("date-list");
        list.AddToClassList("pc-scroll");
        buttons.Clear();
        for (int i = 0; i < count; i++)
        {
            int index = i;
            var b = new Button(() => { pick(index); UpdateForm(root); }) { text = label(i) };
            b.AddToClassList("pc-button");
            b.AddToClassList("date-item");
            buttons.Add(b);
            list.Add(b);
        }
        col.Add(list);
        box.Add(col);
        return list;
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
            pickDay = pickMonth = pickYear = 0;
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
        if (pickDay > days) pickDay = 0; // 31 Apr can't exist: pick the day again
        for (int i = 0; i < dayButtons.Count; i++)
        {
            dayButtons[i].SetEnabled(i < days);
            dayButtons[i].EnableInClassList("picked", pickDay == i + 1);
        }
        for (int i = 0; i < monthButtons.Count; i++) monthButtons[i].EnableInClassList("picked", pickMonth == i + 1);
        for (int i = 0; i < yearButtons.Count; i++) yearButtons[i].EnableInClassList("picked", pickYear == FirstYear + i);

        string name = NormalizeName(typedName);
        bool nameOk = NameReady(typedName.Trim());
        var preview = root.Q<Label>("card-preview");
        if (preview != null)
            preview.text = $"{(nameOk ? name : "-")}  ·  {(DobReady ? Dob : "-")}";
        root.Q<Button>("card-issue")?.SetEnabled(nameOk && DobReady && pickAccess != null);
    }

    static void Options(VisualElement box, string[] values, Func<string> get, Action<string> set, VisualElement root)
    {
        if (box == null) return;
        box.Clear();
        foreach (var v in values)
        {
            var value = v;
            var b = new Button(() => { set(value); BuildForm(root); }) { text = value };
            b.AddToClassList("pc-button");
            b.AddToClassList("card-option");
            b.EnableInClassList("picked", get() == value);
            box.Add(b);
        }
    }

    static void Issue(VisualElement root)
    {
        var who = Customer.WaitingForCard;
        if (who == null || !NameReady(typedName.Trim()) || !DobReady || pickAccess == null) return;
        string name = NormalizeName(typedName);
        string wrong = who.ReceiveCard(name, Dob, pickAccess.Value);
        if (wrong == null)
        {
            formFor = null;
            root.Q<Label>("card-empty").text = $"Card issued to {name}!";
        }
        else root.Q<Label>("card-feedback").text = $"\"{wrong}\"";
        BuildList(root);
        BuildForm(root);
    }
}
