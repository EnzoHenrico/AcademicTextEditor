using AcademicEditor.Core.Layout;
using AcademicEditor.Core.Parsing;
using AcademicEditor.Core.Parsing.Ast;

namespace AcademicEditor.Core.Tests.Parsing;

/// <summary>Chamada de nota (<c>[^1]</c>) e fórmula (<c>$x$</c>) dentro de uma linha.</summary>
public sealed class AcademicMarkupTests
{
    [Fact]
    public void A_chamada_de_nota_vira_tres_runs_com_o_identificador_sobrescrito()
    {
        var runs = Parse("texto[^1] segue");

        Assert.Equal(["texto", "[^", "1", "]", " segue"], runs.Select(run => run.Text));
        Assert.Equal([false, true, false, true, false], runs.Select(run => run.IsMarkup));

        var id = runs[2].Style;

        Assert.True(id.Superscript);
        Assert.Equal(TextStyle.Body.FontSizePt * TextStyle.SuperscriptScale, id.FontSizePt, precision: 9);
    }

    /// <remarks>
    /// Variável em itálico é como texto matemático se compõe, e é o que sai impresso. <b>Parsear e
    /// estilizar, não tipografar</b>: não há quem componha TeX aqui.
    /// </remarks>
    [Fact]
    public void A_formula_vira_tres_runs_com_o_corpo_em_italico()
    {
        var runs = Parse("seja $x + y$ maior");

        Assert.Equal(["seja ", "$", "x + y", "$", " maior"], runs.Select(run => run.Text));
        Assert.True(runs[2].Style.Italic);
        Assert.False(runs[2].Style.Superscript);
    }

    /// <remarks>
    /// As mesmas duas condições de flanqueamento do negrito. Sem elas, uma frase com dois preços
    /// viraria uma fórmula do primeiro cifrão ao segundo.
    /// </remarks>
    [Theory]
    [InlineData("custa R$ 50 e R$ 70")]
    [InlineData("um $ solto")]
    [InlineData("vazio $$ aqui")]
    public void Cifrao_sem_par_ou_encostado_em_branco_continua_sendo_texto(string source)
    {
        Assert.DoesNotContain(Parse(source), run => run.IsMarkup);
    }

    /// <remarks>
    /// O identificador é rótulo, não texto: sem essa regra um <c>[^</c> solto engoliria o resto do
    /// parágrafo até achar um <c>]</c> qualquer.
    /// </remarks>
    [Theory]
    [InlineData("um [^ dois] tres")]
    [InlineData("um [^] dois")]
    [InlineData("um [^sem fechamento")]
    public void Chamada_malformada_continua_sendo_texto(string source)
    {
        Assert.DoesNotContain(Parse(source), run => run.IsMarkup);
    }

    /// <remarks>
    /// Fatiar a linha antes de entregá-la ao <c>InlineMarkup</c> é o que mantém as duas regras
    /// separadas — e é o que faz um asterisco dentro de uma fórmula continuar sendo multiplicação.
    /// </remarks>
    [Fact]
    public void Asterisco_dentro_de_formula_nao_e_enfase()
    {
        var runs = Parse("$a * b$");

        Assert.Equal(["$", "a * b", "$"], runs.Select(run => run.Text));
    }

    [Fact]
    public void Enfase_fora_da_formula_continua_valendo()
    {
        var runs = Parse("**forte** e $z$");

        Assert.Equal(["**", "forte", "**", " e ", "$", "z", "$"], runs.Select(run => run.Text));
        Assert.Equal(FontWeightKind.Bold, runs[1].Style.Weight);
        Assert.True(runs[5].Style.Italic);
    }

    /// <remarks>
    /// <b>A invariante do <c>InlineRun</c>:</b> todo caractere da linha entra em exatamente um run,
    /// na ordem, cópia literal. É dela que o caret vive — e é ela que impede numerar a nota
    /// automaticamente, porque <c>¹</c> não está no arquivo.
    /// </remarks>
    [Theory]
    [InlineData("texto[^1] segue")]
    [InlineData("seja $x + y$ maior")]
    [InlineData("[^a]$b$[^c]")]
    [InlineData("**forte** e $z$")]
    public void Os_runs_cobrem_a_linha_inteira_na_ordem_e_sem_inventar_caractere(string source)
    {
        var runs = Parse(source);
        var rebuilt = string.Concat(runs.Select(run => run.Text));

        Assert.Equal(source, rebuilt);

        var expected = 0;

        foreach (var run in runs)
        {
            Assert.Equal(expected, run.SourceStart);
            expected = run.SourceEnd;
        }

        Assert.Equal(source.Length, expected);
    }

    [Fact]
    public void Linha_sem_notacao_nenhuma_atravessa_sem_mudar_nada()
    {
        var runs = Parse("uma linha comum");

        Assert.Equal("uma linha comum", Assert.Single(runs).Text);
    }

    [Fact]
    public void A_chamada_de_nota_chega_pelo_parser_de_blocos()
    {
        var block = Assert.Single(MarkupParser.Parse("Afirmação[^1].", TypographyPreset.Default).Blocks);

        Assert.Contains(block.Runs, run => run.Style.Superscript);
    }

    private static IReadOnlyList<InlineRun> Parse(string text) =>
        AcademicMarkup.Parse(text, sourceStart: 0, TextStyle.Body);
}
