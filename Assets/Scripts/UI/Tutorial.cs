using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Unity.Cinemachine;
using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// The guided first day (new games only, Day 1) and the one-line hints the first time something happens.
/// Tutorial: a card on the left says what to do, an arrow points at it, and the camera zooms in on things the
/// first time you need them. While it runs the clock holds (the story waits), only the tutorial's own three
/// visitors come in (a buyer, a card sign-up, someone after a book the library hasn't got), and nobody storms out.
/// Steps: talk, look it up, take it from the shelf, hand it over, issue a card, take an order, order it in the
/// Shop; then the day runs normally until closing, when it shows you how to lock up. Skip any time.
/// Hints: <see cref="Show"/> once per key per game, never while the tutorial is guiding you.
/// Added to the HUD object at runtime.
/// </summary>
public class Tutorial : MonoBehaviour
{
    public static Tutorial Instance { get; private set; }

    const string DoneKey = "TutorialDone", HintPrefix = "Hint_";

    enum Step { Waiting, Talk, LookUp, Fetch, Give, CardTalk, CardFill, OrderTalk, OrderBuy, FreePlay, LockUp, Finished }
    Step step = Step.Waiting;

    /// <summary>The tutorial is guiding you: the clock holds and only its visitors come in.</summary>
    /// <summary>A tutorial card or hint is on screen (the notification feed waits until it's gone).</summary>
    public static bool CardShowing => Instance != null && Instance.card != null && !Instance.card.ClassListContains("hidden");

    public static bool HoldsClock => Instance != null && Instance.step >= Step.Talk && Instance.step <= Step.OrderBuy;

    static GameManager GM => GameManager.Instance;

    Customer buyer, signer, seeker;
    BookData wanted;
    int signupsBefore, ordersBefore;
    readonly HashSet<string> zoomed = new HashSet<string>();

    VisualElement layer, card;
    Label title, text;
    Button skip, ok;
    WorldPrompts.Follower pointer;
    Object pointedAt;
    float hideAt;

    void Awake() => Instance = this;
    void OnDestroy() { if (Instance == this) Instance = null; }

    /// <summary>A new game (or a reset) starts with the tutorial and every hint unseen.</summary>
    public static void ResetProgress()
    {
        PlayerPrefs.DeleteKey(DoneKey);
        foreach (var key in HintKeys) PlayerPrefs.DeleteKey(HintPrefix + key);
    }

    void Start()
    {
        var hud = GetComponent<UIDocument>();
        layer = hud != null ? hud.rootVisualElement : null;
        if (layer == null) return;
        card = new VisualElement { name = "tutorial-card" };
        card.AddToClassList("tut-card");
        card.AddToClassList("hidden");
        title = new Label(); title.AddToClassList("tut-title");
        text = new Label(); text.AddToClassList("tut-text");
        var row = new VisualElement(); row.AddToClassList("tut-buttons");
        skip = new Button(Skip) { text = "Skip tutorial" }; skip.AddToClassList("kit-button"); skip.AddToClassList("tut-button");
        ok = new Button(HideCard) { text = "Got it" }; ok.AddToClassList("kit-button"); ok.AddToClassList("tut-button");
        row.Add(ok); row.Add(skip);
        card.Add(title); card.Add(text); card.Add(row);
        // On top of this panel's screens (the Day Report too), but under the black fade between days.
        // In its own full-screen layer (for the game's font and colours) that lets clicks through around the card.
        var holder = new VisualElement { name = "tutorial-layer", pickingMode = PickingMode.Ignore };
        holder.AddToClassList("layer");
        holder.Add(card);
        var fade = layer.Q("screen-fade");
        if (fade != null && fade.parent == layer) layer.Insert(layer.IndexOf(fade), holder);
        else layer.Add(holder);
    }

    // ---------------- The guided day ----------------

    bool CanStart => GM != null && GM.day == 1 && !GM.HasSave && PlayerPrefs.GetInt(DoneKey, 0) == 0
                     && !MainMenuUI.Active && GM.CurrentPhase == GameManager.Phase.Open && !PlayerLock.IsLocked;

    void Update()
    {
        if (GM == null || card == null) return;
        if (hideAt > 0f && Time.unscaledTime > hideAt) HideCard();
        if (step == Step.Waiting) { if (CanStart) Begin(); else { ShowQueuedHint(); return; } }
        if (step == Step.Finished) { ShowQueuedHint(); return; }
        if (GM.day != 1) { Finish(false); return; } // the day ended some other way
        Advance();
        KeepPointerVisible();
        if (step == Step.FreePlay || step == Step.Finished) ShowQueuedHint();
    }

    void Begin()
    {
        signupsBefore = GM.Today.signups;
        ordersBefore = GM.OrdersPlaced;
        PCOverlayTrigger.LastViewed = null;
        GoTo(Step.Talk);
    }

    /// <summary>Checks the current step and moves on when it's done (or can't be done any more).</summary>
    void Advance()
    {
        var carry = GameObject.FindWithTag("Player")?.GetComponent<PlayerCarry>();
        switch (step)
        {
            case Step.Talk:
                if (buyer == null && !SpawnBuyer()) { GoTo(Step.CardTalk); break; }
                if (Gone(buyer)) { GoTo(Step.CardTalk); break; }
                Point(buyer, "Talk");
                if (RequestLog.Has(buyer)) GoTo(Step.LookUp);
                break;
            case Step.LookUp:
                if (Gone(buyer)) { GoTo(Step.CardTalk); break; }
                Point(PC, "Computer");
                // Found one of their books in the catalogue (or already went and got it).
                if (Holding(carry)) GoTo(Step.Give);
                else if (PCOverlayTrigger.LastViewed != null && wanted != null && PCOverlayTrigger.LastViewed.Author == wanted.Author) GoTo(Step.Fetch);
                break;
            case Step.Fetch:
                if (Gone(buyer)) { GoTo(Step.CardTalk); break; }
                Point(ShelfOf(wanted), "Shelf");
                if (Holding(carry)) GoTo(Step.Give);
                break;
            case Step.Give:
                if (Gone(buyer)) { GoTo(Step.CardTalk); break; } // served (or gone): on to the next visitor
                Point(buyer, "Hand it over");
                if (!Holding(carry)) GoTo(Step.Fetch); // put it back down: go get it again
                break;
            case Step.CardTalk:
                if (signer == null && !SpawnSigner()) { GoTo(Step.OrderTalk); break; }
                if (Gone(signer) || GM.Today.signups > signupsBefore) { GoTo(Step.OrderTalk); break; }
                Point(signer, "Talk");
                if (Customer.WaitingForCard == signer) GoTo(Step.CardFill);
                break;
            case Step.CardFill:
                if (Gone(signer) || GM.Today.signups > signupsBefore) { GoTo(Step.OrderTalk); break; }
                Point(PC, "Computer");
                break;
            case Step.OrderTalk:
                if (seeker == null && !SpawnSeeker()) { GoTo(Step.OrderBuy); break; }
                if (GM.ReservationFor(seeker != null ? seeker.profileKey : "") != null) { GoTo(Step.OrderBuy); break; }
                if (Gone(seeker)) { GoTo(Step.OrderBuy); break; }
                Point(seeker, "Talk");
                break;
            case Step.OrderBuy:
                Point(PC, "Computer");
                if (GM.OrdersPlaced > ordersBefore) GoTo(Step.FreePlay);
                break;
            case Step.FreePlay:
                if (GM.CurrentPhase != GameManager.Phase.Open) GoTo(Step.LockUp);
                break;
            case Step.LockUp:
                Point(Door, "Lock up");
                if (DayReportUI.Instance != null && DayReportUI.Instance.IsOpen) Finish(true);
                break;
        }
    }

    void GoTo(Step next)
    {
        step = next;
        Unpoint();
        int n = (int)next; // 1..8 are the guided steps
        string head = next <= Step.OrderBuy ? $"Tutorial  ·  step {n} of 8" : "Tutorial";
        string shelf = wanted != null ? ShelfInteractable.NameOf(wanted.ShelfLocation) : "the right";
        var seekerRes = seeker != null ? GM.ReservationFor(seeker.profileKey) : null;
        switch (next)
        {
            case Step.Talk:
                Say(head, "A visitor's coming to the counter. Walk over (WASD) and click them to hear what they want.\nThe clock waits while the tutorial runs.");
                break;
            case Step.LookUp:
                Say(head, $"They want something by <color=#D67840>{wanted?.Author}</color>. Click the computer, open Books and search for the author to find where it lives.");
                Zoom("pc", PC);
                break;
            case Step.Fetch:
                Say(head, $"\"{wanted?.Title}\" is on the <color=#D67840>{shelf}</color> shelf. Click the shelf and take a copy.");
                Zoom("shelf", ShelfOf(wanted));
                break;
            case Step.Give:
                Say(head, "Now click the visitor and hand it over. Happy visitors pay and tip, and every one served is +1 reputation.");
                break;
            case Step.CardTalk:
                Say(head, "Someone wants a library card. Talk to them: they'll tell you their name and birthday.");
                break;
            case Step.CardFill:
                Say(head, "Fill in their card on the computer: open Members, type their name and pick their birthday. Their details are in your Notes (right edge).");
                break;
            case Step.OrderTalk:
                Say(head, "This visitor wants a book the library doesn't have. Talk to them, ask when they need it, and promise to order it.");
                break;
            case Step.OrderBuy:
                Say(head, seekerRes == null
                    ? "Ordering is how you stock up: on the computer, open the Catalogue, pick a book and press Order. It arrives on the book cart tomorrow morning."
                    : seekerRes.urgent
                    ? $"Now order \"{seekerRes.title}\": on the computer, find it in the Catalogue and press Express. They're waiting in the library, and express gets here in 2 hours."
                    : $"Now order \"{seekerRes.title}\": on the computer, find it in the Catalogue and press Order. It arrives on the book cart tomorrow morning.");
                break;
            case Step.FreePlay:
                Say("Tutorial", "That's the basics! The clock is running now. Serve visitors, tidy up, and at closing time lock the front door. Good luck!");
                hideAt = Time.unscaledTime + 9f;
                break;
            case Step.LockUp:
                Say("Tutorial", "Closing time. Tidy any cups and books left out (it earns reputation), then click the front ropes to lock up.");
                Zoom("door", Door);
                break;
        }
        skip.style.display = next <= Step.OrderBuy || next == Step.LockUp ? DisplayStyle.Flex : DisplayStyle.None;
        ok.style.display = next == Step.FreePlay ? DisplayStyle.Flex : DisplayStyle.None;
    }

    void Skip() => Finish(false);

    void Finish(bool completed)
    {
        step = Step.Finished;
        Unpoint();
        PlayerPrefs.SetInt(DoneKey, 1);
        PlayerPrefs.Save();
        if (completed)
        {
            Say("Tutorial", "Tutorial done! The Day Report shows how today went, and what to try tomorrow.");
            ok.style.display = DisplayStyle.Flex;
            skip.style.display = DisplayStyle.None;
            hideAt = Time.unscaledTime + 8f;
        }
        else HideCard();
    }

    // ---------------- The tutorial's visitors ----------------

    static bool Gone(Customer c) => c == null || c.IsLeaving;
    bool Holding(PlayerCarry carry) => carry != null && wanted != null && carry.Find((b, cond) => b.Author == wanted.Author && cond != BookCondition.Damaged) >= 0;

    /// <summary>First visitor: wants anything by an author with a book on the shelves (so the catalogue search works).</summary>
    bool SpawnBuyer()
    {
        var pool = GM.Books.Where(b => b.Sellable && b.Format == BookFormat.Book && !BookRequest.IsArchive(b) && !BookRequest.IsRare(b)
                                       && BookRequest.Available(b) && b.GoodCopies > 0 && !string.IsNullOrEmpty(b.Author)).ToList();
        if (pool.Count == 0) return false;
        wanted = pool[Random.Range(0, pool.Count)];
        var req = new BookRequest { Hint = HintType.Author, Value = wanted.Author, Target = wanted, Format = BookFormat.Book,
                                    Line = $"Hello! Have you got anything by {wanted.Author}?" };
        buyer = CustomerSpawner.Instance != null ? CustomerSpawner.Instance.SpawnTutorial(Customer.Kind.Buyer, req) : null;
        return buyer != null;
    }

    bool SpawnSigner()
    {
        signer = CustomerSpawner.Instance != null ? CustomerSpawner.Instance.SpawnTutorial(Customer.Kind.SignUp) : null;
        return signer != null;
    }

    bool SpawnSeeker()
    {
        seeker = CustomerSpawner.Instance != null ? CustomerSpawner.Instance.SpawnTutorial(Customer.Kind.Seeker) : null;
        return seeker != null;
    }

    // ---------------- Pointing, zooming and the card ----------------

    static ClickInteractable PC => FindAnyObjectByType<ComputerClick>();
    static ClickInteractable Door => FindAnyObjectByType<EndDayDoor>();
    static ShelfInteractable ShelfOf(BookData b) => b == null ? null : FindObjectsByType<ShelfInteractable>().FirstOrDefault(s => s.Belongs(b));

    void Point(Component target, string label)
    {
        if (target == null) { Unpoint(); return; }
        if (pointedAt == target && pointer != null) return;
        Unpoint();
        pointedAt = target;
        var t = target.transform;
        float height = target is Customer ? 2.3f : 2.4f;
        var anchor = target as ClickInteractable;
        pointer = DayStory.Point(label, () => t == null ? Vector3.zero : anchor != null && !(target is Customer) ? anchor.TopAnchor + Vector3.up * 0.4f : t.position + Vector3.up * height);
    }

    void Unpoint()
    {
        DayStory.Unpoint(ref pointer);
        pointedAt = null;
        NotebookHUD.Tuck(false);
    }

    /// <summary>The notebook sits over the right of the screen: while the arrow points at something behind it
    /// (a shelf on the right), fold the notebook away.</summary>
    void KeepPointerVisible()
    {
        var cam = Camera.main;
        if (cam == null || pointedAt == null || !(pointedAt is Component c) || c == null) { NotebookHUD.Tuck(false); return; }
        var vp = cam.WorldToViewportPoint(c.transform.position);
        NotebookHUD.Tuck(vp.z > 0f && vp.x > 0.7f);
    }

    /// <summary>The first time you need something, the camera glides over and zooms in on it for a moment.</summary>
    void Zoom(string key, Component target)
    {
        if (target == null || !zoomed.Add(key)) return;
        StartCoroutine(ZoomOn(target.transform.position));
    }

    IEnumerator ZoomOn(Vector3 focus)
    {
        while (PlayerLock.IsLocked) yield return null; // after any conversation or screen
        var follow = FindAnyObjectByType<CinemachineCamera>();
        var main = Camera.main;
        var player = GameObject.FindWithTag("Player");
        if (follow == null || main == null || player == null) yield break;
        PlayerLock.Lock(this);
        var go = new GameObject("CM_TutorialZoom");
        var cam = go.AddComponent<CinemachineCamera>();
        var lens = follow.Lens;
        lens.OrthographicSize *= 0.7f; // closer than usual
        cam.Lens = lens;
        Vector3 offset = main.transform.position - player.transform.position;
        go.transform.position = focus + offset;
        go.transform.rotation = main.transform.rotation;
        cam.Priority = 55;
        yield return new WaitForSecondsRealtime(2.4f);
        cam.Priority = 0;
        yield return new WaitForSecondsRealtime(1.2f);
        Destroy(go);
        PlayerLock.Unlock(this);
    }

    void Say(string head, string body)
    {
        hideAt = 0f;
        title.text = head;
        text.text = body;
        card.RemoveFromClassList("hidden");
        card.RemoveFromClassList("hint");
    }

    void HideCard()
    {
        hideAt = 0f;
        card?.AddToClassList("hidden");
    }

    // ---------------- First-time hints ----------------

    /// <summary>Every hint key (so a new game can forget them all).</summary>
    static readonly string[] HintKeys =
        { "seeker", "soldout", "damaged", "staffhelp", "pickup", "vip", "delivery", "mess", "reader", "checkout", "returns", "wrongbook", "express", "ask" };

    readonly Queue<(string key, string text)> hints = new Queue<(string, string)>();

    /// <summary>A one-line tip the first time something happens (once per game). Held back while the
    /// tutorial is guiding you or another tip is showing, then shown in turn.</summary>
    public static void Show(string key, string text)
    {
        var t = Instance;
        if (t == null || PlayerPrefs.GetInt(HintPrefix + key, 0) == 1) return;
        if (t.hints.Any(h => h.key == key)) return;
        t.hints.Enqueue((key, text));
    }

    void ShowQueuedHint()
    {
        if (hints.Count == 0 || card == null || HoldsClock || PlayerLock.IsLocked) return; // not over a conversation or the PC
        if (!card.ClassListContains("hidden")) return; // something's on the card: wait for it
        if (MainMenuUI.Active) return;
        var (key, body) = hints.Dequeue();
        if (PlayerPrefs.GetInt(HintPrefix + key, 0) == 1) return;
        PlayerPrefs.SetInt(HintPrefix + key, 1);
        HUD.Tip(body); // in the notification feed, until clicked
    }
}
