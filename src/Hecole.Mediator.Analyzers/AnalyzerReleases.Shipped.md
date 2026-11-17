; Shipped analyzer releases
; https://github.com/dotnet/roslyn-analyzers/blob/main/src/Microsoft.CodeAnalysis.Analyzers/ReleaseTrackingAnalyzers.Help.md

## Release 2.0.0

### New Rules

Rule ID | Category | Severity | Notes
--------|----------|----------|-------
HM0001 | Hecole.Mediator.Usage | Info | RequestHandlerAnalyzer, request without a handler in the compilation
HM0002 | Hecole.Mediator.Usage | Info | RequestHandlerAnalyzer, more than one handler for the same request
HM1001 | Hecole.Mediator.Restriction | Disabled | RestrictedUsageAnalyzer, assembly scanning
HM1002 | Hecole.Mediator.Restriction | Disabled | RestrictedUsageAnalyzer, Singleton mediator lifetime
HM1003 | Hecole.Mediator.Restriction | Disabled | RestrictedUsageAnalyzer, pipeline behavior registration
HM1004 | Hecole.Mediator.Restriction | Disabled | RestrictedUsageAnalyzer, TaskWhenAllPublisher
HM1005 | Hecole.Mediator.Restriction | Disabled | ExplicitRegistrationAnalyzer, request handler without AddRequestHandler
