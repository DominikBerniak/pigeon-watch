using System.Text.RegularExpressions;

namespace PigeonWatch.ArchitectureTests;

public class InterfaceLocationTests
{
    private const string interfacesFolderName = "Interfaces";

    private static readonly string[] projectFolders =
        ["BusinessObjects", "Data", "BusinessLogic", "WebApi", "DependencyInjection", "WebApi.Host"];

    private static readonly string[] excludedFolders = ["bin", "obj", "Migrations"];

    private static readonly Regex interfaceDeclaration = new(@"\binterface\s+[A-Z]\w*", RegexOptions.Compiled);

    private static readonly Regex nonInterfaceDeclaration = new(@"\b(class|record|struct|enum)\s+[A-Z]\w*", RegexOptions.Compiled);

    [Fact]
    public void Interfaces_live_in_an_Interfaces_folder()
    {
        List<string> interfaceFiles = SourceFiles()
            .Where(file => interfaceDeclaration.IsMatch(File.ReadAllText(file)))
            .ToList();
        List<string> offenders = interfaceFiles
            .Where(file => !IsInInterfacesFolder(file))
            .Select(RelativePath)
            .ToList();

        Assert.NotEmpty(interfaceFiles);
        Assert.True(
            offenders.Count == 0,
            $"Interfaces must live in an {interfacesFolderName} subfolder of the folder that holds their implementations, keeping the parent folder's namespace. Offending files:{Environment.NewLine}"
            + PigeonWatchAssemblies.Describe(offenders));
    }

    [Fact]
    public void Interfaces_folders_contain_only_interfaces()
    {
        List<string> offenders = SourceFiles()
            .Where(file => IsInInterfacesFolder(file) && nonInterfaceDeclaration.IsMatch(File.ReadAllText(file)))
            .Select(RelativePath)
            .ToList();

        Assert.True(
            offenders.Count == 0,
            $"{interfacesFolderName} folders may only contain interfaces. Offending files:{Environment.NewLine}"
            + PigeonWatchAssemblies.Describe(offenders));
    }

    private static IEnumerable<string> SourceFiles()
    {
        string apiDirectory = ApiDirectory();

        return projectFolders
            .SelectMany(project => Directory.EnumerateFiles(Path.Combine(apiDirectory, project), "*.cs", SearchOption.AllDirectories))
            .Where(file => !RelativePath(file)
                .Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                .Any(segment => excludedFolders.Contains(segment, StringComparer.Ordinal)));
    }

    private static bool IsInInterfacesFolder(string file)
    {
        return string.Equals(Path.GetFileName(Path.GetDirectoryName(file)), interfacesFolderName, StringComparison.Ordinal);
    }

    private static string RelativePath(string file)
    {
        return Path.GetRelativePath(ApiDirectory(), file);
    }

    private static string ApiDirectory()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);

        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "PigeonWatchApi.slnx")))
            directory = directory.Parent;

        return directory?.FullName
            ?? throw new DirectoryNotFoundException("PigeonWatchApi.slnx was not found above the test output directory.");
    }
}
