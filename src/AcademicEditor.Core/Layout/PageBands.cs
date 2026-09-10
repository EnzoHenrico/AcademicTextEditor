using AcademicEditor.Core.Layout.Model;
using AcademicEditor.Core.Parsing.Ast;

namespace AcademicEditor.Core.Layout;

/// <summary>
/// Preenche o cabeçalho e o rodapé de um documento já paginado.
/// </summary>
/// <remarks>
/// <para>
/// <b>É um passe sobre o resultado, não um parâmetro do motor</b> — e é o que resolve o
/// <c>{pages}</c> sem ciclo. A contagem de folhas só existe depois de paginar; se a faixa
/// influenciasse a paginação, escrevê-la mudaria o número que ela mostra. Não influencia: a
/// altura da faixa é reserva fixa do <see cref="PageSettings"/>, decidida antes de qualquer
/// linha ser quebrada. Então este passe só mede texto e o assenta no espaço que já estava lá.
/// </para>
/// <para>
/// Fora do <c>LayoutEngine</c> também porque o motor não precisa ficar mais pesado por isto:
/// cabeçalho não influencia quebra de linha nem de página, e o caminho incremental herda o passe
/// de graça, já que quem o aplica é quem publica o layout.
/// </para>
/// </remarks>
public static class PageBands
{
    /// <summary>
    /// O mesmo documento com as faixas montadas, ou ele próprio quando não há o que desenhar.
    /// </summary>
    /// <remarks>
    /// <b>Sem reserva não há faixa</b>, ainda que haja texto configurado: desenhar sem espaço
    /// reservado poria o cabeçalho por cima da primeira linha do texto. A reserva tem um dono só,
    /// que é o mesmo <see cref="PageSettings"/> que já decide onde o conteúdo começa.
    /// </remarks>
    public static PaginatedDocument Apply(
        PaginatedDocument document,
        HeaderFooterSettings settings,
        ITextMeasurer measurer)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(measurer);

        var geometry = document.Settings;
        var hasHeader = !settings.Header.IsEmpty && geometry.HeaderReservedHeightPt > 0.0;
        var hasFooter = !settings.Footer.IsEmpty && geometry.FooterReservedHeightPt > 0.0;

        if (!hasHeader && !hasFooter)
        {
            return document;
        }

        // A faixa sai no corpo de texto do documento: é o que a norma pede e o que faz o número
        // da página parecer parte do trabalho, e não uma anotação de outra ferramenta.
        var style = document.Typography.Body;
        var metrics = measurer.GetLineMetrics(style);

        // Cabeçalho pendurado no ALTO da faixa, logo abaixo da margem; rodapé assentado no PÉ da
        // dele, logo acima da margem de baixo. É onde o olho os procura, e é o que faz a folga
        // sobrar entre a faixa e o texto, em vez de entre a faixa e a borda do papel.
        var headerBaselinePt = geometry.MarginTopPt + metrics.BaselinePt;
        var footerBaselinePt =
            geometry.HeightPt - geometry.MarginBottomPt - (metrics.HeightPt - metrics.BaselinePt);

        var pages = new PageLayout[document.Pages.Count];

        for (var index = 0; index < pages.Length; index++)
        {
            var number = index + 1;

            pages[index] = document.Pages[index] with
            {
                Header = hasHeader
                    ? Build(settings, settings.Header, number, pages.Length, headerBaselinePt, geometry, style, measurer)
                    : [],
                Footer = hasFooter
                    ? Build(settings, settings.Footer, number, pages.Length, footerBaselinePt, geometry, style, measurer)
                    : [],
            };
        }

        // WithSameLines, e não 'with { Pages = ... }': o índice de linhas em ordem de fonte é
        // reaproveitado, e este passe só acrescenta faixas às folhas — as listas de linhas
        // continuam sendo os mesmos objetos. Quem conferir isso é o documento, não um comentário.
        return document.WithSameLines(pages);
    }

    /// <remarks>
    /// <b>O campo da esquerda não mede nada</b> — ele começa na origem da área de conteúdo. Só o
    /// centro e a direita precisam da largura do próprio texto para saber onde começam, que é o
    /// mesmo raciocínio pelo qual a seleção só mede as duas linhas de fronteira.
    /// </remarks>
    private static IReadOnlyList<BandRun> Build(
        HeaderFooterSettings settings,
        PageBand band,
        int pageNumber,
        int pageCount,
        double baselinePt,
        PageSettings geometry,
        TextStyle style,
        ITextMeasurer measurer)
    {
        var left = settings.Resolve(band.Left, pageNumber, pageCount);
        var center = settings.Resolve(band.Center, pageNumber, pageCount);
        var right = settings.Resolve(band.Right, pageNumber, pageCount);

        var runs = new List<BandRun>(3);

        if (left.Length > 0)
        {
            runs.Add(new BandRun(left, style, XPt: 0.0, baselinePt));
        }

        if (center.Length > 0)
        {
            var width = measurer.MeasureWidthPt(center, style);

            runs.Add(new BandRun(center, style, StartPt((geometry.ContentWidthPt - width) / 2.0), baselinePt));
        }

        if (right.Length > 0)
        {
            var width = measurer.MeasureWidthPt(right, style);

            runs.Add(new BandRun(right, style, StartPt(geometry.ContentWidthPt - width), baselinePt));
        }

        return runs.Count == 0 ? [] : runs.ToArray();
    }

    // Um campo mais largo que a área de conteúdo — um título longo ao centro — começaria antes da
    // margem esquerda e vazaria para fora do papel. Encosta na margem e transborda só para a
    // direita, que é o lado onde o olho já espera que o texto continue.
    private static double StartPt(double xPt) => Math.Max(0.0, xPt);
}
