using AcademicEditor.Core.Parsing.Ast;

namespace AcademicEditor.Core.Layout.Model;

/// <summary>
/// Trecho de texto já posicionado dentro de uma linha: o que a renderização desenha de uma vez.
/// </summary>
/// <remarks>
/// <para>
/// <b>Tudo aqui é relativo à linha</b>, nas duas dimensões: <see cref="XPt"/> é medido do início da
/// linha e não da página, e <see cref="LineOffset"/> é medido do <c>SourceStart</c> da linha e não
/// do buffer. Assim a linha inteira pode ser reposicionada — na folha ou no buffer — sem tocar em
/// run nenhum.
/// </para>
/// <para>
/// <b>O offset ser relativo é o que faz o reflow incremental caber numa tecla.</b> Deslocar as
/// linhas que vieram depois de uma edição é a metade cara dele, e com offset absoluto custava
/// reescrever cada run de cada linha: à esquerda são um ou dois por linha, mas justificado são
/// ~20, porque a justificação parte a linha em cada fronteira de branco. Relativo, deslocar uma
/// linha é uma cópia de record e nada mais.
/// </para>
/// <para>
/// Struct pelo mesmo motivo do <see cref="InlineRun"/>: um documento longo tem dezenas de milhares
/// deles a cada repaginação.
/// </para>
/// </remarks>
/// <param name="Text">O texto do trecho, incluindo os espaços que o separam do trecho seguinte.</param>
/// <param name="LineOffset">
/// Onde o trecho começa <b>dentro da linha</b>, em caracteres. O offset no buffer é
/// <c>line.SourceStart + run.LineOffset</c> — nunca é negativo, porque a linha sempre começa em ou
/// antes do primeiro run que ela desenha.
/// </param>
public readonly record struct LaidOutRun(
    string Text,
    TextStyle Style,
    double XPt,
    double WidthPt,
    int LineOffset)
{
    /// <summary>Onde o trecho termina dentro da linha.</summary>
    public int LineEnd => LineOffset + Text.Length;
}
