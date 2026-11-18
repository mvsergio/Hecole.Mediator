using System.Reflection;
using System.Reflection.Emit;

namespace Hecole.Mediator.Tests.Fakes;

/// <summary>
/// Builds, in memory, an assembly with one handler class implementing <c>handlerInterface</c>, so a scan can be tested
/// with a handler that the test assembly itself must not contain (the scan would refuse it in every other test).
/// The handler methods return <see langword="null"/>; they are never called.
/// </summary>
internal static class EmittedHandlerAssembly
{
    public static Assembly WithHandler(Type handlerInterface, string handlerName = "EmittedHandler")
    {
        var assembly = AssemblyBuilder.DefineDynamicAssembly(
            new AssemblyName("Hecole.Mediator.Tests.Emitted." + Guid.NewGuid().ToString("N")), AssemblyBuilderAccess.Run);
        var module = assembly.DefineDynamicModule("Main");
        var type = module.DefineType(handlerName, TypeAttributes.Public | TypeAttributes.Sealed | TypeAttributes.Class, typeof(object), [handlerInterface]);
        type.DefineDefaultConstructor(MethodAttributes.Public);

        foreach (var contract in handlerInterface.GetMethods())
        {
            var method = type.DefineMethod(
                contract.Name,
                MethodAttributes.Public | MethodAttributes.Virtual | MethodAttributes.Final | MethodAttributes.HideBySig | MethodAttributes.NewSlot,
                contract.ReturnType,
                contract.GetParameters().Select(p => p.ParameterType).ToArray());
            var il = method.GetILGenerator();
            il.Emit(OpCodes.Ldnull);
            il.Emit(OpCodes.Ret);
            type.DefineMethodOverride(method, contract);
        }

        type.CreateType();
        return assembly;
    }
}
