using System.Text;
using System.Text.Json;
using Qmd.Core.Models;

namespace Qmd.Core.Formatting;

public static class SingleDocumentFormatter
{
    public static string Format(DocumentResult doc, OutputFormat format)
    {
        return format switch
        {
            OutputFormat.Json => ToJson(doc),
            OutputFormat.Md or OutputFormat.Cli => ToMarkdown(doc),
            OutputFormat.Xml => ToXml(doc),
            _ => ToJson(doc),
        };
    }

    public static string ToJson(DocumentResult doc)
    {
        var obj = new Dictionary<string, object?>
        {
            ["file"] = doc.DisplayPath,
            ["title"] = doc.Title,
        };
        if (doc.Context != null) obj["context"] = doc.Context;
        obj["hash"] = doc.Hash;
        obj["modifiedAt"] = doc.ModifiedAt;
        obj["bodyLength"] = doc.BodyLength;
        if (doc.Body != null) obj["body"] = doc.Body;
        return JsonSerializer.Serialize(obj, new JsonSerializerOptions { WriteIndented = true });
    }

    public static string ToMarkdown(DocumentResult doc)
    {
        var sb = new StringBuilder();
        var heading = !string.IsNullOrEmpty(doc.Title) ? doc.Title : doc.DisplayPath;
        sb.AppendLine($"# {heading}");
        if (doc.Context != null) sb.AppendLine($"**Context:** {doc.Context}");
        sb.AppendLine($"**File:** {doc.DisplayPath}");
        sb.AppendLine($"**Modified:** {doc.ModifiedAt}");
        sb.AppendLine();
        if (doc.Body != null)
        {
            sb.AppendLine("---");
            sb.AppendLine();
            sb.AppendLine(doc.Body);
        }
        return sb.ToString();
    }

    public static string ToXml(DocumentResult doc)
    {
        var sb = new StringBuilder();
        sb.AppendLine("<?xml version=\"1.0\" encoding=\"UTF-8\"?>");
        sb.AppendLine("<document>");
        sb.AppendLine($"  <file>{FormatHelpers.EscapeXml(doc.DisplayPath)}</file>");
        sb.AppendLine($"  <title>{FormatHelpers.EscapeXml(doc.Title)}</title>");
        if (doc.Context != null) sb.AppendLine($"  <context>{FormatHelpers.EscapeXml(doc.Context)}</context>");
        sb.AppendLine($"  <hash>{FormatHelpers.EscapeXml(doc.Hash)}</hash>");
        sb.AppendLine($"  <modifiedAt>{FormatHelpers.EscapeXml(doc.ModifiedAt)}</modifiedAt>");
        sb.AppendLine($"  <bodyLength>{doc.BodyLength}</bodyLength>");
        if (doc.Body != null) sb.AppendLine($"  <body>{FormatHelpers.EscapeXml(doc.Body)}</body>");
        sb.AppendLine("</document>");
        return sb.ToString();
    }
}