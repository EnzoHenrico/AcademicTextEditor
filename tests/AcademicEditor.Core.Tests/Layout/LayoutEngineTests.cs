using AcademicEditor.Core.Layout;
using AcademicEditor.Core.Layout.Model;
using AcademicEditor.Core.Parsing;

namespace AcademicEditor.Core.Tests.Layout;

/// <summary>
/// Integração do motor: da fonte em markup até o documento paginado, com medidor determinístico.
/// </summary>
public sealed class LayoutEngineTests
{
    // 10 caracteres de corpo por linha, 5 linhas de corpo por página.
    private static readonly PageSettings Settings = PageSettings.Uniform(widthPt: 120.0, heightPt: 120.0, marginPt: 10.0);

    private static readonly FakeTextMeasurer Measurer = new();

    [Fact]
    public void Documento_vazio_produz_uma_pagina_em_branco()
    {
        var paginated = Layout("");

        Assert.Empty(Assert.Single(paginated.Pages).Lines);
    }

    [Fact]
    public void Paginado_carrega_a_geometria_usada()
    {
        Assert.Equal(Settings, Layout("texto").Settings);
    }

    [Fact]
    public void Paragrafo_longo_transborda_para_a_pagina_seguinte()
    {
        // 8 palavras de 9 caracteres: uma por linha, 5 linhas por página.
        var source = string.Join(' ', Enumerable.Repeat("aaaaaaaaa", 8));

        var paginated = Layout(source);

        Assert.Equal(2, paginated.Pages.Count);
        Assert.Equal(5, paginated.Pages[0].Lines.Count);
        Assert.Equal(3, paginated.Pages[1].Lines.Count);
    }

    [Fact]
    public void Quebra_explicita_no_markup_chega_ate_a_paginacao()
    {
        var paginated = Layout("Antes\n\n\\page\n\nDepois");

        Assert.Equal(2, paginated.Pages.Count);
        Assert.Equal("Antes", TextOf(paginated.Pages[0]));
        Assert.Equal("Depois", TextOf(paginated.Pages[1]));
    }

    // O heading é mais alto que o corpo, então consome mais altura útil da página. Se o motor
    // ignorasse o estilo, a contagem de páginas do documento inteiro sairia errada.
    [Fact]
    public void Heading_ocupa_mais_altura_que_um_paragrafo()
    {
        var withHeading = Layout("# T\n\naaa\n\naaa\n\naaa\n\naaa");
        var withoutHeading = Layout("T\n\naaa\n\naaa\n\naaa\n\naaa");

        Assert.Equal(2, withHeading.Pages.Count);
        Assert.Single(withoutHeading.Pages);
    }

    [Fact]
    public void Area_de_conteudo_nao_positiva_e_erro_de_programacao()
    {
        var impossible = PageSettings.Uniform(widthPt: 100.0, heightPt: 100.0, marginPt: 50.0);

        Assert.Throws<ArgumentOutOfRangeException>(
            () => LayoutEngine.Layout(MarkupParser.Parse("texto"), impossible, Measurer));
    }

    private static PaginatedDocument Layout(string source) =>
        LayoutEngine.Layout(MarkupParser.Parse(source), Settings, Measurer);

    private static string TextOf(PageLayout page) =>
        string.Concat(page.Lines.SelectMany(line => line.Runs).Select(run => run.Text));
}
