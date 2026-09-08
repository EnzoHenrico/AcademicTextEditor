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
/// Mora no Core, e não no ViewModel, porque a decisão depende do <see cref="PaginatedDocument"/> e
/// porque é a regra que precisa de teste — a camada visual só obedece.
/// </para>
/// </remarks>
public static class BlockMarkers
{
    /// <summary>Trecho que o Backspace deve apagar, ou <c>null</c> se não há marcador em jogo.</summary>
    public static TextRange? BackspaceRange(int offset, PaginatedDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

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

    private static IEnumerable<LaidOutLine> Markers(PaginatedDocument document) =>
        document.Pages
            .SelectMany(page => page.Lines)
            .Where(line => line.Kind == LineKind.PageBreak);

    // O marcador mais o '\n' que o isola. É o '\n' que o Backspace miraria, e apagá-lo sozinho é
    // o que fundiria o marcador com o texto de cima.
    private static TextRange WithSeparator(LaidOutLine line) =>
        new(line.SourceStart, line.SourceLength + 1);
}
