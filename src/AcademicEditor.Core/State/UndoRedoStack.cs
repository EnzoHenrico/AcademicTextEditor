using AcademicEditor.Core.Text;

namespace AcademicEditor.Core.State;

/// <summary>Que tipo de edição foi, para decidir o que se agrupa com o quê no undo.</summary>
public enum EditKind
{
    /// <summary>Nada se agrupa com isto: Enter, colagem, uma edição que veio de outro lugar.</summary>
    Other,

    /// <summary>Caractere digitado.</summary>
    Typing,

    /// <summary>Caractere apagado.</summary>
    Deleting,
}

/// <summary>
/// O histórico de edições do documento aberto.
/// </summary>
/// <remarks>
/// <para>
/// Guarda <see cref="PieceEdit"/>, não texto: desfazer é recolocar peças na lista, e o custo não
/// depende do tamanho do que foi editado.
/// </para>
/// <para>
/// <b>Um grupo, não uma edição.</b> Cada tecla digitada é uma edição no buffer, mas desfazer letra
/// por letra é insuportável. Teclas seguidas que continuam de onde a anterior parou entram no
/// mesmo grupo, e o grupo inteiro se desfaz de uma vez. Mover o caret, salvar ou trocar de tipo
/// de edição fecha o grupo — é o comportamento de qualquer editor, e a regra é essa mesma.
/// </para>
/// </remarks>
public sealed class UndoRedoStack
{
    private readonly EditorDocument _document;
    private readonly List<Group> _done = [];
    private readonly List<Group> _undone = [];

    private Group? _open;

    public UndoRedoStack(EditorDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        _document = document;
    }

    public bool CanUndo => _done.Count > 0;

    public bool CanRedo => _undone.Count > 0;

    /// <summary>
    /// Registra uma edição já aplicada ao documento.
    /// </summary>
    /// <param name="caretBefore">Onde o caret estava — é para onde o undo o devolve.</param>
    /// <param name="caretAfter">Onde ficou — é para onde o redo o devolve.</param>
    public void Record(PieceEdit edit, EditKind kind, int caretBefore, int caretAfter)
    {
        ArgumentNullException.ThrowIfNull(edit);

        if (edit.IsEmpty)
        {
            return;
        }

        // Editar depois de desfazer abandona o que havia sido desfeito: a história que se pretendia
        // refazer descreve peças que não existem mais nesta lista.
        _undone.Clear();

        if (_open is not null && _open.Kind == kind && kind != EditKind.Other && _open.CaretAfter == caretBefore)
        {
            _open.Edits.Add(edit);
            _open.CaretAfter = caretAfter;
            return;
        }

        _open = new Group(kind, caretBefore, caretAfter);
        _open.Edits.Add(edit);
        _done.Add(_open);
    }

    /// <summary>Fecha o grupo corrente. Chame ao mover o caret, ao salvar, ao trocar de contexto.</summary>
    public void Break() => _open = null;

    /// <summary>Desfaz um grupo e devolve para onde o caret vai. <c>null</c> quando não há o que desfazer.</summary>
    public int? Undo()
    {
        if (_done.Count == 0)
        {
            return null;
        }

        var group = _done[^1];
        _done.RemoveAt(_done.Count - 1);
        _undone.Add(group);
        Break();

        // Ao contrário: cada delta descreve a lista como ela estava quando aquela edição entrou.
        for (var i = group.Edits.Count - 1; i >= 0; i--)
        {
            _document.Revert(group.Edits[i]);
        }

        return group.CaretBefore;
    }

    /// <summary>Refaz o último grupo desfeito e devolve para onde o caret vai.</summary>
    public int? Redo()
    {
        if (_undone.Count == 0)
        {
            return null;
        }

        var group = _undone[^1];
        _undone.RemoveAt(_undone.Count - 1);
        _done.Add(group);
        Break();

        foreach (var edit in group.Edits)
        {
            _document.Reapply(edit);
        }

        return group.CaretAfter;
    }

    private sealed class Group(EditKind kind, int caretBefore, int caretAfter)
    {
        public List<PieceEdit> Edits { get; } = [];

        public EditKind Kind { get; } = kind;

        public int CaretBefore { get; } = caretBefore;

        public int CaretAfter { get; set; } = caretAfter;
    }
}
