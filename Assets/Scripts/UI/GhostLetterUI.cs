using System;
using System.Collections;
using System.Linq;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

/// <summary>
/// The night letter's screens (GhostLetter): the scroll beside the desk plate (the newest paper you've found), a loose
/// sheet of paper for each clue, the display case's three-wheel lock and the Archive typewriter. Its own panel
/// (sort order 26: over the notebook, under the menus). Made at runtime.
/// </summary>
public class GhostLetterUI : MonoBehaviour
{
    const int SortOrder = 26;
    public static GhostLetterUI Instance { get; private set; }
    /// <summary>The mouse is on the scroll (a click there mustn't reach the library).</summary>
    public static bool PointerOver { get; private set; }

    enum Mode { None, Paper, Lock, Typewriter }
    Mode mode;
    UIDocument doc;
    VisualElement root, modal, sheet;
    Button icon;
    InputAction cancel;
    bool unread;
    Coroutine running;

    public bool IsOpen => mode != Mode.None;

    public static void Create(GhostLetterData data)
    {
        if (Instance != null || data == null) return;
        var hud = FindAnyObjectByType<HUD>();
        var ps = hud != null ? hud.GetComponent<UIDocument>()?.panelSettings : null;
        if (ps == null) return;
        var go = new GameObject("GhostLetterUI");
        go.SetActive(false);
        var copy = Instantiate(ps);
        copy.name = "PS_GhostLetter";
        copy.sortingOrder = SortOrder;
        var ui = go.AddComponent<GhostLetterUI>();
        ui.doc = go.AddComponent<UIDocument>();
        ui.doc.panelSettings = copy;
        go.SetActive(true);
        ui.Build(data);
    }

    void Awake() => Instance = this;
    void OnDestroy() { if (Instance == this) Instance = null; PointerOver = false; if (mode != Mode.None) PlayerLock.Unlock(this); }

    void Build(GhostLetterData data)
    {
        root = doc.rootVisualElement;
        root.pickingMode = PickingMode.Ignore;
        if (data.styles != null) foreach (var s in data.styles) if (s != null) root.styleSheets.Add(s);
        var layer = new VisualElement { pickingMode = PickingMode.Ignore };
        layer.AddToClassList("layer");
        root.Add(layer);

        icon = new Button(() => { unread = false; GameAudio.Play(a => a.uiClick); GhostLetter.Instance?.ShowCurrentPaper(); });
        icon.AddToClassList("kit-button");
        icon.AddToClassList("gl-icon");
        var img = new VisualElement { pickingMode = PickingMode.Ignore };
        img.AddToClassList("gl-icon-img");
        icon.Add(img);
        icon.tooltip = "The letter";
        layer.Add(icon);

        modal = new VisualElement();
        modal.AddToClassList("gl-modal");
        modal.RegisterCallback<PointerDownEvent>(e => { if (e.target == modal) Close(); }); // click outside the paper
        modal.style.display = DisplayStyle.None;
        layer.Add(modal);
        GameAudio.HookTyping(root);

        cancel = InputSystem.actions?.FindAction("UI/Cancel");
        cancel?.Enable();
    }

    void Update()
    {
        if (icon == null) return;
        bool show = GhostLetter.HasPapers && HUD.BarShown && mode == Mode.None && !MainMenuUI.Active;
        icon.style.display = show ? DisplayStyle.Flex : DisplayStyle.None;
        if (show)
        {
            var bar = HUD.BarRect;
            icon.style.left = bar.xMax + 12f;
            icon.style.top = bar.center.y - 30f;
            icon.EnableInClassList("new", unread && Mathf.Repeat(Time.unscaledTime, 1.2f) < 0.6f);
        }
        PointerOver = false;
        if (show && Mouse.current != null && root.panel != null)
        {
            var m = Mouse.current.position.ReadValue();
            var p = RuntimePanelUtils.ScreenToPanel(root.panel, new Vector2(m.x, Screen.height - m.y));
            PointerOver = icon.worldBound.Contains(p);
        }
        if (mode != Mode.None && cancel != null && cancel.WasPressedThisFrame()) Close();
    }

    // ---------------- The sheet ----------------

    VisualElement NewSheet(string extraClass = null)
    {
        if (running != null) { StopCoroutine(running); running = null; }
        modal.Clear();
        sheet = new VisualElement();
        sheet.AddToClassList("gl-sheet");
        if (extraClass != null) sheet.AddToClassList(extraClass);
        var close = new Button(Close) { text = "x" };
        close.AddToClassList("kit-button");
        close.AddToClassList("gl-close");
        sheet.Add(close);
        modal.Add(sheet);
        modal.style.display = DisplayStyle.Flex;
        if (mode == Mode.None) PlayerLock.Lock(this);
        return sheet;
    }

    static Label Text(string text, string cls)
    {
        var l = new Label(text) { enableRichText = true };
        l.AddToClassList(cls);
        return l;
    }

    public void Close()
    {
        if (mode == Mode.None) return;
        if (running != null) { StopCoroutine(running); running = null; }
        mode = Mode.None;
        modal.style.display = DisplayStyle.None;
        modal.Clear();
        PlayerLock.Unlock(this);
    }

    /// <summary>A loose sheet of aged paper: the letter, or the newest clue.</summary>
    public void ShowPaper(string body, string signature)
    {
        var s = NewSheet();
        mode = Mode.Paper;
        s.Add(Text(body, "gl-body"));
        s.Add(Text("— " + signature, "gl-sign"));
        GameAudio.Play(a => a.uiClick);
        if (!GhostLetter.HasPapers) return;
        unread = false;
    }

    /// <summary>A new paper arrived while you weren't looking at the letter (the scroll pulses until you open it).</summary>
    public void MarkUnread() => unread = true;

    // ---------------- The display case lock ----------------

    public void OpenLock(Func<int[], bool> tryCode)
    {
        var s = NewSheet("gl-lock");
        mode = Mode.Lock;
        s.Add(Text("A small brass lock", "gl-title"));
        var row = new VisualElement();
        row.AddToClassList("gl-wheels");
        s.Add(row);
        var digits = new int[3];
        for (int i = 0; i < 3; i++)
        {
            int k = i;
            var box = new VisualElement();
            box.AddToClassList("wheel");
            box.AddToClassList("gl-wheel");
            if (i == 2) box.AddToClassList("last");
            row.Add(box);
            PCMembersView.Wheel w = null;
            w = new PCMembersView.Wheel(box, "0", 1, () => digits[k] + 1, v => { digits[k] = v - 1; w.Refresh(); },
                (v, d) => (v - 1 + d + 10) % 10 + 1, v => (v - 1).ToString());
            w.Refresh();
        }
        var note = Text("", "gl-note");
        var turn = new Button { text = "Turn the key" };
        turn.AddToClassList("kit-button");
        turn.AddToClassList("gl-action");
        turn.clicked += () =>
        {
            if (tryCode(digits.ToArray()))
            {
                turn.SetEnabled(false);
                note.text = "Click. The lock turns. A card is tucked behind the glass.";
                running = StartCoroutine(After(1.4f, () => GhostLetter.Instance?.AfterCaseOpened()));
                return;
            }
            note.text = "The lock won't turn.";
            GameAudio.Play(a => a.wrongBook);
            sheet.experimental.animation.Start(0f, 1f, 260, (e, t) => e.style.translate = new Translate(Mathf.Sin(t * 30f) * 6f * (1f - t), 0));
        };
        s.Add(turn);
        s.Add(note);
    }

    IEnumerator After(float seconds, Action then)
    {
        yield return new WaitForSecondsRealtime(seconds);
        running = null;
        then?.Invoke();
    }

    // ---------------- The typewriter ----------------

    public void OpenTypewriter(Func<string, bool> tryName, Func<string> hint)
    {
        var s = NewSheet("gl-typewriter");
        mode = Mode.Typewriter;
        s.Add(Text("A sheet is wound into the typewriter.", "gl-title"));
        var page = Text("", "gl-typed");
        s.Add(page);
        var field = new TextField { maxLength = 24 };
        field.AddToClassList("gl-field");
        var type = new Button { text = "Type" };
        type.AddToClassList("kit-button");
        type.AddToClassList("gl-action");
        void Submit()
        {
            string v = (field.value ?? "").Replace("<", "").Replace(">", "").Trim();
            if (v.Length == 0 || running != null) return;
            field.value = "";
            bool right = tryName(v);
            running = StartCoroutine(TypeOut(page, v, right, hint(), field, type));
        }
        type.clicked += Submit;
        field.RegisterCallback<KeyDownEvent>(e => { if (e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter) Submit(); }, TrickleDown.TrickleDown);
        s.Add(field);
        s.Add(type);
        field.schedule.Execute(() => field.Focus());
    }

    /// <summary>The keys type what you wrote. Wrong: they strike it out on their own and type "no." (and, every third
    /// time, the start of her name). Right: they keep going by themselves, "thank you."</summary>
    IEnumerator TypeOut(Label page, string typed, bool right, string hint, TextField field, Button type)
    {
        field.SetEnabled(false);
        type.SetEnabled(false);
        string text = "";
        IEnumerator Key(string add, float gap)
        {
            foreach (char ch in add)
            {
                text += ch;
                page.text = text;
                if (ch != '\n') GameAudio.Type(ch == ' ');
                yield return new WaitForSecondsRealtime(gap);
            }
        }
        yield return Key(typed, 0.07f);
        yield return new WaitForSecondsRealtime(0.5f);
        if (right)
        {
            yield return Key("\n\n", 0.2f);
            yield return Key("thank you.", 0.16f); // slower: it isn't you typing
            yield return new WaitForSecondsRealtime(1.4f);
            running = null;
            GhostLetter.Instance?.AfterNameTyped();
            yield break;
        }
        // The keys hammer an X over every letter, by themselves.
        int start = text.Length - typed.Length;
        for (int i = 0; i < typed.Length; i++)
        {
            if (typed[i] == ' ') continue;
            text = text.Substring(0, start + i) + "X" + text.Substring(start + i + 1);
            page.text = text;
            GameAudio.Type(false);
            yield return new WaitForSecondsRealtime(0.05f);
        }
        yield return Key("\nno.", 0.18f);
        if (!string.IsNullOrEmpty(hint)) { yield return new WaitForSecondsRealtime(0.4f); yield return Key("\n" + hint, 0.22f); }
        running = null;
        field.SetEnabled(true);
        type.SetEnabled(true);
        field.Focus();
    }
}
