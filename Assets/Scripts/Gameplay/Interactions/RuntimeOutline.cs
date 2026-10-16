using UnityEngine;

/// <summary>
/// WebGL-minimal outline: duplicates ONE body MeshRenderer with the same
/// sharedMesh (zero extra vertex memory) rendered by the inverted-hull
/// shader (Game/OutlineHull), which draws only the outside rim.
/// No per-frame cost — built once, toggled via Show/Hide.
/// </summary>
public class RuntimeOutline : MonoBehaviour
{
    [Tooltip("Main body renderer to outline. Leave empty to auto-pick the largest bounds renderer (keys excluded by size).")]
    public MeshRenderer targetBody;

    [Tooltip("Subtree to search for renderers. Leave empty to search this object. Set to the visual root if these scripts sit on a separate trigger object.")]
    public Transform visualsRoot;

    [Tooltip("Rim width in world units. Mirrors the shader's _OutlineWidth.")]
    [Range(0.005f, 0.1f)]
    public float outlineWidth = 0.02f;

    public Color outlineColor = Color.white;

    GameObject outlineObj;
    Material outlineMat;
    bool buildAttempted;

    void Awake()
    {
        BuildOutline();
    }

    void BuildOutline()
    {
        buildAttempted = true;
        Transform root = visualsRoot != null ? visualsRoot : transform;

        if (targetBody == null)
            targetBody = FindLargestRenderer(root);

        if (targetBody == null)
        {
            Debug.LogWarning("[Outline] No MeshRenderer found under " + root.name + " — outline skipped. Assign targetBody or visualsRoot in Inspector.", this);
            return;
        }

        var srcFilter = targetBody.GetComponent<MeshFilter>();
        if (srcFilter == null || srcFilter.sharedMesh == null)
        {
            // Renderer without a MeshFilter on the same node (multi-part FBX layout):
            // fall back to the first MeshFilter under the picked renderer.
            srcFilter = targetBody.GetComponentInChildren<MeshFilter>();
            if (srcFilter == null || srcFilter.sharedMesh == null)
            {
                Debug.LogWarning("[Outline] " + targetBody.name + " has no MeshFilter with a mesh — outline skipped.", this);
                return;
            }
        }

        outlineMat = CreateHullMaterial();
        if (outlineMat == null)
        {
            Debug.LogWarning("[Outline] Outline shader not found (Game/OutlineHull) and no Unlit fallback — outline skipped.", this);
            return;
        }

        outlineObj = new GameObject("Outline_" + targetBody.name);
        outlineObj.transform.SetParent(targetBody.transform, false);
        outlineObj.transform.localPosition = Vector3.zero;
        outlineObj.transform.localRotation = Quaternion.identity;
        outlineObj.transform.localScale = Vector3.one;
        outlineObj.layer = targetBody.gameObject.layer;

        var dstFilter = outlineObj.AddComponent<MeshFilter>();
        dstFilter.sharedMesh = srcFilter.sharedMesh;

        var dstRenderer = outlineObj.AddComponent<MeshRenderer>();
        dstRenderer.sharedMaterial = outlineMat;
        dstRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        dstRenderer.receiveShadows = false;
        dstRenderer.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
        dstRenderer.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;

        outlineObj.SetActive(false);
        Debug.Log("[Outline] Built outline for " + targetBody.name + " bounds=" + targetBody.bounds.size, this);
    }

    MeshRenderer FindLargestRenderer(Transform root)
    {
        var renderers = root.GetComponentsInChildren<MeshRenderer>();
        MeshRenderer best = null;
        float bestVolume = 0f;
        foreach (var r in renderers)
        {
            if (!r.enabled)
                continue;
            var s = r.bounds.size;
            float v = s.x * s.y * s.z;
            if (v > bestVolume)
            {
                bestVolume = v;
                best = r;
            }
        }
        return best;
    }

    Material CreateHullMaterial()
    {
        Shader s = Shader.Find("Game/OutlineHull");
        string used = "Game/OutlineHull";
        if (s == null)
        {
            // Fallback to built-in Unlit (flat white shell; relies on draw order).
            s = Shader.Find("Universal Render Pipeline/Unlit");
            used = "Universal Render Pipeline/Unlit";
        }
        if (s == null)
        {
            s = Shader.Find("Unlit/Color");
            used = "Unlit/Color";
        }
        if (s == null)
            return null;

        var mat = new Material(s);
        if (mat.HasProperty("_OutlineColor"))
            mat.SetColor("_OutlineColor", outlineColor);
        else if (mat.HasProperty("_BaseColor"))
            mat.SetColor("_BaseColor", outlineColor);
        else if (mat.HasProperty("_Color"))
            mat.SetColor("_Color", outlineColor);
        if (mat.HasProperty("_OutlineWidth"))
            mat.SetFloat("_OutlineWidth", outlineWidth);
        Debug.Log("[Outline] Using shader: " + used, this);
        return mat;
    }

    /// <summary>Push slider changes to the material in Edit mode / at runtime.</summary>
    void OnValidate()
    {
        if (outlineMat != null && outlineMat.HasProperty("_OutlineWidth"))
            outlineMat.SetFloat("_OutlineWidth", outlineWidth);
        if (outlineMat != null && outlineMat.HasProperty("_OutlineColor"))
            outlineMat.SetColor("_OutlineColor", outlineColor);
    }

    public void Show()
    {
        if (outlineObj == null && !buildAttempted)
            BuildOutline();
        if (outlineObj != null)
        {
            outlineObj.SetActive(true);
            Debug.Log("[Outline] Show " + targetBody.name, this);
        }
    }

    public void Hide()
    {
        if (outlineObj != null)
        {
            outlineObj.SetActive(false);
            Debug.Log("[Outline] Hide " + targetBody.name, this);
        }
    }

    void OnDestroy()
    {
        if (outlineMat != null)
            Destroy(outlineMat);
    }
}
