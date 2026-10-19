using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine.UIElements;

/// <summary>
/// Search box + Type / Subject / Show drop-downs, shared by the PC catalogue and the end-of-day shop.
/// Subject stays greyed out until a Type is picked, then lists only that type's subjects.
/// The search matches titles, authors and tags.
/// </summary>
public class BookFilterBar
{
    /// <summary>The Type filter: the formats, plus Archive and Rare Books (which come from the shelf).</summary>
    public static readonly string[] TypeNames = { "All", "Book", "Newspaper", "Magazine", "Journal", "Research paper", "Archive", "Rare Books" };
    /// <summary>Books are grouped by their shelf in the Subject list, in this order.</summary>
    static readonly string[] Sections = { "Science", "History", "Philosophy", "Math" };

    public readonly VisualElement Root;
    readonly TextField search;
    readonly Button typeButton, subjectButton, showButton;
    readonly Action changed;
    int type, show;
    string subject; // null = all
    readonly bool pc;

    /// <param name="pcStyle">True on the PC, false on the book pages.</param>
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
        typeButton = new Button();
        typeButton.clicked += () => PickList.Open(typeButton, TypeNames, type, i => { type = i; subject = null; Labels(); changed?.Invoke(); }, pc);
        subjectButton = new Button();
        subjectButton.clicked += () => OpenSubjects(subjectButton, TypeNames[type], subject, s => { subject = s; Labels(); changed?.Invoke(); }, pc);
        showButton = new Button();
        showButton.clicked += () => PickList.Open(showButton, Shows, show, i => { show = i; Labels(); changed?.Invoke(); }, pc);
        foreach (var b in new[] { typeButton, subjectButton, showButton })
        {
            b.AddToClassList(pcStyle ? "pc-button" : "kit-button");
            b.AddToClassList(pcStyle ? "pc-filter" : "page-filter");
            buttons.Add(b);
        }
        Root.Add(buttons);
        Labels();
    }

    /// <summary>Every title in play: the library's books and the ones it could order.</summary>
    public static IEnumerable<BookData> Everything
    {
        get
        {
            var gm = GameManager.Instance;
            return gm == null ? BookData.Samples() : gm.Books.Concat(gm.NewTitles);
        }
    }

    /// <summary>Opens the Subject list for a type: books grouped under their shelf, other types as a plain list.</summary>
    public static void OpenSubjects(Button button, string typeName, string current, Action<string> pick, bool pcStyle)
    {
        var options = new List<string> { "All" };
        var headers = new List<bool> { false };
        var ofType = Everything.Where(b => b.TypeName == typeName).ToList();
        if (typeName == "Book")
            foreach (var section in Sections)
            {
                var subs = ofType.Where(b => b.Subject == section).Select(b => b.SubjectName).Distinct().OrderBy(s => s).ToList();
                if (subs.Count == 0) continue;
                options.Add(section + " shelf"); headers.Add(true);
                foreach (var s in subs) { options.Add(s); headers.Add(false); }
            }
        else
            foreach (var s in ofType.Select(b => b.SubjectName).Distinct().OrderBy(s => s)) { options.Add(s); headers.Add(false); }
        int at = current == null ? 0 : options.IndexOf(current);
        PickList.Open(button, options.ToArray(), at, i => pick(i == 0 ? null : options[i]), pcStyle, null, -1, i => headers[i]);
    }

    /// <summary>All, Reserved (someone's waiting for it), Sold out, In stock, Not in library (a new title you could order).</summary>
    public static readonly string[] Shows = { "All", "Reserved", "Sold out", "In stock", "Not in library" };

    public static bool ShowMatches(int show, BookData b)
    {
        var gm = GameManager.Instance;
        bool inStock = b.GoodCopies + b.WornCopies > 0;
        bool notOwned = gm != null && gm.IsNewTitle(b);
        switch (show)
        {
            case 1: return gm != null && gm.reservations.Exists(r => r.title == b.Title);
            case 2: return !inStock && !notOwned;
            case 3: return inStock;
            case 4: return notOwned;
            default: return true;
        }
    }

    /// <summary>Type and Subject filters (null or "All" = any).</summary>
    public static bool TypeMatches(BookData b, string typeName, string subjectName) =>
        (typeName == null || typeName == "All" || b.TypeName == typeName) &&
        (subjectName == null || b.SubjectName == subjectName);

    void Labels()
    {
        typeButton.text = "Type: " + TypeNames[type];
        subjectButton.text = type == 0 ? "Subject: pick a type first" : "Subject: " + (subject ?? "All");
        subjectButton.SetEnabled(type != 0);
        showButton.text = "Show: " + Shows[show];
    }

    public void Reset()
    {
        type = show = 0;
        subject = null;
        PickList.Close();
        search.SetValueWithoutNotify("");
        Labels();
    }

    /// <summary>Starts with this in the search box.</summary>
    public void SetSearch(string text) => search.SetValueWithoutNotify(text ?? "");

    public bool Matches(BookData b)
    {
        string q = (search.value ?? "").Trim().ToLowerInvariant();
        if (q.Length > 0 && !(b.Title ?? "").ToLowerInvariant().Contains(q) && !(b.Author ?? "").ToLowerInvariant().Contains(q) && !(b.Tag ?? "").ToLowerInvariant().Contains(q)) return false;
        return TypeMatches(b, TypeNames[type], subject) && ShowMatches(show, b);
    }
}
