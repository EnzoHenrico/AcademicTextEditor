using System.Diagnostics;

using AcademicEditor.Core.Layout;
using AcademicEditor.Core.Parsing;
using AcademicEditor.Core.State;
using AcademicEditor.Core.Tests.Layout;

using Xunit.Abstractions;

namespace AcademicEditor.Core.Tests.State;

/// <summary>
/// Quanto custa a geometria de um Ctrl+A num documento do tamanho de uma tese.
/// </summary>
/// <remarks>
/// <para>
/// É o pior caso da seleção por construção: um retângulo por linha visual, e a tese tem dezesseis
/// mil. Se doer, o corte é pedir os retângulos por folha visível em vez do documento inteiro — o
/// culling do desenho já sabe quais são.
/// </para>
/// <para>
/// O teto existe para pegar regressão de ordem de grandeza, não para cravar o número: o dia em
/// que alguém medir o texto das linhas que estão inteiramente dentro do trecho, isto fica
/// vermelho.
/// </para>
/// </remarks>
public sealed class SelectionPerformanceTests(ITestOutputHelper output)
{
    private const int CeilingMilliseconds = 500;

    [Fact]
    public void Selecionar_a_tese_inteira_cabe_no_teto()
    {
        var source = LongDocument.Build();
        var measurer = new FakeTextMeasurer();
        var document = LayoutEngine.Layout(MarkupParser.Parse(source), PageSettings.A4, measurer);
        var selection = new Selection(0, CaretNavigator.At(source.Length, document, measurer));

        _ = SelectionGeometry.RectsFor(selection, document, measurer);

        var stopwatch = Stopwatch.StartNew();
        var rects = SelectionGeometry.RectsFor(selection, document, measurer);
        stopwatch.Stop();

        var lines = document.Pages.Sum(page => page.Lines.Count);
        var elapsed = stopwatch.Elapsed.TotalMilliseconds;

        output.WriteLine($"linhas      : {lines:N0}");
        output.WriteLine($"retângulos  : {rects.Count:N0}");
        output.WriteLine($"tempo       : {elapsed:N2} ms");
        output.WriteLine($"por linha   : {elapsed * 1000.0 / lines:N2} µs");

        Assert.True(rects.Count > 10_000, $"esperava ~16 mil retângulos, vieram {rects.Count}");
        Assert.True(
            elapsed < CeilingMilliseconds,
            $"a geometria de {rects.Count} retângulos levou {elapsed:N0}ms, além do teto de {CeilingMilliseconds}ms");
    }
}
