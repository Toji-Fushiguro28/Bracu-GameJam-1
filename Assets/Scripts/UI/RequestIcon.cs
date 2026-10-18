using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// The "read" request icon: a little closed book drawn in code (the UI kit has no book icon).
/// It draws only while the element has the req-read class; the other request icons are kit sprites.
/// </summary>
public static class RequestIcon
{
    static readonly Color Outline = new Color32(58, 34, 28, 255);
    static readonly Color Cover = new Color32(150, 52, 44, 255);
    static readonly Color Spine = new Color32(108, 36, 32, 255);
    static readonly Color Gold = new Color32(235, 200, 120, 255);
    static readonly Color Pages = new Color32(246, 234, 204, 255);

    public static void Attach(VisualElement icon) => icon.generateVisualContent += Draw;

    static void Draw(MeshGenerationContext ctx)
    {
        var e = ctx.visualElement;
        if (!e.ClassListContains("req-read")) return;
        var r = e.contentRect;
        float s = Mathf.Min(r.width, r.height) / 16f; // a 16x16 pixel grid, like the kit icons
        var o = new Vector2(r.x + (r.width - 16f * s) / 2f, r.y + (r.height - 16f * s) / 2f);
        var p = ctx.painter2D;
        void Px(float x, float y, float w, float h, Color c)
        {
            p.fillColor = c;
            p.BeginPath();
            p.MoveTo(o + new Vector2(x, y) * s);
            p.LineTo(o + new Vector2(x + w, y) * s);
            p.LineTo(o + new Vector2(x + w, y + h) * s);
            p.LineTo(o + new Vector2(x, y + h) * s);
            p.ClosePath();
            p.Fill();
        }
        Px(2, 1, 12, 15, Outline);   // outline
        Px(3, 2, 10, 11, Cover);     // front cover
        Px(3, 2, 2, 11, Spine);      // spine shadow
        Px(6, 4, 6, 1, Gold);        // gold bands
        Px(6, 6, 6, 3, Gold);        // title plate
        Px(7, 7, 4, 1, Cover);
        Px(6, 10, 6, 1, Gold);
        Px(4, 13, 9, 2, Pages);      // page edges at the bottom
        Px(4, 14, 9, 0.5f, new Color32(214, 196, 160, 255));
    }
}
