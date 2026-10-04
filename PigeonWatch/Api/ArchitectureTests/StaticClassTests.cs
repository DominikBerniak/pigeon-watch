using System.Reflection;
using System.Runtime.CompilerServices;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace PigeonWatch.ArchitectureTests;

public class StaticClassTests
{
    private const BindingFlags declaredMembers =
        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance | BindingFlags.DeclaredOnly;

    [Fact]
    public void No_static_classes_except_const_holders_and_service_collection_extensions()
    {
        List<string> offenders = PigeonWatchAssemblies.AllTypes()
            .Where(IsStaticClass)
            .Where(type => !IsConstOnly(type) && !IsServiceCollectionExtensionClass(type))
            .Select(type => type.FullName ?? type.Name)
            .ToList();

        Assert.True(
            offenders.Count == 0,
            $"Static classes are only allowed for const-only holders and IServiceCollection or IEndpointRouteBuilder extension classes in {PigeonWatchAssemblies.DependencyInjectionName}. Offending types:{Environment.NewLine}"
            + PigeonWatchAssemblies.Describe(offenders));
    }

    public static bool IsStaticClass(Type type)
    {
        return type.IsClass && type.IsAbstract && type.IsSealed;
    }

    private static bool IsConstOnly(Type type)
    {
        MemberInfo[] members = type.GetMembers(declaredMembers)
            .Where(member => !IsCompilerGeneratedMember(member))
            .ToArray();

        return members.All(member => member is FieldInfo { IsLiteral: true });
    }

    private static bool IsServiceCollectionExtensionClass(Type type)
    {
        if (type.Assembly != PigeonWatchAssemblies.DependencyInjection)
            return false;

        MemberInfo[] members = type.GetMembers(declaredMembers)
            .Where(member => !IsCompilerGeneratedMember(member))
            .ToArray();

        return members.Length > 0 && members.All(member => member is MethodInfo method && IsServiceCollectionExtension(method));
    }

    private static bool IsServiceCollectionExtension(MethodInfo method)
    {
        ParameterInfo[] parameters = method.GetParameters();

        return method.IsDefined(typeof(ExtensionAttribute), false)
            && parameters.Length > 0
            && (parameters[0].ParameterType == typeof(IServiceCollection)
                || parameters[0].ParameterType == typeof(IEndpointRouteBuilder));
    }

    private static bool IsCompilerGeneratedMember(MemberInfo member)
    {
        return member.Name.Contains('<')
            || member.IsDefined(typeof(CompilerGeneratedAttribute), false)
            || (member is Type nested && PigeonWatchAssemblies.IsCompilerGenerated(nested));
    }
}
