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
    [Tooltip("New games start with this intro scene (the Dean's interview). Empty = straight into Day 1.")]
    public string introScene = "InterviewScene";

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

    VisualElement root, book, main, confirm, settings, credits, howto, modePanel, settingsRows;
    GameManager.Difficulty chosenMode = GameManager.Difficulty.Standard;
    int howtoPage;

    static readonly (string title, string text)[] HowTo =
    {
        ("Welcome!", "You run the library: lend books, help readers and keep the place tidy.\n\nMove: WASD\nRun: Shift (uses stamina; when it runs out, catch your breath)\nInteract: Left click\nMenu: Esc\n\nThe clock stops whenever a menu or chat is open, so take your time.\n\nStandard mode is the way the game is meant to be played."),
        ("The counter", "Visitors queue at the counter. Click them to talk.\n\nThe face over their head shows their mood: keep them smiling for bigger tips. Stuck? Ask for a hint, but it shrinks their tip.\n\nBrowsers pick a book themselves: just click to stamp it out, or check it in after they've read it."),
        ("Finding books", "Use the computer: Books searches titles and authors. Filter by subject and type, and read the abstracts for vague requests.\n\nClick a shelf to take a copy, then click the visitor to hand it over. Upgrade your Book bag to carry more.\n\nNot sure where a book goes? Look it up on the computer: it shows every book's shelf."),
        ("Your notebook", "Every request you hear goes in your notebook on the right: what they asked, the hint, and the books you've noted.\n\nOn the computer, pick a book and press Add to note: it's written down with its shelf. Hand over a wrong book and it gets struck through.\n\nNobody waiting? Add to note puts books from the returns or tables on a To Shelve list, ticked off when you shelve them.\n\nDone with a task? Its entry clears itself. Click the tab to fold the notebook away."),
        ("Ordering books", "Some visitors look around for a book the library doesn't have, then ask you to order it. Others find a book sold out.\n\nAsk \"When do you need it?\". If it's ASAP they wait in the library, so order it express (2 hours). If it's tomorrow, a normal order is fine.\n\nOrder on the computer (Shop): requested books have a gold Requested tag, and new titles join your library when you order them. Collect deliveries from the book cart and hand them over.\n\nNew members may ask you to order a book too."),
        ("Cards & readers", "Visitors who want a library card tell you their name, birthday and which rooms they need. On the computer (Members), type their name, pick the birthday and choose the access.\n\nChat with seated readers: point them to the right shelf for a tip."),
        ("Repairs & rooms", "Worn or damaged copies go to the repair desk: glue, press and fit a new cover for a perfect restoration. When it's done, the desk glows: click it to pick the book up and put it back on its shelf.\n\nThe Archive opens on Day 2 and Rare Books on Day 3. VIP scholars need a Rare card."),
        ("Staff", "Need help? Press Interview on the computer (Shop > Staff) or in the Day Report. Two applicants come in the next morning, after the Day Report and before the library opens.\n\nAsk each three questions, and talk about pay: offer a price, and if it's too low they'll tell you the lowest they can go. Prices: about 70 for a cleaner, 250 for the archivist, 650 for the Rare Books attendant, paid once.\n\nUpgrades: cleaner 35 then 70, archivist 150 then 300, attendant 650.\n\nWhen a pointer says Help!, go and sort out their problem. At closing they wait with a Report. Upgrade staff to make them faster and need less help."),
        ("End of the day", "At 6 pm the library closes. Tidy cups and open books, shelve the returns, then click the front ropes to lock up.\n\nIn the Day Report you can order books, hire staff and buy upgrades. The game saves every morning.\n\nThe Audit Log on the computer (and the Log tab in the Day Report) lists every request, your notes and how it ended, day by day."),
    };
    Label saveLabel;
    Button continueButton, newButton;
    bool started; // play has begun once: Esc now pauses instead
    AudioSource sfx;
    InputAction cancel;
    bool showing, hasSave;
    Vector3 camHome;

    float lastToggle = -10f;
    const float ToggleCooldown = 0.35f;
    bool camHomeSet;

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
        saveLabel.text = hasSave ? $"Your library: Day {gm.day}  ·  {gm.Coins} coins  ·  {GameManager.DifficultyNames[(int)gm.difficulty]}" : "A new library awaits.";

        var cont = continueButton = doc.Q<Button>("btn-continue");
        newButton = doc.Q<Button>("btn-new");
        cont.EnableInClassList("hidden", !hasSave);
        doc.Q<Button>("btn-new").text = hasSave ? "New Game" : "Start";
        cont.clicked += Play;
        // New Game / Start: choose a pace first (Standard is the recommended one).
        modePanel = doc.Q("menu-mode");
        doc.Q<Button>("btn-new").clicked += () => Show(modePanel);
        for (int i = 0; i < 3; i++)
        {
            var mode = (GameManager.Difficulty)i;
            doc.Q<Button>("mode-" + i).clicked += () => PickMode(mode);
        }
        doc.Q<Button>("btn-mode-back").clicked += () => Show(main);
        doc.Q<Button>("btn-settings").clicked += () => { BuildSettings(); Show(settings); };
        doc.Q<Button>("btn-credits").clicked += () => Show(credits);
        howto = doc.Q("menu-howto");
        doc.Q<Button>("btn-howto").clicked += () => { howtoPage = 0; ShowHowTo(); Show(howto); };
        doc.Q<Button>("btn-howto-prev").clicked += () => { howtoPage = Mathf.Max(0, howtoPage - 1); ShowHowTo(); };
        doc.Q<Button>("btn-howto-next").clicked += () => { howtoPage = Mathf.Min(HowTo.Length - 1, howtoPage + 1); ShowHowTo(); };
        doc.Q<Button>("btn-howto-back").clicked += () => Show(main);
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
        if (SkipOnce) { SkipOnce = false; root.AddToClassList("hidden"); BeginPlay(false); HUD.StartBlack(); return; }

        showing = true;
        PlayerLock.Lock(this);
        SetGameHudVisible(false);
        if (menuCamera != null)
        {
            if (!camHomeSet) { camHome = menuCamera.transform.position; camHomeSet = true; }
            menuCamera.Priority = 30;
        }
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
            if (started && cancel != null && cancel.WasPressedThisFrame() && !PlayerLock.IsLocked && PlayerLock.UnlockedFrame != Time.frameCount
                && Time.unscaledTime - lastToggle > ToggleCooldown)
            {
                lastToggle = Time.unscaledTime;
                OpenPause();
            }
            return;
        }
        if (menuCamera != null && !started)
        {
            // Slow figure-of-eight over the hall (title screen only).
            float t = Time.unscaledTime * Mathf.PI * 2f / Mathf.Max(1f, driftSeconds);
            menuCamera.transform.position = camHome + new Vector3(Mathf.Sin(t) * driftAmount.x, Mathf.Sin(t * 2f) * driftAmount.y, Mathf.Cos(t) * driftAmount.z);
        }
        if (cancel != null && cancel.WasPressedThisFrame())
        {
            if (main.ClassListContains("hidden")) Show(main);
            else if (started && Time.unscaledTime - lastToggle > ToggleCooldown) { lastToggle = Time.unscaledTime; BeginPlay(true); } // Esc again resumes
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
        var hint = root.Q<Label>("menu-hint");
        if (hint != null) hint.text = "Paused  -  press Esc to resume";
        continueButton.RemoveFromClassList("hidden");
        newButton.text = "New Game";
        if (gm != null) saveLabel.text = $"Day {gm.day}  ·  {gm.ClockText}  ·  {gm.Coins} coins  ·  {GameManager.DifficultyNames[(int)gm.difficulty]}";
        Show(main);
        root.RemoveFromClassList("hidden");
        root.RemoveFromClassList("fading");
        root.pickingMode = PickingMode.Position;
        book.AddToClassList("intro");
        book.schedule.Execute(() => book.RemoveFromClassList("intro")).ExecuteLater(30);
        // The camera stays on the librarian while paused (no swooping back and forth).
    }

    void Show(VisualElement panel)
    {
        foreach (var p in new[] { main, confirm, settings, credits, howto, modePanel }) p?.EnableInClassList("hidden", p != panel);
        if (panel == modePanel) doc().Q<Button>("mode-1")?.Focus(); // Standard is the default
    }

    // Words picked out in the accent colour on the How to Play pages (longest first).
    static readonly string[] HowToKeywords =
    {
        "Left click", "Left Shift", "Shift", "WASD", "Esc", "Standard mode", "front ropes", "Day Report", "book cart",
        "Add to note", "To Shelve", "Audit Log", "Log tab", "notebook", "Reservations",
        "repair desk", "Rare Books", "Rare card", "VIP scholars", "Archive", "computer", "sold out", "reserve", "hint", "express", "today", "Requested",
        "tips", "tip", "stamina", "Shop", "Members", "Books", "Staff", "Interview", "Day 1", "Day 2", "Day 3", "6 pm",
    };
    int lastHowtoPage;

    static string Highlight(string text)
    {
        var pattern = "\\b(" + string.Join("|", System.Array.ConvertAll(HowToKeywords, System.Text.RegularExpressions.Regex.Escape)) + ")\\b";
        return System.Text.RegularExpressions.Regex.Replace(text, pattern, "<color=#D67840>$1</color>");
    }

    void ShowHowTo()
    {
        var doc = GetComponent<UIDocument>().rootVisualElement;
        doc.Q<Label>("howto-title").text = HowTo[howtoPage].title;
        doc.Q<Label>("howto-text").text = Highlight(HowTo[howtoPage].text);
        doc.Q<Label>("howto-page").text = $"{howtoPage + 1} / {HowTo.Length}";
        doc.Q<Button>("btn-howto-prev").SetEnabled(howtoPage > 0);
        doc.Q<Button>("btn-howto-next").SetEnabled(howtoPage < HowTo.Length - 1);

        // Page icon, with a little pop.
        var icon = doc.Q("howto-icon");
        for (int i = 0; i < HowTo.Length; i++) icon.EnableInClassList("p" + i, i == howtoPage);
        icon.AddToClassList("pop");
        icon.schedule.Execute(() => icon.RemoveFromClassList("pop")).ExecuteLater(160);

        // Page turn: slide in from the side you're flipping towards.
        var body = doc.Q("howto-body");
        string turn = howtoPage >= lastHowtoPage ? "turn" : "turn-back";
        body.AddToClassList(turn);
        body.schedule.Execute(() => { body.RemoveFromClassList("turn"); body.RemoveFromClassList("turn-back"); }).ExecuteLater(20);
        lastHowtoPage = howtoPage;
    }

    void Play() => BeginPlay(true);

    VisualElement doc() => GetComponent<UIDocument>().rootVisualElement;

    void PickMode(GameManager.Difficulty mode)
    {
        chosenMode = mode;
        if (hasSave)
        {
            // Starting over replaces the current library: ask first.
            doc().Q<Label>("confirm-text").text =
                $"Your library on Day {GameManager.Instance.day} will be packed away for good. Begin again from Day 1 in {GameManager.DifficultyNames[(int)mode]} mode?";
            Show(confirm);
            return;
        }
        if (!string.IsNullOrEmpty(introScene)) { GameManager.PendingDifficulty = mode; GoToIntro(); return; }
        GameManager.Instance?.StartFresh(mode);
        Play();
    }

    /// <summary>A brand-new library: meet the Dean and hire a cleaner first, then Day 1.</summary>
    void GoToIntro()
    {
        GameManager.Instance?.DeleteSave();
        PlayerLock.Reset();
        UnityEngine.SceneManagement.SceneManager.LoadScene(introScene);
    }

    void NewGame()
    {
        // Fresh library from Day 1: wipe the save and reload, skipping the title screen.
        SkipOnce = true;
        GameManager.PendingDifficulty = chosenMode;
        if (!string.IsNullOrEmpty(introScene)) { GoToIntro(); return; }
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
        var gm = GameManager.Instance;
        if (gm != null && (hasSave || started))
        {
            var mode = Row("Mode");
            mode.Add(Button("<", () => { gm.SetDifficulty((GameManager.Difficulty)(((int)gm.difficulty + 2) % 3)); BuildSettings(); }));
            mode.Add(Value(gm.difficulty == GameManager.Difficulty.Standard ? "Standard ★" : GameManager.DifficultyNames[(int)gm.difficulty]));
            mode.Add(Button(">", () => { gm.SetDifficulty((GameManager.Difficulty)(((int)gm.difficulty + 1) % 3)); BuildSettings(); }));
        }
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
