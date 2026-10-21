using System.Collections;
using UnityEngine;

/// <summary>
/// The library's music on the Audio_BGM object. Only swaps the clip on its AudioSource, with a fade:
///  menu track  - the title screen and the morning staff interviews (not the Esc pause screen or the tutorial);
///  day track   - the library while it's open (the clip the scene gave the source);
///  night track - from closing time, and the ending with the Dean.
/// At closing (5:30 PM) the day track fades out while the grandfather clock strikes and the night track only
/// fades in once the strikes are over. The source's volume, loop and other settings are left as they are
/// (the volume only dips during a fade). The new-game intro scene plays the menu track itself.
/// </summary>
[RequireComponent(typeof(AudioSource))]
public class DayNightMusic : MonoBehaviour
{
    [Tooltip("Plays from closing time until the next morning, and in the ending.")]
    public AudioClip nightMusic;
    [Tooltip("Plays on the title screen and during the morning staff interviews.")]
    public AudioClip menuMusic;
    [Tooltip("Seconds to fade out, and again to fade in.")]
    public float fadeSeconds = 4f;

    AudioSource source;
    AudioClip dayMusic; // the clip the scene gave the source
    bool swapping, settled;
    float bellAt = -100f; // when the closing clock started striking

    static bool IsNight => GameManager.Instance != null && GameManager.Instance.CurrentPhase != GameManager.Phase.Open;

    AudioClip Wanted
    {
        get
        {
            bool interview = StaffInterview.OverlayOpen;
            AudioClip c = MainMenuUI.OnTitle || (interview && !StaffInterview.Ending) ? menuMusic
                        : (interview && StaffInterview.Ending) || IsNight ? nightMusic : dayMusic;
            return c != null ? c : dayMusic;
        }
    }

    void Awake()
    {
        source = GetComponent<AudioSource>();
        dayMusic = source.clip;
    }

    void Start()
    {
        if (GameManager.Instance != null) GameManager.Instance.PhaseChanged += OnPhase;
    }

    void OnDestroy() { if (GameManager.Instance != null) GameManager.Instance.PhaseChanged -= OnPhase; }

    void OnPhase(GameManager.Phase p) { if (p == GameManager.Phase.Closing) bellAt = Time.unscaledTime; }

    void Update()
    {
        // First frame (everything has started, the title screen too): the right track straight away, no fade.
        // This is also what a reload after closing starts on.
        if (!settled) { settled = true; if (source.clip != Wanted) { source.clip = Wanted; source.Play(); } return; }
        if (!swapping && source.clip != Wanted) StartCoroutine(Swap());
    }

    IEnumerator Swap()
    {
        swapping = true;
        float full = source.volume, started = Time.unscaledTime;
        // Closing: the clock is striking (GameManager.closingBell), so the old track is gone before it ends
        // and the new one waits for the last strike.
        var bell = GameManager.Instance != null ? GameManager.Instance.closingBell : null;
        if (bell != null && started - bellAt > bell.length) bell = null;
        float fadeOut = bell != null ? Mathf.Min(fadeSeconds, bell.length * 0.75f) : fadeSeconds;
        for (float t = 0f; t < fadeOut; t += Time.unscaledDeltaTime)
        {
            source.volume = full * (1f - t / fadeOut);
            yield return null;
        }
        source.volume = 0f;
        if (bell != null)
        {
            source.Stop();
            while (Time.unscaledTime < bellAt + bell.length) yield return null;
        }
        source.clip = Wanted;
        source.Play();
        for (float t = 0f; t < fadeSeconds; t += Time.unscaledDeltaTime)
        {
            source.volume = full * (t / fadeSeconds);
            yield return null;
        }
        source.volume = full; // back to exactly what it was
        swapping = false;
    }
}
