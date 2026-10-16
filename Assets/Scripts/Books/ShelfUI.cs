using System.Linq;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

/// <summary>
/// Open-book shelf screen. Left page: the shelf's titles and stock with Take buttons.
/// Right page: the book in your hands with Put Back. Lives on the GameUI object.
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
    PlayerCarry carry;

    public bool IsOpen => shelf != null;

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
        note.text = "";
        Refresh();
    }

    public void Close()
    {
        if (!IsOpen) return;
        shelf = null;
        root.AddToClassList("hidden");
        PlayerLock.Unlock(this);
    }

    void Update()
    {
        if (IsOpen && cancelAction != null && cancelAction.WasPressedThisFrame()) Close();
    }

    void Refresh()
    {
        title.text = shelf.displayName;
        list.Clear();
        var books = shelf.Books.ToList();
        if (books.Count == 0) list.Add(Row("This shelf is empty.", ""));
        foreach (var b in books)
        {
            var book = b;
            string sub = $"{book.Author}  ·  x{book.Quantity}";
            if (book.WornCopies > 0 || book.DamagedCopies > 0) sub += $"  ·  {book.ConditionSummary}";
            if (book.Tier != BookTier.Normal) sub += "  ·  " + book.Tier;
            var row = Row(book.Title, sub);

            // Main Take grabs the best copy; extra buttons let you pick a worn/damaged one on purpose.
            // Only greyed out when there are no copies; full hands are handled in Take().
            var best = book.BestCopy;
            row.Add(TakeButton("Take", best.HasValue, () => { if (book.BestCopy.HasValue) Take(book, book.BestCopy.Value); }));
            if (book.GoodCopies > 0 && book.WornCopies > 0)
                row.Add(TakeButton("worn", true, () => Take(book, BookCondition.Worn), "small"));
            if (book.DamagedCopies > 0 && (book.GoodCopies > 0 || book.WornCopies > 0))
                row.Add(TakeButton("dmg", true, () => Take(book, BookCondition.Damaged), "small"));
            list.Add(row);
        }

        var held = carry.Carried;
        if (held != null && string.IsNullOrEmpty(note.text))
            note.text = shelf.Belongs(held)
                ? $"Holding \"{held.Title}\" - Take swaps it back onto this shelf."
                : $"Hands full: \"{held.Title}\" goes in {ShelfInteractable.NameOf(held.ShelfLocation)}.";
        handInfo.EnableInClassList("empty", held == null);
        handTitle.text = held != null ? held.Title : "Hands empty";
        handSub.text = held != null ? $"{held.Author}\n{held.Subject}  ·  {carry.CarriedCondition} copy" : "Take a book from the left page.";
        putBack.SetEnabled(held != null);
    }

    VisualElement Row(string main, string sub)
    {
        var row = new VisualElement();
        row.AddToClassList("shelf-row");
        var text = new VisualElement();
        text.AddToClassList("shelf-row-text");
        text.Add(new Label(main) { name = "row-title" });
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
        if (!carry.IsEmpty)
        {
            var held = carry.Carried;
            if (!shelf.Belongs(held))
            {
                note.text = $"Your hands are full. Put \"{held.Title}\" back in {ShelfInteractable.NameOf(held.ShelfLocation)} first.";
                return;
            }
            // Same shelf: swap the held copy back in, then take the new one.
            var heldCondition = carry.CarriedCondition;
            carry.Drop();
            held.AddCopy(heldCondition);
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
        var held = carry.Carried;
        if (held == null) return;
        if (!shelf.Belongs(held))
        {
            note.text = $"That belongs in {ShelfInteractable.NameOf(held.ShelfLocation)}.";
            return;
        }
        var condition = carry.CarriedCondition;
        carry.Drop();
        held.AddCopy(condition);
        Play(putSound);
        note.text = "";
        GameManager.Instance?.NotifyBooksChanged();
        Refresh();
    }

    void Play(AudioClip clip)
    {
        if (clip != null) sfx.PlayOneShot(clip, 0.7f);
    }
}
