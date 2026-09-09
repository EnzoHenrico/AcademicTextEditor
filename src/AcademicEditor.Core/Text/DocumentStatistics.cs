namespace AcademicEditor.Core.Text;

/// <summary>
/// Quantos caracteres e quantas palavras um texto tem — o que a barra de status mostra.
/// </summary>
/// <remarks>
/// <para>
/// <b>Conta caracteres, não unidades UTF-16.</b> Um par substituto é um caractere para quem
/// escreveu e dois para o buffer; contar o buffer faria um emoji valer dois e um limite de
/// caracteres de submissão mentir. A conta pula a metade baixa do par, que é o mesmo critério que
/// Backspace e Delete já seguem para não apagar meio code point.
/// </para>
/// <para>
/// <b>Palavra aqui é um trecho sem branco</b>, e não a definição de três classes do
/// <c>WordBoundaries</c>. As duas perguntas são diferentes: aquela responde "o que o duplo clique
/// seleciona", onde uma vírgula é uma unidade própria; esta responde "quantas palavras o autor
/// escreveu", e ali <c>Silva,</c> é uma palavra, como em qualquer contador.
/// </para>
/// <para>
/// <b>Conta o buffer cru, com a marcação.</b> O <c>#</c> de um título e os asteriscos de um
/// negrito entram. É o simples e o previsível — o que se vê é o que está no arquivo —, e a
/// alternativa (contar só o que é desenhado) faria o número mudar ao mover o caret, porque a
/// marcação é revelada na linha em que ele está.
/// </para>
/// </remarks>
public readonly record struct DocumentStatistics(int Characters, int Words)
{
    public static readonly DocumentStatistics Empty = default;

    /// <summary>Uma passada só, sem alocar: o texto chega como span e nada é materializado.</summary>
    public static DocumentStatistics Of(ReadOnlySpan<char> text)
    {
        var characters = 0;
        var words = 0;
        var inWord = false;

        foreach (var current in text)
        {
            var isSpace = char.IsWhiteSpace(current);

            if (!isSpace && !inWord)
            {
                words++;
            }

            inWord = !isSpace;

            if (!char.IsLowSurrogate(current))
            {
                characters++;
            }
        }

        return new DocumentStatistics(characters, words);
    }
}
