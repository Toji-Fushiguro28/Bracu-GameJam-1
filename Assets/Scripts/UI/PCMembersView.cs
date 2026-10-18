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
        box.style.height = StyleKeyword.Auto;
        box.style.alignItems = Align.Center;
        dayButton = DateButton(box, () =>
        {
            int days = DaysIn(pickMonth, pickYear);
            PickList.Open(dayButton, Enumerable.Range(1, 31).Select(d => d.ToString("00")).ToArray(), pickDay - 1,
                i => { pickDay = i + 1; UpdateForm(root); }, true, i => i < days); // no 31 Apr
        });
        monthButton = DateButton(box, () => PickList.Open(monthButton, Months, pickMonth - 1, i => { pickMonth = i + 1; UpdateForm(root); }, true));
        yearButton = DateButton(box, () =>
        {
            var years = Enumerable.Range(FirstYear, LastYear - FirstYear + 1).Select(y => y.ToString()).ToArray();
            // Nothing picked yet: open around 1980 rather than at 1930.
            PickList.Open(yearButton, years, pickYear - FirstYear, i => { pickYear = FirstYear + i; UpdateForm(root); }, true, null, 1980 - FirstYear);
        });
        DateLabels();
    }

    static void DateLabels()
    {
        if (dayButton != null) dayButton.text = "Day: " + (pickDay > 0 ? pickDay.ToString("00") : "--");
        if (monthButton != null) monthButton.text = "Month: " + (pickMonth > 0 ? Months[pickMonth - 1] : "--");
        if (yearButton != null) yearButton.text = "Year: " + (pickYear > 0 ? pickYear.ToString() : "--");
    }

    static Button DateButton(VisualElement box, Action open)
    {
        var b = new Button(open);
        b.AddToClassList("pc-button");
        b.AddToClassList("pc-filter");
        b.style.flexGrow = 1;
        box.Add(b);
        return b;
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
