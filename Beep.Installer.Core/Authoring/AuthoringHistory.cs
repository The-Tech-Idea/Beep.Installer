using System.Collections;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Beep.Installer.Models;

namespace Beep.Installer.Engine;

public sealed record AuthoringHistoryRestore(InstallProject Project, int MissingCredentials);

/// <summary>Bounded authoring transactions. Snapshots contain configuration, never plaintext credentials.</summary>
public sealed class AuthoringHistory
{
    private sealed record Identity(Guid Id);
    private sealed record Slot(Guid Owner, string Member);
    private sealed record Snapshot(string Json, Dictionary<string, Guid> Nodes, HashSet<Slot> Redacted, Dictionary<Slot, string> Contexts);
    private sealed record Change(string Label, Snapshot Before, Snapshot After);
    private ConditionalWeakTable<object, Identity> _identities = new();
    private readonly List<Change> _undo = new(), _redo = new();
    private readonly HashSet<Slot> _sensitive = new();
    private Snapshot? _current;
    private readonly int _capacity;
    private readonly JsonSerializerOptions _options;
    private bool _scrubbing;
    public bool CanUndo => _undo.Count > 0;
    public bool CanRedo => _redo.Count > 0;
    public string? UndoLabel => _undo.LastOrDefault()?.Label;
    public string? RedoLabel => _redo.LastOrDefault()?.Label;

    public AuthoringHistory(int capacity = 100)
    {
        if (capacity < 1) throw new ArgumentOutOfRangeException(nameof(capacity));
        _capacity = capacity;
        _options = CreateOptions(IsSensitive);
    }

    public void Reset(InstallProject project)
    {
        _identities = new(); _sensitive.Clear(); _undo.Clear(); _redo.Clear(); _current = null; _current = Capture(project);
    }

    public bool Record(InstallProject project, string label)
    {
        var next = Capture(project);
        if (_current is null) { _current = next; return false; }
        if (next.Json == _current.Json) { _current = next; return false; }
        _undo.Add(new(label, _current, next));
        if (_undo.Count > _capacity) _undo.RemoveAt(0);
        _redo.Clear(); _current = next; return true;
    }

    public AuthoringHistoryRestore? Undo(InstallProject current)
    {
        if (!CanUndo) return null;
        // Sensitivity can be declared in a live editor before the next transaction is committed.
        Capture(current);
        var change = _undo[^1]; var restored = Restore(change.Before, current);
        var snapshot = Capture(restored.Project);
        _undo.RemoveAt(_undo.Count - 1); _redo.Add(change); _current = snapshot; return restored;
    }

    public AuthoringHistoryRestore? Redo(InstallProject current)
    {
        if (!CanRedo) return null;
        Capture(current);
        var change = _redo[^1]; var restored = Restore(change.After, current);
        var snapshot = Capture(restored.Project);
        _redo.RemoveAt(_redo.Count - 1); _undo.Add(change); _current = snapshot; return restored;
    }

    /// <summary>Carry identity across a known graph replacement, without retaining the previous graph.</summary>
    public void AdoptReplacement(InstallProject before, InstallProject after)
    {
        var candidates = new Dictionary<string, List<object>>(StringComparer.Ordinal);
        Walk(before, (owner, _) =>
        {
            if (ObjectKey(owner) is not string key) return;
            if (!candidates.TryGetValue(key, out var objects)) candidates[key] = objects = new();
            objects.Add(owner);
        }, (_, _, _, _, _) => { });
        Walk(after, (owner, _) =>
        {
            if (ObjectKey(owner) is not string key || !candidates.TryGetValue(key, out var objects) || objects.Count != 1) return;
            var original = objects[0];
            var identity = Id(original);
            _identities.Remove(owner); _identities.Add(owner, new(identity));
            if (owner is CompiledInstallOperation operation && original is CompiledInstallOperation old && operation.Inputs is not null && old.Inputs is not null)
            { var inputsIdentity = Id(old.Inputs); _identities.Remove(operation.Inputs); _identities.Add(operation.Inputs, new(inputsIdentity)); }
        }, (_, _, _, _, _) => { });
    }

    private static string? ObjectKey(object owner)
    {
        if (owner is InstallProject || owner is IList || owner is IDictionary) return null;
        foreach (var member in new[] { "Id", "Name", "SourcePath", "Clsid", "Extension", "ValueName" })
            if (owner.GetType().GetProperty(member)?.GetValue(owner) is string value && !string.IsNullOrWhiteSpace(value))
                return owner.GetType().FullName + "|" + (owner is CompiledInstallOperation operation ? operation.Type + "|" : "") + value;
        return null;
    }

    private Snapshot Capture(InstallProject project)
    {
        var nodes = new Dictionary<string, Guid>(StringComparer.Ordinal);
        var redacted = new HashSet<Slot>();
        var contexts = new Dictionary<Slot, string>();
        var dictionaryContexts = new Dictionary<object, string>(ReferenceEqualityComparer.Instance);
        var newSensitivity = false;
        Walk(project, (owner, path) =>
        {
            nodes[path] = Id(owner);
            if (owner is CompiledInstallOperation operation && operation.Inputs is not null) dictionaryContexts[operation.Inputs] = ProviderContext(operation);
        }, (owner, member, value, _, sensitive) =>
        {
            var slot = new Slot(Id(owner), member);
            if (sensitive) newSensitivity |= _sensitive.Add(slot);
            if (!IsSensitive(owner, member, value)) return;
            contexts[slot] = dictionaryContexts.TryGetValue(owner, out var context) ? context : CredentialContext(owner, member);
            if (IsPlaintext(value)) redacted.Add(slot);
        });
        var snapshot = new Snapshot(JsonSerializer.Serialize(project, _options), nodes, redacted, contexts);
        if (newSensitivity && !_scrubbing) ScrubHistory();
        return snapshot;
    }

    private void ScrubHistory()
    {
        _scrubbing = true;
        try
        {
            var rewritten = new Dictionary<Snapshot, Snapshot>(ReferenceEqualityComparer.Instance);
            Snapshot Rewrite(Snapshot snapshot)
            {
                if (rewritten.TryGetValue(snapshot, out var existing)) return existing;
                var project = JsonSerializer.Deserialize<InstallProject>(snapshot.Json, _options)!;
                Walk(project, (owner, path) =>
                {
                    if (owner is not InstallProject && snapshot.Nodes.TryGetValue(path, out var id) && !_identities.TryGetValue(owner, out _))
                        _identities.Add(owner, new(id));
                }, (_, _, _, _, _) => { });
                return rewritten[snapshot] = Capture(project);
            }
            if (_current is not null) _current = Rewrite(_current);
            foreach (var stack in new[] { _undo, _redo })
                for (var i = 0; i < stack.Count; i++) stack[i] = stack[i] with { Before = Rewrite(stack[i].Before), After = Rewrite(stack[i].After) };
        }
        finally { _scrubbing = false; }
    }

    private AuthoringHistoryRestore Restore(Snapshot snapshot, InstallProject current)
    {
        var live = new Dictionary<Slot, (string Value, string Context)>();
        var ambiguous = new HashSet<Slot>();
        var dictionaryContexts = new Dictionary<object, string>(ReferenceEqualityComparer.Instance);
        Walk(current, (owner, _) =>
        {
            if (owner is CompiledInstallOperation operation && operation.Inputs is not null) dictionaryContexts[operation.Inputs] = ProviderContext(operation);
        }, (owner, member, value, _, sensitive) =>
        {
            if (!(sensitive || IsSensitive(owner, member, value)) || string.IsNullOrEmpty(value)) return;
            var slot = new Slot(Id(owner), member);
            var context = dictionaryContexts.TryGetValue(owner, out var providerContext) ? providerContext : CredentialContext(owner, member);
            if (live.TryGetValue(slot, out var previous) && (previous.Value != value || previous.Context != context)) ambiguous.Add(slot);
            live[slot] = (value, context);
        });
        var project = JsonSerializer.Deserialize<InstallProject>(snapshot.Json, _options) ?? throw new InvalidDataException("History snapshot could not be restored.");
        var missing = 0;
        Walk(project, (owner, path) =>
        {
            if (owner is InstallProject) return;
            if (!_identities.TryGetValue(owner, out _)) _identities.Add(owner, new(snapshot.Nodes.TryGetValue(path, out var id) ? id : Guid.NewGuid()));
        }, (owner, member, _, write, _) =>
        {
            var slot = new Slot(Id(owner), member);
            var context = snapshot.Contexts.TryGetValue(slot, out var capturedContext) ? capturedContext : CredentialContext(owner, member);
            if (!ambiguous.Contains(slot) && live.TryGetValue(slot, out var value) && value.Context == context && (IsPlaintext(value.Value) || snapshot.Redacted.Contains(slot))) write(value.Value);
            else if (snapshot.Redacted.Contains(slot)) missing++;
        });
        project.ModifiedAt = current.ModifiedAt;
        project.MarkDirty(); return new(project, missing);
    }

    private Guid Id(object owner) => owner is InstallProject ? Guid.Empty : _identities.GetValue(owner, _ => new(Guid.NewGuid())).Id;
    private bool IsSensitive(object owner, string member, string value) => Sensitive(owner, member, value) || _sensitive.Contains(new(Id(owner), member));
    private static string ProviderContext(CompiledInstallOperation operation)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Sensitive(operation, "Type", operation.Type ?? "") ? "<protected>" : operation.Type ?? "")));
    private static string CredentialContext(object owner, string member)
    {
        var values = owner switch
        {
            InstallProject project when member == nameof(InstallProject.CodeSignCertificatePassword) => new[] { project.CodeSignCertificatePath },
            WindowsServiceDefinition service when member == "Password" => new[] { (service.Username ?? "").ToUpperInvariant(), service.Account.ToString() },
            ScheduledTaskDefinition task when member == "Password" => new[] { (task.Username ?? "").ToUpperInvariant() },
            IisAppPoolDefinition pool when member == "Password" => new[] { (pool.Username ?? "").ToUpperInvariant(), (pool.Identity ?? "").ToUpperInvariant() },
            CertificateDefinition certificate when member == "Password" => new[] { certificate.SourcePath },
            _ => Array.Empty<string>()
        };
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(owner.GetType().FullName + "|" + string.Join("|", values.Select(value =>
            Sensitive(owner, "", value ?? "") && IsPlaintext(value ?? "") ? "<protected>" : value)))));
    }
    private static bool IsPlaintext(string value) => !string.IsNullOrEmpty(value) && !SecretReference.IsReference(value);
    private static bool SensitiveName(string name) => new[] { "password", "secret", "token", "apikey", "api_key", "privatekey", "credential" }.Any(part => name.Contains(part, StringComparison.OrdinalIgnoreCase));
    internal static bool Sensitive(object owner, string member, string value)
        => SensitiveName(member) ||
           (member == "DefaultValue" && owner is CustomField { Type: CustomFieldType.Password }) ||
           (member == "Value" && new[] { "Name", "ValueName", "Key", "Selector" }.Any(key => SensitiveName(owner.GetType().GetProperty(key)?.GetValue(owner)?.ToString() ?? ""))) ||
           new[] { "password=", "secret=", "token=", "--password", "--secret", "--token" }.Any(part => value?.Contains(part, StringComparison.OrdinalIgnoreCase) == true);

    private static JsonSerializerOptions CreateOptions(Func<object, string, string, bool> sensitive)
    {
        var resolver = new DefaultJsonTypeInfoResolver();
        resolver.Modifiers.Add(info =>
        {
            foreach (var property in info.Properties)
            {
                if (property.Get is not { } get) continue;
                property.Get = owner =>
                {
                    if (owner is InstallProject && property.Name == "ModifiedAt") return "";
                    var value = get(owner);
                    if (value is string text && sensitive(owner, property.Name, text) && IsPlaintext(text)) return "";
                    if (owner is CompiledInstallOperation operation && property.Name == "Inputs" && operation.Inputs is not null)
                        return new SortedDictionary<string, string>(operation.Inputs.ToDictionary(pair => pair.Key,
                            pair => (operation.SensitiveInputs?.Contains(pair.Key, StringComparer.OrdinalIgnoreCase) == true || sensitive(operation.Inputs, pair.Key, pair.Value)) && IsPlaintext(pair.Value) ? "" : pair.Value), StringComparer.Ordinal);
                    return value;
                };
            }
        });
        return new JsonSerializerOptions { TypeInfoResolver = resolver, IgnoreReadOnlyProperties = true };
    }

    internal static void Walk(object root, Action<object, string> node, Action<object, string, string, Action<string>, bool> field)
    {
        var seen = new HashSet<object>(ReferenceEqualityComparer.Instance);
        Visit(root, "", null);
        void Visit(object? owner, string path, HashSet<string>? sensitiveKeys)
        {
            if (owner is null || owner is string || owner.GetType().IsValueType) return;
            node(owner, path);
            if (!seen.Add(owner) && owner is not IDictionary) return;
            if (owner is IDictionary dictionary)
            {
                foreach (var key in dictionary.Keys.Cast<object>().ToArray())
                    if (dictionary[key] is string || dictionary[key] is null)
                    {
                        var value = dictionary[key] as string ?? "";
                        field(owner, key.ToString()!, value, text => dictionary[key] = text,
                            sensitiveKeys?.Contains(key.ToString()!) == true || Sensitive(owner, key.ToString()!, value));
                    }
                return;
            }
            if (owner is IList list) { for (var i = 0; i < list.Count; i++) Visit(list[i], $"{path}[{i}]", null); return; }
            foreach (var property in owner.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                if (!property.CanRead || property.GetIndexParameters().Length != 0) continue;
                var value = property.GetValue(owner);
                if (property.PropertyType == typeof(string) && property.CanWrite)
                    field(owner, property.Name, value as string ?? "", updated => property.SetValue(owner, updated), Sensitive(owner, property.Name, value as string ?? ""));
                else if (value is not null && value is not string && !value.GetType().IsValueType)
                    Visit(value, path + "." + property.Name, owner is CompiledInstallOperation operation && property.Name == "Inputs" ? new(operation.SensitiveInputs ?? new(), StringComparer.OrdinalIgnoreCase) : null);
            }
        }
    }
}
