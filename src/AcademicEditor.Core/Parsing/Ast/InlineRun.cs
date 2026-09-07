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
/// O único ponto onde <c>Text</c> não é cópia literal da fonte é o espaço de junção que o parser
/// acrescenta no fim de uma linha soft-wrapped: ele ocupa a posição do <c>\n</c>, mantendo a
/// invariante de comprimento. Isso pressupõe entrada normalizada em LF — ver <see cref="MarkupParser"/>.
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
