using Beep.Installer.Engine;

if (args.Length < 2)
{
    Console.Error.WriteLine("Usage: HeadlessSdkConsumer validate|plan|build <project.bsetup> [output-dir]");
    return 2;
}

var command = args[0].Trim().ToLowerInvariant();
var projectPath = args[1];
var outputDirectory = args.Length >= 3 ? args[2] : "";

return command switch
{
    "validate" => Validate(projectPath),
    "plan" => Plan(projectPath),
    "build" => Build(projectPath, outputDirectory),
    _ => Unknown(command)
};

static int Validate(string projectPath)
{
    var result = HeadlessInstallerSdk.Validate(new HeadlessInstallerRequest
    {
        ProjectPath = projectPath,
        Strict = true
    });

    PrintDiagnostics(result.Diagnostics);
    Console.WriteLine(result.Success
        ? $"Validated {result.ProductName} {result.ProductVersion}"
        : "Validation failed.");

    return result.ExitCode;
}

static int Plan(string projectPath)
{
    var result = HeadlessInstallerSdk.Plan(new HeadlessInstallerRequest
    {
        ProjectPath = projectPath,
        Strict = true
    });

    PrintDiagnostics(result.Diagnostics);
    if (result.Success)
    {
        Console.WriteLine($"Plan hash: {result.PlanHash}");
        Console.WriteLine($"Operations: {result.OperationCount}");
    }

    return result.ExitCode;
}

static int Build(string projectPath, string outputDirectory)
{
    var progress = new Progress<BuildPipeline.BuildProgress>(p =>
        Console.WriteLine($"[{p.Percent,3}%] {p.Message}"));

    var result = HeadlessInstallerSdk.Build(new HeadlessInstallerRequest
    {
        ProjectPath = projectPath,
        OutputDirectory = outputDirectory,
        RequireSigned = true,
        TimestampOutagePolicy = "retry",
        TimestampRetryCount = 2,
        Progress = progress
    });

    PrintDiagnostics(result.Diagnostics);
    if (result.Success)
        Console.WriteLine($"Built: {result.OutputFile}");

    return result.ExitCode;
}

static int Unknown(string command)
{
    Console.Error.WriteLine($"Unknown command: {command}");
    return 2;
}

static void PrintDiagnostics(IEnumerable<ProjectSchemaDiagnostic> diagnostics)
{
    foreach (var diagnostic in diagnostics)
        Console.Error.WriteLine($"{diagnostic.Severity} {diagnostic.Code} {diagnostic.Path}: {diagnostic.Message}");
}
