using UnityEngine;

/// <summary>
/// Lets the shared click system open the front-desk PC (PCOverlayTrigger) like any other interactable.
/// </summary>
[RequireComponent(typeof(PCOverlayTrigger))]
public class ComputerClick : ClickInteractable
{
    PCOverlayTrigger pc;

    protected override string DefaultVerb => "Use";

    protected override void Awake()
    {
        base.Awake();
        pc = GetComponent<PCOverlayTrigger>();
    }

    public override void Interact(GameObject player)
    {
        if (pc != null) pc.Open();
    }
}
