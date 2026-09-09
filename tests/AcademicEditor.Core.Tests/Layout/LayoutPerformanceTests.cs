using System.Diagnostics;
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
}
