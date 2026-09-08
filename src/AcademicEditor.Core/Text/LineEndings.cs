namespace AcademicEditor.Core.Text;

/// <summary>
/// Converte fim de linha para LF. É a única tradução que o editor faz no texto do autor.
/// </summary>
/// <remarks>
/// A conversão acontece na <b>entrada</b>, no <see cref="EditorDocument"/>, e não em cada operação
/// de edição. A diferença importa: com CRLF cru no buffer, o Backspace no início de uma linha
/// apagaria só o <c>\n</c> e deixaria um <c>\r</c> órfão no meio do texto, e cada tecla de seta,
/// o parser e o line breaker precisariam carregar o caso especial para sempre. Um ponto de
/// conversão custa uma varredura por documento aberto.
/// </remarks>
public static class LineEndings
{
    /// <summary>
    /// <paramref name="text"/> com <c>\r\n</c> e <c>\r</c> solto trocados por <c>\n</c>. Devolve a
    /// mesma referência quando não há <c>\r</c> — o caso comum não deve alocar nada.
    /// </summary>
    public static string NormalizeToLf(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        if (!text.Contains('\r'))
        {
            return text;
        }

        var builder = new System.Text.StringBuilder(text.Length);

        for (var i = 0; i < text.Length; i++)
        {
            if (text[i] != '\r')
            {
                builder.Append(text[i]);
                continue;
            }

            // CR sozinho é fim de linha do Mac OS clássico, e ainda aparece em arquivo convertido
            // à mão. Tratar os dois aqui evita que um deles vire caractere de controle no meio de
            // um parágrafo.
            builder.Append('\n');

            if (i + 1 < text.Length && text[i + 1] == '\n')
            {
                i++;
            }
        }

        return builder.ToString();
    }
}
