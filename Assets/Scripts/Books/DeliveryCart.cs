using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Book cart by the counter where ordered books arrive. Click it to open its page (like a shelf):
/// every copy that came in, who reserved it, and Take / Put back buttons.
/// </summary>
public class DeliveryCart : ClickInteractable
{
    public static DeliveryCart Instance { get; private set; }

    readonly List<BookData> copies = new List<BookData>();
    public IReadOnlyList<BookData> Copies => copies;
    public int Count => copies.Count;
    public bool Has(BookData b) => copies.Contains(b);
    readonly List<BookData> takenOut = new List<BookData>(); // copies you took off the cart (they can go back on)

    /// <summary>Takes one copy of this book off the cart. False if there's none.</summary>
    public bool Take(BookData book)
    {
        if (!copies.Remove(book)) return false;
        takenOut.Add(book);
        return true;
    }

    /// <summary>A delivered copy nobody has reserved (the cleaner shelves these; reserved ones wait here for you).</summary>
    public bool HasUnreserved
    {
        get
        {
            foreach (var b in copies) if (Shelvable(b)) return true;
            return false;
        }
    }

    /// <summary>Not reserved, and not a rare book (you carry those yourself, e.g. for a VIP).</summary>
    static bool Shelvable(BookData b)
    {
        var gm = GameManager.Instance;
        return !BookRequest.IsRare(b) && (gm == null || !gm.reservations.Exists(r => r.title == b.Title));
    }

    /// <summary>The cleaner takes one unreserved copy off the cart to shelve it. False if every copy is reserved.</summary>
    public bool TakeUnreserved(out BookData book)
    {
        book = copies.Find(Shelvable);
        if (book == null) return false;
        copies.Remove(book);
        GameManager.Instance?.NotifyBooksChanged();
        return true;
    }

    /// <summary>True if this copy came off the cart, so it can be put back on it.</summary>
    public bool CameFromCart(BookData book) => takenOut.Contains(book);

    /// <summary>Puts a copy you took off the cart back on it.</summary>
    public bool PutBack(BookData book)
    {
        if (!takenOut.Remove(book)) return false;
        copies.Add(book);
        return true;
    }

    protected override string DefaultVerb => "Deliveries";
    public override string Verb => copies.Count > 0 ? $"Deliveries ({copies.Count})" : "Deliveries";

    protected override void Awake()
    {
        base.Awake();
        Instance = this;
    }

    void OnDestroy() { if (Instance == this) Instance = null; }

    public void Add(BookData book)
    {
        if (book == null) return;
        copies.Add(book);
        Bubble(copies.Count == 1 ? $"Delivery: \"{book.Title}\"" : $"Delivery! {copies.Count} books waiting.");
    }

    public override void Interact(GameObject player)
    {
        var carry = player.GetComponent<PlayerCarry>();
        bool holdingCartBooks = carry != null && carry.Find((b, c) => takenOut.Contains(b)) >= 0;
        if (copies.Count == 0 && !holdingCartBooks) { Bubble("No deliveries. Order books on the PC (Shop)."); return; }
        if (carry != null && ShelfUI.Instance != null) { ShelfUI.Instance.OpenCart(this, carry); return; }
        if (copies.Count == 0) return;
        if (carry == null || carry.IsFull) { Bubble("Your hands are full."); return; }

        // The book someone at the desk is waiting for first, then other reserved titles.
        var gm = GameManager.Instance;
        int pick = copies.Count - 1;
        var atDesk = new System.Collections.Generic.List<string>();
        foreach (var c in FindObjectsByType<Customer>())
            if (c.IsWaiting && c.CurrentRequest != null && c.CurrentRequest.Target != null) atDesk.Add(c.CurrentRequest.Target.Title);
        int wanted = copies.FindIndex(b => atDesk.Contains(b.Title));
        if (wanted >= 0) pick = wanted;
        else if (gm != null)
            for (int i = 0; i < copies.Count; i++)
                if (gm.reservations.Exists(r => r.title == copies[i].Title)) { pick = i; break; }

        var book = copies[pick];
        Take(book);
        carry.PickUp(book, BookCondition.Good);
        var res = gm != null ? gm.reservations.Find(r => r.title == book.Title) : null;
        Bubble(res != null ? $"\"{book.Title}\" - reserved for {res.name}."
                           : $"Took \"{book.Title}\" from the cart.");
    }

    void Bubble(string text) => WorldPrompts.Instance?.ShowBubble(this, text, 3f);
}
