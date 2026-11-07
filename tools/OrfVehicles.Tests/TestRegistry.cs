using System.Text.Json;
using System.Text.Json.Nodes;

namespace OrfVehicles.Tests;

/// <summary>Builds a temporary copy of the synthetic fixture registry, optionally changed.</summary>
public static class TestRegistry
{
    public const string Model = "models/example-make/example-model.json";
    public const string GenericTemplate = "templates/generic-basic.runsetup";
    public const string ModelTemplate = "templates/example-model-gen1.runsetup";
    public const string GenericModification = "modifications/mod-example-coilover.runsetup";
    public const string ModelModification = "modifications/mod-example-wing.runsetup";
    public const string Gen1 = "example-make/example-model/base@gen1";

    public static string FixtureRoot => Path.Combine(AppContext.BaseDirectory, "fixtures", "registry");

    public static JsonObject Load(string relativePath) =>
        JsonNode.Parse(File.ReadAllText(Path.Combine(FixtureRoot, relativePath)))!.AsObject();

    public static string Serialize(JsonNode node) =>
        node.ToJsonString(new JsonSerializerOptions { WriteIndented = true }).Replace("\r\n", "\n") + "\n";

    /// <summary>The fixture file <paramref name="relativePath"/>, changed by <paramref name="mutate"/>, as text.</summary>
    public static string With(string relativePath, Action<JsonObject> mutate)
    {
        var json = Load(relativePath);
        mutate(json);
        return Serialize(json);
    }

    /// <summary>A copy of the fixture registry; each change writes a file, or deletes it when the content is null.</summary>
    public static string Create(params (string Path, string? Content)[] changes)
    {
        var root = NewRoot();
        Copy(FixtureRoot, root);
        foreach (var (path, content) in changes)
        {
            var full = Path.Combine(root, path);
            if (content is null)
            {
                File.Delete(full);
                continue;
            }
            Directory.CreateDirectory(Path.GetDirectoryName(full)!);
            File.WriteAllText(full, content);
        }
        return root;
    }

    /// <summary>A registry with no files at all.</summary>
    public static string CreateEmpty()
    {
        var root = NewRoot();
        Directory.CreateDirectory(root);
        return root;
    }

    private static string NewRoot() => Path.Combine(Path.GetTempPath(), "orf-vehicles-tests", Guid.NewGuid().ToString("N"));

    private static void Copy(string from, string to)
    {
        Directory.CreateDirectory(to);
        foreach (var f in Directory.GetFiles(from))
            File.Copy(f, Path.Combine(to, Path.GetFileName(f)));
        foreach (var d in Directory.GetDirectories(from))
            Copy(d, Path.Combine(to, Path.GetFileName(d)));
    }
}
