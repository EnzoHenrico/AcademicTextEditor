using System.Globalization;
using System.Text;

using AcademicEditor.Core.Layout.Model;
using AcademicEditor.Core.Parsing.Ast;

namespace AcademicEditor.Core.Layout;

/// <summary>
/// O sumário: uma entrada por título do documento, com condutor de pontos e número da folha.
/// </summary>
/// <remarks>
/// <para>
/// <b>Dois passes, e o segundo não muda geometria nenhuma.</b> Quantas entradas existem e quantas
/// linhas cada uma ocupa sai da AST, <i>antes</i> de qualquer linha ser assentada — é a mesma
/// descoberta que a nota de rodapé deixou registrada: com a altura conhecida com antecedência, o
/// page breaker decide de uma vez e não há laço de convergência. O que só se sabe depois de paginar
/// é o número da folha, e ele entra num segundo passe, como o <c>{pages}</c> do cabeçalho.
/// </para>
/// <para>
/// <b>É <see cref="Build"/> quem garante que os dois passes concordam</b>, porque os dois chamam
/// exatamente ele: a única diferença entre eles é a lista de números, e ela não participa da quebra
/// do título. A coluna do número tem <b>largura reservada fixa</b> justamente para isso — sem a
/// reserva, um título na fronteira da quebra poderia enrolar ao passar de 9 para 100, mudar a
/// contagem de linhas do sumário e mover as folhas que o próprio número descreve.
/// </para>
/// <para>
/// <b>As linhas de entrada não têm posição na fonte</b> (<c>SourceStart</c> = -1,
/// <see cref="LaidOutLine.IsGenerated"/>) e ficam fora do <see cref="PaginatedDocument.Index"/>. Ver
/// o comentário daquela propriedade: um offset falso numa linha gerada não quebra o desenho, quebra
/// o caret.
/// </para>
/// </remarks>
public static class TableOfContents
{
    /// <summary>Um título do documento, do ponto de vista do sumário.</summary>
    /// <param name="SourceStart">Onde o título começa no buffer — é por ele que se acha a folha.</param>
    public readonly record struct Heading(int Level, string Title, int SourceStart);

    /// <summary>
    /// A largura reservada para o número da folha: quatro dígitos.
    /// </summary>
    /// <remarks>
    /// Quatro, e não a largura do número real, porque é a reserva que desfaz o ponto fixo. Uma tese
    /// de mais de 9.999 folhas passaria disto — e aí o número invade a folga do condutor, que é
    /// degradação visível e não erro de paginação.
    /// </remarks>
    private const string NumberReserve = "0000";

    /// <summary>Os títulos do documento, na ordem em que foram escritos.</summary>
    public static List<Heading> Collect(DocumentNode document)
    {
        ArgumentNullException.ThrowIfNull(document);

        var headings = new List<Heading>();

        foreach (var block in document.Blocks)
        {
            if (block is not HeadingNode heading)
            {
                continue;
            }

            var title = TitleOf(heading);

            // Um "## " recém-aberto, ainda sem texto, não vira entrada: o sumário mostraria uma
            // linha só com pontinhos e um número, e ela sumiria de novo na tecla seguinte.
            if (title.Length == 0)
            {
                continue;
            }

            headings.Add(new Heading(heading.Level, title, heading.SourceStart));
        }

        return headings;
    }

    /// <summary>
    /// As linhas do sumário. Com <paramref name="pages"/> nulo sai sem condutor e sem número — é o
    /// primeiro passe, que só precisa saber quanto espaço reservar.
    /// </summary>
    public static List<LaidOutLine> Build(
        IReadOnlyList<Heading> headings,
        IReadOnlyList<int>? pages,
        double contentWidthPt,
        ITextMeasurer measurer,
        TypographyPreset preset)
    {
        ArgumentNullException.ThrowIfNull(headings);
        ArgumentNullException.ThrowIfNull(measurer);
        ArgumentNullException.ThrowIfNull(preset);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(contentWidthPt);

        var body = preset.Body;
        var reservePt = measurer.MeasureWidthPt(NumberReserve, body);
        var gapPt = measurer.MeasureWidthPt(" ", body);
        var dotPt = measurer.MeasureWidthPt(".", body);
        var lines = new List<LaidOutLine>(headings.Count);

        for (var index = 0; index < headings.Count; index++)
        {
            var heading = headings[index];

            // Um em por nível, que é o recuo com que o sumário mostra a hierarquia sem numeração.
            var indentPt = (heading.Level - 1) * body.FontSizePt;
            var titleWidthPt = Math.Max(
                contentWidthPt - indentPt - reservePt - (2.0 * gapPt),
                body.FontSizePt);

            var broken = LineBreaker.BreakIntoLines(
                [new InlineRun(heading.Title, 0, body)],
                titleWidthPt,
                measurer,
                includeMarkup: false,
                preset,
                TextAlignment.Left);

            for (var line = 0; line < broken.Count; line++)
            {
                var runs = Indent(broken[line].Runs, indentPt);

                // Condutor e número vão na ÚLTIMA linha do título, que é onde o olho os procura
                // num título que enrolou.
                if (line == broken.Count - 1 && pages is not null)
                {
                    Fill(runs, broken[line], pages[index], contentWidthPt, gapPt, dotPt, indentPt, body, measurer);
                }

                lines.Add(new LaidOutLine(
                    YPt: 0.0,
                    broken[line].HeightPt,
                    broken[line].BaselinePt,
                    runs,
                    SourceStart: -1,
                    SourceLength: 0,
                    LineKind.TocEntry));
            }
        }

        return lines;
    }

    /// <summary>
    /// O mesmo documento com os números das folhas escritos nas entradas do sumário.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Passe puro sobre o documento pronto, como o <see cref="PageBands"/>: quem o aplica é quem
    /// publica o layout, e é isso que faz o caminho incremental herdá-lo de graça.
    /// </para>
    /// <para>
    /// <b>Recusa em vez de arriscar.</b> Se a contagem de linhas geradas não bater com a que
    /// <see cref="Build"/> produz agora, o documento volta como está: uma folha com o sumário meio
    /// escrito é melhor que uma com a geometria remendada por baixo.
    /// </para>
    /// </remarks>
    public static PaginatedDocument Apply(
        PaginatedDocument document,
        DocumentNode ast,
        ITextMeasurer measurer)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(ast);
        ArgumentNullException.ThrowIfNull(measurer);

        var generated = Count(document);

        if (generated == 0)
        {
            return document;
        }

        var headings = Collect(ast);
        var pages = new int[headings.Count];

        for (var index = 0; index < headings.Count; index++)
        {
            var line = document.FindLineIndex(headings[index].SourceStart);

            pages[index] = line < 0 ? 1 : document.Index[line].PageIndex + 1;
        }

        var filled = Build(
            headings,
            pages,
            document.Settings.ContentWidthPt,
            measurer,
            document.Typography);

        if (filled.Count != generated)
        {
            return document;
        }

        var replaced = new PageLayout[document.Pages.Count];
        var cursor = 0;

        for (var index = 0; index < document.Pages.Count; index++)
        {
            var page = document.Pages[index];
            var lines = page.Lines;
            LaidOutLine[]? updated = null;

            for (var line = 0; line < lines.Count; line++)
            {
                if (!lines[line].IsGenerated)
                {
                    continue;
                }

                updated ??= [.. lines];

                // A caixa é a que o page breaker assentou; o que muda é o conteúdo dela. Recalcular
                // a altura aqui seria mudar a geometria num passe que promete não mudá-la.
                updated[line] = filled[cursor] with
                {
                    YPt = lines[line].YPt,
                    HeightPt = lines[line].HeightPt,
                    BaselinePt = lines[line].BaselinePt,
                };

                cursor++;
            }

            replaced[index] = updated is null ? page : page with { Lines = updated };
        }

        return new PaginatedDocument(replaced, document.Settings, document.RevealedBlock)
        {
            Typography = document.Typography,
        };
    }

    /// <summary>Quantas linhas geradas o documento tem.</summary>
    private static int Count(PaginatedDocument document)
    {
        var total = 0;

        foreach (var page in document.Pages)
        {
            foreach (var line in page.Lines)
            {
                if (line.IsGenerated)
                {
                    total++;
                }
            }
        }

        return total;
    }

    private static string TitleOf(HeadingNode heading)
    {
        var builder = new StringBuilder();

        foreach (var run in heading.Runs)
        {
            // A marcação fica de fora: o sumário mostra o título, não o "## " nem os asteriscos do
            // negrito que o autor pôs dentro dele.
            if (!run.IsMarkup)
            {
                builder.Append(run.Text);
            }
        }

        return builder.ToString().Trim();
    }

    private static List<LaidOutRun> Indent(IReadOnlyList<LaidOutRun> runs, double indentPt)
    {
        var indented = new List<LaidOutRun>(runs.Count + 2);

        foreach (var run in runs)
        {
            indented.Add(indentPt == 0.0 ? run : run with { XPt = run.XPt + indentPt });
        }

        return indented;
    }

    /// <summary>Acrescenta o condutor de pontos e o número da folha à linha.</summary>
    private static void Fill(
        List<LaidOutRun> runs,
        LaidOutLine title,
        int page,
        double contentWidthPt,
        double gapPt,
        double dotPt,
        double indentPt,
        TextStyle body,
        ITextMeasurer measurer)
    {
        var number = page.ToString(CultureInfo.InvariantCulture);
        var numberPt = measurer.MeasureWidthPt(number, body);

        // Encostado na margem direita: é a única coordenada do sumário que não se negocia, e é ela
        // que faz a coluna de números ficar reta folha abaixo.
        var numberXPt = contentWidthPt - numberPt;

        // Da tinta do título, não da extensão crua: um branco no fim do título empurraria o
        // condutor sem desenhar nada. Mesma distinção que o alinhamento à direita já faz.
        var titleEndPt = indentPt + LineExtents.InkExtentPt(title, measurer);
        var fromPt = titleEndPt + gapPt;
        var toPt = numberXPt - gapPt;
        var dots = dotPt > 0.0 ? (int)((toPt - fromPt) / dotPt) : 0;

        if (dots > 0)
        {
            // Encostados no número, e não no título: o olho corre da esquerda para a direita e
            // precisa chegar ao número, não sair do título.
            var widthPt = dots * dotPt;

            runs.Add(new LaidOutRun(new string('.', dots), body, toPt - widthPt, widthPt, LineOffset: 0));
        }

        runs.Add(new LaidOutRun(number, body, numberXPt, numberPt, LineOffset: 0));
    }
}
