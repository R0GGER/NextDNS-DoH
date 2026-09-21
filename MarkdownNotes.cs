namespace NextDnsDoh;

internal static class MarkdownNotes
{
    public static void Render(RichTextBox box, string markdown)
    {
        box.Clear();
        if (string.IsNullOrWhiteSpace(markdown))
        {
            box.AppendText("No release notes.");
            return;
        }

        using var body = new Font("Segoe UI", 9F);
        using var heading1 = new Font("Segoe UI", 12F, FontStyle.Bold);
        using var heading2 = new Font("Segoe UI", 11F, FontStyle.Bold);
        using var heading3 = new Font("Segoe UI", 9F, FontStyle.Bold);
        using var bold = new Font("Segoe UI", 9F, FontStyle.Bold);
        using var italic = new Font("Segoe UI", 9F, FontStyle.Italic);
        using var code = new Font("Consolas", 9F);
        using var link = new Font("Segoe UI", 9F, FontStyle.Underline);

        box.Font = body;
        var lines = markdown.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        var inFence = false;

        foreach (var raw in lines)
        {
            var line = raw.TrimEnd();
            if (line.StartsWith("```", StringComparison.Ordinal))
            {
                inFence = !inFence;
                continue;
            }

            if (inFence)
            {
                AppendRun(box, line + "\n", code, box.ForeColor);
                continue;
            }

            if (line.Length == 0)
            {
                box.AppendText("\n");
                continue;
            }

            if (IsRule(line))
            {
                AppendRun(box, "────────\n", body, SystemColors.GrayText);
                continue;
            }

            if (line.StartsWith("### ", StringComparison.Ordinal))
            {
                AppendRun(box, line.Substring(4).Trim() + "\n", heading3, box.ForeColor);
                continue;
            }

            if (line.StartsWith("## ", StringComparison.Ordinal))
            {
                AppendRun(box, line.Substring(3).Trim() + "\n", heading2, box.ForeColor);
                continue;
            }

            if (line.StartsWith("# ", StringComparison.Ordinal))
            {
                AppendRun(box, line.Substring(2).Trim() + "\n", heading1, box.ForeColor);
                continue;
            }

            if (TryBullet(line, out var indent, out var bullet))
            {
                AppendRun(box, new string(' ', indent) + "• ", body, box.ForeColor);
                AppendInline(box, bullet, body, bold, italic, code, link);
                box.AppendText("\n");
                continue;
            }

            AppendInline(box, line, body, bold, italic, code, link);
            box.AppendText("\n");
        }

        box.SelectionStart = 0;
        box.SelectionLength = 0;
        box.SelectionFont = body;
        box.SelectionColor = box.ForeColor;
    }

    private static bool IsRule(string line)
    {
        var text = line.Trim();
        if (text.Length < 3)
        {
            return false;
        }

        return text.Trim('-').Length == 0 || text.Trim('*').Length == 0 || text.Trim('_').Length == 0;
    }

    private static bool TryBullet(string line, out int indent, out string text)
    {
        indent = 0;
        text = "";
        var i = 0;
        while (i < line.Length && line[i] == ' ')
        {
            i++;
        }

        if (i >= line.Length || (line[i] != '-' && line[i] != '*' && line[i] != '+'))
        {
            return false;
        }

        if (i + 1 >= line.Length || line[i + 1] != ' ')
        {
            return false;
        }

        indent = Math.Min(i, 8);
        text = line.Substring(i + 2).Trim();
        return true;
    }

    private static void AppendInline(
        RichTextBox box,
        string text,
        Font body,
        Font bold,
        Font italic,
        Font code,
        Font link)
    {
        var i = 0;
        while (i < text.Length)
        {
            if (TryDelimited(text, i, "**", out var inner, out var end) ||
                TryDelimited(text, i, "__", out inner, out end))
            {
                AppendRun(box, inner, bold, box.ForeColor);
                i = end;
                continue;
            }

            if (text[i] == '`' && TryDelimited(text, i, "`", out inner, out end))
            {
                AppendRun(box, inner, code, box.ForeColor);
                i = end;
                continue;
            }

            if (text[i] == '*' && (i + 1 >= text.Length || text[i + 1] != '*') &&
                TryDelimited(text, i, "*", out inner, out end) &&
                inner.Length > 0 && inner[0] != ' ')
            {
                AppendRun(box, inner, italic, box.ForeColor);
                i = end;
                continue;
            }

            if (TryLink(text, i, out var label, out end))
            {
                AppendRun(box, label, link, Color.FromArgb(0, 102, 204));
                i = end;
                continue;
            }

            var next = NextMarkup(text, i);
            AppendRun(box, text.Substring(i, next - i), body, box.ForeColor);
            i = next;
        }
    }

    private static bool TryDelimited(string text, int start, string delim, out string inner, out int end)
    {
        inner = "";
        end = start;
        if (start + delim.Length >= text.Length ||
            string.CompareOrdinal(text, start, delim, 0, delim.Length) != 0)
        {
            return false;
        }

        var close = text.IndexOf(delim, start + delim.Length, StringComparison.Ordinal);
        if (close < 0)
        {
            return false;
        }

        inner = text.Substring(start + delim.Length, close - start - delim.Length);
        if (inner.Length == 0)
        {
            return false;
        }

        end = close + delim.Length;
        return true;
    }

    private static bool TryLink(string text, int start, out string label, out int end)
    {
        label = "";
        end = start;
        if (text[start] != '[')
        {
            return false;
        }

        var close = text.IndexOf(']', start + 1);
        if (close < 0 || close + 1 >= text.Length || text[close + 1] != '(')
        {
            return false;
        }

        var urlEnd = text.IndexOf(')', close + 2);
        if (urlEnd < 0)
        {
            return false;
        }

        label = text.Substring(start + 1, close - start - 1);
        if (label.Length == 0)
        {
            label = text.Substring(close + 2, urlEnd - close - 2);
        }

        end = urlEnd + 1;
        return label.Length > 0;
    }

    private static int NextMarkup(string text, int start)
    {
        for (var i = start + 1; i < text.Length; i++)
        {
            var c = text[i];
            if (c is '*' or '_' or '`' or '[')
            {
                return i;
            }
        }

        return text.Length;
    }

    private static void AppendRun(RichTextBox box, string text, Font font, Color color)
    {
        if (text.Length == 0)
        {
            return;
        }

        box.SelectionStart = box.TextLength;
        box.SelectionLength = 0;
        box.SelectionFont = font;
        box.SelectionColor = color;
        box.AppendText(text);
    }
}
