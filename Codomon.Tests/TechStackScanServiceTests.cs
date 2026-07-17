using Codomon.Desktop.Services;

namespace Codomon.Tests;

public sealed class TechStackScanServiceTests : IDisposable
{
    private readonly string _tempDirectory = Path.Combine(
        Path.GetTempPath(),
        "codomon-tests",
        Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_tempDirectory))
            Directory.Delete(_tempDirectory, recursive: true);
    }

    [Fact]
    public async Task ScanAsync_DetectsPackagesSdkAndInfraMarkers()
    {
        var apiDir = Path.Combine(_tempDirectory, "src", "Payments.Api");
        Directory.CreateDirectory(apiDir);

        await File.WriteAllTextAsync(Path.Combine(apiDir, "Payments.Api.csproj"), """
<Project Sdk="Microsoft.NET.Sdk.Web">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.EntityFrameworkCore" Version="8.0.0" />
    <PackageReference Include="Serilog.AspNetCore" Version="8.0.0" />
    <PackageReference Include="OpenTelemetry.Extensions.Hosting" Version="1.8.1" />
  </ItemGroup>
</Project>
""");

        await File.WriteAllTextAsync(Path.Combine(apiDir, "serilog.json"), "{}");
        await File.WriteAllTextAsync(Path.Combine(apiDir, "Dockerfile"), "FROM mcr.microsoft.com/dotnet/aspnet:8.0");
        await File.WriteAllTextAsync(Path.Combine(_tempDirectory, "docker-compose.yml"), "services: {}");

        var result = await TechStackScanService.ScanAsync(_tempDirectory);

        Assert.Contains(result.Technologies, tech => tech.Name == ".NET" && tech.Version.Contains("net8.0", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(result.Technologies, tech => tech.Name == "ASP.NET Core" && tech.ProjectName == "Payments.Api");
        Assert.Contains(result.Technologies, tech => tech.Name == "Entity Framework Core" && tech.ProjectName == "Payments.Api");
        Assert.Contains(result.Technologies, tech => tech.Name == "Serilog" && tech.ProjectName == "Payments.Api");
        Assert.Contains(result.Technologies, tech => tech.Name == "OpenTelemetry" && tech.ProjectName == "Payments.Api");
        Assert.Contains(result.Technologies, tech => tech.Name == "Docker" && tech.ProjectName == "Payments.Api");
        Assert.Contains(result.Technologies, tech => tech.Name == "docker-compose" && tech.ProjectName == "(workspace)");
    }

    [Fact]
    public async Task CheckAsync_FailsWhenNoProjectsOrKnownMarkersExist()
    {
        Directory.CreateDirectory(_tempDirectory);
        await File.WriteAllTextAsync(Path.Combine(_tempDirectory, "notes.txt"), "hello");

        var result = await TechStackScanService.CheckAsync(_tempDirectory);

        Assert.False(result.IsAvailable);
        Assert.Equal(0, result.ProjectCount);
        Assert.Equal(0, result.DetectionMarkerCount);
    }

    [Fact]
    public async Task ScanAsync_DetectsGodotProjectAndVersion()
    {
        var gameDir = Path.Combine(_tempDirectory, "MyGame");
        Directory.CreateDirectory(gameDir);
        var projectFile = Path.Combine(gameDir, "project.godot");
        await File.WriteAllTextAsync(projectFile, """
; Engine configuration file.
config_version=5

[application]
config/name="My Game"

[rendering]
renderer/rendering_method="gl_compatibility"

config/features=PackedStringArray("4.3", "GL Compatibility")
""");

        var availability = await TechStackScanService.CheckAsync(_tempDirectory);
        var result = await TechStackScanService.ScanAsync(_tempDirectory);

        Assert.True(availability.IsAvailable);
        Assert.Equal(1, availability.ProjectCount);
        Assert.Contains(result.Projects, project => project.ProjectFilePath == projectFile);
        var godot = Assert.Single(result.Technologies.Where(tech => tech.Name == "Godot"));
        Assert.Equal("Game Engine", godot.Category);
        Assert.Equal("4.3", godot.Version);
        Assert.Equal("MyGame", godot.ProjectName);
        Assert.Contains(godot.Evidence, evidence => evidence.SourceRef == projectFile);
    }

    [Fact]
    public async Task ScanAsync_MergesGodotProjectFileWithItsDotNetProject()
    {
        var gameDir = Path.Combine(_tempDirectory, "MyGame");
        Directory.CreateDirectory(gameDir);
        var csprojFile = Path.Combine(gameDir, "MyGame.csproj");
        await File.WriteAllTextAsync(csprojFile, """
<Project Sdk="Godot.NET.Sdk/4.3.0">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
  </PropertyGroup>
</Project>
""");
        await File.WriteAllTextAsync(Path.Combine(gameDir, "project.godot"),
            "config/features=PackedStringArray(\"4.3\", \"GL Compatibility\")");

        var result = await TechStackScanService.ScanAsync(_tempDirectory);

        Assert.Single(result.Projects);
        var godot = Assert.Single(result.Technologies.Where(tech => tech.Name == "Godot"));
        Assert.Equal("MyGame", godot.ProjectName);
        Assert.Equal(csprojFile, godot.ProjectFilePath);
        Assert.Equal("4.3", godot.Version);
        Assert.Equal(2, godot.Evidence.Count);
    }
}
