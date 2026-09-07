using AcademicEditor.Core.Parsing.Ast;

namespace AcademicEditor.Core.Layout.Model;

/// <summary>
/// Trecho de texto já posicionado dentro de uma linha: o que a renderização desenha de uma vez.
/// </summary>
/// <remarks>
/// <see cref="XPt"/> é relativo ao início da linha, não à página — assim a linha inteira pode ser
/// reposicionada verticalmente sem tocar nos runs. Struct pelo mesmo motivo do
/// <see cref="InlineRun"/>: um documento longo tem dezenas de milhares deles a cada repaginação.
/// </remarks>
/// <param name="Text">O texto do trecho, incluindo os espaços que o separam do trecho seguinte.</param>
/// <param name="SourceStart">Offset do primeiro caractere no buffer — o elo de volta para o caret.</param>
public readonly record struct LaidOutRun(
    string Text,
    TextStyle Style,
    double XPt,
    double WidthPt,
    int SourceStart)
{
    public int SourceEnd => SourceStart + Text.Length;
}
