using AcademicEditor.Core.Parsing.Ast;

namespace AcademicEditor.Core.Layout;

/// <summary>
/// Métricas verticais de uma linha para um dado estilo, em pontos.
/// </summary>
/// <param name="HeightPt">Altura total da linha, incluindo o entrelinhamento da fonte.</param>
/// <param name="BaselinePt">Distância do topo da linha até a baseline, onde a renderização assenta os glifos.</param>
public readonly record struct LineMetrics(double HeightPt, double BaselinePt);

/// <summary>
/// A única costura entre o Core e a camada gráfica. O motor de layout precisa saber quanto um
/// texto ocupa, e só o toolkit sabe responder — mas o Core não pode conhecer o toolkit.
/// </summary>
/// <remarks>
/// <para>
/// A superfície é deliberadamente mínima: é o que um word-wrap greedy consome, nada além.
/// <c>ReadOnlySpan&lt;char&gt;</c> em vez de <c>string</c> porque o line breaker mede prefixos de
/// uma mesma palavra ao procurar onde parti-la — com string, cada tentativa seria uma alocação
/// descartável na geração 0.
/// </para>
/// <para>
/// Implementações são chamadas de uma thread de background (o layout roda em <c>Task.Run</c>) e
/// precisam ser seguras nessa condição.
/// </para>
/// </remarks>
public interface ITextMeasurer
{
    /// <summary>Largura do texto em pontos, sem quebra de linha.</summary>
    double MeasureWidthPt(ReadOnlySpan<char> text, TextStyle style);

    /// <summary>Altura e baseline de uma linha do estilo dado, independente do conteúdo.</summary>
    LineMetrics GetLineMetrics(TextStyle style);
}
