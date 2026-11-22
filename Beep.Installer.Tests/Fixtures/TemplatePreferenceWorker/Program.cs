using System.Diagnostics;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using System.Text.Json;
using Beep.Installer.Engine;

internal static class Program
{
    private static string _mode = "";
    private static string _lastIo = "";
    private static int Main(string[] args)
    {
        _mode = args[1];
        AppDomain.CurrentDomain.FirstChanceException += (_, exception) =>
        {
            if (exception.Exception is IOException or UnauthorizedAccessException or TimeoutException)
                _lastIo = exception.Exception.GetType().Name + ":" + exception.Exception.HResult.ToString("X8") + ":" + exception.Exception.TargetSite?.Name;
        };
        var testBin = Path.GetFullPath(args[0]);
        AssemblyLoadContext.Default.Resolving += (_, name) =>
        {
            var path = Path.Combine(testBin, name.Name + ".dll");
            return File.Exists(path) ? AssemblyLoadContext.Default.LoadFromAssemblyPath(path) : null;
        };
        try { return Run(args); }
        catch (Exception ex) { Console.WriteLine(JsonSerializer.Serialize(new { error = ex.GetType().Name })); return 3; }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static int Run(string[] args)
    {
        var mode = args[1]; var path = args[2]; var gate = args[3]; var ready = args[4];
        VerifiedTemplateBlueprint? blueprint = null;
        if (mode == "library-write")
        {
            var imported = VerifiedTemplateImporter.Import(args[5], args[6]);
            blueprint = imported.Blueprint;
            if (blueprint is null) return Result(imported.Error.ToString(), 3);
        }
        if (mode == "hold-lock")
        {
            var type = typeof(TemplateFavoritesStore).Assembly.GetType("Beep.Installer.Engine.TemplatePreferenceLock")!;
            using var lease = (IDisposable)type.GetMethod("Acquire", BindingFlags.NonPublic | BindingFlags.Static)!.Invoke(null, new object[] { path })!;
            File.WriteAllText(ready, "locked"); Thread.Sleep(TimeSpan.FromSeconds(30)); return Result("None");
        }
        File.WriteAllText(ready, "ready"); var wait = Stopwatch.StartNew();
        while (!File.Exists(gate)) { if (wait.Elapsed > TimeSpan.FromSeconds(30)) return Result("GateTimeout", 3); Thread.Sleep(10); }
        switch (mode)
        {
            case "favorite-write":
                for (var index = 0; index < 20; index++)
                {
                    var saved = new TemplateFavoritesStore(path).SetFavorite(args[5], true);
                    if (!saved.Available) return Result(saved.Error.ToString(), 2);
                }
                return Result("None");
            case "favorite-read":
                for (var index = 0; index < 400; index++)
                {
                    var current = new TemplateFavoritesStore(path).Read();
                    if (!current.Available) return Result(current.Error.ToString(), 2);
                    Thread.Sleep(2);
                }
                return Result("None");
            case "library-write":
                for (var index = 0; index < 8; index++)
                {
                    var saved = new TemplateLibraryStore(path).Remember(args[5], args[6], blueprint!);
                    if (saved != TemplateLibraryError.None) return Result(saved.ToString(), 2);
                }
                return Result("None");
            case "library-read":
                for (var index = 0; index < 400; index++)
                {
                    var current = new TemplateLibraryStore(path).Read();
                    if (current.Error != TemplateLibraryError.None) return Result(current.Error.ToString(), 2);
                    Thread.Sleep(2);
                }
                return Result("None");
            case "library-discover":
                var discovered = new TemplateLibraryStore(path).Discover();
                Console.WriteLine(JsonSerializer.Serialize(new { error = discovered.Error.ToString(), rejected = discovered.RejectedEntries,
                    ids = discovered.Templates.Select(template => template.Id).ToArray() }));
                return discovered.Error == TemplateLibraryError.None ? 0 : 2;
            default: return Result("UnknownMode", 3);
        }
    }
    private static int Result(string error, int code = 0) { Console.WriteLine(JsonSerializer.Serialize(new { mode = _mode, error, io = _lastIo })); return code; }
}
