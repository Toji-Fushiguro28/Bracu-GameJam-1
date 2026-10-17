using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// Top bar (day, clock, coins) and the carried-book slot. Lives on the GameUI object and adds the notebook.
/// </summary>
[RequireComponent(typeof(UIDocument))]
public class HUD : MonoBehaviour
{
    static HUD instance;
    VisualElement fade;
    Label pin;
    bool upgradeBannerShown;

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
    VisualElement carrySlot;
    PlayerCarry carry;
    string lastClock;
    Label banner, coinDelta;
    VisualElement stamina, staminaFill;
    PlayerMover mover;
    float bannerUntil, deltaUntil;

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
        coinDelta = new Label { name = "hud-coin-delta", pickingMode = PickingMode.Ignore };
        coinDelta.AddToClassList("hud-coin-delta");
        coinDelta.AddToClassList("hidden");
        root.Q(className: "hud-coins")?.Add(coinDelta);
    }

    public void ShowBanner(string text, float seconds = 4f)
    {
        banner.text = text;
        banner.RemoveFromClassList("hidden");
        bannerUntil = Time.time + seconds;
    }

    void OnCoins(int total, int delta)
    {
        coinLabel.text = total.ToString();
        coinDelta.text = delta > 0 ? $"+{delta}" : delta.ToString();
        coinDelta.EnableInClassList("negative", delta < 0);
        coinDelta.RemoveFromClassList("hidden");
        deltaUntil = Time.time + 1.6f;
        coinLabel.parent.experimental.animation.Scale(1.18f, 90).OnCompleted(() => coinLabel.parent.experimental.animation.Scale(1f, 140));
    }

    public static string MorningText(GameManager gm)
    {
        string text = $"Day {gm.day} - the library is open!";
        if (gm.day == GameManager.ArchiveDay) text += "\nThe Archive is open from today.";
        else if (gm.day == GameManager.RareDay) text += "\nThe Rare Books room is open from today. Watch for VIP scholars!";
        else if (gm.day < GameManager.ArchiveDay) text += $"\nThe Archive opens on Day {GameManager.ArchiveDay}, Rare Books on Day {GameManager.RareDay}.";
        int due = gm.reservations.FindAll(r => r.day <= gm.day).Count;
        if (due > 0) text += due == 1 ? "\nSomeone is coming for a reserved book." : $"\n{due} people are coming for reserved books.";
        return text;
    }

    bool lunchShown;

    void OnPhase(GameManager.Phase phase)
    {
        if (phase == GameManager.Phase.Closing) ShowBanner("Closing time! No new visitors.");
        else if (phase == GameManager.Phase.Closed) ShowBanner("The library is closed. Tidy up, then lock the front doors.", 6f);
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
                GameAudio.Play(a => a.delivery);
                ShowBanner(n == 1 ? "Delivery! A book is waiting at the book cart." : $"Delivery! {n} books are waiting at the book cart.", 6f);
            };
            gm.DayStarted += () => upgradeBannerShown = false;
            ShowBanner(MorningText(gm), 6f);
            coinLabel.text = gm.Coins.ToString();
            dayLabel.text = "Day " + gm.day;
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
            if (show && pin.text != text) pin.text = text;
        }
        if (bannerUntil > 0f && Time.time > bannerUntil) { bannerUntil = 0f; banner.AddToClassList("hidden"); }
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
        if (deltaUntil > 0f && Time.time > deltaUntil) { deltaUntil = 0f; coinDelta.AddToClassList("hidden"); }
        if (gm == null || clockLabel == null) return;
        string clock = gm.CurrentPhase == GameManager.Phase.Closed ? "Closed - tidy up, then lock the front doors" : gm.Paused ? gm.ClockText + "  (paused)" : gm.ClockText;
        string day = $"Day {gm.day}";
        if (dayLabel != null && dayLabel.text != day) dayLabel.text = day;
        if (clock != lastClock) clockLabel.text = lastClock = clock;
    }
}
