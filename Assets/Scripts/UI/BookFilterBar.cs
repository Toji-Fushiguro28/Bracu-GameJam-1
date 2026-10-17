using System;
using UnityEngine.UIElements;

/// <summary>
/// Search box + Subject + Type filter buttons, shared by the PC Shop and the end-of-day shop.
/// Same rules as the catalogue: the search matches titles and authors only.
/// </summary>
public class BookFilterBar
{
    static readonly string[] Subjects = { "All", "Science", "History", "Philosophy", "Math" };
    static readonly BookFormat[] Types = (BookFormat[])Enum.GetValues(typeof(BookFormat));

    public readonly VisualElement Root;
    readonly TextField search;
    readonly Button subjectButton, typeButton;
    readonly Action changed;
    int subject, type;

    /// <param name="pcStyle">True on the PC (mint terminal look), false on the book pages.</param>
    public BookFilterBar(Action onChanged, bool pcStyle)
    {
        changed = onChanged;
        Root = new VisualElement();
        Root.AddToClassList(pcStyle ? "pc-filter-bar" : "page-filter-bar");

        search = new TextField { name = pcStyle ? "book-search" : "page-search" };
        search.AddToClassList("filter-search");
        search.textEdition.placeholder = "search title or author";
        search.RegisterValueChangedCallback(_ => changed?.Invoke());
        Root.Add(search);

        var buttons = new VisualElement();
        buttons.AddToClassList(pcStyle ? "pc-filters" : "page-filters");
        subjectButton = new Button(() => { subject = (subject + 1) % Subjects.Length; Labels(); changed?.Invoke(); });
        typeButton = new Button(() => { type = (type + 1) % (Types.Length + 1); Labels(); changed?.Invoke(); });
        foreach (var b in new[] { subjectButton, typeButton })
        {
            b.AddToClassList(pcStyle ? "pc-button" : "kit-button");
            b.AddToClassList(pcStyle ? "pc-filter" : "page-filter");
            buttons.Add(b);
        }
        Root.Add(buttons);
        Labels();
    }

    void Labels()
    {
        subjectButton.text = "Subject: " + Subjects[subject];
        typeButton.text = "Type: " + (type == 0 ? "All" : new BookData { Format = Types[type - 1] }.FormatName);
    }

    public void Reset()
    {
        subject = type = 0;
        search.SetValueWithoutNotify("");
        Labels();
    }

    public bool Matches(BookData b)
    {
        string q = (search.value ?? "").Trim().ToLowerInvariant();
        if (q.Length > 0 && !(b.Title ?? "").ToLowerInvariant().Contains(q) && !(b.Author ?? "").ToLowerInvariant().Contains(q)) return false;
        if (subject > 0 && b.Subject != Subjects[subject]) return false;
        if (type > 0 && b.Format != Types[type - 1]) return false;
        return true;
    }
}
