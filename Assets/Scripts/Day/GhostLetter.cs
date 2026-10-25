using System;
using System.Linq;
using UnityEngine;

/// <summary>
/// The night letter (Day 3 on): Ada Whitmore, the library's founder, never finished her last book.
///  1. Day 3 morning: a folded letter in the return bin (no tip, no arrow). It points to "the man who taught this house
///     to keep time": the grandfather clock's plate says E. Harrow, 1891.
///  2. Search Harrow on the PC: "Notes on Clockwork" (Archive, quest-only). Take it off its shelf: a pressed note.
///  3. The note asks for three numbers: the bust (4), the lectern (9), the globe (2). They only show after the note.
///  4. The display case always has a lock (three wheels, any number of tries). 4-9-2 opens it: "look for me by the fire".
///  5. After closing, faint writing glows above the fireplace: type her name at the Archive typewriter.
///  6. The right name: next morning "The Last Chapter" is on the book cart; it goes on the empty Archive bookcase.
///  7. That night, once everyone has gone home, Elias Harrow walks in and waits at the desk for it. Give it: +10 reputation, Ada shelves the
///     returns at night from then on, the shelf gets a "Whitmore" sign. Keep it: the ghosts never come back, and a
///     collector buys it for 1000 coins on the PC Home page.
/// Every paper so far is re-read from the scroll beside the desk plate (GhostLetterUI). Puzzle steps save at once
/// (PlayerPrefs "GhostLetterLive"); the book, the visit and your choice save with the library each morning.
/// Added to the GameManager at runtime.
/// </summary>
public class GhostLetter : MonoBehaviour
{
    public enum Outcome { None, Given, Kept, Sold }
    public enum Place { Cart, Shelf, Hands }

    [Serializable]
    public class State
    {
        public bool letterRead, noteFound, caseOpen, fireSeen, nameTyped, slipSeen;
        public int typedDay, wrongNames;
        public bool delivered;
        public int deliveredDay;
        public Place bookAt;
        public Outcome outcome;
    }

    public const string NotesTitle = "Notes on Clockwork", AdaName = "Ada Whitmore", ChapterTitle = "The Last Chapter";
    const int LetterDay = 3, ChapterPrice = 1000, GiveReputation = 10;
    const float QuietSeconds = 2.5f; // the library has been empty this long after closing: then he walks in
    static readonly int[] Code = { 4, 9, 2 };
    const string LiveKey = "GhostLetterLive", GivenKey = "GhostLetterGiven";

    static GameManager GM => GameManager.Instance;
    static State Q => GM != null ? GM.quest : null;
    public static GhostLetter Instance { get; private set; }

    GhostLetterData data;
    BookData chapter;
    InspectInteractable clock, bust, lectern, globe, displayCase, fireplace, typewriter, whitmoreShelf;
    GameObject folio, sign, letterProp;
    TextMesh fireText;
    PlayerCarry carry;
    int clockClicks;
    bool visitorToday;
    Customer stranger; // Elias, while he's in the library tonight
    float quietSince = -1f;
    float nextTick;

    // ---------------- For other systems ----------------

    /// <summary>The ghosts are gone for good: you kept Ada's book.</summary>
    public static bool GhostsGone => Q != null && (Q.outcome == Outcome.Kept || Q.outcome == Outcome.Sold);
    /// <summary>Ada has her book back: she shelves the returns at night.</summary>
    public static bool AdaHelps => Q != null && Q.outcome == Outcome.Given;
    /// <summary>Her book is on the Whitmore shelf, waiting for Elias: a ghost stands there reading tonight.</summary>
    public static bool ReaderAtShelf => Q != null && Q.delivered && Q.outcome == Outcome.None && Q.bookAt == Place.Shelf;
    public static Transform WhitmoreShelf => Instance != null && Instance.whitmoreShelf != null ? Instance.whitmoreShelf.transform : null;
    public static GameObject AdaBody => Instance != null && Instance.data != null ? Instance.data.adaBody : null;
    /// <summary>The title menu's line once you've given the book back (any save, for good).</summary>
    public static string MenuTip => PlayerPrefs.GetInt(GivenKey, 0) == 1 ? "They're still reading." : null;
    /// <summary>You kept the book and haven't sold it yet (the PC Home page offers the sale).</summary>
    public static bool CanSell => Q != null && Q.outcome == Outcome.Kept;

    /// <summary>A new game: forget the puzzle steps saved during the day.</summary>
    public static void ForgetLive() => PlayerPrefs.DeleteKey(LiveKey);

    /// <summary>The letter sits on the return pile: clicking the bin reads it (true) before you can take books.</summary>
    public static bool TakeLetter()
    {
        var g = Instance;
        if (g == null || !g.LetterWaiting) return false;
        Q.letterRead = true;
        g.SaveLive();
        if (g.letterProp != null) g.letterProp.SetActive(false);
        GameAudio.Play(a => a.uiClick);
        g.ShowCurrentPaper();
        HUD.Tip("To read the letter again, click the scroll beside the desk plate.");
        return true;
    }

    bool LetterWaiting => Q != null && GM.day >= LetterDay && !Q.letterRead && !Tutorial.HoldsClock;

    // ---------------- Setting up ----------------

    void Awake()
    {
        Instance = this;
        data = Resources.Load<GhostLetterData>("GhostLetter");
        chapter = new BookData
        {
            Title = ChapterTitle, Author = AdaName, Subject = "History", Topic = "Memoir", Tag = "Archive",
            Abstract = "The last book Ada Whitmore wrote, finished at last.", ShelfLocation = "WHITMORE",
            Tier = BookTier.Legendary, BaseValue = ChapterPrice, Sellable = false, QuestOnly = true,
        };
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
        if (InspectInteractable.Intercept == Intercept) InspectInteractable.Intercept = null;
        if (carry != null) carry.Changed -= OnCarry;
    }

    void Start()
    {
        if (GM == null || Q == null) return;
        MergeLive();
        InspectInteractable.Intercept = Intercept;
        carry = GameObject.FindWithTag("Player")?.GetComponent<PlayerCarry>();
        if (carry != null) carry.Changed += OnCarry;

        clock = Prop("SM_GrandfatherClock");
        bust = Prop("SM_Bust_Marble");
        lectern = Prop("SM_Lectern");
        globe = Prop("SM_Globe");
        displayCase = Prop("SM_DisplayCase");
        fireplace = Prop("SM_Fireplace");
        typewriter = Prop("SM_Typewriter");
        whitmoreShelf = Prop("SM_Bookcase_Tall_2m");
        // The scenery first; a problem there must never stop the book from arriving below.
        try { BuildShelf(); BuildFireText(); BuildLetterProp(); }
        catch (Exception e) { Debug.LogException(e, this); }

        // Wherever it was last saved; in your hands overnight means back on the cart.
        if (Q.delivered && (Q.outcome == Outcome.None || Q.outcome == Outcome.Kept))
        {
            if (Q.bookAt == Place.Shelf) ShowFolio(true);
            else { Q.bookAt = Place.Cart; DeliveryCart.Instance?.Add(chapter); }
        }
        DeliverIfDue();
        RefreshSign();
        GhostLetterUI.Create(data);
    }

    static InspectInteractable Prop(string name) =>
        FindObjectsByType<InspectInteractable>(FindObjectsInactive.Include).FirstOrDefault(p => p.name == name);

    /// <summary>The empty Archive bookcase: Ada's book stands on its middle shelf, and its sign reads Whitmore once she has it.</summary>
    void BuildShelf()
    {
        if (whitmoreShelf == null) return;
        if (data != null && data.folio != null)
        {
            folio = Instantiate(data.folio, whitmoreShelf.transform, false);
            folio.name = "WhitmoreFolio";
            folio.transform.localPosition = new Vector3(0.45f, 0.98f, 0.2f); // standing on the middle shelf, in one bay
            folio.transform.localRotation = Quaternion.Euler(0f, 90f, 0f);   // cover to the room
            foreach (var c in folio.GetComponentsInChildren<Collider>()) Destroy(c);
            folio.SetActive(false);
        }
        var s = whitmoreShelf.transform.Find("ShelfSign");
        if (s != null) sign = s.gameObject;
    }

    void ShowFolio(bool on) { if (folio != null) folio.SetActive(on); }

    void RefreshSign()
    {
        if (sign == null || Q.outcome != Outcome.Given) return;
        sign.SetActive(true);
        foreach (var t in sign.GetComponentsInChildren<TextMesh>(true)) { t.text = "Whitmore"; t.gameObject.SetActive(true); }
    }

    /// <summary>The glowing words above the fireplace (only after closing, once the case is open).</summary>
    void BuildFireText()
    {
        if (fireplace == null) return;
        var font = FindObjectsByType<TextMesh>(FindObjectsInactive.Include).FirstOrDefault(t => t.font != null);
        var go = new GameObject("FireWriting");
        go.transform.SetParent(fireplace.transform, false);
        var b = fireplace.GetComponent<Renderer>().bounds;
        var front = fireplace.transform.forward;
        // Up on the chimney breast, nudged along the wall away from the grandfather clock, which stands beside the
        // fireplace and hides that end of the wall from the camera.
        var away = clock != null ? Vector3.ProjectOnPlane(b.center - clock.transform.position, front) : Vector3.zero;
        away.y = 0f;
        go.transform.position = new Vector3(b.center.x, b.max.y + 0.62f, b.center.z) - front * (b.extents.x * 0.9f) + away.normalized * 0.35f;
        go.transform.rotation = Quaternion.LookRotation(-front);
        fireText = go.AddComponent<TextMesh>();
        fireText.text = "my name is the\nlast thing I wrote.\ntype it where I\ntyped my books.";
        fireText.anchor = TextAnchor.MiddleCenter;
        fireText.alignment = TextAlignment.Center;
        fireText.characterSize = 0.04f;
        fireText.fontSize = 64;
        if (font != null)
        {
            fireText.font = font.font;
            go.GetComponent<MeshRenderer>().sharedMaterial = font.GetComponent<MeshRenderer>().sharedMaterial;
        }
        fireText.color = new Color(1f, 0.6f, 0.25f, 0f); // ember orange, faded in at night
    }

    /// <summary>A folded sheet of paper on top of the return pile (the bin's own hover text doesn't change): two thin
    /// halves, opened a little, in the palette's parchment band (the props' own material).</summary>
    void BuildLetterProp()
    {
        var bin = ReturnBin.Instance;
        var source = bin != null ? bin.GetComponentInChildren<Renderer>() : null;
        var palette = data != null && data.folio != null ? data.folio.GetComponentInChildren<Renderer>()?.sharedMaterial : null;
        if (source == null || palette == null) return;
        // Every vertex on one spot of T_Palette: band 6 of 16 (parchment), a light shade.
        var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
        var sheet = Instantiate(cube.GetComponent<MeshFilter>().sharedMesh);
        Destroy(cube);
        var uv = new Vector2[sheet.vertexCount];
        for (int i = 0; i < uv.Length; i++) uv[i] = new Vector2(0.75f, 1f - 5.5f / 16f);
        sheet.uv = uv;
        letterProp = new GameObject("FoldedLetter");
        var b = source.bounds;
        letterProp.transform.position = new Vector3(b.center.x, b.max.y + 0.012f, b.center.z);
        letterProp.transform.rotation = Quaternion.Euler(0f, 25f, 0f);
        for (int i = 0; i < 2; i++)
        {
            var half = new GameObject("Half", typeof(MeshFilter), typeof(MeshRenderer));
            half.GetComponent<MeshFilter>().sharedMesh = sheet;
            half.transform.SetParent(letterProp.transform, false);
            half.transform.localScale = new Vector3(0.11f, 0.004f, 0.16f);
            half.transform.localPosition = new Vector3(i == 0 ? -0.054f : 0.054f, i == 0 ? 0f : 0.012f, 0f);
            half.transform.localRotation = Quaternion.Euler(0f, 0f, i == 0 ? 0f : -14f); // one half lifted at the fold
            var r = half.GetComponent<MeshRenderer>();
            r.sharedMaterial = palette;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }
        letterProp.SetActive(LetterWaiting);
    }

    // ---------------- The props ----------------

    bool Night => GM.CurrentPhase == GameManager.Phase.Closed;

    bool Intercept(InspectInteractable p)
    {
        if (Q == null || p == null || Tutorial.HoldsClock) return false;
        if (p == clock)
        {
            // The maker's plate is there from the start; once the letter's read, it's the first thing you notice.
            clockClicks++;
            if (Q.letterRead && !Q.noteFound || clockClicks % 2 == 1) { Bubble(p, "A brass plate on the door: \"E. Harrow, 1891.\""); return true; }
            return false;
        }
        if (Q.noteFound && p == bust) { Bubble(p, "A tiny number is scratched under the chin: 4."); return true; }
        if (Q.noteFound && p == lectern) { Bubble(p, "The book on the lectern lies open at page 9."); return true; }
        if (Q.noteFound && p == globe) { Bubble(p, "One small island is circled in ink, with a 2 beside it."); return true; }
        if (p == displayCase)
        {
            if (Q.caseOpen) Bubble(p, "The little lock hangs open. The card is gone; only dust inside now.");
            else GhostLetterUI.Instance?.OpenLock(TryCode);
            return true;
        }
        if (p == fireplace && Night && Q.caseOpen && !Q.nameTyped)
        {
            Bubble(p, "Faint words glow above the mantel: \"My name is the last thing I wrote. Type it where I typed my books.\"", 5f);
            if (!Q.fireSeen) { Q.fireSeen = true; SaveLive(); ShowCurrentPaper(); }
            return true;
        }
        if (p == typewriter)
        {
            if (Q.fireSeen && !Q.nameTyped) { GhostLetterUI.Instance?.OpenTypewriter(TryName, NameHint); return true; }
            if (Q.nameTyped) { Bubble(p, "The keys rest. Somewhere, a page is drying."); return true; }
            return false;
        }
        if (p == whitmoreShelf) return ShelfClick(p);
        return false;
    }

    static void Bubble(ClickInteractable p, string text, float seconds = 3.2f) => WorldPrompts.Instance?.ShowBubble(p, text, seconds);

    bool TryCode(int[] digits)
    {
        if (digits == null || !digits.SequenceEqual(Code)) return false;
        Q.caseOpen = true;
        SaveLive();
        GameAudio.Play(a => a.cardStamp);
        return true;
    }

    /// <summary>After the case opens: the card inside, as the latest paper.</summary>
    public void AfterCaseOpened() => ShowCurrentPaper();

    bool TryName(string typed)
    {
        string Clean(string s) => string.Join(" ", (s ?? "").Trim().ToLowerInvariant().Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries));
        if (Clean(typed) == Clean(AdaName))
        {
            Q.nameTyped = true;
            Q.typedDay = GM.day;
            SaveLive();
            return true;
        }
        Q.wrongNames++;
        SaveLive();
        return false;
    }

    /// <summary>Every third wrong try, the keys give away one more letter of her name.</summary>
    string NameHint() => AdaName.Substring(0, Mathf.Min(AdaName.Length, Q.wrongNames / 3));

    /// <summary>The typewriter finished its "thank you": the latest paper is its page.</summary>
    public void AfterNameTyped() => ShowCurrentPaper();

    bool ShelfClick(InspectInteractable p)
    {
        if (carry == null || Q.outcome == Outcome.Sold) return false;
        int held = carry.Find((b, c) => b == chapter);
        if (held >= 0)
        {
            carry.TakeAt(held);
            Q.bookAt = Place.Shelf;
            ShowFolio(true);
            ShelfUI.Instance?.PlayTake();
            Bubble(p, "You stand it in the empty case. It looks as if it was always meant to be there.");
            return true;
        }
        if (Q.bookAt == Place.Shelf && Q.delivered && Q.outcome != Outcome.Given)
        {
            if (carry.IsFull) { Bubble(p, "Your hands are full."); return true; }
            carry.PickUp(chapter, BookCondition.Good);
            Q.bookAt = Place.Hands;
            ShowFolio(false);
            ShelfUI.Instance?.PlayTake();
            return true;
        }
        if (Q.outcome == Outcome.Given) { Bubble(p, "Whitmore. Someone has dusted this shelf."); return true; }
        return false;
    }

    // ---------------- Books in your hands ----------------

    void OnCarry(BookData top)
    {
        if (Q == null || carry == null) return;
        if (Q.letterRead && !Q.noteFound && carry.Find((b, c) => b.Title == NotesTitle) >= 0)
        {
            Q.noteFound = true;
            SaveLive();
            GameAudio.Play(a => a.uiClick);
            ShowCurrentPaper();
        }
        if (carry.Find((b, c) => b == chapter) >= 0)
        {
            Q.bookAt = Place.Hands;
            if (!Q.slipSeen) { Q.slipSeen = true; SaveLive(); ShowCurrentPaper(); }
        }
    }

    // ---------------- Every few frames ----------------

    void Update()
    {
        if (Q == null || Time.unscaledTime < nextTick) return;
        nextTick = Time.unscaledTime + 0.25f;
        if (letterProp != null && letterProp.activeSelf != LetterWaiting) letterProp.SetActive(LetterWaiting);
        DeliverIfDue();
        TrackBook();
        UpdateFireText();
        TryVisitor();
    }

    /// <summary>The morning after you typed her name, her book is on the cart (checked when the day starts and again
    /// every few frames, so nothing at load time can make it miss).</summary>
    void DeliverIfDue()
    {
        if (!Q.nameTyped || Q.delivered || GM.day <= Q.typedDay || DeliveryCart.Instance == null) return;
        Q.delivered = true;
        Q.deliveredDay = GM.day;
        Q.bookAt = Place.Cart;
        DeliveryCart.Instance.Add(chapter);
    }

    /// <summary>Where Ada's book is now: in your hands, on the cart or on her shelf (never lost: back to the cart).</summary>
    void TrackBook()
    {
        if (!Q.delivered || !(Q.outcome == Outcome.None || Q.outcome == Outcome.Kept)) return;
        var cart = DeliveryCart.Instance;
        if (carry != null && carry.Find((b, c) => b == chapter) >= 0) Q.bookAt = Place.Hands;
        else if (cart != null && cart.Has(chapter)) Q.bookAt = Place.Cart;
        else if (folio != null && folio.activeSelf) Q.bookAt = Place.Shelf;
        else if (cart != null) { cart.Add(chapter); Q.bookAt = Place.Cart; }
    }

    void UpdateFireText()
    {
        if (fireText == null) return;
        bool on = Night && Q.fireSeen && !Q.nameTyped && !GhostsGone; // only once you've looked by the fire
        var c = fireText.color;
        float target = on ? 0.75f + 0.2f * Mathf.Sin(Time.time * 2.3f) : 0f;
        c.a = Mathf.MoveTowards(c.a, target, 0.08f);
        fireText.color = c;
    }

    // ---------------- Elias Harrow ----------------

    /// <summary>Tonight is his night: the book has come home and he hasn't had his answer yet (from the night it
    /// arrives; if you lock up before he walks in, the next night).</summary>
    bool StrangerTonight => Q.delivered && Q.outcome == Outcome.None && GM.day >= Q.deliveredDay;

    /// <summary>He can be brought in at all (otherwise nothing waits for him, so the night can never get stuck).</summary>
    bool CanBring => data != null && data.stranger != null && CustomerSpawner.Instance != null && CounterQueue.Instance != null;

    /// <summary>Elias is still to come tonight, or is in the library now: the night ghosts wait (GhostNight) and the
    /// front ropes stay open for him (EndDayDoor). If he came and somehow went without an answer, nothing waits: he
    /// comes again the next night.</summary>
    public static bool StrangerDue => Instance != null && Q != null && Instance.StrangerTonight && Instance.CanBring
                                      && (!Instance.visitorToday || Instance.stranger != null);

    /// <summary>After closing, once the last visitor and the staff have gone home, the front doors open and Elias walks
    /// in alone, straight to the desk.</summary>
    void TryVisitor()
    {
        bool empty = GM.CurrentPhase == GameManager.Phase.Closed && !Tutorial.HoldsClock
                     && FindObjectsByType<Customer>(FindObjectsInactive.Exclude).Length == 0
                     && FindObjectsByType<StaffWorker>(FindObjectsInactive.Exclude).Length == 0;
        if (visitorToday || !StrangerTonight || !empty) { quietSince = -1f; return; }
        if (quietSince < 0f) quietSince = Time.unscaledTime;
        if (Time.unscaledTime - quietSince < QuietSeconds) return;
        if (CustomerSpawner.Instance == null || data == null || data.stranger == null) return;
        DayStory.Visit visit = null;
        visit = new DayStory.Visit
        {
            name = "???", teaser = "...", atDesk = true,
            staysAfterClosing = true, // it's after closing already: he waits until you talk to him (the day can't end before that)
            talk = c => Talk(),
            after = (c, last) =>
            {
                visit.next = null;
                if (last != null && last.StartsWith("give")) Give(c);
                else if (last != null && last.StartsWith("keep")) Keep();
                else { visit.next = visit; visit.lingerUntil = 0f; } // no book in your hands: back to the desk, waiting
            },
        };
        stranger = CustomerSpawner.Instance.SpawnStory(visit, data.stranger);
        visitorToday = stranger != null;
    }

    bool HoldingChapter => carry != null && carry.Find((b, c) => b == chapter) >= 0;
    static string Me => GameManager.PlayerName;

    DialogueAsset Talk()
    {
        var s = new DayStory.Script();
        s.Say("Hello there.", "hello");
        if (!HoldingChapter)
        {
            s.Say(Q.bookAt == Place.Shelf
                ? "You found something of mine. It's on her shelf, isn't it? Bring it to me. I'll wait."
                : "You found something of mine. It's on the cart, isn't it? Bring it to me. I'll wait.", "wait");
            return s.d;
        }
        s.Say($"You found it, {Me}. Her last chapter.", "found")
         .Say("Ada wrote it for me. I have waited a very long time to read how it ends.", "waited")
         .Choose("Hand him the book.", "give", "Keep it.", "keep")
         .Say($"Thank you, {Me}. She can rest easy now.", "give", "give2")
         .Say("She was always tidying. Leave the returns in the bin at night, and you may find them back on their shelves by morning.", "give2", "give3")
         .Say("Keep the lamps lit. She likes the light.", "give3", DialogueAsset.End)
         .Say("I see.", "keep", "keep2")
         .Say("A book like that would fetch a fine price, wouldn't it? I know you'll sell it.", "keep2", "keep3")
         .Say("Greedy little thing, aren't you? Good day.", "keep3", DialogueAsset.End);
        return s.d;
    }

    void Give(Customer c)
    {
        int i = carry != null ? carry.Find((b, cc) => b == chapter) : -1;
        if (i >= 0) carry.TakeAt(i);
        ShowFolio(false);
        Q.outcome = Outcome.Given;
        PlayerPrefs.SetInt(GivenKey, 1);
        PlayerPrefs.Save();
        GM.AddReputation(GiveReputation);
        HUD.SkillPop(c.transform.position + Vector3.up * 2.2f, $"+{GiveReputation} reputation");
        RequestLog.Note(c, $"Gave <color=#D67840>\"{ChapterTitle}\"</color> to Elias Harrow");
        RefreshSign();
        c.gameObject.AddComponent<FadeAway>(); // he walks to the front doors, and fades there like the others
    }

    void Keep()
    {
        Q.outcome = Outcome.Kept;
        RequestLog.News($"You kept <color=#D67840>\"{ChapterTitle}\"</color>. The library feels emptier at night.");
    }

    /// <summary>The PC Home page's "Sell": a collector buys Ada's book wherever it is.</summary>
    public static void Sell()
    {
        var g = Instance;
        if (g == null || !CanSell) return;
        int i = g.carry != null ? g.carry.Find((b, c) => b == g.chapter) : -1;
        if (i >= 0) g.carry.TakeAt(i);
        var cart = DeliveryCart.Instance;
        cart?.Remove(g.chapter);
        g.ShowFolio(false);
        Q.outcome = Outcome.Sold;
        GM.AddCoins(ChapterPrice);
        GM.Today.Earn(GameManager.DayStats.Source.Other, ChapterPrice);
        RequestLog.Money("A collector", $"Sold \"{ChapterTitle}\"", "Sale", ChapterPrice);
        GameAudio.Play(a => a.coinSpend);
    }

    // ---------------- Papers ----------------

    string Signature =>
        !Q.noteFound ? "????????????" : !Q.caseOpen ? "A?? W???????" : !Q.fireSeen ? "Ada W???????" : AdaName;

    /// <summary>The newest paper you've found (the scroll opens this one).</summary>
    public (string body, string signature) CurrentPaper()
    {
        if (Q.delivered && Q.slipSeen) return ("A slip tucked inside the cover:\n\n\"The empty case in the Archive.\"", AdaName);
        if (Q.nameTyped) return ("thank you.\n\nmy book comes home tomorrow. put it in the empty case among the old papers, where my name belongs.", AdaName);
        if (Q.fireSeen) return ("Words above the fireplace, glowing faintly after dark:\n\n\"My name is the last thing I wrote. Type it where I typed my books.\"", Signature);
        if (Q.caseOpen) return ("A card inside the display case:\n\n\"Look for me by the fire after the doors close.\"", Signature);
        if (Q.noteFound) return ("A note pressed between the pages:\n\n\"Three numbers open my glass case. First the stone face, then the open page, then the turning world.\"", Signature);
        return ($"Dear {Me},\n\nI never finished my last book. The rest of it is hidden with the man who taught this house to keep time. Find what he wrote.\n\nI will know when you do.", Signature);
    }

    /// <summary>The scroll beside the desk plate: from reading the letter until the book has gone.</summary>
    public static bool HasPapers => Q != null && Q.letterRead && Q.outcome == Outcome.None;

    public void ShowCurrentPaper()
    {
        var (body, signature) = CurrentPaper();
        GhostLetterUI.Instance?.ShowPaper(body, signature);
    }

    // ---------------- Saving the steps straight away ----------------

    [Serializable] class Live { public bool letterRead, noteFound, caseOpen, fireSeen, nameTyped, slipSeen; public int typedDay, wrongNames; }

    void SaveLive()
    {
        var l = new Live { letterRead = Q.letterRead, noteFound = Q.noteFound, caseOpen = Q.caseOpen, fireSeen = Q.fireSeen,
                           nameTyped = Q.nameTyped, slipSeen = Q.slipSeen, typedDay = Q.typedDay, wrongNames = Q.wrongNames };
        PlayerPrefs.SetString(LiveKey, JsonUtility.ToJson(l));
        PlayerPrefs.Save();
    }

    /// <summary>Steps solved since this morning's save (you quit mid-day) are kept.</summary>
    void MergeLive()
    {
        if (!PlayerPrefs.HasKey(LiveKey)) return;
        var l = JsonUtility.FromJson<Live>(PlayerPrefs.GetString(LiveKey));
        if (l == null) return;
        Q.letterRead |= l.letterRead; Q.noteFound |= l.noteFound; Q.caseOpen |= l.caseOpen; Q.fireSeen |= l.fireSeen; Q.slipSeen |= l.slipSeen;
        if (l.nameTyped && !Q.nameTyped) { Q.nameTyped = true; Q.typedDay = l.typedDay; }
        Q.wrongNames = Mathf.Max(Q.wrongNames, l.wrongNames);
    }

    /// <summary>Elias Harrow, once he has the book: he walks away from the desk as himself (you see his face), and
    /// only fades like the night readers once he's near the front doors.</summary>
    class FadeAway : MonoBehaviour
    {
        // The doors are only a few steps from the desk: he turns away as himself, then fades over the last two metres.
        const float DoorDistance = 2f, FadeSeconds = 1.2f, Fallback = 12f;
        float t, since = -1f;
        Renderer[] renderers;
        MaterialPropertyBlock block;
        static readonly int AlphaId = Shader.PropertyToID("_Alpha");

        void Update()
        {
            t += Time.deltaTime;
            if (since < 0f)
            {
                var door = CounterQueue.Instance != null ? CounterQueue.Instance.exit : null;
                var flat = door != null ? door.position - transform.position : Vector3.zero;
                flat.y = 0f;
                if (door != null && flat.magnitude >= DoorDistance && t < Fallback) return; // still walking to the doors
                since = 0f;
            }
            since += Time.deltaTime;
            if (renderers == null)
            {
                var ghost = Resources.Load<Material>("M_Ghost");
                if (ghost == null) { Destroy(this); return; }
                block = new MaterialPropertyBlock();
                foreach (var r in GetComponentsInChildren<Renderer>())
                {
                    if (r.name.Contains("BlobShadow")) { r.enabled = false; continue; }
                    var mats = new Material[r.sharedMaterials.Length];
                    for (int i = 0; i < mats.Length; i++) mats[i] = ghost;
                    r.sharedMaterials = mats;
                }
                renderers = GetComponentsInChildren<Renderer>();
            }
            float a = Mathf.Clamp01(1f - since / FadeSeconds);
            foreach (var r in renderers)
            {
                if (r == null || !r.enabled) continue;
                r.GetPropertyBlock(block);
                block.SetFloat(AlphaId, a);
                r.SetPropertyBlock(block);
            }
            if (a <= 0f) foreach (var r in renderers) if (r != null) r.enabled = false;
        }
    }
}
