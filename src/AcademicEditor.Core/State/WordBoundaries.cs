using AcademicEditor.Core.Layout.Model;

namespace AcademicEditor.Core.State;

/// <summary>
/// Onde uma palavra começa e termina, para o duplo clique.
/// </summary>
/// <remarks>
/// <para>
/// Trabalha sobre os <b>runs da linha</b>, não sobre o buffer, e é isso que a faz respeitar o que
/// está desenhado — a mesma regra que o <see cref="CaretNavigator"/> segue. Um offset que nenhuma
/// linha cobre não tem palavra, porque não tem posição na tela.
/// </para>
/// <para>
/// A regra de três classes — palavra, branco, símbolo — é a de qualquer editor: dois cliques em
/// <c>Silva</c> pegam <c>Silva</c>, dois no espaço pegam o espaço, dois na vírgula pegam a
/// vírgula. <c>_</c> conta como letra, senão <c>nome_completo</c> viraria três seleções.
/// </para>
/// </remarks>
public static class WordBoundaries
{
    private enum CharClass
    {
        Word,
        Space,
        Symbol,
    }

    /// <summary>A palavra sob <paramref name="offset"/>, ou um trecho vazio ali quando não há.</summary>
    public static TextRange WordAt(int offset, PaginatedDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        var (page, line) = CaretGeometry.FindLine(offset, document);

        if (page < 0)
        {
            return new TextRange(offset, 0);
        }

        foreach (var run in document.Pages[page].Lines[line].Runs)
        {
            // Expande dentro de um run só. Um run é um trecho contíguo de um estilo, então a
            // fronteira de estilo — o começo de um **negrito** — vale como fronteira de palavra:
            // são duas palavras diferentes na tela. Em texto comum a linha inteira é um run, que
            // é o caso que importa.
            if (offset >= run.SourceStart && offset <= run.SourceEnd && run.Text.Length > 0)
            {
                return Expand(run.Text, run.SourceStart, offset);
            }
        }

        return new TextRange(offset, 0);
    }

    private static TextRange Expand(string text, int runStart, int offset)
    {
        var index = offset - runStart;

        // O caractere à direita do offset é o que se apontou; no fim do run não há nenhum, e vale
        // o da esquerda — é o que faz dois cliques depois da última letra da linha pegarem a
        // palavra que acabou de terminar.
        //
        // O offset chega arredondado para a fronteira de caractere mais próxima, então a metade
        // direita da última letra de uma palavra já cai no branco seguinte, e é o branco que se
        // seleciona. Meio caractere de imprecisão, simétrica, contra uma regra a mais que teria a
        // sua própria metade errada — clicar na metade esquerda de uma vírgula pegaria a palavra
        // antes dela.
        var probe = index < text.Length ? index : index - 1;

        if (probe < 0)
        {
            return new TextRange(offset, 0);
        }


        var kind = ClassOf(text[probe]);
        var start = probe;
        var end = probe + 1;

        while (start > 0 && ClassOf(text[start - 1]) == kind)
        {
            start--;
        }

        while (end < text.Length && ClassOf(text[end]) == kind)
        {
            end++;
        }

        return new TextRange(runStart + start, end - start);
    }

    private static CharClass ClassOf(char value)
    {
        if (char.IsLetterOrDigit(value) || value == '_')
        {
            return CharClass.Word;
        }

        return char.IsWhiteSpace(value) ? CharClass.Space : CharClass.Symbol;
    }
}
