using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// Top bar (day, clock, coins) and the carried-book slot. Lives on the GameUI object.
/// </summary>
[RequireComponent(typeof(UIDocument))]
public class HUD : MonoBehaviour
{
    Label dayLabel, clockLabel, coinLabel, carryLabel;
    VisualElement carrySlot;
    PlayerCarry carry;
    string lastClock;
    Label banner, coinDelta;
    float bannerUntil, deltaUntil;

    void OnEnable()
    {
        var root = GetComponent<UIDocument>().rootVisualElement;
        dayLabel = root.Q<Label>("hud-day");
        clockLabel = root.Q<Label>("hud-clock");
        coinLabel = root.Q<Label>("hud-coins");
        carryLabel = root.Q<Label>("hud-carry");
        carrySlot = root.Q("hud-carry-slot");

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

    void OnPhase(GameManager.Phase phase)
    {
        if (phase == GameManager.Phase.Closing) ShowBanner("Closing time! No new visitors.");
        else if (phase == GameManager.Phase.Closed) ShowBanner("The library is closed. Tidy up, then head to the Archive door.", 6f);
    }

    void Start()
    {
        var gm = GameManager.Instance;
        if (gm != null)
        {
            gm.CoinsChanged += OnCoins;
            gm.PhaseChanged += OnPhase;
            gm.DayStarted += () => ShowBanner($"Day {gm.day} - the library is open!");
            coinLabel.text = gm.Coins.ToString();
            dayLabel.text = "Day " + gm.day;
        }
        var player = GameObject.FindWithTag("Player");
        carry = player ? player.GetComponent<PlayerCarry>() : null;
        if (carry != null) carry.Changed += ShowCarry;
        ShowCarry(carry != null ? carry.Carried : null);
    }

    void ShowCarry(BookData book)
    {
        carrySlot?.EnableInClassList("hidden", book == null);
        if (carryLabel == null) return;
        carryLabel.text = book == null ? ""
            : carry.CarriedCondition == BookCondition.Good ? book.Title
            : $"{book.Title} ({carry.CarriedCondition})";
    }

    void Update()
    {
        var gm = GameManager.Instance;
        if (bannerUntil > 0f && Time.time > bannerUntil) { bannerUntil = 0f; banner.AddToClassList("hidden"); }
        if (deltaUntil > 0f && Time.time > deltaUntil) { deltaUntil = 0f; coinDelta.AddToClassList("hidden"); }
        if (gm == null || clockLabel == null) return;
        string clock = gm.CurrentPhase == GameManager.Phase.Closed ? "Closed - tidy up, then the Archive door" : gm.ClockText;
        string day = $"Day {gm.day}";
        if (dayLabel != null && dayLabel.text != day) dayLabel.text = day;
        if (clock != lastClock) clockLabel.text = lastClock = clock;
    }
}
