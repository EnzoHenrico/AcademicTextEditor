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

    // Opaco, e desenhado ATRÁS do texto: um destaque translúcido por cima mudaria a cor de cada
    // glifo, e o que o autor quer ver é o texto, marcado.
    private static readonly IBrush SelectionBrush = new SolidColorBrush(Color.FromRgb(0xB4, 0xD5, 0xFE));
    private static readonly IBrush CaretBrush = Brushes.Black;
    private static readonly IBrush TextBrush = Brushes.Black;
    private static readonly IPen PageBorderPen = new Pen(new SolidColorBrush(Color.FromRgb(0xC8, 0xC8, 0xC8)), 1.0);

    // Tracejado, como a marca de quebra de página de um processador de texto: diz que ali há um
    // comando do autor, e não texto — e diz sem sujar a folha com a palavra "\page".
    /// <summary>
    /// O filete que separa o texto das notas de rodapé.
    /// </summary>
    /// <remarks>
    /// Contínuo e curto — um terço da largura útil —, e não tracejado como o do <c>\page</c>: este
    /// vai para o papel e é convenção tipográfica, aquele é marca de edição e some no PDF.
    /// </remarks>
    private static readonly IPen FootnoteRulePen =
        new Pen(new SolidColorBrush(Color.FromRgb(0x50, 0x50, 0x50)), 0.6);

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

    /// <summary>
    /// Onde um ponto da superfície cai no documento: a folha, e a posição em pontos relativa ao
    /// canto da área de conteúdo dela. <c>null</c> só num documento sem folha alguma.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>É o inverso algébrico de <see cref="CaretRectDip"/>, e mora ao lado dele pelo motivo que
    /// aquele método já documenta:</b> recalcular a soma de <see cref="PageGapDip"/>, origem da
    /// folha e margem em outro arquivo é exatamente como as duas contas divergem uma da outra
    /// depois. Quem quiser conferir, confere lendo os dois juntos.
    /// </para>
    /// <para>
    /// A folha sai por <b>aritmética</b>, não por varredura — a mesma divisão pelo passo da pilha
    /// que <c>VisiblePages</c> usa. Um clique no vão entre duas folhas cai na de cima, porque o vão
    /// pertence ao passo dela.
    /// </para>
    /// <para>
    /// <b>Não grampeia nada</b>, e é de propósito: um ponto acima do texto devolve
    /// <c>YPt</c> negativo, e um à direita da margem devolve <c>XPt</c> maior que a largura útil.
    /// Quem resolve isso é o <c>CaretNavigator.AtPoint</c>, no Core, que já grampeia por
    /// construção — e assim a regra de "todo clique pousa em algum lugar" tem um dono só, testável
    /// sem subsistema gráfico. O sinal cru também é o que diz se o ponteiro está sobre o papel ou
    /// sobre a margem, que é o que decide o cursor.
    /// </para>
    /// </remarks>
    public static (int PageIndex, double XPt, double YPt)? HitTest(
        PaginatedDocument document,
        Point dip,
        double surfaceWidthDip)
    {
        ArgumentNullException.ThrowIfNull(document);

        if (document.Pages.Count == 0)
        {
            return null;
        }

        var settings = document.Settings;
        var stepDip = (settings.HeightPt * PtToDip) + PageGapDip;
        var pageIndex = Math.Clamp((int)((dip.Y - PageGapDip) / stepDip), 0, document.Pages.Count - 1);
        var origin = PageOrigin(settings, pageIndex, surfaceWidthDip);

        return (
            pageIndex,
            ((dip.X - origin.X) / PtToDip) - settings.ContentLeftPt,
            ((dip.Y - origin.Y) / PtToDip) - settings.ContentTopPt);
    }

    /// <param name="selection">
    /// Onde pintar o destaque, já calculado pelo <c>SelectionGeometry</c> — <c>Render</c> não
    /// recalcula nada. <b>Assume ordenado por página</b>, que é como o Core o produz: é o que
    /// permite percorrer a lista uma vez só enquanto as folhas visíveis passam.
    /// </param>
    /// <param name="viewport">
    /// Retângulo visível, nas coordenadas da superfície. Só as folhas que ele cruza são
    /// desenhadas. <c>null</c> desenha a pilha inteira — é o que uma medição ou um exportador
    /// querem, e é o comportamento de quem não vive dentro de um <c>ScrollViewer</c>.
    /// </param>
    public static void Render(
        DrawingContext context,
        PaginatedDocument document,
        double surfaceWidthDip,
        CaretPosition? caret = null,
        Rect? viewport = null,
        IReadOnlyList<SelectionRect>? selection = null)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(document);

        var settings = document.Settings;
        var (first, last) = VisiblePages(document, viewport);
        var rects = selection ?? [];

        // Índice que avança junto com as folhas, em vez de varrer a lista uma vez por folha: um
        // Ctrl+A num documento de 300 páginas produz dezesseis mil retângulos, e o culling existe
        // justamente para nenhum quadro pagar por todos eles.
        var cursor = 0;

        while (cursor < rects.Count && rects[cursor].PageIndex < first)
        {
            cursor++;
        }

        for (var index = first; index <= last; index++)
        {
            var origin = PageOrigin(settings, index, surfaceWidthDip);
            var start = cursor;

            while (cursor < rects.Count && rects[cursor].PageIndex == index)
            {
                cursor++;
            }

            RenderPage(context, document.Pages[index], settings, origin, rects, start, cursor);
        }

        if (caret is { } position && CaretRectDip(document, position, surfaceWidthDip) is { } rect)
        {
            context.FillRectangle(CaretBrush, rect);
        }
    }

    /// <summary>Intervalo de folhas que o retângulo visível cruza, inclusive nas duas pontas.</summary>
    /// <remarks>
    /// Aritmética, não varredura: a pilha é uniforme, então o índice sai de uma divisão pelo passo
    /// — altura da folha mais o vão. Percorrer as páginas para descobrir quais entram custaria
    /// O(páginas) por quadro, que é justamente o que o culling existe para não pagar.
    /// </remarks>
    private static (int First, int Last) VisiblePages(PaginatedDocument document, Rect? viewport)
    {
        var last = document.Pages.Count - 1;

        if (viewport is not { } visible || visible.Height <= 0.0)
        {
            return (0, last);
        }

        var stepDip = (document.Settings.HeightPt * PtToDip) + PageGapDip;

        return (
            Math.Clamp((int)((visible.Top - PageGapDip) / stepDip), 0, last),
            Math.Clamp((int)((visible.Bottom - PageGapDip) / stepDip), 0, last));
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

    /// <summary>
    /// Desenha uma faixa de cabeçalho ou rodapé.
    /// </summary>
    /// <remarks>
    /// As duas coordenadas do <see cref="BandRun"/> têm origens diferentes de propósito, e a conta
    /// aqui é o espelho disso: <c>XPt</c> parte da área de conteúdo, porque é o alinhamento com o
    /// bloco de texto que faz a faixa parecer parte da folha; <c>BaselinePt</c> parte do topo do
    /// papel, porque a faixa vive fora da área de conteúdo e um Y relativo a ela seria negativo.
    /// </remarks>
    private static void DrawBand(
        DrawingContext context,
        IReadOnlyList<BandRun> runs,
        Point origin,
        double contentLeftDip)
    {
        foreach (var run in runs)
        {
            using var text = new TextLayout(
                run.Text,
                AvaloniaTextMeasurer.ToTypeface(run.Style),
                run.Style.FontSizePt * PtToDip,
                TextBrush);

            text.Draw(
                context,
                new Point(
                    contentLeftDip + (run.XPt * PtToDip),
                    origin.Y + (run.BaselinePt * PtToDip) - text.Baseline));
        }
    }

    private static void RenderPage(
        DrawingContext context,
        PageLayout page,
        PageSettings settings,
        Point origin,
        IReadOnlyList<SelectionRect> selection,
        int selectionStart,
        int selectionEnd)
    {
        var bounds = new Rect(origin, new Size(settings.WidthPt * PtToDip, settings.HeightPt * PtToDip));
        context.DrawRectangle(PageBrush, PageBorderPen, bounds);

        var contentLeftDip = origin.X + (settings.ContentLeftPt * PtToDip);
        var contentTopDip = origin.Y + (settings.ContentTopPt * PtToDip);

        // Cabeçalho e rodapé vêm de campos próprios, nunca de page.Lines: eles não têm offset no
        // buffer, e o caret varre aquela lista. Ver PageLayout e BandRun.
        DrawBand(context, page.Header, origin, contentLeftDip);
        DrawBand(context, page.Footer, origin, contentLeftDip);

        // Depois do papel e antes do texto: é a ordem que faz o destaque marcar o texto em vez de
        // apagá-lo.
        for (var index = selectionStart; index < selectionEnd; index++)
        {
            var rect = selection[index];

            context.FillRectangle(
                SelectionBrush,
                new Rect(
                    contentLeftDip + (rect.XPt * PtToDip),
                    contentTopDip + (rect.YPt * PtToDip),
                    rect.WidthPt * PtToDip,
                    rect.HeightPt * PtToDip));
        }

        if (page.FootnoteRulePt is { } rulePt)
        {
            var y = contentTopDip + (rulePt * PtToDip);

            context.DrawLine(
                FootnoteRulePen,
                new Point(contentLeftDip, y),
                new Point(contentLeftDip + (settings.ContentWidthPt * PtToDip / 3.0), y));
        }

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
                // Um TextLayout por run a cada quadro é caro, e é por isso que o culling acima
                // existe: com ele são as ~3 folhas visíveis, não as 301 do documento. Cachear os
                // glyph runs em cima disso é otimizar o que deixou de doer — entra se a medição
                // voltar a apontar para cá. Aqui o que importa é que Render apenas desenha: nada
                // de I/O, nada de recalcular layout.
                using var text = new TextLayout(
                    run.Text,
                    AvaloniaTextMeasurer.ToTypeface(run.Style),
                    run.Style.FontSizePt * PtToDip,
                    TextBrush);

                // O motor alinha as linhas pela baseline, não pelo topo: numa linha que mistura
                // 11pt e 20pt, alinhar pelo topo deixaria os glifos flutuando uns sobre os outros.
                //
                // A subida do sobrescrito é a única coisa que desloca um run em Y, e sai do próprio
                // estilo para que a tela e o PDF levantem pelo mesmo tanto. A largura não muda, então
                // caret, seleção e line breaker seguem sem saber que este run é sobrescrito.
                text.Draw(
                    context,
                    new Point(
                        contentLeftDip + (run.XPt * PtToDip),
                        baselineDip - (run.Style.BaselineRisePt * PtToDip) - text.Baseline));
            }
        }
    }
}
