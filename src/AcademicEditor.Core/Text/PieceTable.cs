namespace AcademicEditor.Core.Text;

/// <summary>
/// O buffer de texto do editor. Dois arrays imutáveis-na-prática — o original do arquivo e o que
/// foi digitado — mais uma lista de peças que diz em que ordem lê-los.
/// </summary>
/// <remarks>
/// <para>
/// A escolha existe por causa da digitação: inserir um caractere no meio de uma tese não move
/// nada de lugar, apenas anexa uma letra ao fim do buffer de adição e mexe na lista de peças.
/// Um <c>StringBuilder</c> copiaria o resto do documento a cada tecla; um array de linhas
/// copiaria a linha inteira.
/// </para>
/// <para>
/// A lista é um <c>List&lt;Piece&gt;</c> com busca linear. Uma árvore balanceada tornaria a busca
/// O(log n), mas só paga em documentos com muitas peças, e a digitação — o caso quente — nem
/// percorre a lista: cai no caminho rápido abaixo. Está no estacionamento de ideias do roadmap,
/// para quando o profiling pedir.
/// </para>
/// </remarks>
public sealed class PieceTable
{
    private const int InitialAddedCapacity = 1024;

    private readonly string _original;
    private readonly List<Piece> _pieces = [];

    private char[] _added;
    private int _addedLength;

    public PieceTable(string original)
    {
        ArgumentNullException.ThrowIfNull(original);

        _original = original;
        _added = new char[InitialAddedCapacity];

        if (original.Length > 0)
        {
            _pieces.Add(new Piece(PieceSource.Original, 0, original.Length));
        }

        Length = original.Length;
    }

    /// <summary>Comprimento do documento em caracteres UTF-16.</summary>
    public int Length { get; private set; }

    /// <summary>
    /// Quantas peças a lista tem. Exposto para o teste que garante o caminho rápido da digitação:
    /// digitar N caracteres seguidos no fim não pode fazer a lista crescer N vezes.
    /// </summary>
    public int PieceCount => _pieces.Count;

    public void Insert(int offset, string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentOutOfRangeException.ThrowIfNegative(offset);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(offset, Length);

        if (text.Length == 0)
        {
            return;
        }

        var addedStart = AppendToAddBuffer(text);

        // Caminho rápido da digitação. Escrever no fim, logo depois de um trecho que também veio
        // do buffer de adição e termina exatamente onde o novo começa, é só esticar a última
        // peça. Digitar um parágrafo inteiro deixa a lista de peças do mesmo tamanho.
        if (offset == Length && _pieces.Count > 0)
        {
            var last = _pieces[^1];

            if (last.Source == PieceSource.Added && last.Start + last.Length == addedStart)
            {
                _pieces[^1] = last with { Length = last.Length + text.Length };
                Length += text.Length;
                return;
            }
        }

        var inserted = new Piece(PieceSource.Added, addedStart, text.Length);
        var (index, offsetInPiece) = FindPiece(offset);

        if (offsetInPiece == 0)
        {
            _pieces.Insert(index, inserted);
        }
        else
        {
            // No meio de uma peça: ela vira prefixo + texto novo + sufixo.
            var target = _pieces[index];

            _pieces[index] = target with { Length = offsetInPiece };
            _pieces.InsertRange(index + 1, [
                inserted,
                target with { Start = target.Start + offsetInPiece, Length = target.Length - offsetInPiece },
            ]);
        }

        Length += text.Length;
    }

    public void Delete(int offset, int length)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(offset);
        ArgumentOutOfRangeException.ThrowIfNegative(length);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(offset + length, Length);

        if (length == 0)
        {
            return;
        }

        // Apagar nunca toca nos buffers: só encolhe, parte ou remove peças. O texto apagado
        // continua lá, intacto, que é o que tornará o undo um delta da lista e não uma cópia.
        var (index, offsetInPiece) = FindPiece(offset);
        var remaining = length;

        while (remaining > 0)
        {
            var piece = _pieces[index];
            var available = piece.Length - offsetInPiece;
            var take = Math.Min(available, remaining);

            if (offsetInPiece == 0 && take == piece.Length)
            {
                _pieces.RemoveAt(index);
            }
            else if (offsetInPiece == 0)
            {
                _pieces[index] = piece with { Start = piece.Start + take, Length = piece.Length - take };
                index++;
            }
            else if (take == available)
            {
                _pieces[index] = piece with { Length = offsetInPiece };
                index++;
            }
            else
            {
                // Buraco no meio de uma peça: ela vira duas.
                _pieces[index] = piece with { Length = offsetInPiece };
                _pieces.Insert(index + 1, piece with
                {
                    Start = piece.Start + offsetInPiece + take,
                    Length = available - take,
                });
                index += 2;
            }

            remaining -= take;
            offsetInPiece = 0;
        }

        Length -= length;
    }

    /// <summary>Caractere numa posição. O caret usa isto para não apagar meio par substituto.</summary>
    public char CharAt(int offset)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(offset);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(offset, Length);

        var (index, offsetInPiece) = FindPiece(offset);
        var piece = _pieces[index];

        return piece.Source == PieceSource.Original
            ? _original[piece.Start + offsetInPiece]
            : _added[piece.Start + offsetInPiece];
    }

    /// <summary>Congela o estado atual para leitura fora da UI thread.</summary>
    public TextBufferSnapshot CreateSnapshot() =>
        new(_original, _added, [.. _pieces], Length);

    /// <summary>
    /// Localiza a peça que contém <paramref name="offset"/>. Para o fim do documento devolve um
    /// índice logo após a última peça, que é onde uma inserção deve entrar.
    /// </summary>
    private (int Index, int OffsetInPiece) FindPiece(int offset)
    {
        var remaining = offset;

        for (var i = 0; i < _pieces.Count; i++)
        {
            if (remaining < _pieces[i].Length)
            {
                return (i, remaining);
            }

            remaining -= _pieces[i].Length;
        }

        return (_pieces.Count, 0);
    }

    private int AppendToAddBuffer(string text)
    {
        var start = _addedLength;

        if (_addedLength + text.Length > _added.Length)
        {
            // Realoca em vez de escrever no array antigo. Snapshots já entregues continuam
            // segurando o array anterior, cujo conteúdo permanece exatamente como estava — é o
            // que dispensa lock entre a digitação e o layout em background.
            var grown = new char[Math.Max(_added.Length * 2, _addedLength + text.Length)];
            Array.Copy(_added, grown, _addedLength);
            _added = grown;
        }

        text.CopyTo(_added.AsSpan(_addedLength));
        _addedLength += text.Length;

        return start;
    }
}
