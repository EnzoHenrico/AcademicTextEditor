using System.Diagnostics;

using AcademicEditor.Core.Layout;
using AcademicEditor.Core.Parsing;
using AcademicEditor.Core.State;
using AcademicEditor.Core.Tests.Layout;

using Xunit.Abstractions;

namespace AcademicEditor.Core.Tests.State;

/// <summary>
/// Quanto custa achar a linha de um offset num documento do tamanho de uma tese.
/// </summary>
/// <remarks>
/// A varredura anterior custava proporcional ao que existia <b>antes</b> do caret: barata no
/// começo do documento e cara no fim, que é justamente onde se escreve. E ela roda a cada tecla,
/// a cada movimento do caret e uma vez por ponta da seleção.
/// </remarks>
public sealed class CaretGeometryPerformanceTests(ITestOutputHelper output)
{
    private const int Lookups = 20_000;
    private const int CeilingMilliseconds = 2000;

    [Fact]
    public void Achar_a_linha_no_fim_da_tese_nao_depende_do_tamanho_dela()
    {
        var measurer = new FakeTextMeasurer();
        var source = LongDocument.Build();
        var document = LayoutEngine.Layout(MarkupParser.Parse(source), PageSettings.A4, measurer);
        var lines = document.Pages.Sum(page => page.Lines.Count);

        // O pior caso da varredura: o último offset do documento, com tudo antes dele para andar.
        var last = source.Length;

        _ = CaretGeometry.Locate(last, document, measurer);

        var stopwatch = Stopwatch.StartNew();

        for (var attempt = 0; attempt < Lookups; attempt++)
        {
            _ = CaretGeometry.Locate(last, document, measurer);
        }

        stopwatch.Stop();

        var elapsed = stopwatch.Elapsed.TotalMilliseconds;

        output.WriteLine($"linhas        : {lines:N0}");
        output.WriteLine($"buscas        : {Lookups:N0}");
        output.WriteLine($"tempo         : {elapsed:N1} ms");
        output.WriteLine($"por busca     : {elapsed * 1000.0 / Lookups:N2} µs");
        output.WriteLine($"passos da busca binária: ~{Math.Ceiling(Math.Log2(lines)):N0} contra {lines:N0} da varredura");

        Assert.True(
            elapsed < CeilingMilliseconds,
            $"{Lookups:N0} buscas levaram {elapsed:N0}ms, além do teto de {CeilingMilliseconds}ms");
    }
}
