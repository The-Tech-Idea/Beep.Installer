using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Runtime.ExceptionServices;
using System.Windows.Forms;
using Beep.Installer.Engine;
using Beep.Installer.Forms;
using Beep.Installer.Lang;
using FluentAssertions;
using Xunit;

namespace Beep.Installer.Tests;

[Collection("Language")]
public sealed class TemplateGalleryTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "BeepGallery-" + Guid.NewGuid().ToString("N"));
    public TemplateGalleryTests() => Directory.CreateDirectory(_root);
    public void Dispose() { foreach (var file in Directory.EnumerateFiles(_root)) File.Delete(file); Directory.Delete(_root); }
    private TemplateFavoritesStore Store() => new(Path.Combine(_root, "favorites.json"));
    private TemplateLibraryStore Library() => new(Path.Combine(_root, "library.json"));
    private static void Sta(Action action)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            var language = LanguageManager.CurrentCulture.TwoLetterISOLanguageName;
            try { LanguageManager.SetLanguage("en"); action(); }
            catch (Exception ex) { failure = ex; }
            finally { LanguageManager.SetLanguage(language); }
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA); thread.Start(); thread.Join(TimeSpan.FromSeconds(45)).Should().BeTrue();
        if (failure is not null) ExceptionDispatchInfo.Capture(failure).Throw();
    }
    private static T Control<T>(System.Windows.Forms.Control owner, string name) where T : System.Windows.Forms.Control => owner.Controls.Find(name, true).OfType<T>().Single();
    private static T Field<T>(object owner, string name) => (T)owner.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(owner)!;
    private static IEnumerable<System.Windows.Forms.Control> Descendants(System.Windows.Forms.Control root) => root.Controls.Cast<System.Windows.Forms.Control>().SelectMany(child => new[] { child }.Concat(Descendants(child)));
    private string SignedPackage()
    {
        using var rsa = RSA.Create(2048); var privateKey = Path.Combine(_root, "private.pem"); var publicKey = Path.Combine(_root, "public.pem");
        File.WriteAllText(privateKey, rsa.ExportPkcs8PrivateKeyPem()); File.WriteAllText(publicKey, rsa.ExportSubjectPublicKeyInfoPem());
        ProjectTemplatePackageService.Export(new() { TemplateId = ProjectTemplates.ConsoleId, ProductName = "Captured product", Publisher = "Captured publisher",
            OutputDirectory = _root, SigningPrivateKeyPath = privateKey }).Success.Should().BeTrue();
        var project = ProjectTemplates.Create(ProjectTemplates.ConsoleId, "Captured product", "1.0.0", "Captured publisher", "");
        project.Components.Single().Name = "Captured component"; project.CodeSignCertificatePassword = "signed-secret";
        var projectPath = Path.Combine(_root, ProjectTemplatePackageService.ProjectFileName); File.WriteAllText(projectPath, ProjectCanonicalJsonExporter.ToJson(project), new UTF8Encoding(false));
        var manifestPath = Path.Combine(_root, ProjectTemplatePackageService.ManifestFileName); var manifest = JsonNode.Parse(File.ReadAllText(manifestPath))!.AsObject();
        manifest["ProjectSha256"] = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(projectPath))).ToLowerInvariant();
        var text = manifest.ToJsonString(); File.WriteAllText(manifestPath, text, new UTF8Encoding(false));
        File.WriteAllText(Path.Combine(_root, ProjectTemplatePackageService.SignatureFileName), "rsa-sha256:" + Convert.ToBase64String(rsa.SignData(Encoding.UTF8.GetBytes(text), HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1)));
        return publicKey;
    }
    private static T Pump<T>(Task<T> task)
    {
        var deadline = DateTime.UtcNow.AddSeconds(15);
        while (!task.IsCompleted && DateTime.UtcNow < deadline) { Application.DoEvents(); Thread.Sleep(5); }
        task.IsCompleted.Should().BeTrue(); return task.GetAwaiter().GetResult();
    }

    [Fact]
    public void SignedImportReviewsActualProtectedContentAndPersistsOnlyFavoriteIdentifier()
    {
        var key = SignedPackage();
        Sta(() =>
        {
            using var dialog = new TemplateGalleryDialog(store: Store(), library: Library()); dialog.Show();
            Control<TextBox>(dialog, "TemplateSearch").Text = "no-match"; Control<CheckBox>(dialog, "TemplateFavoritesOnly").Checked = true;
            Pump(dialog.ImportFromAsync(_root, key)).Error.Should().Be(TemplateImportError.None);
            var descriptor = dialog.SelectedTemplate!; descriptor.Blueprint.Should().NotBeNull(); dialog.SelectedTemplateId.Should().StartWith("signed:");
            Control<ListView>(dialog, "TemplateGallery").Items.Count.Should().Be(6);
            Control<TextBox>(dialog, "TemplateSample").Text.Should().Contain("Captured product").And.Contain("Captured component").And.Contain("<protected>").And.NotContain("signed-secret")
                .And.NotContain(LanguageManager.GetString("Gallery_Limits"));
            Control<Button>(dialog, "TemplateFavoriteToggle").PerformClick(); Store().Read().Ids.Should().Contain(descriptor.Id);
            File.ReadAllText(Path.Combine(_root, "favorites.json")).Should().NotContain("public.pem").And.NotContain("signed-secret").And.NotContain("Captured product");
            LanguageManager.SetLanguage("ar"); dialog.SelectedTemplateId.Should().Be(descriptor.Id); descriptor.Blueprint!.CreateProject().Components.Single().Name.Should().Be("Captured component");
        });
    }

    [Fact]
    public void FailedImportAndPickerCancellationPreserveTheExistingSelection()
    {
        var key = SignedPackage(); File.AppendAllText(Path.Combine(_root, ProjectTemplatePackageService.ProjectFileName), " ");
        Sta(() =>
        {
            using var dialog = new TemplateGalleryDialog(ProjectTemplates.WpfId, store: Store(), library: Library()); dialog.Show();
            var details = Control<TextBox>(dialog, "TemplateSample").Text;
            Pump(dialog.ImportFromAsync(_root, key)).Error.Should().Be(TemplateImportError.Untrusted);
            dialog.SelectedTemplateId.Should().Be(ProjectTemplates.WpfId); Control<TextBox>(dialog, "TemplateSample").Text.Should().Be(details);
            Control<ListView>(dialog, "TemplateGallery").Items.Count.Should().Be(5); Control<Button>(dialog, "TemplateSelect").Enabled.Should().BeTrue();
            dialog.ChooseImportPaths = _ => null; Control<Button>(dialog, "TemplateImport").PerformClick(); dialog.PendingImport.Should().BeNull();
            LanguageManager.SetLanguage("de"); dialog.SelectedTemplateId.Should().Be(ProjectTemplates.WpfId);
        });
    }

    [Fact]
    public void ActualCreationGalleryReturnsVerifiedContentAndPreservesDraftIdentityFields()
    {
        var key = SignedPackage();
        Sta(() =>
        {
            using var parent = new ProjectNewDialog(); parent.Show(); Field<TextBox>(parent, "_nameBox").Text = "Author product";
            Exception? failure = null; bool seen = false;
            using var timer = new System.Windows.Forms.Timer { Interval = 40 };
            timer.Tick += (_, _) =>
            {
                var gallery = Application.OpenForms.OfType<TemplateGalleryDialog>().SingleOrDefault(); if (gallery is null) return; timer.Stop(); seen = true;
                try { gallery.LibraryStore = Library(); gallery.ChooseImportPaths = _ => (_root, key); Control<Button>(gallery, "TemplateImport").PerformClick(); Pump(gallery.PendingImport!).Error.Should().Be(TemplateImportError.None); Control<Button>(gallery, "TemplateSelect").PerformClick(); }
                catch (Exception ex) { failure = ex; gallery.Close(); }
            };
            timer.Start(); Control<Button>(parent, "BrowseTemplateGallery").PerformClick(); seen.Should().BeTrue();
            if (failure is not null) ExceptionDispatchInfo.Capture(failure).Throw(); parent.ProductName.Should().Be("Author product"); parent.SelectedTemplate.Blueprint.Should().NotBeNull();
            var controller = new InstallerController(); controller.StopAutoSave();
            controller.New(parent.SelectedTemplate, parent.ProductName, parent.Version, parent.Publisher, parent.SourceDirectory);
            controller.Project.AppName.Should().Be("Author product"); controller.Project.Components.Single().Name.Should().Be("Captured component");
            controller.Project.AppId.Should().NotBe(parent.SelectedTemplate.Blueprint!.CreateProject().AppId); controller.Project.MarkClean();
        });
    }

    [Fact]
    public void VerifiedTemplateUpdatesPreserveIdentityAndSigningAndUseExistingUndo()
    {
        var key = SignedPackage(); var blueprint = VerifiedTemplateImporter.Import(_root, key).Blueprint!;
        var descriptor = new ProjectTemplate { Id = blueprint.Id, Name = blueprint.Name, Blueprint = blueprint };
        var controller = new InstallerController(); controller.StopAutoSave(); controller.New("Existing", "2.0.0", "Author", "");
        var id = controller.Project.AppId; controller.Project.CodeSignCertificatePassword = "author-secret";
        controller.Project.OutputBaseFilename = "Author-2.0.0"; controller.CommitAuthoringChanges();
        var preview = controller.PreviewTemplateUpdate(descriptor); preview.TemplateId.Should().Be(blueprint.Id);
        controller.Project.Components.Should().BeEmpty(); preview.Diff.Should().Contain(change => change.Path.Contains("components"));
        preview.Diff.Should().NotContain(change => change.CurrentValue.Contains("author-secret") || change.UpdatedValue.Contains("signed-secret"));
        controller.ApplyTemplateUpdate(descriptor); controller.Project.AppId.Should().Be(id); controller.Project.AppName.Should().Be("Existing");
        controller.Project.Components.Single().Name.Should().Be("Captured component"); controller.Project.CodeSignCertificatePassword.Should().Be("author-secret");
        controller.Project.OutputBaseFilename.Should().Be("Author-2.0.0"); controller.Undo(); controller.Project.Components.Should().BeEmpty();
        controller.Project.AppId.Should().Be(id); controller.Project.MarkClean();
    }

    [Fact]
    public void GuidedLanguageChangeRetainsVerifiedTemplateAndCreatesItsActualGraph()
    {
        var key = SignedPackage(); var blueprint = VerifiedTemplateImporter.Import(_root, key).Blueprint!;
        Sta(() =>
        {
            var controller = new InstallerController(); controller.StopAutoSave(); using var advanced = new PackageBuilderForm(controller);
            using var guided = new BuilderWizardForm(controller, advanced, startNew: true); guided.Show();
            var descriptor = new ProjectTemplate { Id = blueprint.Id, Name = blueprint.Name, Blueprint = blueprint };
            var selector = Field<ComboBox>(guided, "_templateBox"); selector.Items.Add(descriptor); selector.SelectedItem = descriptor;
            Field<TextBox>(guided, "_nameBox").Text = "Guided author"; LanguageManager.SetLanguage("ar"); Application.DoEvents();
            ((ProjectTemplate)Field<ComboBox>(guided, "_templateBox").SelectedItem!).Blueprint.Should().BeSameAs(blueprint);
            ((bool)guided.GetType().GetMethod("TryCreateProject", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(guided, null)!).Should().BeTrue();
            controller.Project.AppName.Should().Be("Guided author"); controller.Project.Components.Single().Name.Should().Be("Captured component"); controller.Project.MarkClean();
        });
    }

    [Fact]
    public void ClosingGalleryDuringImportPreventsLateSelectionPublication()
    {
        var key = SignedPackage(); Sta(() =>
        {
            var dialog = new TemplateGalleryDialog(ProjectTemplates.WpfId, store: Store(), library: Library()); dialog.Show();
            Pump(dialog.PendingDiscovery!);
            var import = dialog.ImportFromAsync(_root, key); Control<Button>(dialog, "TemplateSelect").Enabled.Should().BeFalse();
            Pump(dialog.ImportFromAsync(_root, key)).Blueprint.Should().BeNull(); dialog.Dispose();
            Pump(import).Error.Should().Be(TemplateImportError.None); dialog.SelectedTemplateId.Should().Be(ProjectTemplates.WpfId);
        });
    }

    [Fact]
    public void ActualUpdateGalleryRoutesVerifiedSelectionThroughTheSignedPreviewFactory()
    {
        var key = SignedPackage(); Sta(() =>
        {
            var controller = new InstallerController(); controller.StopAutoSave(); var originalId = controller.Project.AppId;
            var signedCalls = 0;
            using var dialog = new TemplateUpdateDialog(ProjectTemplates.Builtins, controller.PreviewTemplateUpdate,
                template => { signedCalls++; return controller.PreviewTemplateUpdate(template); }); dialog.Show();
            Exception? failure = null; using var timer = new System.Windows.Forms.Timer { Interval = 40 };
            timer.Tick += (_, _) =>
            {
                var gallery = Application.OpenForms.OfType<TemplateGalleryDialog>().SingleOrDefault(); if (gallery is null) return; timer.Stop();
                try { gallery.LibraryStore = Library(); Pump(gallery.ImportFromAsync(_root, key)).Error.Should().Be(TemplateImportError.None); Control<Button>(gallery, "TemplateSelect").PerformClick(); }
                catch (Exception ex) { failure = ex; gallery.Close(); }
            };
            timer.Start(); Control<Button>(dialog, "BrowseTemplateGallery").PerformClick();
            if (failure is not null) ExceptionDispatchInfo.Capture(failure).Throw();
            signedCalls.Should().Be(1); dialog.SelectedTemplate.Blueprint.Should().NotBeNull(); dialog.Preview!.TemplateId.Should().Be(dialog.SelectedTemplateId);
            dialog.Preview.Diff.Should().Contain(change => change.UpdatedValue.Contains("Captured component"));
            dialog.Preview.Diff.Should().NotContain(change => change.UpdatedValue.Contains("signed-secret"));
            controller.Project.AppId.Should().Be(originalId); controller.Project.Components.Should().BeEmpty();
            dialog.Close(); controller.Project.MarkClean();
        });
    }

    [Theory]
    [InlineData("de")] [InlineData("ar")]
    public void SignedReviewFitsTheMinimumScaledLayoutWithUnavailableFavorites(string culture)
    {
        var key = SignedPackage(); File.WriteAllText(Path.Combine(_root, "favorites.json"), "keep invalid data");
        Sta(() =>
        {
            LanguageManager.SetLanguage(culture); using var dialog = new TemplateGalleryDialog(store: Store(), library: Library()); dialog.Size = dialog.MinimumSize;
            dialog.Show(); Pump(dialog.ImportFromAsync(_root, key)).Error.Should().Be(TemplateImportError.None);
            dialog.Scale(new System.Drawing.SizeF(2, 2)); dialog.PerformLayout(); Application.DoEvents();
            var controls = Descendants(dialog).Where(control => control.Visible && control is TextBox or ComboBox or CheckBox or ListView or Button or Label).ToArray();
            var bounds = controls.Select(control => dialog.RectangleToClient(control.RectangleToScreen(control.ClientRectangle))).ToArray();
            foreach (var rectangle in bounds) dialog.ClientRectangle.Contains(rectangle).Should().BeTrue();
            for (var i = 0; i < bounds.Length; i++) for (var j = i + 1; j < bounds.Length; j++) bounds[i].IntersectsWith(bounds[j]).Should().BeFalse();
            Control<TextBox>(dialog, "TemplateSample").Text.Should().Contain("Captured component").And.Contain(LanguageManager.GetString("Gallery_SignedLimits"));
            Control<Button>(dialog, "TemplateSelect").Enabled.Should().BeTrue(); Control<Button>(dialog, "TemplateFavoriteToggle").Enabled.Should().BeFalse();
        });
    }

    [Fact]
    public void FreshGalleryDiscoversOnlyUnchangedPinnedPackagesAndFavorites()
    {
        var key = SignedPackage();
        Sta(() =>
        {
            string id;
            using (var first = new TemplateGalleryDialog(store: Store(), library: Library()))
            { first.Show(); Pump(first.ImportFromAsync(_root, key)); id = first.SelectedTemplateId; Control<Button>(first, "TemplateFavoriteToggle").PerformClick(); }
            using (var fresh = new TemplateGalleryDialog(store: Store(), library: Library()))
            {
                fresh.Show(); Pump(fresh.PendingDiscovery!).Templates.Should().ContainSingle();
                Control<CheckBox>(fresh, "TemplateFavoritesOnly").Checked = true;
                Control<ListView>(fresh, "TemplateGallery").Items.Count.Should().Be(1); fresh.SelectedTemplateId.Should().Be(id);
                Control<TextBox>(fresh, "TemplateSample").Text.Should().Contain("Captured component");
            }
            File.AppendAllText(Path.Combine(_root, ProjectTemplatePackageService.ProjectFileName), " ");
            using var changed = new TemplateGalleryDialog(store: Store(), library: Library()); changed.Show();
            Pump(changed.PendingDiscovery!).RejectedEntries.Should().Be(1); Control<ListView>(changed, "TemplateGallery").Items.Count.Should().Be(5);
            changed.SelectedTemplateId.Should().Be(ProjectTemplates.EmptyId);
            Descendants(changed).OfType<Label>().Should().Contain(label => label.Text == LanguageManager.GetString("Gallery_LibraryWarning"));
        });
    }

    [Fact]
    public void UnreadableLibraryPreservesDataWithoutBlockingAReviewedImport()
    {
        var key = SignedPackage(); File.WriteAllText(Path.Combine(_root, "library.json"), "keep invalid library");
        Sta(() =>
        {
            using var dialog = new TemplateGalleryDialog(store: Store(), library: Library()); dialog.Show();
            Pump(dialog.PendingDiscovery!).Error.Should().Be(TemplateLibraryError.Invalid);
            Pump(dialog.ImportFromAsync(_root, key)).Error.Should().Be(TemplateImportError.None);
            dialog.SelectedTemplate!.Blueprint.Should().NotBeNull(); Control<Button>(dialog, "TemplateSelect").Enabled.Should().BeTrue();
            Descendants(dialog).OfType<Label>().Should().Contain(label => label.Text == LanguageManager.GetString("Gallery_LibraryWarning"));
        });
        File.ReadAllText(Path.Combine(_root, "library.json")).Should().Be("keep invalid library");
    }

    [Fact]
    public void ClosingDuringSavedDiscoveryPreventsLateTemplatePublication()
    {
        var key = SignedPackage(); var blueprint = VerifiedTemplateImporter.Import(_root, key).Blueprint!;
        Library().Remember(_root, key, blueprint).Should().Be(TemplateLibraryError.None);
        Sta(() =>
        {
            var dialog = new TemplateGalleryDialog(store: Store(), library: Library()); dialog.Show(); var discovery = dialog.PendingDiscovery!; dialog.Dispose();
            Pump(discovery).Templates.Should().ContainSingle(); dialog.SelectedTemplateId.Should().Be(ProjectTemplates.EmptyId);
        });
    }

    [Fact]
    public void AllGalleryCaptionsArePackagedWithMatchingFormatArgumentsInEveryLanguage()
    {
        Dictionary<string, string> Read(string culture)
        {
            using var stream = typeof(LanguageManager).Assembly.GetManifestResourceStream($"Beep.Installer.Lang.Strings_{culture}.resources");
            using var reader = new System.Resources.ResourceReader(stream!);
            return reader.Cast<System.Collections.DictionaryEntry>().Where(entry => ((string)entry.Key).StartsWith("Gallery_", StringComparison.Ordinal))
                .ToDictionary(entry => (string)entry.Key, entry => (string)entry.Value!);
        }
        var english = Read("en"); english.Should().HaveCount(43);
        foreach (var culture in LanguageManager.SupportedCultures)
        {
            var translated = Read(culture); translated.Keys.Should().BeEquivalentTo(english.Keys);
            foreach (var key in english.Keys)
            {
                translated[key].Should().NotBeNullOrWhiteSpace();
                System.Text.CompositeFormat.Parse(translated[key]).MinimumArgumentCount.Should().Be(System.Text.CompositeFormat.Parse(english[key]).MinimumArgumentCount);
            }
        }
    }

    [Fact]
    public void LocalizedSearchThemeAndLauncherCaptionsKeepSelectionAndFieldValues()
    {
        Sta(() =>
        {
            using var dialog = new TemplateGalleryDialog(ProjectTemplates.ServiceId, store: Store(), library: Library()); dialog.Show();
            using var creation = new ProjectNewDialog(); creation.Show(); Field<TextBox>(creation, "_nameBox").Text = "draft product";
            foreach (var culture in LanguageManager.SupportedCultures)
            {
                LanguageManager.SetLanguage(culture); Control<TextBox>(dialog, "TemplateSearch").Text = LanguageManager.GetString("Gallery_Name_service");
                Control<ListView>(dialog, "TemplateGallery").Items.Count.Should().Be(1); dialog.SelectedTemplateId.Should().Be(ProjectTemplates.ServiceId);
                Control<TextBox>(dialog, "TemplateSample").Text.Should().Contain(LanguageManager.GetString("Gallery_Description_service"))
                    .And.Contain(LanguageManager.GetString("Gallery_Location_Startup")).And.Contain("servicehost.exe");
                Control<Button>(creation, "BrowseTemplateGallery").Text.Should().Be(LanguageManager.GetString("Gallery_Title"));
                Field<TextBox>(creation, "_nameBox").Text.Should().Be("draft product");
            }
            var before = Control<TextBox>(dialog, "TemplateSample").Text;
            dialog.BackColor = System.Drawing.Color.Magenta;
            TheTechIdea.Beep.Winform.Controls.ThemeManagement.BeepThemesManager.CurrentThemeName = TheTechIdea.Beep.Winform.Controls.ThemeManagement.BeepThemesManager.CurrentThemeName;
            dialog.BackColor.Should().Be(Beep.Installer.Ui.InstallerTheme.Panel); Control<TextBox>(dialog, "TemplateSample").Text.Should().Be(before);
            dialog.Dispose(); creation.Dispose(); Action language = () => LanguageManager.SetLanguage("en"); language.Should().NotThrow();
        });
    }

    [Theory]
    [InlineData("de", 1f)] [InlineData("de", 1.25f)] [InlineData("de", 1.5f)] [InlineData("de", 2f)]
    [InlineData("ar", 1f)] [InlineData("ar", 1.25f)] [InlineData("ar", 1.5f)] [InlineData("ar", 2f)]
    public void PopulatedGalleryRemainsUsableAtMinimumSizeAndSupportedScales(string culture, float scale)
    {
        File.WriteAllText(Path.Combine(_root, "favorites.json"), "keep invalid data");
        Sta(() =>
        {
            LanguageManager.SetLanguage(culture); using var dialog = new TemplateGalleryDialog(ProjectTemplates.ServiceId, store: Store(), library: Library());
            dialog.Size = dialog.MinimumSize; dialog.Show(); dialog.Scale(new System.Drawing.SizeF(scale, scale)); dialog.PerformLayout(); Application.DoEvents();
            var controls = Descendants(dialog).Where(control => control.Visible && control is TextBox or ComboBox or CheckBox or ListView or Button or Label).ToArray();
            foreach (var control in controls)
            {
                var actual = dialog.RectangleToClient(control.RectangleToScreen(control.ClientRectangle));
                dialog.ClientRectangle.Contains(actual).Should().BeTrue($"{culture}/{scale} {control.AccessibleName}: {actual} inside {dialog.ClientRectangle}");
            }
            for (var i = 0; i < controls.Length; i++) for (var j = i + 1; j < controls.Length; j++)
            {
                var a = dialog.RectangleToClient(controls[i].RectangleToScreen(controls[i].ClientRectangle));
                var b = dialog.RectangleToClient(controls[j].RectangleToScreen(controls[j].ClientRectangle));
                a.IntersectsWith(b).Should().BeFalse($"{culture}/{scale} {controls[i].Name} overlaps {controls[j].Name}");
            }
            Control<TextBox>(dialog, "TemplateSample").Height.Should().BeGreaterThan(80);
            Control<TextBox>(dialog, "TemplateSample").ScrollBars.Should().Be(ScrollBars.Vertical);
            Control<ListView>(dialog, "TemplateGallery").Items.Count.Should().Be(5);
            Control<Button>(dialog, "TemplateSelect").Enabled.Should().BeTrue();
        });
    }

    [Fact]
    public void SamplesMatchTheActualFactoryAndDetachedMetadataDoesNotChangeWithTheSource()
    {
        var templates = ProjectTemplates.Builtins.ToArray(); var entries = TemplateGallery.Describe(templates);
        foreach (var entry in entries)
        {
            var actual = ProjectTemplates.Create(entry.Id, "SampleApp", "1.0.0", "Sample publisher", "");
            entry.ComponentCount.Should().Be(actual.Components.Count);
            entry.SampleShortcuts.Should().Equal(actual.Shortcuts.Select(shortcut => $"{shortcut.Location}: {shortcut.TargetPath}"));
        }
        entries.Single(entry => entry.Id == ProjectTemplates.ServiceId).SampleShortcuts.Should().ContainSingle().Which.Should().StartWith("Startup:");
        templates[0].Name = "changed"; entries[0].Name.Should().NotBe("changed");
        var foreign = TemplateGallery.Describe(new[] { new ProjectTemplate { Id = "foreign", Name = "Foreign" } }).Single();
        foreign.HasSample.Should().BeFalse();
    }

    [Fact]
    public void FavoritesPersistOnlyIdsAndRoundTripAcrossFreshStoresWithoutLosingOtherIds()
    {
        var store = Store(); store.Read().Available.Should().BeTrue();
        store.SetFavorite(ProjectTemplates.ConsoleId, true).Available.Should().BeTrue();
        Store().SetFavorite(ProjectTemplates.WpfId, true).Available.Should().BeTrue();
        Store().Read().Ids.Should().BeEquivalentTo(ProjectTemplates.ConsoleId, ProjectTemplates.WpfId);
        Store().SetFavorite(ProjectTemplates.ConsoleId, false).Ids.Should().Equal(ProjectTemplates.WpfId);
        var content = File.ReadAllText(Path.Combine(_root, "favorites.json")); content.Should().NotContain("password").And.NotContain("SourceDirectory");
    }

    [Theory]
    [InlineData("bad json")]
    [InlineData("{\"schemaVersion\":2,\"favoriteIds\":[]}")]
    [InlineData("{\"schemaVersion\":1,\"favoriteIds\":[],\"futureData\":\"keep\"}")]
    [InlineData("{\"schemaVersion\":1,\"favoriteIds\":[\"console\",\"CONSOLE\"]}")]
    public void InvalidFavoritesArePreservedAndCannotBeOverwritten(string original)
    {
        var path = Path.Combine(_root, "favorites.json"); File.WriteAllText(path, original);
        Store().Read().Available.Should().BeFalse(); Store().SetFavorite(ProjectTemplates.WpfId, true).Available.Should().BeFalse();
        File.ReadAllText(path).Should().Be(original);
    }

    [Fact]
    public void OversizedAndLockedFavoritesArePreservedAndFailedWritesRemoveTheirTemporaryFile()
    {
        var path = Path.Combine(_root, "favorites.json"); var oversized = new string('x', 64 * 1024 + 1); File.WriteAllText(path, oversized);
        Store().SetFavorite(ProjectTemplates.WpfId, true).Available.Should().BeFalse(); File.ReadAllText(path).Should().Be(oversized);
        File.Delete(path); Store().SetFavorite(ProjectTemplates.ConsoleId, true).Available.Should().BeTrue(); var original = File.ReadAllText(path);
        using (var held = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            Store().SetFavorite(ProjectTemplates.WpfId, true).Available.Should().BeFalse();
        }
        File.ReadAllText(path).Should().Be(original); Directory.EnumerateFiles(_root, "*.tmp").Should().BeEmpty();
    }

    [Fact]
    public async Task IndependentStoreInstancesDoNotLoseConcurrentFavoriteChanges()
    {
        await Task.WhenAll(ProjectTemplates.Builtins.Select(template => Task.Run(() => Store().SetFavorite(template.Id, true))));
        Store().Read().Ids.Should().BeEquivalentTo(ProjectTemplates.Builtins.Select(template => template.Id));
    }

    [Fact]
    public void SearchCategoriesSamplesAndFavoritesUseTheSameSelectedTemplate()
    {
        Sta(() =>
        {
            using var dialog = new TemplateGalleryDialog(ProjectTemplates.ServiceId, store: Store(), library: Library()); dialog.Show(); Application.DoEvents();
            var list = Control<ListView>(dialog, "TemplateGallery"); var details = Control<TextBox>(dialog, "TemplateSample");
            details.Text.Should().Contain("servicehost.exe").And.Contain("Startup:").And.Contain("configure service registration separately");
            Control<Button>(dialog, "TemplateFavoriteToggle").PerformClick(); Store().Read().Ids.Should().Equal(ProjectTemplates.ServiceId);
            Control<CheckBox>(dialog, "TemplateFavoritesOnly").Checked = true; list.Items.Count.Should().Be(1);
            Control<TextBox>(dialog, "TemplateSearch").Text = "no-match"; list.Items.Count.Should().Be(0); Control<Button>(dialog, "TemplateSelect").Enabled.Should().BeFalse();
            Control<TextBox>(dialog, "TemplateSearch").Clear(); Control<CheckBox>(dialog, "TemplateFavoritesOnly").Checked = false;
            var category = Control<ComboBox>(dialog, "TemplateCategory"); category.SelectedIndex = 2;
            list.Items.Count.Should().Be(3); Control<TextBox>(dialog, "TemplateSearch").Text = "app.dll";
            list.Items.Count.Should().Be(1); dialog.SelectedTemplateId.Should().Be(ProjectTemplates.WpfId);
        });
    }

    [Fact]
    public void LanguageThemeAndInvalidFavoritesDoNotChangeDraftFieldsOrBlockSelection()
    {
        File.WriteAllText(Path.Combine(_root, "favorites.json"), "keep invalid data");
        Sta(() =>
        {
            using var dialog = new TemplateGalleryDialog(ProjectTemplates.ConsoleId, store: Store(), library: Library()); dialog.Show();
            Control<Button>(dialog, "TemplateFavoriteToggle").Enabled.Should().BeFalse(); Control<Button>(dialog, "TemplateSelect").Enabled.Should().BeTrue();
            Control<TextBox>(dialog, "TemplateSearch").Text = "console"; LanguageManager.SetLanguage("ar");
            Control<TextBox>(dialog, "TemplateSearch").Text.Should().Be("console"); dialog.SelectedTemplateId.Should().Be(ProjectTemplates.ConsoleId);
            Control<TextBox>(dialog, "TemplateSample").RightToLeft.Should().Be(RightToLeft.Yes);
            dialog.Dispose(); Action language = () => LanguageManager.SetLanguage("de"); language.Should().NotThrow();
        });
        File.ReadAllText(Path.Combine(_root, "favorites.json")).Should().Be("keep invalid data");
    }

    [Fact]
    public void CreationGallerySelectsIntoTheExistingComboAndPreservesUncommittedFields()
    {
        Sta(() =>
        {
            using var parent = new ProjectNewDialog(); parent.Show(); Field<TextBox>(parent, "_nameBox").Text = "uncommitted name";
            bool seen = false; Exception? failure = null;
            using var timer = new System.Windows.Forms.Timer { Interval = 50 };
            timer.Tick += (_, _) =>
            {
                var gallery = Application.OpenForms.OfType<TemplateGalleryDialog>().SingleOrDefault(); if (gallery is null) return;
                timer.Stop(); seen = true;
                try { Control<TextBox>(gallery, "TemplateSearch").Text = "app.dll"; Application.DoEvents(); Control<Button>(gallery, "TemplateSelect").PerformClick(); }
                catch (Exception ex) { failure = ex; gallery.Close(); }
            };
            timer.Start(); Control<Button>(parent, "BrowseTemplateGallery").PerformClick(); seen.Should().BeTrue();
            if (failure is not null) ExceptionDispatchInfo.Capture(failure).Throw();
            parent.TemplateId.Should().Be(ProjectTemplates.WpfId); parent.ProductName.Should().Be("uncommitted name");
        });
    }

    [Fact]
    public void GuidedLanguageRebuildPreservesTheTemplateIdentifierAndUncreatedDraft()
    {
        Sta(() =>
        {
            var controller = new InstallerController(); controller.StopAutoSave();
            using var advanced = new PackageBuilderForm(controller); using var guided = new BuilderWizardForm(controller, advanced, startNew: true); guided.Show();
            var selector = Field<ComboBox>(guided, "_templateBox"); selector.SelectedItem = selector.Items.Cast<ProjectTemplate>().Single(template => template.Id == ProjectTemplates.WpfId);
            Field<TextBox>(guided, "_nameBox").Text = "draft identity";
            LanguageManager.SetLanguage("ar"); Application.DoEvents();
            ((ProjectTemplate)Field<ComboBox>(guided, "_templateBox").SelectedItem!).Id.Should().Be(ProjectTemplates.WpfId);
            Field<TextBox>(guided, "_nameBox").Text.Should().Be("draft identity"); Field<bool>(guided, "_projectCreated").Should().BeFalse();
            controller.Project.MarkClean();
        });
    }
}
