using AcademicEditor.Core.Layout;
using AcademicEditor.Core.Parsing.Ast;

namespace AcademicEditor.Core.Tests.Layout;

/// <summary>
/// Medidor determinístico: toda largura é múltiplo exato de <see cref="CharWidthPt"/>, então as
/// asserções de quebra podem ser números redondos em vez de tolerâncias. É também a razão de
/// <see cref="ITextMeasurer"/> existir — sem ele, testar o motor exigiria subsistema gráfico.
/// </summary>
/// <remarks>
/// As medidas escalam com o tamanho da fonte relativo ao corpo, para que os testes de runs
/// heterogêneos façam sentido: texto em 22pt mede o dobro de texto em 11pt.
/// </remarks>
internal sealed class FakeTextMeasurer(double charWidthPt = 10.0, double lineHeightPt = 20.0) : ITextMeasurer
{
    public const double BaselineRatio = 0.8;

    public double CharWidthPt { get; } = charWidthPt;

    public double MeasureWidthPt(ReadOnlySpan<char> text, TextStyle style) =>
        text.Length * CharWidthPt * Scale(style);

    public LineMetrics GetLineMetrics(TextStyle style)
    {
        var height = lineHeightPt * Scale(style);
        return new LineMetrics(height, height * BaselineRatio);
    }

    private static double Scale(TextStyle style) => style.FontSizePt / TextStyle.Body.FontSizePt;
}
