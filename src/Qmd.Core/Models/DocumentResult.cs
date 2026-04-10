namespace Qmd.Core.Models;

public class DocumentResult
{
    public string Filepath { get; set; } = "";
    public string DisplayPath { get; set; } = "";
    public string Title { get; set; } = "";
    public string? Context { get; set; }
    public string Hash { get; set; } = "";
    public string DocId { get; set; } = "";
    public string CollectionName { get; set; } = "";
    public string ModifiedAt { get; set; } = "";
    public int BodyLength { get; set; }
    public string? Body { get; set; }
}

public class SearchResult : DocumentResult
{
    public double Score { get; set; }
    public string Source { get; set; } = "fts";
    public int? ChunkPos { get; set; }
    public HybridQueryExplain? Explain { get; set; }
}

public class DocumentNotFound
{
    public string Error { get; } = "not_found";
    public string Query { get; set; } = "";
    public List<string> SimilarFiles { get; set; } = new();
}

public class FindDocumentResult
{
    public DocumentResult? Document { get; init; }
    public DocumentNotFound? NotFound { get; init; }
    public bool IsFound => Document != null;

    public static FindDocumentResult Found(DocumentResult doc) => new() { Document = doc };
    public static FindDocumentResult Missing(string query, List<string> similar) =>
        new() { NotFound = new DocumentNotFound { Query = query, SimilarFiles = similar } };
}

public class MultiGetResult
{
    public required DocumentResult Doc { get; set; }
    public bool Skipped { get; set; }
    public string? SkipReason { get; set; }
}
