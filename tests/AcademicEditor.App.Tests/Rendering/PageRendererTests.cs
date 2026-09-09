using AcademicEditor.App.Rendering;

using AcademicEditor.Core.Layout;
using AcademicEditor.Core.Layout.Model;
using AcademicEditor.Core.State;

namespace AcademicEditor.App.Tests.Rendering;

/// <summary>
/// A conversão DIP ↔ pontos do <see cref="PageRenderer"/>, que é a única aritmética do App que o
/// Core não alcança.
/// </summary>
/// <remarks>
/// <para>
/// <b>É o teste que abriu este projeto.</b> O roadmap acumulou quatro argumentos para
/// <c>tests/AcademicEditor.App.Tests/</c> — o <c>availableSize</c> infinito, o teto de latência, o
/// clipboard e este — e este é o primeiro com asserção óbvia:
/// <c>HitTest(CaretRectDip(c)) == c</c> é uma propriedade de uma linha que pega um sinal trocado na
/// hora, e não tem onde morar do lado do Core, porque <c>PageRenderer</c> depende de <c>Rect</c> do
/// Avalonia.
/// </para>
/// <para>
/// Não precisa de medidor de texto nem de subsistema gráfico: as duas contas dependem só da
/// contagem de folhas e da geometria da página. O que o Core já cobre — o ida-e-volta
/// <c>AtPoint</c>/<c>Locate</c> — é a outra metade, em pontos.
/// </para>
/// </remarks>
public sealed class PageRendererTests
{
    private const double SurfaceWidthDip = 900.0;

    // Três folhas vazias: CaretRectDip e HitTest olham a contagem de páginas e a geometria, nunca
    // as linhas. Vazias é o caso mais honesto de montar à mão.
    private static readonly PaginatedDocument Document = new(
        [new PageLayout([]), new PageLayout([]), new PageLayout([])],
        PageSettings.A4);

    [Theory]
    [InlineData(0, 0.0, 0.0)]
    [InlineData(0, 120.5, 300.25)]
    [InlineData(1, 0.0, 617.0)]
    [InlineData(2, 42.0, 17.5)]
    public void HitTest_devolve_o_ponto_de_onde_CaretRectDip_desenhou(int pageIndex, double xPt, double yPt)
    {
        var caret = new CaretPosition(pageIndex, xPt, yPt, HeightPt: 12.0);
        var rect = PageRenderer.CaretRectDip(Document, caret, SurfaceWidthDip);

        Assert.NotNull(rect);

        var hit = PageRenderer.HitTest(Document, rect.Value.TopLeft, SurfaceWidthDip);

        Assert.NotNull(hit);
        Assert.Equal(pageIndex, hit.Value.PageIndex);
        Assert.Equal(xPt, hit.Value.XPt, precision: 9);
        Assert.Equal(yPt, hit.Value.YPt, precision: 9);
    }

    [Fact]
    public void Caret_sem_altura_nao_tem_onde_ser_desenhado()
    {
        // Altura zero é o documento sem linha alguma. Devolver um retângulo ali desenharia uma
        // barra de altura zero — invisível, mas mandando a rolagem automática para o lugar errado.
        Assert.Null(PageRenderer.CaretRectDip(Document, new CaretPosition(0, 0.0, 0.0, 0.0), SurfaceWidthDip));
    }

    [Fact]
    public void Caret_fora_da_pilha_de_folhas_nao_tem_onde_ser_desenhado()
    {
        var beyond = new CaretPosition(Document.Pages.Count, 0.0, 0.0, 12.0);

        Assert.Null(PageRenderer.CaretRectDip(Document, beyond, SurfaceWidthDip));
    }

    [Fact]
    public void HitTest_nao_grampeia_o_ponto_e_e_de_proposito()
    {
        // O contrato que o PageSurface consome para decidir o cursor: acima e à esquerda da área de
        // conteúdo, o resultado é NEGATIVO. Grampear aqui tornaria "estou sobre o papel ou sobre a
        // margem?" impossível de responder sem refazer a conta — e quem grampeia é o AtPoint, no
        // Core, que é onde a regra "todo clique pousa em algum lugar" tem teste.
        var hit = PageRenderer.HitTest(Document, new Avalonia.Point(0.0, 0.0), SurfaceWidthDip);

        Assert.NotNull(hit);
        Assert.Equal(0, hit.Value.PageIndex);
        Assert.True(hit.Value.XPt < 0.0, $"esperava XPt negativo na margem esquerda, veio {hit.Value.XPt}");
        Assert.True(hit.Value.YPt < 0.0, $"esperava YPt negativo acima do conteúdo, veio {hit.Value.YPt}");
    }

    [Fact]
    public void A_pilha_cresce_uma_folha_de_cada_vez()
    {
        var one = PageRenderer.MeasureStack(new PaginatedDocument([new PageLayout([])], PageSettings.A4));
        var three = PageRenderer.MeasureStack(Document);
        var step = (PageSettings.A4.HeightPt * PageRenderer.PtToDip) + PageRenderer.PageGapDip;

        Assert.Equal(one.Width, three.Width, precision: 9);
        Assert.Equal(one.Height + (2.0 * step), three.Height, precision: 9);
    }
}
