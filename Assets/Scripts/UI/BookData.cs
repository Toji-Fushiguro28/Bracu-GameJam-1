using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public enum BookTier { Normal, Epic, Legendary }
public enum BookCondition { Good, Worn, Damaged }
public enum BookFormat { Book, Newspaper, Magazine, Journal, ResearchPaper }

/// <summary>
/// One library title. Authored in the BookDatabase asset; at runtime the GameManager
/// works on copies so stock changes never touch the asset.
/// Copies are tracked per condition, so one damaged copy doesn't spoil the others.
/// Field names match the PC UI (Title, Author, Subject, Tag, Quantity, ShelfLocation...).
/// </summary>
[Serializable]
public class BookData
{
    public string Title;
    [Tooltip("Book, newspaper, magazine, journal or research paper.")]
    public BookFormat Format = BookFormat.Book;
    public string Author;
    [Tooltip("Science, Philosophy, History or Math.")]
    public string Subject;
    [Tooltip("Comma-separated tags, e.g. \"Best Seller, English Literature\".")]
    public string Tag;
    [TextArea(2, 4)] public string Abstract;
    [Tooltip("Id of the shelf this book lives on (matches a ShelfInteractable).")]
    public string ShelfLocation;
    public BookTier Tier = BookTier.Normal;
    public int BaseValue = 20;
    [Tooltip("Rare books can only be read in the library, never sold.")]
    public bool Sellable = true;

    [Header("Copies on the shelf, by condition")]
    public int GoodCopies;
    public int WornCopies;
    public int DamagedCopies;

    /// <summary>Total copies on the shelf.</summary>
    public int Quantity => GoodCopies + WornCopies + DamagedCopies;
    public string Rarity => Tier.ToString();
    /// <summary>"Research paper" style label for UI.</summary>
    public string FormatName => Format == BookFormat.ResearchPaper ? "Research paper" : Format.ToString();
    /// <summary>True if any copy on the shelf is damaged.</summary>
    public bool Damaged => DamagedCopies > 0;

    public int Count(BookCondition c) =>
        c == BookCondition.Good ? GoodCopies : c == BookCondition.Worn ? WornCopies : DamagedCopies;

    /// <summary>Best copy on the shelf (Good, then Worn, then Damaged), or null if none.</summary>
    public BookCondition? BestCopy =>
        GoodCopies > 0 ? BookCondition.Good : WornCopies > 0 ? BookCondition.Worn : DamagedCopies > 0 ? BookCondition.Damaged : (BookCondition?)null;

    public bool TakeCopy(BookCondition c)
    {
        if (Count(c) <= 0) return false;
        Change(c, -1);
        return true;
    }

    public void AddCopy(BookCondition c) => Change(c, 1);

    void Change(BookCondition c, int delta)
    {
        if (c == BookCondition.Good) GoodCopies += delta;
        else if (c == BookCondition.Worn) WornCopies += delta;
        else DamagedCopies += delta;
    }

    /// <summary>"2 good, 1 worn" style summary (empty when every copy is good).</summary>
    public string ConditionSummary
    {
        get
        {
            if (WornCopies == 0 && DamagedCopies == 0) return GoodCopies > 0 ? "all good" : "";
            var parts = new List<string>();
            if (GoodCopies > 0) parts.Add(GoodCopies + " good");
            if (WornCopies > 0) parts.Add(WornCopies + " worn");
            if (DamagedCopies > 0) parts.Add(DamagedCopies + " damaged");
            return string.Join(", ", parts);
        }
    }

    public bool HasTag(string tag) =>
        !string.IsNullOrEmpty(Tag) && Tag.Split(',').Any(t => string.Equals(t.Trim(), tag, StringComparison.OrdinalIgnoreCase));

    public BookData Clone() => JsonUtility.FromJson<BookData>(JsonUtility.ToJson(this));

    /// <summary>
    /// The live catalogue: the GameManager's copy while a game runs, otherwise fresh copies
    /// of the BookDatabase asset (e.g. scenes without a GameManager).
    /// </summary>
    public static List<BookData> Samples()
    {
        if (GameManager.Instance != null) return GameManager.Instance.Books;
        var db = Resources.Load<BookDatabase>(BookDatabase.ResourceName);
        return db != null ? db.CreateRuntimeCopy() : new List<BookData>();
    }
}
