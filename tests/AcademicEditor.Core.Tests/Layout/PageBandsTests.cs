using AcademicEditor.Core.Layout;
using AcademicEditor.Core.Layout.Model;

namespace AcademicEditor.Core.Tests.Layout;

/// <summary>
/// Cabeçalho e rodapé: o passe que resolve os marcadores e assenta a faixa no espaço reservado.
/// </summary>
/// <remarks>
/// Com o <see cref="FakeTextMeasurer"/> as contas fecham em números redondos: cada caractere mede
/// 10pt, a linha tem 20pt de altura e a baseline fica a 16pt do topo dela.
/// </remarks>
public sealed class PageBandsTests
{
    // Área de conteúdo de 160pt de largura; faixa de 30pt em cima e outra embaixo.
    private static readonly PageSettings Settings = PageSettings.Uniform(200.0, 200.0, 20.0) with
    {
        HeaderReservedHeightPt = 30.0,
        FooterReservedHeightPt = 30.0,
    };

    private static readonly FakeTextMeasurer Measurer = new();

    [Fact]
    public void O_numero_da_pagina_e_resolvido_folha_a_folha()
    {
        var bands = new HeaderFooterSettings(PageBand.Empty with { Left = "{page}" }, PageBand.Empty);
        var result = PageBands.Apply(Document(pages: 3), bands, Measurer);

        Assert.Equal("1", Single(result.Pages[0].Header).Text);
        Assert.Equal("2", Single(result.Pages[1].Header).Text);
        Assert.Equal("3", Single(result.Pages[2].Header).Text);
    }

    /// <remarks>
    /// É o marcador que parece um ciclo e não é: a contagem de folhas só existe depois de paginar,
    /// mas a faixa não muda a altura útil da folha — a reserva é fixa e decidida antes de qualquer
    /// linha ser quebrada. Por isso o passe pode ler o total sem repaginar nada.
    /// </remarks>
    [Fact]
    public void O_total_de_folhas_e_resolvido_sem_repaginar()
    {
        var bands = new HeaderFooterSettings(PageBand.Empty with { Left = "{page}/{pages}" }, PageBand.Empty);
        var result = PageBands.Apply(Document(pages: 7), bands, Measurer);

        Assert.Equal("1/7", Single(result.Pages[0].Header).Text);
        Assert.Equal("7/7", Single(result.Pages[6].Header).Text);
    }

    [Fact]
    public void O_titulo_do_documento_e_resolvido()
    {
        var bands = new HeaderFooterSettings(
            PageBand.Empty with { Center = "{title}" },
            PageBand.Empty,
            DocumentTitle: "Dissertação");

        Assert.Equal("Dissertação", Single(PageBands.Apply(Document(1), bands, Measurer).Pages[0].Header).Text);
    }

    /// <remarks>
    /// A regra que impede o cabeçalho de cair por cima da primeira linha do texto: sem espaço
    /// reservado não há onde desenhar, e a geometria tem um dono só.
    /// </remarks>
    [Fact]
    public void Sem_reserva_nao_ha_faixa_ainda_que_haja_texto()
    {
        var document = new PaginatedDocument([new PageLayout([])], PageSettings.A4);
        var bands = new HeaderFooterSettings(PageBand.Empty with { Right = "{page}" }, PageBand.Empty);

        Assert.Empty(PageBands.Apply(document, bands, Measurer).Pages[0].Header);
    }

    [Fact]
    public void Documento_sem_faixa_atravessa_o_passe_sem_custo()
    {
        var document = Document(pages: 3);

        // A MESMA instância: sem faixa configurada não há página para reconstruir.
        Assert.Same(document, PageBands.Apply(document, HeaderFooterSettings.None, Measurer));
    }

    [Theory]
    // Esquerda encosta na origem da área de conteúdo — e não mede nada para saber disso.
    [InlineData("esquerda", 0.0)]
    // "12345" mede 50pt: centrado em (160 - 50) / 2, e encostado à direita em 160 - 50.
    [InlineData("centro", 55.0)]
    [InlineData("direita", 110.0)]
    public void Cada_campo_comeca_onde_o_alinhamento_manda(string field, double expectedXPt)
    {
        var band = field switch
        {
            "esquerda" => PageBand.Empty with { Left = "12345" },
            "centro" => PageBand.Empty with { Center = "12345" },
            _ => PageBand.Empty with { Right = "12345" },
        };

        var result = PageBands.Apply(Document(1), new HeaderFooterSettings(band, PageBand.Empty), Measurer);

        Assert.Equal(expectedXPt, Single(result.Pages[0].Header).XPt, precision: 9);
    }

    [Fact]
    public void Campo_mais_largo_que_a_area_encosta_na_margem_esquerda()
    {
        // 20 caracteres medem 200pt numa área de 160: centrado, começaria em -20 e vazaria do papel.
        var band = PageBand.Empty with { Center = new string('x', 20) };
        var result = PageBands.Apply(Document(1), new HeaderFooterSettings(band, PageBand.Empty), Measurer);

        Assert.Equal(0.0, Single(result.Pages[0].Header).XPt);
    }

    /// <remarks>
    /// O cabeçalho pendurado no alto da faixa (margem 20 + baseline 16) e o rodapé assentado no pé
    /// da dele (fundo da área útil, 180, menos a descida de 4pt abaixo da baseline). É o que faz a
    /// folga sobrar entre a faixa e o texto, em vez de entre a faixa e a borda do papel.
    /// </remarks>
    [Fact]
    public void A_faixa_de_cima_pende_do_alto_e_a_de_baixo_assenta_no_pe()
    {
        var bands = new HeaderFooterSettings(
            PageBand.Empty with { Left = "a" },
            PageBand.Empty with { Left = "b" });

        var page = PageBands.Apply(Document(1), bands, Measurer).Pages[0];

        Assert.Equal(36.0, Single(page.Header).BaselinePt, precision: 9);
        Assert.Equal(176.0, Single(page.Footer).BaselinePt, precision: 9);
    }

    /// <remarks>
    /// <b>A invariante que sustenta o resto do motor.</b> <c>CaretGeometry.FindLine</c> varre
    /// <c>Lines</c> mapeando offset para linha, e <c>LayoutEngine.Reuse</c> exige que a lista case
    /// um-para-um com os blocos do documento. Uma linha de cabeçalho ali dentro — sem offset no
    /// buffer — capturaria o caret e derrubaria o reflow incremental.
    /// </remarks>
    [Fact]
    public void A_faixa_nunca_entra_nas_linhas_da_pagina()
    {
        var lines = new[] { new LaidOutLine(0.0, 20.0, 16.0, [], 0, 5) };
        var document = new PaginatedDocument([new PageLayout(lines)], Settings);
        var bands = new HeaderFooterSettings(PageBand.Empty with { Right = "{page}" }, PageBand.Empty);

        var page = PageBands.Apply(document, bands, Measurer).Pages[0];

        Assert.Single(page.Header);
        Assert.Same(lines, page.Lines);
    }

    private static PaginatedDocument Document(int pages) =>
        new(Enumerable.Range(0, pages).Select(_ => new PageLayout([])).ToArray(), Settings);

    private static BandRun Single(IReadOnlyList<BandRun> runs) => Assert.Single(runs);
}
