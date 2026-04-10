namespace Qmd.Core.Models;

public record BreakPoint(int Pos, double Score, string Type);
public record CodeFenceRegion(int Start, int End);
public record TextChunk(string Text, int Pos);
public record TokenizedChunk(string Text, int Pos, int Tokens);

public enum ChunkStrategy { Regex, Auto }
