using System.Collections;
using Unity.Cinemachine;
using UnityEngine;

/// <summary>
/// On the morning a room opens (Archive day 2, Rare Books day 3): the camera glides over to the
/// dark room, the lights come up, the rope swings aside with a little fanfare, then back to the player.
/// While it plays the room keeps looking closed (RoomGate / RoomDarkness check IsHeld).
/// </summary>
public class RoomReveal : MonoBehaviour
{
    public static RoomReveal Instance { get; private set; }

    [Tooltip("Where the camera looks for each room (Archive, Rare Books).")]
    public Vector3 archiveFocus = new Vector3(5f, 1f, 8f);
    public Vector3 rareFocus = new Vector3(35f, 0.5f, 7f);
    [Tooltip("Camera distance compared with the normal follow camera (smaller = closer).")]
    public float closeness = 0.85f;
    public AudioClip revealSound;
    public float lookSeconds = 2.5f;

    static int heldRoom = -1;
    CinemachineCamera cam;
    AudioSource sfx;

    /// <summary>True while this room should still look closed (the reveal hasn't happened yet).</summary>
    public static bool IsHeld(RoomGate.Room r) => heldRoom == (int)r;

    void Awake()
    {
        Instance = this;
        sfx = gameObject.AddComponent<AudioSource>();
        sfx.playOnAwake = false;
    }

    void OnDestroy() { if (Instance == this) Instance = null; heldRoom = -1; }

    void Start()
    {
        var gm = GameManager.Instance;
        if (gm != null) gm.DayStarted += OnDayStarted;
    }

    void OnDayStarted()
    {
        var gm = GameManager.Instance;
        RoomGate.Room? room = gm.day == GameManager.ArchiveDay ? RoomGate.Room.Archive
                            : gm.day == GameManager.RareDay ? RoomGate.Room.RareBooks : (RoomGate.Room?)null;
        if (room == null) return;
        heldRoom = (int)room.Value; // keep it dark until the camera gets there
        StartCoroutine(Play(room.Value));
    }

    IEnumerator Play(RoomGate.Room room)
    {
        PlayerLock.Lock(this); // the clock waits too
        yield return new WaitForSecondsRealtime(1.0f); // let the morning fade-in finish

        // A temporary camera framing the room, blended to by Cinemachine.
        var main = Camera.main;
        var player = GameObject.FindWithTag("Player");
        Vector3 offset = main != null && player != null ? main.transform.position - player.transform.position : new Vector3(15f, 16f, -20f);
        Vector3 focus = room == RoomGate.Room.Archive ? archiveFocus : rareFocus;
        var followCam = FindAnyObjectByType<CinemachineCamera>();
        var go = new GameObject("CM_RoomReveal");
        cam = go.AddComponent<CinemachineCamera>();
        if (followCam != null) cam.Lens = followCam.Lens;
        go.transform.position = focus + offset * closeness;
        go.transform.rotation = Quaternion.LookRotation(focus - go.transform.position);
        cam.Priority = 50;
        yield return new WaitForSecondsRealtime(2.0f); // travel over

        // Lights up, rope aside, fanfare.
        heldRoom = -1;
        if (revealSound != null) sfx.PlayOneShot(revealSound, 0.9f);
        GameAudio.Play(a => a.unlock, 0.5f);
        var hud = FindAnyObjectByType<HUD>();
        if (hud != null) hud.ShowBanner(room == RoomGate.Room.Archive
            ? "The Archive is open! Readers can now ask for its old books and papers."
            : "The Rare Books room is open! Watch out for VIP scholars.", 6f);
        yield return new WaitForSecondsRealtime(lookSeconds);

        // Back to the librarian.
        cam.Priority = 0;
        yield return new WaitForSecondsRealtime(1.8f);
        Destroy(go);
        PlayerLock.Unlock(this);
    }
}
