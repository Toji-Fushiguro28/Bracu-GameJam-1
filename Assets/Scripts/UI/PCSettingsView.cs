using System;
using UnityEngine;
using UnityEngine.UIElements;

/// <summary>Player settings, kept in PlayerPrefs (WebGL safe) and applied on load.</summary>
public static class GameSettings
{
    public const int Steps = 10;

    public static int Master { get => PlayerPrefs.GetInt("set_master", 8); set { PlayerPrefs.SetInt("set_master", Mathf.Clamp(value, 0, Steps)); Apply(); } }
    public static int Music { get => PlayerPrefs.GetInt("set_music", 7); set { PlayerPrefs.SetInt("set_music", Mathf.Clamp(value, 0, Steps)); Apply(); } }

    static float musicBase = -1f;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    public static void Apply()
    {
        AudioListener.volume = Master / (float)Steps;
        var bgm = GameObject.Find("Audio_BGM")?.GetComponent<AudioSource>();
        if (bgm != null)
        {
            if (musicBase < 0f) musicBase = bgm.volume;
            bgm.volume = musicBase * Music / Steps;
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
        row.Add(Button("-", () => { set(value - 1); Refresh(root); }));
        var bar = new VisualElement();
        bar.AddToClassList("setting-bar");
        for (int i = 0; i < GameSettings.Steps; i++)
        {
            int v = i + 1;
            var pip = new VisualElement();
            pip.AddToClassList("setting-pip");
            pip.style.height = 16 + i * 4; // a pixel staircase, like a volume meter
            pip.EnableInClassList("on", i < value);
            pip.RegisterCallback<ClickEvent>(_ => { set(v == value ? 0 : v); Refresh(root); });
            bar.Add(pip);
        }
        row.Add(bar);
        row.Add(Button("+", () => { set(value + 1); Refresh(root); }));
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
