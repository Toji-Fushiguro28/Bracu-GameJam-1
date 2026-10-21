using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// On the player: a little stack of books (one at first, more with the Book bag upgrade).
/// Shows a book prop between the hands and plays the carry pose. "Carried" is the top book.
/// </summary>
public class PlayerCarry : MonoBehaviour
{
    [Tooltip("Prop shown while carrying (e.g. SM_Book_Thick).")]
    public GameObject bookPropPrefab;
    public Vector3 propOffset = new Vector3(0f, 0.02f, 0.06f);
    [Tooltip("Gap between books held side by side (the book is ~6 cm thick).")]
    public float sideStep = 0.07f;

    // Runtime only. Never serialized: Unity would otherwise rebuild "nothing" as empty
    // BookData entries after a script reload, and the player would hold nameless books.
    [NonSerialized] readonly List<(BookData book, BookCondition condition)> items = new List<(BookData, BookCondition)>();

    public IReadOnlyList<(BookData book, BookCondition condition)> Items => items;
    public int Count => items.Count;
    public int Capacity => GameManager.Instance != null ? GameManager.Instance.BagCapacity : 1;
    public bool IsEmpty => items.Count == 0;
    public bool IsFull => items.Count >= Capacity;
    /// <summary>The top book (the one you'd put down first).</summary>
    public BookData Carried => IsEmpty ? null : items[items.Count - 1].book;
    public BookCondition CarriedCondition => IsEmpty ? BookCondition.Good : items[items.Count - 1].condition;
    public event Action<BookData> Changed;

    CharacterAnimator anim;
    Animator animator;
    readonly List<GameObject> props = new List<GameObject>();
    Transform leftHand, rightHand;

    void Awake()
    {
        anim = GetComponent<CharacterAnimator>();
        animator = GetComponent<Animator>();
        if (animator != null && animator.isHuman)
        {
            leftHand = animator.GetBoneTransform(HumanBodyBones.LeftHand);
            rightHand = animator.GetBoneTransform(HumanBodyBones.RightHand);
        }
    }

    public bool PickUp(BookData book, BookCondition condition = BookCondition.Good)
    {
        if (book == null || string.IsNullOrEmpty(book.Title) || IsFull) return false;
        items.Add((book, condition));
        Refresh();
        return true;
    }

    /// <summary>Empty hands (after the tutorial).</summary>
    public void Clear() { items.Clear(); Refresh(); }

    /// <summary>Takes the top book out of your hands.</summary>
    public BookData Drop() => IsEmpty ? null : TakeAt(items.Count - 1).book;

    public (BookData book, BookCondition condition) TakeAt(int index)
    {
        var item = items[index];
        items.RemoveAt(index);
        Refresh();
        return item;
    }

    /// <summary>Index of the first book matching, preferring the best condition; -1 if none.</summary>
    public int Find(Func<BookData, BookCondition, bool> match)
    {
        int best = -1;
        for (int i = 0; i < items.Count; i++)
            if (match(items[i].book, items[i].condition) && (best < 0 || items[i].condition < items[best].condition)) best = i;
        return best;
    }

    void Refresh()
    {
        if (anim) anim.SetCarrying(!IsEmpty);
        while (props.Count < items.Count && bookPropPrefab != null)
        {
            var p = Instantiate(bookPropPrefab, transform);
            foreach (var c in p.GetComponentsInChildren<Collider>()) Destroy(c);
            props.Add(p);
        }
        for (int i = 0; i < props.Count; i++) props[i].SetActive(i < items.Count);
        Changed?.Invoke(Carried);
    }

    void LateUpdate()
    {
        if (IsEmpty) return;
        Vector3 hold = leftHand && rightHand
            ? (leftHand.position + rightHand.position) * 0.5f
            : transform.position + Vector3.up * 0.8f + transform.forward * 0.3f;
        // Side by side between the hands, centred: one book sits exactly where it always did.
        int n = Mathf.Min(props.Count, items.Count);
        for (int i = 0; i < n; i++)
        {
            float side = (i - (n - 1) * 0.5f) * sideStep;
            props[i].transform.SetPositionAndRotation(hold + transform.rotation * (propOffset + Vector3.right * side), transform.rotation);
        }
    }
}
