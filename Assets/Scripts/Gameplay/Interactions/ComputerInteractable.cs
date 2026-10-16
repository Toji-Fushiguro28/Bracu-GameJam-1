using UnityEngine;

/// <summary>
/// Phase-1 computer interaction: trigger enter/exit logging only.
/// Attach to SM_Computer_CRT (collider as trigger set up in Inspector).
/// </summary>
public class ComputerInteractable : Interactable
{
    RuntimeOutline outline;

    void Awake()
    {
        outline = GetComponent<RuntimeOutline>();
        if (outline == null)
            Debug.LogWarning("[Interact] No RuntimeOutline on " + name + " — outline will never show.", this);
    }

    protected override void OnPlayerEnter()
    {
        Debug.Log("[Interact] Player entered SM_Computer_CRT trigger");
        if (outline != null)
            outline.Show();
    }

    protected override void OnPlayerExit()
    {
        Debug.Log("[Interact] Player exited SM_Computer_CRT trigger");
        if (outline != null)
            outline.Hide();
    }
}
