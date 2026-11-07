using System.Text.Json;
using System.Text.Json.Nodes;
using Json.Schema;

namespace OrfVehicles.Tests;

public class IndexTests
{
    private static readonly DateTimeOffset GeneratedAt = new(2026, 11, 7, 10, 0, 0, TimeSpan.Zero);

    private static string Golden => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "fixtures", "expected-index.json"));

    [Fact]
    public void Indice_coincide_con_el_esperado()
    {
        var index = IndexBuilder.Build(TestRegistry.Create(), GeneratedAt);
        Assert.Equal(Golden, index);

        var schema = JsonSchema.FromText(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "schema", "index.schema.json")));
        using var doc = JsonDocument.Parse(index);
        Assert.True(schema.Evaluate(doc.RootElement).IsValid);
    }

    [Fact]
    public void Generar_dos_veces_da_los_mismos_bytes()
    {
        var root = TestRegistry.Create();
        Assert.Equal(IndexBuilder.Build(root, GeneratedAt), IndexBuilder.Build(root, GeneratedAt));
    }

    [Fact]
    public void Ficheros_con_crlf_dan_el_mismo_indice()
    {
        var root = TestRegistry.Create();
        foreach (var file in Directory.GetFiles(root, "*", SearchOption.AllDirectories))
            File.WriteAllText(file, File.ReadAllText(file).Replace("\r\n", "\n").Replace("\n", "\r\n"));
        Assert.Equal(Golden, IndexBuilder.Build(root, GeneratedAt));
    }

    [Fact]
    public void Indice_con_lf_y_salto_final()
    {
        var index = IndexBuilder.Build(TestRegistry.Create(), GeneratedAt);
        Assert.DoesNotContain("\r", index);
        Assert.EndsWith("}\n", index);
    }

    [Fact]
    public void Indice_sin_detalles_de_procedencia()
    {
        var index = IndexBuilder.Build(TestRegistry.Create(), GeneratedAt);
        Assert.DoesNotContain("contributor", index);
        Assert.DoesNotContain("provenance", index);
    }

    [Fact]
    public void Plantillas_y_modificaciones_van_enteras()
    {
        var index = JsonNode.Parse(IndexBuilder.Build(TestRegistry.Create(), GeneratedAt))!;
        var template = index["templates"]!.AsArray().Single(t => t!["id"]!.GetValue<string>() == "generic-basic")!;
        Assert.True(JsonNode.DeepEquals(TestRegistry.Load(TestRegistry.GenericTemplate), template["file"]));
        var modification = index["modifications"]!.AsArray().Single(t => t!["id"]!.GetValue<string>() == "mod-example-wing")!;
        Assert.True(JsonNode.DeepEquals(TestRegistry.Load(TestRegistry.ModelModification), modification["file"]));
        Assert.Equal(TestRegistry.Gen1, modification["appliesTo"]![0]!.GetValue<string>());
    }

    [Fact]
    public void Generacion_lleva_su_id_completo()
    {
        var index = JsonNode.Parse(IndexBuilder.Build(TestRegistry.Create(), GeneratedAt))!;
        var gen = index["models"]![0]!["variants"]![0]!["generations"]![0]!;
        Assert.Equal(TestRegistry.Gen1, gen["fullId"]!.GetValue<string>());
    }

    [Fact]
    public void Fecha_de_generacion_en_utc_sin_fraccion()
    {
        var index = IndexBuilder.Build(TestRegistry.Create(), new DateTimeOffset(2026, 11, 7, 11, 0, 0, 123, TimeSpan.FromHours(1)));
        Assert.Contains("\"generatedAt\": \"2026-11-07T10:00:00Z\"", index);
    }

    [Fact]
    public void Registro_invalido_no_se_indexa()
    {
        var root = TestRegistry.Create((TestRegistry.Model, TestRegistry.With(TestRegistry.Model, j => j["uid"] = "nope")));
        Assert.Throws<InvalidOperationException>(() => IndexBuilder.Build(root, GeneratedAt));
    }

    [Fact]
    public void Registro_vacio_da_listas_vacias()
    {
        var index = JsonNode.Parse(IndexBuilder.Build(TestRegistry.CreateEmpty(), GeneratedAt))!;
        Assert.Empty(index["models"]!.AsArray());
        Assert.Empty(index["templates"]!.AsArray());
        Assert.Empty(index["modifications"]!.AsArray());
    }
}
