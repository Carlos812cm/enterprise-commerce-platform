using System.Xml.Linq;
using Xunit;

namespace Commerce.ArchitectureTests;

public sealed class ProjectDependencyRulesTests
{
    private static readonly string RepositoryRoot = FindRepositoryRoot();

    private static readonly ProjectDescriptor[] SourceProjects =
        LoadSourceProjects();

    [Fact]
    public void ProjectReferencePathsMustResolve()
    {
        var projectPaths = SourceProjects
            .Select(project => project.FullPath)
            .ToHashSet(StringComparer.Ordinal);

        var violations = SourceProjects
            .SelectMany(project => project.References
                .Where(reference => !projectPaths.Contains(reference.FullPath))
                .Select(reference => FormatViolation(
                    project,
                    reference,
                    "Project reference does not resolve to a known source project.")))
            .ToArray();

        AssertNoViolations(violations);
    }

    [Fact]
    public void ModuleProjectsMustNotReferenceOtherModules()
    {
        var violations = SourceProjects
            .Where(project => project.ModuleName is not null)
            .SelectMany(project => project.References
                .Where(reference =>
                    reference.ModuleName is not null &&
                    reference.ModuleName != project.ModuleName)
                .Select(reference => FormatViolation(
                    project,
                    reference,
                    "Module projects must not reference projects from another module.")))
            .ToArray();

        AssertNoViolations(violations);
    }

    [Fact]
    public void DomainProjectsMustOnlyReferenceDomainBuildingBlock()
    {
        var violations = SourceProjects
            .Where(project => project.Layer == ProjectLayer.Domain)
            .SelectMany(project => project.References
                .Where(reference => !IsAllowedDomainReference(project, reference))
                .Select(reference => FormatViolation(
                    project,
                    reference,
                    "Domain projects may only reference Commerce.Domain.")))
            .ToArray();

        AssertNoViolations(violations);
    }

    [Fact]
    public void ContractsProjectsMustOnlyReferenceContractsBuildingBlock()
    {
        var violations = SourceProjects
            .Where(project => project.Layer == ProjectLayer.Contracts)
            .SelectMany(project => project.References
                .Where(reference => !IsAllowedContractsReference(project, reference))
                .Select(reference => FormatViolation(
                    project,
                    reference,
                    "Contracts projects may only reference Commerce.Contracts.")))
            .ToArray();

        AssertNoViolations(violations);
    }

    [Fact]
    public void ApplicationProjectsMustOnlyReferenceAllowedApplicationDependencies()
    {
        var violations = SourceProjects
            .Where(project => project.Layer == ProjectLayer.Application)
            .SelectMany(project => project.References
                .Where(reference => !IsAllowedApplicationReference(project, reference))
                .Select(reference => FormatViolation(
                    project,
                    reference,
                    "Application projects may only reference their own Domain/Contracts projects and Commerce.Application.")))
            .ToArray();

        AssertNoViolations(violations);
    }

    [Fact]
    public void InfrastructureProjectsMustOnlyReferenceAllowedInfrastructureDependencies()
    {
        var violations = SourceProjects
            .Where(project => project.Layer == ProjectLayer.Infrastructure)
            .SelectMany(project => project.References
                .Where(reference => !IsAllowedInfrastructureReference(project, reference))
                .Select(reference => FormatViolation(
                    project,
                    reference,
                    "Infrastructure projects may only reference their own module projects and Commerce.Infrastructure.")))
            .ToArray();

        AssertNoViolations(violations);
    }

    [Fact]
    public void ModuleApiProjectsMustOnlyReferenceApplicationAndContracts()
    {
        var violations = SourceProjects
            .Where(project =>
                project.Layer == ProjectLayer.Api &&
                project.ModuleName is not null)
            .SelectMany(project => project.References
                .Where(reference => !IsAllowedModuleApiReference(project, reference))
                .Select(reference => FormatViolation(
                    project,
                    reference,
                    "Module Api projects may only reference their own Application and Contracts projects.")))
            .ToArray();

        AssertNoViolations(violations);
    }

    [Fact]
    public void ApiHostMustOnlyReferenceModuleApiProjects()
    {
        var apiHost = SourceProjects.Single(project => project.Name == "Commerce.Api");

        var violations = apiHost.References
            .Where(reference => !IsAllowedApiHostReference(reference))
            .Select(reference => FormatViolation(
                apiHost,
                reference,
                "Commerce.Api may only reference module Api projects."))
            .ToArray();

        AssertNoViolations(violations);
    }

    [Fact]
    public void WorkerHostMustNotReferenceApiOrDomainProjectsDirectly()
    {
        var workerHost = SourceProjects.Single(project => project.Name == "Commerce.Worker");

        var violations = workerHost.References
            .Where(reference => !IsAllowedWorkerHostReference(reference))
            .Select(reference => FormatViolation(
                workerHost,
                reference,
                "Commerce.Worker may reference Application, Infrastructure and Contracts projects, but not Api or Domain projects directly."))
            .ToArray();

        AssertNoViolations(violations);
    }

    private static bool IsAllowedDomainReference(
        ProjectDescriptor project,
        ProjectReferenceDescriptor reference)
    {
        if (project.Name == "Commerce.Domain")
        {
            return false;
        }

        return reference.Name == "Commerce.Domain";
    }

    private static bool IsAllowedContractsReference(
        ProjectDescriptor project,
        ProjectReferenceDescriptor reference)
    {
        if (project.Name == "Commerce.Contracts")
        {
            return false;
        }

        return reference.Name == "Commerce.Contracts";
    }

    private static bool IsAllowedApplicationReference(
        ProjectDescriptor project,
        ProjectReferenceDescriptor reference)
    {
        if (project.Name == "Commerce.Application")
        {
            return reference.Name is "Commerce.Domain" or "Commerce.Contracts";
        }

        if (project.ModuleName is null)
        {
            return false;
        }

        return
            reference.Name == "Commerce.Application" ||
            reference.Name == $"{project.ModuleName}.Domain" ||
            reference.Name == $"{project.ModuleName}.Contracts";
    }

    private static bool IsAllowedInfrastructureReference(
        ProjectDescriptor project,
        ProjectReferenceDescriptor reference)
    {
        if (project.Name == "Commerce.Infrastructure")
        {
            return reference.Name is
                "Commerce.Application" or
                "Commerce.Domain" or
                "Commerce.Contracts";
        }

        if (project.ModuleName is null)
        {
            return false;
        }

        return
            reference.Name == "Commerce.Infrastructure" ||
            reference.Name == $"{project.ModuleName}.Application" ||
            reference.Name == $"{project.ModuleName}.Domain" ||
            reference.Name == $"{project.ModuleName}.Contracts";
    }

    private static bool IsAllowedModuleApiReference(
        ProjectDescriptor project,
        ProjectReferenceDescriptor reference)
    {
        if (project.ModuleName is null)
        {
            return false;
        }

        return
            reference.Name == $"{project.ModuleName}.Application" ||
            reference.Name == $"{project.ModuleName}.Contracts";
    }

    private static bool IsAllowedApiHostReference(ProjectReferenceDescriptor reference)
    {
        if (IsServiceDefaultsReference(reference))
        {
            return true;
        }

        return
            reference.ModuleName is not null &&
            reference.Layer == ProjectLayer.Api;
    }

    private static bool IsAllowedWorkerHostReference(ProjectReferenceDescriptor reference)
    {
        if (IsServiceDefaultsReference(reference))
        {
            return true;
        }

        if (reference.Name is
            "Commerce.Application" or
            "Commerce.Infrastructure" or
            "Commerce.Contracts")
        {
            return true;
        }

        return
            reference.ModuleName is not null &&
            reference.Layer is
                ProjectLayer.Application or
                ProjectLayer.Infrastructure or
                ProjectLayer.Contracts;
    }

    private static bool IsServiceDefaultsReference(ProjectReferenceDescriptor reference)
    {
        return reference.Name == "Commerce.ServiceDefaults";
    }

    private static ProjectDescriptor[] LoadSourceProjects()
    {
        var sourceDirectory = Path.Combine(RepositoryRoot, "src");

        return Directory
            .EnumerateFiles(sourceDirectory, "*.csproj", SearchOption.AllDirectories)
            .Select(CreateProjectDescriptor)
            .OrderBy(project => project.RelativePath, StringComparer.Ordinal)
            .ToArray();
    }

    private static ProjectDescriptor CreateProjectDescriptor(string projectPath)
    {
        var name = Path.GetFileNameWithoutExtension(projectPath);
        var relativePath = ToRepositoryRelativePath(projectPath);
        var layer = GetProjectLayer(name, relativePath);
        var moduleName = GetModuleName(relativePath);
        var references = LoadProjectReferences(projectPath);

        return new ProjectDescriptor(
            name,
            projectPath,
            relativePath,
            layer,
            moduleName,
            references);
    }

    private static ProjectReferenceDescriptor[] LoadProjectReferences(
    string projectPath)
    {
        var document = XDocument.Load(projectPath);
        var projectDirectory = Path.GetDirectoryName(projectPath)
            ?? throw new InvalidOperationException($"Project path has no directory: {projectPath}");

        return document
            .Descendants()
            .Where(element => element.Name.LocalName == "ProjectReference")
            .Select(element => element.Attribute("Include")?.Value)
            .Where(include => !string.IsNullOrWhiteSpace(include))
            .Select(include => CreateProjectReferenceDescriptor(projectDirectory, include!))
            .OrderBy(reference => reference.RelativePath, StringComparer.Ordinal)
            .ToArray();
    }

    private static ProjectReferenceDescriptor CreateProjectReferenceDescriptor(
        string projectDirectory,
        string include)
    {
        var normalizedInclude = include
            .Replace('\\', Path.DirectorySeparatorChar)
            .Replace('/', Path.DirectorySeparatorChar);

        var fullPath = Path.GetFullPath(Path.Combine(projectDirectory, normalizedInclude));
        var name = Path.GetFileNameWithoutExtension(fullPath);
        var relativePath = ToRepositoryRelativePath(fullPath);
        var layer = GetProjectLayer(name, relativePath);
        var moduleName = GetModuleName(relativePath);

        return new ProjectReferenceDescriptor(
            name,
            fullPath,
            relativePath,
            layer,
            moduleName);
    }

    private static ProjectLayer GetProjectLayer(string projectName, string relativePath)
    {
        if (relativePath.StartsWith("src/Hosts/", StringComparison.Ordinal))
        {
            return ProjectLayer.Host;
        }

        if (projectName.EndsWith(".Domain", StringComparison.Ordinal))
        {
            return ProjectLayer.Domain;
        }

        if (projectName.EndsWith(".Application", StringComparison.Ordinal))
        {
            return ProjectLayer.Application;
        }

        if (projectName.EndsWith(".Infrastructure", StringComparison.Ordinal))
        {
            return ProjectLayer.Infrastructure;
        }

        if (projectName.EndsWith(".Contracts", StringComparison.Ordinal))
        {
            return ProjectLayer.Contracts;
        }

        if (projectName.EndsWith(".Api", StringComparison.Ordinal))
        {
            return ProjectLayer.Api;
        }

        return ProjectLayer.Unknown;
    }

    private static string? GetModuleName(string relativePath)
    {
        var segments = relativePath.Split('/');

        if (segments is ["src", "Modules", var moduleName, ..])
        {
            return moduleName;
        }

        return null;
    }

    private static string ToRepositoryRelativePath(string fullPath)
    {
        return Path
            .GetRelativePath(RepositoryRoot, fullPath)
            .Replace(Path.DirectorySeparatorChar, '/')
            .Replace(Path.AltDirectorySeparatorChar, '/');
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null)
        {
            var solutionFile = Path.Combine(directory.FullName, "EnterpriseCommercePlatform.slnx");
            var sourceDirectory = Path.Combine(directory.FullName, "src");

            if (File.Exists(solutionFile) && Directory.Exists(sourceDirectory))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("Repository root could not be located.");
    }

    private static string FormatViolation(
        ProjectDescriptor project,
        ProjectReferenceDescriptor reference,
        string rule)
    {
        return $"{project.RelativePath} -> {reference.RelativePath}: {rule}";
    }

    private static void AssertNoViolations(string[] violations)
    {
        Assert.True(
            violations.Length == 0,
            "Architecture rule violations:" +
            Environment.NewLine +
            string.Join(Environment.NewLine, violations));
    }

    private sealed record ProjectDescriptor(
        string Name,
        string FullPath,
        string RelativePath,
        ProjectLayer Layer,
        string? ModuleName,
        ProjectReferenceDescriptor[] References);

    private sealed record ProjectReferenceDescriptor(
        string Name,
        string FullPath,
        string RelativePath,
        ProjectLayer Layer,
        string? ModuleName);

    private enum ProjectLayer
    {
        Unknown,
        Host,
        Domain,
        Application,
        Infrastructure,
        Contracts,
        Api
    }
}
