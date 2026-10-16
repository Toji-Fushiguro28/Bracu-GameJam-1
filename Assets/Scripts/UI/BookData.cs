using System.Collections.Generic;

/// <summary>
/// One library book. Backed by sample data for now — real database later.
/// </summary>
public class BookData
{
    public string Title;
    public string Subject;
    public string Author;
    public string Abstract;
    public int Quantity;
    public bool Damaged;
    public string Tag;
    public string ShelfLocation;
    public string Rarity;

    /// <summary>Placeholder catalogue until the real database lands.</summary>
    public static List<BookData> Samples()
    {
        return new List<BookData>
        {
            new BookData
            {
                Title = "The Clockwork Atlas",
                Subject = "Science",
                Author = "Prof. Edmund Hale",
                Abstract = "A survey of mechanical navigation instruments and the maps they drew.",
                Quantity = 3,
                Damaged = false,
                Tag = "Reference",
                ShelfLocation = "SCI-A-04",
                Rarity = "Common",
            },
            new BookData
            {
                Title = "Meditations on Dust",
                Subject = "Philosophy",
                Author = "Mara Voss",
                Abstract = "Essays on impermanence, archives, and what libraries choose to forget.",
                Quantity = 1,
                Damaged = true,
                Tag = "Restricted",
                ShelfLocation = "PHI-C-11",
                Rarity = "Rare",
            },
            new BookData
            {
                Title = "The Salt Road Chronicles",
                Subject = "History",
                Author = "Amara Diallo",
                Abstract = "Three centuries of trade, caravans, and the cities the salt road built.",
                Quantity = 2,
                Damaged = false,
                Tag = "Lending",
                ShelfLocation = "HIS-A-09",
                Rarity = "Uncommon",
            },
        };
    }
}
