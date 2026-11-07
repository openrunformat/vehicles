using System.Text.Json;
using System.Text.Json.Nodes;
using Json.Schema;

namespace OrfVehicles;

public static class RegistryValidator
{
    private static readonly Lazy<JsonSchema> ModelSchema = new(() =>
        JsonSchema.FromText(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "schema", "model.schema.json"))));

    /// <summary>Validates the registry under <paramref name="root"/>; findings sorted by File, Path, Code (ordinal).</summary>
    public static IReadOnlyList<Finding> Validate(string root)
    {
        var findings = new List<Finding>();
        CheckStrayFiles(root, findings);

        var models = new List<ModelFile>();
        foreach (var file in RegistryLoader.LoadModels(root, findings))
        {
            var schemaFindings = CheckSchema(file);
            findings.AddRange(schemaFindings);
            if (schemaFindings.Count == 0)
                models.Add(file);
        }

        var setups = new List<SetupFile>();
        foreach (var file in RegistryLoader.LoadSetups(root, findings))
        {
            var before = findings.Count;
            CheckControlCharacters(file.RelativePath, file.Json, "", findings);
            if (findings.Count == before)
                setups.Add(file);
        }

        var systems = SemanticChecks.LoadSystems(root, findings);
        foreach (var model in models)
            SemanticChecks.CheckModel(model, systems, findings);
        foreach (var setup in setups)
            SemanticChecks.CheckSetup(setup, findings);
        SemanticChecks.CheckRegistry(models, setups, findings);
        SemanticChecks.CheckComposition(setups, findings, SemanticChecks.DefaultCompose);

        return findings
            .Distinct()
            .OrderBy(f => f.File, StringComparer.Ordinal)
            .ThenBy(f => f.Path, StringComparer.Ordinal)
            .ThenBy(f => f.Code, StringComparer.Ordinal)
            .ThenBy(f => f.Message, StringComparer.Ordinal)
            .ToList();
    }

    private static void CheckStrayFiles(string root, List<Finding> findings)
    {
        void Stray(string path, string message) =>
            findings.Add(new Finding(RegistryLoader.Relative(root, path), "", "stray-file", message));

        var models = Path.Combine(root, RegistryLoader.ModelsFolder);
        if (Directory.Exists(models))
        {
            foreach (var file in Directory.GetFiles(models))
                if (Path.GetFileName(file) != "README.md")
                    Stray(file, "Only README.md may sit directly in models/; model files go in models/<make>/.");
            foreach (var make in Directory.GetDirectories(models))
            {
                foreach (var file in Directory.GetFiles(make))
                    if (!file.EndsWith(".json", StringComparison.Ordinal))
                        Stray(file, "Only model files (*.json) may sit in a make folder.");
                foreach (var nested in Directory.GetDirectories(make))
                    Stray(nested, "Make folders must not contain subfolders.");
            }
        }

        foreach (var folder in new[] { RegistryLoader.TemplatesFolder, RegistryLoader.ModificationsFolder })
        {
            var dir = Path.Combine(root, folder);
            if (!Directory.Exists(dir))
                continue;
            foreach (var file in Directory.GetFiles(dir))
                if (Path.GetFileName(file) != "README.md" && !file.EndsWith(".runsetup", StringComparison.Ordinal))
                    Stray(file, $"Only *.runsetup files and README.md may sit in {folder}/.");
            foreach (var nested in Directory.GetDirectories(dir))
                Stray(nested, $"{folder}/ must not contain subfolders.");
        }
    }

    private static List<Finding> CheckSchema(ModelFile file)
    {
        var result = new List<Finding>();
        using var doc = JsonDocument.Parse(file.Json.ToJsonString());
        var evaluation = ModelSchema.Value.Evaluate(doc.RootElement, new EvaluationOptions { OutputFormat = OutputFormat.List });
        if (evaluation.IsValid)
        {
            // .NET regular expressions let "$" match before a final "\n", so the schema patterns alone do not
            // keep control characters out of ids and uids.
            CheckControlCharacters(file.RelativePath, file.Json, "", result);
            return result;
        }
        foreach (var detail in evaluation.Details ?? [])
            if (detail.Errors is { Count: > 0 } errors)
                foreach (var error in errors)
                    result.Add(new Finding(file.RelativePath, detail.InstanceLocation.ToString(), "schema", $"{error.Key}: {error.Value}"));
        if (result.Count == 0)
            result.Add(new Finding(file.RelativePath, "", "schema", "Does not match schema/model.schema.json."));
        return result;
    }

    private static void CheckControlCharacters(string file, JsonNode? node, string path, List<Finding> result)
    {
        static bool HasControl(string s) => s.Any(char.IsControl);
        switch (node)
        {
            case JsonObject o:
                foreach (var (key, value) in o)
                {
                    var p = $"{path}/{SemanticChecks.Pointer(key)}";
                    if (HasControl(key))
                        result.Add(new Finding(file, p, "schema", "Property names must not contain control characters."));
                    CheckControlCharacters(file, value, p, result);
                }
                break;
            case JsonArray a:
                for (int i = 0; i < a.Count; i++)
                    CheckControlCharacters(file, a[i], $"{path}/{i}", result);
                break;
            case JsonValue v when v.TryGetValue<string>(out var s) && HasControl(s):
                result.Add(new Finding(file, path, "schema", "Text values must not contain control characters (line breaks, tabs...)."));
                break;
        }
    }
}
