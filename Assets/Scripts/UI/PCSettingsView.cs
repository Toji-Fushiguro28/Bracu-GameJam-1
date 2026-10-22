using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

/// <summary>Player settings, kept in PlayerPrefs (WebGL safe) and applied on load.</summary>
public static class GameSettings
{
    public const int Steps = 10;

    public static int Master { get => PlayerPrefs.GetInt("set_master", 7); set { PlayerPrefs.SetInt("set_master", Mathf.Clamp(value, 0, Steps)); Apply(); } }
    public static int Music { get => PlayerPrefs.GetInt("set_music", 7); set { PlayerPrefs.SetInt("set_music", Mathf.Clamp(value, 0, Steps)); Apply(); } }
    public static int Sfx { get => PlayerPrefs.GetInt("set_sfx", Steps); set { PlayerPrefs.SetInt("set_sfx", Mathf.Clamp(value, 0, Steps)); Apply(); } }
    public static int Ui { get => PlayerPrefs.GetInt("set_ui", Steps); set { PlayerPrefs.SetInt("set_ui", Mathf.Clamp(value, 0, Steps)); Apply(); } }
    public static float MusicScale => Music / (float)Steps;
    public static float SfxScale => Sfx / (float)Steps;
    public static float UiScale => Ui / (float)Steps;

    static float musicBase = -1f;

    // Every scene load, not just the first (the intro → MainGame switch brings a fresh music source).
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Hook()
    {
        UnityEngine.SceneManagement.SceneManager.sceneLoaded -= OnSceneLoaded;
        UnityEngine.SceneManagement.SceneManager.sceneLoaded += OnSceneLoaded;
        Apply();
    }

    static void OnSceneLoaded(UnityEngine.SceneManagement.Scene scene, UnityEngine.SceneManagement.LoadSceneMode mode) => Apply();

    public static void Apply()
    {
        AudioListener.volume = Master / (float)Steps;
        var bgm = GameObject.Find("Audio_BGM")?.GetComponent<AudioSource>();
        if (bgm != null)
        {
            if (musicBase < 0f) musicBase = bgm.volume;
            bgm.volume = musicBase * Music / Steps;
        }
        ScaleAllSources();
    }

    // Every other sound source keeps its own volume here, so the SFX and UI sliders can scale it.
    // GameAudio's one-shot sources scale per clip instead (Skip), and the music has its own slider.
    static readonly Dictionary<AudioSource, float> baseVolume = new Dictionary<AudioSource, float>();
    static readonly HashSet<AudioSource> uiSources = new HashSet<AudioSource>(), skipped = new HashSet<AudioSource>();

    /// <summary>A UI screen's own source (menus, dialogue blips, shelf, repair, report): follows the UI slider.</summary>
    public static void MarkUi(AudioSource s) { if (s == null) return; uiSources.Add(s); ScaleSource(s); }
    /// <summary>Left alone by the sweep (the source scales its sounds itself, or it's music).</summary>
    public static void Skip(AudioSource s) { if (s != null) skipped.Add(s); }
    /// <summary>A script just set this source's volume (a loop starting): remember it as the unscaled volume.</summary>
    public static void SetBase(AudioSource s, float volume) { if (s == null) return; baseVolume[s] = volume; ScaleSource(s); }

    static void ScaleSource(AudioSource s)
    {
        if (s == null || skipped.Contains(s) || s.gameObject.name == "Audio_BGM") return;
        if (!baseVolume.TryGetValue(s, out float b)) baseVolume[s] = b = s.volume;
        s.volume = b * (uiSources.Contains(s) ? UiScale : SfxScale);
    }

    /// <summary>Visitors and staff bring new sources as they spawn: GameAudio runs this every second.</summary>
    public static void ScaleAllSources()
    {
        foreach (var s in UnityEngine.Object.FindObjectsByType<AudioSource>(FindObjectsInactive.Include)) ScaleSource(s);
        if (baseVolume.Count > 300)
        {
            var gone = new List<AudioSource>();
            foreach (var k in baseVolume.Keys) if (k == null) gone.Add(k);
            foreach (var k in gone) baseVolume.Remove(k);
            uiSources.RemoveWhere(k => k == null);
            skipped.RemoveWhere(k => k == null);
        }
    }
}

/// <summary>PC "Settings" screen: volume, music, fullscreen, main menu, new game. (The mode is only picked when a new game starts.)</summary>
public static class PCSettingsView
{
    static float confirmUntil, menuConfirmUntil;

    public static void Wire(VisualElement root)
    {
        Refresh(root);
    }

    static void Refresh(VisualElement root)
    {
        var list = root.Q<ScrollView>("settings-list");
        list.Clear();
        Volume(list, root, "Volume", GameSettings.Master, v => GameSettings.Master = v);
        Volume(list, root, "Music", GameSettings.Music, v => GameSettings.Music = v);
        Volume(list, root, "Effects", GameSettings.Sfx, v => GameSettings.Sfx = v);
        Volume(list, root, "UI sounds", GameSettings.Ui, v => GameSettings.Ui = v);

        var full = Row(list, "Fullscreen");
        full.Add(Value(Screen.fullScreen ? "On" : "Off"));
        full.Add(Button("Toggle", () => { Screen.fullScreen = !Screen.fullScreen; root.schedule.Execute(() => Refresh(root)).ExecuteLater(200); }));

        var menu = Row(list, "Main menu");
        bool leaving = Time.unscaledTime < menuConfirmUntil;
        menu.Add(Value(leaving ? "Today's progress isn't saved. Sure?" : "Back to the title screen."));
        menu.Add(Button(leaving ? "Yes, leave" : "Go", () =>
        {
            if (Time.unscaledTime < menuConfirmUntil) { GameManager.Instance?.ReturnToMenu(); return; }
            menuConfirmUntil = Time.unscaledTime + 4f;
            Refresh(root);
        }));

        var reset = Row(list, "New game");
        bool confirming = Time.unscaledTime < confirmUntil;
        reset.Add(Value(confirming ? "Are you sure? All progress is lost." : "Start again from Day 1."));
        var b = Button(confirming ? "Yes, reset" : "Reset", () =>
        {
            if (Time.unscaledTime < confirmUntil) { GameManager.Instance?.NewGame(); return; }
            confirmUntil = Time.unscaledTime + 4f;
            Refresh(root);
        });
        b.AddToClassList("danger");
        reset.Add(b);

        root.Q<Label>("settings-note").text = "Your library saves itself every morning when a new day starts.";
    }

    static void Volume(ScrollView list, VisualElement root, string name, int value, Action<int> set)
    {
        var row = Row(list, name);
        var minus = Button("-", () => { set(value - 1); Refresh(root); });
        minus.AddToClassList("vol");
        row.Add(minus);
        var bar = new VisualElement();
        bar.AddToClassList("setting-bar");
        for (int i = 0; i < GameSettings.Steps; i++)
        {
            int v = i + 1;
            var pip = new VisualElement();
            pip.AddToClassList("setting-pip");
            pip.style.height = 6 + i * 2; // a small pixel staircase, like a volume meter
            pip.EnableInClassList("on", i < value);
            pip.RegisterCallback<ClickEvent>(_ => { set(v == value ? 0 : v); Refresh(root); });
            bar.Add(pip);
        }
        row.Add(bar);
        var plus = Button("+", () => { set(value + 1); Refresh(root); });
        plus.AddToClassList("vol");
        row.Add(plus);
    }

    static VisualElement Row(ScrollView list, string name)
    {
        var row = new VisualElement();
        row.AddToClassList("setting-row");
        var label = new Label(name);
        label.AddToClassList("setting-name");
        row.Add(label);
        list.Add(row);
        return row;
    }

    static Label Value(string text)
    {
        var l = new Label(text);
        l.AddToClassList("setting-value");
        return l;
    }

    static Button Button(string text, Action onClick)
    {
        var b = new Button(onClick) { text = text };
        b.AddToClassList("pc-button");
        b.AddToClassList("small");
        return b;
    }
}
