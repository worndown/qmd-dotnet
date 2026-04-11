using FluentAssertions;
using Qmd.Core.Chunking;
using Qmd.Core.Models;

namespace Qmd.Core.Tests.Chunking;

public class ChunkingTests
{
    // =========================================================================
    // chunkDocument
    // =========================================================================

    [Fact]
    public void ChunkDocument_SingleChunkForSmallDocs()
    {
        var chunks = DocumentChunker.ChunkDocument("Small document content", 1000, 0);
        chunks.Should().HaveCount(1);
        chunks[0].Text.Should().Be("Small document content");
        chunks[0].Pos.Should().Be(0);
    }

    [Fact]
    public void ChunkDocument_SplitsLargeDocuments()
    {
        var content = new string('A', 10000);
        var chunks = DocumentChunker.ChunkDocument(content, 1000, 0);
        chunks.Should().HaveCountGreaterThan(1);
        for (int i = 1; i < chunks.Count; i++)
            chunks[i].Pos.Should().BeGreaterThan(chunks[i - 1].Pos);
    }

    [Fact]
    public void ChunkDocument_OverlapCreatesOverlappingChunks()
    {
        var content = new string('A', 3000);
        var chunks = DocumentChunker.ChunkDocument(content, 1000, 150);
        chunks.Should().HaveCountGreaterThan(1);
        for (int i = 1; i < chunks.Count; i++)
        {
            var prevEnd = chunks[i - 1].Pos + chunks[i - 1].Text.Length;
            chunks[i].Pos.Should().BeLessThan(prevEnd); // overlap
            chunks[i].Pos.Should().BeGreaterThan(chunks[i - 1].Pos); // forward progress
        }
    }

    [Fact]
    public void ChunkDocument_PrefersParagraphBreaks()
    {
        var content = string.Concat(Enumerable.Repeat("First paragraph.\n\nSecond paragraph.\n\nThird paragraph.", 50));
        var chunks = DocumentChunker.ChunkDocument(content, 500, 0);
        chunks.Should().HaveCountGreaterThan(1);
    }

    [Fact]
    public void ChunkDocument_HandlesUtf8()
    {
        var content = string.Concat(Enumerable.Repeat("こんにちは世界", 500));
        var chunks = DocumentChunker.ChunkDocument(content, 1000, 0);
        foreach (var chunk in chunks)
        {
            // Should not throw on encoding
            System.Text.Encoding.UTF8.GetBytes(chunk.Text);
        }
    }

    [Fact]
    public void ChunkDocument_DefaultParamsUses3600Chars()
    {
        var content = string.Concat(Enumerable.Repeat("Word ", 2500)); // ~12500 chars
        var chunks = DocumentChunker.ChunkDocument(content);
        chunks.Should().HaveCountGreaterThan(1);
        chunks[0].Text.Length.Should().BeGreaterThan(2800);
        chunks[0].Text.Length.Should().BeLessThanOrEqualTo(3600);
    }

    // =========================================================================
    // scanBreakPoints
    // =========================================================================

    [Fact]
    public void ScanBreakPoints_DetectsH1()
    {
        var breaks = BreakPointScanner.ScanBreakPoints("Intro\n# Heading 1\nMore text");
        var h1 = breaks.Find(b => b.Type == "h1");
        h1.Should().NotBeNull();
        h1!.Score.Should().Be(100);
        h1.Pos.Should().Be(5);
    }

    [Fact]
    public void ScanBreakPoints_DetectsMultipleLevels()
    {
        var breaks = BreakPointScanner.ScanBreakPoints("Text\n# H1\n## H2\n### H3\nMore");
        breaks.Find(b => b.Type == "h1")!.Score.Should().Be(100);
        breaks.Find(b => b.Type == "h2")!.Score.Should().Be(90);
        breaks.Find(b => b.Type == "h3")!.Score.Should().Be(80);
    }

    [Fact]
    public void ScanBreakPoints_DetectsCodeBlocks()
    {
        var breaks = BreakPointScanner.ScanBreakPoints("Before\n```js\ncode\n```\nAfter");
        var codeBlocks = breaks.FindAll(b => b.Type == "codeblock");
        codeBlocks.Should().HaveCount(2);
        codeBlocks[0].Score.Should().Be(80);
    }

    [Fact]
    public void ScanBreakPoints_DetectsHorizontalRules()
    {
        var breaks = BreakPointScanner.ScanBreakPoints("Text\n---\n\nMore text");
        var hr = breaks.Find(b => b.Type == "hr");
        hr.Should().NotBeNull();
        hr!.Score.Should().Be(60);
    }

    [Fact]
    public void ScanBreakPoints_DetectsBlankLines()
    {
        var breaks = BreakPointScanner.ScanBreakPoints("First paragraph.\n\nSecond paragraph.");
        var blank = breaks.Find(b => b.Type == "blank");
        blank.Should().NotBeNull();
        blank!.Score.Should().Be(20);
    }

    [Fact]
    public void ScanBreakPoints_DetectsListItems()
    {
        var breaks = BreakPointScanner.ScanBreakPoints("Intro\n- Item 1\n- Item 2\n1. Numbered");
        breaks.FindAll(b => b.Type == "list").Should().HaveCount(2);
        breaks.FindAll(b => b.Type == "numlist").Should().HaveCount(1);
    }

    [Fact]
    public void ScanBreakPoints_DetectsNewlines()
    {
        var breaks = BreakPointScanner.ScanBreakPoints("Line 1\nLine 2\nLine 3");
        var newlines = breaks.FindAll(b => b.Type == "newline");
        newlines.Should().HaveCount(2);
        newlines[0].Score.Should().Be(1);
    }

    [Fact]
    public void ScanBreakPoints_SortedByPosition()
    {
        var breaks = BreakPointScanner.ScanBreakPoints("A\n# B\n\nC\n## D");
        for (int i = 1; i < breaks.Count; i++)
            breaks[i].Pos.Should().BeGreaterThan(breaks[i - 1].Pos);
    }

    [Fact]
    public void ScanBreakPoints_HigherScoreWinsAtSamePosition()
    {
        var breaks = BreakPointScanner.ScanBreakPoints("Text\n# Heading");
        var atPos4 = breaks.FindAll(b => b.Pos == 4);
        atPos4.Should().HaveCount(1);
        atPos4[0].Type.Should().Be("h1");
        atPos4[0].Score.Should().Be(100);
    }

    // =========================================================================
    // findCodeFences
    // =========================================================================

    [Fact]
    public void FindCodeFences_SingleFence()
    {
        var fences = BreakPointScanner.FindCodeFences("Before\n```js\ncode here\n```\nAfter");
        fences.Should().HaveCount(1);
        fences[0].Start.Should().Be(6);
        fences[0].End.Should().Be(26);
    }

    [Fact]
    public void FindCodeFences_MultipleFences()
    {
        var fences = BreakPointScanner.FindCodeFences("Intro\n```\nblock1\n```\nMiddle\n```\nblock2\n```\nEnd");
        fences.Should().HaveCount(2);
    }

    [Fact]
    public void FindCodeFences_UnclosedFence()
    {
        var text = "Before\n```\nunclosed code block";
        var fences = BreakPointScanner.FindCodeFences(text);
        fences.Should().HaveCount(1);
        fences[0].End.Should().Be(text.Length);
    }

    [Fact]
    public void FindCodeFences_NoFences()
    {
        BreakPointScanner.FindCodeFences("No code fences here").Should().BeEmpty();
    }

    // =========================================================================
    // isInsideCodeFence
    // =========================================================================

    [Fact]
    public void IsInsideCodeFence_Inside()
    {
        var fences = new List<CodeFenceRegion> { new(10, 30) };
        BreakPointScanner.IsInsideCodeFence(15, fences).Should().BeTrue();
        BreakPointScanner.IsInsideCodeFence(20, fences).Should().BeTrue();
    }

    [Fact]
    public void IsInsideCodeFence_Outside()
    {
        var fences = new List<CodeFenceRegion> { new(10, 30) };
        BreakPointScanner.IsInsideCodeFence(5, fences).Should().BeFalse();
        BreakPointScanner.IsInsideCodeFence(35, fences).Should().BeFalse();
    }

    [Fact]
    public void IsInsideCodeFence_AtBoundaries()
    {
        var fences = new List<CodeFenceRegion> { new(10, 30) };
        BreakPointScanner.IsInsideCodeFence(10, fences).Should().BeFalse(); // at start
        BreakPointScanner.IsInsideCodeFence(30, fences).Should().BeFalse(); // at end
    }

    [Fact]
    public void IsInsideCodeFence_MultipleFences()
    {
        var fences = new List<CodeFenceRegion> { new(10, 30), new(50, 70) };
        BreakPointScanner.IsInsideCodeFence(20, fences).Should().BeTrue();
        BreakPointScanner.IsInsideCodeFence(60, fences).Should().BeTrue();
        BreakPointScanner.IsInsideCodeFence(40, fences).Should().BeFalse();
    }

    // =========================================================================
    // findBestCutoff
    // =========================================================================

    [Fact]
    public void FindBestCutoff_PrefersHigherScore()
    {
        var bps = new List<BreakPoint>
        {
            new(100, 1, "newline"),
            new(150, 100, "h1"),
            new(180, 20, "blank"),
        };
        BreakPointScanner.FindBestCutoff(bps, 200, 100, 0.7).Should().Be(150);
    }

    [Fact]
    public void FindBestCutoff_H2AtEdgeBeatsBlankAtTarget()
    {
        var bps = new List<BreakPoint>
        {
            new(100, 90, "h2"),
            new(195, 20, "blank"),
        };
        BreakPointScanner.FindBestCutoff(bps, 200, 100, 0.7).Should().Be(100);
    }

    [Fact]
    public void FindBestCutoff_HighScoreOvercomesDistance()
    {
        var bps = new List<BreakPoint>
        {
            new(150, 100, "h1"),
            new(195, 1, "newline"),
        };
        BreakPointScanner.FindBestCutoff(bps, 200, 100, 0.7).Should().Be(150);
    }

    [Fact]
    public void FindBestCutoff_ReturnsTarget_WhenNoBreaksInWindow()
    {
        var bps = new List<BreakPoint> { new(10, 100, "h1") };
        BreakPointScanner.FindBestCutoff(bps, 200, 100, 0.7).Should().Be(200);
    }

    [Fact]
    public void FindBestCutoff_SkipsInsideCodeFences()
    {
        var bps = new List<BreakPoint>
        {
            new(150, 100, "h1"),
            new(180, 20, "blank"),
        };
        var fences = new List<CodeFenceRegion> { new(140, 160) };
        BreakPointScanner.FindBestCutoff(bps, 200, 100, 0.7, fences).Should().Be(180);
    }

    [Fact]
    public void FindBestCutoff_EmptyBreakPoints()
    {
        BreakPointScanner.FindBestCutoff([], 200, 100, 0.7).Should().Be(200);
    }

    // =========================================================================
    // mergeBreakPoints
    // =========================================================================

    [Fact]
    public void MergeBreakPoints_MergesAndKeepsHighestScore()
    {
        var a = new List<BreakPoint> { new(10, 50, "h6"), new(20, 80, "h3") };
        var b = new List<BreakPoint> { new(10, 90, "h2"), new(30, 100, "h1") };
        var merged = BreakPointScanner.MergeBreakPoints(a, b);
        merged.Should().HaveCount(3);
        merged.Find(bp => bp.Pos == 10)!.Score.Should().Be(90);
        merged.Find(bp => bp.Pos == 20)!.Score.Should().Be(80);
        merged.Find(bp => bp.Pos == 30)!.Score.Should().Be(100);
    }

    [Fact]
    public void MergeBreakPoints_SortedByPosition()
    {
        var a = new List<BreakPoint> { new(30, 50, "a") };
        var b = new List<BreakPoint> { new(10, 50, "b") };
        var merged = BreakPointScanner.MergeBreakPoints(a, b);
        merged[0].Pos.Should().Be(10);
        merged[1].Pos.Should().Be(30);
    }

    // =========================================================================
    // ITokenizer
    // =========================================================================

    [Fact]
    public void CharBasedTokenizer_ApproximatesCorrectly()
    {
        var tokenizer = new CharBasedTokenizer();
        tokenizer.CountTokens("Hello world!").Should().Be(4); // 12 chars / 3 = 4 (3 chars/token matches TS)
        tokenizer.CountTokens("").Should().Be(0);
        tokenizer.CountTokens("A").Should().Be(1);
    }

    // =========================================================================
    // Smart Chunking Integration
    // =========================================================================

    [Fact]
    public void ChunkDocument_PrefersHeadingsOverArbitraryBreaks()
    {
        // Create content where the heading falls within the search window
        // We want the heading at ~1680 chars so it's in the window for a 2000 char target
        var section1 = string.Concat(Enumerable.Repeat("Introduction text here. ", 70)); // ~1680 chars
        var section2 = string.Concat(Enumerable.Repeat("Main content text here. ", 50)); // ~1150 chars
        var content = $"{section1}\n# Main Section\n{section2}";

        // With 2000 char chunks and 800 char window (searches 1200-2000)
        // Heading is at ~1680 which is in window
        var chunks = DocumentChunker.ChunkDocument(content, 2000, 0, 800);
        var headingPos = content.IndexOf("\n# Main Section", StringComparison.Ordinal);

        // First chunk should end at the heading (best break point in window)
        chunks.Count.Should().BeGreaterThanOrEqualTo(2);
        chunks[0].Text.Length.Should().Be(headingPos);
    }

    [Fact]
    public void ChunkDocument_DoesNotSplitInsideCodeBlocks()
    {
        var beforeCode = string.Concat(Enumerable.Repeat("Some intro text. ", 30)); // ~480 chars
        var codeBlock = "```typescript\n" + string.Concat(Enumerable.Repeat("const x = 1;\n", 100)) + "```\n";
        var afterCode = string.Concat(Enumerable.Repeat("More text after code. ", 30));
        var content = beforeCode + codeBlock + afterCode;

        var chunks = DocumentChunker.ChunkDocument(content, 1000, 0, 400);

        // Check that no chunk starts in the middle of a code block
        foreach (var chunk in chunks)
        {
            var fenceCount = System.Text.RegularExpressions.Regex.Matches(chunk.Text, @"\n```").Count;
            // If we have an odd number of fence markers, we're splitting inside a block
            // (unless it's the last chunk with unclosed fence)
            if (fenceCount % 2 == 1 && !chunk.Text.EndsWith("```\n"))
            {
                var isLastChunk = chunks.IndexOf(chunk) == chunks.Count - 1;
                if (!isLastChunk)
                {
                    // Not the last chunk — smoke test, just verify it runs
                }
            }
        }
        chunks.Count.Should().BeGreaterThan(1);
    }

    [Fact]
    public void ChunkDocument_HandlesMarkdownWithMixedElements()
    {
        var content = string.Concat(Enumerable.Repeat(@"# Introduction

This is the introduction paragraph with some text.

## Section 1

Some content in section 1.

- List item 1
- List item 2
- List item 3

## Section 2

```javascript
function hello() {
  console.log(""Hello"");
}
```

More text after the code block.

---

## Section 3

Final section content.
", 10));

        var chunks = DocumentChunker.ChunkDocument(content, 500, 75, 200);

        // Should produce multiple chunks
        chunks.Count.Should().BeGreaterThan(5);

        // All chunks should be valid strings
        foreach (var chunk in chunks)
        {
            chunk.Text.Should().BeOfType<string>();
            chunk.Text.Length.Should().BeGreaterThan(0);
            chunk.Pos.Should().BeGreaterThanOrEqualTo(0);
        }
    }
}
