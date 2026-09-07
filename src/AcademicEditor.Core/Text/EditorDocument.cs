namespace AcademicEditor.Core.Text;

/// <summary>
/// O documento aberto: o buffer mais o aviso de que ele mudou. É o que a camada visual conhece —
/// ela nunca fala com a <see cref="PieceTable"/> diretamente.
/// </summary>
/// <remarks>
/// Fino de propósito. O que justifica a existência dele é ser o ponto único onde uma alteração
/// vira um <see cref="TextEdit"/>: sem isso, cada chamador teria que lembrar de anunciar a
/// mudança, e o dia em que um esquecesse o layout ficaria em silêncio desatualizado.
/// </remarks>
public sealed class EditorDocument
{
    private readonly PieceTable _buffer;

    public EditorDocument(string initialText) => _buffer = new PieceTable(initialText);

    public event EventHandler<TextEdit>? Changed;

    public int Length => _buffer.Length;

    public TextBufferSnapshot CreateSnapshot() => _buffer.CreateSnapshot();

    public char CharAt(int offset) => _buffer.CharAt(offset);

    public void Insert(int offset, string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        if (text.Length == 0)
        {
            return;
        }

        _buffer.Insert(offset, text);
        Changed?.Invoke(this, new TextEdit(offset, RemovedLength: 0, text));
    }

    public void Delete(int offset, int length)
    {
        if (length <= 0)
        {
            return;
        }

        _buffer.Delete(offset, length);
        Changed?.Invoke(this, new TextEdit(offset, length, string.Empty));
    }
}
