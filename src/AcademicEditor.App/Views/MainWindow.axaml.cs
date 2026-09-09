using System.Globalization;

using AcademicEditor.App.Input;
using AcademicEditor.App.Rendering;
using AcademicEditor.App.ViewModels;

using AcademicEditor.Core.Input;
using AcademicEditor.Core.Layout;

using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Platform.Storage;

namespace AcademicEditor.App.Views;

public partial class MainWindow : Window
{
    private static readonly FilePickerFileType MarkdownFiles = new("Markdown")
    {
        Patterns = ["*.md", "*.markdown", "*.txt"],
    };

    // O programa é todo em português; formatar "1,234 palavras" no meio dele estaria errado, e
    // depender da cultura da máquina faria o mesmo texto sair diferente em cada uma delas.
    private static readonly CultureInfo Numbers = CultureInfo.GetCultureInfo("pt-BR");

    // A4 com uma faixa reservada no alto para o cabeçalho. 24pt ≈ 8,5mm: cabe a linha do número da
    // página — corpo 12 mede cerca de 14pt de altura — com folga até a primeira linha do texto.
    //
    // A reserva mora aqui, junto do que vai ser escrito nela, porque as duas decisões são uma só:
    // sem reserva o PageBands não desenha nada, e reserva sem texto é papel em branco. A geometria
    // continua tendo um dono só, que é o PageSettings.
    private static readonly PageSettings Page = PageSettings.A4 with { HeaderReservedHeightPt = 24.0 };

    private readonly EditorViewModel _viewModel;
    private readonly ShortcutDispatcher _shortcuts;

    public MainWindow() : this(null)
    {
    }

    /// <param name="initialFilePath">
    /// Arquivo a abrir ao subir, vindo da linha de comando. <c>null</c> abre no documento de
    /// exemplo.
    /// </param>
    public MainWindow(string? initialFilePath)
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
            Page,
            Assets.Samples.Text.UniqueFeaturesCrLf,
            typography: TypographyPreset.Abnt,
            bands: HeaderFooterSettings.Abnt);

        Surface.ViewModel = _viewModel;
        _viewModel.DocumentStateChanged += (_, _) => UpdateDocumentState();

        _shortcuts = new ShortcutDispatcher(this, BuildBindings());
        _shortcuts.Handle(EditorCommands.Undo, _viewModel.Undo);
        _shortcuts.Handle(EditorCommands.Redo, _viewModel.Redo);
        _shortcuts.Handle(EditorCommands.Save, SaveAsync);
        _shortcuts.Handle(EditorCommands.SaveAs, SaveAsAsync);
        _shortcuts.Handle(EditorCommands.Open, OpenAsync);
        _shortcuts.Handle(EditorCommands.Copy, CopyAsync);
        _shortcuts.Handle(EditorCommands.Cut, CutAsync);
        _shortcuts.Handle(EditorCommands.Paste, PasteAsync);
        _shortcuts.Handle(EditorCommands.SelectAll, _viewModel.SelectAll);

        // Um save que falha é a falha que mais importa neste programa. Sem isto ela sumiria numa
        // Task descartada e o autor acharia que gravou.
        _shortcuts.CommandFailed += (_, exception) => _viewModel.Report($"erro: {exception.Message}");

        UpdateDocumentState();

        if (initialFilePath is not null)
        {
            _ = OpenFileAsync(initialFilePath);
        }
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

        // A área de transferência. O despachante escuta no tunelamento, então nenhum destes chega
        // ao PageSurface como texto — é a mesma razão pela qual Ctrl+S salva em vez de escrever "s".
        registry.Bind(ShortcutScope.Editor, Chord(KeyCode.C, ModifierKeys.Control), EditorCommands.Copy);
        registry.Bind(ShortcutScope.Editor, Chord(KeyCode.X, ModifierKeys.Control), EditorCommands.Cut);
        registry.Bind(ShortcutScope.Editor, Chord(KeyCode.V, ModifierKeys.Control), EditorCommands.Paste);
        registry.Bind(
            ShortcutScope.Editor,
            Chord(KeyCode.A, ModifierKeys.Control),
            EditorCommands.SelectAll);

        return registry;
    }

    private static ChordSequence Chord(KeyCode key, ModifierKeys modifiers) =>
        new(new KeyStroke(key, modifiers));

    // A área de transferência fica aqui, e não no ViewModel, exatamente como o IStorageProvider
    // dos diálogos de arquivo: é serviço da janela. Mantém o EditorViewModel sem um único using do
    // Avalonia e dispensa inventar uma interface para envolver a que o toolkit já tem.
    //
    // O Avalonia 12 reescreveu esta API: IClipboard.GetTextAsync/SetTextAsync não existem mais, e
    // texto passa por métodos de extensão sobre DataFormat.Text.
    private async Task CopyAsync()
    {
        if (Clipboard is { } clipboard && _viewModel.SelectedText is { Length: > 0 } text)
        {
            await clipboard.SetTextAsync(text).ConfigureAwait(true);
        }
    }

    /// <summary>Recortar é copiar e apagar — nesta ordem.</summary>
    /// <remarks>
    /// Apagar antes de a área de transferência confirmar tiraria o texto do documento sem ter onde
    /// buscá-lo de volta, e um Ctrl+V logo depois traria outra coisa.
    /// </remarks>
    private async Task CutAsync()
    {
        await CopyAsync().ConfigureAwait(true);
        _viewModel.DeleteSelection();
    }

    private async Task PasteAsync()
    {
        if (Clipboard is not { } clipboard)
        {
            return;
        }

        // Colar não precisa de caso especial para várias linhas: o EditorDocument normaliza o fim
        // de linha na entrada e devolve quantos caracteres de fato entraram, e o LayoutReuse já
        // recusa um trecho com '\n' e cai na paginação completa, que é código provado.
        if (await clipboard.TryGetTextAsync().ConfigureAwait(true) is { Length: > 0 } text)
        {
            _viewModel.InsertText(text);
        }
    }

    private async Task SaveAsync()
    {
        if (_viewModel.FilePath is null)
        {
            await SaveAsAsync().ConfigureAwait(true);
            return;
        }

        await _viewModel.SaveAsync().ConfigureAwait(true);
        UpdateDocumentState();
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
        UpdateDocumentState();
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

        await OpenFileAsync(path).ConfigureAwait(true);
    }

    /// <summary>Abre um caminho já escolhido — pelo seletor ou pela linha de comando.</summary>
    /// <remarks>
    /// Trata a falha aqui, e não deixa subir: a abertura por linha de comando não passa pelo
    /// <c>ShortcutDispatcher</c>, então não há quem a reporte. Um arquivo que não existe tem de
    /// virar mensagem no título, não uma exceção em Task descartada.
    /// </remarks>
    private async Task OpenFileAsync(string path)
    {
        try
        {
            await _viewModel.OpenAsync(path).ConfigureAwait(true);
            UpdateDocumentState();
            Surface.Focus();
        }
        catch (Exception exception)
        {
            _viewModel.Report($"erro ao abrir {Path.GetFileName(path)}: {exception.Message}");
            UpdateDocumentState();
        }
    }

    /// <summary>Reflete arquivo, "não salvo" e a última mensagem no título e na barra de status.</summary>
    /// <remarks>
    /// <b>A mensagem saiu do título.</b> O título é a identidade do documento — o que o gerenciador
    /// de janelas mostra na barra de tarefas —, e uma mensagem ali some no instante em que a
    /// seguinte chega, ou fica pendurada depois de deixar de valer. Um save que falha é a falha que
    /// mais importa neste programa, e o lugar dela é a barra.
    /// </remarks>
    /// <summary>
    /// As contagens da barra: o documento inteiro, ou o trecho selecionado quando há um.
    /// </summary>
    /// <remarks>
    /// Trocar para o trecho, em vez de mostrar os dois números, é o que todo editor faz — e é o que
    /// responde à pergunta que se faz com texto selecionado, que é "quanto tem <i>isto</i>".
    /// </remarks>
    private string FormatCounts()
    {
        var counts = _viewModel.SelectionStatistics ?? _viewModel.Statistics;
        var prefix = _viewModel.SelectionStatistics is null ? string.Empty : "seleção: ";

        return prefix
            + Plural(counts.Words, "palavra", "palavras")
            + " · "
            + Plural(counts.Characters, "caractere", "caracteres");
    }

    private static string Plural(int count, string singular, string plural) =>
        $"{count.ToString("N0", Numbers)} {(count == 1 ? singular : plural)}";

    private void UpdateDocumentState()
    {
        var name = _viewModel.FilePath is { } path ? Path.GetFileName(path) : "documento sem título";
        var modified = _viewModel.IsModified ? " •" : string.Empty;

        Title = $"{name}{modified}  —  AcademicEditor";

        StatusText.Text = _viewModel.StatusMessage;
        StatusCounts.Text = FormatCounts();

        // O caminho inteiro, e não só o nome: o título já dá o nome, e o que falta saber quando o
        // mesmo nome existe em duas pastas é de qual delas este veio.
        StatusPath.Text = _viewModel.FilePath ?? "não salvo em disco";
    }
}
