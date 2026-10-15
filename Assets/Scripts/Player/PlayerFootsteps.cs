using UnityEngine;

/// <summary>
/// DRAFT: plays a random footstep every stride while the player moves.
/// Distance-based, so it stays in sync at any speed without animation events.
/// </summary>
[RequireComponent(typeof(PlayerMover))]
public class PlayerFootsteps : MonoBehaviour
{
    public AudioClip[] clips;
    public float strideLength = 0.7f;
    [Range(0f, 1f)] public float volume = 0.55f;
    public Vector2 pitchRange = new Vector2(0.94f, 1.06f);

    PlayerMover mover;
    AudioSource source;
    float travelled;
    int lastClip = -1;

    void Awake()
    {
        mover = GetComponent<PlayerMover>();
        source = gameObject.AddComponent<AudioSource>();
        source.playOnAwake = false;
        source.spatialBlend = 0f;
    }

    void Update()
    {
        if (clips == null || clips.Length == 0) return;
        if (!mover.IsGrounded || mover.Speed < 0.2f) { travelled = strideLength * 0.6f; return; }

        travelled += mover.Speed * Time.deltaTime;
        if (travelled < strideLength) return;
        travelled = 0f;

        int i = Random.Range(0, clips.Length);
        if (clips.Length > 1 && i == lastClip) i = (i + 1) % clips.Length;
        lastClip = i;
        source.pitch = Random.Range(pitchRange.x, pitchRange.y);
        source.PlayOneShot(clips[i], volume);
    }
}
