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
    [Tooltip("Extra slack around the cursor when nothing is directly under it.")]
    public float pickRadius = 0.35f;
    [Tooltip("Inverted-hull material drawn around the hovered object (M_Outline).")]
    public Material outlineMaterial;

    InputAction clickAction;
    Camera cam;
    ClickInteractable hovered;
    /// <summary>The mouse is over something in the library you can click (the cursor changes).</summary>
    public static bool HoveringSomething { get; private set; }

    readonly RaycastHit[] hits = new RaycastHit[32];

    void Awake()
    {
        cam = Camera.main;
        clickAction = InputSystem.actions?.FindAction("Attack");
        if (clickAction == null)
            Debug.LogWarning("[Interact] 'Attack' action not found — clicking disabled.", this);
    }

    void OnEnable() => clickAction?.Enable();

    void OnDisable() => SetHovered(null); // also clears HoveringSomething

    void Update()
    {
        var picked = PlayerLock.IsLocked || NotebookHUD.PointerOver || HUD.PointerOverFeed || GhostLetterUI.PointerOver ? null : Pick();
        if (picked != null && !Tutorial.Allows(picked)) picked = null; // the tutorial: only the next thing responds
        SetHovered(picked);

        bool inReach = hovered != null && hovered.InReach(transform.position);
        if (WorldPrompts.Instance != null)
            WorldPrompts.Instance.SetHover(hovered, inReach);

        if (inReach && clickAction != null && clickAction.WasPressedThisFrame() && Time.frameCount != PlayerLock.UnlockedFrame)
            hovered.Interact(gameObject);
    }

    /// <summary>
    /// Forgiving pick: a fat ray under the cursor, nearest interactable wins.
    /// Non-interactable things (props, floor, walls) never block it, so you don't have to aim precisely.
    /// </summary>
    ClickInteractable Pick()
    {
        if (Pointer.current == null) return null;
        if (cam == null && (cam = Camera.main) == null) return null;

        Ray ray = cam.ScreenPointToRay(Pointer.current.position.ReadValue());
        var found = Nearest(Physics.RaycastNonAlloc(ray, hits, maxRayDistance, ~0, QueryTriggerInteraction.Ignore));
        if (found == null)
            found = Nearest(Physics.SphereCastNonAlloc(ray, pickRadius, hits, maxRayDistance, ~0, QueryTriggerInteraction.Ignore));
        return found;
    }

    ClickInteractable Nearest(int count)
    {
        float best = float.MaxValue;
        ClickInteractable found = null;
        for (int i = 0; i < count; i++)
        {
            var h = hits[i];
            if (h.distance >= best || h.collider.transform.IsChildOf(transform)) continue;
            var target = h.collider.GetComponentInParent<ClickInteractable>();
            if (target == null || !target.isActiveAndEnabled || !target.CanInteract) continue;
            best = h.distance;
            found = target;
        }
        return found;
    }

    void SetHovered(ClickInteractable next)
    {
        if (next == hovered) return;
        if (hovered != null) HoverOutline.Set(hovered, false, outlineMaterial);
        hovered = next;
        if (hovered != null) HoverOutline.Set(hovered, true, outlineMaterial);
        HoveringSomething = hovered != null;
    }
}
