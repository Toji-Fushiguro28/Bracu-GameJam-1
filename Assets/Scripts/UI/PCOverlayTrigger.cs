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
        ClickGuard.Attach(overlayDoc.rootVisualElement);
        overlayDoc.rootVisualElement.Query(className: "req-icon").ForEach(RequestIcon.Attach); // the drawn book icon
        if (container == null)
        {
            Debug.LogError("[PCUI] '" + ContainerElementName + "' element missing after view swap.", this);
            return;
        }
        container.RegisterCallback<ClickEvent>(OnBackdropClick);
        GameAudio.HookClicks(container); // the tree is rebuilt on every swap, so re-hook each time
    }

    void WireMenuView()
    {
        RehookBackdrop();
        overlayDoc.rootVisualElement.Q<Button>("members-button")?.RegisterCallback<ClickEvent>(_ => { if (JustOpened) return; ShowMembers(); });
        var shopTile = overlayDoc.rootVisualElement.Q<Button>("upgrades-button");
        var gmNow = GameManager.Instance;
        if (shopTile != null)
        {
            bool ready = gmNow != null && gmNow.UpgradeReady;
            var shopLabel = shopTile.Q<Label>("tile-label");
            if (shopLabel != null) shopLabel.text = "Shop";
            // "Upgrade ready!" sits under the tile's icon + name, so the coin stays beside "Shop".
            var note = shopTile.Q<Label>("tile-note");
            if (note == null)
            {
                note = new Label { name = "tile-note", pickingMode = PickingMode.Ignore };
                note.AddToClassList("pc-tile-note");
                shopTile.Add(note);
            }
            bool hire = gmNow != null && gmNow.StaffReady;
            note.text = ready ? "★ Upgrade ready!" : "★ Staff to hire or upgrade!";
            note.EnableInClassList("hidden", !ready && !hire);
            PopularGlow.Ready(shopTile, ready || hire);
            shopTile.EnableInClassList("upgrade-ready", ready);
        }
        overlayDoc.rootVisualElement.Q<Button>("upgrades-button")?.RegisterCallback<ClickEvent>(_ => { if (JustOpened) return; ShowView("PCShopTemplate", PCShopView.Wire); });
        overlayDoc.rootVisualElement.Q<Button>("settings-button")?.RegisterCallback<ClickEvent>(_ => { if (JustOpened) return; ShowView("PCSettingsTemplate", PCSettingsView.Wire); });
        overlayDoc.rootVisualElement.Q<Button>("audit-button")?.RegisterCallback<ClickEvent>(_ => { if (JustOpened) return; ShowView("PCAuditTemplate", PCAuditView.Wire); });

        var books = overlayDoc.rootVisualElement.Q<Button>("books-button");
        if (books == null)
        {
            Debug.LogWarning("[PCUI] Menu button 'books-button' missing in overlay template.", this);
            return;
        }
        if (booksAvailable)
            books.RegisterCallback<ClickEvent>(_ => { if (JustOpened) return; ShowBooks(); });
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

    void ShowMembers() => ShowView("PCMembersTemplate", PCMembersView.Wire);

    /// <summary>Swaps the screen to a Resources template and lets its view wire itself up.</summary>
    void ShowView(string templateName, System.Action<VisualElement, System.Action> wire)
    {
        var template = Resources.Load<VisualTreeAsset>(templateName);
        if (template == null || overlayDoc == null) return;
        overlayDoc.visualTreeAsset = template;
        RehookBackdrop();
        wire(overlayDoc.rootVisualElement, ShowMenu);
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
            booksList.makeItem = () =>
            {
                var row = bookItemTemplate.CloneTree();
                // Clicking a row always shows it, even if the list thinks it's already selected.
                row.RegisterCallback<ClickEvent>(_ => { if (row.userData is BookData rb) ShowDetails(rb); });
                PopularGlow.Pulse(row); // rows are reused, so the pulse runs on every row and only shows on popular ones
                return row;
            };
            booksList.bindItem = BindBookItem;
            booksList.fixedItemHeight = 100;
            booksList.selectionType = SelectionType.Single;
            booksList.selectionChanged += sel => ShowDetails(sel.OfType<BookData>().FirstOrDefault());
        }

        WireFilters(root);
        // "Add to note": the picked book (and its shelf) goes in the notebook.
        root.Q<Button>("detail-select")?.RegisterCallback<ClickEvent>(_ =>
        {
            RequestLog.AddToNote(selectedBook);
            UpdateSelectButton();
        });
        ApplySearchFilter();
        UpdateSearchPlaceholder();
        ShowDetails(null);
    }

    BookData selectedBook;

    /// <summary>"Add to note" goes under the open book request, or on the "To Shelve" page.</summary>
    void UpdateSelectButton()
    {
        var root = overlayDoc.rootVisualElement;
        var button = root.Q<Button>("detail-select");
        var note = root.Q<Label>("detail-select-note");
        bool noted = RequestLog.IsNoted(selectedBook);
        button?.SetEnabled(selectedBook != null && !noted);
        if (note != null)
            note.text = noted ? "Already in your notebook."
                      : RequestLog.Current != null ? "Adds it to the visitor's request in your notebook."
                      : "Nobody's asking: adds it to your To Shelve list.";
    }

    /// <summary>Right-hand pane: where the selected book lives and its state.</summary>
    void ShowDetails(BookData b)
    {
        selectedBook = b;
        var root = overlayDoc.rootVisualElement;
        var empty = root.Q("detail-empty");
        var body = root.Q("detail-body");
        if (empty == null || body == null)
            return;
        empty.style.display = b == null ? DisplayStyle.Flex : DisplayStyle.None;
        body.style.display = b == null ? DisplayStyle.None : DisplayStyle.Flex;
        if (b == null)
            return;

        void Set(string name, string text, string state = null)
        {
            var l = root.Q<Label>(name);
            if (l == null) return;
            l.text = text;
            l.EnableInClassList("warn", state == "warn");
            l.EnableInClassList("bad", state == "bad");
        }
        Set("detail-title", b.Title);
        Set("detail-author", "by " + b.Author);
        Set("detail-shelf", ShelfInteractable.NameOf(b.ShelfLocation));
        Set("detail-stock", b.Quantity > 0 ? b.Quantity + " on shelf" : "none left", b.Quantity > 0 ? null : "bad");
        Set("detail-type", b.FormatName);
        Set("detail-subject", b.Subject);
        Set("detail-tags", string.IsNullOrEmpty(b.Tag) ? "-" : b.Tag);
        bool popular = GameManager.Instance != null && GameManager.Instance.IsPopular(b);
        var detailTitle = overlayDoc.rootVisualElement.Q<Label>("detail-title");
        detailTitle?.EnableInClassList("popular-title", popular);
        if (detailTitle != null && popular) detailTitle.text = b.Title + "  <color=#EBC878>(Popular today)</color>";
        Set("detail-tier", b.Rarity, b.Tier != BookTier.Normal ? "warn" : null);
        Set("detail-condition", b.Quantity > 0 ? b.ConditionSummary : "-", b.DamagedCopies > 0 ? "bad" : b.WornCopies > 0 ? "warn" : null);
        Set("detail-sale", b.Sellable ? "can be sold" : "read in library only", b.Sellable ? null : "warn");
        Set("detail-abstract", b.Abstract);
        UpdateSelectButton();
    }

    // Search only knows titles and authors; subject and type are filters. Abstracts and tags
    // are read in the details pane, so vague requests take a little detective work.
    static readonly string[] SubjectFilters = { "All", "Science", "History", "Philosophy", "Math" };
    static int subjectFilter, typeFilter, showFilter;

    void ApplySearchFilter()
    {
        string query = searchField != null ? searchField.value : "";
        string needle = (query ?? "").Trim().ToLowerInvariant();
        string subject = SubjectFilters[subjectFilter];
        filteredBooks = allBooks.Where(b =>
            (needle.Length == 0 || (b.Title ?? "").ToLowerInvariant().Contains(needle) || (b.Author ?? "").ToLowerInvariant().Contains(needle) || (b.Tag ?? "").ToLowerInvariant().Contains(needle)) &&
            (subjectFilter == 0 || b.Subject == subject) &&
            (typeFilter == 0 || (int)b.Format == typeFilter - 1) &&
            BookFilterBar.ShowMatches(showFilter, b)).ToList();
        RefreshBooksList();
    }

    void WireFilters(VisualElement root)
    {
        var subjectButton = root.Q<Button>("filter-subject");
        var typeButton = root.Q<Button>("filter-type");
        var types = System.Enum.GetValues(typeof(BookFormat)).Cast<BookFormat>().ToArray();
        string TypeName(int i) => i == 0 ? "All" : new BookData { Format = types[i - 1] }.FormatName;
        var typeNames = Enumerable.Range(0, types.Length + 1).Select(TypeName).ToArray();
        // "Show: Reserved" etc., next to the other two (same look).
        Button showButton = null;
        if (typeButton != null)
        {
            showButton = new Button { name = "filter-show" };
            foreach (var c in typeButton.GetClasses()) showButton.AddToClassList(c);
            typeButton.parent.Insert(typeButton.parent.IndexOf(typeButton) + 1, showButton);
        }
        void UpdateLabels()
        {
            if (subjectButton != null) subjectButton.text = "Subject: " + SubjectFilters[subjectFilter];
            if (typeButton != null) typeButton.text = "Type: " + TypeName(typeFilter);
            if (showButton != null) showButton.text = "Show: " + BookFilterBar.Shows[showFilter];
        }
        subjectButton?.RegisterCallback<ClickEvent>(_ => PickList.Open(subjectButton, SubjectFilters, subjectFilter, i => { subjectFilter = i; UpdateLabels(); ApplySearchFilter(); }, true));
        typeButton?.RegisterCallback<ClickEvent>(_ => PickList.Open(typeButton, typeNames, typeFilter, i => { typeFilter = i; UpdateLabels(); ApplySearchFilter(); }, true));
        showButton?.RegisterCallback<ClickEvent>(_ => PickList.Open(showButton, BookFilterBar.Shows, showFilter, i => { showFilter = i; UpdateLabels(); ApplySearchFilter(); }, true));
        UpdateLabels();
    }

    void RefreshBooksList()
    {
        if (booksList == null)
            return;
        // A new search: drop the old row index (it pointed at a different book) but keep the selected
        // book highlighted if it's still in the results.
        booksList.SetSelectionWithoutNotify(System.Array.Empty<int>());
        booksList.itemsSource = filteredBooks;
        booksList.Rebuild();
        int keep = selectedBook != null ? filteredBooks.IndexOf(selectedBook) : -1;
        if (keep >= 0) booksList.SetSelectionWithoutNotify(new[] { keep });
    }

    /// <summary>Minimal row: title, author/subject, tag/qty/shelf/rarity (+ damaged flag).</summary>
    void BindBookItem(VisualElement el, int i)
    {
        if (i < 0 || i >= filteredBooks.Count)
            return;
        BookData b = filteredBooks[i];
        el.userData = b;
        bool popular = GameManager.Instance != null && GameManager.Instance.IsPopular(b);
        el.EnableInClassList("popular", popular);
        var title = el.Q<Label>("book-title");
        var sub = el.Q<Label>("book-sub");
        var meta = el.Q<Label>("book-meta");
        if (title != null)
            title.text = b.Title;
        if (sub != null)
            sub.text = (b.Format == BookFormat.Book ? "" : b.FormatName + "  ·  ") + b.Author + "  ·  " + b.Subject + "  ·  " + ShelfInteractable.NameOf(b.ShelfLocation);
        if (meta != null)
        {
            string m = (popular ? "<color=#EBC878>Popular</color>  ·  " : "") + "x" + b.Quantity + "  ·  " + ShelfInteractable.NameOf(b.ShelfLocation);
            if (b.Tier != BookTier.Normal)
                m += "  ·  " + b.Rarity;
            if (b.DamagedCopies > 0)
                m += "  ·  " + b.DamagedCopies + " damaged";
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
    public void ForceClose() => Hide();

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
        subjectFilter = typeFilter = showFilter = 0; // the catalogue opens on All books every time
        PlayerLock.Lock(this);
        openedAt = Time.unscaledTime;
        overlayObject.SetActive(true);
        GameAudio.Play(a => a.pcOpen, 0.7f);

        // Fresh tree after every activation — reset to menu and (re)wire it.
        if (menuTemplate != null)
            overlayDoc.visualTreeAsset = menuTemplate;
        WireMenuView();

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

    void Hide()
    {
        if (!isOpen)
            return;
        isOpen = false;
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
        button.RegisterCallback<ClickEvent>(_ => { if (JustOpened) return; Debug.Log("[PCUI] " + label + " clicked"); });
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
