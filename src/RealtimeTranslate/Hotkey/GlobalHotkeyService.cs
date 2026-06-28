using System;
using System.Runtime.InteropServices;
using System.Windows.Interop;
using RealtimeTranslate.Config;

namespace RealtimeTranslate.Hotkey;

/// <summary>
/// システム全体のグローバルホットキーを登録する。メッセージ専用ウィンドウ（HWND_MESSAGE）を
/// 生成し、WM_HOTKEY を受けて <see cref="Pressed"/> を発火する。UI スレッドで生成すること。
/// </summary>
public sealed class GlobalHotkeyService : IDisposable
{
    private const int WM_HOTKEY = 0x0312;
    private const int HotkeyId = 0x4253;

    private const uint MOD_ALT = 0x0001;
    private const uint MOD_CONTROL = 0x0002;
    private const uint MOD_SHIFT = 0x0004;
    private const uint MOD_WIN = 0x0008;
    private const uint MOD_NOREPEAT = 0x4000;

    private static readonly IntPtr HwndMessage = new(-3);

    private HwndSource? _source;
    private bool _registered;

    public event EventHandler? Pressed;

    /// <summary>設定のホットキーを登録する。再登録時は一度解除してから登録し直す。</summary>
    public bool Register(AppSettings settings)
    {
        EnsureSource();
        Unregister();

        uint modifiers = MOD_NOREPEAT;
        if (settings.HotkeyCtrl)
        {
            modifiers |= MOD_CONTROL;
        }

        if (settings.HotkeyShift)
        {
            modifiers |= MOD_SHIFT;
        }

        if (settings.HotkeyAlt)
        {
            modifiers |= MOD_ALT;
        }

        if (settings.HotkeyWin)
        {
            modifiers |= MOD_WIN;
        }

        _registered = RegisterHotKey(_source!.Handle, HotkeyId, modifiers, settings.HotkeyVirtualKey);
        return _registered;
    }

    public void Unregister()
    {
        if (_registered && _source is not null)
        {
            UnregisterHotKey(_source.Handle, HotkeyId);
            _registered = false;
        }
    }

    private void EnsureSource()
    {
        if (_source is not null)
        {
            return;
        }

        var parameters = new HwndSourceParameters("RealtimeTranslateHotkey")
        {
            Width = 0,
            Height = 0,
            ParentWindow = HwndMessage, // メッセージ専用ウィンドウ
            WindowStyle = 0,
        };

        _source = new HwndSource(parameters);
        _source.AddHook(WndProc);
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_HOTKEY && wParam.ToInt32() == HotkeyId)
        {
            Pressed?.Invoke(this, EventArgs.Empty);
            handled = true;
        }

        return IntPtr.Zero;
    }

    public void Dispose()
    {
        Unregister();
        if (_source is not null)
        {
            _source.RemoveHook(WndProc);
            _source.Dispose();
            _source = null;
        }
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);
}
