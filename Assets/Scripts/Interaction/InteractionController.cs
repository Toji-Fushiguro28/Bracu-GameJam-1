using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// On the player. Finds the <see cref="ClickInteractable"/> under the cursor, highlights it,
/// shows the hover prompt, and interacts on click when the player is close enough.
/// One raycast per frame for the whole game instead of one per object.
/// </summary>
[DisallowMultipleComponent]
public class InteractionController : MonoBehaviour
{
    public float maxRayDistance = 80f;
    [Tooltip("Inverted-hull material drawn around the hovered object (M_Outline).")]
    public Material outlineMaterial;

    InputAction clickAction;
    Camera cam;
    ClickInteractable hovered;
    readonly RaycastHit[] hits = new RaycastHit[16];

    void Awake()
    {
        cam = Camera.main;
        clickAction = InputSystem.actions?.FindAction("Attack");
        if (clickAction == null)
            Debug.LogWarning("[Interact] 'Attack' action not found — clicking disabled.", this);
    }

    void OnEnable() => clickAction?.Enable();

    void OnDisable() => SetHovered(null);

    void Update()
    {
        SetHovered(PlayerLock.IsLocked ? null : Pick());

        bool inReach = hovered != null && hovered.InReach(transform.position);
        if (WorldPrompts.Instance != null)
            WorldPrompts.Instance.SetHover(hovered, inReach);

        if (inReach && clickAction != null && clickAction.WasPressedThisFrame())
            hovered.Interact(gameObject);
    }

    /// <summary>Nearest solid thing under the cursor; returns it only if it's interactable (walls block).</summary>
    ClickInteractable Pick()
    {
        if (Pointer.current == null) return null;
        if (cam == null && (cam = Camera.main) == null) return null;

        Ray ray = cam.ScreenPointToRay(Pointer.current.position.ReadValue());
        int count = Physics.RaycastNonAlloc(ray, hits, maxRayDistance, ~0, QueryTriggerInteraction.Ignore);
        float best = float.MaxValue;
        ClickInteractable found = null;
        for (int i = 0; i < count; i++)
        {
            var h = hits[i];
            if (h.distance >= best || h.collider.transform.IsChildOf(transform)) continue;
            best = h.distance;
            var target = h.collider.GetComponentInParent<ClickInteractable>();
            found = target != null && target.isActiveAndEnabled ? target : null;
        }
        return found;
    }

    void SetHovered(ClickInteractable next)
    {
        if (next == hovered) return;
        HoverOutline.Set(hovered, false, outlineMaterial);
        hovered = next;
        HoverOutline.Set(hovered, true, outlineMaterial);
    }
}
