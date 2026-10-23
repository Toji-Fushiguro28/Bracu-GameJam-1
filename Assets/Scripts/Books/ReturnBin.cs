using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Returned copies pile up here. Click with empty hands to take one and reshelve it.
/// </summary>
public class ReturnBin : ClickInteractable
{
    public static ReturnBin Instance { get; private set; }

    struct Copy { public BookData book; public BookCondition condition; }
    readonly List<Copy> pile = new List<Copy>();

    public int Count => pile.Count;

    protected override string DefaultVerb => "Returns";
    public override string Verb => pile.Count > 0 ? $"Returns ({pile.Count})" : "Returns";

    protected override void Awake()
    {
        base.Awake();
        Instance = this;
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    public void Add(BookData book, BookCondition condition)
    {
        if (book != null) pile.Add(new Copy { book = book, condition = condition });
        if (book != null && condition == BookCondition.Damaged)
            Tutorial.Show("damaged", "A damaged book is in the return bin. Mend it at the repair desk.");
    }

    /// <summary>Every copy in the bin (for saving).</summary>
    public IEnumerable<(BookData book, BookCondition condition)> Copies
    {
        get { foreach (var c in pile) yield return (c.book, c.condition); }
    }

    public bool HasNeedingRepair => pile.Exists(c => c.condition != BookCondition.Good);
    public bool HasGood => pile.Exists(c => c.condition == BookCondition.Good);

    /// <summary>Archivist: takes a good copy out of the returns to shelve it.</summary>
    public bool TakeGood(out BookData book, out BookCondition condition)
    {
        for (int i = pile.Count - 1; i >= 0; i--)
        {
            if (pile[i].condition != BookCondition.Good) continue;
            book = pile[i].book; condition = pile[i].condition;
            pile.RemoveAt(i);
            return true;
        }
        book = null; condition = BookCondition.Good;
        return false;
    }

    /// <summary>Repairer staff: takes the newest worn/damaged copy out of the bin.</summary>
    public bool TakeNeedingRepair(out BookData book, out BookCondition condition)
    {
        for (int i = pile.Count - 1; i >= 0; i--)
        {
            if (pile[i].condition == BookCondition.Good) continue;
            book = pile[i].book; condition = pile[i].condition;
            pile.RemoveAt(i);
            return true;
        }
        book = null; condition = BookCondition.Good;
        return false;
    }

    /// <summary>Takes this copy (that condition) out of the bin. False if it's gone (a staff member took it).</summary>
    public bool Take(BookData book, BookCondition condition)
    {
        int i = pile.FindLastIndex(c => c.book == book && c.condition == condition);
        if (i < 0) return false;
        pile.RemoveAt(i);
        takenOut.Add((book, condition));
        return true;
    }

    // Copies you took out of the bin: they can go back in (as they were).
    readonly List<(BookData book, BookCondition condition)> takenOut = new List<(BookData, BookCondition)>();
    public bool CameFromBin(BookData book, BookCondition condition) => takenOut.Contains((book, condition));

    /// <summary>Puts a copy you took out back in the bin.</summary>
    public void PutBack(BookData book, BookCondition condition)
    {
        if (!takenOut.Remove((book, condition))) return;
        pile.Add(new Copy { book = book, condition = condition });
    }

    public override void Interact(GameObject player)
    {
        var carry = player.GetComponent<PlayerCarry>();
        // Forget copies you've since shelved or handed over: only ones still in your hands can go back in.
        if (carry != null) takenOut.RemoveAll(t => carry.Find((b, c) => b == t.book && c == t.condition) < 0);
        bool holdingBinBooks = carry != null && carry.Find((b, c) => takenOut.Contains((b, c))) >= 0;
        // The same book screen as the shelves and the cart: pick which copy to take.
        if (carry != null && ShelfUI.Instance != null && (pile.Count > 0 || holdingBinBooks)) { ShelfUI.Instance.OpenBin(this, carry); return; }
        if (pile.Count == 0) { Bubble("The return bin is empty."); return; }
        if (carry == null || carry.IsFull) { Bubble("Your hands are full."); return; }

        var copy = pile[pile.Count - 1];
        pile.RemoveAt(pile.Count - 1);
        carry.PickUp(copy.book, copy.condition);
        ShelfUI.Instance?.PlayTake(); // same sound as taking a book off a shelf
        string note = copy.condition == BookCondition.Good ? "" : $" ({copy.condition.ToString().ToLower()})";
        Bubble($"Took \"{copy.book.Title}\"{note} from the returns.");
    }

    void Bubble(string text)
    {
        if (WorldPrompts.Instance != null) WorldPrompts.Instance.ShowBubble(this, text, 3f);
    }
}
