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
    public List<BookData> CreateNewTitlesCopy() => newTitles.Select(b => { var c = b.Clone(); c.NewRelease = true; return c; }).ToList();
}
