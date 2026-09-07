namespace AcademicEditor.Core.Text;

/// <summary>
/// Vista imutável do buffer num instante. É o que o layout consome numa thread de background
/// enquanto a digitação continua alterando a <see cref="PieceTable"/> na UI thread.
/// </summary>
/// <remarks>
/// <para>
/// <b>Não copia texto.</b> Guarda as duas fontes e uma cópia da lista de peças. A estabilidade
/// vem da forma como o buffer de adição cresce: ele só recebe escrita <i>além</i> do
/// comprimento que este snapshot registrou, e quando precisa crescer é realocado para um array
/// novo — o antigo continua íntegro na mão de quem o segurava. Nenhum lock, e nenhuma janela em
/// que o leitor veja meio caractere.
/// </para>
/// <para>
/// A lista de peças, essa sim, é copiada: a digitação altera o comprimento da última peça no
/// lugar, e um array compartilhado corromperia o snapshot. A cópia é O(peças) e acontece uma vez
/// por repaginação (que é debounced), não por tecla.
/// </para>
/// </remarks>
public sealed class TextBufferSnapshot
{
    private readonly string _original;
    private readonly char[] _added;
    private readonly Piece[] _pieces;

    internal TextBufferSnapshot(string original, char[] added, Piece[] pieces, int length)
    {
        _original = original;
        _added = added;
        _pieces = pieces;
        Length = length;
    }

    /// <summary>Comprimento do documento em caracteres UTF-16.</summary>
    public int Length { get; }

    /// <summary>
    /// Materializa o documento inteiro. É o que o parser consome hoje; quando o reflow
    /// incremental chegar (Fase 4), o parser passa a ler por trecho e isto vira caso de borda.
    /// </summary>
    public string GetText()
    {
        if (Length == 0)
        {
            return string.Empty;
        }

        return string.Create(Length, this, static (destination, snapshot) => snapshot.CopyTo(destination));
    }

    public void CopyTo(Span<char> destination)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(destination.Length, Length);

        var position = 0;

        foreach (var piece in _pieces)
        {
            var source = piece.Source == PieceSource.Original
                ? _original.AsSpan(piece.Start, piece.Length)
                : _added.AsSpan(piece.Start, piece.Length);

            source.CopyTo(destination[position..]);
            position += piece.Length;
        }
    }
}
