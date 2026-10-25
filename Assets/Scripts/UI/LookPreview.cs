using UnityEngine;

/// <summary>
/// The librarian on the intro's "Who are you?" page: a copy of the chosen body far below the room, standing still and
/// facing you, filmed a little from above by its own camera (on a layer nothing else uses) into a small texture the
/// page shows. Lit by the scene's ambient light (not one spot's probes) so the face stays evenly lit, and waves when
/// you switch body. Made at runtime; gone once you've picked.
/// </summary>
public class LookPreview : MonoBehaviour
{
    const int Layer = 31;
    static readonly Vector3 Spot = new Vector3(0f, -200f, 0f);

    public RenderTexture Texture { get; private set; }
    GameObject model;
    CharacterAnimator anim;
    float waveUntil;

    public static LookPreview Create()
    {
        var go = new GameObject("LookPreview");
        var p = go.AddComponent<LookPreview>();
        p.Build();
        return p;
    }

    void Build()
    {
        Texture = new RenderTexture(512, 640, 24, RenderTextureFormat.ARGB32) { name = "LookPreview" };
        var camGo = new GameObject("LookPreviewCam");
        camGo.transform.SetParent(transform, false);
        var cam = camGo.AddComponent<Camera>();
        cam.cullingMask = 1 << Layer;
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = new Color(0f, 0f, 0f, 0f); // see-through: the page shows behind
        cam.targetTexture = Texture;
        cam.fieldOfView = 24f;
        cam.nearClipPlane = 0.1f;
        cam.farClipPlane = 20f;
        camGo.transform.position = Spot + new Vector3(0f, 1.45f, 3.8f); // a little above: looking gently down at them
        camGo.transform.LookAt(Spot + new Vector3(0f, 0.66f, 0f));

        var rig = PlayerLooks.Data != null ? PlayerLooks.Data.previewRig : null;
        if (rig == null) return;
        model = Instantiate(rig, Spot, Quaternion.identity, transform);
        foreach (var t in model.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = Layer;
        var blob = model.transform.Find("BlobShadow");
        if (blob != null) blob.gameObject.SetActive(false);
        foreach (var r in model.GetComponentsInChildren<Renderer>(true)) r.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off; // even ambient light
        var animator = model.GetComponent<Animator>();
        if (animator != null) animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
        anim = model.GetComponent<CharacterAnimator>();
    }

    /// <summary>Wears body <paramref name="index"/> and waves (always facing straight at you).</summary>
    public void Show(int index)
    {
        if (model == null) return;
        PlayerLooks.Apply(model, index);
        model.transform.rotation = Quaternion.identity; // the camera is straight in front (+z)
        if (anim != null) anim.SetActivity(CharacterAnimator.Activity.Wave);
        waveUntil = Time.unscaledTime + 1.8f;
    }

    void Update()
    {
        if (model == null) return;
        if (waveUntil > 0f && Time.unscaledTime > waveUntil) { waveUntil = 0f; if (anim != null) anim.SetActivity(CharacterAnimator.Activity.None); }
    }

    void OnDestroy()
    {
        if (Texture != null) { Texture.Release(); Destroy(Texture); }
    }
}
