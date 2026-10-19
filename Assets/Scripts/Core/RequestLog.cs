using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// The librarian's notebook and the audit log behind it. Every request you hear (books, pickups,
/// library cards) becomes an entry: what they asked, the hint, your guesses and how it ended.
/// Open entries show in the notebook; all entries are saved and shown on the PC and the Day Report.
/// </summary>
public static class RequestLog
{
    public enum Kind { Book, Pickup, Card, Shelve, Money }

    [Serializable]
    public class Guess
    {
        public string title; // the book, if this guess was a book
        public string text;
        public bool wrong;
        public string reason;
    }

    [Serializable]
    public class Entry
    {
        public int day;
        public float hour;
        public string who;
        public Kind kind;
        public string line, hint, outcome;
        public bool usedHint;
        public List<Guess> guesses = new List<Guess>();
        public List<string> notes = new List<string>();
        /// <summary>Coins this entry brought in (+) or cost (-). 0 = no money changed hands.</summary>
        public int coins;
    }

    /// <summary>Every entry in this save, oldest first.</summary>
    public static List<Entry> All { get; private set; } = new List<Entry>();
    /// <summary>What the notebook shows right now, oldest first.</summary>
    public static readonly List<Entry> Open = new List<Entry>();
    static readonly Dictionary<Customer, Entry> live = new Dictionary<Customer, Entry>();
    /// <summary>The "To Shelve" page: books you looked up to put away. Notebook only, not logged.</summary>
    static Entry shelving;

    /// <summary>Notebook or log changed: redraw.</summary>
    public static event Action Changed;

    static GameManager GM => GameManager.Instance;

    /// <summary>A fresh scene: nobody is at the counter and the log comes from the save.</summary>
    public static void Reset(List<Entry> saved = null)
    {
        All = saved ?? new List<Entry>();
        Open.Clear();
        live.Clear();
        shelving = null;
        Changed?.Invoke();
    }

    public static bool Has(Customer c) => c != null && live.ContainsKey(c);

    /// <summary>Writes a new entry the first time you hear a visitor's request.</summary>
    public static void Begin(Customer c, Kind kind, string who, string line, string hint = null, bool usedHint = false)
    {
        if (c == null || live.ContainsKey(c)) return;
        var e = new Entry { day = GM != null ? GM.day : 1, hour = GM != null ? GM.Hour : 9f, who = who, kind = kind, line = line, hint = hint, usedHint = usedHint };
        All.Add(e);
        Open.Add(e);
        live[c] = e;
        Changed?.Invoke();
    }

    public static void SetHint(Customer c, string hint, bool paid)
    {
        if (!live.TryGetValue(c, out var e)) return;
        e.hint = hint;
        e.usedHint |= paid;
        Changed?.Invoke();
    }

    /// <summary>The newest open book request (pickups and cards don't take guesses).</summary>
    public static Entry Current => Open.FindLast(e => e.kind == Kind.Book);

    static string NotedText(BookData b) => $"Noted: \"{b.Title}\" ({ShelfInteractable.NameOf(b.ShelfLocation)})";

    /// <summary>True if the PC's "Add to note" would add nothing new for this book.</summary>
    public static bool IsNoted(BookData book)
    {
        if (book == null) return false;
        var e = Current;
        if (e != null)
        {
            var last = e.guesses.Count > 0 ? e.guesses[e.guesses.Count - 1] : null;
            return last != null && !last.wrong && last.title == book.Title;
        }
        return shelving != null && shelving.guesses.Exists(g => g.title == book.Title);
    }

    /// <summary>
    /// The PC's "Add to note". With a book request open it's your answer for it (swaps an untested one,
    /// or adds a new line after a wrong one). Otherwise the book goes on the "To Shelve" page.
    /// </summary>
    public static void AddToNote(BookData book)
    {
        if (book == null || IsNoted(book)) return;
        var e = Current;
        if (e != null)
        {
            var last = e.guesses.Count > 0 ? e.guesses[e.guesses.Count - 1] : null;
            if (last != null && !last.wrong && last.title != null) { last.title = book.Title; last.text = NotedText(book); }
            else e.guesses.Add(new Guess { title = book.Title, text = NotedText(book) });
        }
        else
        {
            if (shelving == null || !Open.Contains(shelving))
            {
                shelving = new Entry { day = GM != null ? GM.day : 1, hour = GM != null ? GM.Hour : 9f, kind = Kind.Shelve };
                Open.Add(shelving);
            }
            shelving.guesses.Add(new Guess { title = book.Title, text = NotedText(book) });
        }
        Changed?.Invoke();
    }

    /// <summary>A book went back on its shelf: tick it off the "To Shelve" page.</summary>
    public static void Shelved(BookData book)
    {
        if (shelving == null || book == null || shelving.guesses.RemoveAll(g => g.title == book.Title) == 0) return;
        if (shelving.guesses.Count == 0) { Open.Remove(shelving); shelving = null; }
        Changed?.Invoke();
    }

    /// <summary>The visitor turned down a book: strike the guess, or note the book you handed over.</summary>
    public static void Rejected(Customer c, BookData book)
    {
        if (!live.TryGetValue(c, out var e) || book == null) return;
        var g = e.guesses.FindLast(x => !x.wrong && x.title == book.Title);
        if (g != null) g.wrong = true;
        else e.guesses.Add(new Guess { title = book.Title, text = $"Handed: \"{book.Title}\"", wrong = true });
        Changed?.Invoke();
    }

    /// <summary>A library card filled in on the PC. Wrong ones are struck through with the visitor's reason.</summary>
    public static void CardAttempt(Customer c, string text, string wrongReason)
    {
        if (!live.TryGetValue(c, out var e)) return;
        e.guesses.Add(new Guess { text = text, wrong = wrongReason != null, reason = wrongReason });
        Changed?.Invoke();
    }

    /// <summary>An extra line for the log (a damaged copy, a wish for a new book...).</summary>
    public static void Note(Customer c, string text)
    {
        if (!live.TryGetValue(c, out var e)) return;
        e.notes.Add(text);
        Changed?.Invoke();
    }

    /// <summary>The task is over: record how it ended and take it out of the notebook.</summary>
    public static void Finish(Customer c, string outcome)
    {
        if (c == null || !live.TryGetValue(c, out var e)) return;
        e.outcome = outcome;
        live.Remove(c);
        Open.Remove(e);
        Changed?.Invoke();
    }

    /// <summary>A visitor paid (a sale, a fee, a tip): the amount goes on their entry, or on a line of its own if they have none.</summary>
    public static void Paid(Customer c, int amount, string who, string what)
    {
        if (amount == 0) return;
        if (c != null && live.TryGetValue(c, out var e)) { e.coins += amount; Changed?.Invoke(); }
        else Money(who, what, what, amount);
    }

    /// <summary>Money in or out that isn't a visitor's request: orders, upgrades, staff, rewards.
    /// It goes straight into the log (never the notebook).</summary>
    public static void Money(string who, string line, string outcome, int amount)
    {
        if (amount == 0) return;
        All.Add(new Entry { day = GM != null ? GM.day : 1, hour = GM != null ? GM.Hour : 9f, who = who, kind = Kind.Money, line = line, outcome = outcome, coins = amount });
        Changed?.Invoke();
    }

    /// <summary>"+25" / "-15" for a log row.</summary>
    public static string CoinText(int coins) => coins > 0 ? "+" + coins : "-" + (-coins);

    /// <summary>The notebook's Clear button: off the page, still in the log.</summary>
    public static void Clear(Entry e)
    {
        if (e == shelving) shelving = null;
        if (Open.Remove(e)) Changed?.Invoke();
    }

    public static string KindName(Kind k) => k == Kind.Book ? "Book Request" : k == Kind.Pickup ? "Pickup" : k == Kind.Card ? "Library Card" : k == Kind.Money ? "Coins" : "To Shelve";

    /// <summary>Guess lines as rich text: wrong ones struck through.</summary>
    public static string GuessText(Guess g) => g.wrong
        ? $"<s>{g.text}</s>" + (string.IsNullOrEmpty(g.reason) ? "" : $"  <i>{g.reason}</i>")
        : g.text;

    /// <summary>One audit-log entry. The stylesheet it lands in (PC or Day Report) colours the classes.</summary>
    public static VisualElement BuildRow(Entry e)
    {
        var row = new VisualElement();
        row.AddToClassList("log-row");
        string who = string.IsNullOrEmpty(e.who) ? "" : $"  ·  {e.who}";
        string coins = e.coins == 0 ? "" : $"  ·  <color={(e.coins > 0 ? "#4F6B3A" : "#8E3B32")}>{CoinText(e.coins)} coins</color>";
        Add(row, "log-head", $"{GameManager.FormatHour(e.hour)}{who}  ·  {KindName(e.kind)}{coins}");
        if (e.kind == Kind.Money) { Add(row, "log-line", e.line); return row; }
        Add(row, "log-line", $"\"{e.line}\"");
        if (e.kind == Kind.Book)
            Add(row, "log-line", e.hint != null ? $"<color=#D67840>Hint: {e.hint}</color>" + (e.usedHint ? "  (asked for)" : "  (free)") : "No hint used");
        foreach (var g in e.guesses) Add(row, "log-line", GuessText(g));
        foreach (var n in e.notes) Add(row, "log-line", n);
        Add(row, "log-outcome", "Outcome: " + (e.outcome ?? "still waiting"));
        return row;
    }

    static void Add(VisualElement row, string cls, string text)
    {
        var l = new Label(text) { enableRichText = true };
        l.AddToClassList(cls);
        row.Add(l);
    }
}
