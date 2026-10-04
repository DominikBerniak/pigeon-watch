using Microsoft.Extensions.DependencyInjection;

namespace PigeonWatch.ArchitectureTests;

public class ServiceRegistrationTests
{
    private static readonly string[] roleSuffixes = ["Service", "Provider", "Repository", "ViewModelCreator", "Mapper"];

    [Fact]
    public void Role_types_exist()
    {
        Assert.NotEmpty(RoleTypes());
    }

    [Fact]
    public void Role_types_are_not_static()
    {
        List<string> offenders = RoleTypes()
            .Where(StaticClassTests.IsStaticClass)
            .Select(type => type.FullName!)
            .ToList();

        Assert.True(
            offenders.Count == 0,
            $"Services, providers, repositories, view model creators and mappers must not be static. Offending types:{Environment.NewLine}"
            + PigeonWatchAssemblies.Describe(offenders));
    }

    [Fact]
    public void Role_types_implement_a_matching_interface()
    {
        List<string> offenders = RoleTypes()
            .Where(type => FindMatchingInterface(type) is null)
            .Select(type => $"{type.FullName} implements no interface named I{type.Name}")
            .ToList();

        Assert.True(
            offenders.Count == 0,
            $"Every service, provider, repository, view model creator and mapper must implement I<TypeName>. Offending types:{Environment.NewLine}"
            + PigeonWatchAssemblies.Describe(offenders));
    }

    [Fact]
    public void Role_type_interfaces_are_registered_by_AddPigeonWatch()
    {
        ServiceCollection services = PigeonWatchServices.Create();

        List<string> offenders = [];
        foreach (Type type in RoleTypes())
        {
            Type? matchingInterface = FindMatchingInterface(type);

            if (matchingInterface is null)
            {
                offenders.Add($"{type.FullName} has no matching interface to register");
                continue;
            }

            bool registered = services.Any(descriptor =>
                descriptor.ServiceType == matchingInterface
                && !descriptor.IsKeyedService
                && descriptor.ImplementationType == type);

            if (!registered)
                offenders.Add($"{matchingInterface.FullName} -> {type.FullName} is not registered by AddPigeonWatch");
        }

        Assert.True(
            offenders.Count == 0,
            $"Every service, provider, repository, view model creator and mapper must be registered against its interface in {PigeonWatchAssemblies.DependencyInjectionName}. Offenders:{Environment.NewLine}"
            + PigeonWatchAssemblies.Describe(offenders));
    }

    private static List<Type> RoleTypes() =>
        PigeonWatchAssemblies.AllTypes()
            .Where(type => type.IsClass && roleSuffixes.Any(suffix => type.Name.EndsWith(suffix, StringComparison.Ordinal)))
            .ToList();

    private static Type? FindMatchingInterface(Type type)
    {
        string suffix = roleSuffixes.First(candidate => type.Name.EndsWith(candidate, StringComparison.Ordinal));
        HashSet<string> acceptedNames = AcceptedInterfaceNames(type.Name, suffix);

        return type.GetInterfaces()
            .Where(candidate => PigeonWatchAssemblies.All.Contains(candidate.Assembly))
            .OrderByDescending(candidate => candidate.Name.Length)
            .FirstOrDefault(candidate => acceptedNames.Contains(candidate.Name));
    }

    private static HashSet<string> AcceptedInterfaceNames(string typeName, string suffix)
    {
        HashSet<string> names = new(StringComparer.Ordinal);
        int lastStart = typeName.Length - suffix.Length;

        for (int i = 0; i <= lastStart; i++)
        {
            if (char.IsUpper(typeName[i]))
                names.Add("I" + typeName[i..]);
        }

        return names;
    }
}
