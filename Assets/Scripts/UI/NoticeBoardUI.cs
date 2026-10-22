using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

/// <summary>
/// The notice board up close: a cork board in a wooden frame (the kit's book cover, tinted to the board model's
/// brown) with today's papers pinned on it, each a little crooked. The big one in the middle is today's news;
/// the important ones sit round it and the jokes fill the gaps. A work screen: time runs slower while it's open
/// (PlayerLock), like the computer or a shelf. × , a click outside it or Esc closes it.
/// </summary>
public class NoticeBoardUI : MonoBehaviour
{
    /// <summary>Papers that fit on the board: the main one and 8 round it.</summary>
    public const int Slots = 9;

    static NoticeBoardUI instance;
    public static bool IsOpen => instance != null && instance.open;

    UIDocument doc;
    VisualElement hudRoot;
    InputAction cancelAction;
    bool open;

    // Where the small papers go on the cork (left %, top %), filled in this order: the important ones nearest
    // the top, the jokes lower down. Each 22% wide.
    static readonly Vector2[] SmallSlots =
    {
        new Vector2(1.5f, 3f), new Vector2(76.5f, 3f), new Vector2(1.5f, 36f), new Vector2(76.5f, 36f),
        new Vector2(27f, 68f), new Vector2(51f, 68f), new Vector2(1.5f, 69f), new Vector2(76.5f, 69f),
    };
    static readonly Color[] PinColors =
    {
        new Color32(170, 50, 40, 255), new Color32(200, 160, 70, 255), new Color32(70, 120, 80, 255), new Color32(70, 90, 140, 255),
    };

    /// <summary>Opens the board with today's papers (made the first time it's needed).</summary>
    public static void Open(NoticeBoard source)
    {
        if (instance == null)
        {
            var hud = FindAnyObjectByType<HUD>();
            var hudDoc = hud != null ? hud.GetComponent<UIDocument>() : null;
            if (hudDoc == null || hudDoc.panelSettings == null) return;
            // Its own panel at the computer's level (20): under the notebook (25) and the title menu (30).
            var go = new GameObject("NoticeBoardUI");
            go.SetActive(false);
            var ps = Instantiate(hudDoc.panelSettings);
            ps.name = "PS_NoticeBoard";
            ps.sortingOrder = 20;
            var d = go.AddComponent<UIDocument>();
            d.panelSettings = ps;
            instance = go.AddComponent<NoticeBoardUI>();
            instance.doc = d;
            instance.hudRoot = hudDoc.rootVisualElement;
            go.SetActive(true);
        }
        instance.Show(source);
    }

    void Awake()
    {
        cancelAction = InputSystem.actions?.FindAction("UI/Cancel");
        cancelAction?.Enable();
    }

    void OnDestroy()
    {
        PlayerLock.Unlock(this);
        if (instance == this) instance = null;
    }

    void Update()
    {
        if (open && cancelAction != null && cancelAction.WasPressedThisFrame()) Close();
    }

    void Show(NoticeBoard source)
    {
        var root = doc.rootVisualElement;
        if (root == null) return;
        if (hudRoot != null)
            for (int i = 0; i < hudRoot.styleSheets.count; i++)
                if (!root.styleSheets.Contains(hudRoot.styleSheets[i])) root.styleSheets.Add(hudRoot.styleSheets[i]);
        root.Clear();
        ClickGuard.Attach(root);

        var layer = new VisualElement();
        layer.AddToClassList("layer");
        layer.AddToClassList("nb-layer");
        root.Add(layer);

        var dim = new VisualElement();
        dim.AddToClassList("nb-dim");
        dim.RegisterCallback<ClickEvent>(_ => Close());
        layer.Add(dim);

        var board = new VisualElement();
        board.AddToClassList("nb-board");
        board.RegisterCallback<ClickEvent>(e => e.StopPropagation()); // clicks on the board don't close it
        layer.Add(board);

        var cork = new VisualElement();
        cork.AddToClassList("nb-cork");
        board.Add(cork);

        var plaque = new Label("NOTICE BOARD");
        plaque.AddToClassList("nb-plaque");
        plaque.AddToClassList("font-title");
        board.Add(plaque);

        var close = new Button(Close);
        close.AddToClassList("nb-close");
        board.Add(close);

        var papers = source.TodaysPapers();
        int day = GameManager.Instance != null ? GameManager.Instance.day : 1;
        var rng = new System.Random(day * 977 + 13); // the same crooked papers all day
        int small = 0;
        for (int i = 0; i < papers.Count; i++)
        {
            var p = papers[i];
            if (!p.main && small >= SmallSlots.Length) break;
            var paper = new VisualElement();
            paper.AddToClassList("nb-paper");
            if (p.main)
            {
                paper.AddToClassList("main");
                paper.style.left = Length.Percent(27f);
                paper.style.top = Length.Percent(4f);
                paper.style.width = Length.Percent(46f);
            }
            else
            {
                var at = SmallSlots[small++];
                paper.style.left = Length.Percent(at.x);
                paper.style.top = Length.Percent(at.y);
                paper.style.width = Length.Percent(22f);
            }
            paper.style.rotate = new Rotate(new Angle((float)(rng.NextDouble() * 2.0 - 1.0) * (p.main ? 1.5f : 4f)));

            var title = new Label(p.title);
            title.AddToClassList("nb-paper-title");
            title.AddToClassList("font-title");
            paper.Add(title);
            var body = new Label(p.body);
            body.AddToClassList("nb-paper-body");
            paper.Add(body);

            var pin = new VisualElement();
            pin.AddToClassList("nb-pin");
            pin.style.backgroundColor = PinColors[rng.Next(PinColors.Length)];
            paper.Add(pin);
            cork.Add(paper);
        }

        if (!open)
        {
            open = true;
            PlayerLock.Lock(this);
            GameAudio.Play(a => a.panelOpen);
        }
    }

    void Close()
    {
        if (!open) return;
        open = false;
        doc.rootVisualElement?.Clear();
        PlayerLock.Unlock(this);
        GameAudio.Play(a => a.panelClose);
    }
}
