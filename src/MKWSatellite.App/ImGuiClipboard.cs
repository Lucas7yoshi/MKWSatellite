using System.Runtime.InteropServices;
using ImGuiNET;
using Silk.NET.Input;

namespace MKWSatellite.App;

/// <summary>
/// Keyboard fixes for Silk.NET's ImGui controller, which (2.23 with ImGui 1.90) leaves ImGui's clipboard callbacks unset
/// and never sends the modifier keys (ImGuiMod_Ctrl etc.), so Ctrl shortcuts such as copy/paste do nothing in text
/// fields. Routes the clipboard through Silk's keyboard and submits the modifier state before each frame.
/// </summary>
internal static class ImGuiClipboard
{
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate nint GetTextFn(nint userData);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void SetTextFn(nint userData, nint text);

    // Kept alive for as long as ImGui may call them.
    private static readonly GetTextFn GetDelegate = Get;
    private static readonly SetTextFn SetDelegate = Set;
    private static IKeyboard? _keyboard;
    private static nint _text; // UTF-8 copy handed to ImGui; valid until the next get

    /// <summary>Installs the callbacks on the current ImGui context (call again after a new context is created).</summary>
    public static void Install(IInputContext input)
    {
        _keyboard = input.Keyboards.FirstOrDefault();
        if (_keyboard is null) return;
        var io = ImGui.GetIO();
        io.GetClipboardTextFn = Marshal.GetFunctionPointerForDelegate(GetDelegate);
        io.SetClipboardTextFn = Marshal.GetFunctionPointerForDelegate(SetDelegate);
    }

    /// <summary>Submits Ctrl/Shift/Alt/Super; call before the controller starts the ImGui frame.</summary>
    public static void SubmitModifiers()
    {
        if (_keyboard is not { } k) return;
        var io = ImGui.GetIO();
        io.AddKeyEvent(ImGuiKey.ModCtrl, k.IsKeyPressed(Key.ControlLeft) || k.IsKeyPressed(Key.ControlRight));
        io.AddKeyEvent(ImGuiKey.ModShift, k.IsKeyPressed(Key.ShiftLeft) || k.IsKeyPressed(Key.ShiftRight));
        io.AddKeyEvent(ImGuiKey.ModAlt, k.IsKeyPressed(Key.AltLeft) || k.IsKeyPressed(Key.AltRight));
        io.AddKeyEvent(ImGuiKey.ModSuper, k.IsKeyPressed(Key.SuperLeft) || k.IsKeyPressed(Key.SuperRight));
    }

    private static nint Get(nint userData)
    {
        try
        {
            if (_text != 0) Marshal.FreeCoTaskMem(_text);
            _text = Marshal.StringToCoTaskMemUTF8(_keyboard?.ClipboardText ?? "");
            return _text;
        }
        catch { return 0; }
    }

    private static void Set(nint userData, nint text)
    {
        try { if (_keyboard is not null) _keyboard.ClipboardText = Marshal.PtrToStringUTF8(text) ?? ""; }
        catch { }
    }
}
