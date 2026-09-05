using System.Numerics;
using Dalamud.Bindings.ImGui;

namespace Bibliognost.UI;

internal static class BibliognostTheme
{
    public static readonly Vector4 Gold = new(0.84f, 0.67f, 0.30f, 1f);
    public static readonly Vector4 GoldBright = new(1.00f, 0.84f, 0.46f, 1f);
    public static readonly Vector4 Text = new(0.89f, 0.90f, 0.92f, 1f);
    public static readonly Vector4 Dim = new(0.55f, 0.58f, 0.63f, 1f);
    public static readonly Vector4 Surface = new(0.045f, 0.052f, 0.068f, 0.97f);
    private static readonly Dictionary<string, float> Hover = new();

    public static float AnimateHover(string id, bool hovered)
    {
        var current = Hover.GetValueOrDefault(id);
        var target = hovered ? 1f : 0f;
        current += (target - current) * Math.Clamp(ImGui.GetIO().DeltaTime * 12f, 0f, 1f);
        Hover[id] = current;
        return 1f - (1f - current) * (1f - current);
    }

    public static bool AccentButton(string id, string label, Vector2 size)
    {
        ImGui.PushID(id);
        var min = ImGui.GetCursorScreenPos();
        var clicked = ImGui.InvisibleButton("##button", size);
        var hovered = ImGui.IsItemHovered();
        var t = AnimateHover(id, hovered);
        var draw = ImGui.GetWindowDrawList();
        var max = min + size;
        var bg = Vector4.Lerp(new Vector4(0.09f, 0.10f, 0.13f, 1f), new Vector4(0.18f, 0.15f, 0.08f, 1f), t);
        draw.AddRectFilled(min, max, ImGui.GetColorU32(bg), 3f);
        draw.AddRect(min, max, ImGui.GetColorU32(Vector4.Lerp(new Vector4(0.25f, 0.23f, 0.18f, 1f), GoldBright, t)), 3f, ImDrawFlags.None, 1f + t);
        var textSize = ImGui.CalcTextSize(label);
        draw.AddText(min + (size - textSize) / 2f, ImGui.GetColorU32(Vector4.Lerp(Text, GoldBright, t)), label);
        if (hovered && t > .05f)
        {
            var sweepX = min.X + (size.X + 30f) * t - 15f;
            draw.AddLine(new Vector2(sweepX, min.Y + 2), new Vector2(sweepX - 12, max.Y - 2), ImGui.GetColorU32(new Vector4(1, .9f, .55f, .18f)), 5f);
        }
        ImGui.PopID();
        return clicked;
    }

    public static void DrawGlowFrame(string id, bool accent = false)
    {
        var min = ImGui.GetWindowPos() + new Vector2(2, 2);
        var max = min + ImGui.GetWindowSize() - new Vector2(4, 4);
        DrawGlowRect(ImGui.GetWindowDrawList(), min, max, accent ? 1f : .55f, id);
    }

    public static void DrawGlowRect(ImDrawListPtr draw, Vector2 min, Vector2 max, float strength, string id = "glow")
    {
        var pulse = .5f + .5f * MathF.Sin((float)ImGui.GetTime() * .75f + ImGui.GetID(id) % 13);
        var alpha = (.18f + pulse * .14f) * strength;
        draw.AddRect(min, max, ImGui.GetColorU32(new Vector4(1f, .72f, .24f, alpha * .28f)), 6, ImDrawFlags.None, 7f);
        draw.AddRect(min, max, ImGui.GetColorU32(new Vector4(1f, .78f, .30f, alpha * .55f)), 6, ImDrawFlags.None, 3f);
        draw.AddRect(min, max, ImGui.GetColorU32(new Vector4(1f, .84f, .46f, .38f + pulse * .22f)), 6, ImDrawFlags.None, 1.2f);
    }
}
