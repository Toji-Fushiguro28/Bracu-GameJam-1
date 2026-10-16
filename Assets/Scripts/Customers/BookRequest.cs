using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public enum HintType { Author, Subject, Tag, Abstract, Format, FormatSubject }

/// <summary>
/// What a customer asks for. Stores a rule, not a book: any item matching the rule counts
/// (any book by the author, any newspaper, any Math journal...). Abstract hints accept only the one item.
/// </summary>
public class BookRequest
{
    public HintType Hint;
    public string Value;          // author, subject or tag
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
        _ => 1.5f,
    };

    public bool Matches(BookData item)
    {
        if (item == null) return false;
        switch (Hint)
        {
            case HintType.Author: return item.Author == Value;
            case HintType.Subject: return item.Format == BookFormat.Book && item.Subject == Value;
            case HintType.Tag: return item.HasTag(Value);
            case HintType.Format: return item.Format == Format;
            case HintType.FormatSubject: return item.Format == Format && item.Subject == Value;
            default: return item == Target;
        }
    }

    // Tags a customer can ask for, with how they'd say it.
    static readonly Dictionary<string, string> TagLines = new Dictionary<string, string>
    {
        { "Best Seller", "What's your best seller right now?" },
        { "New Arrival", "Anything new come in lately?" },
        { "Classic", "I'd love a real classic." },
        { "Textbook", "I need a textbook for class." },
        { "English Literature", "Something from English literature, please." },
        { "Reference", "I need a good reference book." },
        { "Daily", "Today's paper, please." },
        { "Weekly", "Do you have this week's paper?" },
        { "Puzzles", "Anything with puzzles in it?" },
    };

    public static bool IsRare(BookData b) => b.ShelfLocation != null && b.ShelfLocation.StartsWith("RARE");
    public static bool IsArchive(BookData b) => b.ShelfLocation != null && b.ShelfLocation.StartsWith("ARC");

    /// <summary>
    /// Builds a request for a random item that's on a shelf, or null if none.
    /// Buyers only get sellable items. Readers may also ask for Archive items;
    /// VIP readers (Dean, professors) usually ask for Rare Books.
    /// </summary>
    public static BookRequest Create(IList<BookData> items, bool forReading = false, bool vip = false)
    {
        bool InStock(BookData b) => b.GoodCopies + b.WornCopies > 0;
        var candidates = items.Where(b => InStock(b) && (b.Sellable || (forReading && (IsArchive(b) || (vip && IsRare(b)))))).ToList();
        if (vip && forReading && Random.value < 0.7f)
        {
            var rare = candidates.Where(IsRare).ToList();
            if (rare.Count > 0) candidates = rare;
        }
        if (candidates.Count == 0) return null;
        var item = candidates[Random.Range(0, candidates.Count)];
        var req = new BookRequest { Target = item, Format = item.Format };
        var tags = (item.Tag ?? "").Split(',').Select(t => t.Trim()).Where(TagLines.ContainsKey).ToList();
        float r = Random.value;

        // Periodicals and papers
        if (item.Format != BookFormat.Book)
        {
            if (r < 0.3f && tags.Count > 0) return Tag(req, tags);
            if (r < 0.55f) return About(req);
            switch (item.Format)
            {
                case BookFormat.Newspaper:
                    return Set(req, HintType.Format, null, Pick("Can I see today's newspaper?", "Got a newspaper I could read?"));
                case BookFormat.Magazine:
                    return Set(req, HintType.Format, null, Pick("Got any magazines to flip through?", "I'd love a magazine for the bus."));
                case BookFormat.Journal:
                    return Set(req, HintType.FormatSubject, item.Subject, $"I need a {item.Subject} journal.");
                default:
                    return Set(req, HintType.FormatSubject, item.Subject, $"I'm after a research paper on {item.Subject}.");
            }
        }

        // Books
        if (r < 0.3f) return Set(req, HintType.Author, item.Author, $"Do you have anything by {item.Author}?");
        if (r < 0.55f) return Set(req, HintType.Subject, item.Subject, $"I need a good {item.Subject} book.");
        if (r < 0.8f && tags.Count > 0) return Tag(req, tags);
        return About(req);
    }

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
        string noun = r.Target.Format == BookFormat.Book ? "book" : r.Target.FormatName.ToLowerInvariant();
        return Set(r, HintType.Abstract, null, $"I'm looking for that {noun} about {about}.");
    }

    static string Pick(params string[] lines) => lines[Random.Range(0, lines.Length)];
}
