using Attest.Core;
using Microsoft.Build.Locator;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.FindSymbols;
using Microsoft.CodeAnalysis.MSBuild;

namespace Attest.NET;

/// <summary>
/// Finds the methods a diff changed and, via Roslyn, their direct callers across every project
/// in the repository, not just the same one. A method's own file counts as in scope whether or
/// not any other project calls it.
/// </summary>
public sealed class DiffScope : IDiffScope
{
    // MSBuildLocator.RegisterDefaults() may run exactly once per process; a static field
    // initializer is the CLR's own thread-safe "run once" primitive.
    private static readonly bool MsBuildRegistered = RegisterMsBuildOnce();

    /// <inheritdoc/>
    public async Task<DiffScopeResult> ComputeScopeAsync(string repositoryRoot, string baseRef, CancellationToken cancellationToken)
    {
        _ = MsBuildRegistered;

        // Resolved once, reused for both the diff and project discovery: repositoryRoot is
        // explicitly allowed to be any directory inside the repository, not just its root, and
        // the two steps must agree on the same actual root or a subdirectory call silently
        // misses caller projects that live outside it.
        var realRoot = await GitDiffParser.ResolveRepositoryRootAsync(repositoryRoot, cancellationToken).ConfigureAwait(false);

        var changedLineRanges = await GitDiffParser.GetChangedLineRangesAsync(realRoot, baseRef, cancellationToken).ConfigureAwait(false);
        if (changedLineRanges.Count == 0)
            return new DiffScopeResult([], []);

        var projectPaths = DiscoverProjectPaths(realRoot);
        if (projectPaths.Count == 0)
            throw new AttestDiffScopeFailedException(realRoot, "No .csproj files found under the repository root.");

        using var workspace = MSBuildWorkspace.Create();

        foreach (var projectPath in projectPaths)
        {
            // OpenProjectAsync pulls in every project reachable via ProjectReference, so a
            // project discovered later in this loop may already be part of the workspace.
            if (workspace.CurrentSolution.Projects.Any(p => string.Equals(p.FilePath, projectPath, StringComparison.OrdinalIgnoreCase)))
                continue;

            try
            {
                await workspace.OpenProjectAsync(projectPath, cancellationToken: cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is InvalidOperationException or IOException)
            {
                throw new AttestDiffScopeFailedException(repositoryRoot, $"Could not load project '{projectPath}': {ex.Message}");
            }
        }

        var failures = workspace.Diagnostics.Where(d => d.Kind == WorkspaceDiagnosticKind.Failure).ToList();
        if (failures.Count > 0)
            throw new AttestDiffScopeFailedException(
                repositoryRoot,
                $"{failures.Count} project(s) failed to load: {string.Join("; ", failures.Select(f => f.Message))}");

        var solution = workspace.CurrentSolution;

        var changedMethods = await FindChangedMethodsAsync(solution, changedLineRanges, cancellationToken).ConfigureAwait(false);
        var callerMethods = await FindCallerMethodsAsync(solution, changedMethods, cancellationToken).ConfigureAwait(false);

        return new DiffScopeResult(changedMethods.Select(m => m.Method).ToList(), callerMethods);
    }

    private static bool RegisterMsBuildOnce()
    {
        // Without this, MSBuild keeps worker nodes alive after OpenProjectAsync returns for
        // faster subsequent builds; those nodes can hold a file open under the scanned
        // repository (observed: a lingering handle inside .git/objects) well past the point
        // ComputeScopeAsync returns, which breaks anything that tries to move or delete the
        // repository right after.
        Environment.SetEnvironmentVariable("MSBUILDDISABLENODEREUSE", "1");

        if (!MSBuildLocator.IsRegistered)
            MSBuildLocator.RegisterDefaults();
        return true;
    }

    private static List<string> DiscoverProjectPaths(string repositoryRoot) =>
        Directory.EnumerateFiles(repositoryRoot, "*.csproj", SearchOption.AllDirectories)
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase)
                && !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToList();

    private static async Task<List<(ChangedMethod Method, IMethodSymbol Symbol)>> FindChangedMethodsAsync(
        Solution solution,
        IReadOnlyDictionary<string, IReadOnlyList<(int Start, int End)>> changedLineRanges,
        CancellationToken cancellationToken)
    {
        var found = new List<(ChangedMethod Method, IMethodSymbol Symbol)>();
        var seenSymbols = new HashSet<IMethodSymbol>(SymbolEqualityComparer.Default);

        foreach (var (filePath, ranges) in changedLineRanges)
        {
            var document = solution.Projects
                .SelectMany(project => project.Documents)
                .FirstOrDefault(doc => string.Equals(doc.FilePath, filePath, StringComparison.OrdinalIgnoreCase));

            if (document is null)
                continue;

            var syntaxTree = await document.GetSyntaxTreeAsync(cancellationToken).ConfigureAwait(false);
            var semanticModel = await document.GetSemanticModelAsync(cancellationToken).ConfigureAwait(false);
            if (syntaxTree is null || semanticModel is null)
                continue;

            var root = await syntaxTree.GetRootAsync(cancellationToken).ConfigureAwait(false);

            foreach (var methodNode in root.DescendantNodes().OfType<MethodDeclarationSyntax>())
            {
                var methodLineSpan = syntaxTree.GetLineSpan(methodNode.Span);
                var methodStart = methodLineSpan.StartLinePosition.Line + 1;
                var methodEnd = methodLineSpan.EndLinePosition.Line + 1;

                var touched = ranges.Any(range => range.Start <= methodEnd && range.End >= methodStart);
                if (!touched)
                    continue;

                if (semanticModel.GetDeclaredSymbol(methodNode, cancellationToken) is not { } symbol)
                    continue;

                if (!seenSymbols.Add(symbol))
                    continue;

                found.Add((
                    new ChangedMethod(filePath, symbol.ContainingType.ToDisplayString(), symbol.Name, methodStart, methodEnd),
                    symbol));
            }
        }

        return found;
    }

    private static async Task<List<ChangedMethod>> FindCallerMethodsAsync(
        Solution solution,
        List<(ChangedMethod Method, IMethodSymbol Symbol)> changedMethods,
        CancellationToken cancellationToken)
    {
        var changedSymbols = new HashSet<IMethodSymbol>(changedMethods.Select(m => m.Symbol), SymbolEqualityComparer.Default);
        var callers = new List<ChangedMethod>();
        var seen = new HashSet<(string FilePath, string ContainingType, string MethodName, int StartLine)>();

        foreach (var (_, symbol) in changedMethods)
        {
            var callReferences = await SymbolFinder.FindCallersAsync(symbol, solution, cancellationToken).ConfigureAwait(false);

            foreach (var callReference in callReferences)
            {
                if (callReference.CallingSymbol is not IMethodSymbol callingMethod)
                    continue;

                if (changedSymbols.Contains(callingMethod))
                    continue;

                if (callingMethod.DeclaringSyntaxReferences.FirstOrDefault() is not { } syntaxReference)
                    continue;

                var syntaxTree = syntaxReference.SyntaxTree;
                var lineSpan = syntaxTree.GetLineSpan(syntaxReference.Span);
                var startLine = lineSpan.StartLinePosition.Line + 1;
                var endLine = lineSpan.EndLinePosition.Line + 1;
                var filePath = syntaxTree.FilePath;

                var key = (filePath, callingMethod.ContainingType.ToDisplayString(), callingMethod.Name, startLine);
                if (!seen.Add(key))
                    continue;

                callers.Add(new ChangedMethod(filePath, callingMethod.ContainingType.ToDisplayString(), callingMethod.Name, startLine, endLine));
            }
        }

        return callers;
    }
}
