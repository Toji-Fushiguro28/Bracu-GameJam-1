using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine.UIElements;

/// <summary>
/// Search box + Type / Subject / Show drop-downs, shared by the PC catalogue and the end-of-day shop.
/// Subject works on its own: pick a whole shelf or type ("Science shelf: all") or one topic under it.
/// Picking a Type as well just narrows the Subject list to that type.
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

        search = new TextField { name = pcStyle ? "book-search" : "page-search", maxLength = 40 }; // longest title is well under 40
        search.AddToClassList("filter-search");
        search.textEdition.placeholder = "search anything: title, author, subject, words...";
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
            if (gm == null) return BookData.Samples();
            var rare = SpecialJobs.Instance != null ? SpecialJobs.Instance.WantedRare : null; // the rare dealer's offer
            return rare != null ? gm.Books.Concat(gm.NewTitles).Append(rare) : gm.Books.Concat(gm.NewTitles);
        }
    }

    /// <summary>Opens the Subject list. No need to pick a type first: every shelf (Science, History...) and every
    /// other type (Archive, Newspapers...) is a group you can pick whole ("Science shelf: all"), with its topics
    /// under it. With a Type picked, only that type's groups show. The value is a group ("@Book:Science",
    /// "@Archive") or a topic within a type ("Book|Astronomy"); see <see cref="TypeMatches"/>.</summary>
    public static void OpenSubjects(Button button, string typeName, string current, Action<string> pick, bool pcStyle)
    {
        var options = new List<string> { "All subjects" };
        var values = new List<string> { null };
        var groups = new List<bool> { false };
        bool anyType = typeName == null || typeName == "All";
        var books = Everything.ToList();
        void Group(string label, string value, IEnumerable<BookData> inGroup, string type)
        {
            var topics = inGroup.Select(b => b.SubjectName).Where(t => !string.IsNullOrEmpty(t)).Distinct().OrderBy(t => t).ToList();
            if (topics.Count == 0) return;
            options.Add(label + ": all"); values.Add(value); groups.Add(true);
            foreach (var t in topics) { options.Add("   " + t); values.Add(type + "|" + t); groups.Add(false); }
        }
        foreach (var type in TypeNames.Skip(1))
        {
            if (!anyType && type != typeName) continue;
            var ofType = books.Where(b => b.TypeName == type).ToList();
            if (type == "Book")
                foreach (var section in Sections) Group(section + " shelf", "@Book:" + section, ofType.Where(b => b.Subject == section), "Book");
            else Group(Plural(type), "@" + type, ofType, type);
        }
        int at = Math.Max(0, values.IndexOf(current));
        PickList.Open(button, options.ToArray(), at, i => pick(values[i]), pcStyle, null, -1, null, 0, i => groups[i]);
    }

    static string Plural(string type) => type == "Archive" || type == "Rare Books" ? type : type + "s";

    /// <summary>What the Subject button says for a picked value.</summary>
    public static string SubjectLabel(string value)
    {
        if (string.IsNullOrEmpty(value)) return "All";
        if (value.StartsWith("@Book:")) return value.Substring(6) + " shelf";
        if (value.StartsWith("@")) return Plural(value.Substring(1));
        int bar = value.IndexOf('|');
        return bar >= 0 ? value.Substring(bar + 1) : value;
    }

    /// <summary>All, Reserved (someone's waiting for it), Sold out, In stock, Not in library (a new title you could order).</summary>
    public static readonly string[] Shows = { "All", "Reserved", "Sold out", "In stock", "Not in library", "Damaged" };

    public static bool ShowMatches(int show, BookData b)
    {
        var gm = GameManager.Instance;
        bool inStock = b.GoodCopies + b.WornCopies > 0;
        bool notOwned = gm != null && (gm.IsNewTitle(b) || gm.RareFinds.Contains(b));
        switch (show)
        {
            case 1: return gm != null && gm.reservations.Exists(r => r.title == b.Title);
            case 2: return !inStock && !notOwned;
            case 3: return inStock;
            case 4: return notOwned;
            case 5: return b.DamagedCopies > 0; // copies waiting for the Repair desk
            default: return true;
        }
    }

    /// <summary>Type and Subject filters (null or "All" = any).</summary>
    public static bool TypeMatches(BookData b, string typeName, string subjectName) =>
        (typeName == null || typeName == "All" || b.TypeName == typeName) && SubjectMatches(b, subjectName);

    /// <summary>A whole group ("@Book:Science" = the Science shelf's books, "@Archive" = the Archive), a topic of one
    /// type ("Book|Astronomy"), or a plain topic name (any type).</summary>
    static bool SubjectMatches(BookData b, string value)
    {
        if (string.IsNullOrEmpty(value)) return true;
        if (value.StartsWith("@Book:")) return b.TypeName == "Book" && b.Subject == value.Substring(6);
        if (value.StartsWith("@")) return b.TypeName == value.Substring(1);
        int bar = value.IndexOf('|');
        if (bar >= 0) return b.TypeName == value.Substring(0, bar) && b.SubjectName == value.Substring(bar + 1);
        return b.SubjectName == value;
    }

    void Labels()
    {
        typeButton.text = "Type: " + TypeNames[type];
        subjectButton.text = "Subject: " + SubjectLabel(subject);
        subjectButton.SetEnabled(true); // no need to pick a type first
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

    public bool Matches(BookData b) => SearchMatches(b, search.value) && TypeMatches(b, TypeNames[type], subject) && ShowMatches(show, b);

    /// <summary>The one search (PC catalogue, Day Report shop): every word typed must appear somewhere in the book's
    /// text: title, author, subject, topic, tags, description, type, shelf code and name, room, tier. Any order;
    /// case, apostrophes and punctuation don't matter. Empty: everything.</summary>
    public static bool SearchMatches(BookData b, string query)
    {
        var words = Plain(query).Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
        if (words.Length == 0) return true;
        string hay = Plain(string.Join(" ", b.Title, b.Author, b.Subject, b.Topic, b.Tag, b.Abstract, b.FormatName, b.TypeName,
            b.ShelfLocation, ShelfInteractable.NameOf(b.ShelfLocation), b.Rarity,
            BookRequest.IsArchive(b) ? "archive" : "", BookRequest.IsRare(b) ? "rare books" : ""));
        foreach (var w in words) if (!hay.Contains(w)) return false;
        return true;
    }

    /// <summary>Lower case, apostrophes dropped ("Shakespeare's" = "shakespeares"), other punctuation as spaces.</summary>
    static string Plain(string s)
    {
        if (string.IsNullOrEmpty(s)) return "";
        var sb = new System.Text.StringBuilder(s.Length);
        foreach (char c in s.ToLowerInvariant())
        {
            if (c == '\'' || c == '’') continue;
            sb.Append(char.IsLetterOrDigit(c) ? c : ' ');
        }
        return sb.ToString();
    }
}
