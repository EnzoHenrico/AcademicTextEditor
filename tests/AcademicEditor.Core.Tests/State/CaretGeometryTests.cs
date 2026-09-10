using AcademicEditor.Core.Layout;
using AcademicEditor.Core.Layout.Model;
using AcademicEditor.Core.Parsing;
using AcademicEditor.Core.State;
using AcademicEditor.Core.Tests.Layout;

namespace AcademicEditor.Core.Tests.State;

public sealed class CaretGeometryTests
{
    private static readonly PageSettings Settings =
        PageSettings.Uniform(widthPt: 120.0, heightPt: 120.0, marginPt: 10.0);

    private static readonly FakeTextMeasurer Measurer = new();

    [Fact]
    public void Coluna_e_medida_do_inicio_da_linha()
    {
        var line = FirstLine("abcdef");

        Assert.Equal(0.0, CaretGeometry.ColumnPt(line, 0, Measurer));
        Assert.Equal(30.0, CaretGeometry.ColumnPt(line, 3, Measurer));
        Assert.Equal(60.0, CaretGeometry.ColumnPt(line, 6, Measurer));
    }

    [Fact]
    public void Coluna_e_offset_sao_inversos()
    {
        var line = FirstLine("abcdef");

        for (var offset = 0; offset <= 6; offset++)
        {
            var column = CaretGeometry.ColumnPt(line, offset, Measurer);

            Assert.Equal(offset, CaretGeometry.OffsetAtColumn(line, column, Measurer));
        }
    }

    // Clicar na metade direita de um caractere põe o caret depois dele, não antes — é o que a
    // mão espera de qualquer editor.
    [Fact]
    public void Offset_pousa_na_fronteira_mais_proxima()
    {
        var line = FirstLine("abcdef");

        Assert.Equal(2, CaretGeometry.OffsetAtColumn(line, 24.0, Measurer));
        Assert.Equal(3, CaretGeometry.OffsetAtColumn(line, 26.0, Measurer));
    }

    [Fact]
    public void Coluna_alem_do_fim_da_linha_para_no_ultimo_caractere()
    {
        var line = FirstLine("abc");

        Assert.Equal(3, CaretGeometry.OffsetAtColumn(line, 500.0, Measurer));
    }

    [Fact]
    public void Offset_nunca_cai_entre_as_metades_de_um_par_substituto()
    {
        var line = FirstLine("😀😀😀");

        // 20pt por emoji: uma coluna de 10pt cairia no meio do primeiro par.
        var offset = CaretGeometry.OffsetAtColumn(line, 10.0, Measurer);

        Assert.Equal(0, offset % 2);
    }

    [Fact]
    public void Locate_devolve_pagina_e_altura_da_linha()
    {
        var document = LayoutEngine.Layout(MarkupParser.Parse("abc"), Settings, Measurer);

        var position = CaretGeometry.Locate(2, document, Measurer);

        Assert.Equal(0, position.PageIndex);
        Assert.Equal(20.0, position.XPt);
        Assert.Equal(0.0, position.YPt);
        Assert.Equal(20.0, position.HeightPt);
    }

    // Um offset, duas posições na tela. É o único caso em que a afinidade muda alguma coisa.
    [Fact]
    public void Fronteira_de_quebra_resolve_para_as_duas_linhas_conforme_a_afinidade()
    {
        var document = Layout("aaaaa bbbbbbbbb");

        var upstream = CaretGeometry.Locate(6, document, Measurer, CaretAffinity.Upstream);
        var downstream = CaretGeometry.Locate(6, document, Measurer, CaretAffinity.Downstream);

        Assert.Equal(0.0, upstream.YPt);
        Assert.Equal(60.0, upstream.XPt);

        Assert.Equal(20.0, downstream.YPt);
        Assert.Equal(0.0, downstream.XPt);
    }

    // Numa quebra explícita o \n ocupa uma posição entre as duas linhas, então não há empate e a
    // afinidade não pode mudar nada.
    [Fact]
    public void Quebra_explicita_nao_e_ambigua()
    {
        var document = Layout("aaa\nbbb");

        Assert.Equal(
            CaretGeometry.Locate(3, document, Measurer, CaretAffinity.Downstream),
            CaretGeometry.Locate(3, document, Measurer, CaretAffinity.Upstream));
    }

    // A fronteira compartilhada é o que distingue a quebra que o autor escreveu da que a margem
    // impôs — e é o que decide quantos \n um Enter precisa inserir ali.
    [Fact]
    public void Fronteira_compartilhada_e_so_a_da_quebra_por_largura()
    {
        var wrapped = Layout("aaaaa bbbbbbbbb");

        Assert.True(CaretGeometry.IsSharedBoundary(6, wrapped));

        Assert.False(CaretGeometry.IsSharedBoundary(5, wrapped));
        Assert.False(CaretGeometry.IsSharedBoundary(0, wrapped));
        Assert.False(CaretGeometry.IsSharedBoundary(15, wrapped));

        // O \n ocupa uma posição entre as duas linhas: nenhum offset serve às duas.
        Assert.False(CaretGeometry.IsSharedBoundary(3, Layout("aaa\nbbb")));
        Assert.False(CaretGeometry.IsSharedBoundary(4, Layout("aaa\nbbb")));
    }

    /// <summary>
    /// <b>Todo offset do documento pertence a uma linha, e à linha certa.</b>
    /// </summary>
    /// <remarks>
    /// <para>
    /// A garantia que faltava, e cuja ausência era o bug "o cursor se perde". Com a marcação
    /// escondida os runs dela somem, e a linha nascia ancorada no primeiro chunk que de fato usava:
    /// num bloco que começa com <c>#&#160;</c>, <c>:-:&#160;</c> ou <c>**negrito**</c>, os
    /// primeiros offsets do bloco não pertenciam a linha nenhuma. Quem procura a linha de um
    /// offset descoberto recebe a <b>última linha do bloco anterior</b> — e o caret é desenhado no
    /// parágrafo de cima, longe de onde se errou.
    /// </para>
    /// <para>
    /// Teste de propriedade e não de exemplo, porque o buraco muda de lugar a cada marcação nova:
    /// a Fatia 4 levou o prefixo a quatro caracteres com o <c>:-:&#160;</c>, e a 5a acrescentou
    /// fronteiras desse tipo no meio da linha.
    /// </para>
    /// </remarks>
    [Theory]
    [InlineData("# Titulo\n\ncorpo comum")]
    [InlineData("**negrito** no comeco\n\ncorpo")]
    [InlineData(":-: centralizado\n\ncorpo")]
    [InlineData(":-: # Titulo centralizado\n\ncorpo")]
    [InlineData("corpo\n\ntexto com **negrito** no meio\n\nfim")]
    [InlineData("corpo\n\ntermina em **negrito**\n\nfim")]
    [InlineData("nota[^1] e formula $E=mc^2$\n\ncorpo")]
    [InlineData("## \n\ncorpo")]
    [InlineData("a\n\n\n\nb")]
    [InlineData("corpo com nota[^1]\n\n[^1]: a definicao")]
    [InlineData("corpo\n\n[^1]: definicao que ninguem chama")]
    public void Todo_offset_do_documento_pertence_a_uma_linha(string source)
    {
        // Sem caret: é a configuração em que TODA marcação está escondida, e portanto a que tem
        // mais offsets sem run. Com o caret dentro de um bloco aquele bloco é revelado, e o
        // problema encolhe — foi o que o manteve invisível.
        var document = LayoutEngine.Layout(MarkupParser.Parse(source), Settings, Measurer, caretOffset: -1);

        for (var offset = 0; offset <= source.Length; offset++)
        {
            var covering = document.Pages
                .SelectMany((page, index) => page.Lines.Select(line => (Page: index, Line: line)))
                .Where(found => offset >= found.Line.SourceStart && offset <= found.Line.SourceEnd)
                .ToArray();

            Assert.True(
                covering.Length > 0,
                $"o offset {offset} de '{source}' não pertence a linha nenhuma");

            // E o caret vai parar numa delas. Sem cobertura, ele era desenhado na última linha do
            // bloco ANTERIOR — que é o sintoma como o autor o vê.
            var landed = CaretGeometry.Locate(offset, document, Measurer);

            Assert.Contains(landed.PageIndex, covering.Select(found => found.Page));
        }
    }

    private static PaginatedDocument Layout(string source) =>
        LayoutEngine.Layout(MarkupParser.Parse(source), Settings, Measurer);

    private static LaidOutLine FirstLine(string source) => Layout(source).Pages[0].Lines[0];
}
