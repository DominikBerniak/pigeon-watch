using System.Reflection;

namespace PigeonWatch.ArchitectureTests;

public class CancellationTokenTests
{
    private const BindingFlags declaredMethods =
        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance | BindingFlags.DeclaredOnly;

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

    private static bool IsAwaitableReturn(Type returnType)
    {
        if (returnType == typeof(Task) || returnType == typeof(ValueTask))
            return true;

        if (!returnType.IsGenericType)
            return false;

        Type definition = returnType.GetGenericTypeDefinition();

        return definition == typeof(Task<>) || definition == typeof(ValueTask<>);
    }

    private static string Describe(MethodInfo method) => $"{method.DeclaringType!.FullName}.{method.Name}";
}
