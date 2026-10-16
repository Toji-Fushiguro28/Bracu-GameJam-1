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

    public override void Interact(GameObject player)
    {
        var carry = player.GetComponent<PlayerCarry>();
        if (pile.Count == 0) { Bubble("The return bin is empty."); return; }
        if (carry == null || !carry.IsEmpty) { Bubble("Your hands are full."); return; }

        var copy = pile[pile.Count - 1];
        pile.RemoveAt(pile.Count - 1);
        carry.PickUp(copy.book, copy.condition);
        string note = copy.condition == BookCondition.Good ? "" : $" ({copy.condition.ToString().ToLower()})";
        Bubble($"\"{copy.book.Title}\"{note} - goes in {ShelfInteractable.NameOf(copy.book.ShelfLocation)}.");
    }

    void Bubble(string text)
    {
        if (WorldPrompts.Instance != null) WorldPrompts.Instance.ShowBubble(this, text, 3f);
    }
}
