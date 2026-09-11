namespace AcademicEditor.Core.Parsing;

/// <summary>
/// O cabeçalho de metadados do arquivo: <c>---</c>, pares <c>chave: valor</c>, <c>---</c>, no topo.
/// </summary>
/// <remarks>
/// <para>
/// <b><c>---</c>, e não uma tag da família do <c>\page</c>.</b> A escolha foi interoperabilidade
/// contra coerência, e ganhou a primeira: Pandoc, Obsidian e Jekyll já leem e <i>escondem</i> front
/// matter, então o <c>.md</c> de uma tese atravessa essas ferramentas com o cabeçalho invisível. A
/// consequência está registrada no <see cref="BlockTags"/>: a forma pareada <c>\tag\</c> …
/// <c>/tag/</c> ficou sem nenhum cliente, e por isso continua reservada e sem máquina.
/// </para>
/// <para>
/// <b>Subconjunto plano de YAML, sem biblioteca:</b> <c>chave: valor</c> escalar, uma por linha.
/// Não há lista, não há aninhamento e não há aspas obrigatórias. Chaves desconhecidas atravessam
/// intactas — são texto do buffer, e ninguém as reescreve.
/// </para>
/// <para>
/// <b>Só no topo, e só fechado.</b> Um <c>---</c> no meio do texto é texto; um <c>---</c> de
/// abertura sem fechamento também, e o documento inteiro continua visível. É o que impede uma
/// linha divisória digitada por engano de engolir o começo da tese.
/// </para>
/// </remarks>
public static class FrontMatter
{
    /// <summary>A cerca, sozinha numa linha, abrindo e fechando.</summary>
    public const string Fence = "---";

    /// <summary>Separa a chave do valor.</summary>
    private const char Separator = ':';

    /// <summary>
    /// Quantos caracteres do começo da fonte o cabeçalho ocupa, terminador incluído; 0 quando não
    /// há cabeçalho.
    /// </summary>
    /// <remarks>
    /// É também <b>onde o corpo começa</b>: todo offset menor que isto pertence ao cabeçalho, e é
    /// por essa conta que o caret sabe onde não entrar.
    /// </remarks>
    public static int BodyStart(string source)
    {
        ArgumentNullException.ThrowIfNull(source);

        if (!OpensAt(source, 0, out var position))
        {
            return 0;
        }

        while (position < source.Length)
        {
            var lineEnd = source.IndexOf('\n', position);
            var next = lineEnd < 0 ? source.Length : lineEnd + 1;
            var line = source.AsSpan(position, (lineEnd < 0 ? source.Length : lineEnd) - position);

            if (line.Trim().SequenceEqual(Fence))
            {
                return next;
            }

            position = next;
        }

        // Abriu e não fechou: não é cabeçalho, é texto. Devolver o arquivo inteiro esconderia a
        // tese de quem digitou três hifens por engano.
        return 0;
    }

    /// <summary>
    /// O valor de uma chave do cabeçalho, ou <c>null</c> quando ela não está lá.
    /// </summary>
    /// <remarks>
    /// A primeira ocorrência ganha, que é o que qualquer leitor de YAML faz — e o que faz uma chave
    /// repetida por engano ter um resultado previsível em vez de depender da ordem de leitura.
    /// </remarks>
    public static string? Read(string source, string key)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentException.ThrowIfNullOrEmpty(key);

        var end = BodyStart(source);

        if (end == 0 || !OpensAt(source, 0, out var position))
        {
            return null;
        }

        while (position < end)
        {
            var lineEnd = source.IndexOf('\n', position);
            var stop = lineEnd < 0 || lineEnd > end ? end : lineEnd;
            var line = source.AsSpan(position, stop - position);
            var colon = line.IndexOf(Separator);

            if (colon > 0 && line[..colon].Trim().SequenceEqual(key))
            {
                var value = line[(colon + 1)..].Trim();

                return value.IsEmpty ? null : value.ToString();
            }

            position = stop + 1;
        }

        return null;
    }

    /// <summary>A fonte abre com a cerca? Em caso afirmativo, onde começa a linha seguinte.</summary>
    private static bool OpensAt(string source, int start, out int next)
    {
        var lineEnd = source.IndexOf('\n', start);

        next = lineEnd < 0 ? source.Length : lineEnd + 1;

        // Sem linha seguinte não há o que fechar, então nem se abre.
        return lineEnd >= 0
            && source.AsSpan(start, lineEnd - start).Trim().SequenceEqual(Fence);
    }
}
