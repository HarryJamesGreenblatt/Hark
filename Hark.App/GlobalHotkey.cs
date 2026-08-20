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
    #region Constants

    /// <summary>
    /// Hotkey modifier flag for the ALT key, matching the Win32 <c>MOD_ALT</c> value.
    /// Combine with other <c>Mod*</c> flags using a bitwise OR when registering a hotkey.
    /// </summary>
    public const uint MOD_ALT = 0x0001;

    /// <summary>
    /// Hotkey modifier flag for the CTRL key, matching the Win32 <c>MOD_CONTROL</c> value.
    /// Combine with other <c>Mod*</c> flags using a bitwise OR when registering a hotkey.
    /// </summary>
    public const uint MOD_CONTROL = 0x0002;

    /// <summary>
    /// Hotkey modifier flag for the SHIFT key, matching the Win32 <c>MOD_SHIFT</c> value.
    /// Combine with other <c>Mod*</c> flags using a bitwise OR when registering a hotkey.
    /// </summary>
    public const uint MOD_SHIFT = 0x0004;

    /// <summary>
    /// Hotkey modifier flag for the Windows (WIN) key, matching the Win32 <c>MOD_WIN</c> value.
    /// Combine with other <c>Mod*</c> flags using a bitwise OR when registering a hotkey.
    /// </summary>
    public const uint MOD_WIN = 0x0008;

    /// <summary>
    /// Hotkey modifier flag preventing auto-repeat while the key is held, matching the Win32
    /// <c>MOD_NOREPEAT</c> value. Always combined into registrations to avoid duplicate
    /// <see cref="Pressed"/> events from a held key.
    /// </summary>
    private const uint MOD_NOREPEAT = 0x4000;

    /// <summary>
    /// Windows message identifier sent when a registered hotkey is pressed, matching the
    /// Win32 <c>WM_HOTKEY</c> value.
    /// </summary>
    private const int WM_HOTKEY = 0x0312;

    /// <summary>
    /// Application-defined identifier used to register and match this instance's hotkey
    /// with the Win32 hotkey APIs.
    /// </summary>
    private const int HOTKEY_ID = 0x4841; // 'HA'

    #endregion

    #region Fields

    /// <summary>
    /// Message-only window used solely to receive <c>WM_HOTKEY</c> notifications; it has no
    /// visible UI and is created and torn down alongside this instance.
    /// </summary>
    private readonly HwndSource _source;

    /// <summary>
    /// Indicates whether the hotkey is currently registered with the operating system.
    /// </summary>
    private bool _registered;

    /// <summary>
    /// Indicates whether <see cref="Dispose"/> has already run, guarding against duplicate cleanup.
    /// </summary>
    private bool _disposed;

    #endregion

    #region Properties

    /// <summary>
    /// Gets a value indicating whether the hotkey was successfully registered with the
    /// operating system. When <see langword="false"/>, the key combination may already be
    /// in use by another application.
    /// </summary>
    public bool IsRegistered => _registered;

    #endregion

    #region Events

    /// <summary>
    /// Raised on the UI thread when the registered hotkey combination is pressed.
    /// </summary>
    public event Action? Pressed;

    #endregion

    #region Constructor(s)
    /// <summary>
    /// Creates a message-only window
    /// Check <see cref="IsRegistered"/> after construction to confirm success.
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

        _registered = RegisterHotKey(_source.Handle, HOTKEY_ID, modifiers | MOD_NOREPEAT, virtualKey);
    }

    #endregion

    #region Methods

    /// <summary>
    /// Processes the WM_HOTKEY message and raises <see cref="Pressed"/> if it matches the registered hotkey.
    /// </summary>
    /// <param name="hwnd">Handle to the window receiving the message.</param>
    /// <param name="msg">The message identifier.</param>
    /// <param name="wParam">Additional message information. The contents of this parameter depend on the value of the <paramref name="msg"/> parameter.</param>
    /// <param name="lParam">Additional message information. The contents of this parameter depend on the value of the <paramref name="msg"/> parameter.</param>
    /// <param name="handled">True if the message was handled; otherwise, false.</param>
    /// <returns>IntPtr.Zero</returns>
    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_HOTKEY && wParam.ToInt32() == HOTKEY_ID)
        {
            Pressed?.Invoke();
            handled = true;
        }
        return IntPtr.Zero;
    }

    /// <summary>
    /// Unregisters the hotkey and disposes the message-only window.
    /// </summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        if (_registered)
        {
            UnregisterHotKey(_source.Handle, HOTKEY_ID);
            _registered = false;
        }
        _source.RemoveHook(WndProc);
        _source.Dispose();
    }

    #region Win32 Interop

    /// <summary>
    /// Defines the platform invoke signature for the Win32 <c>RegisterHotKey</c> function,
    /// which associates a hotkey with a window so that a <c>WM_HOTKEY</c> message is posted
    /// to it when the key combination is pressed.
    /// </summary>
    /// <param name="hWnd">Handle to the window that will receive <c>WM_HOTKEY</c> messages.</param>
    /// <param name="id">Identifier of the hotkey; must be unique for the specified window.</param>
    /// <param name="fsModifiers">Combination of <c>Mod*</c> flags specifying keys that must be held with <paramref name="vk"/>.</param>
    /// <param name="vk">Virtual-key code of the hotkey.</param>
    /// <returns><see langword="true"/> if the hotkey was registered successfully; otherwise, <see langword="false"/>.</returns>
    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    /// <summary>
    /// Defines the platform invoke signature for the Win32 <c>UnregisterHotKey</c> function,
    /// which frees a hotkey previously registered with <see cref="RegisterHotKey"/>.
    /// </summary>
    /// <param name="hWnd">Handle to the window associated with the hotkey to be freed.</param>
    /// <param name="id">Identifier of the hotkey to be freed.</param>
    /// <returns><see langword="true"/> if the hotkey was unregistered successfully; otherwise, <see langword="false"/>.</returns>
    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    #endregion

    #endregion
}
