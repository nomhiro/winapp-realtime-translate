using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Runtime.InteropServices;

namespace RealtimeTranslate.Screen;

/// <summary>
/// キャプチャ対象ウィンドウ。<see cref="Bounds"/> はウィンドウ全体、<see cref="ContentBounds"/> は
/// UI Automation で特定した本文領域（タブ・ツールバー等の UI を除外した矩形）。本文不明なら Bounds と同じ。
/// </summary>
public sealed record CaptureTarget(IntPtr Hwnd, Rectangle Bounds, Rectangle ContentBounds, string ProcessName);

/// <summary>
/// 「アクティブなブラウザウィンドウ」を自動決定する。最前面ウィンドウがブラウザなら対象とし、
/// そうでない（アプリ自身や別アプリが前面の）場合は直前に見ていたブラウザを対象に保持する。
/// ユーザーによる領域選択は行わない。
/// </summary>
public sealed class BrowserWindowTracker
{
    private const int DWMWA_EXTENDED_FRAME_BOUNDS = 9;

    private readonly int _selfProcessId = Environment.ProcessId;
    private IntPtr _lastBrowserHwnd = IntPtr.Zero;

    // 本文領域(UIA)のキャッシュ。ウィンドウが変わらない限り再計算しない（UIA 探索は重いため）。
    private IntPtr _contentHwnd;
    private Rectangle _contentWindowBounds;
    private Rectangle _contentRect;

    /// <summary>プロセス名（拡張子なし）が対象ブラウザ一覧に含まれるか。大小無視。テスト用に分離。</summary>
    public static bool IsBrowserName(string processName, IEnumerable<string> browserNames)
        => browserNames.Any(n => string.Equals(n, processName, StringComparison.OrdinalIgnoreCase));

    /// <summary>現在のキャプチャ対象を取得。見つからなければ null。</summary>
    public CaptureTarget? GetCurrentTarget(IReadOnlyCollection<string> browserProcessNames)
    {
        IntPtr fg = GetForegroundWindow();
        if (fg != IntPtr.Zero && TryGetBrowser(fg, browserProcessNames, out CaptureTarget? target))
        {
            _lastBrowserHwnd = fg;
            return target;
        }

        // 前面がブラウザでない（自アプリ/別アプリ）→ 直前のブラウザにフォールバック
        if (_lastBrowserHwnd != IntPtr.Zero && IsWindow(_lastBrowserHwnd) && IsWindowVisible(_lastBrowserHwnd))
        {
            Rectangle? bounds = GetBounds(_lastBrowserHwnd);
            if (bounds is { } b && b.Width > 0 && b.Height > 0)
            {
                string name = GetProcessName(_lastBrowserHwnd) ?? "browser";
                return new CaptureTarget(_lastBrowserHwnd, b, ResolveContentBounds(_lastBrowserHwnd, b), name);
            }
        }

        return null;
    }

    private bool TryGetBrowser(IntPtr hwnd, IReadOnlyCollection<string> browserNames, out CaptureTarget? target)
    {
        target = null;
        if (!IsWindowVisible(hwnd))
        {
            return false;
        }

        GetWindowThreadProcessId(hwnd, out uint pid);
        if (pid == 0 || pid == (uint)_selfProcessId)
        {
            return false;
        }

        string? name = GetProcessNameById((int)pid);
        if (name is null || !IsBrowserName(name, browserNames))
        {
            return false;
        }

        Rectangle? bounds = GetBounds(hwnd);
        if (bounds is not { } b || b.Width <= 0 || b.Height <= 0)
        {
            return false;
        }

        target = new CaptureTarget(hwnd, b, ResolveContentBounds(hwnd, b), name);
        return true;
    }

    /// <summary>本文(Document)領域を UIA で解決。重い処理のため (hwnd, ウィンドウ矩形) が同じ間はキャッシュを返す。</summary>
    private Rectangle ResolveContentBounds(IntPtr hwnd, Rectangle windowBounds)
    {
        if (hwnd == _contentHwnd && windowBounds == _contentWindowBounds && !_contentRect.IsEmpty)
        {
            return _contentRect;
        }

        Rectangle? content = BrowserContentLocator.GetContentRect(hwnd);
        Rectangle resolved = content is { } r ? Rectangle.Intersect(r, windowBounds) : windowBounds;
        if (resolved.Width <= 0 || resolved.Height <= 0)
        {
            resolved = windowBounds;
        }

        _contentHwnd = hwnd;
        _contentWindowBounds = windowBounds;
        _contentRect = resolved;
        return resolved;
    }

    private static string? GetProcessName(IntPtr hwnd)
    {
        GetWindowThreadProcessId(hwnd, out uint pid);
        return pid == 0 ? null : GetProcessNameById((int)pid);
    }

    private static string? GetProcessNameById(int pid)
    {
        try
        {
            using Process p = Process.GetProcessById(pid);
            return p.ProcessName; // 拡張子なし（例: "chrome"）
        }
        catch
        {
            return null;
        }
    }

    /// <summary>DWM の拡張フレーム境界を優先（不可視の縁を除いた実表示範囲）。失敗時は GetWindowRect。</summary>
    private static Rectangle? GetBounds(IntPtr hwnd)
    {
        if (DwmGetWindowAttribute(hwnd, DWMWA_EXTENDED_FRAME_BOUNDS, out RECT dwm, Marshal.SizeOf<RECT>()) == 0)
        {
            return RectFromNative(dwm);
        }

        if (GetWindowRect(hwnd, out RECT r))
        {
            return RectFromNative(r);
        }

        return null;
    }

    private static Rectangle RectFromNative(RECT r)
        => new(r.Left, r.Top, r.Right - r.Left, r.Bottom - r.Top);

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowVisible(IntPtr hWnd);

    [DllImport("dwmapi.dll")]
    private static extern int DwmGetWindowAttribute(IntPtr hwnd, int dwAttribute, out RECT pvAttribute, int cbAttribute);
}
