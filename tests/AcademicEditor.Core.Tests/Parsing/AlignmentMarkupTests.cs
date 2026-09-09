using AcademicEditor.Core.Layout;
using AcademicEditor.Core.Parsing;
using AcademicEditor.Core.Parsing.Ast;

namespace AcademicEditor.Core.Tests.Parsing;

/// <summary>A marcação de alinhamento no parser.</summary>
public sealed class AlignmentMarkupTests
{
    private static readonly TypographyPreset Justified =
        TypographyPreset.Default with { Alignment = TextAlignment.Justify };

    [Theory]
    [InlineData(":- texto", TextAlignment.Left)]
    [InlineData(":-: texto", TextAlignment.Center)]
    [InlineData("-: texto", TextAlignment.Right)]
    public void A_marcacao_decide_o_alinhamento_do_bloco(string source, TextAlignment expected)
    {
        Assert.Equal(expected, Single(source).Alignment);
    }

    /// <remarks>
    /// Sem marcação vale a norma, e é o que torna desnecessária uma marcação para justificado.
    /// </remarks>
    [Fact]
    public void Sem_marcacao_vale_o_padrao_do_preset()
    {
        Assert.Equal(TextAlignment.Justify, Single("texto").Alignment);
        Assert.Equal(TextAlignment.Left, Single("texto", TypographyPreset.Default).Alignment);
    }

    /// <remarks>
    /// Mesma regra do <c>#hashtag</c>, que não é título: sem o espaço, não é marcação — o que
    /// mantém uma linha começando por dois-pontos e hífen sendo texto.
    /// </remarks>
    [Theory]
    [InlineData(":-:texto")]
    [InlineData("-:texto")]
    [InlineData("texto :-: no meio")]
    public void Sem_espaco_depois_nao_e_marcacao(string source)
    {
        var block = Single(source);

        Assert.Equal(TextAlignment.Justify, block.Alignment);
        Assert.DoesNotContain(block.Runs, run => run.IsMarkup);
    }

    /// <remarks>
    /// A marcação é um run como o <c>## </c> de um título, com o mesmo estilo do bloco: revelá-la
    /// muda a largura da linha, nunca a altura.
    /// </remarks>
    [Fact]
    public void A_marcacao_sai_como_run_de_marcacao_no_estilo_do_bloco()
    {
        var block = Single(":-: centralizado");
        var markup = block.Runs[0];

        Assert.True(markup.IsMarkup);
        Assert.Equal(":-: ", markup.Text);
        Assert.Equal(0, markup.SourceStart);
        Assert.Equal(Justified.Body, markup.Style);
    }

    /// <remarks>
    /// Um título centralizado é a razão de o alinhamento ser lido <b>antes</b> do resto: as duas
    /// marcações convivem sem que heading e alinhamento precisem se conhecer. É o que a ABNT pede
    /// para "RESUMO" e "REFERÊNCIAS".
    /// </remarks>
    [Fact]
    public void Alinhamento_e_titulo_convivem_na_mesma_linha()
    {
        var heading = Assert.IsType<HeadingNode>(Single(":-: # Título"));

        Assert.Equal(1, heading.Level);
        Assert.Equal(TextAlignment.Center, heading.Alignment);

        // Dois runs de marcação, cada um cobrindo o seu trecho da fonte, e o texto depois deles.
        Assert.Equal([":-: ", "# ", "Título"], heading.Runs.Select(run => run.Text));
        Assert.Equal([true, true, false], heading.Runs.Select(run => run.IsMarkup));
        Assert.Equal([0, 4, 6], heading.Runs.Select(run => run.SourceStart));
    }

    /// <remarks>
    /// O texto de um run continua sendo cópia literal da fonte, marcação inclusa: é a invariante do
    /// <c>InlineRun</c>, e é dela que o caret vive.
    /// </remarks>
    [Fact]
    public void Os_offsets_dos_runs_continuam_cobrindo_a_fonte_de_ponta_a_ponta()
    {
        const string Source = ":-: um dois";
        var block = Single(Source);

        Assert.Equal(0, block.Runs[0].SourceStart);
        Assert.Equal(Source.Length, block.Runs[^1].SourceEnd);
    }

    private static BlockNode Single(string source, TypographyPreset? preset = null) =>
        Assert.Single(MarkupParser.Parse(source, preset ?? Justified).Blocks);
}
