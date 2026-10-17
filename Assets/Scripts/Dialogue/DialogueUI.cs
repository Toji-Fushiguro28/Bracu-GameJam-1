using System;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

/// <summary>
/// Bottom-of-screen dialogue box: typewriter text, voice blips, branching choices.
/// Click anywhere to finish the line / continue. Lives on the GameUI object.
/// </summary>
[RequireComponent(typeof(UIDocument))]
public class DialogueUI : MonoBehaviour
{
    public static DialogueUI Instance { get; private set; }

    [Tooltip("Characters typed per second.")]
    public float typeSpeed = 40f;
    public AudioClip voiceBlip;
    [Range(0f, 1f)] public float voiceVolume = 0.35f;
    [Tooltip("Play a blip every N visible characters.")]
    public int blipEvery = 2;

    VisualElement root, choiceBox, continueArrow;
    Label speakerLabel, lineLabel;
    AudioSource voice;
    InputAction clickAction;

    DialogueAsset current;
    Action onFinished;
    int index;
    string fullText;
    float typed;
    int shownChars;
    int lastInputFrame;

    public bool IsOpen => current != null;
    bool Typing => fullText != null && shownChars < fullText.Length;

    void Awake()
    {
        Instance = this;
        voice = gameObject.AddComponent<AudioSource>();
        voice.playOnAwake = false;
        clickAction = InputSystem.actions?.FindAction("Attack");
    }

    void OnEnable()
    {
        clickAction?.Enable();
        var doc = GetComponent<UIDocument>().rootVisualElement;
        root = doc.Q("dialogue");
        speakerLabel = doc.Q<Label>("speaker");
        lineLabel = doc.Q<Label>("line");
        choiceBox = doc.Q("choices");
        continueArrow = doc.Q("continue");
        root?.AddToClassList("hidden");
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
        PlayerLock.Unlock(this);
    }

    /// <summary>Start a conversation. <paramref name="finished"/> runs when it closes.</summary>
    /// <summary>Id of every line as it appears (lets callers react to the branch the player took).</summary>
    public event Action<string> LineShown;

    public void Play(DialogueAsset dialogue, Action finished = null)
    {
        if (dialogue == null || dialogue.lines.Count == 0 || root == null) return;
        current = dialogue;
        onFinished = finished;
        lastInputFrame = Time.frameCount; // the click that opened us must not skip the first line
        PlayerLock.Lock(this);
        root.RemoveFromClassList("hidden");
        ShowLine(0);
    }

    void ShowLine(int i)
    {
        if (i < 0 || i >= current.lines.Count) { Close(); return; }
        index = i;
        var line = current.lines[i];
        if (!string.IsNullOrEmpty(line.id)) LineShown?.Invoke(line.id);
        speakerLabel.text = string.IsNullOrEmpty(line.speaker) ? current.speaker : line.speaker;
        fullText = line.text ?? "";
        typed = 0f;
        shownChars = 0;
        lineLabel.text = "";
        choiceBox.Clear();
        choiceBox.AddToClassList("hidden");
        continueArrow.AddToClassList("hidden");
    }

    void Update()
    {
        if (!IsOpen) return;

        if (Typing)
        {
            typed += typeSpeed * Time.unscaledDeltaTime;
            int target = Mathf.Min(fullText.Length, Mathf.FloorToInt(typed));
            while (shownChars < target)
            {
                char c = fullText[shownChars++];
                if (!char.IsWhiteSpace(c) && shownChars % Mathf.Max(1, blipEvery) == 0) Blip();
            }
            lineLabel.text = fullText.Substring(0, shownChars);
            if (!Typing) OnLineTyped();
        }

        if (clickAction == null || !clickAction.WasPressedThisFrame() || Time.frameCount == lastInputFrame) return;
        lastInputFrame = Time.frameCount;

        if (Typing)
        {
            shownChars = fullText.Length;
            lineLabel.text = fullText;
            OnLineTyped();
        }
        else if (current.lines[index].choices.Count == 0)
        {
            ShowLine(current.Resolve(index, current.lines[index].next));
        }
    }

    void OnLineTyped()
    {
        var choices = current.lines[index].choices;
        if (choices.Count == 0)
        {
            continueArrow.RemoveFromClassList("hidden");
            return;
        }
        choiceBox.RemoveFromClassList("hidden");
        foreach (var choice in choices)
        {
            var c = choice;
            var button = new Button(() => Choose(c)) { text = c.text };
            button.AddToClassList("choice");
            choiceBox.Add(button);
        }
    }

    void Choose(DialogueAsset.Choice choice)
    {
        if (Time.frameCount == lastInputFrame) return;
        lastInputFrame = Time.frameCount;
        ShowLine(current.Resolve(index, choice.next));
    }

    void Blip()
    {
        if (voiceBlip == null) return;
        voice.pitch = current.voicePitch * UnityEngine.Random.Range(0.94f, 1.06f);
        voice.PlayOneShot(voiceBlip, voiceVolume);
    }

    void Close()
    {
        current = null;
        fullText = null;
        root.AddToClassList("hidden");
        PlayerLock.Unlock(this);
        var done = onFinished;
        onFinished = null;
        done?.Invoke();
    }
}
