namespace AcademicEditor.Core.Text;

/// <summary>
/// O documento aberto: o buffer mais o aviso de que ele mudou. É o que a camada visual conhece —
/// ela nunca fala com a <see cref="PieceTable"/> diretamente.
/// </summary>
/// <remarks>
/// <para>
/// Fino de propósito. O que justifica a existência dele é ser o ponto único onde uma alteração
/// vira um <see cref="TextEdit"/>: sem isso, cada chamador teria que lembrar de anunciar a
/// mudança, e o dia em que um esquecesse o layout ficaria em silêncio desatualizado.
/// </para>
/// <para>
/// Pelo mesmo motivo é aqui que o fim de linha é normalizado. <b>O buffer nunca contém
/// <c>\r</c></b> — todo texto que entra passa por <see cref="LineEndings.NormalizeToLf"/>, e é
/// essa invariante que dispensa o parser, o line breaker e cada tecla de edição de saber o que
/// fazer com CRLF.
/// </para>
/// </remarks>
public sealed class EditorDocument
{
    private readonly PieceTable _buffer;

    public EditorDocument(string initialText) =>
        _buffer = new PieceTable(LineEndings.NormalizeToLf(initialText));

    public event EventHandler<TextEdit>? Changed;

    public int Length => _buffer.Length;

    public TextBufferSnapshot CreateSnapshot() => _buffer.CreateSnapshot();

    public char CharAt(int offset) => _buffer.CharAt(offset);

    /// <summary>Insere <paramref name="text"/> normalizado e devolve quantos caracteres entraram.</summary>
    /// <remarks>
    /// Devolve o comprimento porque a normalização pode encurtar o texto: colar um trecho CRLF
    /// insere menos caracteres do que a string tinha. Quem move o caret por <c>text.Length</c>
    /// depois disso o deixaria adiante do que o buffer realmente recebeu.
    /// </remarks>
    public int Insert(int offset, string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        var normalized = LineEndings.NormalizeToLf(text);

        if (normalized.Length == 0)
        {
            return 0;
        }

        _buffer.Insert(offset, normalized);
        Changed?.Invoke(this, new TextEdit(offset, RemovedLength: 0, normalized));

        return normalized.Length;
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
