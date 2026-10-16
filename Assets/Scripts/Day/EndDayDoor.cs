using UnityEngine;

/// <summary>
/// The Archive door: once the library has closed and the last visitor is gone,
/// click it to end the day and open the Day Report.
/// </summary>
public class EndDayDoor : ClickInteractable
{
    float confirmUntil;

    protected override string DefaultVerb => "End the day";
    public override string Verb
    {
        get
        {
            var gm = GameManager.Instance;
            return gm != null && gm.CurrentPhase == GameManager.Phase.Closed ? "End the day" : "Archive door";
        }
    }

    public override void Interact(GameObject player)
    {
        var gm = GameManager.Instance;
        if (gm == null) return;
        if (gm.CurrentPhase != GameManager.Phase.Closed)
        {
            Bubble(gm.CurrentPhase == GameManager.Phase.Open ? "The library's still open." : "Almost closing time...");
            return;
        }
        if (FindObjectsByType<Customer>().Length > 0) { Bubble("Wait until the last visitor has left."); return; }

        int messes = MessSpot.All.Count;
        int returns = ReturnBin.Instance != null ? ReturnBin.Instance.Count : 0;
        if ((messes > 0 || returns > 0) && Time.time > confirmUntil)
        {
            confirmUntil = Time.time + 5f;
            string what = messes > 0 && returns > 0 ? $"{messes} things to tidy and {returns} returns to shelve"
                        : messes > 0 ? $"{messes} things to tidy" : $"{returns} returns to shelve";
            Bubble($"Still {what}. Click again to leave anyway.");
            return;
        }
        if (DayReportUI.Instance != null) DayReportUI.Instance.Open();
    }

    void Bubble(string text) => WorldPrompts.Instance?.ShowBubble(this, text, 3f);
}
