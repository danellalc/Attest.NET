using Attest.NET;

namespace Attest.UnitTests;

public class GitDiffParserTests
{
    private static readonly string RepoRoot = Path.GetFullPath("/repo");

    [Fact]
    public void ParseUnifiedDiff_SingleLineChange_CountOmittedMeansOne()
    {
        var diff = """
            diff --git a/Foo.cs b/Foo.cs
            --- a/Foo.cs
            +++ b/Foo.cs
            @@ -8,0 +9 @@ public sealed class Foo
            +    private int _x;
            """;

        var result = GitDiffParser.ParseUnifiedDiff(diff, RepoRoot);

        var file = Path.Combine(RepoRoot, "Foo.cs");
        Assert.Equal([(9, 9)], result[file]);
    }

    [Fact]
    public void ParseUnifiedDiff_MultiLineInsert_UsesExplicitCount()
    {
        var diff = """
            diff --git a/Foo.cs b/Foo.cs
            --- a/Foo.cs
            +++ b/Foo.cs
            @@ -9,0 +11,2 @@ public sealed class Foo
            +    private int _y;
            +    private int _z;
            """;

        var result = GitDiffParser.ParseUnifiedDiff(diff, RepoRoot);

        var file = Path.Combine(RepoRoot, "Foo.cs");
        Assert.Equal([(11, 12)], result[file]);
    }

    [Fact]
    public void ParseUnifiedDiff_PureDeletion_TouchesTheBoundaryLine()
    {
        var diff = """
            diff --git a/Foo.cs b/Foo.cs
            --- a/Foo.cs
            +++ b/Foo.cs
            @@ -86,6 +91,0 @@ public sealed class Foo
            -    private int _dead;
            """;

        var result = GitDiffParser.ParseUnifiedDiff(diff, RepoRoot);

        var file = Path.Combine(RepoRoot, "Foo.cs");
        Assert.Equal([(91, 91)], result[file]);
    }

    [Fact]
    public void ParseUnifiedDiff_BothSidesCountOmitted_StillParses()
    {
        var diff = """
            diff --git a/Foo.cs b/Foo.cs
            --- a/Foo.cs
            +++ b/Foo.cs
            @@ -10 +15 @@ public sealed class Foo
            -    return a;
            +    return b;
            """;

        var result = GitDiffParser.ParseUnifiedDiff(diff, RepoRoot);

        var file = Path.Combine(RepoRoot, "Foo.cs");
        Assert.Equal([(15, 15)], result[file]);
    }

    [Fact]
    public void ParseUnifiedDiff_NewFile_StartsAtLineOne()
    {
        var diff = """
            diff --git a/New.cs b/New.cs
            --- /dev/null
            +++ b/New.cs
            @@ -0,0 +1,19 @@
            +namespace Foo;
            """;

        var result = GitDiffParser.ParseUnifiedDiff(diff, RepoRoot);

        var file = Path.Combine(RepoRoot, "New.cs");
        Assert.Equal([(1, 19)], result[file]);
    }

    [Fact]
    public void ParseUnifiedDiff_DeletedFile_ProducesNoEntry()
    {
        var diff = """
            diff --git a/Gone.cs b/Gone.cs
            --- a/Gone.cs
            +++ /dev/null
            @@ -1,5 +0,0 @@
            -namespace Foo;
            """;

        var result = GitDiffParser.ParseUnifiedDiff(diff, RepoRoot);

        Assert.Empty(result);
    }

    [Fact]
    public void ParseUnifiedDiff_MultipleHunksInOneFile_AccumulatesAllRanges()
    {
        var diff = """
            diff --git a/Foo.cs b/Foo.cs
            --- a/Foo.cs
            +++ b/Foo.cs
            @@ -8,0 +9 @@ public sealed class Foo
            +    private int _x;
            @@ -20,0 +22,3 @@ public sealed class Foo
            +    private int _y;
            +    private int _z;
            +    private int _w;
            """;

        var result = GitDiffParser.ParseUnifiedDiff(diff, RepoRoot);

        var file = Path.Combine(RepoRoot, "Foo.cs");
        Assert.Equal([(9, 9), (22, 24)], result[file]);
    }

    [Fact]
    public void ParseUnifiedDiff_MultipleFiles_KeyedSeparately()
    {
        var diff = """
            diff --git a/Foo.cs b/Foo.cs
            --- a/Foo.cs
            +++ b/Foo.cs
            @@ -8,0 +9 @@ public sealed class Foo
            +    private int _x;
            diff --git a/Bar.cs b/Bar.cs
            --- a/Bar.cs
            +++ b/Bar.cs
            @@ -1,0 +2 @@ public sealed class Bar
            +    private int _y;
            """;

        var result = GitDiffParser.ParseUnifiedDiff(diff, RepoRoot);

        Assert.Equal(2, result.Count);
        Assert.Equal([(9, 9)], result[Path.Combine(RepoRoot, "Foo.cs")]);
        Assert.Equal([(2, 2)], result[Path.Combine(RepoRoot, "Bar.cs")]);
    }

    [Fact]
    public void ParseUnifiedDiff_EmptyDiff_ProducesNoEntries()
    {
        var result = GitDiffParser.ParseUnifiedDiff("", RepoRoot);

        Assert.Empty(result);
    }

    [Fact]
    public void ParseUnifiedDiff_NonAsciiFileName_ResolvesToTheRealPath()
    {
        // The exact bytes git itself produces (verified directly: `git diff` on a real
        // repository with a file named café.cs), not a guessed format.
        var diff = """
            diff --git "a/caf\303\251.cs" "b/caf\303\251.cs"
            --- /dev/null
            +++ "b/caf\303\251.cs"
            @@ -0,0 +1 @@
            +public class Foo {}
            """;

        var result = GitDiffParser.ParseUnifiedDiff(diff, RepoRoot);

        var file = Path.Combine(RepoRoot, "café.cs");
        Assert.Equal([(1, 1)], result[file]);
    }

    [Theory]
    [InlineData("plain.cs", "plain.cs")]
    [InlineData("\"caf\\303\\251.cs\"", "café.cs")]
    [InlineData("\"quote\\\".cs\"", "quote\".cs")]
    [InlineData("\"back\\\\slash.cs\"", "back\\slash.cs")]
    [InlineData("\"tab\\t.cs\"", "tab\t.cs")]
    public void UnquoteGitPath_DecodesGitsCStyleQuoting(string raw, string expected)
    {
        Assert.Equal(expected, GitDiffParser.UnquoteGitPath(raw));
    }
}
