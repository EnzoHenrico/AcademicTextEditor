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

    /// <summary>Insere <paramref name="text"/> normalizado e devolve o delta estrutural.</summary>
    /// <remarks>
    /// O <see cref="PieceEdit.LengthDelta"/> é quantos caracteres realmente entraram, que não é
    /// <c>text.Length</c>: a normalização encurta um trecho CRLF colado. Quem mover o caret pelo
    /// comprimento da string o deixaria adiante do que o buffer recebeu.
    /// </remarks>
    public PieceEdit Insert(int offset, string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        var normalized = LineEndings.NormalizeToLf(text);

        if (normalized.Length == 0)
        {
            return PieceEdit.Empty;
        }

        var edit = _buffer.Insert(offset, normalized);
        Changed?.Invoke(this, new TextEdit(offset, RemovedLength: 0, normalized));

        return edit;
    }

    public PieceEdit Delete(int offset, int length)
    {
        if (length <= 0)
        {
            return PieceEdit.Empty;
        }

        var edit = _buffer.Delete(offset, length);
        Changed?.Invoke(this, new TextEdit(offset, length, string.Empty));

        return edit;
    }

    /// <summary>Desfaz uma edição.</summary>
    /// <remarks>
    /// <b>Não dispara <see cref="Changed"/>.</b> O evento carrega um <see cref="TextEdit"/> com o
    /// texto que entrou, e montá-lo aqui exigiria materializar o trecho restaurado — exatamente a
    /// cópia que o delta estrutural existe para evitar. Hoje ninguém perde nada com isso: quem
    /// desenha repagina a partir do snapshot inteiro. Quando o reflow incremental da Fase 4
    /// chegar, ele vai precisar do trecho, e é aí que a decisão se paga ou se reabre.
    /// </remarks>
    public void Revert(PieceEdit edit) => _buffer.Revert(edit);

    /// <summary>Refaz uma edição desfeita. Vale o mesmo aviso do <see cref="Revert"/>.</summary>
    public void Reapply(PieceEdit edit) => _buffer.Reapply(edit);
}
