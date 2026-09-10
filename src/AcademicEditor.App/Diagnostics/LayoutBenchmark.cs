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
    /// <summary>A norma tipográfica que este banco de provas mede.</summary>
    /// <remarks>
    /// <b>Deliberadamente o preset do MVP, e não o que o aplicativo usa.</b> Todos os números
    /// registrados no roadmap — 910ms sem cache, 309ms a frio, 68ms a quente, 11,2ms com reflow —
    /// saíram deste preset, e trocá-lo aqui os tornaria incomparáveis sem que ninguém percebesse.
    /// Medir o preset da ABNT é um número novo, não uma correção deste: corpo 12 em vez de 11 dá
    /// ~9% mais linhas, e entrelinhamento 1,5 dá ~50% mais folhas para o mesmo texto.
    /// </remarks>
    private static readonly TypographyPreset Preset = TypographyPreset.Default;

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
        var ast = MarkupParser.Parse(source, Preset);
        var parse = TimeOnly(() => MarkupParser.Parse(source, Preset));
        var layout = TimeOnly(() => LayoutEngine.Layout(ast, PageSettings.A4, cache, preset: Preset));

        output.WriteLine($"  ↳ parser          : {parse:N1} ms");
        output.WriteLine($"  ↳ layout          : {layout:N1} ms");
        output.WriteLine(string.Empty);
        output.WriteLine($"tecla, reflow total : {parse + layout:N1} ms");
        output.WriteLine($"tecla, incremental  : {Keystroke(source, cache):N1} ms");
        output.WriteLine(string.Empty);
        WriteKeystrokeTable(output);

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

        // Com marcação: é o documento que o aplicativo tem de aguentar, e é nele que a conferência
        // à mão vale alguma coisa. Um corpus de prosa lisa não exercita nada do que a Fase 6 pôs
        // no motor.
        File.WriteAllText(path, BuildDocument(Paragraphs, WordsPerParagraph, markup: true));
    }

    /// <summary>
    /// O custo de uma tecla nas quatro configurações que existem, e nos três lugares onde o autor
    /// digita.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>É a medição que faltava, e a ausência dela custou uma entrega.</b> O número publicado no
    /// roadmap saía do preset do MVP — alinhado à esquerda — sobre um corpus de prosa lisa, medindo
    /// <i>uma</i> tecla no meio de um parágrafo. O aplicativo roda o preset da ABNT, justificado,
    /// sobre texto com marcação, e quem escreve digita no <b>fim</b> do parágrafo o tempo todo.
    /// Três diferenças, e cada uma esconde um caminho diferente do reflow incremental.
    /// </para>
    /// <para>
    /// <b>Rajada encadeada, e não uma tecla isolada:</b> o layout de cada tecla alimenta a
    /// seguinte, que é o que acontece de verdade. Uma medição isolada sempre parte de um estado
    /// recém-paginado, e por isso nunca revela um reuso que passa a recusar a partir da segunda
    /// tecla.
    /// </para>
    /// </remarks>
    private static void WriteKeystrokeTable(TextWriter output)
    {
        const int Keys = 10;

        (string Label, TypographyPreset Preset, bool Markup)[] configurations =
        [
            ("Default, liso    ", TypographyPreset.Default, false),
            ("Default, marcado ", TypographyPreset.Default, true),
            ("ABNT, liso       ", TypographyPreset.Abnt, false),
            ("ABNT, marcado    ", TypographyPreset.Abnt, true),
        ];

        output.WriteLine($"ms por tecla numa rajada de {Keys} (melhor de 3)");
        output.WriteLine("                      começo      meio   fim de parágrafo      seta");

        foreach (var (label, preset, markup) in configurations)
        {
            var source = BuildDocument(Paragraphs, WordsPerParagraph, markup);

            var start = Burst(source, preset, InsideLine(source, 0.05), Keys);
            var middle = Burst(source, preset, InsideLine(source, 0.50), Keys);
            var lineEnd = Burst(source, preset, EndOfLine(source, 0.50), Keys);
            var arrow = Traversal(source, preset, InsideLine(source, 0.50), Keys);

            output.WriteLine($"{label} : {start,8:N1}  {middle,8:N1}  {lineEnd,11:N1}  {arrow,8:N1}");
        }

        output.WriteLine(string.Empty);
    }

    /// <summary>Milissegundos por tecla, encadeando o layout de uma no reuso da seguinte.</summary>
    private static double Burst(string source, TypographyPreset preset, int caretAt, int keys)
    {
        // O cache é compartilhado entre as tentativas de propósito: quente é o estado em que o
        // aplicativo está enquanto se escreve, e é esse o custo que interessa.
        var measurer = new CachingTextMeasurer(new AvaloniaTextMeasurer());
        var best = double.MaxValue;

        for (var attempt = 0; attempt < 4; attempt++)
        {
            var text = source;
            var caret = caretAt;

            var document = LayoutEngine.Layout(
                MarkupParser.Parse(text, preset), PageSettings.A4, measurer, caret, preset: preset);

            var stopwatch = Stopwatch.StartNew();

            for (var key = 0; key < keys; key++)
            {
                var edited = text.Insert(caret, "x");
                var reuse = LayoutReuse.Between(text, edited, document);

                // O caret pós-edição, que é a convenção do EditorViewModel: ele move o caret ao
                // inserir e só depois pede a repaginação.
                caret++;

                document = LayoutEngine.Layout(
                    MarkupParser.Parse(edited, preset), PageSettings.A4, measurer, caret, reuse, preset);

                text = edited;
            }

            stopwatch.Stop();

            // A primeira passada paga JIT e o aquecimento do cache de medição.
            if (attempt > 0)
            {
                best = Math.Min(best, stopwatch.Elapsed.TotalMilliseconds / keys);
            }
        }

        return best;
    }

    /// <summary>
    /// O custo de uma seta que atravessa blocos: o texto não muda, só a marcação revelada.
    /// </summary>
    /// <remarks>
    /// Uma coluna própria porque é um caminho próprio do reflow, e porque num editor em que uma
    /// linha da fonte é uma linha na página, <b>cada ↑/↓ atravessa um bloco</b>.
    /// </remarks>
    private static double Traversal(string source, TypographyPreset preset, int caretAt, int steps)
    {
        var measurer = new CachingTextMeasurer(new AvaloniaTextMeasurer());
        var best = double.MaxValue;

        for (var attempt = 0; attempt < 4; attempt++)
        {
            var caret = caretAt;

            var document = LayoutEngine.Layout(
                MarkupParser.Parse(source, preset), PageSettings.A4, measurer, caret, preset: preset);

            var stopwatch = Stopwatch.StartNew();

            for (var step = 0; step < steps; step++)
            {
                var newline = source.IndexOf('\n', caret);
                caret = newline < 0 || newline + 1 >= source.Length ? caretAt : newline + 1;

                document = LayoutEngine.Layout(
                    MarkupParser.Parse(source, preset),
                    PageSettings.A4,
                    measurer,
                    caret,
                    LayoutReuse.Between(source, source, document),
                    preset);
            }

            stopwatch.Stop();

            if (attempt > 0)
            {
                best = Math.Min(best, stopwatch.Elapsed.TotalMilliseconds / steps);
            }
        }

        return best;
    }

    /// <summary>Um offset dentro de uma linha de texto, nunca em cima de um <c>\n</c>.</summary>
    /// <remarks>
    /// Inserir sobre a quebra é outro caso: o reflow incremental o recusa de propósito, porque um
    /// <c>\n</c> criado ou apagado muda quantos blocos existem.
    /// </remarks>
    private static int InsideLine(string source, double fraction)
    {
        var at = Math.Clamp((int)(source.Length * fraction), 1, source.Length - 1);

        while (at < source.Length - 1 && (source[at] == '\n' || source[at - 1] == '\n'))
        {
            at++;
        }

        return at;
    }

    /// <summary>O fim da linha em que aquele offset cai — onde a guarda do caret decide.</summary>
    private static int EndOfLine(string source, double fraction)
    {
        var newline = source.IndexOf('\n', InsideLine(source, fraction));

        return newline < 0 ? source.Length : newline;
    }

    /// <summary>
    /// O custo de uma tecla com o reflow incremental: insere um caractere no meio do documento e
    /// repagina reaproveitando o layout anterior. Inclui o parser, que continua completo.
    /// </summary>
    private static double Keystroke(string source, ITextMeasurer measurer)
    {
        var caret = source.Length / 2;
        var previous = LayoutEngine.Layout(
            MarkupParser.Parse(source, Preset), PageSettings.A4, measurer, caret, preset: Preset);

        var edited = source.Insert(caret, "X");

        return TimeOnly(() => LayoutEngine.Layout(
            MarkupParser.Parse(edited, Preset),
            PageSettings.A4,
            measurer,
            caret + 1,
            LayoutReuse.Between(source, edited, previous),
            Preset));
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
        LayoutEngine.Layout(MarkupParser.Parse(source, Preset), PageSettings.A4, measurer, preset: Preset);

    /// <summary>
    /// Parágrafos separados por linha em branco — a forma que o autor escreve e a que o parser vê:
    /// uma linha de fonte por parágrafo, quebrada pela largura da página.
    /// </summary>
    /// <param name="markup">
    /// Salpica o documento com a marcação que o aplicativo suporta — <c>**negrito**</c>,
    /// <c>*itálico*</c>, <c>$fórmula$</c>, <c>[^nota]</c> e blocos <c>:-:</c>.
    /// </param>
    /// <remarks>
    /// <b>A marcação é opcional para que os números antigos continuem comparáveis</b>, e existe
    /// porque sem ela este corpus mede um documento que ninguém escreve. Os sorteios extras ficam
    /// todos dentro do <c>if</c>: com <paramref name="markup"/> falso a sequência do
    /// <see cref="Random"/> é caractere por caractere a mesma de antes, e o corpus histórico não
    /// muda.
    /// </remarks>
    private static string BuildDocument(int paragraphs, int wordsPerParagraph, bool markup = false)
    {
        var random = new Random(Seed);
        var vocabulary = BuildVocabulary(random);
        var zipf = BuildZipfTable(vocabulary.Length);
        var builder = new StringBuilder();
        var note = 0;

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

            // Um bloco alinhado a cada vinte: é o "RESUMO" centralizado e a epígrafe à direita que
            // uma tese tem, e é o caminho em que a linha é reposicionada depois de quebrada.
            if (markup && paragraph % 20 == 19)
            {
                builder.Append(paragraph % 40 == 19 ? ":-: " : "-: ");
            }

            for (var index = 0; index < wordsPerParagraph; index++)
            {
                var word = vocabulary[SampleRank(zipf, random.NextDouble())];

                if (markup)
                {
                    // ~2% das palavras enfatizadas. Mais do que isso não é prosa acadêmica, é
                    // demonstração de marcação — e o custo por linha é o que se quer medir.
                    var draw = random.NextDouble();

                    word = draw switch
                    {
                        < 0.012 => $"**{word}**",
                        < 0.020 => $"*{word}*",
                        _ => word,
                    };
                }

                builder.Append(word);
                builder.Append(index == wordsPerParagraph - 1 ? '\n' : ' ');
            }

            if (markup)
            {
                // Fórmula e chamada de nota entram no fim do parágrafo, que é onde o autor as põe.
                if (paragraph % 10 == 3)
                {
                    builder.Insert(builder.Length - 1, " $E = mc^2$");
                }

                if (paragraph % 6 == 1)
                {
                    builder.Insert(builder.Length - 1, $"[^{++note}]");
                }
            }

            builder.Append('\n');
        }

        // As definições no fim do arquivo, como numa tese de verdade. É o que faz a medição passar
        // pelo caminho em que a ordem de desenho deixa de ser a de fonte.
        for (var index = 1; markup && index <= note; index++)
        {
            builder.Append($"\\note {index} ");

            for (var word = 0; word < 12; word++)
            {
                builder.Append(vocabulary[SampleRank(zipf, random.NextDouble())]);
                builder.Append(word == 11 ? "\n\n" : " ");
            }
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
