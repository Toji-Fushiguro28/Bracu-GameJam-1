using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Something left lying around: a teacup, or a stray book (a real copy that has to go back
/// on its shelf). Click to tidy it for a few coins.
/// </summary>
public class MessSpot : ClickInteractable
{
    public static readonly List<MessSpot> All = new List<MessSpot>();

    public int reward = 2;
    [System.NonSerialized] public BookData book;   // null = teacup
    [System.NonSerialized] public BookCondition condition;

    protected override string DefaultVerb => book != null ? "Pick up book" : "Tidy cup";

    void OnEnable() => All.Add(this);
    void OnDisable() => All.Remove(this);

    public override void Interact(GameObject player)
    {
        if (book != null)
        {
            var carry = player.GetComponent<PlayerCarry>();
            if (carry == null || !carry.IsEmpty)
            {
                WorldPrompts.Instance?.ShowBubble(this, "Your hands are full.", 2f);
                return;
            }
            carry.PickUp(book, condition);
            book = null;
        }
        Clean(true);
    }

    /// <summary>Removes the mess. Stray books still holding their copy go back on the shelf.</summary>
    public void Clean(bool byPlayer)
    {
        var gm = GameManager.Instance;
        if (book != null) { book.AddCopy(condition); book = null; gm?.NotifyBooksChanged(); }
        if (gm != null)
        {
            gm.Today.cleaned++;
            if (byPlayer) { GameAudio.Play(a => a.tidy); gm.AddCoins(reward); gm.Today.income += reward; }
        }
        Destroy(gameObject);
    }

    /// <summary>Drops a cup (book == null) or a stray copy at a spot.</summary>
    public static MessSpot Spawn(GameObject model, Material material, Vector3 position, BookData book = null, BookCondition condition = BookCondition.Good)
    {
        if (model == null) return null;
        var go = Instantiate(model, position, Quaternion.Euler(0f, Random.Range(0f, 360f), 0f));
        go.name = book != null ? "Mess_Book" : "Mess_Cup";
        if (material != null) foreach (var r in go.GetComponentsInChildren<Renderer>()) r.sharedMaterial = material;
        if (go.GetComponentInChildren<Collider>() == null)
        {
            var box = go.AddComponent<BoxCollider>();
            box.size = Vector3.one * 0.35f;
            box.center = Vector3.up * 0.1f;
        }
        var m = go.AddComponent<MessSpot>();
        m.book = book;
        m.condition = condition;
        return m;
    }
}
