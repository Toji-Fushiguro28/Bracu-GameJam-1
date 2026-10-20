using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

/// <summary>
/// The game's mouse cursor, from the TravelBook kit: Cursor01c normally, Cursor01d over something you can click
/// (an object in the library or a UI button) and while the button is held. Each click plays the little
/// MouseCursorClick01a animation (frames 1-3), then it's back to the arrow. The 14x16 art is scaled up in whole
/// pixels to suit the screen (2x at 1080p), so it stays crisp. On its own root object in the main scene; it lives on
/// through scene loads (the intro, a return to the title screen), and a reloaded copy steps aside for it.
/// </summary>
public class GameCursor : MonoBehaviour
{
    [Tooltip("UI_TravelBook_Cursor01c (Read/Write on).")]
    public Texture2D normal;
    [Tooltip("UI_TravelBook_Cursor01d (Read/Write on).")]
    public Texture2D hover;
    [Tooltip("Where the click lands, in the original art's pixels (the arrow's tip).")]
    public Vector2 hotspot = new Vector2(1f, 1f);
    [Tooltip("MouseCursorClick01a frames 1, 2, 3 (Read/Write on), played on each click.")]
    public Texture2D[] click;
    [Tooltip("The arrow's tip in the 21x21 click frames (the arrow sits 7 right, 5 down), so it doesn't jump.")]
    public Vector2 clickHotspot = new Vector2(8f, 6f);
    [Tooltip("Seconds per click frame.")]
    public float clickFrameTime = 0.06f;

    Texture2D normalBig, hoverBig;
    Texture2D[] clickBig;
    float clickStart = -1f;
    int clickFrame = -1;
    int scale;
    bool showingHover;
    UIDocument[] docs;
    float nextDocs;

    static GameCursor instance;

    void Awake()
    {
        if (instance != null && instance != this) { Destroy(gameObject); return; }
        instance = this;
        DontDestroyOnLoad(gameObject);
    }

    void OnEnable() => Apply(true);

    void OnDestroy() => FreeScaled();

    void FreeScaled()
    {
        if (normalBig != null) Destroy(normalBig);
        if (hoverBig != null) Destroy(hoverBig);
        if (clickBig != null) foreach (var t in clickBig) if (t != null) Destroy(t);
        clickBig = null;
    }

    void Update()
    {
        if (ScaleForScreen() != scale) Apply(true);
        var mouse = Mouse.current;
        // A click: play the little click animation, then carry on as normal.
        if (mouse != null && mouse.leftButton.wasPressedThisFrame && clickBig != null && clickBig.Length > 0)
        {
            clickStart = Time.unscaledTime;
            clickFrame = -1;
        }
        if (clickStart >= 0f)
        {
            int f = Mathf.FloorToInt((Time.unscaledTime - clickStart) / Mathf.Max(0.01f, clickFrameTime));
            if (f < clickBig.Length)
            {
                if (f != clickFrame && clickBig[f] != null)
                {
                    clickFrame = f;
                    UnityEngine.Cursor.SetCursor(clickBig[f], clickHotspot * scale, CursorMode.Auto);
                }
                return;
            }
            clickStart = -1f;
            clickFrame = -1;
            showingHover = OverClickable() || (mouse != null && mouse.leftButton.isPressed);
            Apply(false); // back to the arrow
            return;
        }
        bool want = OverClickable() || (mouse != null && mouse.leftButton.isPressed);
        if (want != showingHover) { showingHover = want; Apply(false); }
    }

    static int ScaleForScreen() => Mathf.Clamp(Mathf.RoundToInt(Screen.height / 540f), 1, 4);

    void Apply(bool force)
    {
        int s = ScaleForScreen();
        if (force || s != scale || normalBig == null)
        {
            scale = s;
            FreeScaled();
            normalBig = Upscale(normal, s);
            hoverBig = Upscale(hover, s);
            if (click != null)
            {
                clickBig = new Texture2D[click.Length];
                for (int i = 0; i < click.Length; i++) clickBig[i] = Upscale(click[i], s);
            }
        }
        var tex = showingHover && hoverBig != null ? hoverBig : normalBig;
        if (tex == null) return;
        UnityEngine.Cursor.SetCursor(tex, hotspot * scale, CursorMode.Auto);
    }

    /// <summary>A whole-pixel bigger copy (nearest neighbour). Null if the texture can't be read (Read/Write off).</summary>
    static Texture2D Upscale(Texture2D src, int s)
    {
        if (src == null || !src.isReadable) return null;
        var pixels = src.GetPixels32();
        int w = src.width, h = src.height;
        var big = new Texture2D(w * s, h * s, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, name = src.name + "_x" + s };
        var outPixels = new Color32[w * s * h * s];
        for (int y = 0; y < h * s; y++)
            for (int x = 0; x < w * s; x++)
                outPixels[y * w * s + x] = pixels[(y / s) * w + (x / s)];
        big.SetPixels32(outPixels);
        big.Apply(false, false);
        return big;
    }

    /// <summary>Over something you can click: a library object in reach of the click system, or a UI button.</summary>
    bool OverClickable()
    {
        if (InteractionController.HoveringSomething) return true;
        if (Pointer.current == null) return false;
        if (Time.unscaledTime >= nextDocs || docs == null) { docs = FindObjectsByType<UIDocument>(); nextDocs = Time.unscaledTime + 2f; }
        Vector2 screen = Pointer.current.position.ReadValue();
        Vector2 flipped = new Vector2(screen.x, Screen.height - screen.y);
        foreach (var d in docs)
        {
            var panel = d != null && d.isActiveAndEnabled ? d.rootVisualElement?.panel : null;
            if (panel == null) continue;
            var picked = panel.Pick(RuntimePanelUtils.ScreenToPanel(panel, flipped));
            for (var e = picked; e != null; e = e.parent)
                if (e is Button b && b.enabledInHierarchy) return true;
        }
        return false;
    }
}
