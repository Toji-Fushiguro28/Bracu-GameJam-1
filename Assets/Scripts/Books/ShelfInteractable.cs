using System.Collections.Generic;
using System.Linq;
using UnityEngine;

/// <summary>
/// Click a bookshelf to open the shelf UI: take a book or put the carried one back.
/// Each bookcase has its own id, so every case shows its own books.
/// </summary>
public class ShelfInteractable : ClickInteractable
{
    [Tooltip("Matches BookData.ShelfLocation.")]
    public string shelfId = "SCI";
    public string displayName = "Science";

    static readonly Dictionary<string, string> names = new Dictionary<string, string>();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() => names.Clear();

    protected override string DefaultVerb => "Browse";

    protected override void Awake()
    {
        base.Awake();
        names[shelfId] = displayName;
    }

    /// <summary>Display name of the shelf a book belongs on, e.g. "Rare Books II".</summary>
    public static string NameOf(string shelfId) =>
        shelfId != null && names.TryGetValue(shelfId, out var n) ? n : shelfId;

    public IEnumerable<BookData> Books =>
        GameManager.Instance != null
            ? GameManager.Instance.Books.Where(b => b.ShelfLocation == shelfId)
            : Enumerable.Empty<BookData>();

    public bool Belongs(BookData book) => book != null && book.ShelfLocation == shelfId;

    public override void Interact(GameObject player)
    {
        if (ShelfUI.Instance != null) ShelfUI.Instance.Open(this, player.GetComponent<PlayerCarry>());
    }
}
