namespace AcademicEditor.Core.Input;

/// <summary>
/// O nome de um comando. O atalho aponta para isto, não para o método que executa.
/// </summary>
/// <remarks>
/// A indireção é o que permite reconfigurar teclas sem tocar em quem faz o trabalho, e é também o
/// que a paleta de comandos e o menu vão consumir quando existirem: os três acabam pedindo o mesmo
/// <c>editor.save</c>.
/// </remarks>
public readonly record struct CommandId(string Value)
{
    public override string ToString() => Value;
}

/// <summary>Os comandos que o editor conhece hoje.</summary>
public static class EditorCommands
{
    public static readonly CommandId Save = new("editor.save");
    public static readonly CommandId SaveAs = new("editor.saveAs");
    public static readonly CommandId Open = new("editor.open");
    public static readonly CommandId ExportPdf = new("editor.exportPdf");
    public static readonly CommandId Undo = new("editor.undo");
    public static readonly CommandId Redo = new("editor.redo");
    public static readonly CommandId Copy = new("editor.copy");
    public static readonly CommandId Cut = new("editor.cut");
    public static readonly CommandId Paste = new("editor.paste");
    public static readonly CommandId SelectAll = new("editor.selectAll");
    public static readonly CommandId CycleAlignment = new("editor.cycleAlignment");
}

/// <summary>Onde um atalho vale.</summary>
/// <remarks>
/// <see cref="Global"/> vale em qualquer lugar da janela; <see cref="Editor"/> só com o texto em
/// foco. A distinção existe para que uma tecla nua — um dia, <c>F2</c> renomeando algo numa árvore
/// lateral — não roube o teclado de quem está escrevendo.
/// </remarks>
public enum ShortcutScope
{
    Global,
    Editor,
}
