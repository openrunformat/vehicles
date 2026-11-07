using System.Text.Json.Nodes;
using OpenRunFormat.Setup;

namespace OrfVehicles.Tests;

public class SemanticTests
{
    private static IReadOnlyList<Finding> Validate(params (string Path, string? Content)[] changes) =>
        RegistryValidator.Validate(TestRegistry.Create(changes));

    private static string ModelWith(Action<JsonObject> mutate) => TestRegistry.With(TestRegistry.Model, mutate);

    private static JsonObject Gen(JsonObject model, int index) =>
        model["variants"]![0]!["generations"]![index]!.AsObject();

    [Fact]
    public void Validador_rechaza_appliesTo_desconocido_e_ids_duplicados()
    {
        // A modification for a generation that is not in the registry.
        var unknown = Validate((TestRegistry.ModelModification, TestRegistry.With(TestRegistry.ModelModification,
            j => j["appliesTo"] = new JsonArray("example-make/example-model/base@gen9"))));
        var f = Assert.Single(unknown);
        Assert.Equal("unknown-applies-to", f.Code);
        Assert.Equal(TestRegistry.ModelModification, f.File);
        Assert.Equal("/appliesTo/0", f.Path);

        // Two model files with the same id.
        var copy = ModelWith(j =>
        {
            j["uid"] = "01a19c89-ee4a-7000-8000-000000000101";
            Gen(j, 0)["uid"] = "01a19c89-ee4a-7000-8000-000000000102";
            Gen(j, 1)["uid"] = "01a19c89-ee4a-7000-8000-000000000103";
            j.Remove("external");
        });
        var duplicate = Validate(("models/example-make/example-model-copy.json", copy));
        // The file read later is the one reported; both are in the message.
        var dup = Assert.Single(duplicate, x => x.Code == "duplicate-id" && x.Path == "/id");
        Assert.Contains("models/example-make/example-model", dup.ToString());
    }

    [Theory]
    [InlineData("values")]
    [InlineData("vehicle")]
    [InlineData("extensions")]
    public void Ficheros_del_registro_no_llevan_valores_vehiculo_ni_extensiones(string field)
    {
        JsonNode content = field == "extensions"
            ? new JsonObject { ["x-example"] = new JsonObject { ["owner"] = "someone" } }
            : new JsonObject { ["camber.fl"] = "-0.03" };
        foreach (var file in new[] { TestRegistry.GenericTemplate, TestRegistry.GenericModification })
        {
            var findings = Validate((file, TestRegistry.With(file, j => j[field] = content.DeepClone())));
            var f = Assert.Single(findings);
            Assert.Equal("registry-values", f.Code);
            Assert.Equal(file, f.File);
            Assert.Equal("/" + field, f.Path);
        }
    }

    [Fact]
    public void AppliesTo_de_una_plantilla_tambien_se_comprueba()
    {
        var findings = Validate((TestRegistry.ModelTemplate, TestRegistry.With(TestRegistry.ModelTemplate,
            j => j["appliesTo"] = new JsonArray("example-make/example-model"))));
        Assert.Contains(findings, f => f.Code == "unknown-applies-to" && f.File == TestRegistry.ModelTemplate);
    }

    [Fact]
    public void Generacion_repetida_en_un_modelo_es_id_duplicado()
    {
        var findings = Validate((TestRegistry.Model, ModelWith(j => Gen(j, 1)["id"] = "gen1")));
        Assert.Contains(findings, f => f.Code == "duplicate-id" && f.Path == "/variants/0/generations/1/id");
    }

    [Fact]
    public void Plantilla_y_modificacion_con_el_mismo_id()
    {
        var findings = Validate(("modifications/generic-basic.runsetup",
            TestRegistry.With(TestRegistry.GenericModification, j => j["id"] = "generic-basic")));
        var f = Assert.Single(findings, x => x.Code == "duplicate-id");
        Assert.Contains("modifications/generic-basic.runsetup", f.ToString());
        Assert.Contains("templates/generic-basic.runsetup", f.ToString());
    }

    [Fact]
    public void Uid_repetido()
    {
        var findings = Validate((TestRegistry.Model, ModelWith(j => Gen(j, 1)["uid"] = Gen(j, 0)["uid"]!.GetValue<string>())));
        Assert.Contains(findings, f => f.Code == "duplicate-uid" && f.Path == "/variants/0/generations/1/uid");
    }

    [Fact]
    public void Uid_que_no_es_version_7()
    {
        var findings = Validate((TestRegistry.Model, ModelWith(j => j["uid"] = "01a19c89-ee4a-4000-8000-000000000001")));
        Assert.Contains(findings, f => f.Code == "uid-not-v7" && f.Path == "/uid");
    }

    [Fact]
    public void Generacion_con_plantilla_inexistente()
    {
        var findings = Validate((TestRegistry.Model, ModelWith(j => Gen(j, 1)["template"] = "no-such-template")));
        var f = Assert.Single(findings);
        Assert.Equal("unknown-template", f.Code);
        Assert.Equal("/variants/0/generations/1/template", f.Path);
    }

    [Fact]
    public void Generacion_que_apunta_a_una_modificacion()
    {
        var findings = Validate((TestRegistry.Model, ModelWith(j => Gen(j, 1)["template"] = "mod-example-coilover")));
        Assert.Contains(findings, f => f.Code == "unknown-template");
    }

    [Fact]
    public void Sistema_externo_desconocido()
    {
        var findings = Validate((TestRegistry.Model, ModelWith(j => j["external"]![0]!["system"] = "elsewhere")));
        Assert.Contains(findings, f => f.Code == "system-unknown" && f.Path == "/external/0/system");
    }

    [Fact]
    public void Id_externo_con_formato_incorrecto()
    {
        var findings = Validate((TestRegistry.Model, ModelWith(j => j["make"]!["external"]![0]!["id"] = "Q0")));
        Assert.Contains(findings, f => f.Code == "external-pattern" && f.Path == "/make/external/0/id");
    }

    [Fact]
    public void Id_externo_repetido_entre_modelos()
    {
        var other = ModelWith(j =>
        {
            j["id"] = "example-make/other-model";
            j["uid"] = "01a19c89-ee4a-7000-8000-000000000201";
            Gen(j, 0)["uid"] = "01a19c89-ee4a-7000-8000-000000000202";
            Gen(j, 1)["uid"] = "01a19c89-ee4a-7000-8000-000000000203";
        });
        var findings = Validate(("models/example-make/other-model.json", other));
        Assert.Contains(findings, f => f.Code == "duplicate-external" && f.File == "models/example-make/other-model.json" && f.Path == "/external/0");
        // The make is shared: repeating its external id in another model is expected.
        Assert.DoesNotContain(findings, f => f.Path.StartsWith("/make", StringComparison.Ordinal));
    }

    [Fact]
    public void Misma_marca_con_otro_nombre()
    {
        var other = ModelWith(j =>
        {
            j["id"] = "example-make/other-model";
            j["uid"] = "01a19c89-ee4a-7000-8000-000000000201";
            Gen(j, 0)["uid"] = "01a19c89-ee4a-7000-8000-000000000202";
            Gen(j, 1)["uid"] = "01a19c89-ee4a-7000-8000-000000000203";
            j.Remove("external");
            j["make"]!["name"] = "Example Motors";
        });
        var findings = Validate(("models/example-make/other-model.json", other));
        Assert.Contains(findings, f => f.Code == "make-mismatch" && f.File == "models/example-make/other-model.json" && f.Path == "/make");
    }

    [Fact]
    public void Anos_invertidos()
    {
        var findings = Validate((TestRegistry.Model, ModelWith(j => Gen(j, 0)["years"] = new JsonObject { ["from"] = 2022, ["to"] = 2018 })));
        Assert.Contains(findings, f => f.Code == "years" && f.Path == "/variants/0/generations/0/years/to");
    }

    [Fact]
    public void Procedencia_oficial_sin_fuente()
    {
        var findings = Validate((TestRegistry.Model, ModelWith(j =>
        {
            var p = j["provenance"]![0]!.AsObject();
            p.Remove("source");
            p.Remove("url");
        })));
        Assert.Contains(findings, f => f.Code == "provenance" && f.Path == "/provenance/0");
    }

    [Fact]
    public void Procedencia_con_licencia_incompatible()
    {
        var findings = Validate((TestRegistry.Model, ModelWith(j => j["provenance"]![1]!["licence"] = "CC-BY-NC-4.0")));
        Assert.Contains(findings, f => f.Code == "provenance" && f.Path == "/provenance/1/licence");
    }

    [Fact]
    public void Composicion_que_falla_es_hallazgo()
    {
        var root = TestRegistry.Create();
        var findings = new List<Finding>();
        var setups = RegistryLoader.LoadSetups(root, findings);
        SemanticChecks.CheckComposition(setups, findings, (_, _) => throw new InvalidOperationException("boom"));
        Assert.Contains(findings, f => f.Code == "compose-failed" && f.File == TestRegistry.GenericModification);
        Assert.Contains(findings, f => f.Code == "compose-failed" && f.File == TestRegistry.ModelModification);
    }

    [Fact]
    public void Composicion_empareja_genericas_con_todas_y_especificas_solo_con_las_suyas()
    {
        var root = TestRegistry.Create();
        var setups = RegistryLoader.LoadSetups(root, []);
        var pairs = new List<(string, string)>();
        SemanticChecks.CheckComposition(setups, [], (t, m) =>
        {
            pairs.Add((t.Id!, string.Join(",", m.Select(x => x.Id))));
            return RunSetupComposer.Compose(t, m);
        });
        Assert.Equal(
            [
                ("example-model-gen1", ""),
                ("example-model-gen1", "mod-example-coilover"),
                ("example-model-gen1", "mod-example-wing"),
                ("generic-basic", ""),
                ("generic-basic", "mod-example-coilover"),
            ],
            pairs.Order());
    }
}
