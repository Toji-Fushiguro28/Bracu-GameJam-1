using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

/// <summary>
/// Shows the user-owned PCOverlayUIDocument when the front-desk computer is clicked.
/// This script never modifies the UIDocument or its UXML template — it only toggles
/// the document's GameObject active state and freezes/unfreezes the player.
/// Added at runtime by <see cref="PCOverlayBootstrap"/> — Mechanics scene only.
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
    // Looked up by type name so this compiles without the player scripts.
    Behaviour mover;
    Behaviour footsteps;
    Camera cam;

    GameObject overlayObject;
    UIDocument overlayDoc;
    VisualElement container;

    VisualTreeAsset menuTemplate;
    VisualTreeAsset booksTemplate;
    VisualTreeAsset bookItemTemplate;
    bool booksAvailable;

    List<BookData> allBooks = new List<BookData>();
    List<BookData> filteredBooks = new List<BookData>();
    TextField searchField;
    Label searchPlaceholder;
    ListView booksList;

    InputAction attackAction;
    InputAction cancelAction;

    bool isOpen;
    float openedAt = -10f;
    bool prevCursorVisible;
    CursorLockMode prevLock;
    bool prevMoverEnabled = true;
    bool prevStepsEnabled = true;

    void Awake()
    {
        computer = GetComponent<ComputerInteractable>();
        hover = GetComponent<HoverPrompt>();
        outline = GetComponent<RuntimeOutline>();
        cam = Camera.main;

        var player = GameObject.FindWithTag("Player");
        if (player != null)
        {
            mover = player.GetComponent("PlayerMover") as Behaviour;
            footsteps = player.GetComponent("PlayerFootsteps") as Behaviour;
        }

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
    /// Locates the user's overlay by GameObject name, falling back to a template-name
    /// search. Starts it hidden and hooks the backdrop click — template untouched.
    /// </summary>
    bool FindOverlay()
    {
        overlayObject = GameObject.Find(OverlayObjectName);
        overlayDoc = overlayObject != null ? overlayObject.GetComponent<UIDocument>() : null;

        if (overlayDoc == null)
        {
            foreach (var candidate in Object.FindObjectsByType<UIDocument>(FindObjectsInactive.Include, FindObjectsSortMode.None))
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

        if (overlayDoc.rootVisualElement == null)
        {
            Debug.LogError("[PCUI] Overlay UIDocument has no rootVisualElement.", this);
            return false;
        }

        // NOTE: don't Q elements or register callbacks here. UIDocument rebuilds
        // its whole tree on every SetActive toggle, so wiring happens fresh in Show().
        menuTemplate = overlayDoc.visualTreeAsset;
        booksTemplate = Resources.Load<VisualTreeAsset>("PCBooksTemplate");
        bookItemTemplate = Resources.Load<VisualTreeAsset>("PCBookItem");
        booksAvailable = menuTemplate != null && booksTemplate != null && bookItemTemplate != null;
        if (!booksAvailable)
            Debug.LogWarning("[PCUI] Books templates missing in Resources — Books button disabled.", this);
        allBooks = BookData.Samples();

        // Overlays start hidden; show only on PC click.
        overlayObject.SetActive(false);
        return true;
    }

    /// <summary>Re-queries the backdrop after a visual-tree swap (old callbacks die with the old tree).</summary>
    void RehookBackdrop()
    {
        if (overlayDoc == null || overlayDoc.rootVisualElement == null)
            return;
        container = overlayDoc.rootVisualElement.Q(ContainerElementName);
        if (container == null)
        {
            Debug.LogError("[PCUI] '" + ContainerElementName + "' element missing after view swap.", this);
            return;
        }
        container.RegisterCallback<ClickEvent>(OnBackdropClick);
    }

    void WireMenuView()
    {
        RehookBackdrop();
        RegisterMenuButton("members-button", "Members");
        RegisterMenuButton("upgrades-button", "Upgrades");
        RegisterMenuButton("settings-button", "Settings");

        var books = overlayDoc.rootVisualElement.Q<Button>("books-button");
        if (books == null)
        {
            Debug.LogWarning("[PCUI] Menu button 'books-button' missing in overlay template.", this);
            return;
        }
        if (booksAvailable)
            books.RegisterCallback<ClickEvent>(_ => ShowBooks());
        else
            books.RegisterCallback<ClickEvent>(_ => Debug.Log("[PCUI] Books clicked (templates missing)"));
    }

    void ShowBooks()
    {
        if (!booksAvailable || overlayDoc == null)
            return;
        overlayDoc.visualTreeAsset = booksTemplate;
        RehookBackdrop();
        WireBooksView();
        Debug.Log("[PCUI] Books opened");
    }

    void ShowMenu()
    {
        if (overlayDoc == null || menuTemplate == null)
            return;
        overlayDoc.visualTreeAsset = menuTemplate;
        WireMenuView();
    }

    void WireBooksView()
    {
        var root = overlayDoc.rootVisualElement;
        var back = root.Q<Button>("back-button");
        if (back != null)
            back.RegisterCallback<ClickEvent>(_ => ShowMenu());
        else
            Debug.LogWarning("[PCUI] 'back-button' missing in books template.", this);

        searchField = root.Q<TextField>("book-search");
        searchPlaceholder = root.Q<Label>("search-placeholder");
        booksList = root.Q<ListView>("books-list");

        if (searchField != null)
        {
            searchField.RegisterValueChangedCallback(_ => { ApplySearchFilter(); UpdateSearchPlaceholder(); });
            searchField.RegisterCallback<FocusInEvent>(_ => UpdateSearchPlaceholder());
            searchField.RegisterCallback<FocusOutEvent>(_ => UpdateSearchPlaceholder());
        }
        if (booksList != null)
        {
            booksList.makeItem = () => bookItemTemplate.CloneTree();
            booksList.bindItem = BindBookItem;
            booksList.fixedItemHeight = 96;
            booksList.selectionType = SelectionType.Single;
        }

        filteredBooks = new List<BookData>(allBooks);
        RefreshBooksList();
        UpdateSearchPlaceholder();
    }

    void ApplySearchFilter()
    {
        string query = searchField != null ? searchField.value : "";
        if (string.IsNullOrWhiteSpace(query))
        {
            filteredBooks = new List<BookData>(allBooks);
        }
        else
        {
            string needle = query.Trim().ToLowerInvariant();
            filteredBooks = allBooks.Where(b =>
                (b.Title ?? "").ToLowerInvariant().Contains(needle) ||
                (b.Author ?? "").ToLowerInvariant().Contains(needle) ||
                (b.Subject ?? "").ToLowerInvariant().Contains(needle) ||
                (b.Tag ?? "").ToLowerInvariant().Contains(needle)).ToList();
        }
        RefreshBooksList();
    }

    void RefreshBooksList()
    {
        if (booksList == null)
            return;
        booksList.itemsSource = filteredBooks;
        booksList.Rebuild();
    }

    /// <summary>Minimal row: title, author/subject, tag/qty/shelf/rarity (+ damaged flag).</summary>
    void BindBookItem(VisualElement el, int i)
    {
        if (i < 0 || i >= filteredBooks.Count)
            return;
        BookData b = filteredBooks[i];
        var title = el.Q<Label>("book-title");
        var sub = el.Q<Label>("book-sub");
        var meta = el.Q<Label>("book-meta");
        if (title != null)
            title.text = b.Title;
        if (sub != null)
            sub.text = b.Author + "  ·  " + b.Subject;
        if (meta != null)
        {
            string m = b.Tag + "  ·  Qty: " + b.Quantity + "  ·  Shelf: " + b.ShelfLocation + "  ·  " + b.Rarity;
            if (b.Damaged)
                m += "  ·  DAMAGED";
            meta.text = m;
        }
    }

    void UpdateSearchPlaceholder()
    {
        if (searchPlaceholder == null || searchField == null)
            return;
        searchPlaceholder.style.display =
            string.IsNullOrEmpty(searchField.value) ? DisplayStyle.Flex : DisplayStyle.None;
    }

    void Update()
    {
        if (isOpen)
        {
            if (cancelAction != null && cancelAction.WasPressedThisFrame())
                Hide();
            return;
        }

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

    void Show()
    {
        if (isOpen || overlayObject == null)
            return;
        isOpen = true;
        openedAt = Time.unscaledTime;
        overlayObject.SetActive(true);

        // Fresh tree after every activation — reset to menu and (re)wire it.
        if (menuTemplate != null)
            overlayDoc.visualTreeAsset = menuTemplate;
        WireMenuView();

        prevCursorVisible = UnityEngine.Cursor.visible;
        prevLock = UnityEngine.Cursor.lockState;
        UnityEngine.Cursor.visible = true;
        UnityEngine.Cursor.lockState = CursorLockMode.None;

        if (mover != null)
        {
            prevMoverEnabled = mover.enabled;
            mover.enabled = false;
        }
        if (footsteps != null)
        {
            prevStepsEnabled = footsteps.enabled;
            footsteps.enabled = false;
        }
        if (hover != null)
        {
            hover.enabled = false;
            HidePromptIcon();
        }
        if (outline != null)
            outline.Hide();
    }

    void Hide()
    {
        if (!isOpen)
            return;
        isOpen = false;
        if (overlayObject != null)
            overlayObject.SetActive(false);

        UnityEngine.Cursor.visible = prevCursorVisible;
        UnityEngine.Cursor.lockState = prevLock;

        if (mover != null)
            mover.enabled = prevMoverEnabled;
        if (footsteps != null)
            footsteps.enabled = prevStepsEnabled;
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

    /// <summary>Stub menu handler: logs clicks until real functionality lands.</summary>
    void RegisterMenuButton(string elementName, string label)
    {
        if (overlayDoc == null || overlayDoc.rootVisualElement == null)
            return;
        var button = overlayDoc.rootVisualElement.Q<Button>(elementName);
        if (button == null)
        {
            Debug.LogWarning("[PCUI] Menu button '" + elementName + "' missing in overlay template.", this);
            return;
        }
        button.RegisterCallback<ClickEvent>(_ => Debug.Log("[PCUI] " + label + " clicked"));
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
        var computer = Object.FindFirstObjectByType<ComputerInteractable>();
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
