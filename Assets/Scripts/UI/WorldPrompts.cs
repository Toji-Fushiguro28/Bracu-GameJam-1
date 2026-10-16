using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// Screen-space UI that follows world objects: the hover prompt ("Talk", "Inspect")
/// and speech bubbles above inspected props. Lives on the GameUI object.
/// </summary>
[RequireComponent(typeof(UIDocument))]
public class WorldPrompts : MonoBehaviour
{
    public static WorldPrompts Instance { get; private set; }

    [Tooltip("Height above the object's top where prompts and bubbles float.")]
    public float heightOffset = 0.35f;

    class Bubble { public VisualElement el; public ClickInteractable target; public float until; }

    /// <summary>A UI element kept above a moving world point (e.g. a customer's patience bar).</summary>
    public class Follower { public VisualElement el; public System.Func<Vector3> anchor; }
    readonly List<Follower> followers = new List<Follower>();

    public Follower Follow(VisualElement el, System.Func<Vector3> anchor)
    {
        if (layer == null) return null;
        el.pickingMode = PickingMode.Ignore;
        layer.Add(el);
        var f = new Follower { el = el, anchor = anchor };
        followers.Add(f);
        return f;
    }

    public void Unfollow(Follower f)
    {
        if (f == null) return;
        f.el.RemoveFromHierarchy();
        followers.Remove(f);
    }

    VisualElement layer, prompt;
    Label promptLabel;
    Camera cam;
    ClickInteractable hoverTarget;
    readonly List<Bubble> bubbles = new List<Bubble>();

    void Awake() => Instance = this;

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    void OnEnable()
    {
        var root = GetComponent<UIDocument>().rootVisualElement;
        layer = root.Q("world-layer");
        prompt = root.Q("prompt");
        promptLabel = root.Q<Label>("prompt-label");
        prompt?.AddToClassList("hidden");
    }

    /// <summary>Called every frame by the InteractionController.</summary>
    public void SetHover(ClickInteractable target, bool inReach)
    {
        hoverTarget = target;
        if (prompt == null) return;
        if (target == null || BubbleFor(target) != null)
        {
            prompt.AddToClassList("hidden");
            return;
        }
        prompt.RemoveFromClassList("hidden");
        prompt.EnableInClassList("far", !inReach);
        promptLabel.text = inReach ? target.Verb : "Too far";
    }

    public void ShowBubble(ClickInteractable target, string text, float seconds)
    {
        if (layer == null) return;
        var b = BubbleFor(target);
        if (b == null)
        {
            b = new Bubble { target = target, el = new VisualElement() };
            b.el.AddToClassList("bubble");
            b.el.pickingMode = PickingMode.Ignore;
            var label = new Label { pickingMode = PickingMode.Ignore };
            label.AddToClassList("bubble-text");
            b.el.Add(label);
            layer.Add(b.el);
            bubbles.Add(b);
        }
        b.el.Q<Label>().text = text;
        b.until = Time.time + seconds;
    }

    Bubble BubbleFor(ClickInteractable target) => bubbles.Find(b => b.target == target);

    void LateUpdate()
    {
        if (cam == null && (cam = Camera.main) == null) return;

        if (hoverTarget != null && prompt != null && !prompt.ClassListContains("hidden"))
            Place(prompt, hoverTarget.PromptAnchor);

        for (int i = followers.Count - 1; i >= 0; i--)
            Place(followers[i].el, followers[i].anchor());

        for (int i = bubbles.Count - 1; i >= 0; i--)
        {
            var b = bubbles[i];
            if (b.target == null || Time.time > b.until)
            {
                b.el.RemoveFromHierarchy();
                bubbles.RemoveAt(i);
                continue;
            }
            Place(b.el, b.target.TopAnchor);
        }
    }

    /// <summary>Centres an element horizontally above a world point (bottom edge at the point).</summary>
    void Place(VisualElement el, Vector3 world)
    {
        world.y += heightOffset;
        Vector3 vp = cam.WorldToViewportPoint(world);
        bool visible = vp.z > 0f || cam.orthographic;
        el.style.visibility = visible ? Visibility.Visible : Visibility.Hidden;
        float w = layer.resolvedStyle.width, h = layer.resolvedStyle.height;
        if (w <= 0f || h <= 0f) return;
        el.style.left = vp.x * w - el.resolvedStyle.width * 0.5f;
        el.style.top = (1f - vp.y) * h - el.resolvedStyle.height;
    }
}
