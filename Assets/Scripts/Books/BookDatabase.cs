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
}
