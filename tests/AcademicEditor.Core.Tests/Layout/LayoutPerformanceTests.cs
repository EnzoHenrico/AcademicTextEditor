using System.Diagnostics;
using System.Text;

using AcademicEditor.Core.Layout;
using AcademicEditor.Core.Parsing;

using Xunit.Abstractions;

namespace AcademicEditor.Core.Tests.Layout;

/// <summary>
/// Quanto custa paginar um documento do tamanho de uma tese, com o medidor determinístico.
/// </summary>
/// <remarks>
/// <para>
/// Mede o <b>algoritmo</b>: parser, line breaker e page breaker. O custo de medir texto de verdade
/// fica de fora de propósito — o <see cref="FakeTextMeasurer"/> responde em aritmética, e é a
/// subtração entre este número e o do medidor real que diz onde está o gargalo.
/// </para>
/// <para>
/// O teto é generoso porque isto roda em máquina compartilhada: ele não existe para cravar o
/// número, existe para pegar uma regressão de ordem de grandeza — o dia em que alguém puser uma
/// busca linear dentro do laço de linhas.
/// </para>
/// </remarks>
public sealed class LayoutPerformanceTests(ITestOutputHelper output)
{
    // Palavras de comprimentos variados: com palavra de tamanho fixo a quebra cai sempre no mesmo
    // lugar e o line breaker nunca exercita o caminho da palavra que não coube.
    private static readonly string[] Words =
    [
        "paginação", "documento", "acadêmico", "layout", "de", "texto", "em", "tempo", "real",
        "com", "quebra", "por", "largura", "da", "página", "e", "medição", "tipográfica",
    ];

    private const int Paragraphs = 640;
    private const int WordsPerParagraph = 100;
    private const int CeilingMilliseconds = 3000;

    [Fact]
    public void Documento_de_tese_pagina_dentro_do_teto()
    {
        var source = BuildDocument(Paragraphs, WordsPerParagraph);
        var measurer = new FakeTextMeasurer();

        // Uma passada fora do relógio: a primeira paga JIT e o aquecimento do heap, e é ruído
        // sobre o que se quer medir, que é a repaginação em regime.
        _ = LayoutEngine.Layout(MarkupParser.Parse(source), PageSettings.A4, measurer);

        var stopwatch = Stopwatch.StartNew();
        var paginated = LayoutEngine.Layout(MarkupParser.Parse(source), PageSettings.A4, measurer);
        stopwatch.Stop();

        var pages = paginated.Pages.Count;
        var lines = paginated.Pages.Sum(page => page.Lines.Count);
        var elapsed = stopwatch.Elapsed.TotalMilliseconds;

        output.WriteLine($"caracteres : {source.Length:N0}");
        output.WriteLine($"páginas    : {pages:N0}");
        output.WriteLine($"linhas     : {lines:N0}");
        output.WriteLine($"tempo      : {elapsed:N1} ms");
        output.WriteLine($"por linha  : {elapsed * 1000.0 / lines:N2} µs");
        output.WriteLine($"por página : {elapsed / pages:N2} ms");

        Assert.True(pages >= 250, $"esperava um documento de ~300 páginas, vieram {pages}");
        Assert.True(
            elapsed < CeilingMilliseconds,
            $"paginar {pages} páginas levou {elapsed:N0}ms, além do teto de {CeilingMilliseconds}ms");
    }

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
}
