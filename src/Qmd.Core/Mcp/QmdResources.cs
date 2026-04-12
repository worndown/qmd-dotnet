using System.ComponentModel;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using Qmd.Core.Content;

namespace Qmd.Core.Mcp;

/// <summary>
/// MCP resources for QMD. Provides qmd:// URI access to documents.
/// </summary>
[McpServerResourceType]
internal class QmdResources
{
    private readonly IQmdStore _store;

    public QmdResources(IQmdStore store)
    {
        _store = store;
    }

    [McpServerResource(UriTemplate = "qmd://{+path}", Name = "QMD Document", MimeType = "text/markdown")]
    [Description("A markdown document from your QMD knowledge base. Use search tools to discover documents.")]
    public async Task<ResourceContents> ReadDocument(string path)
    {
        var decodedPath = Uri.UnescapeDataString(path);
        var result = await _store.GetAsync(decodedPath, new GetOptions { IncludeBody = true });

        if (!result.IsFound)
        {
            return new TextResourceContents
            {
                Uri = $"qmd://{path}",
                MimeType = "text/markdown",
                Text = $"Document not found: {decodedPath}",
            };
        }

        var doc = result.Document!;
        var text = TextUtils.AddLineNumbers(doc.Body ?? "");
        if (doc.Context != null)
            text = $"<!-- Context: {doc.Context} -->\n\n" + text;

        return new TextResourceContents
        {
            Uri = $"qmd://{path}",
            MimeType = "text/markdown",
            Text = text,
        };
    }
}
