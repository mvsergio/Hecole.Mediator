using System.Reflection;
using System.Text;
using Hecole.Mediator;

namespace Hecole.Mediator.Tests;

/// <summary>
/// Locks the public surface of the <c>Hecole.Mediator</c> namespace (contracts and pipeline).
/// Any change here is a public API change.
/// </summary>
public class PublicApiSnapshotTests
{
    private const string Expected = """
        sealed class Hecole.Mediator.HecoleMediatorConfiguration
          method Hecole.Mediator.HecoleMediatorConfiguration AddBehavior(System.Type implementationType, Microsoft.Extensions.DependencyInjection.ServiceLifetime serviceLifetime = Transient)
          method Hecole.Mediator.HecoleMediatorConfiguration AddBehavior(System.Type serviceType, System.Type implementationType, Microsoft.Extensions.DependencyInjection.ServiceLifetime serviceLifetime = Transient)
          method Hecole.Mediator.HecoleMediatorConfiguration AddBehavior<TImplementationType>(Microsoft.Extensions.DependencyInjection.ServiceLifetime serviceLifetime = Transient)
          method Hecole.Mediator.HecoleMediatorConfiguration AddBehavior<TServiceType, TImplementationType>(Microsoft.Extensions.DependencyInjection.ServiceLifetime serviceLifetime = Transient)
          method Hecole.Mediator.HecoleMediatorConfiguration AddOpenBehavior(System.Type openBehaviorType, Microsoft.Extensions.DependencyInjection.ServiceLifetime serviceLifetime = Transient)
          method Hecole.Mediator.HecoleMediatorConfiguration AddOpenBehaviors(System.Collections.Generic.IEnumerable<System.Type> openBehaviorTypes, Microsoft.Extensions.DependencyInjection.ServiceLifetime serviceLifetime = Transient)
          method Hecole.Mediator.HecoleMediatorConfiguration RegisterServicesFromAssemblies(params System.Reflection.Assembly[] assemblies)
          method Hecole.Mediator.HecoleMediatorConfiguration RegisterServicesFromAssembly(System.Reflection.Assembly assembly)
          method Hecole.Mediator.HecoleMediatorConfiguration RegisterServicesFromAssemblyContaining(System.Type type)
          method Hecole.Mediator.HecoleMediatorConfiguration RegisterServicesFromAssemblyContaining<T>()
          property Hecole.Mediator.INotificationPublisher NotificationPublisher { get; set; }
          property Microsoft.Extensions.DependencyInjection.ServiceLifetime Lifetime { get; set; }
          property System.Type? NotificationPublisherType { get; set; }
          property bool RequireScope { get; set; }
        interface Hecole.Mediator.IBaseRequest
        interface Hecole.Mediator.IMediator : Hecole.Mediator.IPublisher, Hecole.Mediator.ISender
        interface Hecole.Mediator.INotification
        interface Hecole.Mediator.INotificationHandler<in TNotification> where TNotification : Hecole.Mediator.INotification
          method System.Threading.Tasks.Task Handle(TNotification notification, System.Threading.CancellationToken cancellationToken)
        interface Hecole.Mediator.INotificationPublisher
          method System.Threading.Tasks.Task Publish(System.Collections.Generic.IEnumerable<Hecole.Mediator.NotificationHandlerExecutor> handlerExecutors, Hecole.Mediator.INotification notification, System.Threading.CancellationToken cancellationToken)
        interface Hecole.Mediator.IPipelineBehavior<in TRequest, TResponse> where TRequest : notnull
          method System.Threading.Tasks.Task<TResponse> Handle(TRequest request, Hecole.Mediator.RequestHandlerDelegate<TResponse> next, System.Threading.CancellationToken cancellationToken)
        interface Hecole.Mediator.IPublisher
          method System.Threading.Tasks.Task Publish(object notification, System.Threading.CancellationToken cancellationToken = default)
          method System.Threading.Tasks.Task Publish<TNotification>(TNotification notification, System.Threading.CancellationToken cancellationToken = default) where TNotification : Hecole.Mediator.INotification
        interface Hecole.Mediator.IRequest : Hecole.Mediator.IBaseRequest
        interface Hecole.Mediator.IRequest<out TResponse> : Hecole.Mediator.IBaseRequest
        interface Hecole.Mediator.IRequestCatalog
          property System.Collections.Generic.IReadOnlyList<Hecole.Mediator.RequestDescriptor> Requests { get; }
        interface Hecole.Mediator.IRequestHandler<in TRequest> where TRequest : Hecole.Mediator.IRequest
          method System.Threading.Tasks.Task Handle(TRequest request, System.Threading.CancellationToken cancellationToken)
        interface Hecole.Mediator.IRequestHandler<in TRequest, TResponse> where TRequest : Hecole.Mediator.IRequest<TResponse>
          method System.Threading.Tasks.Task<TResponse> Handle(TRequest request, System.Threading.CancellationToken cancellationToken)
        interface Hecole.Mediator.ISender
          method System.Threading.Tasks.Task Send<TRequest>(TRequest request, System.Threading.CancellationToken cancellationToken = default) where TRequest : Hecole.Mediator.IRequest
          method System.Threading.Tasks.Task<TResponse> Send<TResponse>(Hecole.Mediator.IRequest<TResponse> request, System.Threading.CancellationToken cancellationToken = default)
          method System.Threading.Tasks.Task<object?> Send(object request, System.Threading.CancellationToken cancellationToken = default)
        sealed class Hecole.Mediator.MediatorConfigurationException : System.Runtime.Serialization.ISerializable
          property System.Collections.Generic.IReadOnlyList<string> Problems { get; }
        class Hecole.Mediator.NotificationHandlerExecutor : System.IEquatable<Hecole.Mediator.NotificationHandlerExecutor>
          method Hecole.Mediator.NotificationHandlerExecutor <Clone>$()
          method bool Equals(Hecole.Mediator.NotificationHandlerExecutor? other)
          method override bool Equals(object? obj)
          method override int GetHashCode()
          method override string ToString()
          method static bool op_Equality(Hecole.Mediator.NotificationHandlerExecutor? left, Hecole.Mediator.NotificationHandlerExecutor? right)
          method static bool op_Inequality(Hecole.Mediator.NotificationHandlerExecutor? left, Hecole.Mediator.NotificationHandlerExecutor? right)
          method void Deconstruct(out object HandlerInstance, out System.Func<Hecole.Mediator.INotification, System.Threading.CancellationToken, System.Threading.Tasks.Task> HandlerCallback)
          property System.Func<Hecole.Mediator.INotification, System.Threading.CancellationToken, System.Threading.Tasks.Task> HandlerCallback { get; init; }
          property object HandlerInstance { get; init; }
        sealed class Hecole.Mediator.RequestDescriptor : System.IEquatable<Hecole.Mediator.RequestDescriptor>
          method Hecole.Mediator.RequestDescriptor <Clone>$()
          method bool Equals(Hecole.Mediator.RequestDescriptor? other)
          method override bool Equals(object? obj)
          method override int GetHashCode()
          method override string ToString()
          method static bool op_Equality(Hecole.Mediator.RequestDescriptor? left, Hecole.Mediator.RequestDescriptor? right)
          method static bool op_Inequality(Hecole.Mediator.RequestDescriptor? left, Hecole.Mediator.RequestDescriptor? right)
          method void Deconstruct(out System.Type RequestType, out System.Type ResponseType, out System.Type HandlerType)
          property System.Type HandlerType { get; init; }
          property System.Type RequestType { get; init; }
          property System.Type ResponseType { get; init; }
        delegate System.Threading.Tasks.Task<TResponse> Hecole.Mediator.RequestHandlerDelegate<TResponse>(System.Threading.CancellationToken t = default)
        readonly struct Hecole.Mediator.Unit : System.IComparable, System.IComparable<Hecole.Mediator.Unit>, System.IEquatable<Hecole.Mediator.Unit>
          field static readonly Hecole.Mediator.Unit Value
          method bool Equals(Hecole.Mediator.Unit other)
          method int CompareTo(Hecole.Mediator.Unit other)
          method override bool Equals(object? obj)
          method override int GetHashCode()
          method override string ToString()
          method static bool op_Equality(Hecole.Mediator.Unit first, Hecole.Mediator.Unit second)
          method static bool op_Inequality(Hecole.Mediator.Unit first, Hecole.Mediator.Unit second)
          property static System.Threading.Tasks.Task<Hecole.Mediator.Unit> Task { get; }
        """;

    private const string ExpectedRegistration = """
        static class Microsoft.Extensions.DependencyInjection.HecoleMediatorServiceCollectionExtensions
          method static Microsoft.Extensions.DependencyInjection.IServiceCollection AddHecoleMediator(Microsoft.Extensions.DependencyInjection.IServiceCollection services, System.Action<Hecole.Mediator.HecoleMediatorConfiguration> configure)
          method static Microsoft.Extensions.DependencyInjection.IServiceCollection AddNotificationHandler<TNotification, THandler>(Microsoft.Extensions.DependencyInjection.IServiceCollection services) where TNotification : Hecole.Mediator.INotification where THandler : class, Hecole.Mediator.INotificationHandler<TNotification>
          method static Microsoft.Extensions.DependencyInjection.IServiceCollection AddRequestHandler<TRequest, THandler>(Microsoft.Extensions.DependencyInjection.IServiceCollection services) where TRequest : Hecole.Mediator.IRequest where THandler : class, Hecole.Mediator.IRequestHandler<TRequest>
          method static Microsoft.Extensions.DependencyInjection.IServiceCollection AddRequestHandler<TRequest, TResponse, THandler>(Microsoft.Extensions.DependencyInjection.IServiceCollection services) where TRequest : Hecole.Mediator.IRequest<TResponse> where THandler : class, Hecole.Mediator.IRequestHandler<TRequest, TResponse>
          method static void VerifyHecoleMediator(System.IServiceProvider provider, params System.Reflection.Assembly[] requestAssemblies)
        """;

    private const string ExpectedNotificationPublishers = """
        class Hecole.Mediator.NotificationPublishers.ForeachAwaitPublisher : Hecole.Mediator.INotificationPublisher
          method System.Threading.Tasks.Task Publish(System.Collections.Generic.IEnumerable<Hecole.Mediator.NotificationHandlerExecutor> handlerExecutors, Hecole.Mediator.INotification notification, System.Threading.CancellationToken cancellationToken)
        class Hecole.Mediator.NotificationPublishers.TaskWhenAllPublisher : Hecole.Mediator.INotificationPublisher
          method System.Threading.Tasks.Task Publish(System.Collections.Generic.IEnumerable<Hecole.Mediator.NotificationHandlerExecutor> handlerExecutors, Hecole.Mediator.INotification notification, System.Threading.CancellationToken cancellationToken)
        """;

    private const string ExpectedBehaviors = """
        sealed class Hecole.Mediator.Behaviors.LoggingBehavior<TRequest, TResponse> : Hecole.Mediator.IPipelineBehavior<TRequest, TResponse> where TRequest : notnull
          method System.Threading.Tasks.Task<TResponse> Handle(TRequest request, Hecole.Mediator.RequestHandlerDelegate<TResponse> next, System.Threading.CancellationToken cancellationToken)
        sealed class Hecole.Mediator.Behaviors.PerformanceBehavior<TRequest, TResponse> : Hecole.Mediator.IPipelineBehavior<TRequest, TResponse> where TRequest : notnull
          method System.Threading.Tasks.Task<TResponse> Handle(TRequest request, Hecole.Mediator.RequestHandlerDelegate<TResponse> next, System.Threading.CancellationToken cancellationToken)
        sealed class Hecole.Mediator.Behaviors.PerformanceBehaviorOptions
          property System.TimeSpan Threshold { get; set; }
        sealed class Hecole.Mediator.Behaviors.RequestValidationException : System.Runtime.Serialization.ISerializable
          property System.Collections.Generic.IReadOnlyList<System.ComponentModel.DataAnnotations.ValidationResult> Errors { get; }
          property string Message { get; }
        sealed class Hecole.Mediator.Behaviors.UnhandledExceptionBehavior<TRequest, TResponse> : Hecole.Mediator.IPipelineBehavior<TRequest, TResponse> where TRequest : notnull
          method System.Threading.Tasks.Task<TResponse> Handle(TRequest request, Hecole.Mediator.RequestHandlerDelegate<TResponse> next, System.Threading.CancellationToken cancellationToken)
        sealed class Hecole.Mediator.Behaviors.ValidationBehavior<TRequest, TResponse> : Hecole.Mediator.IPipelineBehavior<TRequest, TResponse> where TRequest : notnull
          method System.Threading.Tasks.Task<TResponse> Handle(TRequest request, Hecole.Mediator.RequestHandlerDelegate<TResponse> next, System.Threading.CancellationToken cancellationToken)
        """;

    [Fact]
    public void PublicApi_OfBehaviorsNamespace_MatchesSnapshot()
    {
        var actual = Describe(typeof(IMediator).Assembly, "Hecole.Mediator.Behaviors");

        Assert.Equal(Normalize(ExpectedBehaviors), actual);
    }

    [Fact]
    public void PublicApi_OfHecoleMediatorNamespace_MatchesSnapshot()
    {
        var actual = Describe(typeof(IMediator).Assembly, "Hecole.Mediator");

        Assert.Equal(Normalize(Expected), actual);
    }

    [Fact]
    public void PublicApi_OfRegistrationNamespace_MatchesSnapshot()
    {
        var actual = Describe(typeof(IMediator).Assembly, "Microsoft.Extensions.DependencyInjection");

        Assert.Equal(Normalize(ExpectedRegistration), actual);
    }

    [Fact]
    public void PublicApi_OfNotificationPublishersNamespace_MatchesSnapshot()
    {
        var actual = Describe(typeof(IMediator).Assembly, "Hecole.Mediator.NotificationPublishers");

        Assert.Equal(Normalize(ExpectedNotificationPublishers), actual);
    }

    [Fact]
    public void PublicTypes_LiveOnlyInExpectedNamespaces()
    {
        string[] allowed =
        [
            "Hecole.Mediator",
            "Hecole.Mediator.Behaviors",
            "Hecole.Mediator.NotificationPublishers",
            "Microsoft.Extensions.DependencyInjection",
        ];

        var unexpected = typeof(IMediator).Assembly.GetExportedTypes()
            .Where(t => !allowed.Contains(t.Namespace))
            .Select(t => t.FullName)
            .ToList();

        Assert.Empty(unexpected);
    }

    private static string Normalize(string text) => text.ReplaceLineEndings("\n").Trim();

    private static string Describe(Assembly assembly, string ns)
    {
        var nullability = new NullabilityInfoContext();
        var sb = new StringBuilder();

        foreach (var type in assembly.GetExportedTypes()
                     .Where(t => t.Namespace == ns)
                     .OrderBy(t => t.Name.Split('`')[0], StringComparer.Ordinal)
                     .ThenBy(t => t.GetGenericArguments().Length))
        {
            if (typeof(Delegate).IsAssignableFrom(type))
            {
                var invoke = type.GetMethod("Invoke")!;
                sb.Append("delegate ")
                  .Append(FormatType(invoke.ReturnType, nullability.Create(invoke.ReturnParameter)))
                  .Append(' ').Append(TypeName(type, declaration: true))
                  .Append('(').Append(FormatParameters(invoke, nullability)).Append(')')
                  .Append('\n');
                continue;
            }

            sb.Append(Kind(type)).Append(' ').Append(TypeName(type, declaration: true));

            var baseList = type.GetInterfaces()
                .Where(i => !type.GetInterfaces().Any(o => o != i && o.GetInterfaces().Contains(i)) || type.IsValueType)
                .Select(i => FormatType(i, null))
                .OrderBy(s => s, StringComparer.Ordinal)
                .ToList();
            if (baseList.Count > 0)
                sb.Append(" : ").Append(string.Join(", ", baseList));

            sb.Append(FormatConstraints(type.GetGenericArguments())).Append('\n');

            const BindingFlags flags = BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;
            var members = new List<string>();

            foreach (var field in type.GetFields(flags))
            {
                members.Add($"field {(field.IsStatic ? "static " : "")}{(field.IsInitOnly ? "readonly " : "")}{FormatType(field.FieldType, nullability.Create(field))} {field.Name}");
            }

            foreach (var property in type.GetProperties(flags))
            {
                var accessor = property.GetMethod ?? property.SetMethod!;
                var setter = property.SetMethod?.ReturnParameter.GetRequiredCustomModifiers().Contains(typeof(System.Runtime.CompilerServices.IsExternalInit)) == true
                    ? "init; "
                    : "set; ";
                var accessors = (property.GetMethod?.IsPublic == true ? "get; " : "") + (property.SetMethod?.IsPublic == true ? setter : "");
                members.Add($"property {(accessor.IsStatic ? "static " : "")}{FormatType(property.PropertyType, nullability.Create(property))} {property.Name} {{ {accessors}}}");
            }

            foreach (var method in type.GetMethods(flags).Where(m => !m.IsSpecialName || m.Name.StartsWith("op_", StringComparison.Ordinal)))
            {
                var modifiers = (method.IsStatic ? "static " : "")
                    + (method.GetBaseDefinition() != method ? "override " : "");
                var generics = method.IsGenericMethodDefinition
                    ? "<" + string.Join(", ", method.GetGenericArguments().Select(a => a.Name)) + ">"
                    : "";
                members.Add($"method {modifiers}{FormatType(method.ReturnType, nullability.Create(method.ReturnParameter))} {method.Name}{generics}({FormatParameters(method, nullability)}){FormatConstraints(method.GetGenericArguments())}");
            }

            foreach (var member in members.OrderBy(m => m, StringComparer.Ordinal))
                sb.Append("  ").Append(member).Append('\n');
        }

        return Normalize(sb.ToString());
    }

    private static string Kind(Type type) => type switch
    {
        { IsInterface: true } => "interface",
        { IsValueType: true } when type.IsDefined(typeof(System.Runtime.CompilerServices.IsReadOnlyAttribute), false) => "readonly struct",
        { IsValueType: true } => "struct",
        { IsAbstract: true, IsSealed: true } => "static class",
        { IsSealed: true } => "sealed class",
        { IsAbstract: true } => "abstract class",
        _ => "class",
    };

    private static string TypeName(Type type, bool declaration)
    {
        var name = type.IsGenericType ? type.FullName![..type.FullName!.IndexOf('`')] : type.FullName!;
        if (!type.IsGenericType)
            return name;

        var args = type.GetGenericArguments().Select(a =>
        {
            if (!declaration || !a.IsGenericParameter)
                return FormatType(a, null);
            var variance = a.GenericParameterAttributes & GenericParameterAttributes.VarianceMask;
            var prefix = variance switch
            {
                GenericParameterAttributes.Covariant => "out ",
                GenericParameterAttributes.Contravariant => "in ",
                _ => "",
            };
            return prefix + a.Name;
        });
        return $"{name}<{string.Join(", ", args)}>";
    }

    private static string FormatConstraints(Type[] genericArguments)
    {
        var sb = new StringBuilder();
        foreach (var argument in genericArguments.Where(a => a.IsGenericParameter))
        {
            var constraints = new List<string>();
            var attributes = argument.GenericParameterAttributes;
            if (attributes.HasFlag(GenericParameterAttributes.ReferenceTypeConstraint)) constraints.Add("class");
            if (attributes.HasFlag(GenericParameterAttributes.NotNullableValueTypeConstraint)) constraints.Add("struct");
            var typeConstraints = argument.GetGenericParameterConstraints();
            if (constraints.Count == 0 && typeConstraints.Length == 0 && HasNotNullConstraint(argument)) constraints.Add("notnull");
            constraints.AddRange(typeConstraints.Select(c => FormatType(c, null)));
            if (attributes.HasFlag(GenericParameterAttributes.DefaultConstructorConstraint) && !attributes.HasFlag(GenericParameterAttributes.NotNullableValueTypeConstraint))
                constraints.Add("new()");
            if (constraints.Count > 0)
                sb.Append($" where {argument.Name} : {string.Join(", ", constraints)}");
        }

        return sb.ToString();
    }

    // The C# compiler encodes 'notnull' as nullability 1 on the generic parameter: either [Nullable(1)]
    // on the parameter itself or, when omitted, the [NullableContext(1)] of the declaring method or type.
    private static bool HasNotNullConstraint(Type genericParameter)
    {
        var own = NullableFlag(genericParameter, "NullableAttribute");
        if (own is not null)
            return own == 1;

        var context = (genericParameter.DeclaringMethod is { } m ? NullableFlag(m, "NullableContextAttribute") : null)
            ?? NullableFlag(genericParameter.DeclaringType!, "NullableContextAttribute");
        return context == 1;
    }

    private static byte? NullableFlag(MemberInfo member, string attributeName)
    {
        var attribute = member.CustomAttributes.FirstOrDefault(a =>
            a.AttributeType.FullName == "System.Runtime.CompilerServices." + attributeName);
        return attribute?.ConstructorArguments.Count == 1 && attribute.ConstructorArguments[0].Value is byte b ? b : null;
    }

    private static string FormatParameters(MethodInfo method, NullabilityInfoContext nullability) =>
        string.Join(", ", method.GetParameters().Select(p =>
        {
            var modifier = p.IsDefined(typeof(ParamArrayAttribute), false) ? "params "
                : p.ParameterType.IsByRef ? (p.IsOut ? "out " : p.IsIn ? "in " : "ref ")
                : "";
            var type = p.ParameterType.IsByRef ? p.ParameterType.GetElementType()! : p.ParameterType;
            var info = nullability.Create(p);
            var text = modifier + $"{FormatType(type, p.ParameterType.IsByRef ? info.ElementType ?? info : info)} {p.Name}";
            if (p.HasDefaultValue)
                text += " = " + (p.DefaultValue is null ? "default" : p.DefaultValue.ToString());
            return text;
        }));

    private static string FormatType(Type type, NullabilityInfo? info)
    {
        var suffix = info?.ReadState == NullabilityState.Nullable && !type.IsValueType ? "?" : "";

        // An unconstrained generic parameter reads as maybe-null; the declaration has no '?', so none is shown.
        if (type.IsGenericParameter)
            return type.Name;

        var keyword = type.IsEnum ? null : Type.GetTypeCode(type) switch
        {
            TypeCode.Boolean => "bool",
            TypeCode.Int32 => "int",
            TypeCode.String => "string",
            TypeCode.Object when type == typeof(object) => "object",
            _ => null,
        };
        if (keyword is not null)
            return keyword + suffix;
        if (type == typeof(void))
            return "void";

        if (!type.IsGenericType)
            return type.FullName + suffix;

        var arguments = type.GetGenericArguments();
        var formatted = arguments.Select((a, i) =>
            FormatType(a, info is not null && info.GenericTypeArguments.Length > i ? info.GenericTypeArguments[i] : null));
        var name = (type.IsGenericTypeDefinition ? type.FullName : type.GetGenericTypeDefinition().FullName)!;
        return $"{name[..name.IndexOf('`')]}<{string.Join(", ", formatted)}>{suffix}";
    }
}
