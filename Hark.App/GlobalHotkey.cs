using System.Runtime.InteropServices;
using System.Windows.Interop;

namespace Hark.App;

/// <summary>
/// Registers a system-wide hotkey via Win32 <c>RegisterHotKey</c> against a message-only window and
/// raises <see cref="Pressed"/> when it fires. Behaves like other Windows global hotkeys: it is
/// active only while the app runs and is released on dispose (it does not persist across login).
/// </summary>
public sealed class GlobalHotkey : IDisposable
{
    // Modifier flags for RegisterHotKey.
    public const uint ModAlt = 0x0001;
    public const uint ModControl = 0x0002;
    public const uint ModShift = 0x0004;
    public const uint ModWin = 0x0008;
    private const uint ModNoRepeat = 0x4000;

    private const int WmHotkey = 0x0312;
    private const int HotkeyId = 0x4841; // 'HA'

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    private readonly HwndSource _source;
    private bool _registered;
    private bool _disposed;

    /// <summary>Raised on the UI thread when the hotkey combination is pressed.</summary>
    public event Action? Pressed;

    /// <summary>True when the OS accepted the hotkey registration.</summary>
    public bool IsRegistered => _registered;

    /// <summary>
    /// Creates a hidden message-only window and attempts to register the given hotkey.
    /// </summary>
    /// <param name="modifiers">Combination of the Mod* flags.</param>
    /// <param name="virtualKey">Virtual-key code (e.g. 0x48 for 'H').</param>
    public GlobalHotkey(uint modifiers, uint virtualKey)
    {
        var parameters = new HwndSourceParameters("HARK.HotkeyWindow")
        {
            // Message-only window: parent = HWND_MESSAGE (-3).
            ParentWindow = new IntPtr(-3),
            WindowStyle = 0,
        };
        _source = new HwndSource(parameters);
        _source.AddHook(WndProc);

        _registered = RegisterHotKey(_source.Handle, HotkeyId, modifiers | ModNoRepeat, virtualKey);
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WmHotkey && wParam.ToInt32() == HotkeyId)
        {
            Pressed?.Invoke();
            handled = true;
        }
        return IntPtr.Zero;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        if (_registered)
        {
            UnregisterHotKey(_source.Handle, HotkeyId);
            _registered = false;
        }
        _source.RemoveHook(WndProc);
        _source.Dispose();
    }
}
