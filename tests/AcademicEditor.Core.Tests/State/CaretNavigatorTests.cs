using AcademicEditor.Core.Layout;
using AcademicEditor.Core.Layout.Model;
using AcademicEditor.Core.Parsing;
using AcademicEditor.Core.State;
using AcademicEditor.Core.Tests.Layout;

namespace AcademicEditor.Core.Tests.State;

public sealed class CaretNavigatorTests
{
    // 10pt por caractere e 20pt de altura: 10 caracteres por linha, 5 linhas por página.
    private static readonly PageSettings Settings =
        PageSettings.Uniform(widthPt: 120.0, heightPt: 120.0, marginPt: 10.0);

    private static readonly FakeTextMeasurer Measurer = new();

    [Fact]
    public void Setas_horizontais_andam_caractere_a_caractere()
    {
        var document = Layout("abcdef");
        var caret = CaretNavigator.At(0, document, Measurer);

        caret = CaretNavigator.MoveRight(caret, document, Measurer);
        caret = CaretNavigator.MoveRight(caret, document, Measurer);

        Assert.Equal(2, caret.Offset);
        Assert.Equal(20.0, caret.DesiredColumnPt);

        caret = CaretNavigator.MoveLeft(caret, document, Measurer);

        Assert.Equal(1, caret.Offset);
        Assert.Equal(10.0, caret.DesiredColumnPt);
    }

    [Fact]
    public void Setas_horizontais_nao_estouram_o_documento()
    {
        var document = Layout("abc");

        var start = CaretNavigator.At(0, document, Measurer);
        Assert.Equal(start, CaretNavigator.MoveLeft(start, document, Measurer));

        var end = CaretNavigator.At(3, document, Measurer);
        Assert.Equal(end, CaretNavigator.MoveRight(end, document, Measurer));
    }

    [Fact]
    public void Setas_verticais_nao_estouram_o_documento()
    {
        var document = Layout("uma linha só");

        var caret = CaretNavigator.At(0, document, Measurer);

        Assert.Equal(caret, CaretNavigator.MoveUp(caret, document, Measurer));
    }

    // O motivo de o caret guardar uma coluna alvo. Descer de uma linha longa para uma curta e
    // continuar descendo tem que voltar à coluna original, não grudar no fim da linha curta.
    [Fact]
    public void Coluna_alvo_sobrevive_a_travessia_de_linha_curta()
    {
        // Três parágrafos: longo, curto, longo. Cada um vira uma linha.
        var document = Layout("aaaaaaaa\n\nbb\n\ncccccccc");

        var caret = CaretNavigator.At(7, document, Measurer);
        Assert.Equal(70.0, caret.DesiredColumnPt);

        caret = CaretNavigator.MoveDown(caret, document, Measurer);
        Assert.Equal(70.0, caret.DesiredColumnPt);

        caret = CaretNavigator.MoveDown(caret, document, Measurer);

        // De volta à coluna 7 da terceira linha, que começa no offset 14.
        Assert.Equal(70.0, CaretGeometry.ColumnPt(LineOf(document, caret.Offset), caret.Offset, Measurer));
    }

    // Home e End param no limite visual da quebra, não no do parágrafo — é a folha que manda.
    [Fact]
    public void Home_e_End_respeitam_a_quebra_visual_e_nao_o_paragrafo()
    {
        // Um parágrafo de 15 caracteres numa linha de 10: quebra em duas linhas visuais.
        var document = Layout("aaaaa bbbbbbbbb");
        var caret = CaretNavigator.At(8, document, Measurer);

        var end = CaretNavigator.MoveToLineEnd(caret, document, Measurer);
        var start = CaretNavigator.MoveToLineStart(caret, document, Measurer);

        Assert.Equal(6, start.Offset);
        Assert.Equal(0.0, start.DesiredColumnPt);
        Assert.Equal(15, end.Offset);
        Assert.True(end.Offset < 16, "End parou no fim da linha visual, não no fim do parágrafo");
    }

    [Fact]
    public void Navegacao_vertical_cruza_a_fronteira_de_pagina()
    {
        // Seis linhas de conteúdo numa página que comporta cinco.
        var document = Layout(string.Join("\n\n", Enumerable.Repeat("aaaa", 6)));
        Assert.Equal(2, document.Pages.Count);

        // Última linha da primeira página.
        var caret = CaretNavigator.At(document.Pages[0].Lines[4].SourceStart, document, Measurer);
        Assert.Equal(0, CaretGeometry.Locate(caret.Offset, document, Measurer).PageIndex);

        caret = CaretNavigator.MoveDown(caret, document, Measurer);

        Assert.Equal(1, CaretGeometry.Locate(caret.Offset, document, Measurer).PageIndex);

        caret = CaretNavigator.MoveUp(caret, document, Measurer);

        Assert.Equal(0, CaretGeometry.Locate(caret.Offset, document, Measurer).PageIndex);
    }

    [Fact]
    public void PageDown_e_PageUp_trocam_de_folha()
    {
        var document = Layout(string.Join("\n\n", Enumerable.Repeat("aaaa", 12)));
        Assert.Equal(3, document.Pages.Count);

        var caret = CaretNavigator.At(0, document, Measurer);

        caret = CaretNavigator.MovePageDown(caret, document, Measurer);
        Assert.Equal(1, CaretGeometry.Locate(caret.Offset, document, Measurer).PageIndex);

        caret = CaretNavigator.MovePageDown(caret, document, Measurer);
        Assert.Equal(2, CaretGeometry.Locate(caret.Offset, document, Measurer).PageIndex);

        caret = CaretNavigator.MovePageUp(caret, document, Measurer);
        Assert.Equal(1, CaretGeometry.Locate(caret.Offset, document, Measurer).PageIndex);
    }

    [Fact]
    public void PageDown_na_ultima_folha_vai_para_a_ultima_linha()
    {
        var document = Layout("aaaa\n\nbbbb");

        var caret = CaretNavigator.At(0, document, Measurer);
        caret = CaretNavigator.MovePageDown(caret, document, Measurer);

        var lines = document.Pages[0].Lines;
        Assert.Equal(lines[^1].SourceStart, caret.Offset);
    }

    // A marcação do heading não é desenhada, então o caret não pousa nela: seguir para a direita
    // do fim de um bloco cai no primeiro caractere visível do bloco seguinte.
    [Fact]
    public void Caret_pula_a_marcacao_que_nao_e_desenhada()
    {
        const string Source = "# T\n\nabc";
        var document = Layout(Source);

        // Fim do texto do heading: "T" ocupa o offset 2.
        var caret = CaretNavigator.At(3, document, Measurer);
        caret = CaretNavigator.MoveRight(caret, document, Measurer);

        Assert.Equal(Source.IndexOf('a', StringComparison.Ordinal), caret.Offset);
    }

    [Fact]
    public void Par_substituto_e_atravessado_de_uma_vez()
    {
        var document = Layout("a😀b");

        var caret = CaretNavigator.At(1, document, Measurer);
        caret = CaretNavigator.MoveRight(caret, document, Measurer);

        Assert.Equal(3, caret.Offset);

        caret = CaretNavigator.MoveLeft(caret, document, Measurer);

        Assert.Equal(1, caret.Offset);
    }

    [Fact]
    public void Documento_vazio_mantem_o_caret_no_inicio()
    {
        var document = Layout("");
        var caret = CaretNavigator.At(0, document, Measurer);

        Assert.Equal(caret, CaretNavigator.MoveRight(caret, document, Measurer));
        Assert.Equal(caret, CaretNavigator.MoveDown(caret, document, Measurer));
        Assert.Equal(0, caret.Offset);
    }

    private static PaginatedDocument Layout(string source) =>
        LayoutEngine.Layout(MarkupParser.Parse(source), Settings, Measurer);

    private static LaidOutLine LineOf(PaginatedDocument document, int offset)
    {
        var position = CaretGeometry.Locate(offset, document, Measurer);

        return document.Pages[position.PageIndex].Lines
            .First(line => line.SourceStart <= offset && offset <= line.SourceEnd);
    }
}
