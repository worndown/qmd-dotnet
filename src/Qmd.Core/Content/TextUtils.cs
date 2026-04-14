using System.Text;

namespace Qmd.Core.Content;

internal static class TextUtils
{
    internal static string AddLineNumbers(string text, int startLine = 1)
    {
        var lines = text.Split('\n');
        var sb = new StringBuilder();
        for (int i = 0; i < lines.Length; i++)
        {
            if (i > 0) sb.Append('\n');
            sb.Append($"{startLine + i}: {lines[i]}");
        }
        return sb.ToString();
    }
}
