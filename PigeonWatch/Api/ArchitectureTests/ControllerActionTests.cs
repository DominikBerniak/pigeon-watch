using System.Reflection;
using Microsoft.AspNetCore.Mvc;

namespace PigeonWatch.ArchitectureTests;

public class ControllerActionTests
{
    private const string modelsNamespace = "PigeonWatch.WebApi.Models";

    [Fact]
    public void Controller_actions_return_task_of_action_result_of_api_model()
    {
        List<MethodInfo> actions = ControllerTypes()
            .SelectMany(controller => controller.GetMethods(BindingFlags.Public | BindingFlags.Instance))
            .Where(IsControllerAction)
            .ToList();

        List<string> offenders = actions
            .Where(action => !ReturnsTaskOfActionResultOfApiModel(action.ReturnType))
            .Select(action => $"{action.DeclaringType!.FullName}.{action.Name} returns {FormatType(action.ReturnType)}")
            .ToList();

        Assert.NotEmpty(actions);
        Assert.True(
            offenders.Count == 0,
            $"Every public controller action must return Task<ActionResult<T>> with T in {modelsNamespace}. Offenders:{Environment.NewLine}"
            + PigeonWatchAssemblies.Describe(offenders));
    }

    public static bool IsControllerAction(MethodInfo method) =>
        method.IsPublic
        && !method.IsStatic
        && !method.IsSpecialName
        && !method.IsAbstract
        && method.DeclaringType is not null
        && typeof(ControllerBase).IsAssignableFrom(method.DeclaringType)
        && method.DeclaringType.Assembly != typeof(ControllerBase).Assembly
        && method.DeclaringType != typeof(object)
        && !method.IsDefined(typeof(NonActionAttribute), true);

    public static List<Type> ControllerTypes() =>
        PigeonWatchAssemblies.AllTypes()
            .Where(type => type.IsClass && typeof(ControllerBase).IsAssignableFrom(type))
            .ToList();

    private static bool ReturnsTaskOfActionResultOfApiModel(Type returnType)
    {
        if (!returnType.IsGenericType || returnType.GetGenericTypeDefinition() != typeof(Task<>))
        {
            return false;
        }

        Type actionResult = returnType.GetGenericArguments()[0];
        if (!actionResult.IsGenericType || actionResult.GetGenericTypeDefinition() != typeof(ActionResult<>))
        {
            return false;
        }

        Type model = actionResult.GetGenericArguments()[0];

        return model.Namespace == modelsNamespace && model.Assembly == PigeonWatchAssemblies.WebApi;
    }

    private static string FormatType(Type type)
    {
        if (!type.IsGenericType)
        {
            return type.FullName ?? type.Name;
        }

        int arityMarker = type.Name.IndexOf('`');
        string name = arityMarker < 0 ? type.Name : type.Name[..arityMarker];

        return $"{name}<{string.Join(", ", type.GetGenericArguments().Select(FormatType))}>";
    }
}
