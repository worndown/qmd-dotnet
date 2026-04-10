using System.Text;
using System.Text.Json;
using Qmd.Core.Models;

namespace Qmd.Core.Formatting;

public static class DocumentFormatter
{
    public static string Format(List<MultiGetFile> results, OutputFormat format)
    {
        return format switch
        {
            OutputFormat.Json => ToJson(results),
            OutputFormat.Csv => ToCsv(results),
            OutputFormat.Files => ToFiles(results),
            OutputFormat.Md or OutputFormat.Cli => ToMarkdown(results),
            OutputFormat.Xml => ToXml(results),
            _ => ToJson(results),
        };
    }

    public static string ToJson(List<MultiGetFile> results)
    {
        var items = results.Select(r =>
        {
            var obj = new Dictionary<string, object?>
            {
                ["file"] = r.DisplayPath,
                ["title"] = r.Title,
            };
            if (r.Context != null) obj["context"] = r.Context;
            if (r.Skipped)
            {
                obj["skipped"] = true;
                obj["reason"] = r.SkipReason;
            }
            else
            {
                obj["body"] = r.Body;
            }
            return obj;
        }).ToList();

        return JsonSerializer.Serialize(items, new JsonSerializerOptions { WriteIndented = true });
    }

    public static string ToCsv(List<MultiGetFile> results)
    {
        var sb = new StringBuilder();
        sb.AppendLine("file,title,context,skipped,body");
        foreach (var r in results)
        {
            sb.AppendLine(string.Join(',',
                FormatHelpers.EscapeCsv(r.DisplayPath),
                FormatHelpers.EscapeCsv(r.Title),
                FormatHelpers.EscapeCsv(r.Context),
                r.Skipped ? "true" : "false",
                FormatHelpers.EscapeCsv(r.Skipped ? r.SkipReason : r.Body)
            ));
        }
        return sb.ToString();
    }

    public static string ToFiles(List<MultiGetFile> results)
    {
        var sb = new StringBuilder();
        foreach (var r in results)
        {
            sb.Append(r.DisplayPath);
            var ctx = r.Context != null ? $",\"{r.Context.Replace("\"", "\"\"")}\"" : "";
            var status = r.Skipped ? ",[SKIPPED]" : "";
            sb.AppendLine($"{ctx}{status}");
        }
        return sb.ToString();
    }

    public static string ToMarkdown(List<MultiGetFile> results)
    {
        var sb = new StringBuilder();
        foreach (var r in results)
        {
            sb.AppendLine($"## {r.DisplayPath}");
            sb.AppendLine();
            if (!string.IsNullOrEmpty(r.Title) && r.Title != r.DisplayPath) sb.AppendLine($"**Title:** {r.Title}");
            if (r.Context != null) sb.AppendLine($"**Context:** {r.Context}");
            sb.AppendLine();
            if (r.Skipped)
            {
                sb.AppendLine($"> {r.SkipReason}");
            }
            else
            {
                sb.AppendLine("```");
                sb.AppendLine(r.Body);
                sb.AppendLine("```");
            }
            sb.AppendLine();
        }
        return sb.ToString();
    }

    public static string ToXml(List<MultiGetFile> results)
    {
        var sb = new StringBuilder();
        sb.AppendLine("<?xml version=\"1.0\" encoding=\"UTF-8\"?>");
        sb.AppendLine("<documents>");
        foreach (var r in results)
        {
            sb.AppendLine("  <document>");
            sb.AppendLine($"    <file>{FormatHelpers.EscapeXml(r.DisplayPath)}</file>");
            sb.AppendLine($"    <title>{FormatHelpers.EscapeXml(r.Title)}</title>");
            if (r.Context != null) sb.AppendLine($"    <context>{FormatHelpers.EscapeXml(r.Context)}</context>");
            if (r.Skipped)
            {
                sb.AppendLine("    <skipped>true</skipped>");
                sb.AppendLine($"    <reason>{FormatHelpers.EscapeXml(r.SkipReason ?? "")}</reason>");
            }
            else
            {
                sb.AppendLine($"    <body>{FormatHelpers.EscapeXml(r.Body)}</body>");
            }
            sb.AppendLine("  </document>");
        }
        sb.AppendLine("</documents>");
        return sb.ToString();
    }
}

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
