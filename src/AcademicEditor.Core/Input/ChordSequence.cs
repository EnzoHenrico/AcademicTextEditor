namespace AcademicEditor.Core.Input;

/// <summary>
/// A sequência de teclas que dispara um comando. Um passo (<c>Ctrl+S</c>) ou vários
/// (<c>Ctrl+K, Ctrl+S</c>).
/// </summary>
/// <remarks>
/// Nasce com N passos mesmo com todo binding do MVP tendo um só. O motivo é o despachante: um
/// atalho de dois passos precisa de estado entre teclas — a primeira não executa nada, apenas
/// deixa a sequência pendente. Adicionar isso depois seria reescrever o laço de teclado, não
/// acrescentar um caso.
/// </remarks>
public sealed class ChordSequence : IEquatable<ChordSequence>
{
    private readonly KeyStroke[] _strokes;

    public ChordSequence(params KeyStroke[] strokes)
    {
        ArgumentNullException.ThrowIfNull(strokes);

        if (strokes.Length == 0)
        {
            throw new ArgumentException("Um atalho tem ao menos um passo.", nameof(strokes));
        }

        _strokes = [.. strokes];
    }

    public int Count => _strokes.Length;

    public KeyStroke this[int index] => _strokes[index];

    /// <summary>Este atalho começa por <paramref name="prefix"/>? É o que mantém a sequência pendente.</summary>
    public bool StartsWith(IReadOnlyList<KeyStroke> prefix)
    {
        ArgumentNullException.ThrowIfNull(prefix);

        if (prefix.Count > _strokes.Length)
        {
            return false;
        }

        for (var i = 0; i < prefix.Count; i++)
        {
            if (_strokes[i] != prefix[i])
            {
                return false;
            }
        }

        return true;
    }

    public bool Matches(IReadOnlyList<KeyStroke> strokes) =>
        strokes is not null && strokes.Count == _strokes.Length && StartsWith(strokes);

    public bool Equals(ChordSequence? other) => other is not null && Matches(other._strokes);

    public override bool Equals(object? obj) => Equals(obj as ChordSequence);

    public override int GetHashCode()
    {
        var hash = default(HashCode);

        foreach (var stroke in _strokes)
        {
            hash.Add(stroke);
        }

        return hash.ToHashCode();
    }

    public override string ToString() => string.Join(", ", _strokes.Select(Describe));

    private static string Describe(KeyStroke stroke)
    {
        var parts = new List<string>(4);

        if (stroke.Modifiers.HasFlag(ModifierKeys.Control)) parts.Add("Ctrl");
        if (stroke.Modifiers.HasFlag(ModifierKeys.Shift)) parts.Add("Shift");
        if (stroke.Modifiers.HasFlag(ModifierKeys.Alt)) parts.Add("Alt");
        if (stroke.Modifiers.HasFlag(ModifierKeys.Meta)) parts.Add("Meta");

        parts.Add(stroke.Key.ToString());

        return string.Join("+", parts);
    }
}
