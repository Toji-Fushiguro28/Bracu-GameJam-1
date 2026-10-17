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
    }

    /// <summary>Every copy in the bin (for saving).</summary>
    public IEnumerable<(BookData book, BookCondition condition)> Copies
    {
        get { foreach (var c in pile) yield return (c.book, c.condition); }
    }

    public bool HasNeedingRepair => pile.Exists(c => c.condition != BookCondition.Good);

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

    public override void Interact(GameObject player)
    {
        var carry = player.GetComponent<PlayerCarry>();
        if (pile.Count == 0) { Bubble("The return bin is empty."); return; }
        if (carry == null || carry.IsFull) { Bubble("Your hands are full."); return; }

        var copy = pile[pile.Count - 1];
        pile.RemoveAt(pile.Count - 1);
        carry.PickUp(copy.book, copy.condition);
        string note = copy.condition == BookCondition.Good ? "" : $" ({copy.condition.ToString().ToLower()})";
        Bubble($"Took \"{copy.book.Title}\"{note} from the returns.");
    }

    void Bubble(string text)
    {
        if (WorldPrompts.Instance != null) WorldPrompts.Instance.ShowBubble(this, text, 3f);
    }
}
