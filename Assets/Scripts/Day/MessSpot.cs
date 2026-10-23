using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// A teacup or open book placed on a table. It stays hidden while the library is tidy and
/// shows up when a visitor leaves it behind. Click to tidy it for a few coins.
/// An open book left at closing holds a real copy that has to go back on its shelf.
/// </summary>
public class MessSpot : ClickInteractable
{
    public enum Kind { Cup, Book }

    /// <summary>Messes waiting to be tidied.</summary>
    public static readonly List<MessSpot> All = new List<MessSpot>();
    /// <summary>Every placed spot, tidy or not.</summary>
    public static readonly List<MessSpot> Placed = new List<MessSpot>();

    public Kind kind;
    public int reward = 2;
    [System.NonSerialized] public BookData book;
    [System.NonSerialized] public BookCondition condition;

    public bool IsDirty { get; private set; }
    Renderer[] renderers;
    Collider[] colliders;

    protected override string DefaultVerb => kind == Kind.Cup ? "Tidy cup" : book != null ? "Pick up book" : "Close book";
    public override bool CanInteract => IsDirty;

    protected override void Awake()
    {
        base.Awake();
        renderers = GetComponentsInChildren<Renderer>();
        colliders = GetComponentsInChildren<Collider>();
        Placed.Add(this);
        SetDirty(false); // the library opens tidy
    }

    void OnDestroy()
    {
        Placed.Remove(this);
        All.Remove(this);
    }

    /// <summary>Someone left this here. A book can carry a real copy taken off the shelves.</summary>
    public void MakeMess(BookData copyOf = null, BookCondition copyCondition = BookCondition.Good)
    {
        Tutorial.Show("mess", "Click cups and books to tidy. A tidy library earns reputation.");
        book = copyOf;
        condition = copyCondition;
        SetDirty(true);
    }

    public override void Interact(GameObject player)
    {
        if (!IsDirty) return;
        if (book != null)
        {
            var carry = player.GetComponent<PlayerCarry>();
            if (carry == null || carry.IsFull)
            {
                WorldPrompts.Instance?.ShowBubble(this, "Your hands are full.", 2f);
                return;
            }
            carry.PickUp(book, condition);
            WorldPrompts.Instance?.ShowBubble(this, $"Picked up \"{book.Title}\".", 2.5f);
            book = null;
        }
        Clean(true);
    }

    /// <summary>Staff tidied it. Returns the reward: they hand it over with their end-of-day report.</summary>
    public int CleanByStaff()
    {
        if (!IsDirty) return 0;
        Clean(false);
        return kind == Kind.Book ? 6 : 5; // the cleaner earns well for each cup and book cleared
    }

    /// <summary>Tidies it away. A book still holding its copy puts it back on the shelf.</summary>
    public void Clean(bool byPlayer)
    {
        if (!IsDirty) return;
        var gm = GameManager.Instance;
        if (book != null) { book.AddCopy(condition); book = null; gm?.NotifyBooksChanged(); }
        if (gm != null)
        {
            gm.Today.cleaned++;
            if (byPlayer) { GameAudio.Play(a => a.tidy); gm.AddCoins(reward); gm.Today.Earn(GameManager.DayStats.Source.Other, reward); RequestLog.Money("You", "Tidied up the library", "Tidy reward", reward); }
        }
        SetDirty(false);
    }

    void SetDirty(bool dirty)
    {
        IsDirty = dirty;
        foreach (var r in renderers) if (r != null) r.enabled = dirty;
        foreach (var c in colliders) if (c != null) c.enabled = dirty;
        if (dirty) { if (!All.Contains(this)) All.Add(this); }
        else All.Remove(this);
    }

    /// <summary>The nearest tidy spot of a kind within range, or null.</summary>
    public static MessSpot NearestTidy(Kind kind, Vector3 near, float range)
    {
        MessSpot best = null;
        float bestDist = range * range;
        foreach (var m in Placed)
        {
            if (m.IsDirty || m.kind != kind) continue;
            float d = (m.transform.position - near).sqrMagnitude;
            if (d < bestDist) { bestDist = d; best = m; }
        }
        return best;
    }
}
