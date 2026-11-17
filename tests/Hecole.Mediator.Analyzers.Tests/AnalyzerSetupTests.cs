using Microsoft.CodeAnalysis;
using static Hecole.Mediator.Analyzers.Tests.AnalyzerHarness;

namespace Hecole.Mediator.Analyzers.Tests;

/// <summary>Descriptors (stable ids, defaults, help links) and compilations that do not reference Hecole.Mediator.</summary>
public sealed class AnalyzerSetupTests
{
    [Fact]
    public void Descriptors_AreStable()
    {
        var descriptors = AllAnalyzers
            .SelectMany(analyzer => analyzer.SupportedDiagnostics)
            .OrderBy(descriptor => descriptor.Id, StringComparer.Ordinal)
            .Select(descriptor => $"{descriptor.Id} {descriptor.Category} {descriptor.DefaultSeverity} {(descriptor.IsEnabledByDefault ? "on" : "off")}");

        Assert.Equal(
            [
                "HM0001 Hecole.Mediator.Usage Info on",
                "HM0002 Hecole.Mediator.Usage Info on",
                "HM1001 Hecole.Mediator.Restriction Warning off",
                "HM1002 Hecole.Mediator.Restriction Warning off",
                "HM1003 Hecole.Mediator.Restriction Warning off",
                "HM1004 Hecole.Mediator.Restriction Warning off",
                "HM1005 Hecole.Mediator.Restriction Warning off",
            ],
            descriptors);
    }

    [Fact]
    public void Descriptors_HaveHelpLinkTitleAndDescription()
    {
        foreach (var descriptor in AllAnalyzers.SelectMany(analyzer => analyzer.SupportedDiagnostics))
        {
            Assert.Equal(
                "https://github.com/mvsergio/Hecole.Mediator/blob/master/docs/analyzers.md#" + descriptor.Id.ToLowerInvariant(),
                descriptor.HelpLinkUri);
            Assert.False(string.IsNullOrWhiteSpace(descriptor.Title.ToString()));
            Assert.False(string.IsNullOrWhiteSpace(descriptor.MessageFormat.ToString()));
            Assert.False(string.IsNullOrWhiteSpace(descriptor.Description.ToString()));
        }
    }

    [Fact]
    public void DocsPage_ListsEveryRule()
    {
        var docs = File.ReadAllText(Path.Combine(FindRepositoryRoot(), "docs", "analyzers.md"));

        foreach (var descriptor in AllAnalyzers.SelectMany(analyzer => analyzer.SupportedDiagnostics))
        {
            Assert.Contains("### " + descriptor.Id, docs, StringComparison.Ordinal);
            Assert.Contains("dotnet_diagnostic." + descriptor.Id + ".severity", docs, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task CompilationWithoutHecoleMediator_ReportsNothing_EvenWithEveryRuleOn()
    {
        // Lookalike types with the same simple names, in another namespace.
        var compilation = Compile(
            "NoMediator",
            ["""
            using System;
            using System.Threading;
            using System.Threading.Tasks;
            namespace Other;
            public interface IRequest;
            public interface IRequest<T>;
            public interface IRequestHandler<TRequest, TResponse> { Task<TResponse> Handle(TRequest request, CancellationToken cancellationToken); }
            public sealed record Orphan : IRequest<int>;
            public sealed record Query : IRequest<int>;
            public sealed class A : IRequestHandler<Query, int> { public Task<int> Handle(Query r, CancellationToken c) => Task.FromResult(1); }
            public sealed class B : IRequestHandler<Query, int> { public Task<int> Handle(Query r, CancellationToken c) => Task.FromResult(2); }
            public sealed class TaskWhenAllPublisher;
            public static class Use { public static object Make() => new TaskWhenAllPublisher(); }
            """],
            referenceMediator: false,
            options: new[] { "HM0001", "HM0002", "HM1001", "HM1002", "HM1003", "HM1004", "HM1005" }
                .ToDictionary(id => id, _ => ReportDiagnostic.Error));

        Assert.DoesNotContain(compilation.References, reference => reference.Display?.EndsWith("Hecole.Mediator.dll", StringComparison.OrdinalIgnoreCase) == true);
        Assert.Empty(await AnalyzeAsync(compilation));
    }

    private static string FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Hecole.Mediator.slnx")))
            {
                return directory.FullName;
            }
        }

        throw new InvalidOperationException("Repository root not found.");
    }
}
