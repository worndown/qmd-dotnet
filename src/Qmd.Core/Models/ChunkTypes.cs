namespace Qmd.Core.Models;

internal record BreakPoint(int Pos, double Score, string Type);

internal record CodeFenceRegion(int Start, int End);

internal record TextChunk(string Text, int Pos);

internal record TokenizedChunk(string Text, int Pos, int Tokens);

public enum ChunkStrategy { Regex, Auto }
