using AcademicEditor.Core.Layout;
using AcademicEditor.Core.Layout.Model;
using AcademicEditor.Core.State;

using Avalonia;
using Avalonia.Media;
using Avalonia.Media.TextFormatting;

namespace AcademicEditor.App.Rendering;

/// <summary>
/// Desenha um <see cref="PaginatedDocument"/> como uma pilha de folhas.
/// </summary>
/// <remarks>
/// <b>Esta é a única fronteira onde pontos viram DIP.</b> O motor de layout raciocina em pontos
/// (1/72") do começo ao fim; se a conversão vazasse para dentro dele, o resultado passaria a
/// depender da tela e não serviria mais para um exportador PDF.
/// </remarks>
public static class PageRenderer
{
    /// <summary>Um ponto tipográfico (1/72") em DIP do Avalonia (1/96").</summary>
    public const double PtToDip = 96.0 / 72.0;

    /// <summary>Espaço em volta e entre as folhas — é o que faz a pilha parecer papel.</summary>
    public const double PageGapDip = 20.0;

    /// <summary>Largura do caret em DIP, não em pontos: um fio de cabelo na tela, sempre.</summary>
    private const double CaretWidthDip = 1.0;

    private static readonly IBrush PageBrush = Brushes.White;
    private static readonly IBrush CaretBrush = Brushes.Black;
    private static readonly IBrush TextBrush = Brushes.Black;
    private static readonly IPen PageBorderPen = new Pen(new SolidColorBrush(Color.FromRgb(0xC8, 0xC8, 0xC8)), 1.0);

    // Tracejado, como a marca de quebra de página de um processador de texto: diz que ali há um
    // comando do autor, e não texto — e diz sem sujar a folha com a palavra "\page".
    private static readonly IPen PageBreakPen = new Pen(
        new SolidColorBrush(Color.FromRgb(0x9A, 0x9A, 0x9A)),
        1.0,
        new DashStyle([4.0, 3.0], 0.0));

    /// <summary>Tamanho que a pilha de folhas ocupa, para o <c>ScrollViewer</c> saber o que rolar.</summary>
    public static Size MeasureStack(PaginatedDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        var pageCount = document.Pages.Count;

        return new Size(
            (document.Settings.WidthPt * PtToDip) + (2.0 * PageGapDip),
            (pageCount * document.Settings.HeightPt * PtToDip) + ((pageCount + 1) * PageGapDip));
    }

    /// <summary>
    /// Retângulo do caret em coordenadas da superfície, ou <c>null</c> quando não há onde
    /// assentá-lo.
    /// </summary>
    /// <remarks>
    /// Público porque a rolagem automática precisa exatamente do retângulo que o desenho usa.
    /// Recalcular a soma de <see cref="PageGapDip"/>, origem da folha e margem no controle é como
    /// as duas contas divergem uma da outra depois.
    /// </remarks>
    public static Rect? CaretRectDip(PaginatedDocument document, CaretPosition caret, double surfaceWidthDip)
    {
        ArgumentNullException.ThrowIfNull(document);

        // Altura zero é o documento sem linha alguma — não há onde assentar o caret.
        if (caret.HeightPt <= 0.0 || caret.PageIndex < 0 || caret.PageIndex >= document.Pages.Count)
        {
            return null;
        }

        var settings = document.Settings;
        var origin = PageOrigin(settings, caret.PageIndex, surfaceWidthDip);

        return new Rect(
            origin.X + ((settings.ContentLeftPt + caret.XPt) * PtToDip),
            origin.Y + ((settings.ContentTopPt + caret.YPt) * PtToDip),
            CaretWidthDip,
            caret.HeightPt * PtToDip);
    }

    public static void Render(
        DrawingContext context,
        PaginatedDocument document,
        double surfaceWidthDip,
        CaretPosition? caret = null)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(document);

        var settings = document.Settings;

        for (var index = 0; index < document.Pages.Count; index++)
        {
            RenderPage(context, document.Pages[index], settings, PageOrigin(settings, index, surfaceWidthDip));
        }

        if (caret is { } position && CaretRectDip(document, position, surfaceWidthDip) is { } rect)
        {
            context.FillRectangle(CaretBrush, rect);
        }
    }

    /// <summary>Canto superior esquerdo de uma folha na pilha.</summary>
    private static Point PageOrigin(PageSettings settings, int pageIndex, double surfaceWidthDip)
    {
        var pageWidthDip = settings.WidthPt * PtToDip;
        var pageHeightDip = settings.HeightPt * PtToDip;

        return new Point(
            Math.Max(PageGapDip, (surfaceWidthDip - pageWidthDip) / 2.0),
            PageGapDip + (pageIndex * (pageHeightDip + PageGapDip)));
    }

    private static void RenderPage(DrawingContext context, PageLayout page, PageSettings settings, Point origin)
    {
        var bounds = new Rect(origin, new Size(settings.WidthPt * PtToDip, settings.HeightPt * PtToDip));
        context.DrawRectangle(PageBrush, PageBorderPen, bounds);

        var contentLeftDip = origin.X + (settings.ContentLeftPt * PtToDip);
        var contentTopDip = origin.Y + (settings.ContentTopPt * PtToDip);

        foreach (var line in page.Lines)
        {
            var baselineDip = contentTopDip + ((line.YPt + line.BaselinePt) * PtToDip);

            if (line.Kind == LineKind.PageBreak)
            {
                var y = contentTopDip + ((line.YPt + (line.HeightPt / 2.0)) * PtToDip);

                context.DrawLine(
                    PageBreakPen,
                    new Point(contentLeftDip, y),
                    new Point(contentLeftDip + (settings.ContentWidthPt * PtToDip), y));

                continue;
            }

            foreach (var run in line.Runs)
            {
                // Um TextLayout por run a cada frame é caro e será substituído por glyph runs
                // cacheados quando a rolagem tiver volume de verdade (culling é Fase 4). Aqui o
                // que importa é que Render apenas desenha: nada de I/O, nada de recalcular layout.
                using var text = new TextLayout(
                    run.Text,
                    AvaloniaTextMeasurer.ToTypeface(run.Style),
                    run.Style.FontSizePt * PtToDip,
                    TextBrush);

                // O motor alinha as linhas pela baseline, não pelo topo: numa linha que mistura
                // 11pt e 20pt, alinhar pelo topo deixaria os glifos flutuando uns sobre os outros.
                text.Draw(context, new Point(contentLeftDip + (run.XPt * PtToDip), baselineDip - text.Baseline));
            }
        }
    }
}
