using System.Diagnostics;
using AcademicEditor.Core.Layout;
using AcademicEditor.Core.Layout.Model;
using AcademicEditor.Core.Parsing;
using AcademicEditor.Core.Parsing.Ast;

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
    private const int CeilingMilliseconds = 3000;

    [Fact]
    public void Documento_de_tese_pagina_dentro_do_teto()
    {
        var source = LongDocument.Build();
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

    /// <summary>O que a justificação custa, contra a mesma paginação alinhada à esquerda.</summary>
    /// <remarks>
    /// Justificar parte cada linha nas fronteiras de branco — uma string por segmento, contra uma
    /// por run —, e é o preço de manter a invariante do caret sem esticar o texto dentro de um run.
    /// O número que importa é a razão entre os dois: se ela crescer, é aqui que aparece.
    /// </remarks>
    [Fact]
    public void Justificar_a_tese_inteira_cabe_no_mesmo_teto()
    {
        var source = LongDocument.Build();
        var measurer = new FakeTextMeasurer();
        var justified = TypographyPreset.Default with { Alignment = TextAlignment.Justify };

        var (leftMs, lines) = Time(source, TypographyPreset.Default, measurer);
        var (justifiedMs, _) = Time(source, justified, measurer);

        output.WriteLine($"linhas          : {lines:N0}");
        output.WriteLine($"à esquerda      : {leftMs:N1} ms   ({leftMs * 1000.0 / lines:N2} µs/linha)");
        output.WriteLine($"justificado     : {justifiedMs:N1} ms   ({justifiedMs * 1000.0 / lines:N2} µs/linha)");
        output.WriteLine($"custo do passe  : {justifiedMs / leftMs:N2}x");

        Assert.True(
            justifiedMs < CeilingMilliseconds,
            $"justificar levou {justifiedMs:N0}ms, além do teto de {CeilingMilliseconds}ms");
    }

    /// <summary>O melhor de cinco. Máquina compartilhada mede alto, nunca baixo.</summary>
    /// <remarks>
    /// Uma passada só dava 15% de variação entre execuções — mais do que a diferença que se quer
    /// enxergar. O mínimo é o número menos contaminado por outro processo tendo roubado o núcleo, e
    /// é o que torna a razão entre as duas colunas comparável de uma execução para outra.
    /// </remarks>
    private static (double Milliseconds, int Lines) Time(
        string source,
        TypographyPreset preset,
        ITextMeasurer measurer)
    {
        // Uma passada fora do relógio, pelo mesmo motivo do teste acima.
        var paginated = Paginate(source, preset, measurer);
        var best = double.MaxValue;

        for (var attempt = 0; attempt < 5; attempt++)
        {
            var stopwatch = Stopwatch.StartNew();
            _ = Paginate(source, preset, measurer);
            stopwatch.Stop();

            best = Math.Min(best, stopwatch.Elapsed.TotalMilliseconds);
        }

        return (best, paginated.Pages.Sum(page => page.Lines.Count));
    }

    private static PaginatedDocument Paginate(string source, TypographyPreset preset, ITextMeasurer measurer) =>
        LayoutEngine.Layout(MarkupParser.Parse(source, preset), PageSettings.A4, measurer, preset: preset);
}
