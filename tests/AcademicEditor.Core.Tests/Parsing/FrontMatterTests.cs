using AcademicEditor.Core.Parsing;

namespace AcademicEditor.Core.Tests.Parsing;

/// <summary>
/// A gramática do cabeçalho de metadados: onde ele termina e o que dá para ler dele.
/// </summary>
/// <remarks>
/// A garantia de fundo é <b>não engolir texto</b>: o cabeçalho só existe no topo e só fechado.
/// Errar para o outro lado esconderia a tese de quem digitou três hifens por engano, e não haveria
/// nada na tela dizendo por quê.
/// </remarks>
public sealed class FrontMatterTests
{
    private const string WithHeader = "---\ntitle: Uma tese\nauthor: Alguém\n---\ncorpo";

    [Fact]
    public void O_corpo_comeca_depois_da_cerca_que_fecha()
    {
        Assert.Equal(WithHeader.IndexOf("corpo", StringComparison.Ordinal), FrontMatter.BodyStart(WithHeader));
    }

    [Theory]
    [InlineData("corpo comum")]
    // Abriu e não fechou: é texto, e o documento inteiro continua visível.
    [InlineData("---\ntitle: x\ncorpo")]
    // A cerca não está no topo.
    [InlineData("corpo\n---\ntitle: x\n---\n")]
    // Uma cerca sozinha, sem linha seguinte, não tem o que fechar.
    [InlineData("---")]
    [InlineData("")]
    [InlineData("--\ntitle: x\n--\ncorpo")]
    public void Sem_cabecalho_o_corpo_comeca_no_zero(string source)
    {
        Assert.Equal(0, FrontMatter.BodyStart(source));
    }

    [Fact]
    public void Cabecalho_vazio_e_cabecalho()
    {
        Assert.Equal(8, FrontMatter.BodyStart("---\n---\ncorpo"));
    }

    [Fact]
    public void A_cerca_aceita_espacos_em_volta()
    {
        // "  ---  \n" são oito caracteres, "---\n" mais quatro.
        Assert.Equal(12, FrontMatter.BodyStart("  ---  \n---\ncorpo"));
    }

    [Theory]
    [InlineData("title", "Uma tese")]
    [InlineData("author", "Alguém")]
    public void As_chaves_declaradas_sao_lidas(string key, string expected)
    {
        Assert.Equal(expected, FrontMatter.Read(WithHeader, key));
    }

    [Fact]
    public void Chave_ausente_e_nula()
    {
        Assert.Null(FrontMatter.Read(WithHeader, "bib"));
    }

    [Fact]
    public void Chave_sem_valor_e_nula()
    {
        Assert.Null(FrontMatter.Read("---\ntitle:   \n---\ncorpo", "title"));
    }

    /// <remarks>
    /// Uma chave escrita no corpo não é metadado. Sem isto, a linha "title: outro" de um parágrafo
    /// qualquer mudaria o cabeçalho de todas as folhas.
    /// </remarks>
    [Fact]
    public void O_corpo_nao_e_lido()
    {
        Assert.Null(FrontMatter.Read("---\nauthor: x\n---\ntitle: isto e texto", "title"));
    }

    /// <remarks>A primeira ganha, que é o que qualquer leitor de YAML faz.</remarks>
    [Fact]
    public void Chave_repetida_resolve_pela_primeira()
    {
        Assert.Equal("um", FrontMatter.Read("---\ntitle: um\ntitle: dois\n---\nc", "title"));
    }

    [Fact]
    public void Sem_cabecalho_nao_ha_o_que_ler()
    {
        Assert.Null(FrontMatter.Read("title: isto e texto\n\ncorpo", "title"));
    }
}
