using System.Linq;
using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// PC "Members" screen: the card list (access, hearts, visits) and the New Card form.
/// You type the name the visitor told you, pick their birthday from day / month / year drop-downs
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
    static Button dayButton, monthButton, yearButton;

    public static void Wire(VisualElement root)
    {
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
            row.Add(Cell(m.name, "name"));
            row.Add(Cell(m.dob, "born"));
            row.Add(Cell(m.access.ToString(), "access"));
            var hearts = new VisualElement();
            hearts.AddToClassList("happy");
            for (int i = 0; i < 5; i++)
            {
                var h = new VisualElement();
                h.AddToClassList("heart"); // the kit's pixel heart, full or empty
                h.EnableInClassList("empty", i >= m.Hearts);
                hearts.Add(h);
            }
            row.Add(hearts);
            row.Add(Cell(m.visits.ToString(), "num"));
            row.Add(Cell(m.spent.ToString(), "num"));
            list.Add(row);
        }
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

    /// <summary>Day / Month / Year: pixel boxes that open a list showing six at a time.</summary>
    static void BuildDatePicker(VisualElement root)
    {
        dayButton = root.Q<Button>("dob-day");
        monthButton = root.Q<Button>("dob-month");
        yearButton = root.Q<Button>("dob-year");
        yearButton?.AddToClassList("last");
        // Drop-downs, not text boxes: the kit's arrow on each.
        foreach (var b in new[] { dayButton, monthButton, yearButton })
            if (b != null && b.Q(className: "dd-arrow") == null)
            {
                var arrow = new VisualElement { pickingMode = PickingMode.Ignore };
                arrow.AddToClassList("dd-arrow");
                b.Add(arrow);
            }
        dayButton?.RegisterCallback<ClickEvent>(_ =>
        {
            int days = DaysIn(pickMonth, pickYear);
            PickList.Open(dayButton, Enumerable.Range(1, 31).Select(d => d.ToString("00")).ToArray(), pickDay - 1,
                i => { pickDay = i + 1; UpdateForm(root); }, true, i => i < days, -1, null, 6); // no 31 Apr
        });
        monthButton?.RegisterCallback<ClickEvent>(_ => PickList.Open(monthButton, Months, pickMonth - 1, i => { pickMonth = i + 1; UpdateForm(root); }, true, null, -1, null, 6));
        yearButton?.RegisterCallback<ClickEvent>(_ =>
        {
            var years = Enumerable.Range(FirstYear, LastYear - FirstYear + 1).Select(y => y.ToString()).ToArray();
            // Nothing picked yet: open around 1980 rather than at 1930.
            PickList.Open(yearButton, years, pickYear - FirstYear, i => { pickYear = FirstYear + i; UpdateForm(root); }, true, null, 1980 - FirstYear, null, 6);
        });
        DateLabels();
    }

    static void DateLabels()
    {
        SetDate(dayButton, pickDay > 0 ? pickDay.ToString("00") : null, "Day");
        SetDate(monthButton, pickMonth > 0 ? Months[pickMonth - 1] : null, "Month");
        SetDate(yearButton, pickYear > 0 ? pickYear.ToString() : null, "Year");
    }

    static void SetDate(Button b, string value, string empty)
    {
        if (b == null) return;
        b.text = value ?? empty;
        b.EnableInClassList("empty", value == null);
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
        DateLabels();

        string name = NormalizeName(typedName);
        bool nameOk = NameReady(typedName.Trim());
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
            b.AddToClassList("small");
            b.EnableInClassList("primary", get() == value);
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
        if (wrong == null) UnityEngine.Object.FindAnyObjectByType<PCOverlayTrigger>()?.ForceClose(); // done here: back to the library
    }
}
