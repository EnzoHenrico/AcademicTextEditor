using System.Diagnostics;
using System.Text;

using AcademicEditor.App.Rendering;

using Avalonia;
using Avalonia.Media.Imaging;

using AcademicEditor.Core.Layout;
using AcademicEditor.Core.Layout.Model;
using AcademicEditor.Core.Parsing;
using AcademicEditor.Core.Parsing.Ast;

namespace AcademicEditor.App.Diagnostics;

/// <summary>
/// Quanto custa paginar um documento do tamanho de uma tese com o medidor <b>real</b>.
/// </summary>
/// <remarks>
/// <para>
/// Existe no App, e não nos testes, porque o número que decide a Fase 4 é o do
/// <see cref="AvaloniaTextMeasurer"/> — e o projeto de testes não referencia Avalonia de
/// propósito. Lá, <c>LayoutPerformanceTests</c> mede a mesma coisa com o medidor determinístico:
/// a diferença entre os dois é o custo de medir texto de verdade.
/// </para>
/// <para>
/// Roda por <c>--measure-layout</c> e não abre janela. Precisa da plataforma inicializada mesmo
/// assim: o <c>TextLayout</c> depende do gerenciador de fontes.
/// </para>
/// </remarks>
public static class LayoutBenchmark
{
    // Dimensionado para ~300 páginas de A4 com o medidor real, que cabe bem mais caractere por
    // linha que o determinístico dos testes — daí o corpus ser maior que o de lá.
    private const int Paragraphs = 1210;
    private const int WordsPerParagraph = 100;

    // Vocabulário sintético com cauda longa, e é o detalhe que decide se esta medição vale alguma
    // coisa. Uma tese de 300 páginas tem ~200 mil palavras e ~12 mil formas distintas, com as mais
    // frequentes respondendo pela maior parte do texto (Zipf). Um corpus de dezoito palavras —
    // como este arquivo tinha — daria a um cache de medição quase 100% de acerto e mediria a si
    // mesmo, não o ganho real.
    private const int VocabularySize = 12_000;

    // Semente fixa: duas execuções têm de medir o mesmo documento, senão os números não comparam.
    private const int Seed = 20260908;

    // Um título a cada oito parágrafos. Não é enfeite: exercita o caminho do heading — estilo
    // maior, altura de linha diferente, marcação revelada na linha do caret — que responde por
    // parte da paginação de um documento acadêmico de verdade.
    private const int ParagraphsPerHeading = 8;

    // Palavras montadas por sílabas, e não por letras sorteadas. O documento vira um arquivo que
    // dá para abrir e ler como texto; letra aleatória parece arquivo corrompido. As vogais com
    // acento entram porque o TextLayout faz shaping, e texto só-ASCII não exercita o mesmo caminho
    // que "paginação".
    private static readonly string[] Consonants =
        ["b", "c", "d", "f", "g", "l", "m", "n", "p", "qu", "r", "s", "t", "v", "z", "ch", "lh", "nh"];

    private static readonly string[] Vowels =
        ["a", "e", "i", "o", "u", "á", "é", "í", "ó", "ú", "ã", "õ", "ai", "ei", "ou", "ão"];

    public static void Run(TextWriter output)
    {
        ArgumentNullException.ThrowIfNull(output);

        var source = BuildDocument(Paragraphs, WordsPerParagraph);

        // Uma passada fora do relógio: paga JIT, o carregamento da fonte e o aquecimento do heap.
        _ = Paginate(source, new AvaloniaTextMeasurer());

        // As três situações que decidem alguma coisa. O cache frio é o custo de abrir um arquivo;
        // o quente é o custo de uma tecla, que é o que o autor sente enquanto escreve.
        var plain = Time(source, new AvaloniaTextMeasurer());

        var cache = new CachingTextMeasurer(new AvaloniaTextMeasurer());
        var cold = Time(source, cache);
        var warm = Time(source, cache);

        // Passada instrumentada, sem cache: um relógio por chamada custa alguns nanossegundos e
        // infla o total, então o total honesto é o de cima. Esta responde outra pergunta — quanto
        // do trabalho está dentro do medidor, e quanto dele é repetição.
        var counting = new CountingMeasurer(new AvaloniaTextMeasurer());
        var paginated = Paginate(source, counting);

        var pages = paginated.Pages.Count;
        var lines = paginated.Pages.Sum(page => page.Lines.Count);

        output.WriteLine($"caracteres          : {source.Length:N0}");
        output.WriteLine($"páginas             : {pages:N0}");
        output.WriteLine($"linhas              : {lines:N0}");
        output.WriteLine(string.Empty);
        output.WriteLine($"sem cache           : {plain:N1} ms   ({plain * 1000.0 / lines:N2} µs/linha)");
        output.WriteLine($"cache frio (abrir)  : {cold:N1} ms   ({plain / cold:N1}x)");
        output.WriteLine($"cache quente (tecla): {warm:N1} ms   ({plain / warm:N1}x)");
        output.WriteLine(string.Empty);
        // Decomposição do caminho quente: com o cache cheio, onde estão os milissegundos de uma
        // tecla. É o que aponta para qual nível do reflow incremental paga primeiro.
        var ast = MarkupParser.Parse(source);
        var parse = TimeOnly(() => MarkupParser.Parse(source));
        var layout = TimeOnly(() => LayoutEngine.Layout(ast, PageSettings.A4, cache));

        output.WriteLine($"  ↳ parser          : {parse:N1} ms");
        output.WriteLine($"  ↳ layout          : {layout:N1} ms");
        output.WriteLine(string.Empty);
        output.WriteLine($"tecla, reflow total : {parse + layout:N1} ms");
        output.WriteLine($"tecla, incremental  : {Keystroke(source, cache):N1} ms");
        output.WriteLine(string.Empty);
        output.WriteLine($"medições largura    : {counting.WidthCalls:N0}");
        output.WriteLine($"  distintas         : {counting.DistinctWidths:N0}");
        output.WriteLine($"  repetidas         : {1.0 - ((double)counting.DistinctWidths / counting.WidthCalls):P1} (teto do cache)");
        output.WriteLine($"medições métricas   : {counting.MetricsCalls:N0}");
        output.WriteLine($"tempo no medidor    : {counting.ElapsedMilliseconds:N1} ms (instrumentado, sem cache)");
    }

    /// <summary>
    /// Quanto custa <b>um quadro</b> — desenhar, não paginar.
    /// </summary>
    /// <remarks>
    /// A outra metade da conta, e a que o autor sente parado: <c>InvalidateVisual</c> vem do timer
    /// de piscar do caret a cada 530ms, então o custo de um quadro é pago duas vezes por segundo
    /// mesmo sem ninguém tocar no teclado. Desenha num <c>RenderTargetBitmap</c> do tamanho de uma
    /// janela, com o <c>DrawingContext</c> de verdade — não um simulacro.
    /// </remarks>
    public static void RunRender(TextWriter output)
    {
        ArgumentNullException.ThrowIfNull(output);

        const int WidthDip = 900;
        const int HeightDip = 700;
        const int Frames = 10;

        var paginated = Paginate(
            BuildDocument(Paragraphs, WordsPerParagraph),
            new CachingTextMeasurer(new AvaloniaTextMeasurer()));

        using var target = new RenderTargetBitmap(new PixelSize(WidthDip, HeightDip), new Vector(96.0, 96.0));

        // Meio do documento, não o começo: é onde a aritmética dos índices tem de estar certa, e
        // onde o "antes" e o "depois" desenham a mesma coisa na tela por caminhos diferentes.
        var pageStepDip = (paginated.Settings.HeightPt * PageRenderer.PtToDip) + PageRenderer.PageGapDip;
        var middle = new Rect(0.0, pageStepDip * (paginated.Pages.Count / 2), WidthDip, HeightDip);

        DrawFrame(target, paginated, WidthDip, viewport: null);

        var whole = TimeFrames(target, paginated, WidthDip, viewport: null, Frames);
        var visible = TimeFrames(target, paginated, WidthDip, middle, Frames);

        output.WriteLine($"páginas             : {paginated.Pages.Count:N0}");
        output.WriteLine($"linhas              : {paginated.Pages.Sum(page => page.Lines.Count):N0}");
        output.WriteLine(string.Empty);
        output.WriteLine($"pilha inteira       : {whole:N1} ms/quadro");
        output.WriteLine($"só o visível        : {visible:N2} ms/quadro   ({whole / visible:N0}x)");
        output.WriteLine(string.Empty);
        output.WriteLine($"parado, a 530ms     : {whole * 2.0:N0} ms/s antes, {visible * 2.0:N1} ms/s depois");
    }

    private static double TimeFrames(
        RenderTargetBitmap target,
        PaginatedDocument paginated,
        double widthDip,
        Rect? viewport,
        int frames)
    {
        var stopwatch = Stopwatch.StartNew();

        for (var frame = 0; frame < frames; frame++)
        {
            DrawFrame(target, paginated, widthDip, viewport);
        }

        stopwatch.Stop();

        return stopwatch.Elapsed.TotalMilliseconds / frames;
    }

    private static void DrawFrame(
        RenderTargetBitmap target,
        PaginatedDocument paginated,
        double widthDip,
        Rect? viewport)
    {
        using var context = target.CreateDrawingContext();

        PageRenderer.Render(context, paginated, widthDip, caret: null, viewport);
    }

    /// <summary>
    /// Grava o documento do benchmark num arquivo, para abri-lo no editor de verdade e sentir a
    /// latência em vez de só lê-la num número.
    /// </summary>
    public static void WriteCorpus(string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);

        var directory = Path.GetDirectoryName(Path.GetFullPath(path));

        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        File.WriteAllText(path, BuildDocument(Paragraphs, WordsPerParagraph));
    }

    /// <summary>
    /// O custo de uma tecla com o reflow incremental: insere um caractere no meio do documento e
    /// repagina reaproveitando o layout anterior. Inclui o parser, que continua completo.
    /// </summary>
    private static double Keystroke(string source, ITextMeasurer measurer)
    {
        var caret = source.Length / 2;
        var previous = LayoutEngine.Layout(MarkupParser.Parse(source), PageSettings.A4, measurer, caret);
        var edited = source.Insert(caret, "X");

        return TimeOnly(() => LayoutEngine.Layout(
            MarkupParser.Parse(edited),
            PageSettings.A4,
            measurer,
            caret + 1,
            LayoutReuse.Between(source, edited, previous)));
    }

    private static double TimeOnly(Action work)
    {
        work();

        var stopwatch = Stopwatch.StartNew();
        work();
        stopwatch.Stop();

        return stopwatch.Elapsed.TotalMilliseconds;
    }

    /// <summary>Cronometra uma repaginação completa e devolve os milissegundos.</summary>
    private static double Time(string source, ITextMeasurer measurer)
    {
        var stopwatch = Stopwatch.StartNew();
        _ = Paginate(source, measurer);
        stopwatch.Stop();

        return stopwatch.Elapsed.TotalMilliseconds;
    }

    private static PaginatedDocument Paginate(string source, ITextMeasurer measurer) =>
        LayoutEngine.Layout(MarkupParser.Parse(source), PageSettings.A4, measurer);

    /// <summary>
    /// Parágrafos separados por linha em branco — a forma que o autor escreve e a que o parser vê:
    /// uma linha de fonte por parágrafo, quebrada pela largura da página.
    /// </summary>
    private static string BuildDocument(int paragraphs, int wordsPerParagraph)
    {
        var random = new Random(Seed);
        var vocabulary = BuildVocabulary(random);
        var zipf = BuildZipfTable(vocabulary.Length);
        var builder = new StringBuilder();

        for (var paragraph = 0; paragraph < paragraphs; paragraph++)
        {
            if (paragraph % ParagraphsPerHeading == 0)
            {
                builder.Append(paragraph == 0 ? "# " : "## ");

                for (var index = 0; index < 4; index++)
                {
                    builder.Append(vocabulary[SampleRank(zipf, random.NextDouble())]);
                    builder.Append(index == 3 ? '\n' : ' ');
                }

                builder.Append('\n');
            }

            for (var index = 0; index < wordsPerParagraph; index++)
            {
                builder.Append(vocabulary[SampleRank(zipf, random.NextDouble())]);
                builder.Append(index == wordsPerParagraph - 1 ? '\n' : ' ');
            }

            builder.Append('\n');
        }

        return builder.ToString();
    }

    // Comprimentos de 2 a 13 caracteres. Palavra de tamanho fixo faria a quebra cair sempre no
    // mesmo lugar, e o line breaker nunca exercitaria o caminho da palavra que não coube.
    private static string[] BuildVocabulary(Random random)
    {
        var words = new string[VocabularySize];
        var builder = new StringBuilder();

        for (var index = 0; index < words.Length; index++)
        {
            // De uma a cinco sílabas: a distribuição de comprimento da prosa. Palavra de tamanho
            // fixo faria a quebra cair sempre no mesmo lugar, e o line breaker nunca exercitaria o
            // caminho da palavra que não coube.
            var syllables = 1 + random.Next(5);

            builder.Clear();

            for (var syllable = 0; syllable < syllables; syllable++)
            {
                builder.Append(Consonants[random.Next(Consonants.Length)]);
                builder.Append(Vowels[random.Next(Vowels.Length)]);
            }

            words[index] = builder.ToString();
        }

        return words;
    }

    // Distribuição acumulada de Zipf: a palavra de posto r aparece com peso 1/r. É o que dá ao
    // corpus a cauda longa da língua — sem ela, sortear uniformemente daria a cada palavra a mesma
    // frequência e o cache teria taxa de acerto irrealisticamente baixa.
    private static double[] BuildZipfTable(int size)
    {
        var cumulative = new double[size];
        var total = 0.0;

        for (var index = 0; index < size; index++)
        {
            total += 1.0 / (index + 1);
            cumulative[index] = total;
        }

        for (var index = 0; index < size; index++)
        {
            cumulative[index] /= total;
        }

        return cumulative;
    }

    private static int SampleRank(double[] cumulative, double uniform)
    {
        var index = Array.BinarySearch(cumulative, uniform);

        if (index < 0)
        {
            index = ~index;
        }

        return Math.Min(index, cumulative.Length - 1);
    }

    /// <summary>Conta as chamadas ao medidor e cronometra o tempo gasto dentro dele.</summary>
    private sealed class CountingMeasurer(ITextMeasurer inner) : ITextMeasurer
    {
        // Quantos trechos distintos foram medidos. A razão contra o total é o teto do que um cache
        // por (texto, estilo) pode economizar — antes de escrever o cache.
        private readonly HashSet<string> _distinct = new(StringComparer.Ordinal);

        private long _ticks;

        public int WidthCalls { get; private set; }

        public int MetricsCalls { get; private set; }

        public int DistinctWidths => _distinct.Count;

        public double ElapsedMilliseconds => _ticks * 1000.0 / Stopwatch.Frequency;

        public double MeasureWidthPt(ReadOnlySpan<char> text, TextStyle style)
        {
            WidthCalls++;
            _distinct.Add(new string(text));

            var start = Stopwatch.GetTimestamp();
            var width = inner.MeasureWidthPt(text, style);
            _ticks += Stopwatch.GetTimestamp() - start;

            return width;
        }

        public LineMetrics GetLineMetrics(TextStyle style)
        {
            MetricsCalls++;
            var start = Stopwatch.GetTimestamp();
            var metrics = inner.GetLineMetrics(style);
            _ticks += Stopwatch.GetTimestamp() - start;

            return metrics;
        }
    }
}
