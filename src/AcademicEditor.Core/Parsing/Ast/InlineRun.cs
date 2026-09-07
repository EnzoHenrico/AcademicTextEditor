namespace AcademicEditor.Core.Parsing.Ast;

/// <summary>
/// Trecho contíguo de texto com um único estilo. É a unidade que o line breaker mede e quebra.
/// </summary>
/// <remarks>
/// <para>
/// <b>Invariante de mapeamento:</b> o caractere <c>Text[i]</c> corresponde ao offset
/// <c>SourceStart + i</c> no buffer. É essa correspondência que permite ao caret e ao
/// highlighting irem da geometria da tela de volta para a posição no texto, sem tabela auxiliar.
/// </para>
/// <para>
/// <c>Text</c> é sempre cópia literal do trecho que o run cobre — o parser não inventa nem
/// suprime caractere nenhum. O que fica de fora dos runs é a marcação: o <c>## </c> de um
/// heading, o <c>\n</c> entre duas linhas e o <c>\r</c> de uma fonte ainda em CRLF.
/// </para>
/// <para>
/// É struct para não alocar um objeto por trecho: um documento de 300 páginas tem dezenas de
/// milhares de runs, e cada um seria pressão de GC gratuita na geração 0 a cada repaginação.
/// </para>
/// </remarks>
public readonly record struct InlineRun(string Text, int SourceStart, TextStyle Style)
{
    /// <summary>Offset logo após o último caractere do run.</summary>
    public int SourceEnd => SourceStart + Text.Length;
}
