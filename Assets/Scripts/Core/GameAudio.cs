using System;
using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// One place for gameplay sounds. Lives on the GameManager; anything can call
/// GameAudio.Play(a => a.coin). Also gives every UI button on the game UI a click.
/// </summary>
public class GameAudio : MonoBehaviour
{
    public static GameAudio Instance { get; private set; }

    [Header("Economy")]
    public AudioClip coin;
    public AudioClip buy;
    [Header("Customers")]
    public AudioClip customerArrives;
    public AudioClip served;
    public AudioClip wrongBook;
    public AudioClip angryLeave;
    public AudioClip impatient;
    [Header("Chores")]
    public AudioClip tidy;
    public AudioClip cardIssued;
    public AudioClip cardWrong;
    [Header("Day")]
    public AudioClip closed;
    public AudioClip dayReport;
    public AudioClip newDay;
    public AudioClip unlock;
    public AudioClip delivery;
    [Header("UI")]
    public AudioClip uiClick;
    public AudioClip panelOpen;
    public AudioClip panelClose;
    public AudioClip pcOpen;
    public AudioClip pcClose;
    public UIDocument gameUI;
    [Range(0f, 1f)] public float volume = 0.8f;

    AudioSource source;
    float lastPlay;
    AudioClip lastClip;

    void Awake()
    {
        Instance = this;
        source = gameObject.AddComponent<AudioSource>();
        source.playOnAwake = false;
        source.spatialBlend = 0f;
    }

    void OnDestroy() { if (Instance == this) Instance = null; }

    void Start()
    {
        var gm = GameManager.Instance;
        if (gm != null)
        {
            gm.CoinsChanged += (_, delta) => { if (delta > 0) Play(a => a.coin, 0.6f); };
            gm.PhaseChanged += p => { if (p == GameManager.Phase.Closed) Play(a => a.closed); };
            gm.DayStarted += () => Play(a => a.newDay);
        }
        if (gameUI != null) HookClicks(gameUI.rootVisualElement);
    }

    /// <summary>Every button under this root clicks (trickle down so it runs before the button's own handler).</summary>
    public static void HookClicks(VisualElement root)
    {
        if (root == null || root.ClassListContains("audio-hooked")) return;
        root.AddToClassList("audio-hooked");
        root.RegisterCallback<ClickEvent>(e =>
        {
            if (e.target is Button b && b.enabledInHierarchy) Play(a => a.uiClick, 0.5f);
        }, TrickleDown.TrickleDown);
    }

    public static void Play(Func<GameAudio, AudioClip> pick, float volumeScale = 1f)
    {
        var a = Instance;
        if (a == null) return;
        var clip = pick(a);
        if (clip == null) return;
        // Don't stack the same clip on one frame (e.g. coins + served together).
        if (clip == a.lastClip && Time.unscaledTime - a.lastPlay < 0.05f) return;
        a.lastClip = clip;
        a.lastPlay = Time.unscaledTime;
        a.source.PlayOneShot(clip, a.volume * volumeScale);
    }
}
