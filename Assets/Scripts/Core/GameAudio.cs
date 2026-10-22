using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// One place for gameplay sounds. Lives on the GameManager; anything can call
/// GameAudio.Play(a => a.coin). Also gives every UI button on the game UI a click.
/// </summary>
public class GameAudio : MonoBehaviour
{
    public static GameAudio Instance { get; private set; }

    /// <summary>A clip with its own volume (the event sounds vary a lot in loudness).</summary>
    [Serializable]
    public class Sfx
    {
        public AudioClip clip;
        [Range(0f, 1f)] public float volume = 1f;
    }

    [Header("Economy")]
    public AudioClip coin;
    [Tooltip("Each coin landing in the purse (top right).")]
    public AudioClip coinAdd;
    public AudioClip buy;
    [Tooltip("Coins leaving the purse: plays whenever the total goes down.")]
    public Sfx coinSpend = new Sfx();
    [Header("Customers")]
    public AudioClip customerArrives;
    public AudioClip served;
    public AudioClip wrongBook;
    public AudioClip angryLeave;
    public AudioClip impatient;
    [Header("Chores")]
    public AudioClip tidy;
    [Tooltip("A staff member calling for help (not the counter bell).")]
    public AudioClip staffHelp;
    public AudioClip cardIssued;
    public AudioClip cardWrong;
    [Header("Day")]
    public AudioClip closed;
    public AudioClip dayReport;
    public AudioClip newDay;
    public AudioClip unlock;
    public AudioClip delivery;
    [Tooltip("Reputation went up (a rising chime).")]
    public AudioClip reputationUp;
    [Tooltip("A member gained half a heart (a bright ding) / lost one (a low ding).")]
    public AudioClip heartUp;
    public AudioClip heartDown;
    [Header("Events (each with its own volume, kept a little under the music)")]
    public Sfx powerOff = new Sfx();
    public Sfx powerOn = new Sfx();
    [Tooltip("Loops on the leaky-roof day until the rain stops.")]
    public Sfx rainLoop = new Sfx();
    [Tooltip("Loops quietly while the power's out.")]
    public Sfx candleLoop = new Sfx();
    [Tooltip("A visitor sits down with a book.")]
    public Sfx pageTurn = new Sfx();
    [Tooltip("Locking the front doors at closing.")]
    public Sfx doorCreak = new Sfx();
    [Tooltip("Mr. Pryce starts his inspection.")]
    public Sfx announcement = new Sfx();
    public Sfx thiefAlert = new Sfx();
    public Sfx thiefCaught = new Sfx();
    [Tooltip("The book signing starts.")]
    public Sfx signingStart = new Sfx();
    [Tooltip("The exam rush starts.")]
    public Sfx examStart = new Sfx();
    [Tooltip("A book got soaked by the leak.")]
    public Sfx soaked = new Sfx();
    [Tooltip("A day's event is over (rush ends, verdict, signing farewell).")]
    public Sfx eventDone = new Sfx();
    [Tooltip("Someone arrives to collect a reserved book.")]
    public Sfx pickupArrives = new Sfx();
    [Tooltip("A tutorial step ticked off.")]
    public Sfx tutorialTick = new Sfx();
    [Tooltip("The ink stamp landing on a new library card.")]
    public Sfx cardStamp = new Sfx();
    [Header("Typing and wheels")]
    [Tooltip("One key press. Every character typed into any text box plays it at a random pitch.")]
    public Sfx typing = new Sfx();
    [Tooltip("Single clicks cut from the date scroll sound. Each wheel step plays a random one.")]
    public AudioClip[] scrollClicks;
    [Range(0f, 1f)] public float scrollVolume = 0.5f;
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
    float lastSpend = -1f;
    int lastScroll = -1;
    // Typing and wheel clicks change pitch per sound, so they take turns on their own little pool of sources.
    // Static so the intro scene (no GameAudio there) can type too; rebuilt after a scene change.
    static AudioSource[] pitched;
    static int nextPitched;
    static float lastTypePitch = 1f;
    static AudioClip introKey; // the intro scene's key sound, lent by StaffInterview

    void Awake()
    {
        Instance = this;
        source = gameObject.AddComponent<AudioSource>();
        source.playOnAwake = false;
        source.spatialBlend = 0f;
        GameSettings.Skip(source); // scaled per clip in Play (SFX or UI)
    }

    /// <summary>vol is the final volume (master already applied).</summary>
    static void PlayPitched(AudioClip clip, float vol, float pitch)
    {
        if (clip == null) return;
        if (pitched == null || pitched[0] == null)
        {
            var host = Instance != null ? Instance.gameObject : new GameObject("Key Sounds");
            pitched = new AudioSource[8];
            for (int i = 0; i < pitched.Length; i++)
            {
                pitched[i] = host.AddComponent<AudioSource>();
                pitched[i].playOnAwake = false;
                pitched[i].spatialBlend = 0f;
                GameSettings.Skip(pitched[i]);
            }
        }
        var s = pitched[nextPitched];
        nextPitched = (nextPitched + 1) % pitched.Length;
        s.pitch = pitch;
        s.PlayOneShot(clip, vol * GameSettings.UiScale); // typing and wheel clicks are UI sounds
    }

    /// <summary>The intro has no GameAudio: its name box types with this clip.</summary>
    public static void UseIntroKey(AudioClip clip) => introKey = clip;

    /// <summary>A key press: random pitch and loudness, never the same pitch twice running, like a real keyboard.
    /// The spacebar is a touch lower.</summary>
    public static void Type(bool space = false)
    {
        var a = Instance;
        var clip = a != null && a.typing.clip != null ? a.typing.clip : introKey;
        if (clip == null) return;
        float p;
        if (space) p = UnityEngine.Random.Range(0.8f, 0.84f);
        else do p = UnityEngine.Random.Range(0.86f, 1.18f); while (Mathf.Abs(p - lastTypePitch) < 0.05f);
        if (!space) lastTypePitch = p;
        float v = (a != null ? a.volume * a.typing.volume : 0.4f) * UnityEngine.Random.Range(0.75f, 1f);
        PlayPitched(clip, v, p);
    }

    /// <summary>Coins leaving the purse (shop, wages, fines): any time the total goes down. It replaces the
    /// purchase sound when both would play together (see Play and JustSpent).</summary>
    void CoinsSpent()
    {
        lastSpend = Time.unscaledTime;
        Play(a => a.coinSpend);
    }

    /// <summary>True right after coins went down: a purchase sound now would only clash with the coin clink.</summary>
    public static bool JustSpent => Instance != null && Time.unscaledTime - Instance.lastSpend < 0.25f;

    /// <summary>One wheel step: a random click (never the last one), nudged in pitch.</summary>
    public static void Scroll()
    {
        var a = Instance;
        if (a == null || a.scrollClicks == null || a.scrollClicks.Length == 0) return;
        int i = UnityEngine.Random.Range(0, a.scrollClicks.Length);
        if (i == a.lastScroll && a.scrollClicks.Length > 1) i = (i + 1) % a.scrollClicks.Length;
        a.lastScroll = i;
        PlayPitched(a.scrollClicks[i], a.volume * a.scrollVolume, UnityEngine.Random.Range(0.94f, 1.08f));
    }

    void OnDestroy() { if (Instance == this) Instance = null; }

    void Start()
    {
        var gm = GameManager.Instance;
        if (gm != null)
        {
            gm.CoinsChanged += (_, delta) => { if (delta > 0) Play(a => a.coin, 0.6f); else if (delta < 0) CoinsSpent(); };
            gm.PhaseChanged += p => { if (p == GameManager.Phase.Closed) Play(a => a.closed); };
            gm.DayStarted += () => Play(a => a.newDay);
        }
        if (gameUI != null) HookClicks(gameUI.rootVisualElement);
        PutEarsOnPlayer();
    }

    // The camera is ~30 m from the librarian, too far to hear the fire or the staff typing:
    // hear from the librarian instead, facing the way the camera faces (so left and right match the screen).
    Transform ears, player, cam;

    void PutEarsOnPlayer()
    {
        var camListener = Camera.main != null ? Camera.main.GetComponent<AudioListener>() : null;
        var p = GameObject.FindWithTag("Player");
        if (camListener == null || p == null) return;
        camListener.enabled = false;
        player = p.transform;
        cam = Camera.main.transform;
        ears = new GameObject("Ears").transform;
        ears.gameObject.AddComponent<AudioListener>();
    }

    float nextSweep;

    void LateUpdate()
    {
        // New visitors and staff bring their own sound sources: put them on the SFX / UI sliders.
        if (Time.unscaledTime >= nextSweep) { nextSweep = Time.unscaledTime + 1f; GameSettings.ScaleAllSources(); }
        if (ears == null || player == null || cam == null) return;
        ears.SetPositionAndRotation(player.position + Vector3.up * 1.6f, cam.rotation);
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
        HookTyping(root);
    }

    /// <summary>Every text box under this root plays a key press per character typed (and for Backspace / Delete).</summary>
    public static void HookTyping(VisualElement root)
    {
        if (root == null || root.ClassListContains("typing-hooked")) return;
        root.AddToClassList("typing-hooked");
        root.RegisterCallback<KeyDownEvent>(e =>
        {
            if (!(e.target is VisualElement ve)) return;
            var field = ve as TextField ?? ve.GetFirstAncestorOfType<TextField>();
            if (field == null) return;
            // The key and its character arrive as separate events: count printable characters, plus the erase keys,
            // and only when something would actually change (no clatter on an empty box or a full one).
            int len = (field.value ?? "").Length;
            bool selected = field.cursorIndex != field.selectIndex;
            if (e.keyCode == KeyCode.Backspace) { if (field.cursorIndex > 0 || selected) Type(); }
            else if (e.keyCode == KeyCode.Delete) { if (field.cursorIndex < len || selected) Type(); }
            else if (e.character != '\0' && !char.IsControl(e.character) && (field.maxLength < 0 || len < field.maxLength || selected))
                Type(e.character == ' ');
        }, TrickleDown.TrickleDown);
    }

    public static void Play(Func<GameAudio, AudioClip> pick, float volumeScale = 1f)
    {
        var a = Instance;
        if (a == null) return;
        var clip = pick(a);
        if (clip == null) return;
        if (clip == a.buy && JustSpent) return; // the coin clink already says it
        // Don't stack the same clip on one frame (e.g. coins + served together).
        if (clip == a.lastClip && Time.unscaledTime - a.lastPlay < 0.05f) return;
        a.lastClip = clip;
        a.lastPlay = Time.unscaledTime;
        bool ui = clip == a.uiClick || clip == a.panelOpen || clip == a.panelClose || clip == a.pcOpen || clip == a.pcClose;
        a.source.PlayOneShot(clip, a.volume * volumeScale * (ui ? GameSettings.UiScale : GameSettings.SfxScale));
    }

    /// <summary>An event sound, at its own volume.</summary>
    public static void Play(Func<GameAudio, Sfx> pick)
    {
        var a = Instance;
        if (a == null) return;
        var s = pick(a);
        if (s != null) Play(_ => s.clip, s.volume);
    }

    readonly Dictionary<string, AudioSource> loops = new Dictionary<string, AudioSource>();

    /// <summary>Starts or stops a looping sound (rain, candles). Starting one that's already playing does nothing.</summary>
    public static void Loop(Func<GameAudio, Sfx> pick, bool on)
    {
        var a = Instance;
        if (a == null) return;
        var s = pick(a);
        if (s == null || s.clip == null) return;
        a.loops.TryGetValue(s.clip.name, out var src);
        if (!on) { if (src != null) src.Stop(); return; }
        if (src == null)
        {
            src = a.gameObject.AddComponent<AudioSource>();
            src.playOnAwake = false;
            src.loop = true;
            src.spatialBlend = 0f;
            a.loops[s.clip.name] = src;
        }
        src.clip = s.clip;
        GameSettings.SetBase(src, a.volume * s.volume); // the SFX slider scales it
        if (!src.isPlaying) src.Play();
    }
}
