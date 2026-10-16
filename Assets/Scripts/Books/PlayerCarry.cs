using System;
using UnityEngine;

/// <summary>
/// On the player: holds one book. Shows a book prop between the hands and plays the carry pose.
/// </summary>
public class PlayerCarry : MonoBehaviour
{
    [Tooltip("Prop shown while carrying (e.g. SM_Book_Thick).")]
    public GameObject bookPropPrefab;
    public Vector3 propOffset = new Vector3(0f, 0.02f, 0.06f);

    // Runtime only. Never serialized: Unity would otherwise rebuild "nothing" as an empty
    // BookData after a script reload, and the player would hold a nameless book.
    [NonSerialized] BookData carried;
    [NonSerialized] BookCondition carriedCondition;

    public BookData Carried => IsEmpty ? null : carried;
    /// <summary>Condition of the one copy in your hands.</summary>
    public BookCondition CarriedCondition => carriedCondition;
    public bool IsEmpty => carried == null || string.IsNullOrEmpty(carried.Title);
    public event Action<BookData> Changed;

    CharacterAnimator anim;
    Animator animator;
    GameObject prop;
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
        if (book == null || !IsEmpty) return false;
        carried = book;
        carriedCondition = condition;
        if (anim) anim.SetCarrying(true);
        if (prop == null && bookPropPrefab != null)
        {
            prop = Instantiate(bookPropPrefab, transform);
            foreach (var c in prop.GetComponentsInChildren<Collider>()) Destroy(c);
        }
        if (prop) prop.SetActive(true);
        Changed?.Invoke(Carried);
        return true;
    }

    public BookData Drop()
    {
        var book = Carried;
        carried = null;
        if (anim) anim.SetCarrying(false);
        if (prop) prop.SetActive(false);
        Changed?.Invoke(null);
        return book;
    }

    void LateUpdate()
    {
        if (prop == null || !prop.activeSelf) return;
        Vector3 hold = leftHand && rightHand
            ? (leftHand.position + rightHand.position) * 0.5f
            : transform.position + Vector3.up * 0.8f + transform.forward * 0.3f;
        prop.transform.SetPositionAndRotation(hold + transform.rotation * propOffset, transform.rotation);
    }
}
