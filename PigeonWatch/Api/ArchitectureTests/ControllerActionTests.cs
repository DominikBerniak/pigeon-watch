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

    [Fact]
    public void Api_models_expose_no_business_object_or_data_types()
    {
        List<Type> models = PigeonWatchAssemblies.WebApi.GetTypes()
            .Where(type => type.Namespace == modelsNamespace && !PigeonWatchAssemblies.IsCompilerGenerated(type))
            .ToList();

        List<string> offenders = models
            .SelectMany(model => ForbiddenWireTypes(model, [])
                .Select(forbidden => $"{model.FullName} exposes {FormatType(forbidden)}"))
            .ToList();

        Assert.NotEmpty(models);
        Assert.True(
            offenders.Count == 0,
            $"API models in {modelsNamespace} must not expose {PigeonWatchAssemblies.BusinessObjectsName} or {PigeonWatchAssemblies.DataName} types. Offenders:{Environment.NewLine}"
            + PigeonWatchAssemblies.Describe(offenders));
    }

    public static bool IsControllerAction(MethodInfo method)
    {
        return method.IsPublic
            && !method.IsStatic
            && !method.IsSpecialName
            && !method.IsAbstract
            && method.DeclaringType is not null
            && typeof(ControllerBase).IsAssignableFrom(method.DeclaringType)
            && method.DeclaringType.Assembly != typeof(ControllerBase).Assembly
            && method.DeclaringType != typeof(object)
            && !method.IsDefined(typeof(NonActionAttribute), true);
    }

    public static List<Type> ControllerTypes()
    {
        return PigeonWatchAssemblies.AllTypes()
            .Where(type => type.IsClass && typeof(ControllerBase).IsAssignableFrom(type))
            .ToList();
    }

    private static bool ReturnsTaskOfActionResultOfApiModel(Type returnType)
    {
        if (!returnType.IsGenericType || returnType.GetGenericTypeDefinition() != typeof(Task<>))
            return false;

        Type actionResult = returnType.GetGenericArguments()[0];

        if (!actionResult.IsGenericType || actionResult.GetGenericTypeDefinition() != typeof(ActionResult<>))
            return false;

        Type model = actionResult.GetGenericArguments()[0];

        return model.Namespace == modelsNamespace && model.Assembly == PigeonWatchAssemblies.WebApi;
    }

    private static IEnumerable<Type> ForbiddenWireTypes(Type type, HashSet<Type> visited)
    {
        if (!visited.Add(type))
            yield break;

        if (type.Assembly == PigeonWatchAssemblies.BusinessObjects || type.Assembly == PigeonWatchAssemblies.Data)
        {
            yield return type;
            yield break;
        }

        List<Type> nested = [];

        if (type.IsArray)
            nested.Add(type.GetElementType()!);

        if (type.IsGenericType)
            nested.AddRange(type.GetGenericArguments());

        if (type.Assembly == PigeonWatchAssemblies.WebApi)
            nested.AddRange(type.GetProperties(BindingFlags.Public | BindingFlags.Instance).Select(property => property.PropertyType));

        foreach (Type candidate in nested)
        {
            foreach (Type forbidden in ForbiddenWireTypes(candidate, visited))
                yield return forbidden;
        }
    }

    private static string FormatType(Type type)
    {
        if (!type.IsGenericType)
            return type.FullName ?? type.Name;

        int arityMarker = type.Name.IndexOf('`');
        string name = arityMarker < 0 ? type.Name : type.Name[..arityMarker];

        return $"{name}<{string.Join(", ", type.GetGenericArguments().Select(FormatType))}>";
    }
}
