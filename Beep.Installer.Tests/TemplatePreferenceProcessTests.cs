using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using Beep.Installer.Engine;
using FluentAssertions;
using Xunit;

namespace Beep.Installer.Tests;

public sealed class TemplatePreferenceProcessFixture : IAsyncLifetime
{
    internal string Root { get; } = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "BeepTemplateProcesses-" + Guid.NewGuid().ToString("N")));
    private string Worker => Path.Combine(Root, "bin", "TemplatePreferenceWorker.dll");
    public async Task InitializeAsync()
    {
        Directory.CreateDirectory(Root);
        var source = new DirectoryInfo(AppContext.BaseDirectory);
        while (source is not null && !File.Exists(Path.Combine(source.FullName, "Beep.Installer.Tests.csproj"))) source = source.Parent;
        source.Should().NotBeNull("the process fixture builds the checked-in worker source");
        var info = StartInfo("build", Path.Combine(source!.FullName, "Fixtures", "TemplatePreferenceWorker", "TemplatePreferenceWorker.csproj"),
            "--output", Path.Combine(Root, "bin"), "--nologo", "--verbosity", "minimal",
            "/p:BaseIntermediateOutputPath=" + Path.Combine(Root, "obj") + Path.DirectorySeparatorChar,
            "/p:InstallerTestBin=" + AppContext.BaseDirectory);
        using var build = new OwnedWorker(info);
        var result = await build.FinishAsync(); result.Code.Should().Be(0, result.Output + result.Error);
    }
    public Task DisposeAsync()
    {
        var resolved = Path.GetFullPath(Root);
        if (!resolved.StartsWith(Path.GetFullPath(Path.GetTempPath()), StringComparison.OrdinalIgnoreCase)
            || !Path.GetFileName(resolved).StartsWith("BeepTemplateProcesses-", StringComparison.Ordinal)) throw new InvalidOperationException("Unexpected fixture root.");
        if (Directory.Exists(resolved)) Directory.Delete(resolved, true);
        return Task.CompletedTask;
    }
    private static ProcessStartInfo StartInfo(params string[] args)
    {
        var info = new ProcessStartInfo("dotnet") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var arg in args) info.ArgumentList.Add(arg);
        return info;
    }
    internal OwnedWorker Start(string mode, string store, string gate, string ready, params string[] args)
        => new(StartInfo(new[] { Worker, AppContext.BaseDirectory, mode, store, gate, ready }.Concat(args).ToArray()));

    internal sealed class OwnedWorker : IDisposable
    {
        private readonly Process _process;
        private readonly Task<string> _output;
        private readonly Task<string> _error;
        internal bool Exited => _process.HasExited;
        internal OwnedWorker(ProcessStartInfo info)
        {
            _process = Process.Start(info)!; _output = _process.StandardOutput.ReadToEndAsync(); _error = _process.StandardError.ReadToEndAsync();
        }
        internal async Task<(int Code, string Output, string Error)> FinishAsync()
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(90));
            await _process.WaitForExitAsync(timeout.Token);
            return (_process.ExitCode, await _output, await _error);
        }
        internal async Task StopAsync()
        {
            if (!_process.HasExited) _process.Kill(entireProcessTree: true);
            await _process.WaitForExitAsync();
        }
        public void Dispose() { if (!_process.HasExited) _process.Kill(entireProcessTree: true); _process.Dispose(); }
    }
}

public sealed class TemplatePreferenceProcessTests : IClassFixture<TemplatePreferenceProcessFixture>, IDisposable
{
    private readonly TemplatePreferenceProcessFixture _fixture;
    private readonly string _root;
    public TemplatePreferenceProcessTests(TemplatePreferenceProcessFixture fixture)
    {
        _fixture = fixture; _root = Path.Combine(fixture.Root, Guid.NewGuid().ToString("N")); Directory.CreateDirectory(_root);
    }
    public void Dispose()
    {
        var resolved = Path.GetFullPath(_root);
        if (!resolved.StartsWith(_fixture.Root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Unexpected test root.");
        if (Directory.Exists(resolved)) Directory.Delete(resolved, true);
    }
    private static string Id(int index) => "signed:" + index.ToString("X64");
    private async Task<IReadOnlyList<(int Code, string Output, string Error)>> Batch(params (string Mode, string Store, string[] Args)[] requests)
    {
        var gate = Path.Combine(_root, Guid.NewGuid().ToString("N") + ".gate");
        var workers = requests.Select((request, index) =>
        {
            var ready = gate + ".ready-" + index;
            return (Ready: ready, Worker: _fixture.Start(request.Mode, request.Store, gate, ready, request.Args));
        }).ToArray();
        try
        {
            var wait = Stopwatch.StartNew();
            while (workers.Any(worker => !File.Exists(worker.Ready)))
            {
                foreach (var worker in workers.Where(worker => worker.Worker.Exited && !File.Exists(worker.Ready)))
                { var result = await worker.Worker.FinishAsync(); throw new InvalidOperationException("Worker exited before readiness: " + result.Output + result.Error); }
                wait.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(30), "every independent worker must reach the start gate"); await Task.Delay(20);
            }
            File.WriteAllText(gate, "start");
            return await Task.WhenAll(workers.Select(worker => worker.Worker.FinishAsync()));
        }
        finally { foreach (var worker in workers) { await worker.Worker.StopAsync(); worker.Worker.Dispose(); } }
    }
    private string Favorites => Path.Combine(_root, "favorites.json");
    private string Library => Path.Combine(_root, "library.json");
    private void SeedFavorites(IEnumerable<string> ids) => File.WriteAllText(Favorites, JsonSerializer.Serialize(new { schemaVersion = 1, favoriteIds = ids }));
    private (string[] Packages, string Key, string[] Ids) Packages(int count)
    {
        using var rsa = RSA.Create(2048); var key = Path.Combine(_root, "public.pem"); var privateKey = Path.Combine(_root, "private.pem");
        File.WriteAllText(key, rsa.ExportSubjectPublicKeyInfoPem()); File.WriteAllText(privateKey, rsa.ExportPkcs8PrivateKeyPem());
        var packages = Enumerable.Range(0, count).Select(index =>
        {
            var path = Path.Combine(_root, "package-" + index);
            ProjectTemplatePackageService.Export(new() { TemplateId = ProjectTemplates.ConsoleId, ProductName = "Process sample " + index,
                Publisher = "Process publisher", OutputDirectory = path, SigningPrivateKeyPath = privateKey }).Success.Should().BeTrue();
            return path;
        }).ToArray();
        return (packages, key, packages.Select(path => VerifiedTemplateImporter.Import(path, key).Blueprint!.Id).ToArray());
    }

    [Fact]
    public async Task ConcurrentFavoriteWritersAndReaderPreserveEveryIdAcrossPathCasing()
    {
        SeedFavorites(new[] { ProjectTemplates.ConsoleId });
        var writes = Enumerable.Range(100, 8).Select((index, position) => ("favorite-write", position % 2 == 0 ? Favorites : Favorites.ToUpperInvariant(), new[] { Id(index) }));
        var results = await Batch(writes.Append(("favorite-read", Favorites, Array.Empty<string>())).ToArray());
        foreach (var result in results) result.Code.Should().Be(0, result.Output + result.Error);
        var saved = new TemplateFavoritesStore(Favorites).Read(); saved.Error.Should().Be(TemplateFavoritesError.None);
        saved.Ids.Should().BeEquivalentTo(Enumerable.Range(100, 8).Select(Id).Append(ProjectTemplates.ConsoleId));
        Directory.GetFiles(_root, "*.tmp").Should().BeEmpty();
    }

    [Fact]
    public async Task ConcurrentWritersAtTheFavoritesLimitCannotDamageTheStore()
    {
        SeedFavorites(Enumerable.Range(0, 255).Select(Id));
        var results = await Batch(Enumerable.Range(1000, 4).Select(index => ("favorite-write", Favorites, new[] { Id(index) })).ToArray());
        results.Count(result => result.Code == 0).Should().Be(1); results.Count(result => result.Code == 2 && result.Output.Contains("Invalid")).Should().Be(3);
        var saved = new TemplateFavoritesStore(Favorites).Read(); saved.Error.Should().Be(TemplateFavoritesError.None); saved.Ids.Should().HaveCount(256);
        saved.Ids.Should().Contain(Enumerable.Range(0, 255).Select(Id));
        var before = File.ReadAllBytes(Favorites);
        var refused = await Batch(("favorite-write", Favorites, new[] { Id(2000) })); refused.Single().Code.Should().Be(2);
        File.ReadAllBytes(Favorites).Should().Equal(before); Directory.GetFiles(_root, "*.tmp").Should().BeEmpty();
    }

    [Fact]
    public async Task ConcurrentSignedLibraryWritersAndReaderPreserveEveryVerifiedSource()
    {
        var package = Packages(5); File.WriteAllText(Library, "{\"schemaVersion\":1,\"entries\":[]}");
        var writes = package.Packages.Select(path => ("library-write", Library, new[] { path, package.Key }));
        var results = await Batch(writes.Append(("library-read", Library, Array.Empty<string>())).ToArray());
        foreach (var result in results) result.Code.Should().Be(0, result.Output + result.Error);
        new TemplateLibraryStore(Library).Read().Entries.Should().HaveCount(5); Directory.GetFiles(_root, "*.tmp").Should().BeEmpty();
        var discovered = await Batch(("library-discover", Library, Array.Empty<string>())); discovered.Single().Code.Should().Be(0, discovered.Single().Output);
        using var json = JsonDocument.Parse(discovered.Single().Output);
        json.RootElement.GetProperty("rejected").GetInt32().Should().Be(0);
        json.RootElement.GetProperty("ids").EnumerateArray().Select(value => value.GetString()).Should().BeEquivalentTo(package.Ids);
        var catalog = File.ReadAllBytes(Library); File.AppendAllText(Path.Combine(package.Packages[0], ProjectTemplatePackageService.ProjectFileName), " ");
        var changed = await Batch(("library-discover", Library, Array.Empty<string>()));
        using var changedJson = JsonDocument.Parse(changed.Single().Output);
        changedJson.RootElement.GetProperty("rejected").GetInt32().Should().Be(1);
        changedJson.RootElement.GetProperty("ids").GetArrayLength().Should().Be(4); File.ReadAllBytes(Library).Should().Equal(catalog);
    }

    [Fact]
    public async Task IndependentProcessesCannotReplaceDamagedPreferences()
    {
        var package = Packages(1); const string damaged = "{damaged preferences";
        File.WriteAllText(Favorites, damaged); File.WriteAllText(Library, damaged);
        var results = await Batch(("favorite-write", Favorites, new[] { Id(100) }), ("library-write", Library, new[] { package.Packages[0], package.Key }));
        foreach (var result in results) { result.Code.Should().Be(2); result.Output.Should().Contain("Invalid"); }
        File.ReadAllText(Favorites).Should().Be(damaged); File.ReadAllText(Library).Should().Be(damaged); Directory.GetFiles(_root, "*.tmp").Should().BeEmpty();
    }

    [Fact]
    public async Task AWorkerTerminatedWhileHoldingTheActualPreferenceLockDoesNotBlockFutureWrites()
    {
        SeedFavorites(new[] { ProjectTemplates.ConsoleId });
        var ready = Path.Combine(_root, "lock.ready"); using var holder = _fixture.Start("hold-lock", Favorites, Path.Combine(_root, "unused.gate"), ready);
        var wait = Stopwatch.StartNew();
        while (!File.Exists(ready)) { holder.Exited.Should().BeFalse(); wait.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(30)); await Task.Delay(20); }
        await holder.StopAsync();
        new TemplateFavoritesStore(Favorites).Read().Ids.Should().Equal(ProjectTemplates.ConsoleId);
        var result = await Batch(("favorite-write", Favorites, new[] { Id(100) })); result.Single().Code.Should().Be(0, result.Single().Output + result.Single().Error);
        new TemplateFavoritesStore(Favorites).Read().Ids.Should().BeEquivalentTo(new[] { ProjectTemplates.ConsoleId, Id(100) });
    }
}
