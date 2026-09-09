using AcademicEditor.Core.Layout;
using AcademicEditor.Core.Layout.Model;
using AcademicEditor.Core.Parsing;
using AcademicEditor.Core.State;
using AcademicEditor.Core.Tests.Layout;

namespace AcademicEditor.Core.Tests.State;

public sealed class SelectionGeometryTests
{
    // 10pt por caractere e 20pt de altura: 10 caracteres por linha, 5 linhas por página.
    private static readonly PageSettings Settings =
        PageSettings.Uniform(widthPt: 120.0, heightPt: 120.0, marginPt: 10.0);

    private static readonly FakeTextMeasurer Measurer = new();

    [Fact]
    public void Selecao_vazia_nao_pinta_nada()
    {
        var document = Layout("abcdef");

        Assert.Empty(SelectionGeometry.RectsFor(SelectionOf(document, 3, 3), document, Measurer));
    }

    [Fact]
    public void Selecao_dentro_de_uma_linha_e_um_retangulo()
    {
        var document = Layout("abcdef");

        var rect = Assert.Single(
            SelectionGeometry.RectsFor(SelectionOf(document, 1, 4), document, Measurer));

        Assert.Equal(new SelectionRect(0, 10.0, 0.0, 30.0, 20.0), rect);
    }

    // A seleção é um trecho, não uma direção: arrastar da direita para a esquerda destaca os
    // mesmos pixels.
    [Fact]
    public void Arrastar_para_tras_pinta_o_mesmo()
    {
        var document = Layout("abcdef");

        Assert.Equal(
            SelectionGeometry.RectsFor(SelectionOf(document, 1, 4), document, Measurer),
            SelectionGeometry.RectsFor(SelectionOf(document, 4, 1), document, Measurer));
    }

    [Fact]
    public void Selecao_cruzando_quebra_explicita_pinta_uma_linha_por_vez()
    {
        var document = Layout("abc\ndef");

        var rects = SelectionGeometry.RectsFor(SelectionOf(document, 1, 6), document, Measurer);

        Assert.Equal(
            [
                new SelectionRect(0, 10.0, 0.0, 20.0, 20.0),
                new SelectionRect(0, 0.0, 20.0, 20.0, 20.0),
            ],
            rects);
    }

    // Numa quebra por largura o fim de uma linha e o começo da seguinte são o mesmo offset. As
    // duas pontas resolvem para dentro do trecho — começo Downstream, fim Upstream —, e é o que
    // faz o destaque sair contíguo em vez de com um retângulo de largura zero pendurado.
    [Fact]
    public void Selecao_cruzando_quebra_por_largura_sai_contigua()
    {
        // "aaaaa " fica na primeira linha (o espaço pendurado na margem); "bbbbbbbbb" na segunda.
        var document = Layout("aaaaa bbbbbbbbb");

        var rects = SelectionGeometry.RectsFor(SelectionOf(document, 2, 9), document, Measurer);

        Assert.Equal(
            [
                new SelectionRect(0, 20.0, 0.0, 40.0, 20.0),
                new SelectionRect(0, 0.0, 20.0, 30.0, 20.0),
            ],
            rects);
    }

    [Fact]
    public void Selecao_cruzando_fronteira_de_pagina_muda_de_folha()
    {
        // Seis linhas, cinco por folha.
        var document = Layout(string.Join("\n", Enumerable.Repeat("aaaa", 6)));

        var rects = SelectionGeometry.RectsFor(
            SelectionOf(document, 0, document.Pages[1].Lines[0].SourceEnd),
            document,
            Measurer);

        Assert.Equal(6, rects.Count);
        Assert.Equal(5, rects.Count(rect => rect.PageIndex == 0));

        // A última linha abre a folha seguinte, e o Y volta ao topo da área de conteúdo.
        Assert.Equal(new SelectionRect(1, 0.0, 0.0, 40.0, 20.0), rects[^1]);
    }

    // A lasca existe para dizer "a quebra de linha também está aqui". Sem ela, uma linha em
    // branco no meio da seleção pareceria um buraco.
    [Fact]
    public void Linha_em_branco_dentro_do_trecho_ganha_a_largura_de_um_espaco()
    {
        var document = Layout("abc\n\ndef");

        var rects = SelectionGeometry.RectsFor(SelectionOf(document, 1, 7), document, Measurer);

        Assert.Equal(3, rects.Count);
        Assert.Equal(new SelectionRect(0, 0.0, 20.0, 10.0, 20.0), rects[1]);
    }

    // O trecho que só termina no começo da linha em branco não pegou nada dela — e uma lasca ali
    // prometeria um caractere selecionado que não existe.
    [Fact]
    public void Linha_em_branco_no_fim_do_trecho_nao_ganha_lasca()
    {
        var document = Layout("abc\n\ndef");

        var rects = SelectionGeometry.RectsFor(SelectionOf(document, 1, 4), document, Measurer);

        Assert.Equal(new SelectionRect(0, 10.0, 0.0, 20.0, 20.0), Assert.Single(rects));
    }

    [Fact]
    public void Documento_vazio_nao_pinta_nada()
    {
        var document = Layout("");

        Assert.Empty(SelectionGeometry.RectsFor(SelectionOf(document, 0, 0), document, Measurer));
    }

    private static Selection SelectionOf(PaginatedDocument document, int anchor, int active) =>
        new(anchor, CaretNavigator.At(active, document, Measurer));

    private static PaginatedDocument Layout(string source) =>
        LayoutEngine.Layout(MarkupParser.Parse(source), Settings, Measurer);
}
