using Unity.Cinemachine;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

/// <summary>
/// Title screen shown over the live library when the game starts. A menu camera drifts slowly
/// around the hall; pressing Continue / New Game fades the book away and the camera glides down
/// to the librarian. The clock is paused (PlayerLock) until play starts.
/// </summary>
[RequireComponent(typeof(UIDocument))]
public class MainMenuUI : MonoBehaviour
{
    public static MainMenuUI Instance { get; private set; }
    /// <summary>True while the title screen is up.</summary>
    public static bool Active => Instance != null && Instance.showing;
    /// <summary>Set before reloading for a new game, so it starts straight away.</summary>
    public static bool SkipOnce;

    public string gameTitle = "The Whitmore Library";
    public string subtitle = "a cozy library tale";
    [Tooltip("Menu camera (higher priority than the follow cam while the menu is up).")]
    public CinemachineCamera menuCamera;
    [Tooltip("How far the menu camera sways, in metres, and how long one sway takes.")]
    public Vector3 driftAmount = new Vector3(3f, 0.6f, 1.5f);
    public float driftSeconds = 24f;
    public AudioClip hoverSound, startSound;

    static readonly string[] Tips =
    {
        "Tip: the PC catalogue only searches titles and authors. Read the abstracts!",
        "Tip: sold out? Reserve it, then order it from the PC Shop.",
        "Tip: chat with readers - point them to the right shelf for a bigger tip.",
        "Tip: the clock stops while you're in a menu or a conversation.",
        "Tip: the Archive opens on Day 2 and Rare Books on Day 3.",
        "Tip: VIP scholars need a Rare Books card before they can read.",
    };

    VisualElement root, book, main, confirm, settings, credits, settingsRows;
    Label saveLabel;
    Button continueButton, newButton;
    bool started; // play has begun once: Esc now pauses instead
    AudioSource sfx;
    InputAction cancel;
    bool showing, hasSave;
    Vector3 camHome;

    void Awake()
    {
        Instance = this;
        sfx = gameObject.AddComponent<AudioSource>();
        sfx.playOnAwake = false;
        cancel = InputSystem.actions?.FindAction("UI/Cancel");
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
        PlayerLock.Unlock(this);
    }

    void Start()
    {
        var doc = GetComponent<UIDocument>().rootVisualElement;
        root = doc.Q("menu");
        book = doc.Q("menu-book");
        main = doc.Q("menu-main");
        confirm = doc.Q("menu-confirm");
        settings = doc.Q("menu-settings");
        credits = doc.Q("menu-credits");
        settingsRows = doc.Q("settings-rows");
        saveLabel = doc.Q<Label>("menu-save");
        doc.Q<Label>("menu-title").text = gameTitle;
        doc.Q<Label>("menu-subtitle").text = subtitle;
        doc.Q<Label>("menu-tip").text = Tips[Random.Range(0, Tips.Length)];

        var gm = GameManager.Instance;
        hasSave = gm != null && gm.HasSave;
        saveLabel.text = hasSave ? $"Your library: Day {gm.day}  ·  {gm.Coins} coins  ·  {gm.members.Count} members" : "A new library awaits.";

        var cont = continueButton = doc.Q<Button>("btn-continue");
        newButton = doc.Q<Button>("btn-new");
        cont.EnableInClassList("hidden", !hasSave);
        doc.Q<Button>("btn-new").text = hasSave ? "New Game" : "Start";
        cont.clicked += Play;
        doc.Q<Button>("btn-new").clicked += () => { if (hasSave) Show(confirm); else Play(); };
        doc.Q<Button>("btn-settings").clicked += () => { BuildSettings(); Show(settings); };
        doc.Q<Button>("btn-credits").clicked += () => Show(credits);
        var quit = doc.Q<Button>("btn-quit");
        quit.clicked += () => Application.Quit();
        quit.EnableInClassList("hidden", Application.platform == RuntimePlatform.WebGLPlayer); // a browser tab can't quit
        doc.Q<Button>("btn-confirm-yes").clicked += NewGame;
        doc.Q<Button>("btn-confirm-no").clicked += () => Show(main);
        doc.Q<Button>("btn-settings-back").clicked += () => Show(main);
        doc.Q<Button>("btn-credits-back").clicked += () => Show(main);
        doc.Q<Label>("confirm-text").text = hasSave
            ? $"Your library on Day {gm.day} will be packed away for good. Begin again from Day 1?"
            : "";

        // Soft sound when the pointer moves onto a menu button; the click sound comes from GameAudio.
        doc.Query<Button>(className: "menu-button").ForEach(b => b.RegisterCallback<PointerEnterEvent>(_ =>
        {
            if (hoverSound != null && b.enabledInHierarchy) sfx.PlayOneShot(hoverSound, 0.35f);
        }));
        GameAudio.HookClicks(doc);

        cancel?.Enable();
        if (SkipOnce) { SkipOnce = false; root.AddToClassList("hidden"); BeginPlay(false); return; }

        showing = true;
        PlayerLock.Lock(this);
        SetGameHudVisible(false);
        if (menuCamera != null) { camHome = menuCamera.transform.position; menuCamera.Priority = 30; }
        cancel?.Enable();
        // Book slides up into view.
        book.AddToClassList("intro");
        book.schedule.Execute(() => book.RemoveFromClassList("intro")).ExecuteLater(60);
    }

    void Update()
    {
        if (!showing)
        {
            // Esc during play brings the menu back as a pause screen, unless Esc just closed
            // another window (PC, shelf, dialogue...) this frame.
            if (started && cancel != null && cancel.WasPressedThisFrame() && !PlayerLock.IsLocked && PlayerLock.UnlockedFrame != Time.frameCount)
                OpenPause();
            return;
        }
        if (menuCamera != null)
        {
            // Slow figure-of-eight over the hall.
            float t = Time.unscaledTime * Mathf.PI * 2f / Mathf.Max(1f, driftSeconds);
            menuCamera.transform.position = camHome + new Vector3(Mathf.Sin(t) * driftAmount.x, Mathf.Sin(t * 2f) * driftAmount.y, Mathf.Cos(t) * driftAmount.z);
        }
        if (cancel != null && cancel.WasPressedThisFrame())
        {
            if (main.ClassListContains("hidden")) Show(main);
            else if (started) BeginPlay(true); // Esc again resumes
        }
    }

    /// <summary>The title screen again, mid-game: the clock stops and Continue reads "Resume".</summary>
    void OpenPause()
    {
        var gm = GameManager.Instance;
        showing = true;
        PlayerLock.Lock(this);
        SetGameHudVisible(false);
        hasSave = true;
        continueButton.text = "Resume";
        continueButton.RemoveFromClassList("hidden");
        newButton.text = "New Game";
        if (gm != null) saveLabel.text = $"Day {gm.day}  ·  {gm.ClockText}  ·  {gm.Coins} coins";
        Show(main);
        root.RemoveFromClassList("hidden");
        root.RemoveFromClassList("fading");
        root.pickingMode = PickingMode.Position;
        book.AddToClassList("intro");
        book.schedule.Execute(() => book.RemoveFromClassList("intro")).ExecuteLater(30);
        if (menuCamera != null) { camHome = menuCamera.transform.position; menuCamera.Priority = 30; }
    }

    void Show(VisualElement panel)
    {
        foreach (var p in new[] { main, confirm, settings, credits }) p.EnableInClassList("hidden", p != panel);
    }

    void Play() => BeginPlay(true);

    void NewGame()
    {
        // Fresh library from Day 1: wipe the save and reload, skipping the title screen.
        SkipOnce = true;
        GameManager.Instance?.NewGame();
    }

    void BeginPlay(bool fade)
    {
        bool resuming = started;
        started = true;
        showing = false;
        if (startSound != null) sfx.PlayOneShot(startSound, 0.7f);
        if (menuCamera != null) menuCamera.Priority = 0; // Cinemachine blends down to the follow cam
        if (fade)
        {
            root.AddToClassList("fading");
            root.pickingMode = PickingMode.Ignore;
            root.schedule.Execute(() => root.AddToClassList("hidden")).ExecuteLater(650);
        }
        SetGameHudVisible(true);
        PlayerLock.Unlock(this);
        var hud = FindAnyObjectByType<HUD>();
        if (!resuming && hud != null && GameManager.Instance != null) hud.ShowBanner(HUD.MorningText(GameManager.Instance), 6f);
    }

    static void SetGameHudVisible(bool visible)
    {
        var hud = FindAnyObjectByType<HUD>();
        var ui = hud != null ? hud.GetComponent<UIDocument>().rootVisualElement : null;
        ui?.Q("hud")?.EnableInClassList("hidden", !visible);
        ui?.Q("world-layer")?.EnableInClassList("hidden", !visible);
    }

    // ---------------- Settings (same values as the PC Settings screen) ----------------

    void BuildSettings()
    {
        settingsRows.Clear();
        Volume("Volume", GameSettings.Master, v => GameSettings.Master = v);
        Volume("Music", GameSettings.Music, v => GameSettings.Music = v);
        var day = Row("Day");
        day.Add(Button("<", () => { GameSettings.DayLength--; BuildSettings(); }));
        day.Add(Value(GameSettings.DayLengthNames[GameSettings.DayLength]));
        day.Add(Button(">", () => { GameSettings.DayLength++; BuildSettings(); }));
        var full = Row("Screen");
        full.Add(Value(Screen.fullScreen ? "Fullscreen" : "Window"));
        full.Add(Button("Toggle", () => { Screen.fullScreen = !Screen.fullScreen; settingsRows.schedule.Execute(BuildSettings).ExecuteLater(200); }));
    }

    void Volume(string name, int value, System.Action<int> set)
    {
        var row = Row(name);
        row.Add(Button("-", () => { set(value - 1); BuildSettings(); }));
        var pips = new VisualElement();
        pips.AddToClassList("menu-pips");
        for (int i = 0; i < GameSettings.Steps; i++)
        {
            var pip = new VisualElement();
            pip.AddToClassList("menu-pip");
            pip.EnableInClassList("on", i < value);
            int v = i + 1;
            pip.RegisterCallback<ClickEvent>(_ => { set(v == value ? 0 : v); BuildSettings(); });
            pips.Add(pip);
        }
        row.Add(pips);
        row.Add(Button("+", () => { set(value + 1); BuildSettings(); }));
    }

    VisualElement Row(string name)
    {
        var row = new VisualElement();
        row.AddToClassList("menu-setting");
        var l = new Label(name);
        l.AddToClassList("menu-setting-name");
        row.Add(l);
        settingsRows.Add(row);
        return row;
    }

    static Label Value(string text)
    {
        var l = new Label(text);
        l.AddToClassList("menu-setting-value");
        return l;
    }

    static Button Button(string text, System.Action onClick)
    {
        var b = new Button(onClick) { text = text };
        b.AddToClassList("kit-button");
        return b;
    }
}
