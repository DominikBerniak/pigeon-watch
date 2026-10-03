using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PigeonWatch.Data;

namespace PigeonWatch.ArchitectureTests;

public class TypeLocationTests
{
    public static TheoryData<string, string> SuffixNamespaces => new()
    {
        { "Repository", "PigeonWatch.Data.Repositories" },
        { "Service", "PigeonWatch.BusinessLogic.Services" },
        { "ViewModelCreator", "PigeonWatch.WebApi.ViewModelCreators" },
        { "Model", "PigeonWatch.WebApi.Models" }
    };

    [Theory]
    [MemberData(nameof(SuffixNamespaces))]
    public void Types_with_role_suffix_live_in_their_namespace(string suffix, string expectedNamespace)
    {
        List<string> offenders = PigeonWatchAssemblies.AllTypes()
            .Where(type => type.Name.EndsWith(suffix, StringComparison.Ordinal) && type.Namespace != expectedNamespace)
            .Select(type => type.FullName!)
            .ToList();

        Assert.True(
            offenders.Count == 0,
            $"Types ending in {suffix} must live in {expectedNamespace}. Offending types:{Environment.NewLine}"
            + PigeonWatchAssemblies.Describe(offenders));
    }

    [Fact]
    public void Api_models_live_only_in_WebApi()
    {
        List<string> offenders = PigeonWatchAssemblies.AllTypes()
            .Where(type => type.Namespace == "PigeonWatch.WebApi.Models" && type.Assembly != PigeonWatchAssemblies.WebApi)
            .Select(type => type.FullName!)
            .ToList();

        Assert.True(
            offenders.Count == 0,
            $"API models must live in {PigeonWatchAssemblies.WebApiName}. Offending types:{Environment.NewLine}"
            + PigeonWatchAssemblies.Describe(offenders));
    }

    [Fact]
    public void Controllers_live_only_in_WebApi_Controllers()
    {
        List<Type> controllers = ControllerActionTests.ControllerTypes();
        List<string> offenders = controllers
            .Where(type => type.Assembly != PigeonWatchAssemblies.WebApi || type.Namespace != "PigeonWatch.WebApi.Controllers")
            .Select(type => type.FullName!)
            .ToList();

        Assert.NotEmpty(controllers);
        Assert.True(
            offenders.Count == 0,
            $"Controllers must live in PigeonWatch.WebApi.Controllers. Offending types:{Environment.NewLine}"
            + PigeonWatchAssemblies.Describe(offenders));
    }

    [Fact]
    public void DbContexts_live_only_in_Data()
    {
        List<string> offenders = PigeonWatchAssemblies.AllTypes()
            .Where(type => typeof(DbContext).IsAssignableFrom(type) && type.Assembly != PigeonWatchAssemblies.Data)
            .Select(type => type.FullName!)
            .ToList();

        Assert.True(
            offenders.Count == 0,
            $"DbContext types must live in {PigeonWatchAssemblies.DataName}. Offending types:{Environment.NewLine}"
            + PigeonWatchAssemblies.Describe(offenders));
    }

    [Fact]
    public void Entities_live_only_in_Data_and_are_not_public()
    {
        Type entityBase = PigeonWatchAssemblies.Data.GetType("PigeonWatch.Data.Entities.Entity", throwOnError: true)!;
        List<Type> modelEntityTypes = ModelEntityClrTypes();

        List<string> offenders = PigeonWatchAssemblies.AllTypes()
            .Where(type => entityBase.IsAssignableFrom(type))
            .Concat(modelEntityTypes)
            .Distinct()
            .Where(type => type.Assembly != PigeonWatchAssemblies.Data || type.IsPublic || type.IsNestedPublic)
            .Select(type => type.FullName!)
            .ToList();

        Assert.NotEmpty(modelEntityTypes);
        Assert.True(
            offenders.Count == 0,
            $"EF entities must live in {PigeonWatchAssemblies.DataName} and must not be public. Offending types:{Environment.NewLine}"
            + PigeonWatchAssemblies.Describe(offenders));
    }

    private static List<Type> ModelEntityClrTypes()
    {
        using ServiceProvider provider = PigeonWatchServices.Create().BuildServiceProvider();
        using IServiceScope scope = provider.CreateScope();
        PigeonWatchDbContext context = scope.ServiceProvider.GetRequiredService<PigeonWatchDbContext>();

        return context.Model.GetEntityTypes().Select(entityType => entityType.ClrType).ToList();
    }
}
