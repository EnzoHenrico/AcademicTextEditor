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

    private static PaginatedDocument Layout(string source) =>
        LayoutEngine.Layout(MarkupParser.Parse(source), Settings, Measurer);

    private static LaidOutLine FirstLine(string source) => Layout(source).Pages[0].Lines[0];
}
