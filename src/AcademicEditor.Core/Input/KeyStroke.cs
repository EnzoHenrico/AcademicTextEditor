namespace AcademicEditor.Core.Input;

/// <summary>
/// Teclas que um atalho pode usar, sem depender de nenhuma biblioteca de UI.
/// </summary>
/// <remarks>
/// Existe porque o Core não referencia Avalonia. Os nomes são deliberadamente <b>iguais</b> aos do
/// <c>Avalonia.Input.Key</c>: a tradução na camada visual vira um mapa por nome, construído uma
/// vez, em vez de um <c>switch</c> de duzentas linhas que alguém teria de manter em dia.
/// </remarks>
public enum KeyCode
{
    None = 0,

    A, B, C, D, E, F, G, H, I, J, K, L, M,
    N, O, P, Q, R, S, T, U, V, W, X, Y, Z,

    D0, D1, D2, D3, D4, D5, D6, D7, D8, D9,

    F1, F2, F3, F4, F5, F6, F7, F8, F9, F10, F11, F12,

    Escape, Tab, Space, Enter, Back, Delete, Insert,
    Home, End, PageUp, PageDown,
    Left, Right, Up, Down,

    OemComma, OemPeriod, OemPlus, OemMinus, OemQuestion, OemOpenBrackets, OemCloseBrackets,
}

/// <summary>Modificadores segurados junto com a tecla.</summary>
/// <remarks>
/// <c>Meta</c> é Command no macOS e a tecla Windows no resto. Fica aqui para que um preset por
/// plataforma seja questão de registrar outro binding, não de mexer no despachante.
/// </remarks>
[Flags]
public enum ModifierKeys
{
    None = 0,
    Control = 1,
    Shift = 2,
    Alt = 4,
    Meta = 8,
}

/// <summary>Uma tecla com seus modificadores. Um passo de um atalho.</summary>
public readonly record struct KeyStroke(KeyCode Key, ModifierKeys Modifiers = ModifierKeys.None);
