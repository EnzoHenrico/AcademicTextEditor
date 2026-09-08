namespace AcademicEditor.Core.Input;

/// <summary>O que o teclado produziu até agora.</summary>
public enum ChordStatus
{
    /// <summary>Nenhum atalho começa assim. O despachante devolve as teclas a quem estava digitando.</summary>
    None,

    /// <summary>Prefixo de um atalho maior: espera o próximo passo, sem executar nada.</summary>
    Pending,

    /// <summary>Sequência completa.</summary>
    Resolved,
}

/// <param name="Status">O que fazer com as teclas acumuladas.</param>
/// <param name="Command">Preenchido só quando <paramref name="Status"/> é <see cref="ChordStatus.Resolved"/>.</param>
public readonly record struct ChordResolution(ChordStatus Status, CommandId Command)
{
    public static readonly ChordResolution None = new(ChordStatus.None, default);

    public static readonly ChordResolution Pending = new(ChordStatus.Pending, default);
}

/// <summary>
/// De que teclas sai que comando. Sem estado de teclado — quem acumula os passos é o despachante.
/// </summary>
/// <remarks>
/// Um escopo <see cref="ShortcutScope.Editor"/> enxerga também os atalhos globais, e o mais
/// específico ganha: é o que permite um dia um painel redefinir <c>Ctrl+F</c> sem que se perca o
/// <c>Ctrl+S</c> da janela inteira.
/// </remarks>
public sealed class KeyBindingRegistry
{
    private readonly List<Binding> _bindings = [];

    public void Bind(ShortcutScope scope, ChordSequence chord, CommandId command)
    {
        ArgumentNullException.ThrowIfNull(chord);

        // Registrar de novo substitui: reconfigurar uma tecla não deve depender de o antigo
        // binding ter sido removido antes, senão qual dos dois ganha vira ordem de carregamento.
        _bindings.RemoveAll(binding => binding.Scope == scope && binding.Chord.Equals(chord));
        _bindings.Add(new Binding(scope, chord, command));
    }

    public ChordResolution Resolve(ShortcutScope scope, IReadOnlyList<KeyStroke> pending)
    {
        ArgumentNullException.ThrowIfNull(pending);

        if (pending.Count == 0)
        {
            return ChordResolution.None;
        }

        var anyPrefix = false;

        // Duas passadas por escopo, do mais específico para o global: um atalho completo no escopo
        // do editor ganha de um prefixo global, senão digitar num campo de texto ficaria refém de
        // sequências registradas na janela.
        foreach (var candidate in Scopes(scope))
        {
            foreach (var binding in _bindings)
            {
                if (binding.Scope != candidate || !binding.Chord.StartsWith(pending))
                {
                    continue;
                }

                if (binding.Chord.Count == pending.Count)
                {
                    return new ChordResolution(ChordStatus.Resolved, binding.Command);
                }

                anyPrefix = true;
            }
        }

        return anyPrefix ? ChordResolution.Pending : ChordResolution.None;
    }

    private static IEnumerable<ShortcutScope> Scopes(ShortcutScope scope) =>
        scope == ShortcutScope.Global ? [ShortcutScope.Global] : [scope, ShortcutScope.Global];

    private sealed record Binding(ShortcutScope Scope, ChordSequence Chord, CommandId Command);
}
