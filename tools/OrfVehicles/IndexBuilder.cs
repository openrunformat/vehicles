using System.Globalization;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace OrfVehicles;

/// <summary>
/// Builds <c>dist/index.json</c>: every model (without provenance details) and every template and modification, whole,
/// in a stable byte-exact form.
/// </summary>
public static class IndexBuilder
{
    public const string Source = "https://github.com/openrunformat/vehicles";

    /// <summary>
    /// Returns the index as indented JSON (2 spaces), LF line ends and a final newline. Throws
    /// <see cref="InvalidOperationException"/> when the registry has findings.
    /// </summary>
    public static string Build(string root, DateTimeOffset generatedAt)
    {
        var findings = RegistryValidator.Validate(root);
        if (findings.Count > 0)
            throw new InvalidOperationException(
                $"The registry has {findings.Count} finding(s); run 'orf-vehicles validate' first. First: {findings[0]}");

        var models = RegistryLoader.LoadModels(root, []).OrderBy(f => SemanticChecks.Str(f.Json, "id"), StringComparer.Ordinal);
        var setups = RegistryLoader.LoadSetups(root, []);

        var buffer = new MemoryStream();
        var options = new JsonWriterOptions
        {
            Indented = true,
            IndentSize = 2,
            NewLine = "\n",
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        };
        using (var w = new Utf8JsonWriter(buffer, options))
        {
            w.WriteStartObject();
            w.WriteNumber("version", 1);
            w.WriteString("generatedAt", generatedAt.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture));
            w.WriteString("source", Source);
            w.WriteString("licence", "CC0-1.0");
            w.WriteStartArray("models");
            foreach (var file in models)
                WriteModel(w, file.Json);
            w.WriteEndArray();
            WriteSetups(w, "templates", setups.Where(SemanticChecks.IsTemplate));
            WriteSetups(w, "modifications", setups.Where(SemanticChecks.IsModification));
            w.WriteEndObject();
        }
        return Encoding.UTF8.GetString(buffer.ToArray()) + "\n";
    }

    private static void WriteModel(Utf8JsonWriter w, JsonObject j)
    {
        var id = SemanticChecks.Str(j, "id")!;
        w.WriteStartObject();
        Copy(w, j, "id");
        Copy(w, j, "uid");
        var make = j["make"]!.AsObject();
        w.WritePropertyName("make");
        w.WriteStartObject();
        Copy(w, make, "id");
        Copy(w, make, "name");
        CopyOr(w, make, "external", () => w.WriteStartArray("external"), () => w.WriteEndArray());
        w.WriteEndObject();
        Copy(w, j, "name");
        CopyOr(w, j, "names", () => w.WriteStartObject("names"), () => w.WriteEndObject());
        CopyOr(w, j, "external", () => w.WriteStartArray("external"), () => w.WriteEndArray());

        w.WriteStartArray("variants");
        foreach (var variantNode in SemanticChecks.Arr(j, "variants"))
        {
            var variant = variantNode!.AsObject();
            var variantId = SemanticChecks.Str(variant, "id")!;
            w.WriteStartObject();
            Copy(w, variant, "id");
            Copy(w, variant, "name");
            CopyOr(w, variant, "names", () => w.WriteStartObject("names"), () => w.WriteEndObject());
            w.WriteStartArray("generations");
            foreach (var genNode in SemanticChecks.Arr(variant, "generations"))
            {
                var gen = genNode!.AsObject();
                w.WriteStartObject();
                Copy(w, gen, "id");
                w.WriteString("fullId", SemanticChecks.GenerationId(id, variantId, SemanticChecks.Str(gen, "id")!));
                Copy(w, gen, "uid");
                Copy(w, gen, "name");
                CopyOr(w, gen, "names", () => w.WriteStartObject("names"), () => w.WriteEndObject());
                var years = gen["years"]!.AsObject();
                w.WritePropertyName("years");
                w.WriteStartObject();
                Copy(w, years, "from");
                CopyOrNull(w, years, "to");
                w.WriteEndObject();
                CopyOrNull(w, gen, "stock");
                CopyOrNull(w, gen, "template");
                w.WriteEndObject();
            }
            w.WriteEndArray();
            w.WriteEndObject();
        }
        w.WriteEndArray();

        w.WriteStartArray("attribution");
        var attributions = SemanticChecks.Arr(j, "provenance")
            .Select(p => SemanticChecks.Str(p!.AsObject(), "attribution"))
            .OfType<string>()
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal);
        foreach (var a in attributions)
            w.WriteStringValue(a);
        w.WriteEndArray();

        w.WriteEndObject();
    }

    private static void WriteSetups(Utf8JsonWriter w, string name, IEnumerable<SetupFile> files)
    {
        w.WriteStartArray(name);
        foreach (var file in files.OrderBy(f => f.Setup.Id, StringComparer.Ordinal))
        {
            w.WriteStartObject();
            w.WriteString("id", file.Setup.Id);
            w.WriteNumber("version", file.Setup.Version!.Value);
            Copy(w, file.Json, "name");
            w.WriteStartArray("appliesTo");
            foreach (var target in file.Setup.AppliesTo)
                w.WriteStringValue(target);
            w.WriteEndArray();
            w.WritePropertyName("file");
            file.Json.WriteTo(w);
            w.WriteEndObject();
        }
        w.WriteEndArray();
    }

    private static void Copy(Utf8JsonWriter w, JsonObject o, string name)
    {
        w.WritePropertyName(name);
        o[name]!.WriteTo(w);
    }

    private static void CopyOrNull(Utf8JsonWriter w, JsonObject o, string name)
    {
        if (o[name] is null)
            w.WriteNull(name);
        else
            Copy(w, o, name);
    }

    private static void CopyOr(Utf8JsonWriter w, JsonObject o, string name, Action writeEmptyStart, Action writeEmptyEnd)
    {
        if (o[name] is not null)
        {
            Copy(w, o, name);
            return;
        }
        writeEmptyStart();
        writeEmptyEnd();
    }
}
