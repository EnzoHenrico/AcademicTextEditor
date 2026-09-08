using System.Diagnostics;
using System.Text;

using AcademicEditor.App.Rendering;

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
    private const int Paragraphs = 1900;
    private const int WordsPerParagraph = 100;

    // Palavras de comprimentos variados: com palavra de tamanho fixo a quebra cai sempre no mesmo
    // lugar e o line breaker nunca exercita o caminho da palavra que não coube.
    private static readonly string[] Words =
    [
        "paginação", "documento", "acadêmico", "layout", "de", "texto", "em", "tempo", "real",
        "com", "quebra", "por", "largura", "da", "página", "e", "medição", "tipográfica",
    ];

    public static void Run(TextWriter output)
    {
        ArgumentNullException.ThrowIfNull(output);

        var source = BuildDocument(Paragraphs, WordsPerParagraph);
        var measurer = new AvaloniaTextMeasurer();

        // Uma passada fora do relógio: paga JIT, o carregamento da fonte e o aquecimento do heap.
        _ = Paginate(source, measurer);

        var stopwatch = Stopwatch.StartNew();
        var paginated = Paginate(source, measurer);
        stopwatch.Stop();

        var pages = paginated.Pages.Count;
        var lines = paginated.Pages.Sum(page => page.Lines.Count);
        var elapsed = stopwatch.Elapsed.TotalMilliseconds;

        // Segunda passada, instrumentada: um relógio por chamada custa alguns nanossegundos e
        // infla o total, então o total honesto é o de cima. Esta responde outra pergunta — quanto
        // do trabalho está dentro do medidor.
        var counting = new CountingMeasurer(new AvaloniaTextMeasurer());
        _ = Paginate(source, counting);

        output.WriteLine($"caracteres        : {source.Length:N0}");
        output.WriteLine($"páginas           : {pages:N0}");
        output.WriteLine($"linhas            : {lines:N0}");
        output.WriteLine($"tempo             : {elapsed:N1} ms");
        output.WriteLine($"por linha         : {elapsed * 1000.0 / lines:N2} µs");
        output.WriteLine($"por página        : {elapsed / pages:N2} ms");
        output.WriteLine($"medições largura  : {counting.WidthCalls:N0}");
        output.WriteLine($"medições métricas : {counting.MetricsCalls:N0}");
        output.WriteLine($"tempo no medidor  : {counting.ElapsedMilliseconds:N1} ms (instrumentado)");
    }

    private static PaginatedDocument Paginate(string source, ITextMeasurer measurer) =>
        LayoutEngine.Layout(MarkupParser.Parse(source), PageSettings.A4, measurer);

    /// <summary>
    /// Parágrafos separados por linha em branco — a forma que o autor escreve e a que o parser vê:
    /// uma linha de fonte por parágrafo, quebrada pela largura da página.
    /// </summary>
    private static string BuildDocument(int paragraphs, int wordsPerParagraph)
    {
        var builder = new StringBuilder();
        var word = 0;

        for (var paragraph = 0; paragraph < paragraphs; paragraph++)
        {
            for (var index = 0; index < wordsPerParagraph; index++)
            {
                builder.Append(Words[word++ % Words.Length]);
                builder.Append(index == wordsPerParagraph - 1 ? '\n' : ' ');
            }

            builder.Append('\n');
        }

        return builder.ToString();
    }

    /// <summary>Conta as chamadas ao medidor e cronometra o tempo gasto dentro dele.</summary>
    private sealed class CountingMeasurer(ITextMeasurer inner) : ITextMeasurer
    {
        private long _ticks;

        public int WidthCalls { get; private set; }

        public int MetricsCalls { get; private set; }

        public double ElapsedMilliseconds => _ticks * 1000.0 / Stopwatch.Frequency;

        public double MeasureWidthPt(ReadOnlySpan<char> text, TextStyle style)
        {
            WidthCalls++;
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
