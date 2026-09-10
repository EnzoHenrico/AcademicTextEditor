using System.Diagnostics;

using AcademicEditor.Core.Layout;
using AcademicEditor.Core.Layout.Model;
using AcademicEditor.Core.Parsing;
using AcademicEditor.Core.State;
using AcademicEditor.Core.Tests.Layout;

using Xunit.Abstractions;

namespace AcademicEditor.Core.Tests.State;

/// <summary>
/// Quanto custa achar a linha de um offset numa tese.
/// </summary>
/// <remarks>
/// <para>
/// Mede o <b>pior caso da varredura que existia aqui</b>: o último offset do documento, com tudo
/// antes dele para andar. Era proporcional ao que existe antes do caret — dezesseis mil linhas —, e
/// passa a ser proporcional ao logaritmo disso.
/// </para>
/// <para>
/// O teto é generoso porque isto roda em máquina compartilhada: ele não existe para cravar o
/// número, existe para pegar uma regressão de ordem de grandeza — o dia em que alguém puser uma
/// varredura de volta no caminho do caret.
/// </para>
/// </remarks>
public sealed class CaretGeometryPerformanceTests(ITestOutputHelper output)
{
    private const int Lookups = 20_000;
    private const int CeilingMilliseconds = 500;

    [Fact]
    public void Achar_a_linha_do_ultimo_offset_e_barato()
    {
        var source = LongDocument.Build();
        var measurer = new FakeTextMeasurer();
        var document = LayoutEngine.Layout(MarkupParser.Parse(source), PageSettings.A4, measurer);

        var lines = document.Index.Count;
        var last = source.Length;

        // Uma passada fora do relógio: paga JIT e o aquecimento do heap.
        _ = CaretGeometry.Locate(last, document, measurer);

        var stopwatch = Stopwatch.StartNew();

        for (var lookup = 0; lookup < Lookups; lookup++)
        {
            _ = CaretGeometry.Locate(last, document, measurer);
        }

        stopwatch.Stop();

        var elapsed = stopwatch.Elapsed.TotalMilliseconds;
        var steps = (int)Math.Ceiling(Math.Log2(lines));

        output.WriteLine($"linhas       : {lines:N0}");
        output.WriteLine($"buscas       : {Lookups:N0}");
        output.WriteLine($"por busca    : {elapsed * 1000.0 / Lookups:N2} µs");
        output.WriteLine($"passos       : ~{steps}, contra {lines:N0} da varredura");

        Assert.True(
            elapsed < CeilingMilliseconds,
            $"{Lookups:N0} buscas levaram {elapsed:N0}ms, além do teto de {CeilingMilliseconds}ms — "
                + $"num documento de {lines:N0} linhas, isso é varredura e não busca binária");
    }
}
