using AcademicEditor.App.Rendering;

using AcademicEditor.Core.Layout;
using AcademicEditor.Core.Parsing;
using AcademicEditor.Core.Text;

using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;

namespace AcademicEditor.App.Diagnostics;

/// <summary>
/// Grava uma folha do documento como PNG, exatamente como o aplicativo a desenharia.
/// </summary>
/// <remarks>
/// <para>
/// <b>Existe porque "o app abriu e ficou de pé" não é conferência.</b> Duas entregas da Fase 6
/// passaram pelo gate com defeitos visíveis em menos de um minuto de digitação real — margem
/// furada, caret na folha errada —, e o que faltava não era teste: era olhar a folha. Esta máquina
/// já estava montada para o <c>--measure-render</c>; o que faltava era gravar o bitmap em vez de
/// descartá-lo.
/// </para>
/// <para>
/// Desenha com o <c>DrawingContext</c> de verdade, pelo mesmo <see cref="PageRenderer"/> que a tela
/// usa, sobre a <see cref="DocumentConfiguration"/> que o aplicativo executa. Um caminho de desenho
/// próprio aqui responderia por um aplicativo que ninguém abre — que é justamente o defeito que
/// esta ferramenta existe para pegar.
/// </para>
/// </remarks>
public static class PageSnapshot
{
    /// <summary>O fundo da janela, atrás da folha — <c>MainWindow.axaml</c>.</summary>
    private static readonly IBrush SurfaceBrush = new SolidColorBrush(Color.FromRgb(0xE4, 0xE4, 0xE7));

    /// <summary>
    /// Desenha a folha <paramref name="pageNumber"/> de <paramref name="sourcePath"/> em
    /// <paramref name="path"/>.
    /// </summary>
    /// <param name="sourcePath">
    /// O <c>.md</c> a paginar. <c>null</c> usa o corpus do banco de provas, que é o documento com
    /// marcação de ~300 folhas — o mesmo em que a latência é medida.
    /// </param>
    /// <returns>Quantas folhas o documento tem, para quem chamou saber o que pedir.</returns>
    public static int Capture(string path, string? sourcePath, int pageNumber)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(pageNumber);

        var source = sourcePath is null
            ? LayoutBenchmark.Corpus()
            : LineEndings.NormalizeToLf(File.ReadAllText(sourcePath));

        // O mesmo cache que o aplicativo usa: sem ele uma folha de tese custa a paginação inteira
        // sem desconto, e a conferência fica lenta à toa.
        var measurer = new CachingTextMeasurer(new AvaloniaTextMeasurer());
        var settings = DocumentConfiguration.Page;

        // Pelo LayoutEngine.Publish, e não pelos passes um a um: a conferência tem de ver
        // exatamente o documento que o aplicativo publica. Montar a sequência aqui foi o primeiro
        // defeito que esta ferramenta pegou — dela mesma, num sumário sem número nenhum. Pelo mesmo
        // motivo o cabeçalho de metadados vale aqui: um documento que declara outra norma tem de
        // ser conferido nela.
        var metadata = DocumentMetadata.From(source);
        var norm = metadata.NormOver(DocumentConfiguration.Typography);

        var paginated = LayoutEngine.Publish(
            MarkupParser.Parse(source, norm),
            settings,
            measurer,
            metadata.BandsOver(DocumentConfiguration.Bands),
            preset: norm);

        var pageWidthDip = settings.WidthPt * PageRenderer.PtToDip;
        var pageHeightDip = settings.HeightPt * PageRenderer.PtToDip;
        var widthDip = pageWidthDip + (2.0 * PageRenderer.PageGapDip);
        var heightDip = pageHeightDip + (2.0 * PageRenderer.PageGapDip);

        var index = Math.Clamp(pageNumber - 1, 0, paginated.Pages.Count - 1);

        // A pilha inteira é desenhada nas coordenadas da superfície; para enquadrar uma folha,
        // translada-se a pilha em vez de recortar o bitmap. O viewport continua sendo o mesmo
        // retângulo, e é o que faz o culling desenhar só esta folha — o mesmo caminho da tela.
        var offsetDip = index * (pageHeightDip + PageRenderer.PageGapDip);
        var viewport = new Rect(0.0, offsetDip, widthDip, heightDip);

        using var target = new RenderTargetBitmap(
            new PixelSize((int)Math.Ceiling(widthDip), (int)Math.Ceiling(heightDip)),
            new Vector(96.0, 96.0));

        using (var context = target.CreateDrawingContext())
        {
            context.FillRectangle(SurfaceBrush, new Rect(0.0, 0.0, widthDip, heightDip));

            using (context.PushTransform(Matrix.CreateTranslation(0.0, -offsetDip)))
            {
                PageRenderer.Render(context, paginated, widthDip, caret: null, viewport);
            }
        }

        var directory = Path.GetDirectoryName(Path.GetFullPath(path));

        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        // Avalonia 12 aposentou o Save(caminho) sem opções — a quarta surpresa da mesma família,
        // depois do FocusChangedEventArgs, do BringIntoView(Rect) e do clipboard.
        target.Save(path, PngBitmapEncoderOptions.Default);

        return paginated.Pages.Count;
    }
}
