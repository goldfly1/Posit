using System.Text;

namespace Posit.Tools;

/// <summary>
/// Generates .csproj, .sln, and Program.cs files for the bot harness build.
/// </summary>
internal static class BotHarnessProjects
{
    /// <summary>
    /// Generate a .csproj file for a component.
    /// isExe is true only for the CLI component.
    /// projectReferences lists other component projects this one depends on.
    /// </summary>
    internal static string GenerateCsproj(string projectName, bool isExe, List<string>? projectReferences = null)
    {
        var outputType = isExe ? "Exe" : "Library";
        var sb = new StringBuilder();
        sb.AppendLine("<Project Sdk=\"Microsoft.NET.Sdk\">");
        sb.AppendLine();
        sb.AppendLine("  <PropertyGroup>");
        sb.AppendLine("    <TargetFramework>net10.0</TargetFramework>");
        sb.AppendLine($"    <OutputType>{outputType}</OutputType>");
        sb.AppendLine($"    <AssemblyName>{projectName}</AssemblyName>");
        sb.AppendLine($"    <RootNamespace>{projectName}</RootNamespace>");
        sb.AppendLine("    <Nullable>enable</Nullable>");
        sb.AppendLine("    <ImplicitUsings>enable</ImplicitUsings>");
        sb.AppendLine("    <EnableDefaultCompileItems>false</EnableDefaultCompileItems>");
        sb.AppendLine("  </PropertyGroup>");
        sb.AppendLine();
        sb.AppendLine("  <ItemGroup>");
        // Include all .cs files from the project directory recursively (single glob, no duplicates)
        sb.AppendLine("    <Compile Include=\"**\\*.cs\" />");
        sb.AppendLine("  </ItemGroup>");
        sb.AppendLine();
        sb.AppendLine("  <ItemGroup>");
        // Reference DafnyRuntime.dll from the DafnyRuntime/ subdir
        sb.AppendLine("    <Reference Include=\"DafnyRuntime\">");
        sb.AppendLine("      <HintPath>..\\DafnyRuntime\\DafnyRuntime.dll</HintPath>");
        sb.AppendLine("    </Reference>");
        sb.AppendLine("  </ItemGroup>");
        // Add project references for dependencies (Wire.cs calls into other components)
        if (projectReferences is { Count: > 0 })
        {
            sb.AppendLine();
            sb.AppendLine("  <ItemGroup>");
            foreach (var dep in projectReferences)
                if (dep != projectName)
                    sb.AppendLine($"    <ProjectReference Include=\"..\\{dep}\\{dep}.csproj\" />");
            sb.AppendLine("  </ItemGroup>");
        }
        sb.AppendLine();
        sb.AppendLine("</Project>");
        return sb.ToString();
    }

    /// <summary>
    /// Generate a .sln file linking all project .csproj files.
    /// </summary>
    internal static string GenerateSln(string solutionName, List<string> projectNames)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Microsoft Visual Studio Solution File, Format Version 12.00");
        sb.AppendLine("# Visual Studio Version 17");
        sb.AppendLine($"VisualStudioVersion = 17.0.0.0");
        sb.AppendLine("MinimumVisualStudioVersion = 10.0.40219.1");
        sb.AppendLine();

        var projectGuids = new Dictionary<string, string>();
        foreach (var name in projectNames)
        {
            var guid = Guid.NewGuid().ToString("B").ToUpper();
            projectGuids[name] = guid;
            sb.AppendLine($"Project(\"{{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}}\") = \"{name}\", \"{name}\\{name}.csproj\", \"{guid}\"");
            sb.AppendLine("EndProject");
        }

        sb.AppendLine("Global");
        sb.AppendLine("    GlobalSection(SolutionConfigurationPlatforms) = preSolution");
        sb.AppendLine("        Debug|Any CPU = Debug|Any CPU");
        sb.AppendLine("        Release|Any CPU = Release|Any CPU");
        sb.AppendLine("    EndGlobalSection");
        sb.AppendLine("    GlobalSection(ProjectConfigurationPlatforms) = postSolution");
        foreach (var name in projectNames)
        {
            var guid = projectGuids[name];
            sb.AppendLine($"        {guid}.Debug|Any CPU.ActiveCfg = Debug|Any CPU");
            sb.AppendLine($"        {guid}.Debug|Any CPU.Build.0 = Debug|Any CPU");
            sb.AppendLine($"        {guid}.Release|Any CPU.ActiveCfg = Release|Any CPU");
            sb.AppendLine($"        {guid}.Release|Any CPU.Build.0 = Release|Any CPU");
        }
        sb.AppendLine("    EndGlobalSection");
        sb.AppendLine("    GlobalSection(SolutionProperties) = preSolution");
        sb.AppendLine("        HideSolutionNode = FALSE");
        sb.AppendLine("    EndGlobalSection");
        sb.AppendLine("EndGlobal");

        return sb.ToString();
    }

    /// <summary>
    /// Generate a minimal Program.cs entry point for the CLI component
    /// if one doesn't exist in the source bundle.
    /// </summary>
    internal static string GenerateProgramCs(string projectName)
    {
        var sb = new StringBuilder();
        sb.AppendLine("using System;");
        sb.AppendLine();
        sb.AppendLine($"namespace {projectName};");
        sb.AppendLine();
        sb.AppendLine("public static class Program");
        sb.AppendLine("{");
        sb.AppendLine("    public static int Main(string[] args)");
        sb.AppendLine("    {");
        sb.AppendLine("        if (args.Length == 0)");
        sb.AppendLine("        {");
        sb.AppendLine($"            Console.WriteLine(\"Usage: {projectName} <command> [args]\");");
        sb.AppendLine("            return 1;");
        sb.AppendLine("        }");
        sb.AppendLine("        try");
        sb.AppendLine("        {");
        sb.AppendLine("            Console.WriteLine($\"Command: {args[0]}\");");
        sb.AppendLine("            return 0;");
        sb.AppendLine("        }");
        sb.AppendLine("        catch (Exception ex)");
        sb.AppendLine("        {");
        sb.AppendLine("            Console.Error.WriteLine($\"Error: {ex.Message}\");");
        sb.AppendLine("            return 1;");
        sb.AppendLine("        }");
        sb.AppendLine("    }");
        sb.AppendLine("}");
        return sb.ToString();
    }
}