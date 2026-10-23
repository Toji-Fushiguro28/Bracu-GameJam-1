using System.Collections.Generic;
using System.Linq;
using UnityEngine;

/// <summary>
/// Every title the library owns. Lives in Resources so any scene can load it.
/// </summary>
[CreateAssetMenu(menuName = "Library/Book Database", fileName = BookDatabase.ResourceName)]
public class BookDatabase : ScriptableObject
{
    public const string ResourceName = "BookDatabase";

    public List<BookData> books = new List<BookData>();

    public List<BookData> CreateRuntimeCopy() => books.Select(b => b.Clone()).ToList();

    [Tooltip("Books the library doesn't own yet. Visitors ask for these; ordering one adds it to the library.")]
    public List<BookData> newTitles = new List<BookData>();
    /// <summary>Titles the library doesn't stock yet: always no copies (some entries carry counts in the asset), so
    /// ordering one adds the title but the only copies are the ones that arrive on the cart.</summary>
    public List<BookData> CreateNewTitlesCopy() => newTitles.Select(b =>
    {
        var c = b.Clone();
        c.NewRelease = true;
        c.GoodCopies = c.WornCopies = c.DamagedCopies = 0;
        return c;
    }).ToList();

    [Tooltip("Rare books the library doesn't hold yet. Only a VIP request offers one, from the rare dealer in the PC Catalogue; once bought it stays in Rare Books.")]
    public List<BookData> rareFinds = new List<BookData>();
    public List<BookData> CreateRareFindsCopy() => rareFinds.Select(b => b.Clone()).ToList();
}
