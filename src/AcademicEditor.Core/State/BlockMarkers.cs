using AcademicEditor.Core.Layout.Model;

namespace AcademicEditor.Core.State;

/// <summary>
/// O que uma tecla de apagar deve remover quando um marcador de bloco está no caminho.
/// </summary>
/// <remarks>
/// <para>
/// Um marcador como o <c>\page</c> é uma unidade: ele só significa quebra de página enquanto
/// estiver sozinho na linha. Apagar o <c>\n</c> que o isola o gruda no texto de cima, e aí ele
/// deixa de ser marcador e passa a aparecer escrito no documento — que foi exatamente o defeito
/// relatado. A tecla de apagar tem de remover o marcador inteiro ou não tocá-lo.
/// </para>
/// <para>
/// O cabeçalho de metadados é a mesma família vista pelo outro lado: ele não sai <b>nunca</b> por
/// uma tecla de apagar. Apagar o <c>\n</c> que fecha a cerca desmancharia o cabeçalho inteiro — o
/// <c>---</c> deixaria de estar sozinho na linha —, e o arquivo inteiro passaria a aparecer na
/// folha de uma vez, sem que nada na tela explicasse por quê.
/// </para>
/// <para>
/// Mora no Core, e não no ViewModel, porque a decisão depende do <see cref="PaginatedDocument"/> e
/// porque é a regra que precisa de teste — a camada visual só obedece.
/// </para>
/// </remarks>
public static class BlockMarkers
{
    /// <summary>
    /// Trecho que o Backspace deve apagar, ou <c>null</c> se não há marcador em jogo.
    /// </summary>
    /// <remarks>
    /// Um trecho <b>vazio</b> é a terceira resposta, e quer dizer "não apague nada": é o que a
    /// fronteira do cabeçalho de metadados devolve. Quem consome já trata comprimento zero como
    /// nada a fazer, então a recusa não precisa de um caminho próprio — e não abre grupo de undo
    /// nem pede repaginação por uma tecla que não mudou o texto.
    /// </remarks>
    public static TextRange? BackspaceRange(int offset, PaginatedDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        if (IsBodyStart(offset, document))
        {
            return TextRange.Empty;
        }

        // O caret está no próprio marcador: ele sai inteiro.
        if (MarkerAt(offset, document) is { } here)
        {
            return here;
        }

        // O caret está logo depois de um marcador. Sem isto, o Backspace comeria só o '\n' e o
        // marcador viraria texto na linha de cima.
        return MarkerEndingBefore(offset, document);
    }

    /// <summary>Trecho que o Delete deve apagar, ou <c>null</c>.</summary>
    public static TextRange? DeleteRange(int offset, PaginatedDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        if (MarkerAt(offset, document) is { } here)
        {
            return here;
        }

        return MarkerStartingAfter(offset, document);
    }

    /// <summary>O marcador cobre este offset?</summary>
    private static TextRange? MarkerAt(int offset, PaginatedDocument document)
    {
        foreach (var line in Markers(document))
        {
            if (line.SourceStart == offset)
            {
                return WithSeparator(line);
            }
        }

        return null;
    }

    // O marcador termina onde a linha seguinte começa: o '\n' que os separa é o caractere que o
    // Backspace mira, e apagá-lo é o que fundiria o marcador com o texto de cima.
    private static TextRange? MarkerEndingBefore(int offset, PaginatedDocument document)
    {
        foreach (var line in Markers(document))
        {
            if (line.SourceEnd + 1 == offset)
            {
                return WithSeparator(line);
            }
        }

        return null;
    }

    private static TextRange? MarkerStartingAfter(int offset, PaginatedDocument document)
    {
        foreach (var line in Markers(document))
        {
            if (line.SourceStart == offset + 1)
            {
                return WithSeparator(line);
            }
        }

        return null;
    }

    /// <summary>
    /// O offset é o primeiro do corpo, logo depois do cabeçalho de metadados?
    /// </summary>
    /// <remarks>
    /// Sai do documento publicado e não da fonte: o cabeçalho é a primeira linha da primeira folha,
    /// então a resposta custa dois acessos. Reler a fonte para descobrir onde o corpo começa
    /// materializaria o documento inteiro a cada Backspace.
    /// </remarks>
    public static bool IsBodyStart(int offset, PaginatedDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        var first = document.Pages[0].Lines;

        return first.Count > 0
            && first[0].Kind == LineKind.FrontMatter
            && first[0].SourceEnd == offset;
    }

    private static IEnumerable<LaidOutLine> Markers(PaginatedDocument document) =>
        document.Pages
            .SelectMany(page => page.Lines)
            .Where(line => line.Kind is LineKind.PageBreak or LineKind.TableOfContents);

    // O marcador mais o '\n' que o isola. É o '\n' que o Backspace miraria, e apagá-lo sozinho é
    // o que fundiria o marcador com o texto de cima.
    private static TextRange WithSeparator(LaidOutLine line) =>
        new(line.SourceStart, line.SourceLength + 1);
}
