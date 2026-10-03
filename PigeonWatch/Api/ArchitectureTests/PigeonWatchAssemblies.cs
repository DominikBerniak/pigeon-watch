using System.Reflection;
using System.Runtime.CompilerServices;

namespace PigeonWatch.ArchitectureTests;

public class PigeonWatchAssemblies
{
    public const string BusinessObjectsName = "PigeonWatch.BusinessObjects";
    public const string DataName = "PigeonWatch.Data";
    public const string BusinessLogicName = "PigeonWatch.BusinessLogic";
    public const string WebApiName = "PigeonWatch.WebApi";
    public const string DependencyInjectionName = "PigeonWatch.DependencyInjection";
    public const string WebApiHostName = "PigeonWatch.WebApi.Host";

    public static Assembly BusinessObjects { get; } = Load(BusinessObjectsName);

    public static Assembly Data { get; } = Load(DataName);

    public static Assembly BusinessLogic { get; } = Load(BusinessLogicName);

    public static Assembly WebApi { get; } = Load(WebApiName);

    public static Assembly DependencyInjection { get; } = Load(DependencyInjectionName);

    public static Assembly WebApiHost { get; } = Load(WebApiHostName);

    public static IReadOnlyList<Assembly> All { get; } =
        [BusinessObjects, Data, BusinessLogic, WebApi, DependencyInjection, WebApiHost];

    public static IEnumerable<Type> AllTypes() =>
        All.SelectMany(assembly => assembly.GetTypes()).Where(type => !IsCompilerGenerated(type));

    public static bool IsCompilerGenerated(Type type)
    {
        for (Type? current = type; current is not null; current = current.DeclaringType)
        {
            if (current.Name.Contains('<') || current.IsDefined(typeof(CompilerGeneratedAttribute), false))
            {
                return true;
            }
        }

        return false;
    }

    public static bool IsCompilerGenerated(MethodInfo method) =>
        method.Name.Contains('<') || method.IsDefined(typeof(CompilerGeneratedAttribute), false);

    public static string Describe(IEnumerable<string> offenders) =>
        string.Join(Environment.NewLine, offenders.Order(StringComparer.Ordinal));

    private static Assembly Load(string name) => Assembly.Load(new AssemblyName(name));
}
