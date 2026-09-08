using AcademicEditor.Core.Input;

using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace AcademicEditor.App.Input;

/// <summary>
/// Transforma teclas em comandos, na janela inteira.
/// </summary>
/// <remarks>
/// <para>
/// Escuta na fase de <b>tunelamento</b>, não na de borbulhamento: o evento desce da janela até o
/// controle focado antes de subir de volta. É o que faz <c>Ctrl+S</c> salvar em vez de virar texto
/// — se esperasse o borbulhamento, o <c>PageSurface</c> já teria tratado a tecla e marcado
/// <c>Handled</c>.
/// </para>
/// <para>
/// Guarda os passos pendentes de um atalho de várias teclas. Uma tecla que não continua nenhuma
/// sequência limpa o acúmulo e é devolvida a quem estava digitando, em vez de sumir.
/// </para>
/// </remarks>
internal sealed class ShortcutDispatcher
{
    private readonly KeyBindingRegistry _registry;
    private readonly FocusScopeTracker _scope;
    private readonly Dictionary<CommandId, Func<Task>> _handlers = [];
    private readonly List<KeyStroke> _pending = [];

    private readonly TopLevel _topLevel;

    public ShortcutDispatcher(TopLevel topLevel, KeyBindingRegistry registry)
    {
        ArgumentNullException.ThrowIfNull(topLevel);
        ArgumentNullException.ThrowIfNull(registry);

        _topLevel = topLevel;
        _registry = registry;
        _scope = new FocusScopeTracker(topLevel);

        _topLevel.AddHandler(InputElement.KeyDownEvent, OnKeyDown, RoutingStrategies.Tunnel);
    }

    /// <summary>Um comando falhou. A janela decide como contar isso a quem está escrevendo.</summary>
    public event EventHandler<Exception>? CommandFailed;

    public void Handle(CommandId command, Func<Task> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        _handlers[command] = handler;
    }

    public void Handle(CommandId command, Action handler)
    {
        ArgumentNullException.ThrowIfNull(handler);

        _handlers[command] = () =>
        {
            handler();
            return Task.CompletedTask;
        };
    }

    public void Detach() => _topLevel.RemoveHandler(InputElement.KeyDownEvent, OnKeyDown);

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        // Segurar só o modificador não é um passo: senão Ctrl sozinho já abandonaria uma sequência
        // pendente antes de a segunda tecla chegar.
        if (e.Key is Key.LeftCtrl or Key.RightCtrl or Key.LeftShift or Key.RightShift
            or Key.LeftAlt or Key.RightAlt or Key.LWin or Key.RWin)
        {
            return;
        }

        if (KeyTranslation.ToStroke(e.Key, e.KeyModifiers) is not { } stroke)
        {
            _pending.Clear();
            return;
        }

        _pending.Add(stroke);

        var resolution = _registry.Resolve(_scope.Current, _pending);

        switch (resolution.Status)
        {
            case ChordStatus.Pending:
                // Consome a tecla sem executar nada: ela é o começo de um atalho maior, e deixá-la
                // passar escreveria um caractere que o autor não pediu.
                e.Handled = true;
                break;

            case ChordStatus.Resolved:
                _pending.Clear();
                e.Handled = Execute(resolution.Command);
                break;

            default:
                _pending.Clear();
                break;
        }
    }

    private bool Execute(CommandId command)
    {
        if (!_handlers.TryGetValue(command, out var handler))
        {
            // Atalho registrado sem quem o execute: a tecla segue para quem estava digitando, em
            // vez de sumir sem explicação.
            return false;
        }

        _ = RunAsync(handler);
        return true;
    }

    private async Task RunAsync(Func<Task> handler)
    {
        try
        {
            await handler().ConfigureAwait(true);
        }
        catch (Exception exception)
        {
            CommandFailed?.Invoke(this, exception);
        }
    }
}
