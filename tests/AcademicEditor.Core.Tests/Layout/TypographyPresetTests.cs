using AcademicEditor.Core.Layout;
using AcademicEditor.Core.Parsing;
using AcademicEditor.Core.Parsing.Ast;

namespace AcademicEditor.Core.Tests.Layout;

/// <summary>
/// A norma tipográfica: com que fonte e em que corpo o documento é composto, e como o
/// entrelinhamento chega à altura da linha.
/// </summary>
public sealed class TypographyPresetTests
{
    private static readonly PageSettings Settings =
        PageSettings.Uniform(widthPt: 120.0, heightPt: 120.0, marginPt: 10.0);

    private static readonly FakeTextMeasurer Measurer = new();

    /// <remarks>
    /// <see cref="TextStyle.Body"/> é o ponto fixo dos testes e o preset padrão é o do motor. Os
    /// dois dizem "corpo 11pt na fonte do sistema" em lugares diferentes, e este teste existe para
    /// que não divirjam sem que ninguém perceba — o dia em que divergirem, toda medida redonda dos
    /// testes de layout passa a mentir.
    /// </remarks>
    [Fact]
    public void O_preset_padrao_e_o_estilo_de_referencia_do_motor()
    {
        Assert.Equal(TextStyle.Body, TypographyPreset.Default.Body);
    }

    [Fact]
    public void Entrelinhamento_simples_devolve_as_metricas_naturais()
    {
        var natural = new LineMetrics(HeightPt: 20.0, BaselinePt: 16.0);

        Assert.Equal(natural, TypographyPreset.Default.Apply(natural));
    }

    /// <remarks>
    /// É o que "a folga fica acima da linha" quer dizer, e é verificável sem falar de pixel: o que
    /// sobra <b>abaixo</b> da baseline não muda. Distribuir a folga pelos dois lados moveria os
    /// glifos dentro da própria caixa e faria o texto subir meio espaço em toda linha.
    /// </remarks>
    [Fact]
    public void A_folga_do_entrelinhamento_fica_acima_da_linha()
    {
        var natural = new LineMetrics(HeightPt: 20.0, BaselinePt: 16.0);
        var applied = (TypographyPreset.Default with { LineSpacing = 1.5 }).Apply(natural);

        Assert.Equal(30.0, applied.HeightPt);
        Assert.Equal(26.0, applied.BaselinePt);
        Assert.Equal(natural.HeightPt - natural.BaselinePt, applied.HeightPt - applied.BaselinePt);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(7)]
    public void Titulo_fora_de_1_a_6_nao_existe(int level)
    {
        var sizes = TypographyPreset.Default.HeadingSizesPt;

        Assert.Throws<ArgumentOutOfRangeException>(() => sizes[level]);
    }

    /// <remarks>
    /// Com corpo 12, a escala do MVP deixava H5 e H6 em 11pt — títulos <i>menores</i> que o texto
    /// que titulam. É a única coisa que a norma diz sobre o corpo dos títulos: que se distingam.
    /// </remarks>
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    public void Nenhum_titulo_da_ABNT_sai_menor_que_o_corpo(int level)
    {
        Assert.True(
            TypographyPreset.Abnt.HeadingSizesPt[level] >= TypographyPreset.Abnt.BodySizePt,
            $"título de nível {level} saiu menor que o corpo do texto");
    }

    [Fact]
    public void O_parser_compoe_com_o_preset_que_recebe()
    {
        var preset = new TypographyPreset(
            "Fonte X",
            BodySizePt: 13.0,
            new HeadingSizes(30.0, 28.0, 26.0, 24.0, 22.0, 20.0),
            LineSpacing: 1.0,
            TextAlignment.Left);

        var blocks = MarkupParser.Parse("# Título\ncorpo", preset).Blocks;

        // O run 0 do heading é a marcação: sai com o MESMO estilo do título, e é isso que faz
        // revelá-la mudar a largura da linha e nunca a altura.
        Assert.All(blocks[0].Runs, run => Assert.Equal(preset.Heading(1), run.Style));
        Assert.All(blocks[1].Runs, run => Assert.Equal(preset.Body, run.Style));
        Assert.Equal("Fonte X", blocks[1].Runs[0].Style.FontFamily);
    }

    /// <remarks>
    /// O caminho inteiro: preset → line breaker → altura da linha → quantas cabem na folha. Sem
    /// isto, o entrelinhamento poderia estar sendo aplicado no desenho, onde ninguém que consome
    /// <c>LaidOutLine</c> — page breaker, caret, seleção, exportador — o veria.
    /// </remarks>
    [Fact]
    public void O_entrelinhamento_chega_a_altura_da_linha_e_ao_numero_de_folhas()
    {
        // Seis linhas de fonte, altura natural 20pt e área útil de 100pt: cinco por folha em
        // entrelinhamento simples, três quando cada linha passa a ocupar 30pt.
        var source = string.Join('\n', Enumerable.Repeat("aa", 6));

        var single = Paginate(source, TypographyPreset.Default);
        var oneAndHalf = Paginate(source, TypographyPreset.Default with { LineSpacing = 1.5 });

        Assert.Equal(20.0, single.Pages[0].Lines[0].HeightPt);
        Assert.Equal(30.0, oneAndHalf.Pages[0].Lines[0].HeightPt);

        Assert.Equal(5, single.Pages[0].Lines.Count);
        Assert.Equal(3, oneAndHalf.Pages[0].Lines.Count);

        Assert.Equal(2, single.Pages.Count);
        Assert.Equal(2, oneAndHalf.Pages.Count);
    }

    /// <remarks>
    /// Espelha <c>Geometria_diferente_cai_para_a_paginacao_completa</c>: são as duas metades da
    /// norma, e nenhuma sobrevive a uma troca. Reaproveitar linhas medidas em outra fonte não
    /// quebra o desenho — quebra o caret, porque os offsets deixam de descrever onde a tinta está.
    /// </remarks>
    [Fact]
    public void Preset_diferente_cai_para_a_paginacao_completa()
    {
        const string Before = "aaa bbb\n\nccc";
        const string After = "aaa bXbb\n\nccc";

        var spaced = TypographyPreset.Default with { LineSpacing = 1.5 };
        var previous = Paginate(Before, TypographyPreset.Default);

        var reuse = LayoutReuse.Between(Before, After, previous);
        Assert.NotNull(reuse);

        var reused = LayoutEngine.Layout(
            MarkupParser.Parse(After, spaced), Settings, Measurer, caretOffset: 5, reuse, spaced);

        // Se o reaproveitamento tivesse sido aceito, as linhas de antes viriam com 20pt de altura.
        Assert.Equal(30.0, reused.Pages[0].Lines[0].HeightPt);
        Assert.Equal(spaced, reused.Typography);
    }

    [Fact]
    public void O_layout_carimba_o_preset_que_o_produziu()
    {
        Assert.Equal(TypographyPreset.Abnt, Paginate("texto", TypographyPreset.Abnt).Typography);
    }

    private static Core.Layout.Model.PaginatedDocument Paginate(string source, TypographyPreset preset) =>
        LayoutEngine.Layout(MarkupParser.Parse(source, preset), Settings, Measurer, preset: preset);
}
