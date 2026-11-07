namespace OrfVehicles.Tests;

public class CliTests
{
    private static (int Code, string Out, string Err) Run(params string[] args)
    {
        var output = new StringWriter();
        var error = new StringWriter();
        var code = Cli.Run(args, output, error);
        return (code, output.ToString(), error.ToString());
    }

    [Fact]
    public void Validate_devuelve_0_con_registro_valido()
    {
        var (code, output, _) = Run("validate", TestRegistry.Create());
        Assert.Equal(0, code);
        Assert.Contains("OK: 1 models, 2 templates, 2 modifications", output);
    }

    [Fact]
    public void Validate_devuelve_1_con_hallazgos()
    {
        var root = TestRegistry.Create((TestRegistry.Model, TestRegistry.With(TestRegistry.Model, j => j["uid"] = "nope")));
        var (code, output, _) = Run("validate", root);
        Assert.Equal(1, code);
        Assert.Contains("schema", output);
    }

    [Fact]
    public void Index_escribe_dist_index_json()
    {
        var root = TestRegistry.Create();
        var (code, _, _) = Run("index", root, "--generated-at", "2026-11-07T10:00:00Z");
        Assert.Equal(0, code);
        var golden = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "fixtures", "expected-index.json"));
        Assert.Equal(golden, File.ReadAllText(Path.Combine(root, "dist", "index.json")));
    }

    [Fact]
    public void Index_sin_fecha_es_error_de_uso() =>
        Assert.Equal(2, Run("index", TestRegistry.Create()).Code);

    [Fact]
    public void Uid_imprime_un_uuid_version_7()
    {
        var (code, output, _) = Run("uid");
        Assert.Equal(0, code);
        Assert.Equal(7, Guid.Parse(output.Trim()).Version);
    }

    [Fact]
    public void Carpeta_inexistente_es_error_de_uso() =>
        Assert.Equal(2, Run("validate", Path.Combine(Path.GetTempPath(), "orf-vehicles-missing-" + Guid.NewGuid().ToString("N"))).Code);
}
