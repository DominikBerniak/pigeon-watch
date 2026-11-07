using System.Reflection;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace PigeonWatch.ArchitectureTests;

public class CancellationTokenTests
{
    private const BindingFlags declaredMethods =
        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance | BindingFlags.DeclaredOnly;

    private static readonly IReadOnlyList<MethodInfo> exemptFrameworkInterfaceMethods =
    [
        typeof(IValueProviderFactory).GetMethod(nameof(IValueProviderFactory.CreateValueProviderAsync))!
    ];

    [Fact]
    public void Async_methods_take_a_cancellation_token_with_a_default_except_controller_actions()
    {
        List<string> offenders = [];
        int checkedMethods = 0;

        foreach (Type type in PigeonWatchAssemblies.AllTypes())
        {
            foreach (MethodInfo method in type.GetMethods(declaredMethods))
            {
                if (PigeonWatchAssemblies.IsCompilerGenerated(method) || !IsAwaitableReturn(method.ReturnType))
                    continue;

                if (ImplementsExemptFrameworkInterfaceMethod(type, method))
                    continue;

                checkedMethods++;
                bool isControllerAction = ControllerActionTests.IsControllerAction(method);
                ParameterInfo? cancellationToken = method.GetParameters()
                    .FirstOrDefault(parameter => parameter.ParameterType == typeof(CancellationToken));

                if (cancellationToken is null)
                    offenders.Add($"{Describe(method)} has no CancellationToken parameter");
                else if (isControllerAction && cancellationToken.HasDefaultValue)
                    offenders.Add($"{Describe(method)} is a controller action; its CancellationToken must not have a default value");
                else if (!isControllerAction && !cancellationToken.HasDefaultValue)
                    offenders.Add($"{Describe(method)} must declare CancellationToken cancellationToken = default");
            }
        }

        Assert.True(checkedMethods > 0, "No async methods were found; the assembly scan is broken.");
        Assert.True(
            offenders.Count == 0,
            $"Async methods must take a CancellationToken with a default value; controller actions take one without a default. Offenders:{Environment.NewLine}"
            + PigeonWatchAssemblies.Describe(offenders));
    }

    [Fact]
    public void Exempt_framework_interface_methods_are_exactly_the_value_provider_factory_entry_point()
    {
        List<string> exempt = exemptFrameworkInterfaceMethods.Select(Describe).ToList();

        Assert.Equal(["Microsoft.AspNetCore.Mvc.ModelBinding.IValueProviderFactory.CreateValueProviderAsync"], exempt);
    }

    private static bool ImplementsExemptFrameworkInterfaceMethod(Type type, MethodInfo method)
    {
        if (type.IsInterface)
            return false;

        foreach (MethodInfo exempt in exemptFrameworkInterfaceMethods)
        {
            Type interfaceType = exempt.DeclaringType!;

            if (!interfaceType.IsAssignableFrom(type))
                continue;

            InterfaceMapping mapping = type.GetInterfaceMap(interfaceType);
            int index = Array.IndexOf(mapping.InterfaceMethods, exempt);

            if (index >= 0 && mapping.TargetMethods[index] == method)
                return true;
        }

        return false;
    }

    private static bool IsAwaitableReturn(Type returnType)
    {
        if (returnType == typeof(Task) || returnType == typeof(ValueTask))
            return true;

        if (!returnType.IsGenericType)
            return false;

        Type definition = returnType.GetGenericTypeDefinition();

        return definition == typeof(Task<>) || definition == typeof(ValueTask<>);
    }

    private static string Describe(MethodInfo method)
    {
        return $"{method.DeclaringType!.FullName}.{method.Name}";
    }
}
