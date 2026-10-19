using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Fakes day and night over the one lightmap bake: the daytime grade (VP_Cozy) stays untouched,
/// and a night Volume (VP_Night) fades in over the evening. Window light shafts fade out as it gets dark.
/// </summary>
[RequireComponent(typeof(Volume))]
public class DayNightCycle : MonoBehaviour
{
    [Tooltip("Hour the evening starts to set in.")]
    public float duskStart = 16f;
    [Tooltip("Night strength over the evening (0 = day grade, 1 = full night). X = 0 at duskStart, 1 at closing.")]
    public AnimationCurve nightCurve = new AnimationCurve(
        new Keyframe(0f, 0f, 0f, 0.4f), new Keyframe(0.7f, 0.45f), new Keyframe(1f, 1f, 2f, 0f));
    [Tooltip("Renderers that fade out at night (window light shafts).")]
    public Renderer[] lightShafts;
    [Range(0f, 1f)] public float shaftsAtNight = 0.08f;
    [Tooltip("Seconds to blend when the day jumps (e.g. a new morning).")]
    public float blendSpeed = 0.5f;

    static readonly int BaseColor = Shader.PropertyToID("_BaseColor");
    Volume volume;
    Color[] shaftColors;
    MaterialPropertyBlock block;
    float current = -1f;

    void Awake()
    {
        volume = GetComponent<Volume>();
        block = new MaterialPropertyBlock();
        if (lightShafts == null || lightShafts.Length == 0)
        {
            var found = new System.Collections.Generic.List<Renderer>();
            foreach (var r in FindObjectsByType<Renderer>())
                if (r.name.StartsWith("SM_LightShaft")) found.Add(r);
            lightShafts = found.ToArray();
        }
        shaftColors = new Color[lightShafts.Length];
        for (int i = 0; i < lightShafts.Length; i++)
            shaftColors[i] = lightShafts[i] && lightShafts[i].sharedMaterial && lightShafts[i].sharedMaterial.HasProperty(BaseColor)
                ? lightShafts[i].sharedMaterial.GetColor(BaseColor) : Color.white;
    }

    /// <summary>Morning scenes (the interview room before opening) show daylight, whatever the clock says.</summary>
    public static bool ForceDay;
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() => ForceDay = false;

    /// <summary>0 = day, 1 = night, for the current game hour.</summary>
    public float NightAmount
    {
        get
        {
            var gm = GameManager.Instance;
            if (gm == null || ForceDay) return 0f;
            if (gm.CurrentPhase == GameManager.Phase.Closed) return 1f;
            float t = Mathf.InverseLerp(duskStart, gm.closeHour, gm.Hour);
            return Mathf.Max(Mathf.Clamp01(nightCurve.Evaluate(t)), DayStory.Darkness); // a power cut: candlelight at noon
        }
    }

    void Update()
    {
        float target = NightAmount;
        // Ease towards the target so jumps (new day) don't pop; normal time flow is slow anyway.
        current = current < 0f || ForceDay ? target : Mathf.MoveTowards(current, target, Time.deltaTime / Mathf.Max(0.01f, blendSpeed));
        volume.weight = current;

        float shaft = Mathf.Lerp(1f, shaftsAtNight, current);
        for (int i = 0; i < lightShafts.Length; i++)
        {
            var r = lightShafts[i];
            if (r == null) continue;
            var c = shaftColors[i];
            c.a *= shaft;
            r.GetPropertyBlock(block);
            block.SetColor(BaseColor, c);
            r.SetPropertyBlock(block);
        }
    }
}
