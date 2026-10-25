using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// Props that say something when clicked ("You feel enlightened.").
/// Shows a speech bubble above the object; lines rotate or play once.
/// </summary>
public class InspectInteractable : ClickInteractable
{
    [TextArea(1, 3)] public string[] lines = { "You feel enlightened." };
    [Tooltip("Go through the lines in order (looping). Off = random line each time.")]
    public bool inOrder = true;
    [Tooltip("After the first inspection the object is no longer interactable.")]
    public bool onceOnly;
    public float bubbleSeconds = 2.8f;
    [Tooltip("Hook up effects in the Inspector (play a sound, give a coin...).")]
    public UnityEvent onInspected;

    int nextLine;
    bool used;

    /// <summary>The night letter (GhostLetter) takes over some props while its trail is on: true = it handled the click.</summary>
    public static System.Func<InspectInteractable, bool> Intercept;

    public override void Interact(GameObject player)
    {
        if (Intercept != null && Intercept(this)) return;
        if ((onceOnly && used) || lines == null || lines.Length == 0) return;
        used = true;

        string line = inOrder ? lines[nextLine++ % lines.Length] : lines[Random.Range(0, lines.Length)];
        if (WorldPrompts.Instance != null)
            WorldPrompts.Instance.ShowBubble(this, line, bubbleSeconds);
        onInspected?.Invoke();

        if (onceOnly) enabled = false;
    }
}
