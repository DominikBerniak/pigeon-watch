using System.Reflection;
using NetArchTest.Rules;

namespace PigeonWatch.ArchitectureTests;

public class LayerDependencyTests
{
    private const string entityFrameworkCore = "Microsoft.EntityFrameworkCore";
    private const string aspNetCore = "Microsoft.AspNetCore";

    public static TheoryData<string, string[]> ForbiddenDependencies => new()
    {
        {
            PigeonWatchAssemblies.BusinessObjectsName,
            [
                PigeonWatchAssemblies.DataName,
                PigeonWatchAssemblies.BusinessLogicName,
                PigeonWatchAssemblies.WebApiName,
                PigeonWatchAssemblies.DependencyInjectionName,
                PigeonWatchAssemblies.WebApiHostName,
                entityFrameworkCore,
                aspNetCore
            ]
        },
        {
            PigeonWatchAssemblies.DataName,
            [
                PigeonWatchAssemblies.BusinessLogicName,
                PigeonWatchAssemblies.WebApiName,
                PigeonWatchAssemblies.DependencyInjectionName,
                PigeonWatchAssemblies.WebApiHostName
            ]
        },
        {
            PigeonWatchAssemblies.BusinessLogicName,
            [
                PigeonWatchAssemblies.WebApiName,
                PigeonWatchAssemblies.DependencyInjectionName,
                PigeonWatchAssemblies.WebApiHostName,
                entityFrameworkCore,
                aspNetCore
            ]
        },
        {
            PigeonWatchAssemblies.WebApiName,
            [
                PigeonWatchAssemblies.DataName,
                PigeonWatchAssemblies.DependencyInjectionName,
                entityFrameworkCore
            ]
        },
        {
            PigeonWatchAssemblies.WebApiHostName,
            [
                PigeonWatchAssemblies.DataName,
                PigeonWatchAssemblies.BusinessLogicName,
                entityFrameworkCore
            ]
        }
    };

    public static TheoryData<string> AssembliesWithoutRegistrationCode => new()
    {
        PigeonWatchAssemblies.BusinessObjectsName,
        PigeonWatchAssemblies.DataName,
        PigeonWatchAssemblies.BusinessLogicName,
        PigeonWatchAssemblies.WebApiName
    };

    [Theory]
    [MemberData(nameof(ForbiddenDependencies))]
    public void Layer_does_not_depend_on_forbidden_assemblies(string assemblyName, string[] forbidden)
    {
        Assembly assembly = PigeonWatchAssemblies.All.Single(candidate => candidate.GetName().Name == assemblyName);

        NetArchTest.Rules.TestResult result = Types.InAssembly(assembly)
            .Should()
            .NotHaveDependencyOnAny(forbidden)
            .GetResult();

        Assert.True(
            result.IsSuccessful,
            $"{assemblyName} must not depend on {string.Join(", ", forbidden)}. Offending types:{Environment.NewLine}"
            + PigeonWatchAssemblies.Describe(result.FailingTypeNames ?? []));
    }

    [Theory]
    [MemberData(nameof(AssembliesWithoutRegistrationCode))]
    public void Only_dependency_injection_uses_service_registration_apis(string assemblyName)
    {
        Assembly assembly = PigeonWatchAssemblies.All.Single(candidate => candidate.GetName().Name == assemblyName);

        NetArchTest.Rules.TestResult result = Types.InAssembly(assembly)
            .Should()
            .NotHaveDependencyOnAny(
                "Microsoft.Extensions.DependencyInjection.IServiceCollection",
                "Microsoft.Extensions.DependencyInjection.ServiceCollection",
                "Microsoft.Extensions.DependencyInjection.ServiceCollectionServiceExtensions",
                "Microsoft.Extensions.DependencyInjection.Extensions.ServiceCollectionDescriptorExtensions")
            .GetResult();

        Assert.True(
            result.IsSuccessful,
            $"{assemblyName} must not contain service registration code; register services in {PigeonWatchAssemblies.DependencyInjectionName}. Offending types:{Environment.NewLine}"
            + PigeonWatchAssemblies.Describe(result.FailingTypeNames ?? []));
    }
}
