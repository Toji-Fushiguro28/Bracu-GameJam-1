using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

/// <summary>
/// Generic hover prompt: while the player is in this object's Interactable
/// trigger AND a raycast from the cursor hits its collider, shows the shared
/// UI Toolkit icon floating above it. Attach alongside any Interactable subclass.
/// Icon artwork is bound in the UXML tree, not here.
/// </summary>
[RequireComponent(typeof(Interactable))]
[RequireComponent(typeof(BoxCollider))]
public class HoverPrompt : MonoBehaviour
{
    [Tooltip("Screen-space UI Document showing the prompt layer. Required.")]
    public UIDocument doc;

    [Tooltip("Height above the collider top where the icon floats.")]
    [Range(0f, 2f)]
    public float heightOffset = 0.5f;

    [Tooltip("Icon size in panel pixels.")]
    public float iconSize = 64f;

    [Tooltip("Max raycast distance for hover detection.")]
    public float hoverDistance = 50f;

    Interactable interactable;
    BoxCollider rangeBox;
    BoxCollider hoverMesh;
    Camera cam;
    VisualElement icon;
    bool initialized;
    bool shown;

    void Awake()
    {
        interactable = GetComponent<Interactable>();
        rangeBox = GetComponent<BoxCollider>();
        cam = Camera.main;
        if (cam == null)
            Debug.LogWarning("[Prompt] No MainCamera — hover prompt cannot project.", this);
    }

    void Update()
    {
        if (!initialized && !TryInit())
            return;

        // Range trigger is untouched. The tight hover box exists only
        // while the player is in range; hover is an explicit raycast
        // against it (the player never enters it, so no trigger overlap).
        if (cam == null || !interactable.IsPlayerInRange)
        {
            Hide();
            DestroyHoverMesh();
            return;
        }
        EnsureHoverMesh();
        if (hoverMesh == null || !IsHovering())
        {
            Hide();
            return;
        }

        Bounds b = hoverMesh.bounds;
        Vector3 anchor = new Vector3(b.center.x, b.max.y + heightOffset, b.center.z);
        // Behind-camera check that works for perspective and orthographic.
        if (Vector3.Dot(cam.transform.forward, anchor - cam.transform.position) < 0f)
        {
            Hide();
            return;
        }

        // Explicit viewport (y-up, 0-1) to panel (y-down, pixels) mapping.
        // ScreenToPanel's origin convention hid a vertical flip that put
        // the icon below the anchor instead of above it.
        Vector3 viewport = cam.WorldToViewportPoint(anchor);
        float panelW = doc.rootVisualElement.resolvedStyle.width;
        float panelH = doc.rootVisualElement.resolvedStyle.height;
        if (panelW <= 0f || panelH <= 0f)
        {
            Hide();
            return;
        }
        Vector2 panelPos = new Vector2(viewport.x * panelW, (1f - viewport.y) * panelH);
        icon.style.left = panelPos.x - iconSize * 0.5f;
        icon.style.top = panelPos.y - iconSize * 0.5f;
        Show();
    }

    /// <summary>
    /// Adds a tight hover box around the object's renderer on a child object.
    /// Only raycast against, so the range logic is untouched. Created
    /// on range-enter, destroyed on range-exit; idle objects carry nothing.
    /// </summary>
    void EnsureHoverMesh()
    {
        if (hoverMesh != null)
            return;
        // if (meshFilter == null || meshFilter.sharedMesh == null)
        // {
        //     Debug.LogWarning("[Prompt] No MeshFilter mesh on " + name + " — hover skipped.", this);
        //     return;
        // }
        // Lives on a child so Unity doesn't try (and fail) to auto-size it from
        // a static-batched mesh; size comes from the renderer's world bounds.
        var box = new GameObject("HoverBox");
        box.layer = gameObject.layer;
        box.transform.SetParent(transform, false);
        hoverMesh = box.AddComponent<BoxCollider>();
        hoverMesh.isTrigger = true;

        var rend = GetComponentInChildren<Renderer>();
        if (rend != null)
        {
            Bounds wb = rend.bounds;
            Vector3 min = Vector3.positiveInfinity, max = Vector3.negativeInfinity;
            for (int i = 0; i < 8; i++)
            {
                Vector3 corner = wb.center + Vector3.Scale(wb.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                Vector3 local = box.transform.InverseTransformPoint(corner);
                min = Vector3.Min(min, local);
                max = Vector3.Max(max, local);
            }
            hoverMesh.center = (min + max) * 0.5f;
            hoverMesh.size = (max - min) * 1.5f;
        }
        else if (rangeBox != null)
        {
            Vector3 c = rangeBox.center;
            hoverMesh.center = new Vector3(c.x, c.y, c.z * 0.5f);
            hoverMesh.size = Vector3.one * 1.5f;
        }
    }

    void DestroyHoverMesh()
    {
        if (hoverMesh != null)
        {
            Destroy(hoverMesh.gameObject);
            hoverMesh = null;
        }
    }

    /// <summary>
    /// Raycasts from the cursor against the transient hover mesh only.
    /// Called while the player is in range, so at most one raycast
    /// per frame — and only then.
    /// </summary>
    bool IsHovering()
    {
        if (hoverMesh == null || !hoverMesh.enabled)
            return false;
        if (Pointer.current == null)
            return false;
        Vector2 pointer = Pointer.current.position.ReadValue();
        Ray ray = cam.ScreenPointToRay(pointer);
        int mask = 1 << gameObject.layer;
        if (Physics.Raycast(ray, out RaycastHit hit, hoverDistance, mask, QueryTriggerInteraction.Collide))
            return hit.collider == hoverMesh;
        return false;
    }

    bool TryInit()
    {
        if (doc == null)
        {
            Debug.LogWarning("[Prompt] No UIDocument assigned on " + name + ".", this);
            return false;
        }
        if (doc.rootVisualElement == null || doc.rootVisualElement.panel == null)
            return false; // Document not ready yet; retry next frame.
        icon = doc.rootVisualElement.Q("prompt-icon");
        if (icon == null)
        {
            Debug.LogWarning("[Prompt] No 'prompt-icon' element in " + doc.name + ".", this);
            return false;
        }
        icon.style.width = iconSize;
        icon.style.height = iconSize;
        icon.style.display = DisplayStyle.None;
        initialized = true;
        return true;
    }

    void Show()
    {
        if (!shown)
        {
            icon.style.display = DisplayStyle.Flex;
            shown = true;
        }
    }

    void Hide()
    {
        if (shown)
        {
            icon.style.display = DisplayStyle.None;
            shown = false;
        }
    }
}
