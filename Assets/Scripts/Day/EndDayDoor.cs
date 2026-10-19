using UnityEngine;

/// <summary>
/// Locking up for the night: once the library has closed and the last visitor is gone, this opens the Day Report.
/// You click the front ropes (RoomGate, Front Door) to do it; the doors' own box isn't clickable, so it never
/// covers the ropes or the staff waiting to report.
/// </summary>
public class EndDayDoor : ClickInteractable
{
    float confirmUntil;

    public override bool CanInteract => false;

    protected override string DefaultVerb => "Lock up for the night";
    public override string Verb
    {
        get
        {
            var gm = GameManager.Instance;
            return gm != null && gm.CurrentPhase == GameManager.Phase.Closed ? "Lock up for the night" : "Front doors";
        }
    }

    public override void Interact(GameObject player)
    {
        var gm = GameManager.Instance;
        if (gm == null) return;
        if (gm.CurrentPhase != GameManager.Phase.Closed)
        {
            Bubble(gm.CurrentPhase == GameManager.Phase.Open ? $"The library's open until {(gm.closeHour > 12f ? gm.closeHour - 12f : gm.closeHour):0}:00 PM." : "Almost closing time...");
            return;
        }
        if (FindObjectsByType<Customer>().Length > 0) { Bubble("Wait until the last visitor has left."); return; }
        // Staff hand over their report (and the day's takings) before you can lock up.
        var waiting = StaffManager.Instance != null ? StaffManager.Instance.PendingReport() : null;
        if (waiting != null) { Bubble($"{GameManager.StaffPerson[(int)waiting.role]} is waiting by the gate with their report."); return; }

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
        if (DayReportUI.Instance != null) HUD.FadeThen(() => DayReportUI.Instance.Open());
    }

    void Bubble(string text) => WorldPrompts.Instance?.ShowBubble(this, text, 3f);
}
