using System.Linq;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

/// <summary>
/// Open-book shelf screen. Left page: the shelf's titles and stock with Take buttons.
/// Right page: the book in your hands with Put Back. Lives on the GameUI object.
/// The delivery cart uses the same screen (OpenCart): what arrived, and who it's reserved for.
/// </summary>
[RequireComponent(typeof(UIDocument))]
public class ShelfUI : MonoBehaviour
{
    public static ShelfUI Instance { get; private set; }

    public AudioClip takeSound;
    public AudioClip putSound;

    VisualElement root, list, handInfo;
    Label title, handTitle, handSub, note;
    Button putBack;
    InputAction cancelAction;
    AudioSource sfx;

    ShelfInteractable shelf;
    DeliveryCart cart;
    int cartCount = -1;
    PlayerCarry carry;

    public bool IsOpen => shelf != null || cart != null;

    void Awake()
    {
        Instance = this;
        sfx = gameObject.AddComponent<AudioSource>();
        sfx.playOnAwake = false;
        cancelAction = InputSystem.actions?.FindAction("UI/Cancel");
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
        PlayerLock.Unlock(this);
    }

    void OnEnable()
    {
        cancelAction?.Enable();
        var doc = GetComponent<UIDocument>().rootVisualElement;
        root = doc.Q("shelf");
        list = doc.Q("shelf-list");
        title = doc.Q<Label>("shelf-title");
        handInfo = doc.Q("hand-info");
        handTitle = doc.Q<Label>("hand-title");
        handSub = doc.Q<Label>("hand-sub");
        note = doc.Q<Label>("shelf-note");
        // What the book you picked out is about (click a row).
        about = new Label { name = "shelf-about" };
        about.AddToClassList("shelf-about");
        note.parent.Insert(note.parent.IndexOf(note), about);
        putBack = doc.Q<Button>("put-back");
        doc.Q<Button>("shelf-close")?.RegisterCallback<ClickEvent>(_ => Close());
        putBack?.RegisterCallback<ClickEvent>(_ => PutBack());
        root?.AddToClassList("hidden");
    }

    public void Open(ShelfInteractable target, PlayerCarry playerCarry)
    {
        if (target == null || playerCarry == null || root == null || IsOpen) return;
        shelf = target;
        carry = playerCarry;
        PlayerLock.Lock(this);
        root.RemoveFromClassList("hidden");
        GameAudio.Play(a => a.panelOpen, 0.6f);
        note.text = ""; picked = null; ShowAbout(); // a fresh shelf: nothing picked yet
        Refresh();
    }

    /// <summary>The delivery cart's page: take the copies you need, or put them back.</summary>
    public void OpenCart(DeliveryCart target, PlayerCarry playerCarry)
    {
        if (target == null || playerCarry == null || root == null || IsOpen) return;
        cart = target;
        carry = playerCarry;
        PlayerLock.Lock(this);
        root.RemoveFromClassList("hidden");
        GameAudio.Play(a => a.panelOpen, 0.6f);
        note.text = ""; picked = null; ShowAbout();
        Refresh();
    }

    public void Close()
    {
        if (!IsOpen) return;
        shelf = null;
        cart = null;
        GameAudio.Play(a => a.panelClose, 0.6f);
        root.AddToClassList("hidden");
        PlayerLock.Unlock(this);
    }

    void Update()
    {
        if (IsOpen && cancelAction != null && cancelAction.WasPressedThisFrame()) Close();
        // An express order can arrive while the cart is open.
        else if (cart != null && cart.Count != cartCount) Refresh();
    }

    void Refresh()
    {
        if (cart != null) { RefreshCart(); return; }
        title.text = shelf.displayName;
        list.Clear();
        var books = shelf.Books.ToList();
        if (books.Count == 0) list.Add(Row("This shelf is empty.", ""));
        foreach (var b in books)
        {
            var book = b;
            string sub = $"{book.Author}  ·  x{book.Quantity}";
            if (book.WornCopies > 0) sub += $"  ·  {book.WornCopies} worn";
            if (book.DamagedCopies > 0) sub += $"  ·  <color=#B8322A>{book.DamagedCopies} damaged</color>";
            if (book.Tier != BookTier.Normal) sub += "  ·  " + book.Tier;
            var row = Row(book.Title, sub, GameManager.Instance != null && GameManager.Instance.IsPopular(book));
            row.EnableInClassList("has-damage", book.DamagedCopies > 0);
            Pickable(row, book);

            // Main Take grabs the best copy; extra buttons let you pick a worn/damaged one on purpose.
            // Only greyed out when there are no copies; full hands are handled in Take().
            var best = book.BestCopy;
            // Buttons stacked in one column so the title keeps its width.
            var buttons = new VisualElement();
            buttons.AddToClassList("take-col");
            buttons.Add(TakeButton("Take", best.HasValue, () => { if (book.BestCopy.HasValue) Take(book, book.BestCopy.Value); }));
            if (book.GoodCopies > 0 && book.WornCopies > 0)
                buttons.Add(TakeButton("Worn", true, () => Take(book, BookCondition.Worn)));
            if (book.DamagedCopies > 0 && (book.GoodCopies > 0 || book.WornCopies > 0))
                buttons.Add(TakeButton("Damaged", true, () => Take(book, BookCondition.Damaged), "take-damaged"));
            row.Add(buttons);
            list.Add(row);
        }

        var held = carry.Carried;
        int belongs = 0;
        foreach (var (b, _) in carry.Items) if (shelf.Belongs(b)) belongs++;
        if (held != null && string.IsNullOrEmpty(note.text))
            note.text = belongs > 0 ? (belongs == 1 ? "One of your books goes on this shelf." : $"{belongs} of your books go on this shelf.")
                : carry.IsFull ? $"Hands full: \"{held.Title}\" doesn't go on this shelf. Check the computer."
                : "";
        HandPage(belongs);
    }

    /// <summary>Deliveries, one row per title: how many came, who reserved it, and a Take button.</summary>
    void RefreshCart()
    {
        cartCount = cart.Count;
        title.text = "Delivery cart";
        list.Clear();
        var gm = GameManager.Instance;
        var titles = cart.Copies.Distinct()
            .OrderBy(b => gm != null && gm.reservations.Exists(r => r.title == b.Title) ? 0 : 1).ThenBy(b => b.Title).ToList();
        if (titles.Count == 0) list.Add(Row("The cart is empty.", "Order books on the computer (Shop)."));
        var atDesk = FindObjectsByType<Customer>()
            .Where(c => c.IsWaiting && c.CurrentRequest != null && c.CurrentRequest.Target != null)
            .Select(c => c.CurrentRequest.Target).ToList();
        foreach (var b in titles)
        {
            var book = b;
            int n = cart.Copies.Count(x => x == book);
            var res = gm != null ? gm.reservations.Find(r => r.title == book.Title) : null;
            string sub = $"{book.Author}  ·  x{n}  ·  ";
            sub += atDesk.Contains(book) ? "<color=#D67840>Someone at the desk wants this!</color>"
                 : res != null ? $"Reserved for {res.name}{(res.urgent ? " (needs it today)" : "")}"
                 : $"Goes on {ShelfInteractable.NameOf(book.ShelfLocation)}";
            var row = Row(book.Title, sub, gm != null && gm.IsPopular(book));
            Pickable(row, book);
            var buttons = new VisualElement();
            buttons.AddToClassList("take-col");
            buttons.Add(TakeButton("Take", true, () => TakeFromCart(book)));
            row.Add(buttons);
            list.Add(row);
        }
        int back = 0;
        foreach (var (b, c) in carry.Items) if (c == BookCondition.Good && cart.CameFromCart(b)) back++;
        HandPage(back);
    }

    void TakeFromCart(BookData book)
    {
        if (carry.IsFull) { note.text = "Your hands are full. Hand a book over or shelve it first."; Refresh(); return; }
        if (!cart.Take(book)) { Refresh(); return; }
        carry.PickUp(book, BookCondition.Good);
        Play(takeSound);
        note.text = "";
        Refresh();
    }

    /// <summary>Right page: what's in your hands, and Put back for the <paramref name="belongs"/> books that go here.</summary>
    void HandPage(int belongs)
    {
        var held = carry.Carried;
        handInfo.EnableInClassList("empty", held == null);
        handTitle.text = held != null ? held.Title : "Hands empty";
        string more = "";
        for (int i = carry.Count - 2; i >= 0; i--) more += $"\n+ {carry.Items[i].book.Title}";
        handSub.text = held != null
            ? $"{held.Author}  ·  {carry.CarriedCondition} copy{more}\n({carry.Count}/{carry.Capacity} in your bag)"
            : carry.Capacity > 1 ? $"Take books from the left page. You can carry {carry.Capacity}." : "Take a book from the left page.";
        putBack.text = belongs > 1 ? $"Put back ({belongs})" : "Put back";
        putBack.SetEnabled(belongs > 0);
    }

    Label about;
    BookData picked;

    /// <summary>Clicking a row (not its buttons) shows what the book is about on the right page.</summary>
    void Pickable(VisualElement row, BookData book)
    {
        row.EnableInClassList("picked", book == picked);
        row.RegisterCallback<ClickEvent>(e =>
        {
            for (var t = e.target as VisualElement; t != null && t != row; t = t.parent) if (t is Button) return;
            picked = book;
            ShowAbout();
            Refresh();
        });
    }

    void ShowAbout()
    {
        if (about == null) return;
        about.text = picked == null ? "" : string.IsNullOrEmpty(picked.Abstract)
            ? $"{BookRequest.H(picked.Title)}: no description."
            : $"{BookRequest.H(picked.Title)}: {picked.Abstract}";
    }

    VisualElement Row(string main, string sub, bool popular = false)
    {
        var row = new VisualElement();
        row.AddToClassList("shelf-row");
        var text = new VisualElement();
        text.AddToClassList("shelf-row-text");
        text.Add(PopularGlow.TitleRow(new Label(main) { name = "row-title" }, popular, row));
        if (!string.IsNullOrEmpty(sub)) text.Add(new Label(sub) { name = "row-sub" });
        row.Add(text);
        return row;
    }

    Button TakeButton(string label, bool enabled, System.Action onClick, string extraClass = null)
    {
        var b = new Button(onClick) { text = label };
        b.AddToClassList("kit-button");
        if (extraClass != null) b.AddToClassList(extraClass);
        b.SetEnabled(enabled);
        return b;
    }

    void Take(BookData book, BookCondition condition)
    {
        if (book.Count(condition) <= 0) return;
        if (carry.IsFull)
        {
            // Full bag: swap one of this shelf's books back in, if there is one.
            int i = carry.Find((b, c) => shelf.Belongs(b));
            if (i < 0)
            {
                var held = carry.Carried;
                note.text = $"Your hands are full, and \"{held.Title}\" doesn't go here.";
                return;
            }
            var (back, backCondition) = carry.TakeAt(i);
            back.AddCopy(backCondition);
            if (book.Count(condition) <= 0) { Refresh(); return; }
        }
        if (!carry.PickUp(book, condition)) return;
        book.TakeCopy(condition);
        Play(takeSound);
        note.text = "";
        GameManager.Instance?.NotifyBooksChanged();
        Refresh();
    }

    void PutBack()
    {
        if (carry.IsEmpty) return;
        if (cart != null)
        {
            // Copies that came off the cart go back on it.
            int j, returned = 0;
            while ((j = carry.Find((b, c) => c == BookCondition.Good && cart.CameFromCart(b))) >= 0)
            {
                var (b, _) = carry.TakeAt(j);
                cart.PutBack(b);
                returned++;
            }
            if (returned > 0) { Play(putSound); note.text = ""; }
            else note.text = "Only books from the cart can go back on it.";
            Refresh();
            return;
        }
        // Shelve every book in your hands that lives here.
        int i, shelved = 0;
        while ((i = carry.Find((b, c) => shelf.Belongs(b))) >= 0)
        {
            var (book, condition) = carry.TakeAt(i);
            book.AddCopy(condition);
            RequestLog.Shelved(book);
            shelved++;
        }
        if (shelved == 0)
        {
            var held = carry.Carried;
            note.text = "That doesn't go on this shelf. Look it up on the computer.";
            return;
        }
        Play(putSound);
        note.text = "";
        GameManager.Instance?.NotifyBooksChanged();
        Refresh();
    }

    /// <summary>The book-taken sound, for other places books are picked up (e.g. the return bin).</summary>
    public void PlayTake() => Play(takeSound);

    void Play(AudioClip clip)
    {
        if (clip != null) sfx.PlayOneShot(clip, 0.7f);
    }
}
