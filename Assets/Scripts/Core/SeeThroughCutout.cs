using UnityEngine;

/// <summary>
/// On the camera. When a wall is between the camera and the player, fades in a soft circular
/// see-through hole around the player (drawn by the "Library/ToonBaked SeeThrough" wall shader).
/// </summary>
[RequireComponent(typeof(Camera))]
public class SeeThroughCutout : MonoBehaviour
{
    [Tooltip("Hole size, as a fraction of screen height.")]
    public float radius = 0.16f;
    public float fadeSpeed = 4f;
    [Tooltip("Point on the player to keep visible.")]
    public float targetHeight = 0.9f;
    [Tooltip("Only these block the view (walls, columns, the mezzanine floor).")]
    public string[] wallNames = { "SM_Wall", "SM_Column", "SM_Floor_Upper" };

    static readonly int CenterId = Shader.PropertyToID("_SeeThrough_Center");
    static readonly int RadiusId = Shader.PropertyToID("_SeeThrough_Radius");
    static readonly int StrengthId = Shader.PropertyToID("_SeeThrough_Strength");
    static readonly int DepthId = Shader.PropertyToID("_SeeThrough_Depth");

    Camera cam;
    Transform player;
    float strength;
    readonly RaycastHit[] hits = new RaycastHit[16];

    void Awake() => cam = GetComponent<Camera>();

    void OnDisable() => Shader.SetGlobalFloat(StrengthId, 0f);

    void LateUpdate()
    {
        if (player == null)
        {
            var p = GameObject.FindWithTag("Player");
            if (p == null) return;
            player = p.transform;
        }

        Vector3 target = player.position + Vector3.up * targetHeight;
        bool blocked = IsBlocked(target);
        strength = Mathf.MoveTowards(strength, blocked ? 1f : 0f, fadeSpeed * Time.deltaTime);

        Vector3 vp = cam.WorldToViewportPoint(target);
        Shader.SetGlobalVector(CenterId, new Vector4(vp.x, vp.y, 0f, 0f));
        Shader.SetGlobalFloat(RadiusId, radius);
        Shader.SetGlobalFloat(StrengthId, strength);
        Shader.SetGlobalFloat(DepthId, -cam.worldToCameraMatrix.MultiplyPoint(target).z);
    }

    bool IsBlocked(Vector3 target)
    {
        Vector3 toCam = cam.transform.position - target;
        int n = Physics.SphereCastNonAlloc(target, 0.25f, toCam.normalized, hits, toCam.magnitude, ~0, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < n; i++)
            for (var t = hits[i].collider.transform; t != null; t = t.parent)
                foreach (var w in wallNames)
                    if (t.name.StartsWith(w)) return true;
        return false;
    }
}
