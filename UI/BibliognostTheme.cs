using System.Numerics;
using Dalamud.Bindings.ImGui;

namespace Bibliognost.UI;

internal static class BibliognostTheme
{
    internal sealed record Palette(string Name, Vector4 Accent, Vector4 Bright, Vector4 Text, Vector4 Dim, Vector4 Surface, Vector4 HoverSurface);
    internal static readonly Palette[] Palettes =
    [
        new("Archive Gold", new(.84f, .67f, .30f, 1), new(1f, .84f, .46f, 1), new(.89f, .90f, .92f, 1), new(.55f, .58f, .63f, 1), new(.045f, .052f, .068f, .97f), new(.18f, .15f, .08f, 1)),
        new("Moonlit Azure", new(.29f, .63f, .92f, 1), new(.55f, .82f, 1f, 1), new(.91f, .95f, 1f, 1), new(.56f, .65f, .76f, 1), new(.025f, .045f, .072f, .98f), new(.055f, .15f, .24f, 1)),
        new("Amethyst Nocturne", new(.68f, .43f, .91f, 1), new(.86f, .66f, 1f, 1), new(.95f, .92f, .98f, 1), new(.66f, .58f, .73f, 1), new(.052f, .032f, .071f, .98f), new(.18f, .08f, .24f, 1)),
        new("Verdant Aether", new(.28f, .76f, .58f, 1), new(.55f, 1f, .78f, 1), new(.90f, .97f, .94f, 1), new(.54f, .68f, .62f, 1), new(.025f, .060f, .052f, .98f), new(.05f, .19f, .14f, 1)),
        new("Crimson Manuscript", new(.86f, .32f, .37f, 1), new(1f, .58f, .61f, 1), new(.97f, .92f, .92f, 1), new(.69f, .57f, .59f, 1), new(.065f, .030f, .038f, .98f), new(.23f, .06f, .08f, 1)),
    ];
    private static Palette current = Palettes[0];
    public static Vector4 Gold => current.Accent;
    public static Vector4 GoldBright => current.Bright;
    public static Vector4 Text => current.Text;
    public static Vector4 Dim => current.Dim;
    public static Vector4 Surface => current.Surface;
    internal static void Apply(string? name) => current = Palettes.FirstOrDefault(p => p.Name.Equals(name, StringComparison.OrdinalIgnoreCase)) ?? Palettes[0];
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
        var bg = Vector4.Lerp(current.Surface with { W = 1f }, current.HoverSurface, t);
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
        draw.AddRect(min, max, ImGui.GetColorU32(Gold with { W = alpha * .28f }), 6, ImDrawFlags.None, 7f);
        draw.AddRect(min, max, ImGui.GetColorU32(Gold with { W = alpha * .55f }), 6, ImDrawFlags.None, 3f);
        draw.AddRect(min, max, ImGui.GetColorU32(GoldBright with { W = .38f + pulse * .22f }), 6, ImDrawFlags.None, 1.2f);
    }
}
