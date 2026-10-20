using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public enum HintType { Author, Subject, Tag, Abstract, Format, FormatSubject, Title }

/// <summary>
/// What a customer asks for. Stores a rule, not a book: any item matching the rule counts
/// (any book by the author, any newspaper, any Math journal...). Abstract hints accept only the one item.
/// </summary>
public class BookRequest
{
    public HintType Hint;
    public string Value;          // author, subject (topic) or tag
    public BookFormat Format;     // for Format / FormatSubject
    public BookData Target;       // the item the hint was written from
    public string Line;           // what the customer says

    /// <summary>Seconds of patience at the counter: vaguer hints get more time.</summary>
    public float Patience => Hint switch
    {
        HintType.Author => 55f,
        HintType.Subject => 60f,
        HintType.Format => 50f,
        HintType.FormatSubject => 60f,
        HintType.Tag => 70f,
        HintType.Title => 55f,
        _ => 90f,
    };

    /// <summary>Tip multiplier for solving a vaguer hint.</summary>
    public float TipBonus => Hint switch
    {
        HintType.Author => 1f,
        HintType.Format => 0.9f,
        HintType.Subject => 1.1f,
        HintType.FormatSubject => 1.1f,
        HintType.Tag => 1.25f,
        HintType.Title => 1f,
        _ => 1.5f,
    };

    public bool Matches(BookData item)
    {
        if (item == null) return false;
        switch (Hint)
        {
            case HintType.Author: return item.Author == Value;
            case HintType.Subject: return item.Format == BookFormat.Book && OnSubject(item);
            case HintType.Tag: return item.HasTag(Value);
            case HintType.Format: return item.Format == Format;
            case HintType.FormatSubject: return item.Format == Format && OnSubject(item);
            case HintType.Title: return item == Target;
            default: return item == Target;
        }
    }

    /// <summary>Its subject (topic) is the one asked for. The old section still counts, for requests made with one (the exam rush).</summary>
    bool OnSubject(BookData item) => item.SubjectName == Value || item.Subject == Value;

    // Tags a customer can ask for, with how they'd say it.
    static readonly Dictionary<string, string> TagLines = new Dictionary<string, string>
    {
        { "Best Seller", "What's your <color=#D67840>best seller</color> right now?" },
        { "New Arrival", "Anything <color=#D67840>new</color> come in lately?" },
        { "Classic", "I'd love a real <color=#D67840>classic</color>." },
        { "Textbook", "I need a proper <color=#D67840>textbook</color>." },
        { "English Literature", "Something from <color=#D67840>English literature</color>, please." },
        { "Reference", "I need a good <color=#D67840>reference</color> book." },
        { "Daily", "<color=#D67840>Today's paper</color>, please." },
        { "Weekly", "Do you have <color=#D67840>this week's</color> paper?" },
        { "Puzzles", "Anything with <color=#D67840>puzzles</color> in it?" },
    };

    public static bool IsRare(BookData b) => b.ShelfLocation != null && b.ShelfLocation.StartsWith("RARE");
    public static bool IsArchive(BookData b) => b.ShelfLocation != null && b.ShelfLocation.StartsWith("ARC");

    // "Authors" nobody would ask for by name (old records, anonymous manuscripts).
    static readonly HashSet<string> NoAuthor = new HashSet<string> { "Unknown", "Anonymous", "Various", "Whitmore Library" };
    /// <summary>It has an author a visitor could ask for by name.</summary>
    public static bool HasAuthor(BookData b) => b != null && !string.IsNullOrEmpty(b.Author) && !NoAuthor.Contains(b.Author);

    static readonly string[] Sections = { "Science", "History", "Philosophy", "Math" };

    /// <summary>What it is and where it lives, as a visitor would say it: "a Math book" (main shelves),
    /// "an old record from the Archive", "one of the Rare Books", "a magazine".</summary>
    public static string Kind(BookData b)
    {
        if (IsArchive(b)) return "an old record from the Archive";
        if (IsRare(b)) return "one of the Rare Books";
        if (b.Format == BookFormat.Book) return $"a {b.Subject} book";
        return b.Format == BookFormat.ResearchPaper ? "a research paper" : $"a {b.FormatName.ToLowerInvariant()}";
    }

    /// <summary>A hint about what kind of item it is. Always true, but from Day 3 a visitor sometimes only half
    /// remembers the shelf ("Science... or was it History?"): the right one is always one of the two.</summary>
    public static string KindHint(BookData b)
    {
        var gm = GameManager.Instance;
        bool mainShelf = b.Format == BookFormat.Book && !IsArchive(b) && !IsRare(b) && System.Array.IndexOf(Sections, b.Subject) >= 0;
        if (mainShelf && (gm == null || gm.day >= 3) && Random.value < 0.2f)
        {
            var others = Sections.Where(s => s != b.Subject).ToArray();
            string other = others[Random.Range(0, others.Length)];
            bool rightFirst = Random.value < 0.5f;
            return $"It's a {(rightFirst ? b.Subject : other)} book... or was it {(rightFirst ? other : b.Subject)}? One of those two.";
        }
        string kind = Kind(b);
        if (IsArchive(b) || IsRare(b) || b.Format != BookFormat.Book)
            return $"It's {kind}" + (string.IsNullOrEmpty(b.Topic) ? "." : $", filed under {b.SubjectName}."); // the catalogue's Subject
        return Pick($"I think it's {kind}.", $"I'm fairly sure it's {kind}.", $"It's {kind}, if that helps.");
    }

    /// <summary>Asking by topic, worded for where it lives (Archive and Rare Books say so).</summary>
    static string SubjectLine(BookData b) =>
        IsArchive(b) ? $"Could I look at something from the {H("Archive")} on {H(b.SubjectName)}?"
      : IsRare(b) ? $"May I see one of your {H("rare books")} on {H(b.SubjectName)}?"
      : $"I need a good book on {H(b.SubjectName)}.";

    /// <summary>
    /// Builds a request for a random item that's on a shelf, or null if none.
    /// Buyers only get sellable items. Readers may also ask for Archive items;
    /// VIP readers (Dean, professors) usually ask for Rare Books.
    /// </summary>
    public static bool InStock(BookData b) => b.GoodCopies + b.WornCopies > 0;

    /// <summary>False for Archive items before the Archive opens, and Rare items before Rare Books opens.</summary>
    public static bool Available(BookData b)
    {
        var gm = GameManager.Instance;
        if (gm == null) return true;
        if (IsArchive(b) && !gm.ArchiveOpen) return false;
        if (IsRare(b) && !gm.RareOpen) return false;
        return true;
    }

    /// <summary>True if no copy on the shelves would satisfy this request right now.</summary>
    public bool OutOfStock(IList<BookData> items) => !items.Any(b => Matches(b) && InStock(b) && (b.Sellable || IsArchive(b) || IsRare(b)));

    /// <summary>Chance a buyer asks for something the library doesn't have, by day.</summary>
    public static float OutOfStockChance(int day) => day < 2 ? 0f : day == 2 ? 0.2f : 0.25f;

    public static BookRequest Create(IList<BookData> items, bool forReading = false, bool vip = false)
    {
        var gm = GameManager.Instance;
        int day = gm != null ? gm.day : 3;

        // Sometimes a buyer wants a title that's sold out: check the catalogue, then order and reserve it.
        if (!forReading && Random.value < (gm != null ? gm.OutOfStockChance : OutOfStockChance(day)))
        {
            var missing = items.Where(b => b.Sellable && !InStock(b) && Available(b)).ToList();
            if (missing.Count > 0)
            {
                var want = Weighted(missing, day);
                var oos = new BookRequest { Target = want, Format = want.Format };
                return Random.value < 0.6f
                    ? Set(oos, HintType.Title, null, Pick($"Do you have {H($"\"{want.Title}\"")}?", $"I'm looking for {H($"\"{want.Title}\"")}."))
                    : About(oos);
            }
        }

        bool archive = gm == null || gm.ArchiveOpen, rareOpen = gm == null || gm.RareOpen;
        // Today's popular books are asked for more, whether or not you have them in: that's what makes stocking them pay.
        // Readers only ask for what's on the shelves (they want to read it now).
        var candidates = items.Where(b => Available(b) && (InStock(b) || !forReading) && (b.Sellable || (forReading && ((archive && IsArchive(b)) || (vip && rareOpen && IsRare(b)))))).ToList();
        if (vip && forReading && Random.value < 0.7f)
        {
            var rare = candidates.Where(IsRare).ToList();
            if (rare.Count > 0) candidates = rare;
        }
        if (candidates.Count == 0) return null;
        var item = Weighted(candidates, day);
        // Wanted but not in stock: they ask for it by name, so you can order it for them.
        if (!InStock(item) && !forReading && item.Sellable)
        {
            var oos = new BookRequest { Target = item, Format = item.Format };
            return Random.value < 0.6f || day < 2
                ? Set(oos, HintType.Title, null, Pick($"Do you have {H($"\"{item.Title}\"")}?", $"I'm looking for {H($"\"{item.Title}\"")}."))
                : About(oos);
        }
        var req = new BookRequest { Target = item, Format = item.Format };
        var tags = (item.Tag ?? "").Split(',').Select(t => t.Trim()).Where(TagLines.ContainsKey).ToList();
        float r = Random.value;

        bool simple = day < 2; // first day: plain hints only
        // Periodicals and papers
        if (item.Format != BookFormat.Book)
        {
            if (!simple && r < 0.3f && tags.Count > 0) return Tag(req, tags);
            if (!simple && r < 0.55f) return About(req);
            switch (item.Format)
            {
                case BookFormat.Newspaper:
                    return Set(req, HintType.Format, null, forReading
                        ? Pick("Could I read today's paper here?", "Got a newspaper I could sit with?")
                        : Pick("Can I buy today's newspaper?", "I'll take a newspaper, please."));
                case BookFormat.Magazine:
                    return Set(req, HintType.Format, null, forReading
                        ? Pick("Got a magazine I could flip through here?", "I'd love a magazine to read for a bit.")
                        : Pick("I'd love a magazine for the bus.", "Can I buy a magazine?"));
                case BookFormat.Journal:
                    return Set(req, HintType.FormatSubject, item.SubjectName, $"I need a {H("journal")} on {H(item.SubjectName)}.");
                default:
                    return Set(req, HintType.FormatSubject, item.SubjectName, $"I'm after a {H("research paper")} on {H(item.SubjectName)}.");
            }
        }

        // Books (nobody asks for an old record "by Unknown": those are asked for by topic)
        bool byAuthor = HasAuthor(item);
        if (simple) return r < 0.5f && byAuthor
            ? Set(req, HintType.Author, item.Author, $"Do you have anything by {H(item.Author)}?")
            : Set(req, HintType.Subject, item.SubjectName, SubjectLine(item));
        if (r < 0.3f && byAuthor) return Set(req, HintType.Author, item.Author, $"Do you have anything by {H(item.Author)}?");
        if (r < 0.55f) return Set(req, HintType.Subject, item.SubjectName, SubjectLine(item));
        if (r < 0.8f && tags.Count > 0) return Tag(req, tags);
        return About(req);
    }

    /// <summary>Picks a book: today's popular books are asked for six times as often as the rest.
    /// On Day 1, books you have in stock get a little extra weight so the first day isn't all "sorry".</summary>
    static BookData Weighted(List<BookData> pool, int day)
    {
        float total = 0f;
        var w = new float[pool.Count];
        for (int i = 0; i < pool.Count; i++)
        {
            bool popular = GameManager.Instance != null && GameManager.Instance.IsPopular(pool[i]);
            w[i] = (popular ? 6f : 1f) * (day < 2 && InStock(pool[i]) ? 2f : 1f) * DayStory.RequestWeight(pool[i]); // exam subject, the signing author
            total += w[i];
        }
        float r = Random.value * total;
        for (int i = 0; i < pool.Count; i++) { r -= w[i]; if (r <= 0f) return pool[i]; }
        return pool[pool.Count - 1];
    }

    /// <summary>The highlight colour for the words that tell you which book (titles, authors, subjects, tags).</summary>
    public static string H(string s) => $"<color=#D67840>{s}</color>";

    static BookRequest Set(BookRequest r, HintType hint, string value, string line)
    {
        r.Hint = hint; r.Value = value; r.Line = line;
        return r;
    }

    static BookRequest Tag(BookRequest r, List<string> tags)
    {
        var tag = tags[Random.Range(0, tags.Count)];
        return Set(r, HintType.Tag, tag, TagLines[tag]);
    }

    static BookRequest About(BookRequest r)
    {
        string about = (r.Target.Abstract ?? r.Target.Title).TrimEnd('.');
        about = char.ToLowerInvariant(about[0]) + about.Substring(1);
        // The one word that gives it away: search it on the computer (the catalogue searches descriptions too).
        string key = KeyWord(r.Target);
        if (key != null) about = System.Text.RegularExpressions.Regex.Replace(about, $@"\b{System.Text.RegularExpressions.Regex.Escape(key)}\b", m => H(m.Value), System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        string noun = IsArchive(r.Target) ? "old record" : IsRare(r.Target) ? "rare book"
                    : r.Target.Format == BookFormat.Book ? "book" : r.Target.FormatName.ToLowerInvariant();
        return Set(r, HintType.Abstract, null, $"I'm looking for that {noun} about {about}.");
    }

    static readonly HashSet<string> Common = new HashSet<string>
    { "about", "after", "again", "their", "there", "these", "those", "which", "while", "where", "other", "every", "under", "through",
      "could", "would", "should", "into", "from", "with", "that", "this", "what", "when", "your", "they", "them", "were", "have",
      "been", "being", "some", "more", "most", "very", "just", "only", "also", "than", "then", "over", "each", "much", "many" };

    /// <summary>The most telling word in a description: at least 4 letters, not a common one, and found in as few
    /// other descriptions as possible (ideally only this one), so searching it finds the book.</summary>
    public static string KeyWord(BookData b)
    {
        if (b == null || string.IsNullOrEmpty(b.Abstract)) return null;
        var gm = GameManager.Instance;
        var all = gm != null ? gm.Books.Concat(gm.NewTitles).ToList() : new List<BookData>();
        string best = null;
        int bestCount = int.MaxValue;
        foreach (System.Text.RegularExpressions.Match m in System.Text.RegularExpressions.Regex.Matches(b.Abstract, @"[A-Za-z']{4,}"))
        {
            string w = m.Value.Trim('\'').ToLowerInvariant();
            if (w.Length < 4 || Common.Contains(w)) continue;
            int count = all.Count(x => x != b && !string.IsNullOrEmpty(x.Abstract) && x.Abstract.ToLowerInvariant().Contains(w));
            if (count < bestCount || (count == bestCount && best != null && w.Length > best.Length)) { best = w; bestCount = count; }
        }
        return best;
    }

    static string Pick(params string[] lines) => lines[Random.Range(0, lines.Length)];
}
