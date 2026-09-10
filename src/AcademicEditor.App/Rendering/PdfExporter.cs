using System.Collections.Concurrent;

using AcademicEditor.Core.Layout;
using AcademicEditor.Core.Layout.Model;
using AcademicEditor.Core.Parsing.Ast;

using SkiaSharp;

namespace AcademicEditor.App.Rendering;

/// <summary>
/// Grava um <see cref="PaginatedDocument"/> como PDF.
/// </summary>
/// <remarks>
/// <para>
/// <b>É a prova da aposta que abriu o projeto.</b> O motor de layout nasceu independente de tela —
/// raciocina em pontos, não conhece o toolkit, e devolve um resultado imutável. Este exportador
/// consome exatamente o mesmo <see cref="PaginatedDocument"/> que a tela desenha: se ele precisasse
/// paginar de novo, ou paginar diferente, a aposta teria saído errada.
/// </para>
/// <para>
/// <b>Nenhuma conversão de unidade.</b> O canvas de PDF do Skia já é em pontos tipográficos, que é
/// a unidade interna do motor — este é o único consumidor do layout que não multiplica nada. O
/// <c>PageRenderer</c> converte para DIP porque a tela é que tem outra unidade.
/// </para>
/// <para>
/// <b>Skia, e não uma biblioteca de PDF de alto nível.</b> É o mesmo motor que molda o texto na
/// tela — o mesmo argumento que escolheu <c>TextLayout</c> em vez de somar avanços de glifo na
/// Fatia 2 da Fase 3. Onde medir e desenhar discordam o texto vaza da folha, e aqui discordar
/// significaria entregar um PDF diferente do que o autor viu.
/// </para>
/// <para>
/// <b>Mora no App, e isso não é concessão.</b> Consumir <see cref="PaginatedDocument"/> de fora do
/// Core é exatamente o que a regra "o Core nunca referencia o toolkit" existia para permitir. Nada
/// aqui depende do Avalonia: um exportador de linha de comando reusaria este arquivo como está.
/// </para>
/// </remarks>
public static class PdfExporter
{
    // Poucos, imutáveis e reusados entre exportações — a mesma política do cache de FontFamily do
    // AvaloniaTextMeasurer. Um documento tem menos de trinta estilos distintos, e descartá-los
    // custaria mais do que guardá-los. O SKTypeface padrão é compartilhado pelo processo e não
    // deve ser descartado por ninguém, o que por si só já desaconselha um cache com dono.
    private static readonly ConcurrentDictionary<TextStyle, SKFont> Fonts = new();

    private static readonly SKPaint Ink = new() { Color = SKColors.Black };

    /// <summary>Grava o documento no caminho dado, substituindo o que estiver lá.</summary>
    /// <remarks>
    /// Escreve num temporário no mesmo diretório e move por cima, como o save do <c>.md</c> já faz:
    /// morrer no meio da escrita deixaria um PDF pela metade no lugar do anterior, que já não
    /// existiria para recuperar. Mover só é atômico dentro do mesmo volume, daí o temporário ficar
    /// ao lado do destino e não em /tmp.
    /// </remarks>
    public static void Export(PaginatedDocument document, string path, string title = "")
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentException.ThrowIfNullOrEmpty(path);

        var directory = Path.GetDirectoryName(Path.GetFullPath(path));

        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var temporary = Path.Combine(
            directory ?? ".",
            $".{Path.GetFileName(path)}.{Guid.NewGuid():N}.tmp");

        try
        {
            using (var file = File.Create(temporary))
            {
                Export(document, file, title);
            }

            File.Move(temporary, path, overwrite: true);
        }
        catch
        {
            TryDelete(temporary);
            throw;
        }
    }

    /// <summary>Escreve o PDF no fluxo dado. Não fecha o fluxo.</summary>
    public static void Export(PaginatedDocument document, Stream stream, string title = "")
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(stream);

        var metadata = new SKDocumentPdfMetadata
        {
            Title = title,
            Creator = "AcademicEditor",
            Producer = "AcademicEditor",
        };

        using var pdf = SKDocument.CreatePdf(stream, metadata);
        var settings = document.Settings;

        foreach (var page in document.Pages)
        {
            // A folha do PDF é a folha do documento, em pontos. Sem retângulo branco e sem borda:
            // aquilo é a pilha de papel desenhada na tela, não conteúdo. Uma página de PDF já é
            // branca, e desenhar a borda poria um traço no papel impresso.
            var canvas = pdf.BeginPage((float)settings.WidthPt, (float)settings.HeightPt);

            DrawPage(canvas, page, settings);

            pdf.EndPage();
        }

        pdf.Close();
    }

    /// <remarks>
    /// <b>O PDF desenha o documento, não o editor.</b> Ficam de fora o caret, o destaque da
    /// seleção, a borda da folha e o filete tracejado do <c>\page</c>: os quatro existem para quem
    /// está escrevendo, e nenhum deles é texto que o leitor deva encontrar na página impressa. O
    /// marcador em especial já fez o trabalho dele — a folha terminou ali.
    /// </remarks>
    private static void DrawPage(SKCanvas canvas, PageLayout page, PageSettings settings)
    {
        var contentLeft = (float)settings.ContentLeftPt;
        var contentTop = (float)settings.ContentTopPt;

        DrawBand(canvas, page.Header, contentLeft);
        DrawBand(canvas, page.Footer, contentLeft);

        // O filete da nota de rodapé VAI para o papel, ao contrário do tracejado do \page: aquele
        // é marca de edição, este é convenção tipográfica — sem ele o leitor não sabe onde o texto
        // termina e a nota começa.
        if (page.FootnoteRulePt is { } rulePt)
        {
            using var rule = new SKPaint { Color = SKColors.Black, StrokeWidth = 0.6f, IsAntialias = true };

            canvas.DrawLine(
                contentLeft,
                contentTop + (float)rulePt,
                contentLeft + ((float)settings.ContentWidthPt / 3.0f),
                contentTop + (float)rulePt,
                rule);
        }

        foreach (var line in page.Lines)
        {
            if (line.Kind == LineKind.PageBreak)
            {
                continue;
            }

            var baseline = contentTop + (float)(line.YPt + line.BaselinePt);

            foreach (var run in line.Runs)
            {
                if (run.Text.Length > 0)
                {
                    // A subida do sobrescrito sai do estilo, e não de uma constante daqui: é o que
                    // garante que a chamada de nota caia na mesma altura no papel e na tela.
                    canvas.DrawText(
                        run.Text,
                        contentLeft + (float)run.XPt,
                        baseline - (float)run.Style.BaselineRisePt,
                        FontFor(run.Style),
                        Ink);
                }
            }
        }
    }

    // As duas coordenadas do BandRun têm origens diferentes de propósito, e a conta é a mesma que
    // o PageRenderer faz: XPt parte da área de conteúdo, BaselinePt parte do topo do papel.
    private static void DrawBand(SKCanvas canvas, IReadOnlyList<BandRun> runs, float contentLeft)
    {
        foreach (var run in runs)
        {
            canvas.DrawText(run.Text, contentLeft + (float)run.XPt, (float)run.BaselinePt, FontFor(run.Style), Ink);
        }
    }

    private static SKFont FontFor(TextStyle style) =>
        Fonts.GetOrAdd(style, static key => new SKFont(Resolve(key), (float)key.FontSizePt));

    /// <summary>Resolve a família do estilo contra as fontes do sistema.</summary>
    /// <remarks>
    /// A família pode trazer alternativas separadas por vírgula — <c>"Times New Roman, Liberation
    /// Serif, DejaVu Serif"</c> —, e <b>percorrer a lista é obrigatório</b>: passar a lista inteira
    /// como um nome não casaria com fonte nenhuma, o Skia cairia na padrão dele, e o PDF sairia numa
    /// fonte diferente da que está na tela. <see cref="SKFontManager.MatchFamily"/> devolve
    /// <c>null</c> quando não encontra, que é o que permite tentar a seguinte;
    /// <c>SKTypeface.FromFamilyName</c> não serviria, porque nunca falha.
    /// </remarks>
    private static SKTypeface Resolve(TextStyle style)
    {
        var wanted = new SKFontStyle(
            style.Weight == FontWeightKind.Bold ? SKFontStyleWeight.Bold : SKFontStyleWeight.Normal,
            SKFontStyleWidth.Normal,
            style.Italic ? SKFontStyleSlant.Italic : SKFontStyleSlant.Upright);

        foreach (var name in style.FontFamily.Split(
            ',',
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (SKFontManager.Default.MatchFamily(name, wanted) is { } match)
            {
                return match;
            }
        }

        // Família vazia, ou nenhuma das alternativas instalada. Nome nulo é como o Skia pede "a
        // padrão do sistema", e o estilo continua valendo — sem isso um título em negrito sairia
        // do PDF em peso normal.
        return SKTypeface.FromFamilyName(null, wanted) ?? SKTypeface.Default;
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (IOException)
        {
            // Falhar a limpeza não pode mascarar a exceção real, que é a que diz por que a
            // exportação não aconteceu.
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
