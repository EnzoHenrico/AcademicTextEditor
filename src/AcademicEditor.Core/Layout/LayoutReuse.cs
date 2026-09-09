using AcademicEditor.Core.Layout.Model;

namespace AcademicEditor.Core.Layout;

/// <summary>
/// O que a paginação anterior ainda serve para a próxima, e onde ela deixou de servir.
/// </summary>
/// <remarks>
/// <para>
/// Medido: com o cache de medição no lugar, uma tecla num documento de 301 páginas custa 74ms — e
/// <b>o parser responde por 0,5ms deles</b>. O resto é construir 16.056 <see cref="LaidOutLine"/>
/// para um documento em que uma linha mudou. Reparsear tudo é barato; requebrar tudo não é, e é
/// por isso que o reflow incremental daqui não tem nível de parser.
/// </para>
/// <para>
/// <b>O trecho alterado sai de comparar os dois textos</b> — prefixo e sufixo comuns —, e não de
/// rastrear as edições. É exato por construção, sobrevive a uma rajada de teclas coalescida num
/// layout só, e trata colar, desfazer e refazer sem caso especial nenhum.
/// </para>
/// <para>
/// <b>Só o caso comum entra aqui</b>: alteração que não cria nem apaga <c>\n</c>, isto é, dentro de
/// uma linha da fonte. Aí os blocos são os mesmos um a um, e só um deles precisa ser requebrado.
/// Qualquer outra coisa — Enter, Backspace numa fronteira, colar um parágrafo — devolve
/// <c>null</c>, e o motor pagina do zero pelo caminho de sempre.
/// </para>
/// </remarks>
/// <param name="DirtyStart">Primeiro offset que mudou. Vale nos dois textos: até ele, são iguais.</param>
/// <param name="DirtyOldEnd">Fim do trecho alterado, em coordenadas do texto <b>antigo</b>.</param>
/// <param name="LengthDelta">Quanto o documento cresceu, ou encolheu.</param>
public sealed record LayoutReuse(
    PaginatedDocument Previous,
    int DirtyStart,
    int DirtyOldEnd,
    int LengthDelta)
{
    /// <summary>O reaproveitamento possível entre duas versões do texto, ou <c>null</c>.</summary>
    public static LayoutReuse? Between(string previousSource, string currentSource, PaginatedDocument previous)
    {
        ArgumentNullException.ThrowIfNull(previousSource);
        ArgumentNullException.ThrowIfNull(currentSource);
        ArgumentNullException.ThrowIfNull(previous);

        // Sem texto anterior não há layout anterior que preste — é o primeiro documento, ou um
        // arquivo recém-aberto, e os dois querem a paginação completa.
        if (previousSource.Length == 0)
        {
            return null;
        }

        var prefix = previousSource.AsSpan().CommonPrefixLength(currentSource);
        var suffix = CommonSuffixLength(previousSource, currentSource, prefix);

        var oldEnd = previousSource.Length - suffix;
        var newEnd = currentSource.Length - suffix;

        // Um '\n' criado ou apagado muda quantos blocos existem, e o mapa de um para um entre o
        // layout velho e o novo deixa de valer. É a única condição que precisa ser verificada:
        // sem ela, o bloco n do texto novo não é mais o bloco n do antigo.
        if (previousSource.AsSpan(prefix, oldEnd - prefix).Contains('\n')
            || currentSource.AsSpan(prefix, newEnd - prefix).Contains('\n'))
        {
            return null;
        }

        return new LayoutReuse(previous, prefix, oldEnd, currentSource.Length - previousSource.Length);
    }

    // Comparação caractere a caractere, e é o custo desta abordagem: ~1ms por megabyte, contra os
    // 74ms que ela economiza. Vetorizar o sufixo exigiria inverter os spans, que aloca.
    private static int CommonSuffixLength(string previous, string current, int prefix)
    {
        var limit = Math.Min(previous.Length, current.Length) - prefix;
        var length = 0;

        while (length < limit && previous[^(length + 1)] == current[^(length + 1)])
        {
            length++;
        }

        return length;
    }
}
