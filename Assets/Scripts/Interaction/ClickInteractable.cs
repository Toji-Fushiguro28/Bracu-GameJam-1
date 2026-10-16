using UnityEngine;

/// <summary>
/// Base for everything the player clicks to use (NPCs, inspectable props, shelves...).
/// Reach is a distance check from the object's bounds, so no trigger setup is needed.
/// Driven by <see cref="InteractionController"/> on the player.
/// </summary>
public abstract class ClickInteractable : Interactable
{
    [Tooltip("How close the player must be, in metres from the object's bounds.")]
    public float range = 2.2f;

    [Tooltip("Word shown in the hover prompt. Empty = the type's default.")]
    public string verb;

    Collider col;
    Renderer rend;

    protected virtual string DefaultVerb => "Inspect";
    public string Verb => string.IsNullOrEmpty(verb) ? DefaultVerb : verb;

    protected virtual void Awake()
    {
        col = GetComponentInChildren<Collider>();
        rend = GetComponentInChildren<Renderer>();
    }

    public bool InReach(Vector3 playerPos)
    {
        Vector3 p = col ? col.bounds.ClosestPoint(playerPos) : transform.position;
        p.y = playerPos.y;
        return (p - playerPos).sqrMagnitude <= range * range;
    }

    /// <summary>World point just above the object, used to anchor prompts and bubbles.</summary>
    public Vector3 TopAnchor
    {
        get
        {
            if (rend == null) return transform.position + Vector3.up * 2f;
            Bounds b = rend.bounds;
            return new Vector3(b.center.x, b.max.y, b.center.z);
        }
    }

    public abstract void Interact(GameObject player);
}
