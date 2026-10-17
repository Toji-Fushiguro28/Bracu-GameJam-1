using UnityEngine;

/// <summary>
/// A soft pulsing gold outline around the return bin while it has books in it,
/// so returns that need shelving (or repairing) are easy to spot.
/// </summary>
public class ReturnBinGlow : MonoBehaviour
{
    [Tooltip("Outline-style material (inverted hull) for the glow.")]
    public Material glowMaterial;
    public Color color = new Color(1f, 0.78f, 0.3f, 1f);
    public Vector2 width = new Vector2(0.035f, 0.075f);
    public float pulseSeconds = 1.4f;

    static readonly int WidthId = Shader.PropertyToID("_OutlineWidth");
    static readonly int ColorId = Shader.PropertyToID("_OutlineColor");
    ReturnBin bin;
    RepairDesk desk;
    DeliveryCart cart;
    PCOverlayTrigger computer;
    Renderer glow;
    MaterialPropertyBlock block;

    void Start()
    {
        bin = GetComponent<ReturnBin>();
        desk = GetComponent<RepairDesk>(); // also works on the repair desk: glows while repaired books wait
        cart = GetComponent<DeliveryCart>(); // ...and the delivery cart while deliveries wait
        computer = GetComponent<PCOverlayTrigger>(); // ...and the computer while an upgrade is affordable
        var mf = GetComponent<MeshFilter>();
        if (mf == null || mf.sharedMesh == null || glowMaterial == null) { enabled = false; return; }
        // A copy of the bin's mesh drawn with the outline shader, slightly larger.
        var go = new GameObject("ReturnBin_Glow");
        go.transform.SetParent(transform, false);
        go.AddComponent<MeshFilter>().sharedMesh = mf.sharedMesh;
        var r = go.AddComponent<MeshRenderer>();
        var mats = new Material[Mathf.Max(1, mf.sharedMesh.subMeshCount)];
        for (int i = 0; i < mats.Length; i++) mats[i] = glowMaterial;
        r.sharedMaterials = mats;
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        r.receiveShadows = false;
        glow = r;
        block = new MaterialPropertyBlock();
    }

    void Update()
    {
        var gm = GameManager.Instance;
        bool on = bin != null ? bin.Count > 0
                : desk != null ? desk.Done.Count > 0
                : cart != null ? cart.Count > 0
                : computer != null && gm != null && gm.UpgradeReady && gm.upgradesSeenDay != gm.day;
        if (glow.enabled != on) glow.enabled = on;
        if (!on) return;
        float t = 0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * Mathf.PI * 2f / Mathf.Max(0.1f, pulseSeconds));
        glow.GetPropertyBlock(block);
        block.SetFloat(WidthId, Mathf.Lerp(width.x, width.y, t));
        var c = color; c.a = Mathf.Lerp(0.55f, 1f, t);
        block.SetColor(ColorId, c * Mathf.Lerp(1f, 1.6f, t));
        glow.SetPropertyBlock(block);
    }
}
