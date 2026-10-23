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
    ReturnBin bin;
    int binCount;
    int cartCount = -1;
    PlayerCarry carry;

    public bool IsOpen => shelf != null || cart != null || bin != null;

    void Awake()
    {
        Instance = this;
        sfx = gameObject.AddComponent<AudioSource>();
        sfx.playOnAwake = false;
        GameSettings.MarkUi(sfx); // follows the UI sounds slider
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
        // Your hands as slots (replaces the single empty frame).
        slots = new VisualElement();
        slots.AddToClassList("hand-slots");
        var frame = handInfo.Q(className: "hand-book");
        if (frame != null) frame.style.display = DisplayStyle.None;
        handInfo.Insert(0, slots);
        // The back cover of the book you picked out (click a row).
        aboutBox = new VisualElement();
        aboutBox.AddToClassList("about-box");
        var head = new VisualElement();
        head.AddToClassList("about-head");
        var scroll = new VisualElement();
        scroll.AddToClassList("about-icon");
        head.Add(scroll);
        aboutTitle = new Label();
        aboutTitle.AddToClassList("about-title");
        head.Add(aboutTitle);
        aboutBox.Add(head);
        about = new Label { name = "shelf-about" };
        about.AddToClassList("shelf-about");
        aboutBox.Add(about);
        aboutTags = new VisualElement();
        aboutTags.AddToClassList("about-tags");
        aboutBox.Add(aboutTags);
        note.parent.Insert(note.parent.IndexOf(note), aboutBox);
        putBack = doc.Q<Button>("put-back");
        doc.Q<Button>("shelf-close")?.RegisterCallback<ClickEvent>(_ => Close());
        putBack?.RegisterCallback<ClickEvent>(_ => PutBack());
        // Like the computer: a click on the dim backdrop outside the book closes it (not the click that opened it).
        root?.RegisterCallback<ClickEvent>(e => { if (e.target == root && Time.unscaledTime - openedAt > 0.3f) Close(); });
        root?.AddToClassList("hidden");
    }

    float openedAt;

    public void Open(ShelfInteractable target, PlayerCarry playerCarry)
    {
        if (target == null || playerCarry == null || root == null || IsOpen) return;
        shelf = target;
        carry = playerCarry;
        PlayerLock.Lock(this);
        root.RemoveFromClassList("hidden");
        openedAt = Time.unscaledTime;
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
        openedAt = Time.unscaledTime;
        GameAudio.Play(a => a.panelOpen, 0.6f);
        note.text = ""; picked = null; ShowAbout();
        Refresh();
    }

    /// <summary>The return bin's page: every copy that came back, its condition, and where it goes next.</summary>
    public void OpenBin(ReturnBin target, PlayerCarry playerCarry)
    {
        if (target == null || playerCarry == null || root == null || IsOpen) return;
        bin = target;
        carry = playerCarry;
        PlayerLock.Lock(this);
        root.RemoveFromClassList("hidden");
        openedAt = Time.unscaledTime;
        GameAudio.Play(a => a.panelOpen, 0.6f);
        note.text = ""; picked = null; ShowAbout();
        Refresh();
    }

    public void Close()
    {
        if (!IsOpen) return;
        shelf = null;
        cart = null;
        bin = null;
        GameAudio.Play(a => a.panelClose, 0.6f);
        root.AddToClassList("hidden");
        PlayerLock.Unlock(this);
    }

    void Update()
    {
        if (IsOpen && cancelAction != null && cancelAction.WasPressedThisFrame()) Close();
        // An express order can arrive while the cart is open.
        else if (cart != null && cart.Count != cartCount) Refresh();
        // Staff take returns, and visitors bring them, while the bin is open.
        else if (bin != null && bin.Count != binCount) Refresh();
    }

    void Refresh()
    {
        if (cart != null) { RefreshCart(); return; }
        if (bin != null) { RefreshBin(); return; }
        title.text = shelf.displayName;
        list.Clear();
        var books = shelf.Books.ToList();
        if (books.Count == 0) list.Add(Row("This shelf is empty.", ""));
        foreach (var b in books)
        {
            var book = b;
            var row = BookRow(book);
            Pickable(row, book);

            // Main Take grabs the best copy; extra buttons let you pick a worn/damaged one on purpose.
            // Only greyed out when there are no copies; full hands are handled in Take().
            var best = book.BestCopy;
            // Buttons stacked in one column so the title keeps its width.
            var buttons = new VisualElement();
            buttons.AddToClassList("take-col");
            // Only damaged copies left: the button says so.
            bool onlyDamaged = best == BookCondition.Damaged;
            bool allowed = Tutorial.AllowsBook(book); // the tutorial: only its one book
            buttons.Add(TakeButton(onlyDamaged ? "Damaged" : "Take", best.HasValue && allowed, () => { if (book.BestCopy.HasValue) Take(book, book.BestCopy.Value); }, onlyDamaged ? "take-damaged" : null));
            if (book.GoodCopies > 0 && book.WornCopies > 0 && !Tutorial.HoldsClock)
                buttons.Add(TakeButton("Worn", true, () => Take(book, BookCondition.Worn)));
            if (book.DamagedCopies > 0 && (book.GoodCopies > 0 || book.WornCopies > 0) && !Tutorial.HoldsClock)
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
            buttons.Add(TakeButton("Take", Tutorial.AllowsBook(book), () => TakeFromCart(book)));
            row.Add(buttons);
            list.Add(row);
        }
        int back = 0;
        foreach (var (b, c) in carry.Items) if (c == BookCondition.Good && cart.CameFromCart(b)) back++;
        HandPage(back);
    }

    /// <summary>Returns, newest first: one row per copy with its condition and where it goes next (its shelf, or the repair desk).</summary>
    void RefreshBin()
    {
        binCount = bin.Count;
        title.text = "Return bin";
        list.Clear();
        var copies = bin.Copies.Reverse().ToList();
        if (copies.Count == 0) list.Add(Row("The bin is empty.", "Visitors drop books here when they're done."));
        foreach (var (b, c) in copies)
        {
            var book = b; var cond = c;
            string where = cond == BookCondition.Good ? $"Goes on {ShelfInteractable.NameOf(book.ShelfLocation)}"
                         : cond == BookCondition.Worn ? $"Worn: shelve it, or mend it at the repair desk"
                         : "<color=#B8322A>Damaged: take it to the repair desk</color>";
            var row = Row(book.Title, $"{book.Author}  ·  {where}");
            row.EnableInClassList("has-damage", cond == BookCondition.Damaged);
            Pickable(row, book);
            var buttons = new VisualElement();
            buttons.AddToClassList("take-col");
            buttons.Add(TakeButton(cond == BookCondition.Good ? "Take" : cond.ToString(), true, () => TakeFromBin(book, cond), cond == BookCondition.Damaged ? "take-damaged" : null));
            row.Add(buttons);
            list.Add(row);
        }
        int back = 0;
        foreach (var (b, c) in carry.Items) if (bin.CameFromBin(b, c)) back++;
        HandPage(back);
    }

    void TakeFromBin(BookData book, BookCondition condition)
    {
        if (carry.IsFull) { note.text = "Your hands are full. Hand a book over or shelve it first."; Refresh(); return; }
        if (!bin.Take(book, condition)) { note.text = "Someone already took that one."; Refresh(); return; }
        carry.PickUp(book, condition);
        Play(takeSound);
        note.text = "";
        Refresh();
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
        // One slot per book you can carry: a pixel book (its colour shows the condition) or an empty outline.
        slots.Clear();
        for (int i = 0; i < Mathf.Max(1, carry.Capacity); i++)
        {
            // Each slot: the box with the book, and the book's name under it.
            var cell = new VisualElement();
            cell.AddToClassList("hand-cell");
            var slot = new VisualElement();
            slot.AddToClassList("hand-slot");
            var icon = new VisualElement();
            icon.AddToClassList("hand-slot-book");
            var name = new Label();
            name.AddToClassList("hand-slot-name");
            if (i < carry.Count)
            {
                var (b, c) = carry.Items[carry.Count - 1 - i]; // top book first
                icon.EnableInClassList("worn", c == BookCondition.Worn);
                icon.EnableInClassList("damaged", c == BookCondition.Damaged);
                name.text = c == BookCondition.Good ? b.Title : $"{b.Title} ({c.ToString().ToLowerInvariant()})";
            }
            else icon.AddToClassList("empty");
            slot.Add(icon);
            cell.Add(slot);
            cell.Add(name);
            slots.Add(cell);
        }
        // The names are under the slots: the lines below only explain empty hands, or the bag count.
        handTitle.text = held != null ? "" : "Hands empty";
        handTitle.EnableInClassList("hidden", held != null);
        handSub.text = held != null
            ? (carry.Capacity > 1 ? $"{carry.Count}/{carry.Capacity} in your hands" : "")
            : carry.Capacity > 1 ? $"Take books from the left page. You can carry {carry.Capacity}." : "Take a book from the left page.";
        putBack.text = belongs > 1 ? $"Put back ({belongs})" : "Put back";
        putBack.SetEnabled(belongs > 0);
    }

    Label about, aboutTitle;
    VisualElement aboutBox, aboutTags, slots;
    BookData picked;

    /// <summary>Clicking a row (not its buttons) shows what the book is about on the right page.</summary>
    void Pickable(VisualElement row, BookData book)
    {
        row.EnableInClassList("picked", book == picked);
        row.RegisterCallback<ClickEvent>(e =>
        {
            for (var t = e.target as VisualElement; t != null && t != row; t = t.parent) if (t is Button) return;
            if (picked != book) GameAudio.BookSelect();
            picked = book;
            ShowAbout();
            Refresh();
        });
    }

    /// <summary>The back cover of the book you clicked: what it's about, its tags and type, and whether it's for sale.</summary>
    void ShowAbout()
    {
        if (aboutBox == null) return;
        aboutTags.Clear();
        if (picked == null)
        {
            aboutTitle.text = "Back cover";
            about.text = "Click a book on the left to read about it.";
            about.AddToClassList("dim");
            return;
        }
        about.RemoveFromClassList("dim");
        aboutTitle.text = picked.Title;
        about.text = string.IsNullOrEmpty(picked.Abstract) ? "No description." : picked.Abstract;
        void Chip(string text, string cls = null)
        {
            var l = new Label(text);
            l.AddToClassList("about-chip");
            if (cls != null) l.AddToClassList(cls);
            aboutTags.Add(l);
        }
        Chip(picked.TypeName);
        if (!string.IsNullOrEmpty(picked.SubjectName)) Chip(picked.SubjectName);
        foreach (var t in (picked.Tag ?? "").Split(',').Select(t => t.Trim()).Where(t => t.Length > 0)) Chip(t);
        if (picked.Tier != BookTier.Normal) Chip(picked.Tier.ToString(), picked.Tier == BookTier.Epic ? "epic" : "legendary");
        if (!picked.Sellable) Chip("Read here only", "warn");
    }

    /// <summary>A shelf row: the title (with Popular / Reserved), author and subject, then one dot per copy
    /// (green good, amber worn, red damaged), the price and anything on order. The left edge shows the tier.</summary>
    VisualElement BookRow(BookData book)
    {
        var gm = GameManager.Instance;
        var res = gm != null ? gm.reservations.Find(r => r.title == book.Title) : null;
        var row = Row(book.Title, $"{book.Author}  ·  {book.SubjectName}", gm != null && gm.IsPopular(book));
        row.AddToClassList("book-spine");
        row.EnableInClassList("epic", book.Tier == BookTier.Epic);
        row.EnableInClassList("legendary", book.Tier == BookTier.Legendary);
        row.EnableInClassList("has-damage", book.DamagedCopies > 0);
        var text = row.Q(className: "shelf-row-text");
        if (res != null)
        {
            var tag = new Label($"Reserved for {res.name}");
            tag.AddToClassList("row-tag");
            text.Insert(1, tag);
        }
        var meta = new VisualElement();
        meta.AddToClassList("row-meta");
        // Copies, one dot each (capped, with the rest as a number).
        var pips = new VisualElement();
        pips.AddToClassList("row-pips");
        int shown = 0;
        void Pips(int n, string cls) { for (int i = 0; i < n && shown < 8; i++, shown++) { var p = new VisualElement(); p.AddToClassList("cpip"); if (cls != null) p.AddToClassList(cls); pips.Add(p); } }
        Pips(book.GoodCopies, null);
        Pips(book.WornCopies, "worn");
        Pips(book.DamagedCopies, "damaged");
        meta.Add(pips);
        var count = new Label(book.Quantity == 0 ? "none left" : $"x{book.Quantity}"); // always the number, like the computer's catalogue
        count.AddToClassList("row-meta-text");
        count.EnableInClassList("bad", book.Quantity == 0);
        meta.Add(count);
        var coin = new VisualElement();
        coin.AddToClassList(book.Sellable ? "row-coin" : "row-lock");
        meta.Add(coin);
        var price = new Label(book.Sellable ? book.SaleValue.ToString() : "read here");
        price.AddToClassList("row-meta-text");
        meta.Add(price);
        int coming = gm != null ? gm.OnOrder(book) : 0;
        if (coming > 0)
        {
            var clock = new VisualElement();
            clock.AddToClassList("row-clock");
            meta.Add(clock);
            var c = new Label($"+{coming} coming");
            c.AddToClassList("row-meta-text");
            meta.Add(c);
        }
        text.Add(meta);
        return row;
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
        if (bin != null)
        {
            // Copies that came out of the bin go back in, as they were.
            int k, back = 0;
            while ((k = carry.Find((b, c) => bin.CameFromBin(b, c))) >= 0)
            {
                var (b, c) = carry.TakeAt(k);
                bin.PutBack(b, c);
                back++;
            }
            if (back > 0) { Play(putSound); note.text = ""; }
            else note.text = "Only books from the bin can go back in it.";
            Refresh();
            return;
        }
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
