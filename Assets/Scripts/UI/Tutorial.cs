using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Unity.Cinemachine;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

/// <summary>
/// The tutorial (new games, Day 1 morning; or replayed from Settings / the title menu) and the one-time tips.
/// 1. A short tour: the camera glides to each important thing, an animated pixel frame marks it and a small box
///    says what it does (Next / Enter to go on).
/// 2. Three visitors only: a book by an author (computer, shelf, hand over), a library card (Members), and a book
///    the library doesn't have (reserve, order; in the tutorial it arrives on the cart at once, then hand it over).
///    Three step cards (book, card, order) tick off each one. The clock holds, patience doesn't drain and nobody else comes in.
/// 3. "You're ready!", then a fade into Day 1 proper: the practice coins and reputation are put back (and today's
///    numbers start from there); finishing it the first time leaves a small welcome gift.
/// Skip any time: same fade, no gift. Its card and frame sit on their own panel, above the computer and shelves.
/// Tips: <see cref="Show"/> once per key per game, in the notification feed (never during the tutorial).
/// Added to the HUD object at runtime.
/// </summary>
public class Tutorial : MonoBehaviour
{
    public static Tutorial Instance { get; private set; }

    const string DoneKey = "TutorialDone", HintPrefix = "Hint_", GiftKey = "TutorialGift";
    const int WelcomeGift = 20;

    enum Step { Waiting, Tour, Talk, LookUp, Fetch, Give, CardTalk, CardFill, OrderTalk, OrderBuy, CartTake, CartGive, Ready, Finished }
    Step step = Step.Waiting;

    /// <summary>A tutorial card is on screen (the notification feed waits until it's gone).</summary>
    public static bool CardShowing => Instance != null && Instance.card != null && !Instance.card.ClassListContains("hidden");

    /// <summary>The tutorial is running: the clock holds, patience doesn't drain and only its visitors come in.</summary>
    public static bool HoldsClock => Instance != null && Instance.step >= Step.Tour && Instance.step <= Step.Ready;

    static bool replayRequested;
    /// <summary>Settings / title menu: run the tutorial again (as soon as the library is open and nothing's on screen).</summary>
    public static void RequestReplay() => replayRequested = true;

    static GameManager GM => GameManager.Instance;
    static string H(string s) => $"<color=#D67840>{s}</color>";

    Customer buyer, signer, seeker;
    BookData wanted;
    int signupsBefore, servedBefore, ordersBefore;
    readonly bool[] ticked = new bool[3]; // served, card, order
    bool finishing;
    bool noteSaid; // the card says "Press Add to note" (the right book is picked on the computer)

    VisualElement root, card, checklist, spot;
    Label title, text, spotLabel;
    VisualElement[] checks; // the three step cards
    Button skip, ok;
    WorldPrompts.Follower pointer;
    Object pointedAt;
    float hideAt;

    void Awake() => Instance = this;
    void OnDestroy() { if (Instance == this) Instance = null; if (topGo != null) Destroy(topGo); }

    /// <summary>A new game (or a reset) starts with the tutorial, the welcome gift and every tip unseen.</summary>
    public static void ResetProgress()
    {
        PlayerPrefs.DeleteKey(DoneKey);
        PlayerPrefs.DeleteKey(GiftKey);
        foreach (var key in HintKeys) PlayerPrefs.DeleteKey(HintPrefix + key);
    }

    GameObject topGo;

    void Start()
    {
        var hud = GetComponent<UIDocument>();
        if (hud == null || hud.panelSettings == null) return;
        // Its own panel, above the computer (20) and the notebook (25), under the title menu (30), so the card and the
        // frame are never under a screen's dark backdrop. A root object: a UIDocument under another one joins its panel.
        topGo = new GameObject("TutorialUI");
        topGo.SetActive(false);
        var ps = Instantiate(hud.panelSettings);
        ps.name = "PS_Tutorial";
        ps.sortingOrder = 28;
        var doc = topGo.AddComponent<UIDocument>();
        doc.panelSettings = ps;
        topGo.SetActive(true);
        root = doc.rootVisualElement;
        root.pickingMode = PickingMode.Ignore;
        if (hud.rootVisualElement != null)
            for (int i = 0; i < hud.rootVisualElement.styleSheets.count; i++) root.styleSheets.Add(hud.rootVisualElement.styleSheets[i]);
        var holder = new VisualElement { name = "tutorial-layer", pickingMode = PickingMode.Ignore };
        holder.AddToClassList("layer");
        root.Add(holder);

        // The frame and its little label (the tour, and what to click next).
        spot = new VisualElement { pickingMode = PickingMode.Ignore };
        spot.AddToClassList("tut-spot");
        spot.AddToClassList("hidden");
        spotLabel = new Label { pickingMode = PickingMode.Ignore };
        spotLabel.AddToClassList("tut-spot-label");
        spotLabel.AddToClassList("hidden");
        holder.Add(spot);
        holder.Add(spotLabel);

        card = new VisualElement { name = "tutorial-card" };
        card.AddToClassList("tut-card");
        card.AddToClassList("hidden");
        title = new Label(); title.AddToClassList("tut-title");
        text = new Label(); text.AddToClassList("tut-text");
        // Three little cards, one per visitor: the current one has an orange edge, done ones get the kit's tick.
        checklist = new VisualElement(); checklist.AddToClassList("tut-steps");
        checks = new[] { ("book", "Find a book"), ("key", "Library card"), ("scroll", "Order a book") }.Select(s =>
        {
            var c = new VisualElement(); c.AddToClassList("tut-stepcard");
            var icon = new VisualElement(); icon.AddToClassList("tut-stepicon"); icon.AddToClassList("tut-step-" + s.Item1);
            var label = new Label(s.Item2); label.AddToClassList("tut-steplabel");
            var tick = new VisualElement(); tick.AddToClassList("tut-steptick");
            c.Add(icon); c.Add(label); c.Add(tick);
            checklist.Add(c); return c;
        }).ToArray();
        var buttons = new VisualElement(); buttons.AddToClassList("tut-buttons");
        ok = new Button(OnOk) { text = "Next" }; ok.AddToClassList("kit-button"); ok.AddToClassList("tut-button");
        skip = new Button(() => Finish(false)) { text = "Skip tutorial" }; skip.AddToClassList("kit-button"); skip.AddToClassList("tut-button");
        buttons.Add(ok); buttons.Add(skip);
        card.Add(title); card.Add(text); card.Add(checklist); card.Add(buttons);
        holder.Add(card);
        spot.BringToFront(); // over the card too (the last card frames its "Start Day 1" button)
        spotLabel.BringToFront();
    }

    // ---------------- Running it ----------------

    bool CanStart => GM != null && !MainMenuUI.Active && GM.CurrentPhase == GameManager.Phase.Open && !PlayerLock.IsLocked && !CameraMoving
                     && (replayRequested || (GM.day == 1 && !GM.HasSave && PlayerPrefs.GetInt(DoneKey, 0) == 0));

    void Update()
    {
        if (GM == null || card == null) return;
        if (hideAt > 0f && Time.unscaledTime > hideAt) HideCard();
        AnimateSpot();
        if (step == Step.Waiting || step == Step.Finished)
        {
            if (CanStart) Begin();
            else ShowQueuedHint();
            return;
        }
        if (finishing) return;
        if (step == Step.Tour)
        {
            if (Keyboard.current != null && (Keyboard.current.enterKey.wasPressedThisFrame || Keyboard.current.numpadEnterKey.wasPressedThisFrame)) NextStop();
            PlaceSpot();
            return;
        }
        if (step == Step.Ready && Keyboard.current != null && (Keyboard.current.enterKey.wasPressedThisFrame || Keyboard.current.numpadEnterKey.wasPressedThisFrame)) { Finish(true); return; }
        Advance();
        KeepPointerVisible();
        PlaceSpot();
    }

    // The tutorial is practice: coins, reputation and today's numbers go back to these when it ends.
    int coinsBefore = -1, repBefore;
    GameManager.DayStats todayBefore;
    List<string> membersBefore; // cards made in practice are torn up afterwards

    void Begin()
    {
        replayRequested = false;
        finishing = false;
        coinsBefore = GM.Coins; repBefore = GM.Reputation; todayBefore = GM.Today.Copy();
        membersBefore = GM.members.Select(m => m.key).ToList();
        GM.RememberBooksForTutorial();
        signupsBefore = GM.Today.signups;
        servedBefore = GM.Today.served;
        ordersBefore = GM.OrdersPlaced;
        buyer = signer = seeker = null; wanted = null;
        for (int i = 0; i < ticked.Length; i++) { ticked[i] = false; checks[i].RemoveFromClassList("done"); checks[i].RemoveFromClassList("current"); }
        PCOverlayTrigger.LastViewed = null;
        StartTour();
    }

    /// <summary>The camera is still gliding (from the title menu): wait, or the tour starts from the wrong place.</summary>
    static bool CameraMoving
    {
        get
        {
            var brain = Camera.main != null ? Camera.main.GetComponent<CinemachineBrain>() : null;
            return brain != null && brain.IsBlending;
        }
    }

    /// <summary>The camera that follows the librarian (not the title menu's).</summary>
    static CinemachineCamera FollowCam => FindObjectsByType<CinemachineCamera>().FirstOrDefault(c => c.Follow != null);

    void OnOk()
    {
        if (step == Step.Tour) NextStop();
        else if (step == Step.Ready) Finish(true);
        else HideCard();
    }

    // ---------------- 1. The tour ----------------

    class Stop { public Component target; public VisualElement ui; public Bounds bounds; public string name, text; }
    readonly List<Stop> stops = new List<Stop>();
    int stopIndex;
    GameObject tourCam;
    Coroutine glide;

    void StartTour()
    {
        stops.Clear();
        var queue = CounterQueue.Instance;
        if (queue != null)
        {
            var c = queue.transform.position + queue.transform.forward * 1.1f + Vector3.up * 0.6f;
            stops.Add(new Stop { target = queue, bounds = new Bounds(c, new Vector3(3f, 1.2f, 1.6f)), name = "Front desk",
                                 text = $"Visitors {H("queue")} at the front desk. Walk up and {H("click")} them to hear what they want." });
        }
        AddStop(FindAnyObjectByType<ComputerClick>(), "Computer",
            $"The {H("computer")}: search the {H("Catalogue")}, issue library {H("cards")}, {H("order")} books, buy {H("upgrades")} and hire {H("staff")}.");
        var player = GameObject.FindWithTag("Player");
        var shelf = FindObjectsByType<ShelfInteractable>().Where(s => s.shelfId == "HIS" || s.shelfId == "SCI" || s.shelfId == "PHI" || s.shelfId == "MATH")
                    .OrderBy(s => player != null ? (s.transform.position - player.transform.position).sqrMagnitude : 0f).FirstOrDefault();
        AddStop(shelf, "Shelves", $"Books live on their section's {H("shelf")} (the sign says which). Click a shelf to take a copy.");
        AddStop(DeliveryCart.Instance, "Book cart", $"{H("Orders")} arrive on the book {H("cart")}.");
        AddStop(ReturnBin.Instance, "Return bin", $"Returned books land in the {H("return bin")}. Put them back on their shelves.");
        var notebook = FindAnyObjectByType<NotebookHUD>()?.GetComponent<UIDocument>()?.rootVisualElement?.Q("notebook");
        if (notebook != null) stops.Add(new Stop { ui = notebook, name = "Your notes", text = $"Your {H("Notes")} keep every request. {H("Coins")} are top right, {H("reputation")} at the bottom." });
        AddStop(FindAnyObjectByType<EndDayDoor>(), "Front ropes", $"At {H("closing time")}, tidy up and click the {H("front ropes")} to lock up.");

        step = Step.Tour;
        PlayerLock.Lock(this); // a look round: nothing moves
        var follow = FollowCam;
        if (follow != null && Camera.main != null)
        {
            tourCam = new GameObject("CM_TutorialTour");
            var cam = tourCam.AddComponent<CinemachineCamera>();
            var lens = follow.Lens;
            lens.OrthographicSize *= 0.8f;
            cam.Lens = lens;
            tourCam.transform.SetPositionAndRotation(follow.transform.position, follow.transform.rotation); // starts where the follow camera is
            cam.Priority = 55;
        }
        stopIndex = -1;
        NextStop();
    }

    void AddStop(Component target, string name, string body)
    {
        if (target == null) return;
        stops.Add(new Stop { target = target, bounds = BoundsOf(target), name = name, text = body });
    }

    void NextStop()
    {
        stopIndex++;
        if (stopIndex >= stops.Count) { EndTour(); GoTo(Step.Talk); return; }
        var s = stops[stopIndex];
        Say($"Tour  ·  {stopIndex + 1} of {stops.Count}", s.text);
        ok.text = stopIndex == stops.Count - 1 ? "Meet a visitor" : "Next";
        ok.RemoveFromClassList("primary");
        ok.style.display = DisplayStyle.Flex;
        skip.style.display = DisplayStyle.Flex;
        checklist.style.display = DisplayStyle.None;
        spotLabel.text = s.name; // the card explains; the frame just names it
        if (s.target != null && tourCam != null) { if (glide != null) StopCoroutine(glide); glide = StartCoroutine(Glide(s.bounds.center)); }
        GameAudio.Play(a => a.uiClick, 0.6f);
    }

    /// <summary>The tour camera eases over to look at a point.</summary>
    IEnumerator Glide(Vector3 focus)
    {
        var player = GameObject.FindWithTag("Player");
        var main = Camera.main;
        if (tourCam == null || player == null || main == null) yield break;
        // The follow camera's offset from the librarian (the main camera may be mid-blend).
        var follow = FollowCam;
        Vector3 offset = follow != null ? follow.transform.position - player.transform.position : main.transform.position - player.transform.position;
        Vector3 goal = focus + offset, vel = Vector3.zero;
        for (float t = 0f; t < 1.4f && tourCam != null; t += Time.unscaledDeltaTime)
        {
            tourCam.transform.position = Vector3.SmoothDamp(tourCam.transform.position, goal, ref vel, 0.35f, Mathf.Infinity, Time.unscaledDeltaTime);
            yield return null;
        }
    }

    void EndTour()
    {
        if (glide != null) { StopCoroutine(glide); glide = null; }
        ClearSpot();
        if (tourCam != null)
        {
            var cam = tourCam.GetComponent<CinemachineCamera>();
            if (cam != null) cam.Priority = 0;
            Destroy(tourCam, 1.5f);
            tourCam = null;
        }
        PlayerLock.Unlock(this);
    }

    // ---------------- 2. The three visitors ----------------

    void Advance()
    {
        var carry = GameObject.FindWithTag("Player")?.GetComponent<PlayerCarry>();
        switch (step)
        {
            case Step.Talk:
                if (buyer == null && !SpawnBuyer()) { GoTo(Step.CardTalk); break; } // no book on any shelf: can't do this one
                if (Gone(buyer)) { Respawn(ref buyer); break; }
                Point(buyer, "Talk");
                if (RequestLog.Has(buyer)) GoTo(Step.LookUp);
                break;
            case Step.LookUp:
                if (Gone(buyer)) { Respawn(ref buyer); GoTo(Step.Talk); break; }
                Point(PC, "Computer");
                if (Holding(carry)) GoTo(Step.Give);
                else if (RequestLog.IsNoted(wanted)) { FindAnyObjectByType<PCOverlayTrigger>()?.ForceClose(); GoTo(Step.Fetch); }
                else if (NoteOnly != noteSaid) // the right book is picked: now only "Add to note" works
                {
                    noteSaid = NoteOnly;
                    Say(title.text, noteSaid ? $"That's the one. Press {H("Add to note")} so you remember its shelf."
                                             : $"They want {H($"\"{wanted?.Title}\"")}. Click the {H("computer")} and find it in the {H("Catalogue")}.");
                }
                break;
            case Step.Fetch:
                if (Gone(buyer)) { Respawn(ref buyer); GoTo(Step.Talk); break; }
                Point(ShelfOf(wanted), "Shelf");
                if (Holding(carry)) GoTo(Step.Give);
                break;
            case Step.Give:
                if (Gone(buyer))
                {
                    if (GM.Today.served > servedBefore) { Tick(0); GoTo(Step.CardTalk); } // served: on to the next visitor
                    else { Respawn(ref buyer); GoTo(Step.Talk); }
                    break;
                }
                Point(buyer, "Hand it over");
                if (!Holding(carry)) GoTo(Step.Fetch);
                break;
            case Step.CardTalk:
                if (signer == null && !SpawnSigner()) { GoTo(Step.OrderTalk); break; }
                if (GM.Today.signups > signupsBefore) { Tick(1); GoTo(Step.OrderTalk); break; }
                if (Gone(signer)) { Respawn(ref signer); break; }
                Point(signer, "Talk");
                if (Customer.WaitingForCard == signer) GoTo(Step.CardFill);
                break;
            case Step.CardFill:
                if (GM.Today.signups > signupsBefore) { Tick(1); GoTo(Step.OrderTalk); break; }
                if (Gone(signer)) { Respawn(ref signer); GoTo(Step.CardTalk); break; }
                Point(PC, "Computer");
                break;
            case Step.OrderTalk:
                if (seeker == null && !SpawnSeeker()) { GoTo(Step.Ready); break; }
                if (GM.ReservationFor(seeker.profileKey) != null) { ordersBefore = GM.OrdersPlaced; GoTo(Step.OrderBuy); break; }
                if (Gone(seeker)) { Respawn(ref seeker); break; }
                if (seeker.IsWaiting) Point(seeker, "Talk"); else Unpoint();
                break;
            case Step.OrderBuy:
            {
                Point(PC, "Computer");
                var res = seeker != null ? GM.ReservationFor(seeker.profileKey) : null;
                if (GM.OrdersPlaced > ordersBefore && (res == null || res.ordered || GM.OnOrder(SeekerBook) > 0))
                {
                    Tick(2);
                    FindAnyObjectByType<PCOverlayTrigger>()?.ForceClose(); // ordered: back to the library
                    GM.DeliverOrdersNow(); // the tutorial only: the order comes straight away
                    GoTo(res != null && !Gone(seeker) ? Step.CartTake : Step.Ready);
                }
                break;
            }
            case Step.CartTake:
                if (Gone(seeker)) { GoTo(Step.Ready); break; } // (can't happen: they wait for it)
                Point(DeliveryCart.Instance, "Book cart");
                if (carry != null && SeekerBook != null && carry.Find((b, c) => b == SeekerBook) >= 0) GoTo(Step.CartGive);
                break;
            case Step.CartGive:
                if (Gone(seeker)) { GoTo(Step.Ready); break; } // collected
                Point(seeker, "Hand it over");
                break;
        }
    }

    BookData SeekerBook => seeker != null && seeker.CurrentRequest != null ? seeker.CurrentRequest.Target : null;

    void GoTo(Step next)
    {
        step = next;
        Unpoint();
        ClearSpot();
        string head = next == Step.Ready ? "Tutorial" : $"Tutorial  ·  {VisitorNo(next)} of 3";
        string shelf = wanted != null ? ShelfInteractable.NameOf(wanted.ShelfLocation) : "the right";
        checklist.style.display = DisplayStyle.Flex;
        for (int i = 0; i < checks.Length; i++) checks[i].EnableInClassList("current", next != Step.Ready && i == VisitorNo(next) - 1 && !ticked[i]);
        ok.style.display = DisplayStyle.None;
        ok.RemoveFromClassList("primary");
        skip.style.display = DisplayStyle.Flex;
        noteSaid = false;
        switch (next)
        {
            case Step.Talk:
                Say(head, $"A visitor's at the desk. Walk over ({H("WASD")}) and {H("click")} them.");
                break;
            case Step.LookUp:
                Say(head, $"They want {H($"\"{wanted?.Title}\"")}. Click the {H("computer")} and find it in the {H("Catalogue")}.");
                break;
            case Step.Fetch:
                Say(head, $"It's on the {H(shelf)} shelf. Click the shelf and take a copy.");
                break;
            case Step.Give:
                Say(head, "Click the visitor and hand it over.");
                break;
            case Step.CardTalk:
                Say(head, $"Someone wants a library {H("card")}. Talk to them.");
                break;
            case Step.CardFill:
                Say(head, $"On the {H("computer")}, open {H("Members")} and type their name and birthday (in your {H("Notes")}).");
                break;
            case Step.OrderTalk:
                Say(head, $"This visitor wants a book we don't have. Talk to them and {H("reserve")} it.");
                break;
            case Step.OrderBuy:
                Say(head, $"They need it {H("today")}: find it in the {H("Catalogue")} and press {H("Express")} (normally 2 hours; this one's quick).");
                // Practice coins: always enough for the express price (the coins are put back after the tutorial).
                if (SeekerBook != null && GM.Coins < GameManager.OrderPrice(SeekerBook, true)) GM.AddCoins(GameManager.OrderPrice(SeekerBook, true) - GM.Coins);
                break;
            case Step.CartTake:
                Say(head, $"{H("Orders")} arrive on the book {H("cart")} (normally the next morning; this one's quick). Take it.");
                break;
            case Step.CartGive:
                Say(head, "Now hand it to them at the desk.");
                break;
            case Step.Ready:
                Say("You're ready!", $"Serve visitors for coins and {H("reputation")}. {H("Order")} what you don't have. At {H("closing")}, lock the {H("front ropes")}.");
                ok.text = "Start Day 1";
                ok.style.display = DisplayStyle.Flex;
                ok.AddToClassList("primary"); // the only thing left to do: orange, framed and focused (Enter works too)
                skip.style.display = DisplayStyle.None;
                SetSpot(null, ok, null);
                ok.schedule.Execute(() => ok.Focus());
                GameAudio.Play(a => a.dayReport, 0.6f);
                break;
        }
    }

    static int VisitorNo(Step s) => s <= Step.Give ? 1 : s <= Step.CardFill ? 2 : 3;

    /// <summary>Ticks a step card, with a little sound.</summary>
    void Tick(int i)
    {
        if (ticked[i]) return;
        ticked[i] = true;
        checks[i].AddToClassList("done");
        checks[i].RemoveFromClassList("current");
        GameAudio.Play(a => a.served, 0.7f);
    }

    // ---------------- Only the next thing works ----------------

    /// <summary>Can this be clicked right now? During the tutorial only the step's own target (the visitor, the
    /// computer, the shelf, the cart); everything else ignores you. Outside the tutorial: everything.</summary>
    public static bool Allows(Object target)
    {
        var t = Instance;
        if (t == null || !HoldsClock || target == null) return true;
        switch (t.step)
        {
            case Step.Talk: case Step.Give: return target == t.buyer;
            case Step.LookUp: case Step.CardFill: case Step.OrderBuy: return target is ComputerClick;
            case Step.Fetch: return target == ShelfOf(t.wanted);
            case Step.CardTalk: return target == t.signer;
            case Step.OrderTalk: case Step.CartGive: return target == t.seeker;
            case Step.CartTake: return target == DeliveryCart.Instance;
            default: return false; // the tour and the last card: nothing to click in the library
        }
    }

    /// <summary>The computer tab this step needs (the others are greyed out); null outside the tutorial.</summary>
    public static string NeededTab
    {
        get
        {
            var t = Instance;
            if (t == null || !HoldsClock) return null;
            return t.step == Step.CardFill ? "members" : t.step == Step.LookUp || t.step == Step.OrderBuy ? "catalogue" : "home";
        }
    }

    /// <summary>The one book this step is about (its shelf and cart buttons work; the others are greyed out).</summary>
    public static bool AllowsBook(BookData b)
    {
        var t = Instance;
        if (t == null || !HoldsClock) return true;
        return t.step == Step.Fetch || t.step == Step.LookUp || t.step == Step.Give ? b == t.wanted
             : t.step == Step.CartTake || t.step == Step.CartGive ? b == t.SeekerBook : false;
    }

    /// <summary>The first visitor's book is picked in the Catalogue: only "Add to note" works until it's pressed
    /// (the rest of the catalogue is greyed out and the computer can't be closed).</summary>
    public static bool NoteOnly
    {
        get
        {
            var t = Instance;
            return t != null && HoldsClock && !t.finishing && t.step == Step.LookUp && t.wanted != null
                   && PCCatalogueView.Selected == t.wanted && !RequestLog.IsNoted(t.wanted);
        }
    }

    /// <summary>The order step: only Express works (the visitor needs it today).</summary>
    public static bool ExpressOnly => Instance != null && HoldsClock && Instance.step == Step.OrderBuy;

    /// <summary>Ordering: only the reserved visitor's book, and only in the order step.</summary>
    public static bool AllowsOrder(BookData b)
    {
        var t = Instance;
        if (t == null || !HoldsClock) return true;
        return t.step == Step.OrderBuy && b != null && b == t.SeekerBook && GM.OrdersPlaced == t.ordersBefore;
    }

    // ---------------- 3. Finishing (done or skipped) ----------------

    void Finish(bool completed)
    {
        if (finishing || step == Step.Waiting || step == Step.Finished) return;
        finishing = true;
        if (step == Step.Tour) EndTour();
        Unpoint();
        ClearSpot();
        HideCard();
        // Close whatever's open, then fade out and back in to the library's real morning.
        FindAnyObjectByType<PCOverlayTrigger>()?.ForceClose();
        ShelfUI.Instance?.Close();
        DialogueUI.Instance?.Close();
        HUD.FadeOut(() =>
        {
            RemoveVisitors();
            if (coinsBefore >= 0) GM.ResetAfterTutorial(coinsBefore, repBefore, todayBefore);
            coinsBefore = -1;
            if (membersBefore != null) GM.members.RemoveAll(m => !membersBefore.Contains(m.key));
            membersBefore = null;
            GM.RestoreBooksAfterTutorial();
            if (completed && PlayerPrefs.GetInt(GiftKey, 0) == 0)
            {
                PlayerPrefs.SetInt(GiftKey, 1);
                GM.AddCoins(WelcomeGift);
                GM.Today.Earn(GameManager.DayStats.Source.Other, WelcomeGift);
                RequestLog.Money("Dean Whitmore", "Welcome gift for finishing the tutorial", "Gift", WelcomeGift);
            }
            step = Step.Finished;
            PlayerPrefs.SetInt(DoneKey, 1);
            PlayerPrefs.Save();
            HUD.FadeIn();
            var hud = FindAnyObjectByType<HUD>();
            if (hud != null) hud.ShowBanner(completed && GM.day == 1 ? $"Tutorial done! Welcome gift: +{WelcomeGift} coins.\n{HUD.MorningText(GM)}" : HUD.MorningText(GM), 6f);
            finishing = false;
        });
    }

    /// <summary>The tutorial's visitors leave with it (their requests, reservations and seats too).</summary>
    void RemoveVisitors()
    {
        foreach (var c in new[] { buyer, signer, seeker })
        {
            if (c == null) continue;
            GM.reservations.RemoveAll(r => r.key == c.profileKey);
            RequestLog.Finish(c, "Tutorial over");
            Destroy(c.gameObject); // OnDestroy frees their place in line, their seat and the card desk
        }
        buyer = signer = seeker = null;
        GM.NotifyBooksChanged();
    }

    // ---------------- The tutorial's visitors ----------------

    /// <summary>A tutorial visitor left before their step was done: clear them away; the step brings a new one.</summary>
    void Respawn(ref Customer c)
    {
        var who = c;
        if (who != null)
        {
            GM.reservations.RemoveAll(r => r.key == who.profileKey);
            RequestLog.Finish(who, "Left (tutorial)");
            Destroy(who.gameObject);
        }
        c = null; // the step spawns a fresh visitor next frame
    }

    static bool Gone(Customer c) => c == null || c.IsLeaving;
    bool Holding(PlayerCarry carry) => carry != null && wanted != null && carry.Find((b, cond) => b == wanted && cond != BookCondition.Damaged) >= 0;

    /// <summary>First visitor: wants anything by an author with a book on the shelves (so the catalogue search works).</summary>
    bool SpawnBuyer()
    {
        var pool = GM.Books.Where(b => b.Sellable && b.Format == BookFormat.Book && !BookRequest.IsArchive(b) && !BookRequest.IsRare(b)
                                       && BookRequest.Available(b) && b.GoodCopies > 0 && BookRequest.HasAuthor(b)).ToList();
        if (pool.Count == 0) return false;
        wanted = pool[Random.Range(0, pool.Count)];
        var req = new BookRequest { Hint = HintType.Title, Target = wanted, Format = BookFormat.Book,
                                    Line = $"Hello! Have you got {H($"\"{wanted.Title}\"")}?" };
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

    // ---------------- Pointing, the frame and the card ----------------

    static ClickInteractable PC => FindAnyObjectByType<ComputerClick>();
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
        pointer = DayStory.Point(label, () => t == null ? Vector3.zero : anchor != null && !(target is Customer) ? anchor.TopAnchor + Vector3.up * 0.4f : t.position + Vector3.up * height,
                                 above: (target as Customer)?.Overhead); // stacked over their bubble, never on it
    }

    /// <summary>The tutorial's arrow is over this already (so its hover prompt would only repeat it).</summary>
    public static bool IsPointingAt(Object target) => Instance != null && target != null && Instance.pointer != null && Instance.pointedAt == target;

    void Unpoint()
    {
        DayStory.Unpoint(ref pointer);
        pointedAt = null;
        NotebookHUD.Tuck(false);
    }

    /// <summary>The notebook sits over the right of the screen: while the arrow points at something behind it, fold it away.</summary>
    void KeepPointerVisible()
    {
        var cam = Camera.main;
        if (cam == null || pointedAt == null || !(pointedAt is Component c) || c == null) { NotebookHUD.Tuck(false); return; }
        var vp = cam.WorldToViewportPoint(c.transform.position);
        NotebookHUD.Tuck(vp.z > 0f && vp.x > 0.7f);
    }

    // The animated pixel frame (UI_TravelBook_SlotCursor01a, 4 frames) round something, with a small label above it.
    Component spotTarget;
    VisualElement spotUi;
    Bounds spotBounds;
    float spotFrameAt;
    int spotFrame;

    /// <summary>Frames a thing in the world (or a screen element) with the pixel cursor and an optional label.
    /// Also used outside the tutorial (the repair desk on the morning the Archive opens).</summary>
    public static void Spotlight(Component target, string label) => Instance?.SetSpot(target, null, label);
    public static void ClearSpotlight() => Instance?.ClearSpot();

    void SetSpot(Component target, VisualElement ui, string label)
    {
        if (spot == null) return;
        spotTarget = target; spotUi = ui;
        if (target != null) spotBounds = BoundsOf(target);
        spot.RemoveFromClassList("hidden");
        spotLabel.text = label ?? "";
        spotLabel.EnableInClassList("hidden", string.IsNullOrEmpty(label));
        PlaceSpot();
    }

    void ClearSpot()
    {
        spotTarget = null; spotUi = null;
        spot?.AddToClassList("hidden");
        spotLabel?.AddToClassList("hidden");
    }

    /// <summary>Which thing to frame right now: the tour stop, or the step's target (a computer tab while the PC is open).</summary>
    void PlaceSpot()
    {
        if (step == Step.Tour && stopIndex >= 0 && stopIndex < stops.Count)
        {
            var s = stops[stopIndex];
            if (spotTarget != s.target || spotUi != s.ui || spot.ClassListContains("hidden"))
            { spotTarget = s.target; spotUi = s.ui; spotBounds = s.bounds; spot.RemoveFromClassList("hidden"); spotLabel.text = s.name; spotLabel.RemoveFromClassList("hidden"); }
        }
        else if (HoldsClock && step != Step.Ready)
        {
            // Inside the computer: frame the tab or button to press; out in the library: frame where to go.
            var pcEl = PcElementFor(step);
            if (pcEl != null) { if (spotUi != pcEl) SetSpot(null, pcEl, null); }
            else
            {
                Component t = step == Step.LookUp || step == Step.CardFill || step == Step.OrderBuy ? PC
                            : step == Step.Fetch ? ShelfOf(wanted)
                            : step == Step.CartTake ? DeliveryCart.Instance : null;
                bool screenOpen = PlayerLock.IsLocked;
                if (t == null || screenOpen) { if (spotTarget != null || spotUi != null) ClearSpot(); }
                else if (spotTarget != t) SetSpot(t, null, null); // just the frame: the arrow above says what it is
            }
        }
        if (spot == null || spot.ClassListContains("hidden") || root == null) return;
        float w = root.resolvedStyle.width, h = root.resolvedStyle.height;
        if (float.IsNaN(w) || w <= 0f) return;
        Rect r;
        if (spotUi != null)
        {
            // An element on another panel: through normalized screen space.
            var ownRoot = spotUi.panel?.visualTree;
            if (ownRoot == null || spotUi.worldBound.width < 1f) { spot.style.visibility = Visibility.Hidden; spotLabel.style.visibility = Visibility.Hidden; return; }
            float ow = ownRoot.layout.width, oh = ownRoot.layout.height;
            var b = spotUi.worldBound;
            r = new Rect(b.x / ow * w, b.y / oh * h, b.width / ow * w, b.height / oh * h);
        }
        else
        {
            var cam = Camera.main;
            if (cam == null || spotTarget == null) return;
            var c = spotBounds.center; var e = spotBounds.extents;
            float xMin = float.MaxValue, yMin = float.MaxValue, xMax = float.MinValue, yMax = float.MinValue;
            for (int i = 0; i < 8; i++)
            {
                var p = c + new Vector3((i & 1) == 0 ? -e.x : e.x, (i & 2) == 0 ? -e.y : e.y, (i & 4) == 0 ? -e.z : e.z);
                var vp = cam.WorldToViewportPoint(p);
                xMin = Mathf.Min(xMin, vp.x); xMax = Mathf.Max(xMax, vp.x); yMin = Mathf.Min(yMin, vp.y); yMax = Mathf.Max(yMax, vp.y);
            }
            r = new Rect(xMin * w, (1f - yMax) * h, (xMax - xMin) * w, (yMax - yMin) * h);
        }
        const float pad = 10f;
        spot.style.visibility = Visibility.Visible;
        spot.style.left = r.x - pad; spot.style.top = r.y - pad;
        spot.style.width = Mathf.Max(40f, r.width + pad * 2f); spot.style.height = Mathf.Max(40f, r.height + pad * 2f);
        if (!spotLabel.ClassListContains("hidden"))
        {
            spotLabel.style.visibility = Visibility.Visible;
            float lw = spotLabel.resolvedStyle.width, lh = spotLabel.resolvedStyle.height;
            if (float.IsNaN(lw)) lw = 300f;
            if (float.IsNaN(lh)) lh = 40f;
            float top = r.y - pad - lh - 8f;
            if (top < 8f) top = r.yMax + pad + 8f; // no room above: below
            spotLabel.style.left = Mathf.Clamp(r.center.x - lw * 0.5f, 8f, w - lw - 8f);
            spotLabel.style.top = Mathf.Clamp(top, 8f, h - lh - 8f);
        }
    }

    /// <summary>The computer's tab or button for this step, while the computer is open (null otherwise).</summary>
    static VisualElement PcElementFor(Step s)
    {
        string[] names = s == Step.LookUp ? (NoteOnly ? new[] { "detail-select" } : new[] { "book-search", "tab-catalogue" })
                       : s == Step.CardFill ? new[] { "tab-members" }
                       : s == Step.OrderBuy ? new[] { "detail-express", "tab-catalogue" } : null;
        if (names == null) return null;
        foreach (var doc in FindObjectsByType<UIDocument>())
        {
            if (!doc.name.Contains("PCOverlay")) continue;
            if (doc.rootVisualElement == null || !doc.isActiveAndEnabled) continue;
            foreach (var n in names)
            {
                var el = doc.rootVisualElement.Q(n);
                if (el != null && el.resolvedStyle.display != DisplayStyle.None && el.worldBound.width > 1f) return el;
            }
        }
        return null;
    }

    void AnimateSpot()
    {
        if (spot == null || spot.ClassListContains("hidden") || Time.unscaledTime < spotFrameAt) return;
        spotFrameAt = Time.unscaledTime + 0.14f;
        spot.RemoveFromClassList("f" + (spotFrame + 1));
        spotFrame = (spotFrame + 1) % 4;
        spot.AddToClassList("f" + (spotFrame + 1));
    }

    static Bounds BoundsOf(Component target)
    {
        var rends = target.GetComponentsInChildren<Renderer>().Where(r => !(r is ParticleSystemRenderer)).ToArray();
        if (rends.Length == 0) return new Bounds(target.transform.position + Vector3.up * 0.8f, Vector3.one * 1.5f);
        var b = rends[0].bounds;
        foreach (var r in rends) b.Encapsulate(r.bounds);
        return b;
    }

    void Say(string head, string body)
    {
        hideAt = 0f;
        title.text = head;
        text.text = body;
        card.RemoveFromClassList("hidden");
    }

    void HideCard()
    {
        hideAt = 0f;
        card?.AddToClassList("hidden");
    }

    // ---------------- First-time tips ----------------

    /// <summary>Every tip key (so a new game can forget them all).</summary>
    static readonly string[] HintKeys =
        { "seeker", "soldout", "damaged", "staffhelp", "pickup", "vip", "delivery", "mess", "reader", "checkout", "returns", "wrongbook", "express", "ask", "lockup" };

    readonly Queue<(string key, string text)> hints = new Queue<(string, string)>();

    /// <summary>A short tip the first time something happens (once per game). Held back while the tutorial runs,
    /// then shown in the notification feed.</summary>
    public static void Show(string key, string text)
    {
        var t = Instance;
        if (t == null || HoldsClock || PlayerPrefs.GetInt(HintPrefix + key, 0) == 1) return;
        if (t.hints.Any(h => h.key == key)) return;
        t.hints.Enqueue((key, text));
    }

    void ShowQueuedHint()
    {
        if (hints.Count == 0 || card == null || HoldsClock || PlayerLock.IsLocked) return; // not over a conversation or the PC
        if (MainMenuUI.Active) return;
        var (key, body) = hints.Dequeue();
        if (PlayerPrefs.GetInt(HintPrefix + key, 0) == 1) return;
        PlayerPrefs.SetInt(HintPrefix + key, 1);
        HUD.Tip(body); // in the notification feed, until clicked
    }
}
