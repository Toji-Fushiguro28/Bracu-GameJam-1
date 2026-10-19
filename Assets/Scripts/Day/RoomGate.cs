using UnityEngine;

/// <summary>
/// A rope gate (SM_PassGate_Rope) across a doorway into the Archive or Rare Books. Closed until
/// its opening day, with a sign above it; then it swings aside against the wall.
/// </summary>
public class RoomGate : ClickInteractable
{
    public enum Room { Archive, RareBooks, FrontDoor }

    public Room room;
    [Tooltip("World pose when open (rope moved aside against the wall).")]
    public Vector3 openPosition;
    public float openYaw;
    [Tooltip("\"Opens Day N\" sign shown while closed.")]
    public GameObject sign;
    public float swingSeconds = 1.2f;

    Vector3 closedPosition;
    Quaternion closedRotation;
    float openAmount = -1f;
    bool? shown;

    public static string NameOf(Room r) => r == Room.Archive ? "Archive" : r == Room.RareBooks ? "Rare Books room" : "Front doors";
    public static int OpenDay(Room r) => r == Room.Archive ? GameManager.ArchiveDay : GameManager.RareDay;
    public static bool IsOpen(Room r)
    {
        var gm = GameManager.Instance;
        if (gm == null) return true;
        // The front ropes close at night, once the last visitor has gone home.
        if (r == Room.FrontDoor) return gm.CurrentPhase != GameManager.Phase.Closed || FindAnyObjectByType<Customer>() != null;
        return r == Room.Archive ? gm.ArchiveOpen : gm.RareOpen;
    }

    /// <summary>True if this point is inside a room that hasn't opened yet.</summary>
    public static bool IsLockedAt(Vector3 p)
    {
        foreach (var d in RoomDarkness.All)
            if (!IsOpen(d.room) && d.Contains(p)) return true;
        return false;
    }

    bool Open => IsOpen(room);
    protected override string DefaultVerb => NameOf(room);
    public override string Verb => room == Room.FrontDoor ? (Open ? "Front doors" : "Lock up for the night")
        : Open ? NameOf(room) : $"{NameOf(room)} (opens Day {OpenDay(room)})";

    protected override void Awake()
    {
        base.Awake();
        closedPosition = transform.position;
        closedRotation = transform.rotation;
    }

    public override void Interact(GameObject player)
    {
        if (room == Room.FrontDoor)
        {
            // Same as the doors: lock up and see the day's report.
            var door = FindAnyObjectByType<EndDayDoor>();
            if (door != null) door.Interact(player);
            return;
        }
        string text = Open
            ? (room == Room.Archive ? "The Archive is open. Readers can ask for the old stacks now."
                                    : "The Rare Books room is open. Scholars with a Rare card may read here.")
            : $"Not open yet. The {NameOf(room)} opens on Day {OpenDay(room)}.";
        WorldPrompts.Instance?.ShowBubble(this, text, 3f);
    }

    Vector3 signHome;
    bool signHomeSet;

    /// <summary>"Opens Day N" while closed; "NOW OPEN!" (bobbing) on its opening day; hidden after.</summary>
    void UpdateSign()
    {
        if (sign == null || room == Room.FrontDoor) return;
        var gm = GameManager.Instance;
        bool openingDay = Open && gm != null && gm.day == OpenDay(room) && gm.Hour < 10f; // "NOW OPEN!" until 10 AM
        bool show = !Open || openingDay;
        if (sign.activeSelf != show) sign.SetActive(show);
        if (!show) return;
        if (!signHomeSet) { signHome = sign.transform.position; signHomeSet = true; }
        var tm = sign.GetComponent<TextMesh>();
        string label = room == Room.Archive ? "ARCHIVE" : "RARE BOOKS";
        if (tm != null) tm.text = openingDay ? $"{label}\nNOW OPEN!" : $"{label}\nopens Day {OpenDay(room)}";
        sign.transform.position = signHome + (openingDay ? Vector3.up * (0.12f * Mathf.Sin(Time.time * 3f)) : Vector3.zero);
    }

    void Update()
    {
        UpdateSign();
        if (room == Room.FrontDoor)
        {
            // The front ropes don't move: they're invisible while open and appear in place at night.
            bool show = !Open;
            if (shown != show)
            {
                shown = show;
                foreach (var r in GetComponentsInChildren<Renderer>()) r.enabled = show;
                foreach (var c in GetComponentsInChildren<Collider>()) c.enabled = show;
            }
            return;
        }
        // Visually stays shut while the opening-day reveal is on its way.
        float target = Open && !RoomReveal.IsHeld(room) ? 1f : 0f;
        if (Mathf.Approximately(openAmount, target)) return;
        // First frame snaps; later changes (a new day) swing.
        openAmount = openAmount < 0f ? target : Mathf.MoveTowards(openAmount, target, Time.deltaTime / Mathf.Max(0.01f, swingSeconds));
        float t = Mathf.SmoothStep(0f, 1f, openAmount);
        transform.SetPositionAndRotation(Vector3.Lerp(closedPosition, openPosition, t),
            Quaternion.Slerp(closedRotation, Quaternion.Euler(0f, openYaw, 0f), t));
        UpdateSign();
    }
}
