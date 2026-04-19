namespace Qmd.Core.Database;

// -- Document queries (DocumentFinder, QmdStore.ListFilesAsync) --

internal class DocumentRow
{
    public string VirtualPath { get; set; } = "";
    public string DisplayPath { get; set; } = "";
    public string Title { get; set; } = "";
    public string Hash { get; set; } = "";
    public string Collection { get; set; } = "";
    public string ModifiedAt { get; set; } = "";
    public int BodyLength { get; set; }
    public string? Body { get; set; }
}

internal class DocIdRow
{
    public string Filepath { get; set; } = "";
    public string Hash { get; set; } = "";
}

internal class BodyRow
{
    public string? Body { get; set; }
}

internal class ListFileRow
{
    public string Path { get; set; } = "";
    public int Size { get; set; }
}

// -- Collection queries (ContextResolver, ConfigSync, StatusOperations) --

internal class StoreCollectionRow
{
    public string Name { get; set; } = "";
    public string? Path { get; set; }
    public string? Context { get; set; }
}

internal class StatusCollectionRow
{
    public string Name { get; set; } = "";
    public string? Path { get; set; }
    public string? Pattern { get; set; }
    public int DocCount { get; set; }
    public string LastUpdated { get; set; } = "";
}

// -- Search queries (FtsSearcher, VectorSearcher) --

internal class FtsMatchRow
{
    public string Filepath { get; set; } = "";
    public string DisplayPath { get; set; } = "";
    public string Title { get; set; } = "";
    public string? Body { get; set; }
    public string Hash { get; set; } = "";
    public string Collection { get; set; } = "";
    public double Bm25Score { get; set; }
}

internal class VectorMatchRow
{
    public string HashSeq { get; set; } = "";
    public double Distance { get; set; }
}

internal class ContentVectorDocRow
{
    public string HashSeq { get; set; } = "";
    public string Hash { get; set; } = "";
    public int Pos { get; set; }
    public string Filepath { get; set; } = "";
    public string DisplayPath { get; set; } = "";
    public string Title { get; set; } = "";
    public string Collection { get; set; } = "";
    public string? Body { get; set; }
}

// -- Simple/utility rows --

internal class CountRow
{
    public int Cnt { get; set; }
}

internal class SingleValueRow
{
    public string? Value { get; set; }
}

internal class SingleNameRow
{
    public string Name { get; set; } = "";
}

internal class SinglePathRow
{
    public string Path { get; set; } = "";
}

internal class SqliteMasterRow
{
    public string Name { get; set; } = "";
}

// -- Embedding queries (EmbeddingOperations) --

internal class EmbeddingPendingRow
{
    public string Hash { get; set; } = "";
    public string Path { get; set; } = "";
    public long Bytes { get; set; }
}

internal class HashBodyRow
{
    public string Hash { get; set; } = "";
    public string? Body { get; set; }
}

internal class HashBodyPathRow
{
    public string Hash { get; set; } = "";
    public string? Body { get; set; }
    public string Path { get; set; } = "";
}

// -- Glob matching --

internal class GlobFileRow
{
    public string VirtualPath { get; set; } = "";
    public int BodyLength { get; set; }
    public string Path { get; set; } = "";
    public string Collection { get; set; } = "";
}

// -- Embedding profiler --

internal class ChunkSampleRow
{
    public string Hash { get; set; } = "";
    public int Seq { get; set; }
    public int Pos { get; set; }
    public string Collection { get; set; } = "";
    public string? ChunkText { get; set; }
}
