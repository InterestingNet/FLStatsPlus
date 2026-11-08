using Dalamud.Bindings.ImGui;
using System;
using System.Collections.Generic;
using System.Numerics;

namespace PvpStats.Helpers;

/// <summary>
/// The blue theme from the HTML reports, applied to every plugin window. Wrap the window system's draw call in
/// <see cref="Push"/> / <see cref="Pop"/>. Job, team and placement colours are drawn explicitly by the windows and
/// are not affected. Colour slots are looked up by name at runtime because some were renamed between ImGui versions
/// (e.g. TabActive → TabSelected), so a renamed slot is skipped instead of breaking the build.
/// </summary>
internal static class Theme {
    private static Vector4 Hex(uint rgb, float a = 1f) => new(((rgb >> 16) & 0xFF) / 255f, ((rgb >> 8) & 0xFF) / 255f, (rgb & 0xFF) / 255f, a);

    // report palette
    private static readonly Vector4 Bg = Hex(0x15171F, 0.96f);
    private static readonly Vector4 Panel = Hex(0x14161D);
    private static readonly Vector4 Line = Hex(0x2A3446);
    private static readonly Vector4 Accent = Hex(0x4F8FD9);
    private static readonly Vector4 Accent2 = Hex(0xA9CDF5);
    private static readonly Vector4 Text = Hex(0xE4E9F1);
    private static readonly Vector4 Dim = Hex(0x8D97A8);

    private static readonly (string[] Names, Vector4 Color)[] ColorSpec = [
        (["Text"], Text),
        (["TextDisabled"], Dim),
        (["WindowBg"], Bg),
        (["ChildBg"], new Vector4(0, 0, 0, 0)),
        (["PopupBg"], Hex(0x0E1118, 0.98f)),
        (["Border"], Line),
        (["FrameBg"], Panel),
        (["FrameBgHovered"], Hex(0x1C2638)),
        (["FrameBgActive"], Hex(0x24324A)),
        (["TitleBg"], Hex(0x0F131B)),
        (["TitleBgActive"], Hex(0x1A2B4A)),
        (["TitleBgCollapsed"], Hex(0x0F131B, 0.8f)),
        (["MenuBarBg"], Hex(0x11151D)),
        (["ScrollbarBg"], Hex(0x0F1117)),
        (["ScrollbarGrab"], Hex(0x2A3446)),
        (["ScrollbarGrabHovered"], Hex(0x3B5478)),
        (["ScrollbarGrabActive"], Accent),
        (["CheckMark"], Accent2),
        (["SliderGrab"], Accent),
        (["SliderGrabActive"], Accent2),
        (["Button"], Hex(0x1D2A40)),
        (["ButtonHovered"], Hex(0x2D4A75)),
        (["ButtonActive"], Accent),
        (["Header"], Hex(0x223553, 0.85f)),
        (["HeaderHovered"], Hex(0x2D4A75, 0.9f)),
        (["HeaderActive"], Accent),
        (["Separator"], Line),
        (["SeparatorHovered"], Hex(0x3B5478)),
        (["SeparatorActive"], Accent),
        (["ResizeGrip"], Hex(0x4F8FD9, 0.25f)),
        (["ResizeGripHovered"], Hex(0x4F8FD9, 0.6f)),
        (["ResizeGripActive"], Accent),
        (["Tab"], Hex(0x18202E)),
        (["TabHovered"], Hex(0x2D4A75)),
        (["TabSelected", "TabActive"], Accent),
        (["TabSelectedOverline"], Accent2),
        (["TabDimmed", "TabUnfocused"], Hex(0x141A25)),
        (["TabDimmedSelected", "TabUnfocusedActive"], Hex(0x2D4A75)),
        (["PlotLines"], Accent),
        (["PlotLinesHovered"], Accent2),
        (["PlotHistogram"], Accent),
        (["PlotHistogramHovered"], Accent2),
        (["TableHeaderBg"], Hex(0x1A2232)),
        (["TableBorderStrong"], Line),
        (["TableBorderLight"], Hex(0x1F2737)),
        (["TableRowBg"], new Vector4(0, 0, 0, 0)),
        (["TableRowBgAlt"], new Vector4(1, 1, 1, 0.025f)),
        (["TextSelectedBg"], Hex(0x4F8FD9, 0.35f)),
        (["DragDropTarget"], Accent2),
        (["NavCursor", "NavHighlight"], Accent),
    ];

    private static readonly (string Name, float Value)[] VarSpec = [
        ("WindowRounding", 6f), ("ChildRounding", 4f), ("FrameRounding", 3f), ("PopupRounding", 4f),
        ("ScrollbarRounding", 6f), ("GrabRounding", 3f), ("TabRounding", 3f),
    ];

    private static readonly List<(ImGuiCol Col, Vector4 Color)> Colors = [];
    private static readonly List<(ImGuiStyleVar Var, float Value)> Vars = [];
    private static int _pushedColors, _pushedVars;

    static Theme() {
        foreach(var (names, color) in ColorSpec) {
            foreach(var name in names) {
                if(Enum.TryParse<ImGuiCol>(name, out var col)) {
                    Colors.Add((col, color));
                    break;
                }
            }
        }
        foreach(var (name, value) in VarSpec) {
            if(Enum.TryParse<ImGuiStyleVar>(name, out var v)) {
                Vars.Add((v, value));
            }
        }
    }

    public static void Push() {
        _pushedColors = _pushedVars = 0;
        foreach(var (col, color) in Colors) {
            ImGui.PushStyleColor(col, color);
            _pushedColors++;
        }
        foreach(var (v, value) in Vars) {
            ImGui.PushStyleVar(v, value);
            _pushedVars++;
        }
    }

    public static void Pop() {
        if(_pushedVars > 0) ImGui.PopStyleVar(_pushedVars);
        if(_pushedColors > 0) ImGui.PopStyleColor(_pushedColors);
        _pushedColors = _pushedVars = 0;
    }
}
