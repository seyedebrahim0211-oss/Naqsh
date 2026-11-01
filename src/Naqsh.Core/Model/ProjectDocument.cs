using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Naqsh.Core.Formats;

namespace Naqsh.Core.Model;

/// <summary>A saved show: format, inputs and switcher state. Versioned JSON (see <see cref="ProjectSerializer"/>).</summary>
public sealed record ProjectDocument
{
    public const int CurrentSchemaVersion = 1;

    public int SchemaVersion { get; init; } = CurrentSchemaVersion;

    public VideoFormat Format { get; init; } = VideoFormat.Default;

    public IReadOnlyList<InputDescriptor> Inputs { get; init; } = [];

    public Guid Preview { get; init; }

    public Guid Program { get; init; }

    public int FadeDurationMs { get; init; } = 1000;
}

/// <summary>Loads/saves projects with schema migrations. Older files are backed up before migration.</summary>
public static class ProjectSerializer
{
    public static JsonSerializerOptions Options { get; } = CreateOptions();

    public static string Serialize(ProjectDocument doc) => JsonSerializer.Serialize(doc, Options);

    /// <summary>Parses and migrates JSON. <paramref name="migratedFrom"/> is the original version if a migration ran.</summary>
    public static ProjectDocument Deserialize(string json, out int? migratedFrom)
    {
        migratedFrom = null;
        var node = JsonNode.Parse(json) as JsonObject
                   ?? throw new InvalidDataException("Project root must be a JSON object.");
        var version = node["schemaVersion"]?.GetValue<int>() ?? 0;
        if (version > ProjectDocument.CurrentSchemaVersion)
        {
            throw new InvalidDataException($"Project schema {version} is newer than supported {ProjectDocument.CurrentSchemaVersion}.");
        }

        if (version < ProjectDocument.CurrentSchemaVersion)
        {
            migratedFrom = version;
            Migrate(node, version);
        }

        return node.Deserialize<ProjectDocument>(Options)
               ?? throw new InvalidDataException("Empty project.");
    }

    public static async Task SaveAsync(ProjectDocument doc, string path, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(path);
        var tmp = path + ".tmp";
        await File.WriteAllTextAsync(tmp, Serialize(doc), ct).ConfigureAwait(false);
        File.Move(tmp, path, overwrite: true);
    }

    public static async Task<ProjectDocument> LoadAsync(string path, CancellationToken ct)
    {
        var json = await File.ReadAllTextAsync(path, ct).ConfigureAwait(false);
        var doc = Deserialize(json, out var from);
        if (from is not null)
        {
            File.Copy(path, $"{path}.v{from}.bak", overwrite: true);
        }

        return doc;
    }

    private static void Migrate(JsonObject node, int fromVersion)
    {
        // v0 (pre-release drafts): "fadeMs" was renamed to "fadeDurationMs"; no version field.
        if (fromVersion < 1)
        {
            if (node["fadeMs"] is { } fade)
            {
                node.Remove("fadeMs");
                node["fadeDurationMs"] = fade.GetValue<int>();
            }

            node["schemaVersion"] = 1;
        }
    }

    private static JsonSerializerOptions CreateOptions()
    {
        var o = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            WriteIndented = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        };
        return o;
    }
}
