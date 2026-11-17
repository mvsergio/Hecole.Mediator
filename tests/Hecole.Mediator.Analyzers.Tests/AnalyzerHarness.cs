using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Hecole.Mediator.Analyzers.Tests;

/// <summary>
/// Compiles sample code in memory and runs the Hecole.Mediator analyzers on it, the way the compiler does in a build:
/// default severities unless <c>options</c> overrides them (same mechanism as .editorconfig
/// <c>dotnet_diagnostic.ID.severity</c>), concurrent execution, analyzer exceptions collected and failing the test.
/// </summary>
internal static class AnalyzerHarness
{
    private const string Usings = """
        global using System;
        global using System.Collections.Generic;
        global using System.Threading;
        global using System.Threading.Tasks;
        global using Hecole.Mediator;
        global using Microsoft.Extensions.DependencyInjection;
        """;

    private static readonly ImmutableArray<MetadataReference> PlatformReferences = LoadPlatformReferences();

    private static readonly MetadataReference MediatorReference =
        MetadataReference.CreateFromFile(typeof(IRequest).Assembly.Location);

    public static ImmutableArray<DiagnosticAnalyzer> AllAnalyzers { get; } =
    [
        new RequestHandlerAnalyzer(),
        new ExplicitRegistrationAnalyzer(),
        new RestrictedUsageAnalyzer(),
    ];

    /// <summary>Compiles <paramref name="sources"/> as one assembly; fails the test on any compiler error.</summary>
    public static CSharpCompilation Compile(
        string assemblyName,
        IEnumerable<string> sources,
        bool referenceMediator = true,
        IEnumerable<MetadataReference>? extraReferences = null,
        IReadOnlyDictionary<string, ReportDiagnostic>? options = null)
    {
        var trees = sources
            .Select(source => CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Latest)))
            .ToList();
        if (referenceMediator)
        {
            trees.Add(CSharpSyntaxTree.ParseText(Usings, new CSharpParseOptions(LanguageVersion.Latest)));
        }

        var references = PlatformReferences.AddRange(extraReferences ?? []);
        if (referenceMediator)
        {
            references = references.Add(MediatorReference);
        }

        var compilationOptions = new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable);
        if (options is not null)
        {
            compilationOptions = compilationOptions.WithSpecificDiagnosticOptions(options);
        }

        var compilation = CSharpCompilation.Create(assemblyName, trees, references, compilationOptions);

        var errors = compilation.GetDiagnostics().Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error).ToList();
        Assert.True(errors.Count == 0, "Sample code does not compile:\n" + string.Join("\n", errors));
        return compilation;
    }

    /// <summary>Runs every analyzer on the compilation and returns the reported diagnostics, sorted by position.</summary>
    public static async Task<IReadOnlyList<Diagnostic>> AnalyzeAsync(Compilation compilation)
    {
        var exceptions = new List<string>();
        var analyzerOptions = new CompilationWithAnalyzersOptions(
            new AnalyzerOptions([]),
            onAnalyzerException: (exception, analyzer, _) => { lock (exceptions) { exceptions.Add($"{analyzer}: {exception}"); } },
            concurrentAnalysis: true,
            logAnalyzerExecutionTime: false,
            reportSuppressedDiagnostics: false);

        var diagnostics = await compilation
            .WithAnalyzers(AllAnalyzers, analyzerOptions)
            .GetAnalyzerDiagnosticsAsync(CancellationToken.None);

        Assert.True(exceptions.Count == 0, "Analyzer threw:\n" + string.Join("\n", exceptions));
        Assert.DoesNotContain(diagnostics, diagnostic => diagnostic.Id.StartsWith("AD", StringComparison.Ordinal));

        return diagnostics
            .OrderBy(diagnostic => diagnostic.Location.SourceTree?.FilePath, StringComparer.Ordinal)
            .ThenBy(diagnostic => diagnostic.Location.SourceSpan.Start)
            .ThenBy(diagnostic => diagnostic.Id, StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>Compiles one assembly from <paramref name="sources"/> and analyzes it.</summary>
    public static Task<IReadOnlyList<Diagnostic>> AnalyzeAsync(params string[] sources)
        => AnalyzeAsync(Compile("Sample", sources));

    /// <summary>Same as <see cref="AnalyzeAsync(string[])"/>, with the given rules turned on as errors.</summary>
    public static Task<IReadOnlyList<Diagnostic>> AnalyzeWithAsync(string[] enabledIds, params string[] sources)
        => AnalyzeAsync(Compile("Sample", sources, options: enabledIds.ToDictionary(id => id, _ => ReportDiagnostic.Error)));

    /// <summary>Diagnostics with the given id.</summary>
    public static List<Diagnostic> WithId(this IEnumerable<Diagnostic> diagnostics, string id)
        => diagnostics.Where(diagnostic => diagnostic.Id == id).ToList();

    /// <summary>Source text the diagnostic points at.</summary>
    public static string Text(this Diagnostic diagnostic)
        => diagnostic.Location.SourceTree!.GetText().ToString(diagnostic.Location.SourceSpan);

    private static ImmutableArray<MetadataReference> LoadPlatformReferences()
    {
        var mediatorFile = Path.GetFileName(typeof(IRequest).Assembly.Location);
        var paths = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(Path.PathSeparator);

        return
        [
            .. paths
                .Where(path => !string.Equals(Path.GetFileName(path), mediatorFile, StringComparison.OrdinalIgnoreCase))
                .Where(path => Path.GetFileName(path) is var name
                               && (name.StartsWith("System.", StringComparison.OrdinalIgnoreCase)
                                   || name.StartsWith("Microsoft.Extensions.", StringComparison.OrdinalIgnoreCase)
                                   || name is "mscorlib.dll" or "netstandard.dll"))
                .Select(path => (MetadataReference)MetadataReference.CreateFromFile(path)),
        ];
    }
}
