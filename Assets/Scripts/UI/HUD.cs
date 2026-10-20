using System.Collections.Generic;
using System.Collections;
using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// Bottom bar (day, clock, reputation), the coin purse in the top right, and the carried-book slot. Lives on the GameUI object and adds the notebook.
/// </summary>
[RequireComponent(typeof(UIDocument))]
public class HUD : MonoBehaviour
{
    static HUD instance;
    VisualElement fade;
    Label pin;
    bool upgradeBannerShown;

    /// <summary>Fades to black and stays there; <paramref name="atBlack"/> runs once it's dark. FadeIn() lifts it.</summary>
    public static void FadeOut(System.Action atBlack)
    {
        if (instance == null || instance.fade == null) { atBlack?.Invoke(); return; }
        var f = instance.fade;
        f.pickingMode = PickingMode.Position;
        f.AddToClassList("on");
        f.schedule.Execute(() => atBlack?.Invoke()).ExecuteLater(600);
    }

    /// <summary>Starts the screen black (arriving from another scene), then fades in.</summary>
    public static void StartBlack()
    {
        if (instance == null || instance.fade == null) return;
        var f = instance.fade;
        f.style.opacity = 1f; // no fade-to-black flash first
        f.AddToClassList("on");
        f.schedule.Execute(() => { f.style.opacity = StyleKeyword.Null; f.RemoveFromClassList("on"); f.pickingMode = PickingMode.Ignore; }).ExecuteLater(300);
    }

    public static void FadeIn()
    {
        if (instance == null || instance.fade == null) return;
        var f = instance.fade;
        f.schedule.Execute(() => { f.RemoveFromClassList("on"); f.pickingMode = PickingMode.Ignore; }).ExecuteLater(150);
    }

    /// <summary>Hides the whole game UI (while another scene, like the interview room, is showing).</summary>
    public static void SetHidden(bool hidden)
    {
        if (instance == null) return;
        instance.GetComponent<UIDocument>().rootVisualElement.style.display = hidden ? DisplayStyle.None : DisplayStyle.Flex;
    }

    /// <summary>Fades to black, runs <paramref name="atBlack"/>, then fades back in.</summary>
    public static void FadeThen(System.Action atBlack)
    {
        if (instance == null || instance.fade == null) { atBlack?.Invoke(); return; }
        var f = instance.fade;
        f.pickingMode = PickingMode.Position; // no clicks while dark
        f.AddToClassList("on");
        f.schedule.Execute(() =>
        {
            atBlack?.Invoke();
            f.schedule.Execute(() => { f.RemoveFromClassList("on"); f.pickingMode = PickingMode.Ignore; }).ExecuteLater(150);
        }).ExecuteLater(600);
    }
    Label dayLabel, clockLabel, coinLabel, carryLabel;
    VisualElement carrySlot, carryBooks;
    PlayerCarry carry;
    string lastClock;
    Label banner, repLabel, repDelta;
    float repDeltaUntil;
    VisualElement stamina, staminaFill;
    PlayerMover mover;
    float bannerUntil;

    void OnEnable()
    {
        var root = GetComponent<UIDocument>().rootVisualElement;
        dayLabel = root.Q<Label>("hud-day");
        clockLabel = root.Q<Label>("hud-clock");
        coinLabel = root.Q<Label>("hud-coins");
        carryLabel = root.Q<Label>("hud-carry");
        carrySlot = root.Q("hud-carry-slot");
        instance = this;
        fade = new VisualElement { name = "screen-fade", pickingMode = PickingMode.Ignore };
        fade.AddToClassList("screen-fade");
        root.Add(fade); // last child: drawn over everything on this panel

        // Pinned notice under the HUD on a room's opening day (stays until noon).
        pin = new Label { name = "hud-pin", pickingMode = PickingMode.Ignore };
        pin.AddToClassList("hud-pin");
        pin.AddToClassList("hidden");
        root.Q("hud")?.Add(pin);

        // Phase banner and the little "+12" next to the coins.
        banner = new Label { name = "hud-banner", pickingMode = PickingMode.Ignore };
        banner.AddToClassList("hud-banner");
        banner.AddToClassList("hidden");
        root.Q("hud")?.Add(banner);
        feed = new VisualElement { name = "hud-feed", pickingMode = PickingMode.Ignore };
        feed.AddToClassList("hud-feed");
        root.Q("hud")?.Add(feed);
        purse = root.Q("hud-purse");
        if (GetComponent<Tutorial>() == null) gameObject.AddComponent<Tutorial>(); // the guided first day and first-time tips
        hudLayer = root.Q("hud");
        repLabel = root.Q<Label>("hud-rep");
        repDelta = new Label { name = "hud-rep-delta", pickingMode = PickingMode.Ignore };
        repDelta.AddToClassList("hidden");
        root.Q(className: "hud-rep")?.Add(repDelta);
    }

    /// <summary>Puts a message in the notification feed (top-left). Each line becomes its own short card; long ones
    /// are split. Long-lived messages (7 s or more: jobs, VIPs, events) count as urgent: they stay longer, with an
    /// orange edge, until clicked. Every card is also written to the Log.</summary>
    public void ShowBanner(string text, float seconds = 4f)
    {
        bannerUntil = Time.time + seconds; // still used to space out reminders
        var parts = SplitForFeed(text);
        for (int i = parts.Count - 1; i >= 0; i--) Notify(parts[i], IconFor(parts[i]), seconds >= 7f, true); // first line ends up on top
    }

    // ---------------- Notification feed ----------------

    class Note { public VisualElement el; public float until, born; public bool urgent, tip; }
    readonly List<Note> notes = new List<Note>();
    VisualElement feed;
    const int MaxNotes = 6; // room for the whole morning summary
    const float NoteSeconds = 14f, UrgentSeconds = 30f, StaffSeconds = 6f;
    /// <summary>True while the pointer is over a notification (so the click doesn't also reach the library).</summary>
    public static bool PointerOverFeed { get; private set; }

    // Waiting to be shown: one card every FeedGap seconds, and none while a tutorial card is up.
    readonly List<(string text, string icon, bool urgent, float seconds)> pending = new List<(string, string, bool, float)>();
    float nextCardAt;
    const float FeedGap = 3f; // one new card every 3 s, so each can be read
    const int MaxPending = 10;

    void Notify(string text, string icon, bool urgent, bool log, float seconds = NoteSeconds)
    {
        if (feed == null || string.IsNullOrEmpty(text)) return;
        if (Tutorial.HoldsClock) return; // the tutorial's practice day: its card says it all (no delivery/upgrade cards after)
        // The same message already showing or waiting: once is enough.
        if (pending.Exists(p => p.text == text) || notes.Exists(n => !n.tip && n.el.Q<Label>(className: "feed-text")?.text == text)) return;
        if (log) RequestLog.News(text); // in the Log straight away, even if the card waits
        pending.Add((text, icon, urgent, seconds));
        // A long wait (a tutorial step): drop the oldest ordinary ones; they're in the Log.
        while (pending.Count > MaxPending)
        {
            int i = pending.FindIndex(p => !p.urgent && p.icon != "tip");
            pending.RemoveAt(i >= 0 ? i : 0);
        }
    }

    /// <summary>A tip (the first time something happens): in the feed with a lamp, until you click it away.</summary>
    public static void Tip(string text)
    {
        if (instance != null) instance.Notify(Highlight(text), "tip", false, false);
    }

    // Words worth spotting at a glance in a tip: where to go and what to press.
    static readonly string[] TipWords = { "computer", "Catalogue", "Members", "Shop", "Notes", "return bin", "repair desk", "Repair desk",
        "book cart", "cart", "shelf", "shelves", "reserve", "Order", "Express", "Rare Books", "Archive", "Day Report", "library card",
        "card", "hint", "click", "front ropes", "VIP", "reputation", "tidy" };

    /// <summary>Highlights the key words of a tip (unless it's already coloured).</summary>
    static string Highlight(string text)
    {
        if (string.IsNullOrEmpty(text) || text.Contains("<")) return text;
        string pattern = @"\b(" + string.Join("|", System.Linq.Enumerable.Select(TipWords, System.Text.RegularExpressions.Regex.Escape)) + @")\b";
        return System.Text.RegularExpressions.Regex.Replace(text, pattern, m => $"<color=#D67840>{m.Value}</color>", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
    }

    void ShowCard(string text, string icon, bool urgent, float seconds)
    {
        bool tip = icon == "tip";
        var card = new VisualElement();
        card.AddToClassList("feed-card");
        card.EnableInClassList("urgent", urgent);
        card.EnableInClassList("tip", tip);
        var pic = new VisualElement { pickingMode = PickingMode.Ignore };
        pic.AddToClassList("feed-icon");
        pic.AddToClassList("nf-" + icon);
        card.Add(pic);
        var label = new Label(text) { pickingMode = PickingMode.Ignore };
        label.AddToClassList("feed-text");
        if (tip)
        {
            // "TIP" heading over the text.
            var col = new VisualElement { pickingMode = PickingMode.Ignore };
            col.AddToClassList("feed-col");
            var head = new Label("TIP  ·  click to close") { pickingMode = PickingMode.Ignore };
            head.AddToClassList("feed-tip-head");
            col.Add(head);
            col.Add(label);
            card.Add(col);
        }
        else card.Add(label);
        var note = new Note { el = card, urgent = urgent, tip = tip, born = Time.unscaledTime + notes.Count * 0.0001f,
                              until = tip ? float.MaxValue : Time.unscaledTime + (urgent ? UrgentSeconds : seconds) };
        card.RegisterCallback<ClickEvent>(_ => Dismiss(note));
        card.RegisterCallback<PointerEnterEvent>(_ => PointerOverFeed = true);
        card.RegisterCallback<PointerLeaveEvent>(_ => PointerOverFeed = false);
        feed.Insert(0, card);
        notes.Insert(0, note);
        card.schedule.Execute(() => card.AddToClassList("in")).ExecuteLater(16); // slide in
        // Too many: the oldest ordinary one goes first; urgent ones only if there's nothing else.
        while (notes.Count > MaxNotes)
        {
            // The oldest ordinary message goes (an earlier line of a multi-line message counts as older).
            Note old = null;
            foreach (var n in notes) if (!n.urgent && !n.tip && (old == null || n.born < old.born)) old = n; // tips stay until clicked
            if (old == null) old = notes[notes.Count - 1];
            Remove(old);
        }
    }

    void Dismiss(Note n) { if (notes.Contains(n)) Remove(n); }

    void Remove(Note n)
    {
        notes.Remove(n);
        n.el.RemoveFromClassList("in");
        n.el.schedule.Execute(() => n.el.RemoveFromHierarchy()).ExecuteLater(250); // fade out
        if (notes.Count == 0) PointerOverFeed = false;
    }

    void UpdateFeed()
    {
        // A tutorial card or tip is up (same corner): hide the feed and hold its timers until it's gone.
        bool tip = Tutorial.CardShowing;
        feed.style.display = tip ? DisplayStyle.None : DisplayStyle.Flex;
        if (tip) { foreach (var n in notes) n.until += Time.unscaledDeltaTime; return; }
        for (int i = notes.Count - 1; i >= 0; i--) if (Time.unscaledTime > notes[i].until) Remove(notes[i]);
        // After the tutorial card, and one at a time so the panel has room to breathe.
        if (pending.Count == 0 || Time.unscaledTime < nextCardAt) return;
        var (text, icon, urgent, seconds) = pending[0];
        pending.RemoveAt(0);
        ShowCard(text, icon, urgent, seconds);
        nextCardAt = Time.unscaledTime + FeedGap;
    }

    /// <summary>One card per line; a line over ~60 letters is split at a sentence break near its middle.</summary>
    static List<string> SplitForFeed(string text)
    {
        var parts = new List<string>();
        foreach (var raw in (text ?? "").Split('\n'))
        {
            string line = raw.Trim();
            if (line.Length == 0) continue;
            string plain = System.Text.RegularExpressions.Regex.Replace(line, "<[^>]*>", "");
            if (plain.Length > 60 && !line.Contains("<"))
            {
                int best = -1;
                for (int i = 1; i < line.Length - 2; i++)
                    if ((line[i] == '.' || line[i] == '!' || line[i] == '?') && line[i + 1] == ' '
                        && (best < 0 || Mathf.Abs(i - line.Length / 2) < Mathf.Abs(best - line.Length / 2))) best = i;
                if (best > 10 && best < line.Length - 10)
                {
                    parts.Add(line.Substring(0, best + 1).Trim());
                    parts.Add(line.Substring(best + 1).Trim());
                    continue;
                }
            }
            parts.Add(line);
        }
        return parts;
    }

    /// <summary>A pixel icon for what a message is about.</summary>
    static string IconFor(string text)
    {
        string s = text.ToLowerInvariant();
        if (s.StartsWith("day ") || s.Contains("closing") || s.Contains("closed") || s.Contains("lunch") || s.Contains("o'clock")) return "clock";
        if (s.Contains("vip") || s.Contains("rare") || s.Contains("archive")) return "key";
        if (s.Contains("coin") || s.Contains("afford")) return "coin";
        if (s.Contains("reputation") || s.Contains("goal")) return "star";
        if (s.Contains("reserv") || s.Contains("order") || s.Contains("book club") || s.Contains("copies")) return "books";
        if (s.Contains("staff") || s.Contains("interview") || s.Contains("hire")) return "broom";
        return "news";
    }

    /// <summary>"37/60" while the Dean's goal is on (gold once reached), just the reputation after the ending.</summary>
    void ShowReputation()
    {
        var gm = GameManager.Instance;
        if (gm == null || repLabel == null) return;
        repLabel.text = gm.GoalActive ? $"{gm.Reputation}/{gm.ReputationGoal}" : gm.Reputation.ToString();
        repLabel.parent.EnableInClassList("goal-met", gm.GoalActive && gm.Reputation >= gm.ReputationGoal);
    }

    float nextRepSound, repFlushAt;
    int repGained;

    void FlushReputation()
    {
        if (repFlushAt <= 0f || Time.unscaledTime < repFlushAt) return;
        int n = repGained;
        repGained = 0; repFlushAt = 0f;
        if (n <= 0) return;
        if (Time.unscaledTime >= nextRepSound) { GameAudio.Play(a => a.reputationUp, 0.7f); nextRepSound = Time.unscaledTime + 1f; }
    }

    void OnReputation(int total, int delta)
    {
        ShowReputation();
        // Reputation up: gather gains that come together, then one green "+N reputation" and one chime (see FlushReputation).
        if (delta > 0) { repGained += delta; if (repFlushAt <= 0f) repFlushAt = Time.unscaledTime + 0.25f; }
        if (repDelta == null) return;
        repDelta.text = delta > 0 ? $"+{delta}" : delta.ToString();
        repDelta.EnableInClassList("negative", delta < 0);
        repDelta.RemoveFromClassList("hidden");
        repDeltaUntil = Time.time + 1.6f;
        if (delta < 0) repLabel.parent.experimental.animation.Scale(1.18f, 90).OnCompleted(() => repLabel.parent.experimental.animation.Scale(1f, 140));
    }

    // ---------------- Coins: the purse in the top right ----------------

    VisualElement purse, hudLayer;
    Coroutine shaking;

    /// <summary>Paid: coins fly up from the librarian into the purse, which shakes as each one lands and counts up,
    /// and a big "+12" floats up beside it (the coin sound plays from GameAudio). Spending: a shake and a red "-40".</summary>
    void OnCoins(int total, int delta)
    {
        // No effects until the HUD has been laid out (e.g. coins changed on the very first frame).
        bool ready = purse != null && hudLayer != null && isActiveAndEnabled && purse.worldBound.width > 0f && !float.IsNaN(purse.worldBound.width);
        if (delta <= 0 || !ready) coinLabel.text = total.ToString();
        if (delta == 0 || !ready) return;
        StartCoroutine(Pop(delta));
        if (delta > 0) StartCoroutine(FlyCoins(delta));
        else Shake(0.8f);
    }

    void Shake(float strength)
    {
        if (shaking != null) StopCoroutine(shaking);
        shaking = StartCoroutine(Shaking(strength));
    }

    /// <summary>A wobble that settles, with a little swell.</summary>
    IEnumerator Shaking(float strength)
    {
        const float duration = 0.45f;
        for (float t = 0f; t < 1f; t += Time.unscaledDeltaTime / duration)
        {
            float fade = 1f - t;
            purse.style.rotate = new Rotate(new Angle(Mathf.Sin(t * 40f) * 12f * strength * fade));
            float s = 1f + 0.25f * strength * fade * fade;
            purse.style.scale = new Scale(new Vector3(s, s, 1f));
            yield return null;
        }
        purse.style.rotate = StyleKeyword.Null;
        purse.style.scale = StyleKeyword.Null;
        shaking = null;
    }

    /// <summary>Where on the HUD the librarian is (the money comes from the counter), or the middle of the screen.</summary>
    Vector2 FlyStart()
    {
        float w = hudLayer.resolvedStyle.width, h = hudLayer.resolvedStyle.height;
        var cam = Camera.main;
        var player = GameObject.FindWithTag("Player");
        if (cam != null && player != null)
        {
            var vp = cam.WorldToViewportPoint(player.transform.position + Vector3.up * 1.4f);
            if (vp.z > 0f && vp.x > 0f && vp.x < 1f && vp.y > 0f && vp.y < 1f) return new Vector2(vp.x * w, (1f - vp.y) * h);
        }
        return new Vector2(w * 0.5f, h * 0.6f);
    }

    Vector2 PurseCoin()
    {
        var icon = purse.Q(className: "icon-coin") ?? purse;
        return hudLayer.WorldToLocal(icon.worldBound.center);
    }

    /// <summary>More coins for more money (3 for a little, up to 24 for a lot), one after another, faster when
    /// there are many: each one jingles as it drops in the purse.</summary>
    IEnumerator FlyCoins(int delta)
    {
        int n = Mathf.Clamp(2 + delta / 4, 3, 24);
        float gap = n > 10 ? 0.055f : 0.075f;
        Vector2 from = FlyStart();
        for (int i = 0; i < n; i++)
        {
            StartCoroutine(FlyOne(from + Random.insideUnitCircle * 30f));
            yield return new WaitForSecondsRealtime(gap);
        }
    }

    /// <summary>One coin on a curve up to the purse; when it lands the purse shakes and shows the new total.</summary>
    IEnumerator FlyOne(Vector2 from)
    {
        var coin = new VisualElement { pickingMode = PickingMode.Ignore };
        coin.AddToClassList("coin-fly");
        hudLayer.Add(coin);
        float side = Random.Range(-140f, 140f);
        const float duration = 0.7f;
        for (float t = 0f; t < 1f; t += Time.unscaledDeltaTime / duration)
        {
            float e = t * t; // speeds up into the purse
            Vector2 to = PurseCoin();
            Vector2 mid = (from + to) * 0.5f + new Vector2(side, 0f);
            Vector2 p = (1f - e) * (1f - e) * from + 2f * (1f - e) * e * mid + e * e * to;
            coin.style.left = p.x - 13f;
            coin.style.top = p.y - 14f;
            float s = Mathf.Lerp(1.4f, 0.9f, e);
            coin.style.scale = new Scale(new Vector3(s, s, 1f));
            yield return null;
        }
        coin.RemoveFromHierarchy();
        if (GameManager.Instance != null) coinLabel.text = GameManager.Instance.Coins.ToString();
        GameAudio.Play(a => a.coinAdd, 0.5f); // into the purse: clink
        Shake(1f);
    }

    readonly List<Label> staffNotes = new List<Label>();

    /// <summary>A staff member just did something (tidied, mended, shelved, served): a short line on the left,
    /// a little above the middle, gone after 2 seconds. A few can stack.</summary>
    public static void StaffNote(string text)
    {
        if (instance == null || instance.hudLayer == null || !instance.isActiveAndEnabled) return;
        string icon = "broom";
        for (int i = 0; i < GameManager.StaffPerson.Length; i++)
            if (!string.IsNullOrEmpty(GameManager.StaffPerson[i]) && text.StartsWith(GameManager.StaffPerson[i]))
                icon = i == (int)GameManager.StaffRole.Repairer ? "hammer" : i == (int)GameManager.StaffRole.Cleaner ? "broom" : "key";
        instance.Notify(text, icon, false, false, StaffSeconds);
    }

    IEnumerator ShowStaffNote(string text)
    {
        var l = new Label(text) { pickingMode = PickingMode.Ignore };
        l.AddToClassList("staff-note");
        hudLayer.Add(l);
        staffNotes.Add(l);
        if (staffNotes.Count > 3) { staffNotes[0].RemoveFromHierarchy(); staffNotes.RemoveAt(0); }
        const float duration = 2f;
        for (float t = 0f; t < 1f && l.parent != null; t += Time.unscaledDeltaTime / duration)
        {
            int slot = staffNotes.IndexOf(l);
            l.style.top = Length.Percent(36f - (staffNotes.Count - 1 - slot) * 5f); // newest lowest, older ones above
            l.style.opacity = t < 0.75f ? 1f : 1f - (t - 0.75f) / 0.25f;
            l.style.translate = new Translate(Mathf.Lerp(-30f, 0f, Mathf.Min(1f, t * 6f)), 0f); // slides in
            yield return null;
        }
        staffNotes.Remove(l);
        l.RemoveFromHierarchy();
    }

    /// <summary>A gold line that rises from a spot in the world and fades: a reward for doing something well
    /// (the right shelf in a chat, a perfect repair, a special order).</summary>
    public static void SkillPop(Vector3 world, string text)
    {
        if (instance == null || instance.hudLayer == null || !instance.isActiveAndEnabled) return;
        instance.StartCoroutine(instance.SkillPopping(world, text));
    }

    IEnumerator SkillPopping(Vector3 world, string text)
    {
        var cam = Camera.main;
        if (cam == null) yield break;
        var pop = new Label(text) { pickingMode = PickingMode.Ignore };
        pop.AddToClassList("skill-pop");
        pop.style.visibility = Visibility.Hidden; // until it has a size to centre with
        hudLayer.Add(pop);
        const float duration = 2f;
        for (float t = 0f; t < 1f; t += Time.unscaledDeltaTime / duration)
        {
            var vp = cam.WorldToViewportPoint(world);
            float w = hudLayer.resolvedStyle.width, h = hudLayer.resolvedStyle.height, pw = pop.resolvedStyle.width;
            if (vp.z > 0f && !float.IsNaN(pw))
            {
                pop.style.visibility = Visibility.Visible;
                pop.style.left = vp.x * w - pw * 0.5f;
                pop.style.top = (1f - vp.y) * h - 70f * (1f - (1f - t) * (1f - t)) - pop.resolvedStyle.height;
            }
            pop.style.opacity = t < 0.7f ? 1f : 1f - (t - 0.7f) / 0.3f;
            float s = t < 0.12f ? Mathf.Lerp(1.5f, 1f, t / 0.12f) : 1f;
            pop.style.scale = new Scale(new Vector3(s, s, 1f));
            yield return null;
        }
        pop.RemoveFromHierarchy();
    }

    /// <summary>"+12" in big green (or "-40" in red) just under the purse, floating up and fading.</summary>
    IEnumerator Pop(int delta)
    {
        var pop = new Label(delta > 0 ? $"+{delta}" : delta.ToString()) { pickingMode = PickingMode.Ignore };
        pop.AddToClassList("coin-pop");
        pop.EnableInClassList("negative", delta < 0);
        hudLayer.Add(pop);
        var box = hudLayer.WorldToLocal(purse.worldBound);
        pop.style.left = box.xMin + 8f;
        const float duration = 1.4f;
        for (float t = 0f; t < 1f; t += Time.unscaledDeltaTime / duration)
        {
            pop.style.top = Mathf.Lerp(box.yMax + 60f, box.yMax + 4f, 1f - (1f - t) * (1f - t));
            pop.style.opacity = t < 0.65f ? 1f : 1f - (t - 0.65f) / 0.35f;
            float s = t < 0.15f ? Mathf.Lerp(1.5f, 1f, t / 0.15f) : 1f;
            pop.style.scale = new Scale(new Vector3(s, s, 1f));
            yield return null;
        }
        pop.RemoveFromHierarchy();
    }

    public static string MorningText(GameManager gm)
    {
        string text = $"Day {gm.day} - the library is open!";
        if (gm.day == GameManager.ArchiveDay) text += "\nThe Archive is open from today.";
        else if (gm.day == GameManager.RareDay) text += "\nThe Rare Books room is open from today. Watch for VIP scholars!";
        else if (gm.day < GameManager.ArchiveDay) text += $"\nThe Archive opens on Day {GameManager.ArchiveDay}, Rare Books on Day {GameManager.RareDay}.";
        if (gm.GoalActive)
            text += gm.day == GameManager.EndingDay
                ? $"\nLast day! The Dean's goal: reputation {gm.ReputationGoal} by tonight (now {gm.Reputation})."
                : $"\nThe Dean's goal: reputation {gm.ReputationGoal} by the end of Day {GameManager.EndingDay} (now {gm.Reputation}).";
        text += DayStory.MorningLine(gm);
        int due = gm.reservations.FindAll(r => r.day <= gm.day).Count;
        if (due > 0) text += due == 1 ? "\nSomeone is coming for a reserved book today." : $"\n{due} people are coming for reserved books during the day.";
        return text;
    }

    bool lunchShown;

    void OnPhase(GameManager.Phase phase)
    {
        if (phase == GameManager.Phase.Closing)
        {
            ShowBanner("Closing time! No new visitors.");
            Tutorial.Show("lockup", "Closing time: tidy any cups and books left out (it earns reputation), then click the front ropes to lock up.");
        }
        else if (phase == GameManager.Phase.Closed) ShowBanner("The library is closed. Tidy up, then click the front ropes to lock up.", 6f);
    }

    void Start()
    {
        var gm = GameManager.Instance;
        if (gm != null)
        {
            gm.CoinsChanged += OnCoins;
            gm.PhaseChanged += OnPhase;
            gm.DayStarted += () => ShowBanner(MorningText(gm), 6f);
            gm.Delivered += n =>
            {
                Tutorial.Show("delivery", "Your order arrived on the book cart. Click the cart to take books, then shelve them or hand them over.");
                GameAudio.Play(a => a.delivery);
                ShowBanner(n == 1 ? "Delivery! A book is waiting at the book cart." : $"Delivery! {n} books are waiting at the book cart.", 6f);
            };
            gm.DayStarted += () => upgradeBannerShown = false;
            gm.ReputationChanged += OnReputation;
            gm.DayStarted += ShowReputation;
            if (!MainMenuUI.Active) ShowBanner(MorningText(gm), 6f); // with the title menu up, Play/Continue shows it
            coinLabel.text = gm.Coins.ToString();
            dayLabel.text = "Day " + gm.day;
            ShowReputation();
        }
        var player = GameObject.FindWithTag("Player");
        carry = player ? player.GetComponent<PlayerCarry>() : null;
        mover = player ? player.GetComponent<PlayerMover>() : null;
        BuildStaminaMeter(player);
        NotebookHUD.Create(GetComponent<UIDocument>());
        if (carry != null) carry.Changed += ShowCarry;
        ShowCarry(carry != null ? carry.Carried : null);
    }

    void ShowCarry(BookData book)
    {
        carrySlot?.EnableInClassList("hidden", book == null);
        // One slot per book in your hands (top book first): a pixel book coloured by its condition, its name underneath.
        if (carrySlot != null && carry != null)
        {
            if (carryBooks == null)
            {
                carryBooks = new VisualElement { pickingMode = PickingMode.Ignore };
                carryBooks.AddToClassList("hud-carry-books");
                carrySlot.Insert(0, carryBooks);
                var oldIcon = carrySlot.Q(className: "hud-carry-icon");
                if (oldIcon != null) oldIcon.style.display = DisplayStyle.None;
                if (carryLabel != null) carryLabel.style.display = DisplayStyle.None;
            }
            carryBooks.Clear();
            for (int i = carry.Count - 1; i >= 0; i--)
            {
                var (b, c) = carry.Items[i];
                var cell = new VisualElement { pickingMode = PickingMode.Ignore };
                cell.AddToClassList("hud-carry-cell");
                var slot = new VisualElement { pickingMode = PickingMode.Ignore };
                slot.AddToClassList("hud-carry-icon");
                var icon = new VisualElement { pickingMode = PickingMode.Ignore };
                icon.AddToClassList("carry-book");
                icon.EnableInClassList("worn", c == BookCondition.Worn);
                icon.EnableInClassList("damaged", c == BookCondition.Damaged);
                slot.Add(icon);
                var name = new Label(c == BookCondition.Good ? b.Title : $"{b.Title} ({c.ToString().ToLowerInvariant()})") { pickingMode = PickingMode.Ignore };
                name.AddToClassList("hud-carry-name");
                cell.Add(slot);
                cell.Add(name);
                carryBooks.Add(cell);
            }
            return;
        }
        if (carryLabel == null) return;
        // Top book first; the bag count shows once you can carry more than one.
        string text = "";
        for (int i = carry.Count - 1; i >= 0; i--)
        {
            var (b, c) = carry.Items[i];
            if (text.Length > 0) text += "\n";
            text += c == BookCondition.Good ? b.Title : $"{b.Title} ({c})";
        }
        if (carry.Capacity > 1 && carry.Count > 0) text += $"\n<size=70%>{carry.Count}/{carry.Capacity} in your bag</size>";
        carryLabel.text = text;
    }

    /// <summary>Little energy bar over the librarian's head, shown while running or catching breath.</summary>
    void BuildStaminaMeter(GameObject player)
    {
        if (player == null || WorldPrompts.Instance == null) return;
        stamina = new VisualElement { pickingMode = PickingMode.Ignore };
        stamina.AddToClassList("stamina");
        var icon = new VisualElement { pickingMode = PickingMode.Ignore };
        icon.AddToClassList("stamina-icon");
        var bar = new VisualElement { pickingMode = PickingMode.Ignore };
        bar.AddToClassList("stamina-bar");
        staminaFill = new VisualElement { pickingMode = PickingMode.Ignore };
        staminaFill.AddToClassList("stamina-fill");
        bar.Add(staminaFill);
        stamina.Add(icon);
        stamina.Add(bar);
        WorldPrompts.Instance.Follow(stamina, () => player.transform.position + Vector3.up * 1.55f);
    }

    void UpdateStamina()
    {
        if (stamina == null || mover == null) return;
        float s = mover.Stamina01;
        stamina.EnableInClassList("shown", s < 0.999f || mover.Running);
        stamina.EnableInClassList("tired", mover.Exhausted);
        staminaFill.style.width = Length.Percent(s * 100f);
    }

    void Update()
    {
        UpdateStamina();
        var gm = GameManager.Instance;
        if (gm != null && pin != null)
        {
            string text = gm.Hour < 10f && gm.day == GameManager.ArchiveDay ? "★ The Archive is open today! Readers will ask for its old books."
                        : gm.Hour < 10f && gm.day == GameManager.RareDay ? "★ Rare Books is open today! A VIP scholar is coming."
                        : null;
            bool show = text != null && !MainMenuUI.Active && (RoomReveal.Instance == null || !PlayerLock.IsLocked);
            pin.EnableInClassList("hidden", !show);
            banner?.EnableInClassList("below-pin", show);
            if (show && pin.text != text) pin.text = text;
        }
        if (bannerUntil > 0f && Time.time > bannerUntil) { bannerUntil = 0f; banner.AddToClassList("hidden"); }
        UpdateFeed();
        FlushReputation();
        if (gm != null && !lunchShown && gm.Hour >= 12f && gm.Hour < 13f)
        {
            lunchShown = true;
            ShowBanner("Lunch time - a good time to tidy, repair and order books.", 5f);
        }
        if (gm != null && gm.Hour < 12f) lunchShown = false;
        // Got enough coins for an upgrade? Say so once a day (the computer glows too).
        if (gm != null && !upgradeBannerShown && gm.UpgradeReady && gm.upgradesSeenDay != gm.day && bannerUntil <= 0f && !MainMenuUI.Active)
        {
            upgradeBannerShown = true;
            ShowBanner("You can afford an upgrade! Check the Shop on the computer.", 5f);
        }
        if (repDeltaUntil > 0f && Time.time > repDeltaUntil) { repDeltaUntil = 0f; repDelta.AddToClassList("hidden"); }
        if (gm == null || clockLabel == null) return;
        string clock = gm.CurrentPhase == GameManager.Phase.Closed ? "Closed - tidy up, then click the front ropes" : gm.Paused ? gm.ClockText + "  (paused)" : gm.Slowed ? gm.ClockText + "  (slowed)" : gm.ClockText;
        string day = Tutorial.HoldsClock ? "Day 0 - Training day" : $"Day {gm.day}";
        if (dayLabel != null && dayLabel.text != day) dayLabel.text = day;
        if (clock != lastClock) clockLabel.text = lastClock = clock;
    }
}
