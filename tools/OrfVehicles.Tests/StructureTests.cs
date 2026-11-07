using System.Text.Json.Nodes;

namespace OrfVehicles.Tests;

public class StructureTests
{
    private static IReadOnlyList<Finding> Validate(params (string Path, string? Content)[] changes) =>
        RegistryValidator.Validate(TestRegistry.Create(changes));

    [Fact]
    public void Registro_sintetico_valida_sin_hallazgos() =>
        Assert.Empty(RegistryValidator.Validate(TestRegistry.Create()));

    [Fact]
    public void Registro_vacio_es_valido() =>
        Assert.Empty(RegistryValidator.Validate(TestRegistry.CreateEmpty()));

    [Fact]
    public void Campo_desconocido_en_el_modelo_es_hallazgo_de_esquema()
    {
        var findings = Validate((TestRegistry.Model, TestRegistry.With(TestRegistry.Model, j => j["colour"] = "red")));
        Assert.Contains(findings, f => f.Code == "schema" && f.File == TestRegistry.Model);
    }

    [Fact]
    public void Falta_un_campo_obligatorio_del_modelo()
    {
        var findings = Validate((TestRegistry.Model, TestRegistry.With(TestRegistry.Model, j => j.Remove("variants"))));
        Assert.Contains(findings, f => f.Code == "schema" && f.File == TestRegistry.Model);
    }

    [Fact]
    public void Ambito_externo_de_la_marca_debe_ser_make()
    {
        var findings = Validate((TestRegistry.Model, TestRegistry.With(TestRegistry.Model, j => j["make"]!["external"]![0]!["scope"] = "model")));
        Assert.Contains(findings, f => f.Code == "schema" && f.Path.StartsWith("/make/external/0", StringComparison.Ordinal));
    }

    [Fact]
    public void Json_roto_es_hallazgo_json()
    {
        var findings = Validate((TestRegistry.Model, "{ \"uid\": "));
        Assert.Contains(findings, f => f.Code == "json" && f.File == TestRegistry.Model);
    }

    [Fact]
    public void Propiedad_repetida_es_hallazgo_json()
    {
        var findings = Validate((TestRegistry.GenericModification,
            "{ \"orf\": \"1.3\", \"orf\": \"1.3\", \"kind\": \"modification\", \"id\": \"mod-example-coilover\", \"version\": 1, \"name\": { \"en\": \"x\" } }"));
        Assert.Contains(findings, f => f.Code == "json" && f.File == TestRegistry.GenericModification);
    }

    [Fact]
    public void Modelo_guardado_con_otro_nombre_de_fichero()
    {
        var findings = Validate((TestRegistry.Model, null), ("models/example-make/other.json", File.ReadAllText(Path.Combine(TestRegistry.FixtureRoot, TestRegistry.Model))));
        Assert.Contains(findings, f => f.Code == "file-name" && f.File == "models/example-make/other.json");
    }

    [Fact]
    public void Marca_distinta_del_prefijo_del_id()
    {
        var findings = Validate((TestRegistry.Model, TestRegistry.With(TestRegistry.Model, j => j["make"]!["id"] = "other-make")));
        Assert.Contains(findings, f => f.Code == "make-mismatch" && f.Path == "/make/id");
    }

    [Fact]
    public void Plantilla_en_carpeta_de_modificaciones_se_rechaza()
    {
        var template = TestRegistry.With(TestRegistry.GenericTemplate, j => j["id"] = "moved-template");
        var findings = Validate(("modifications/moved-template.runsetup", template));
        var f = Assert.Single(findings);
        Assert.Equal("kind-mismatch", f.Code);
        Assert.Equal("modifications/moved-template.runsetup", f.File);
    }

    [Fact]
    public void Modificacion_en_carpeta_de_plantillas_se_rechaza()
    {
        var modification = TestRegistry.With(TestRegistry.GenericModification, j => j["id"] = "moved-modification");
        var findings = Validate(("templates/moved-modification.runsetup", modification));
        Assert.Contains(findings, f => f.Code == "kind-mismatch" && f.File == "templates/moved-modification.runsetup");
    }

    [Fact]
    public void Reglaje_completo_no_entra_en_el_registro()
    {
        var setup = "{ \"orf\": \"1.3\", \"kind\": \"setup\", \"id\": \"a-setup\", \"version\": 1, \"name\": { \"en\": \"x\" }, \"values\": {} }";
        var findings = Validate(("templates/a-setup.runsetup", setup));
        Assert.Contains(findings, f => f.Code == "kind-mismatch" && f.File == "templates/a-setup.runsetup");
    }

    [Fact]
    public void Runsetup_que_la_biblioteca_rechaza()
    {
        var findings = Validate((TestRegistry.GenericTemplate, TestRegistry.With(TestRegistry.GenericTemplate, j => j.Remove("kind"))));
        Assert.Contains(findings, f => f.Code == "runsetup" && f.File == TestRegistry.GenericTemplate);
    }

    [Fact]
    public void Runsetup_guardado_con_otro_nombre_de_fichero()
    {
        var findings = Validate((TestRegistry.GenericTemplate, null),
            ("templates/other-name.runsetup", File.ReadAllText(Path.Combine(TestRegistry.FixtureRoot, TestRegistry.GenericTemplate))));
        Assert.Contains(findings, f => f.Code == "file-name" && f.File == "templates/other-name.runsetup");
    }

    [Fact]
    public void Id_de_runsetup_con_sintaxis_invalida()
    {
        var findings = Validate((TestRegistry.GenericTemplate, null),
            ("templates/Generic_Basic.runsetup", TestRegistry.With(TestRegistry.GenericTemplate, j => j["id"] = "Generic_Basic")));
        Assert.Contains(findings, f => f.Code == "id-syntax" && f.File == "templates/Generic_Basic.runsetup");
    }

    [Fact]
    public void Runsetup_anterior_a_1_3_se_rechaza()
    {
        var findings = Validate((TestRegistry.GenericTemplate, TestRegistry.With(TestRegistry.GenericTemplate, j => j["orf"] = "1.2")));
        Assert.Contains(findings, f => f.Code == "orf-version" && f.File == TestRegistry.GenericTemplate);
    }

    [Fact]
    public void Entrada_que_los_lectores_ignorarian_es_hallazgo()
    {
        var findings = Validate((TestRegistry.GenericModification, TestRegistry.With(TestRegistry.GenericModification,
            j => j["operations"]!.AsArray().Add(new JsonObject { ["op"] = "add", ["key"] = "x-undescribed" }))));
        Assert.Contains(findings, f => f.Code == "ignored-entry" && f.File == TestRegistry.GenericModification && f.Path == "/operations/4");
    }

    [Fact]
    public void Parametro_mal_descrito_es_hallazgo()
    {
        var findings = Validate((TestRegistry.GenericTemplate, TestRegistry.With(TestRegistry.GenericTemplate,
            j => j["parameters"]!["x-broken"] = new JsonObject { ["type"] = "nonsense" })));
        Assert.Contains(findings, f => f.Code == "ignored-entry" && f.File == TestRegistry.GenericTemplate && f.Path == "/parameters/x-broken");
    }

    [Theory]
    [InlineData("models/stray.txt")]
    [InlineData("models/example-make/notes.txt")]
    [InlineData("models/example-make/nested/x.json")]
    [InlineData("templates/notes.txt")]
    [InlineData("modifications/sub/x.runsetup")]
    public void Fichero_fuera_de_sitio_es_hallazgo(string path)
    {
        var findings = Validate((path, "x"));
        Assert.Contains(findings, f => f.Code == "stray-file");
    }

    [Theory]
    [InlineData("models/README.md")]
    [InlineData("templates/README.md")]
    [InlineData("modifications/README.md")]
    public void Readme_en_las_carpetas_se_admite(string path) =>
        Assert.Empty(Validate((path, "Readme.\n")));

    [Fact]
    public void Texto_con_caracteres_de_control_es_hallazgo_de_esquema()
    {
        var findings = Validate((TestRegistry.Model, TestRegistry.With(TestRegistry.Model, j => j["name"] = "Example\nModel")));
        Assert.Contains(findings, f => f.Code == "schema" && f.Path == "/name");
    }

    [Fact]
    public void Bom_utf8_es_hallazgo_json()
    {
        var text = File.ReadAllText(Path.Combine(TestRegistry.FixtureRoot, TestRegistry.Model));
        var root = TestRegistry.Create();
        File.WriteAllText(Path.Combine(root, TestRegistry.Model), text, new System.Text.UTF8Encoding(true));
        Assert.Contains(RegistryValidator.Validate(root), f => f.Code == "json" && f.File == TestRegistry.Model);
    }
}
