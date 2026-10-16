using UnityEngine;

/// <summary>
/// Base trigger setup for all interactables.
/// Attach a subclass (e.g. ComputerInteractable) to the object.
/// Collider isTrigger is configured in the Inspector, not forced here.
/// Player detection uses the "Player" tag (e.g. PF_CH_Main_Librarian).
/// </summary>
public abstract class Interactable : MonoBehaviour
{
    /// <summary>True while the tagged player is inside this trigger.</summary>
    public bool IsPlayerInRange { get; private set; }

    void OnTriggerEnter(Collider other)
    {
        if (!other.CompareTag("Player"))
            return;
        IsPlayerInRange = true;
        OnPlayerEnter();
    }

    void OnTriggerExit(Collider other)
    {
        if (!other.CompareTag("Player"))
            return;
        IsPlayerInRange = false;
        OnPlayerExit();
    }

    /// <summary>
    /// Called when the tagged player enters this trigger.
    /// Override in child class for object-specific behavior.
    /// </summary>
    protected virtual void OnPlayerEnter() { }

    /// <summary>
    /// Called when the tagged player exits this trigger.
    /// Override in child class for object-specific behavior.
    /// </summary>
    protected virtual void OnPlayerExit() { }
}
