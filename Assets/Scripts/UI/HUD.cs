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

    void OnEnable()
    {
        var root = GetComponent<UIDocument>().rootVisualElement;
        dayLabel = root.Q<Label>("hud-day");
        clockLabel = root.Q<Label>("hud-clock");
        coinLabel = root.Q<Label>("hud-coins");
        carryLabel = root.Q<Label>("hud-carry");
        carrySlot = root.Q("hud-carry-slot");
    }

    void Start()
    {
        var gm = GameManager.Instance;
        if (gm != null)
        {
            gm.CoinsChanged += (total, _) => coinLabel.text = total.ToString();
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
        if (gm == null || clockLabel == null) return;
        string clock = gm.ClockText;
        if (clock != lastClock) clockLabel.text = lastClock = clock;
    }
}
