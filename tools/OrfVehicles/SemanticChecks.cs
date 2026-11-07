using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using OpenRunFormat.Setup;

namespace OrfVehicles;

/// <summary>Checks that need more than the JSON schema (run only on files that passed the schema or the library).</summary>
internal static partial class SemanticChecks
{
    private static readonly string[] AllowedLicences = ["CC0-1.0", "CC-BY-4.0"];

    public sealed record ExternalSystem(string Id, Regex Pattern);

    public static readonly Func<RunSetup, IReadOnlyList<RunSetup>, ComposedSetup> DefaultCompose =
        (template, modifications) => RunSetupComposer.Compose(template, modifications);

    [GeneratedRegex("^[a-z0-9]+(-[a-z0-9]+)*$", RegexOptions.CultureInvariant)]
    private static partial Regex SetupIdPattern();

    public const int MaxSetupIdLength = 64;

    /// <summary>The id of a generation: <c>&lt;make&gt;/&lt;model&gt;/&lt;variant&gt;@&lt;generation&gt;</c>.</summary>
    public static string GenerationId(string modelId, string variantId, string generationId) =>
        $"{modelId}/{variantId}@{generationId}";

    /// <summary>
    /// Reads schema/systems.json (the registry's own, or the tool's copy). Returns null and adds a "systems" finding
    /// when the file cannot be used; external system checks are then skipped.
    /// </summary>
    public static IReadOnlyDictionary<string, ExternalSystem>? LoadSystems(string root, List<Finding> findings)
    {
        var path = Path.Combine(root, "schema", "systems.json");
        if (!File.Exists(path))
            path = Path.Combine(AppContext.BaseDirectory, "schema", "systems.json");
        try
        {
            var json = JsonNode.Parse(File.ReadAllText(path), documentOptions: new System.Text.Json.JsonDocumentOptions { AllowDuplicateProperties = false })!;
            var result = new Dictionary<string, ExternalSystem>(StringComparer.Ordinal);
            foreach (var s in json["systems"]!.AsArray())
            {
                var id = s!["id"]!.GetValue<string>();
                var pattern = new Regex(s["pattern"]!.GetValue<string>(), RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
                if (!result.TryAdd(id, new ExternalSystem(id, pattern)))
                    throw new FormatException($"System '{id}' is listed twice.");
            }
            return result;
        }
        catch (Exception ex) when (ex is System.Text.Json.JsonException or FormatException or ArgumentException
                                       or InvalidOperationException or NullReferenceException)
        {
            findings.Add(new Finding("schema/systems.json", "", "systems", $"Cannot read the list of external id systems: {ex.Message}"));
            return null;
        }
    }

    /// <summary>Checks inside one model file.</summary>
    public static void CheckModel(ModelFile file, IReadOnlyDictionary<string, ExternalSystem>? systems, List<Finding> findings)
    {
        var j = file.Json;
        void Add(string path, string code, string message) => findings.Add(new Finding(file.RelativePath, path, code, message));

        var id = Str(j, "id")!;
        var slash = id.IndexOf('/');
        var idMake = id[..slash];
        var idModel = id[(slash + 1)..];
        if (file.RelativePath != $"{RegistryLoader.ModelsFolder}/{idMake}/{idModel}.json")
            Add("/id", "file-name", $"Model '{id}' must be stored as models/{idMake}/{idModel}.json.");
        var make = j["make"]!.AsObject();
        if (Str(make, "id") != idMake)
            Add("/make/id", "make-mismatch", $"Make '{Str(make, "id")}' must match the id prefix '{idMake}'.");

        CheckUid(j, "/uid", Add);

        var variantIds = new HashSet<string>(StringComparer.Ordinal);
        var variants = Arr(j, "variants");
        for (int vi = 0; vi < variants.Count; vi++)
        {
            var variant = variants[vi]!.AsObject();
            var vp = $"/variants/{vi}";
            if (!variantIds.Add(Str(variant, "id")!))
                Add($"{vp}/id", "duplicate-id", $"Variant '{Str(variant, "id")}' appears more than once in this model.");
            var generations = Arr(variant, "generations");
            for (int gi = 0; gi < generations.Count; gi++)
            {
                var gen = generations[gi]!.AsObject();
                var gp = $"{vp}/generations/{gi}";
                CheckUid(gen, $"{gp}/uid", Add);
                var years = gen["years"]!.AsObject();
                if (years["to"] is { } to && to.GetValue<double>() < years["from"]!.GetValue<double>())
                    Add($"{gp}/years/to", "years", "'to' must not be before 'from'.");
                var modes = gen["stock"]?["driveModes"] as JsonArray ?? [];
                var modeIds = new HashSet<string>(StringComparer.Ordinal);
                for (int mi = 0; mi < modes.Count; mi++)
                    if (!modeIds.Add(Str(modes[mi]!.AsObject(), "id")!))
                        Add($"{gp}/stock/driveModes/{mi}/id", "duplicate-id", $"Drive mode '{Str(modes[mi]!.AsObject(), "id")}' appears more than once.");
            }
        }

        CheckExternal(Arr(make, "external"), "/make/external", systems, Add);
        CheckExternal(Arr(j, "external"), "/external", systems, Add);
        CheckProvenance(j, Add);
    }

    private static void CheckUid(JsonObject owner, string path, Action<string, string, string> add)
    {
        var uid = Guid.Parse(Str(owner, "uid")!);
        if (uid.Version != 7)
            add(path, "uid-not-v7", $"uid must be a UUID version 7 (found version {uid.Version}); generate one with 'orf-vehicles uid'.");
    }

    private static void CheckExternal(JsonArray list, string basePath, IReadOnlyDictionary<string, ExternalSystem>? systems,
        Action<string, string, string> add)
    {
        if (systems is null)
            return;   // The systems list is broken; that is reported once, on schema/systems.json.
        for (int i = 0; i < list.Count; i++)
        {
            var e = list[i]!.AsObject();
            var system = Str(e, "system")!;
            var eid = Str(e, "id")!;
            if (!systems.TryGetValue(system, out var s))
                add($"{basePath}/{i}/system", "system-unknown", $"System '{system}' is not listed in schema/systems.json.");
            else if (!s.Pattern.IsMatch(eid) || eid.Any(char.IsControl))
                add($"{basePath}/{i}/id", "external-pattern", $"'{eid}' is not a valid {system} id.");
        }
    }

    private static void CheckProvenance(JsonObject j, Action<string, string, string> add)
    {
        var list = Arr(j, "provenance");
        var ids = new HashSet<string>(StringComparer.Ordinal);
        for (int i = 0; i < list.Count; i++)
        {
            var p = list[i]!.AsObject();
            var pp = $"/provenance/{i}";
            if (!ids.Add(Str(p, "id")!))
                add($"{pp}/id", "provenance", $"Provenance '{Str(p, "id")}' appears more than once.");
            if (Str(p, "method") is "official" or "published" && Str(p, "source") is null && Str(p, "url") is null)
                add(pp, "provenance", "Official and published sources need a 'source' or a 'url' that others can check.");
            var licence = Str(p, "licence");
            if (licence is not null && !AllowedLicences.Contains(licence, StringComparer.Ordinal))
                add($"{pp}/licence", "provenance", $"Licence '{licence}' is not compatible; use CC0-1.0 or CC-BY-4.0.");
            if (licence == "CC-BY-4.0" && Str(p, "attribution") is null)
                add(pp, "provenance", "CC-BY-4.0 sources need an 'attribution' text.");
        }
    }

    /// <summary>Checks inside one .runsetup file.</summary>
    public static void CheckSetup(SetupFile file, List<Finding> findings)
    {
        var j = file.Json;
        var s = file.Setup;
        void Add(string path, string code, string message) => findings.Add(new Finding(file.RelativePath, path, code, message));

        var folder = file.FolderKind == RunSetupKind.Template ? RegistryLoader.TemplatesFolder : RegistryLoader.ModificationsFolder;
        if (s.Kind != file.FolderKind)
            Add("/kind", "kind-mismatch", $"{folder}/ holds only files of kind '{KindName(file.FolderKind)}'; this one is '{KindName(s.Kind)}'.");

        var orfApplies = SetupVocabulary.Applies(s.Orf);
        if (!orfApplies)
            Add("/orf", "orf-version", $"Registry files must be Open Run Format 1.3 or later; this one is '{s.Orf}'.");

        if (s.Id is { } id)
        {
            if (id.Length > MaxSetupIdLength || !SetupIdPattern().IsMatch(id) || id.Any(char.IsControl))
                Add("/id", "id-syntax", $"'{id}' is not a valid registry id (a-z, 0-9 and single '-', at most {MaxSetupIdLength} characters).");
            if (file.RelativePath != $"{folder}/{id}{RunSetupFile.Extension}")
                Add("/id", "file-name", $"'{id}' must be stored as {folder}/{id}{RunSetupFile.Extension}.");
        }

        // Entries the readers would ignore on their own: in a curated registry, they are mistakes.
        if (j["parameters"] is JsonObject parameters)
        {
            foreach (var (key, value) in parameters)
            {
                var probe = new JsonObject { ["parameters"] = new JsonObject { [key] = value?.DeepClone() } };
                if (Probe(probe, RunSetupKind.Template).Parameters.Count != 1)
                    Add($"/parameters/{Pointer(key)}", "ignored-entry", "Readers ignore this parameter: its description is malformed, or the key needs one.");
                else if (orfApplies && !SetupVocabulary.IsStandard(key) && !SetupVocabulary.IsCustom(key))
                    Add($"/parameters/{Pointer(key)}", "nonstandard-key", $"'{key}' is not in the standard vocabulary; non-standard keys start with 'x-'.");
            }
        }
        else if (j["parameters"] is not null)
            Add("/parameters", "ignored-entry", "Readers ignore 'parameters': it must be an object.");

        if (j["operations"] is JsonArray operations)
        {
            for (int i = 0; i < operations.Count; i++)
            {
                var probe = new JsonObject { ["operations"] = new JsonArray(operations[i]?.DeepClone()) };
                var parsed = Probe(probe, RunSetupKind.Modification).Operations;
                if (parsed.Count != 1)
                    Add($"/operations/{i}", "ignored-entry", "Readers ignore this operation: it is malformed, or a non-standard key has no description.");
                else if (orfApplies && parsed[0].Key is { } key && !SetupVocabulary.IsStandard(key) && !SetupVocabulary.IsCustom(key))
                    Add($"/operations/{i}/key", "nonstandard-key", $"'{key}' is not in the standard vocabulary; non-standard keys start with 'x-'.");
            }
        }
        else if (j["operations"] is not null)
            Add("/operations", "ignored-entry", "Readers ignore 'operations': it must be an array.");

        if (j["damperWays"] is not null && s.DamperWays is null)
            Add("/damperWays", "ignored-entry", "Readers ignore 'damperWays': it needs integer 'front' and 'rear' between 0 and 4.");
    }

    private static RunSetup Probe(JsonObject content, RunSetupKind kind)
    {
        content["orf"] = "1.3";
        content["kind"] = KindName(kind);
        content["id"] = "probe";
        content["version"] = 1;
        content["name"] = new JsonObject { ["en"] = "probe" };
        return RunSetupFile.Parse(content.ToJsonString());
    }

    private static string KindName(RunSetupKind kind) => kind switch
    {
        RunSetupKind.Template => "template",
        RunSetupKind.Modification => "modification",
        _ => "setup",
    };

    /// <summary>
    /// Checks across files: unique ids and uids, unique external ids, one make block per make, and references from
    /// generations to templates and from <c>appliesTo</c> to generations.
    /// </summary>
    public static void CheckRegistry(IReadOnlyList<ModelFile> models, IReadOnlyList<SetupFile> setups, List<Finding> findings)
    {
        var ids = new Dictionary<string, string>(StringComparer.Ordinal);
        var uids = new Dictionary<string, string>(StringComparer.Ordinal);
        var externals = new Dictionary<(string, string), string>();
        var makes = new Dictionary<string, (string File, JsonObject Make)>(StringComparer.Ordinal);
        var makeExternals = new Dictionary<(string, string), string>();
        var generations = new HashSet<string>(StringComparer.Ordinal);
        var templateRefs = new List<(string File, string Path, string Template)>();

        foreach (var file in models)
        {
            var j = file.Json;
            void Add(string path, string code, string message) => findings.Add(new Finding(file.RelativePath, path, code, message));

            var id = Str(j, "id")!;
            if (!ids.TryAdd(id, file.RelativePath))
                Add("/id", "duplicate-id", $"'{id}' is already used by {ids[id]}.");
            var uidList = new List<(string Path, string Uid)> { ("/uid", Str(j, "uid")!) };

            var variants = Arr(j, "variants");
            for (int vi = 0; vi < variants.Count; vi++)
            {
                var variant = variants[vi]!.AsObject();
                var gens = Arr(variant, "generations");
                for (int gi = 0; gi < gens.Count; gi++)
                {
                    var gen = gens[gi]!.AsObject();
                    var gp = $"/variants/{vi}/generations/{gi}";
                    var full = GenerationId(id, Str(variant, "id")!, Str(gen, "id")!);
                    generations.Add(full);
                    if (!ids.TryAdd(full, $"{file.RelativePath}:{gp}"))
                        Add($"{gp}/id", "duplicate-id", $"'{full}' is already used by {ids[full]}.");
                    uidList.Add(($"{gp}/uid", Str(gen, "uid")!));
                    if (Str(gen, "template") is { } template)
                        templateRefs.Add((file.RelativePath, $"{gp}/template", template));
                }
            }
            foreach (var (path, uid) in uidList)
                if (!uids.TryAdd(uid, $"{file.RelativePath}:{path}"))
                    Add(path, "duplicate-uid", $"uid {uid} is already used by {uids[uid]}.");

            var external = Arr(j, "external");
            for (int i = 0; i < external.Count; i++)
            {
                var e = external[i]!.AsObject();
                var key = (Str(e, "system")!, Str(e, "id")!);
                if (!externals.TryAdd(key, file.RelativePath))
                    Add($"/external/{i}", "duplicate-external", $"{key.Item1} {key.Item2} is already used by {externals[key]}.");
            }

            var make = j["make"]!.AsObject();
            var makeId = Str(make, "id")!;
            if (makes.TryGetValue(makeId, out var first))
            {
                if (!JsonNode.DeepEquals(first.Make, make))
                    Add("/make", "make-mismatch", $"Make '{makeId}' is described differently in {first.File}; every model of a make repeats the same 'make' block.");
            }
            else
            {
                makes[makeId] = (file.RelativePath, make);
                var makeExternal = Arr(make, "external");
                for (int i = 0; i < makeExternal.Count; i++)
                {
                    var e = makeExternal[i]!.AsObject();
                    var key = (Str(e, "system")!, Str(e, "id")!);
                    if (!makeExternals.TryAdd(key, makeId))
                        Add($"/make/external/{i}", "duplicate-external", $"{key.Item1} {key.Item2} is already used by make '{makeExternals[key]}'.");
                }
            }
        }

        var templates = new HashSet<string>(StringComparer.Ordinal);
        foreach (var file in setups)
        {
            var id = file.Setup.Id;
            if (id is null)
                continue;
            if (!ids.TryAdd(id, file.RelativePath))
                findings.Add(new Finding(file.RelativePath, "/id", "duplicate-id", $"'{id}' is already used by {ids[id]}."));
            if (IsTemplate(file))
                templates.Add(id);
            for (int i = 0; i < file.Setup.AppliesTo.Count; i++)
            {
                var target = file.Setup.AppliesTo[i];
                if (!generations.Contains(target))
                    findings.Add(new Finding(file.RelativePath, $"/appliesTo/{i}", "unknown-applies-to",
                        $"'{target}' is not a generation of the registry (<make>/<model>/<variant>@<generation>)."));
            }
        }

        foreach (var (file, path, template) in templateRefs)
            if (!templates.Contains(template))
                findings.Add(new Finding(file, path, "unknown-template", $"There is no template '{template}' in templates/."));
    }

    /// <summary>
    /// Composes every template alone, with every generic modification (empty <c>appliesTo</c>) and with every
    /// modification whose <c>appliesTo</c> shares a generation with the template's. Any exception is a
    /// "compose-failed" finding.
    /// </summary>
    public static void CheckComposition(IReadOnlyList<SetupFile> setups, List<Finding> findings,
        Func<RunSetup, IReadOnlyList<RunSetup>, ComposedSetup> compose)
    {
        var templates = setups.Where(IsTemplate).OrderBy(s => s.Setup.Id, StringComparer.Ordinal).ToList();
        var modifications = setups.Where(IsModification).OrderBy(s => s.Setup.Id, StringComparer.Ordinal).ToList();

        foreach (var template in templates)
        {
            try
            {
                compose(template.Setup, []);
            }
            catch (Exception ex)
            {
                findings.Add(new Finding(template.RelativePath, "", "compose-failed", $"The template cannot be composed: {ex.Message}"));
            }
        }

        foreach (var modification in modifications)
        foreach (var template in templates)
        {
            var targets = modification.Setup.AppliesTo;
            if (targets.Count > 0 && !targets.Intersect(template.Setup.AppliesTo, StringComparer.Ordinal).Any())
                continue;
            try
            {
                compose(template.Setup, [modification.Setup]);
            }
            catch (Exception ex)
            {
                findings.Add(new Finding(modification.RelativePath, "", "compose-failed",
                    $"Composing template '{template.Setup.Id}' with this modification fails: {ex.Message}"));
            }
        }
    }

    internal static bool IsTemplate(SetupFile f) => f.FolderKind == RunSetupKind.Template && f.Setup.Kind == RunSetupKind.Template;

    internal static bool IsModification(SetupFile f) => f.FolderKind == RunSetupKind.Modification && f.Setup.Kind == RunSetupKind.Modification;

    internal static string Pointer(string key) => key.Replace("~", "~0").Replace("/", "~1");

    internal static string? Str(JsonObject o, string name) => o[name]?.GetValue<string>();

    internal static JsonArray Arr(JsonObject o, string name) => o[name] as JsonArray ?? [];
}
