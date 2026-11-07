using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using OpenRunFormat.Setup;

namespace OrfVehicles;

/// <summary>A model file that parsed as a JSON object.</summary>
public sealed record ModelFile(string RelativePath, JsonObject Json);

/// <summary>
/// A <c>.runsetup</c> file that parsed as a JSON object and that the Open Run Format library accepts.
/// <paramref name="FolderKind"/> is the kind its folder holds (<c>templates/</c> or <c>modifications/</c>).
/// </summary>
public sealed record SetupFile(string RelativePath, RunSetupKind FolderKind, JsonObject Json, RunSetup Setup);

public static class RegistryLoader
{
    public const string ModelsFolder = "models";
    public const string TemplatesFolder = "templates";
    public const string ModificationsFolder = "modifications";

    private static readonly UTF8Encoding StrictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    /// <summary>
    /// Reads every <c>models/&lt;make&gt;/*.json</c> file under <paramref name="root"/>. Files that are not valid UTF-8
    /// JSON objects (without BOM) produce a "json" finding; this method never throws because of file content.
    /// </summary>
    public static IReadOnlyList<ModelFile> LoadModels(string root, List<Finding> findings)
    {
        var result = new List<ModelFile>();
        var dir = Path.Combine(root, ModelsFolder);
        if (!Directory.Exists(dir))
            return result;
        foreach (var makeDir in Directory.GetDirectories(dir).Order(StringComparer.Ordinal))
        foreach (var file in Directory.GetFiles(makeDir, "*.json").Order(StringComparer.Ordinal))
            if (ReadObject(root, file, findings) is { } obj)
                result.Add(new ModelFile(Relative(root, file), obj));
        return result;
    }

    /// <summary>
    /// Reads every <c>templates/*.runsetup</c> and <c>modifications/*.runsetup</c> file. Files that are not valid JSON
    /// objects produce a "json" finding; files the Open Run Format library rejects produce a "runsetup" finding.
    /// Results are ordered by path.
    /// </summary>
    public static IReadOnlyList<SetupFile> LoadSetups(string root, List<Finding> findings)
    {
        var result = new List<SetupFile>();
        foreach (var (folder, kind) in new[] { (ModificationsFolder, RunSetupKind.Modification), (TemplatesFolder, RunSetupKind.Template) })
        {
            var dir = Path.Combine(root, folder);
            if (!Directory.Exists(dir))
                continue;
            foreach (var file in Directory.GetFiles(dir, "*" + RunSetupFile.Extension).Order(StringComparer.Ordinal))
            {
                if (ReadObject(root, file, findings) is not { } obj)
                    continue;
                var rel = Relative(root, file);
                try
                {
                    result.Add(new SetupFile(rel, kind, obj, RunSetupFile.Parse(obj.ToJsonString())));
                }
                catch (FormatException ex)
                {
                    findings.Add(new Finding(rel, "", "runsetup", ex.Message));
                }
            }
        }
        return result;
    }

    private static JsonObject? ReadObject(string root, string file, List<Finding> findings)
    {
        var rel = Relative(root, file);
        var bytes = File.ReadAllBytes(file);
        if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
        {
            findings.Add(new Finding(rel, "", "json", "File starts with a UTF-8 byte order mark; save it as UTF-8 without BOM."));
            return null;
        }
        try
        {
            var text = StrictUtf8.GetString(bytes);
            var node = JsonNode.Parse(text, documentOptions: new JsonDocumentOptions
            {
                AllowTrailingCommas = false,
                CommentHandling = JsonCommentHandling.Disallow,
                AllowDuplicateProperties = false,
            });
            if (node is JsonObject obj)
                return obj;
            findings.Add(new Finding(rel, "", "json", "The file must contain a JSON object."));
        }
        catch (Exception ex) when (ex is JsonException or DecoderFallbackException)
        {
            findings.Add(new Finding(rel, "", "json", $"Not valid JSON: {ex.Message}"));
        }
        return null;
    }

    internal static string Relative(string root, string path) =>
        Path.GetRelativePath(root, path).Replace('\\', '/');
}
