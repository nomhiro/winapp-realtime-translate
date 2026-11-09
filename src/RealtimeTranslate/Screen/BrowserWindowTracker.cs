using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;

namespace RealtimeTranslate.Screen;

/// <summary>
/// キャプチャ対象ウィンドウ。<see cref="Bounds"/> はウィンドウ全体、<see cref="ContentBounds"/> は
/// UI Automation で特定した本文領域（タブ・ツールバー等の UI を除外した矩形）。本文不明なら Bounds と同じ。
/// </summary>
public sealed record CaptureTarget(
    IntPtr Hwnd,
    Rectangle Bounds,
    Rectangle ContentBounds,
    string ProcessName,
    string WindowTitle = "");

/// <summary>選択可能なブラウザウィンドウ。</summary>
public sealed record BrowserWindowCandidate(
    IntPtr Hwnd,
    Rectangle Bounds,
    string ProcessName,
    string WindowTitle);

/// <summary>ブラウザ候補から自動追従または固定対象を決定する。</summary>
public sealed class BrowserTargetSelector
{
    private IntPtr _lastAutomaticHwnd;

    public IntPtr? PinnedHwnd { get; private set; }

    public bool Pin(IntPtr hwnd, IReadOnlyCollection<BrowserWindowCandidate> candidates)
    {
        if (!candidates.Any(candidate => candidate.Hwnd == hwnd))
        {
            return false;
        }

        PinnedHwnd = hwnd;
        _lastAutomaticHwnd = hwnd;
        return true;
    }

    public void FollowAutomatically() => PinnedHwnd = null;

    public BrowserWindowCandidate? Select(
        IReadOnlyCollection<BrowserWindowCandidate> candidates,
        IntPtr foregroundHwnd)
    {
        if (PinnedHwnd is { } pinnedHwnd)
        {
            return candidates.FirstOrDefault(candidate => candidate.Hwnd == pinnedHwnd);
        }

        BrowserWindowCandidate? foreground = candidates.FirstOrDefault(candidate => candidate.Hwnd == foregroundHwnd);
        if (foreground is not null)
        {
            _lastAutomaticHwnd = foreground.Hwnd;
            return foreground;
        }

        return candidates.FirstOrDefault(candidate => candidate.Hwnd == _lastAutomaticHwnd);
    }
}

/// <summary>ブラウザウィンドウ候補を列挙し、選択されたウィンドウをキャプチャ対象へ変換する。</summary>
public sealed class BrowserWindowTracker
{
    private const int DWMWA_EXTENDED_FRAME_BOUNDS = 9;

    private readonly int _selfProcessId = Environment.ProcessId;
    private readonly object _sync = new();
    private readonly BrowserTargetSelector _selector = new();

    // 本文領域(UIA)のキャッシュ。ウィンドウが変わらない限り再計算しない（UIA 探索は重いため）。
    private IntPtr _contentHwnd;
    private Rectangle _contentWindowBounds;
    private Rectangle _contentRect;

    public IntPtr? PinnedHwnd
    {
        get
        {
            lock (_sync)
            {
                return _selector.PinnedHwnd;
            }
        }
    }

    /// <summary>プロセス名（拡張子なし）が対象ブラウザ一覧に含まれるか。大小無視。</summary>
    public static bool IsBrowserName(string processName, IEnumerable<string> browserNames)
        => browserNames.Any(n => string.Equals(n, processName, StringComparison.OrdinalIgnoreCase));

    /// <summary>現在選択可能なブラウザウィンドウを列挙する。非表示・最小化された窓は含めない。</summary>
    public IReadOnlyList<BrowserWindowCandidate> GetCandidates(IReadOnlyCollection<string> browserProcessNames)
    {
        lock (_sync)
        {
            return EnumerateCandidates(browserProcessNames);
        }
    }

    public bool PinTarget(IntPtr hwnd, IReadOnlyCollection<string> browserProcessNames)
    {
        lock (_sync)
        {
            return _selector.Pin(hwnd, EnumerateCandidates(browserProcessNames));
        }
    }

    public void FollowAutomatically()
    {
        lock (_sync)
        {
            _selector.FollowAutomatically();
        }
    }

    /// <summary>現在の対象候補を取得。見つからなければ null。対象確認では OCR を実行しない。</summary>
    public BrowserWindowCandidate? GetCurrentCandidate(IReadOnlyCollection<string> browserProcessNames)
    {
        lock (_sync)
        {
            return SelectCurrentCandidate(browserProcessNames);
        }
    }

    /// <summary>現在のキャプチャ対象を取得。見つからなければ null。</summary>
    public CaptureTarget? GetCurrentTarget(IReadOnlyCollection<string> browserProcessNames)
    {
        BrowserWindowCandidate? candidate = GetCurrentCandidate(browserProcessNames);
        if (candidate is null)
        {
            return null;
        }

        lock (_sync)
        {
            return new CaptureTarget(
                candidate.Hwnd,
                candidate.Bounds,
                ResolveContentBounds(candidate.Hwnd, candidate.Bounds),
                candidate.ProcessName,
                candidate.WindowTitle);
        }
    }

    private BrowserWindowCandidate? SelectCurrentCandidate(IReadOnlyCollection<string> browserProcessNames)
        => _selector.Select(EnumerateCandidates(browserProcessNames), GetForegroundWindow());

    private List<BrowserWindowCandidate> EnumerateCandidates(IReadOnlyCollection<string> browserNames)
    {
        var candidates = new List<BrowserWindowCandidate>();
        EnumWindowsProc callback = (hwnd, _) =>
        {
            bool visible = IsWindowVisible(hwnd);
            bool minimized = IsIconic(hwnd);
            if (!visible || minimized)
            {
                return true;
            }

            GetWindowThreadProcessId(hwnd, out uint pid);
            if (pid == 0 || pid == (uint)_selfProcessId)
            {
                return true;
            }

            string? processName = GetProcessNameById((int)pid);
            if (processName is null || !IsBrowserName(processName, browserNames))
            {
                return true;
            }

            Rectangle? bounds = GetBounds(hwnd);
            if (bounds is not { } rect || !IsUsableWindow(visible, minimized, rect))
            {
                return true;
            }

            candidates.Add(new BrowserWindowCandidate(hwnd, rect, processName, GetWindowTitle(hwnd)));
            return true;
        };
        EnumWindows(callback, IntPtr.Zero);
        return candidates
            .OrderBy(candidate => candidate.ProcessName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(candidate => candidate.WindowTitle, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    internal static bool IsUsableWindow(bool visible, bool minimized, Rectangle bounds)
        => visible && !minimized && bounds.Width > 0 && bounds.Height > 0;

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

    private static string GetWindowTitle(IntPtr hwnd)
    {
        int length = GetWindowTextLength(hwnd);
        if (length <= 0)
        {
            return string.Empty;
        }

        var title = new StringBuilder(length + 1);
        return GetWindowText(hwnd, title, title.Capacity) > 0 ? title.ToString() : string.Empty;
    }

    /// <summary>DWM の拡張フレーム境界を優先（不可視の縁を除いた実表示範囲）。失敗時は GetWindowRect。</summary>
    private static Rectangle? GetBounds(IntPtr hwnd)
    {
        if (DwmGetWindowAttribute(hwnd, DWMWA_EXTENDED_FRAME_BOUNDS, out RECT dwm, Marshal.SizeOf<RECT>()) == 0)
        {
            Rectangle bounds = RectFromNative(dwm);
            if (bounds.Width > 0 && bounds.Height > 0)
            {
                return bounds;
            }
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

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

    private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowTextLength(IntPtr hWnd);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowVisible(IntPtr hWnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsIconic(IntPtr hWnd);

    [DllImport("dwmapi.dll")]
    private static extern int DwmGetWindowAttribute(IntPtr hwnd, int dwAttribute, out RECT pvAttribute, int cbAttribute);
}
