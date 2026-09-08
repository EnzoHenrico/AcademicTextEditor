using System.Collections.Frozen;

using AcademicEditor.Core.Input;

using Avalonia.Input;

namespace AcademicEditor.App.Input;

/// <summary>
/// Traduz a tecla do Avalonia para o vocabulário do Core.
/// </summary>
/// <remarks>
/// O mapa é montado por <b>nome</b>, uma vez, em vez de um <c>switch</c> gigante: os dois enums
/// usam os mesmos identificadores de propósito, então acrescentar uma tecla ao
/// <see cref="KeyCode"/> já a torna traduzível, sem uma segunda edição que alguém esqueceria.
/// </remarks>
internal static class KeyTranslation
{
    private static readonly FrozenDictionary<Key, KeyCode> Keys = Enum
        .GetValues<Key>()
        .Select(key => (Key: key, Parsed: Enum.TryParse<KeyCode>(key.ToString(), out var code) ? code : KeyCode.None))
        .Where(pair => pair.Parsed != KeyCode.None)

        // Key tem apelidos: Prior e PageUp são o mesmo valor, e ToString() devolve o nome canônico
        // para os dois. Sem descartar a repetição, montar o mapa lança na primeira tecla traduzida.
        .DistinctBy(pair => pair.Key)
        .ToFrozenDictionary(pair => pair.Key, pair => pair.Parsed);

    /// <summary>A tecla como um passo de atalho, ou <c>null</c> se o Core não a conhece.</summary>
    public static KeyStroke? ToStroke(Key key, KeyModifiers modifiers) =>
        Keys.TryGetValue(key, out var code) ? new KeyStroke(code, ToModifiers(modifiers)) : null;

    private static ModifierKeys ToModifiers(KeyModifiers modifiers)
    {
        var result = ModifierKeys.None;

        if (modifiers.HasFlag(KeyModifiers.Control)) result |= ModifierKeys.Control;
        if (modifiers.HasFlag(KeyModifiers.Shift)) result |= ModifierKeys.Shift;
        if (modifiers.HasFlag(KeyModifiers.Alt)) result |= ModifierKeys.Alt;
        if (modifiers.HasFlag(KeyModifiers.Meta)) result |= ModifierKeys.Meta;

        return result;
    }
}
