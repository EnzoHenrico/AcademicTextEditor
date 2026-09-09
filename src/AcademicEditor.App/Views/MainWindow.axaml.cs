using AcademicEditor.App.Input;
using AcademicEditor.App.Rendering;
using AcademicEditor.App.ViewModels;

using AcademicEditor.Core.Input;
using AcademicEditor.Core.Layout;

using Avalonia.Controls;
using Avalonia.Platform.Storage;

namespace AcademicEditor.App.Views;

public partial class MainWindow : Window
{
    private static readonly FilePickerFileType MarkdownFiles = new("Markdown")
    {
        Patterns = ["*.md", "*.markdown", "*.txt"],
    };

    private readonly EditorViewModel _viewModel;
    private readonly ShortcutDispatcher _shortcuts;

    public MainWindow()
    {
        InitializeComponent();

        // O conteúdo inicial continua fixo porque não há documento aberto ao subir. Daqui em
        // diante Ctrl+O troca isso.
        //
        // De propósito a variante CRLF, que é o caso difícil: o EditorDocument normaliza na
        // entrada, então trocar por UniqueFeaturesLf tem de dar exatamente a mesma tela. Se um dia
        // não der, a regressão aparece já na primeira execução em vez de esperar um teste.
        // O medidor real por baixo, o cache por cima. Medido na Fatia 6: sem ele, 97,9% de uma
        // repaginação de 300 páginas é medição de texto, e 96% dessas medições são repetição.
        _viewModel = new EditorViewModel(
            new CachingTextMeasurer(new AvaloniaTextMeasurer()),
            PageSettings.A4,
            Assets.Samples.Text.UniqueFeaturesCrLf);

        Surface.ViewModel = _viewModel;
        _viewModel.DocumentStateChanged += (_, _) => UpdateTitle();

        _shortcuts = new ShortcutDispatcher(this, BuildBindings());
        _shortcuts.Handle(EditorCommands.Undo, _viewModel.Undo);
        _shortcuts.Handle(EditorCommands.Redo, _viewModel.Redo);
        _shortcuts.Handle(EditorCommands.Save, SaveAsync);
        _shortcuts.Handle(EditorCommands.SaveAs, SaveAsAsync);
        _shortcuts.Handle(EditorCommands.Open, OpenAsync);

        // Um save que falha é a falha que mais importa neste programa. Sem isto ela sumiria numa
        // Task descartada e o autor acharia que gravou.
        _shortcuts.CommandFailed += (_, exception) => _viewModel.Report($"erro: {exception.Message}");

        UpdateTitle();
    }

    private static KeyBindingRegistry BuildBindings()
    {
        var registry = new KeyBindingRegistry();

        registry.Bind(ShortcutScope.Global, Chord(KeyCode.S, ModifierKeys.Control), EditorCommands.Save);
        registry.Bind(
            ShortcutScope.Global,
            Chord(KeyCode.S, ModifierKeys.Control | ModifierKeys.Shift),
            EditorCommands.SaveAs);
        registry.Bind(ShortcutScope.Global, Chord(KeyCode.O, ModifierKeys.Control), EditorCommands.Open);
        registry.Bind(ShortcutScope.Editor, Chord(KeyCode.Z, ModifierKeys.Control), EditorCommands.Undo);
        registry.Bind(ShortcutScope.Editor, Chord(KeyCode.Y, ModifierKeys.Control), EditorCommands.Redo);

        // Ctrl+Shift+Z é o refazer de fato usado no Linux e no macOS; Ctrl+Y é o do Windows. Os
        // dois apontam para o mesmo comando, que é a razão de o atalho apontar para um CommandId.
        registry.Bind(
            ShortcutScope.Editor,
            Chord(KeyCode.Z, ModifierKeys.Control | ModifierKeys.Shift),
            EditorCommands.Redo);

        return registry;
    }

    private static ChordSequence Chord(KeyCode key, ModifierKeys modifiers) =>
        new(new KeyStroke(key, modifiers));

    private async Task SaveAsync()
    {
        if (_viewModel.FilePath is null)
        {
            await SaveAsAsync().ConfigureAwait(true);
            return;
        }

        await _viewModel.SaveAsync().ConfigureAwait(true);
        UpdateTitle();
    }

    private async Task SaveAsAsync()
    {
        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Salvar documento",
            DefaultExtension = "md",
            SuggestedFileName = Path.GetFileName(_viewModel.FilePath) ?? "documento.md",
            FileTypeChoices = [MarkdownFiles],
        }).ConfigureAwait(true);

        if (file?.TryGetLocalPath() is not { } path)
        {
            return;
        }

        await _viewModel.SaveAsAsync(path).ConfigureAwait(true);
        UpdateTitle();
    }

    private async Task OpenAsync()
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Abrir documento",
            AllowMultiple = false,
            FileTypeFilter = [MarkdownFiles],
        }).ConfigureAwait(true);

        if (files.Count == 0 || files[0].TryGetLocalPath() is not { } path)
        {
            return;
        }

        await _viewModel.OpenAsync(path).ConfigureAwait(true);
        UpdateTitle();
        Surface.Focus();
    }

    private void UpdateTitle()
    {
        var name = _viewModel.FilePath is { } path ? Path.GetFileName(path) : "documento sem título";
        var modified = _viewModel.IsModified ? " •" : string.Empty;
        var status = _viewModel.StatusMessage.Length > 0 ? $"  —  {_viewModel.StatusMessage}" : string.Empty;

        Title = $"{name}{modified}  —  AcademicEditor{status}";
    }
}
