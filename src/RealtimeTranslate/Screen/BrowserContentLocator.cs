using System;
using System.Drawing;
using System.Windows.Automation;

namespace RealtimeTranslate.Screen;

/// <summary>
/// UI Automation で Chromium/Edge 等の「本文(Document)領域」の画面矩形を特定する。
/// これによりタブ・ツールバー・ブックマーク・サイドパネル等の UI を除外し、
/// ページ本文だけを OCR 対象にできる。特定できなければ null。
/// </summary>
public static class BrowserContentLocator
{
    public static Rectangle? GetContentRect(IntPtr hwnd)
    {
        try
        {
            AutomationElement? root = AutomationElement.FromHandle(hwnd);
            if (root is null)
            {
                return null;
            }

            var condition = new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Document);
            AutomationElementCollection docs = root.FindAll(TreeScope.Descendants, condition);

            Rectangle best = Rectangle.Empty;
            foreach (AutomationElement? doc in docs)
            {
                if (doc is null)
                {
                    continue;
                }

                System.Windows.Rect r = doc.Current.BoundingRectangle;
                if (double.IsInfinity(r.Width) || r.Width <= 1 || r.Height <= 1)
                {
                    continue;
                }

                var rect = new Rectangle((int)r.X, (int)r.Y, (int)r.Width, (int)r.Height);
                // 最大面積の Document を本文とみなす（iframe 等の小領域を除外）
                if ((long)rect.Width * rect.Height > (long)best.Width * best.Height)
                {
                    best = rect;
                }
            }

            return best.IsEmpty ? null : best;
        }
        catch
        {
            return null;
        }
    }
}
