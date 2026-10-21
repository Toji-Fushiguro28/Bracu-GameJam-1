using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

/// <summary>
/// The front-desk computer: shows PCOverlayUIDocument (a pixel monitor with tabs: Home, Catalogue, Members,
/// Shop, Log, Settings) when the computer is clicked, and freezes/unfreezes the player.
/// Each tab is a template from Resources cloned under the frame's #page and wired by its view (PCHomeView...).
/// </summary>
[RequireComponent(typeof(ComputerInteractable))]
public class PCOverlayTrigger : MonoBehaviour
{
    const string OverlayObjectName = "PCOverlayUIDocument";
    const string OverlayTemplateName = "PCOverlayTemplate";
    const string ContainerElementName = "container";

    /// <summary>Max raycast distance for click detection (mirrors HoverPrompt.hoverDistance).</summary>
    const float ClickRange = 60f;

    /// <summary>Grace period after opening: the opening click's pointer-up must not instantly close.</summary>
    const float CloseGraceSeconds = 0.35f;

    ComputerInteractable computer;
    HoverPrompt hover;
    RuntimeOutline outline;
    Camera cam;

    GameObject overlayObject;
    UIDocument overlayDoc;
    VisualElement container;

    VisualTreeAsset frameTemplate;
    VisualElement page;
    string currentTab;
    IVisualElementScheduledItem ticker;
    bool starUp;

    /// <summary>The open computer (views use it to switch tabs, e.g. Home's "Order" button).</summary>
    public static PCOverlayTrigger Current { get; private set; }
    /// <summary>The last book looked at in the catalogue (the tutorial checks you found it).</summary>
    public static BookData LastViewed;
    /// <summary>Minimised: the next open comes back to the same tab (and the catalogue keeps its search).</summary>
    static bool resume;
    static string lastTab = "home";
    static bool maximised;

    static readonly string[] Tabs = { "home", "catalogue", "members", "shop", "log", "settings" };
    static readonly Dictionary<string, (string template, System.Action<VisualElement> wire)> Pages = new Dictionary<string, (string, System.Action<VisualElement>)>
    {
        { "home", ("PCHomeTemplate", PCHomeView.Wire) },
        { "catalogue", ("PCBooksTemplate", PCCatalogueView.Wire) },
        { "members", ("PCMembersTemplate", PCMembersView.Wire) },
        { "shop", ("PCShopTemplate", PCShopView.Wire) },
        { "log", ("PCAuditTemplate", PCAuditView.Wire) },
        { "settings", ("PCSettingsTemplate", PCSettingsView.Wire) },
    };

    InputAction attackAction;
    InputAction cancelAction;

    bool isOpen;
    float openedAt = -10f;
    bool prevCursorVisible;
    CursorLockMode prevLock;

    void Awake()
    {
        computer = GetComponent<ComputerInteractable>();
        hover = GetComponent<HoverPrompt>();
        outline = GetComponent<RuntimeOutline>();
        cam = Camera.main;

        attackAction = InputSystem.actions?.FindAction("Attack");
        if (attackAction == null)
            Debug.LogWarning("[PCUI] 'Attack' action not found — click-to-open disabled.", this);
        cancelAction = InputSystem.actions?.FindAction("UI/Cancel");
        if (cancelAction == null)
            Debug.LogWarning("[PCUI] 'UI/Cancel' action not found — Esc-to-close disabled.", this);

        if (!FindOverlay())
        {
            Debug.LogError("[PCUI] PCOverlayUIDocument not found — controller disabled.", this);
            enabled = false;
        }
    }

    void OnEnable()
    {
        attackAction?.Enable();
        cancelAction?.Enable();
    }

    /// <summary>
    /// Locates the overlay by GameObject name, falling back to a template-name search, and starts it hidden.
    /// Its template is the monitor frame (title bar, tabs, chin); each tab's page is cloned into #page.
    /// </summary>
    bool FindOverlay()
    {
        overlayObject = GameObject.Find(OverlayObjectName);
        overlayDoc = overlayObject != null ? overlayObject.GetComponent<UIDocument>() : null;

        if (overlayDoc == null)
        {
            foreach (var candidate in Object.FindObjectsByType<UIDocument>(FindObjectsInactive.Include))
            {
                if (candidate.visualTreeAsset != null && candidate.visualTreeAsset.name == OverlayTemplateName)
                {
                    overlayDoc = candidate;
                    overlayObject = candidate.gameObject;
                    break;
                }
            }
        }

        if (overlayDoc == null || overlayObject == null)
        {
            Debug.LogError("[PCUI] No UIDocument using " + OverlayTemplateName + " in scene.", this);
            return false;
        }

        // NOTE: don't Q elements here. UIDocument rebuilds its whole tree on every SetActive toggle,
        // so wiring happens fresh in Show().
        frameTemplate = overlayDoc.visualTreeAsset;
        overlayObject.SetActive(false);
        return true;
    }

    /// <summary>Backdrop click, click guard and click sounds on a freshly built tree.</summary>
    void RehookBackdrop()
    {
        if (overlayDoc == null || overlayDoc.rootVisualElement == null)
            return;
        container = overlayDoc.rootVisualElement.Q(ContainerElementName);
        ClickGuard.Attach(overlayDoc.rootVisualElement);
        if (container == null)
        {
            Debug.LogError("[PCUI] '" + ContainerElementName + "' element missing.", this);
            return;
        }
        container.RegisterCallback<ClickEvent>(OnBackdropClick);
        GameAudio.HookClicks(container);
    }

    /// <summary>Title bar buttons, the tab row and the ticker that keeps the clock, coins and tab counts live.</summary>
    void WireFrame()
    {
        RehookBackdrop();
        var root = overlayDoc.rootVisualElement;
        page = root.Q("page");
        root.Q<Button>("win-close")?.RegisterCallback<ClickEvent>(_ => { if (!JustOpened) Hide(); });
        root.Q<Button>("win-min")?.RegisterCallback<ClickEvent>(_ => { if (JustOpened) return; resume = true; Hide(); });
        root.Q<Button>("win-max")?.RegisterCallback<ClickEvent>(_ => { maximised = !maximised; root.Q("monitor")?.EnableInClassList("max", maximised); });
        root.Q("monitor")?.EnableInClassList("max", maximised);
        foreach (var t in Tabs)
        {
            string tab = t;
            root.Q<Button>("tab-" + tab)?.RegisterCallback<ClickEvent>(_ => { if (!JustOpened && (Tutorial.NeededTab == null || Tutorial.NeededTab == tab)) ShowTab(tab); });
        }
        ticker = root.schedule.Execute(RefreshFrame).Every(450);
    }

    /// <summary>Swaps the page under the tabs. fresh: the catalogue opens with empty search and filters.</summary>
    public void ShowTab(string tab, bool fresh = false)
    {
        if (page == null || !Pages.TryGetValue(tab, out var p)) return;
        var template = Resources.Load<VisualTreeAsset>(p.template);
        if (template == null) { Debug.LogWarning("[PCUI] Missing template " + p.template, this); return; }
        PickList.Close();
        if (fresh && tab == "catalogue") PCCatalogueView.ResetFilters();
        page.Clear();
        template.CloneTree(page);
        currentTab = lastTab = tab;
        p.wire(page);
        foreach (var t in Tabs) overlayDoc.rootVisualElement.Q("tab-" + t)?.EnableInClassList("selected", t == tab);
        RefreshFrame();
    }

    /// <summary>Opens the Shop on Upgrades (0) or Staff (1).</summary>
    public void ShowShop(int subTab)
    {
        PCShopView.OpenOn(subTab);
        ShowTab("shop");
    }

    /// <summary>Clock, coins and the little counts on the tabs.</summary>
    void RefreshFrame()
    {
        var gm = GameManager.Instance;
        var root = overlayDoc != null ? overlayDoc.rootVisualElement : null;
        if (gm == null || root == null || !isOpen) return;
        SetText(root, "stat-day", "Day " + gm.day);
        SetText(root, "stat-clock", GameManager.FormatHour(gm.Hour));
        SetText(root, "stat-coins", gm.Coins + " coins");

        int toOrder = gm.reservations.Count(PCHomeView.NeedsOrder);
        bool urgent = gm.reservations.Any(r => PCHomeView.NeedsOrder(r) && r.urgent && r.day == gm.day);
        var cat = root.Q<Label>("count-catalogue");
        if (cat != null) { cat.text = toOrder.ToString(); cat.EnableInClassList("hidden", toOrder == 0); cat.EnableInClassList("urgent", urgent); }
        var mem = root.Q<Label>("count-members");
        if (mem != null) { mem.text = "1"; mem.EnableInClassList("hidden", Customer.WaitingForCard == null); }

        // Shop: the star shows on every tab while there's an upgrade or a hire you can afford; the name glows elsewhere.
        bool ready = gm.UpgradeReady || gm.StaffReady;
        var star = root.Q("shop-star");
        if (star != null)
        {
            star.EnableInClassList("hidden", !ready);
            starUp = !starUp;
            star.EnableInClassList("pop", ready && starUp);
        }
        PopularGlow.Ready(root.Q("tab-shop"), ready && currentTab != "shop");
    }

    static void SetText(VisualElement root, string name, string text)
    {
        var l = root.Q<Label>(name);
        if (l != null && l.text != text) l.text = text;
    }

    void Update()
    {
        if (isOpen)
        {
            if (cancelAction != null && cancelAction.WasPressedThisFrame())
                Hide();
            return;
        }

        // The shared InteractionController opens us through ComputerClick.Open().
        if (GetComponent<ComputerClick>() != null)
            return;
        if (computer == null || !computer.IsPlayerInRange)
            return;
        if (attackAction == null || !attackAction.WasPressedThisFrame())
            return;
        if (cam == null && (cam = Camera.main) == null)
            return;

        if (Pointer.current == null)
            return;
        Vector2 pointer = Pointer.current.position.ReadValue();
        Ray ray = cam.ScreenPointToRay(pointer);
        if (Physics.Raycast(ray, out RaycastHit hit, ClickRange, ~0, QueryTriggerInteraction.Collide)
            && hit.collider.GetComponentInParent<ComputerInteractable>() == computer)
            Show();
    }

    /// <summary>True right after opening: the click that opened the PC must not also press a menu tile.</summary>
    bool JustOpened => Time.unscaledTime - openedAt < CloseGraceSeconds;

    /// <summary>Open the PC overlay (used by the shared click system).</summary>
    public void Open() => Show();

    /// <summary>Shut it from outside (the power just went).</summary>
    public void ForceClose() => Hide(true);

    void Show()
    {
        if (isOpen || overlayObject == null)
            return;
        if (DayStory.PowerOut)
        {
            // Power cut: a dead screen.
            var click = GetComponent<ComputerClick>();
            if (click != null) WorldPrompts.Instance?.ShowBubble(click, "No power. The computer's dead until 2:00 PM.", 3f);
            return;
        }
        isOpen = true;
        Current = this;
        PlayerLock.Lock(this);
        openedAt = Time.unscaledTime;
        overlayObject.SetActive(true);
        GameAudio.Play(a => a.pcOpen, 0.7f);

        // Fresh tree after every activation: build the frame, then Home (or where you were, after a minimise).
        if (frameTemplate != null)
            overlayDoc.visualTreeAsset = frameTemplate;
        WireFrame();
        bool back = resume;
        resume = false;
        PCCatalogueView.ResetFilters(); // every open (after closing or minimising): All books, no search
        ShowTab(Tutorial.NeededTab ?? (back ? lastTab : "home"), !back); // a normal open: the catalogue starts on All books
        // The tutorial: only the tab it needs (the others greyed out).
        foreach (var t in Tabs) overlayDoc.rootVisualElement.Q<Button>("tab-" + t)?.SetEnabled(Tutorial.NeededTab == null || Tutorial.NeededTab == t);

        prevCursorVisible = UnityEngine.Cursor.visible;
        prevLock = UnityEngine.Cursor.lockState;
        UnityEngine.Cursor.visible = true;
        UnityEngine.Cursor.lockState = CursorLockMode.None;

        // Player movement is frozen by PlayerLock above.
        if (hover != null)
        {
            hover.enabled = false;
            HidePromptIcon();
        }
        if (outline != null)
            outline.Hide();
    }

    /// <param name="force">Close even while the tutorial waits for "Add to note".</param>
    void Hide(bool force = false)
    {
        if (!isOpen || (!force && Tutorial.NoteOnly))
            return;
        isOpen = false;
        ticker?.Pause();
        PickList.Close();
        PCHomeView.HideTip();
        GameAudio.Play(a => a.pcClose, 0.7f);
        PlayerLock.Unlock(this);
        if (overlayObject != null)
            overlayObject.SetActive(false);

        UnityEngine.Cursor.visible = prevCursorVisible;
        UnityEngine.Cursor.lockState = prevLock;

        if (hover != null)
            hover.enabled = true; // its Update re-shows the icon when appropriate
        if (outline != null && computer != null && computer.IsPlayerInRange)
            outline.Show();
    }

    /// <summary>
    /// Same approach as HoverPrompt.Hide(): the prompt icon is driven by inline
    /// display, so hide it the same way while the overlay owns the screen.
    /// </summary>
    void HidePromptIcon()
    {
        var doc = hover.doc;
        if (doc == null || doc.rootVisualElement == null)
            return;
        var icon = doc.rootVisualElement.Q("prompt-icon");
        if (icon != null)
            icon.style.display = DisplayStyle.None;
    }

    /// <summary>Click-outside-to-close: only clicks whose target IS the backdrop close.</summary>
    void OnBackdropClick(ClickEvent evt)
    {
        if (!isOpen)
            return;
        if (Time.unscaledTime - openedAt < CloseGraceSeconds)
            return;
        if ((evt.target as VisualElement) == container)
            Hide();
    }
}

/// <summary>
/// Attaches <see cref="PCOverlayTrigger"/> to the computer at runtime, Mechanics scene only.
/// Keeps every other scene untouched without any scene-file edits.
/// </summary>
public static class PCOverlayBootstrap
{
    const string TargetScene = "Mechanics";

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void OnAfterSceneLoad()
    {
        SceneManager.sceneLoaded += OnSceneLoaded;
        TryAttach(SceneManager.GetActiveScene());
    }

    static void OnSceneLoaded(Scene scene, LoadSceneMode mode) => TryAttach(scene);

    static void TryAttach(Scene scene)
    {
        if (!scene.IsValid() || scene.name != TargetScene)
            return;
        var computer = Object.FindAnyObjectByType<ComputerInteractable>();
        if (computer == null)
        {
            Debug.LogWarning("[PCUI] No ComputerInteractable in Mechanics — overlay not attached.");
            return;
        }
        if (computer.GetComponent<PCOverlayTrigger>() == null)
        {
            computer.gameObject.AddComponent<PCOverlayTrigger>();
            Debug.Log("[PCUI] Attached PCOverlayTrigger to " + computer.name);
        }
    }
}
