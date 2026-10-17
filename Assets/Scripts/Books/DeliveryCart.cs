using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Book cart by the counter where ordered books arrive. Click to take the next copy;
/// reserved books are handed out first so you can give them straight to whoever reserved them.
/// </summary>
public class DeliveryCart : ClickInteractable
{
    public static DeliveryCart Instance { get; private set; }

    readonly List<BookData> copies = new List<BookData>();
    public IReadOnlyList<BookData> Copies => copies;
    public int Count => copies.Count;
    public bool Has(BookData b) => copies.Contains(b);

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
        if (copies.Count == 0) { Bubble("No deliveries. Order books on the PC (Shop)."); return; }
        if (carry == null || carry.IsFull) { Bubble("Your hands are full."); return; }

        // Reserved titles first.
        var gm = GameManager.Instance;
        int pick = copies.Count - 1;
        if (gm != null)
            for (int i = 0; i < copies.Count; i++)
                if (gm.reservations.Exists(r => r.title == copies[i].Title)) { pick = i; break; }

        var book = copies[pick];
        copies.RemoveAt(pick);
        carry.PickUp(book, BookCondition.Good);
        var res = gm != null ? gm.reservations.Find(r => r.title == book.Title) : null;
        Bubble(res != null ? $"\"{book.Title}\" - reserved for {res.name}."
                           : $"Took \"{book.Title}\" from the cart.");
    }

    void Bubble(string text) => WorldPrompts.Instance?.ShowBubble(this, text, 3f);
}
