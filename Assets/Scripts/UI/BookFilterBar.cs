using System;
using UnityEngine.UIElements;

/// <summary>
/// Search box + Subject / Type / Show drop-downs, shared by the PC Shop and the end-of-day shop.
/// The search matches titles, authors and tags.
/// </summary>
public class BookFilterBar
{
    static readonly string[] Subjects = { "All", "Science", "History", "Philosophy", "Math" };
    static readonly BookFormat[] Types = (BookFormat[])Enum.GetValues(typeof(BookFormat));

    public readonly VisualElement Root;
    readonly TextField search;
    readonly Button subjectButton, typeButton, showButton;
    readonly Action changed;
    int subject, type, show;
    readonly bool pc;

    /// <param name="pcStyle">True on the PC (mint terminal look), false on the book pages.</param>
    public BookFilterBar(Action onChanged, bool pcStyle)
    {
        changed = onChanged;
        pc = pcStyle;
        Root = new VisualElement();
        Root.AddToClassList(pcStyle ? "pc-filter-bar" : "page-filter-bar");

        search = new TextField { name = pcStyle ? "book-search" : "page-search" };
        search.AddToClassList("filter-search");
        search.textEdition.placeholder = "search title, author or tag";
        search.RegisterValueChangedCallback(_ => changed?.Invoke());
        Root.Add(search);

        var buttons = new VisualElement();
        buttons.AddToClassList(pcStyle ? "pc-filters" : "page-filters");
        subjectButton = new Button();
        subjectButton.clicked += () => PickList.Open(subjectButton, Subjects, subject, i => { subject = i; Labels(); changed?.Invoke(); }, pc);
        typeButton = new Button();
        typeButton.clicked += () => PickList.Open(typeButton, TypeNames, type, i => { type = i; Labels(); changed?.Invoke(); }, pc);
        showButton = new Button();
        showButton.clicked += () => PickList.Open(showButton, Shows, show, i => { show = i; Labels(); changed?.Invoke(); }, pc);
        foreach (var b in new[] { subjectButton, typeButton, showButton })
        {
            b.AddToClassList(pcStyle ? "pc-button" : "kit-button");
            b.AddToClassList(pcStyle ? "pc-filter" : "page-filter");
            buttons.Add(b);
        }
        Root.Add(buttons);
        Labels();
    }

    static string[] TypeNames
    {
        get
        {
            var names = new string[Types.Length + 1];
            names[0] = "All";
            for (int i = 0; i < Types.Length; i++) names[i + 1] = new BookData { Format = Types[i] }.FormatName;
            return names;
        }
    }

    /// <summary>Shared with the catalogue: All, Reserved (someone's waiting for it), Sold out, In stock.</summary>
    public static readonly string[] Shows = { "All", "Reserved", "Sold out", "In stock" };

    public static bool ShowMatches(int show, BookData b)
    {
        bool inStock = b.GoodCopies + b.WornCopies > 0;
        switch (show)
        {
            case 1: var gm = GameManager.Instance; return gm != null && gm.reservations.Exists(r => r.title == b.Title);
            case 2: return !inStock;
            case 3: return inStock;
            default: return true;
        }
    }

    void Labels()
    {
        subjectButton.text = "Subject: " + Subjects[subject];
        typeButton.text = "Type: " + TypeNames[type];
        showButton.text = "Show: " + Shows[show];
    }

    public void Reset()
    {
        subject = type = show = 0;
        PickList.Close();
        search.SetValueWithoutNotify("");
        Labels();
    }

    /// <summary>Starts with this in the search box (e.g. carried over from the catalogue).</summary>
    public void SetSearch(string text) => search.SetValueWithoutNotify(text ?? "");

    public bool Matches(BookData b)
    {
        string q = (search.value ?? "").Trim().ToLowerInvariant();
        if (q.Length > 0 && !(b.Title ?? "").ToLowerInvariant().Contains(q) && !(b.Author ?? "").ToLowerInvariant().Contains(q) && !(b.Tag ?? "").ToLowerInvariant().Contains(q)) return false;
        if (subject > 0 && b.Subject != Subjects[subject]) return false;
        if (type > 0 && b.Format != Types[type - 1]) return false;
        if (!ShowMatches(show, b)) return false;
        return true;
    }
}
