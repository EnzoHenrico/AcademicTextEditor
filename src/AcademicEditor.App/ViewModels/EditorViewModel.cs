using AcademicEditor.Core.IO;
using AcademicEditor.Core.Layout;
using AcademicEditor.Core.Layout.Model;
using AcademicEditor.Core.Parsing;
using AcademicEditor.Core.State;
using AcademicEditor.Core.Text;

using Avalonia.Threading;

namespace AcademicEditor.App.ViewModels;

/// <summary>
/// Estado do editor entre o documento e a superfície de desenho. MVVM leve, sem framework: a
/// view liga num único evento e volta a desenhar.
/// </summary>
/// <remarks>
/// O trabalho de verdade aqui é o pipeline de repaginação: cada tecla pede um layout novo, mas o
/// layout não pode rodar na UI thread nem uma vez por tecla. Ver <see cref="SchedulePagination"/>.
/// </remarks>
public sealed class EditorViewModel
{
    // Curto o bastante para a paginação parecer instantânea, longo o bastante para que uma
    // rajada de digitação vire um layout só. O número definitivo sai da medição com documento
    // de ~300 páginas (Fatia 6); até lá, este é um chute informado.
    private const int DebounceMilliseconds = 50;

    // Teto de espera. Sem ele, o debounce inanição: a repetição automática do teclado dispara a
    // cada ~33-40ms, menor que o debounce, então cada tecla cancelava a repaginação pendente
    // antes que ela rodasse e a tela só atualizava ao soltar a tecla. Com o teto, segurar uma
    // tecla repagina ~8x/s e a digitação normal continua coalescendo. Mesmo chute informado que
    // o debounce, e mede junto com ele na Fatia 6.
    private const int MaxLatencyMilliseconds = 120;

    private readonly ITextMeasurer _measurer;
    private readonly IDocumentStorage _storage;

    private EditorDocument _document;
    private UndoRedoStack _undo;

    private CancellationTokenSource? _pending;
    private long _lastPublishedAtMs;
    private int _requestedGeneration;
    private int _publishedGeneration;
    private PageSettings _pageSettings;
    private Caret _caret;
    private bool _caretColumnStale = true;
    private DocumentEncoding _encoding = DocumentEncoding.Utf8;

    public EditorViewModel(
        ITextMeasurer measurer,
        PageSettings pageSettings,
        string initialText,
        IDocumentStorage? storage = null)
    {
        ArgumentNullException.ThrowIfNull(measurer);
        ArgumentNullException.ThrowIfNull(initialText);

        _measurer = measurer;
        _storage = storage ?? new FileDocumentStorage();
        _pageSettings = pageSettings;
        _document = new EditorDocument(initialText);
        _undo = new UndoRedoStack(_document);

        // No começo do documento, não no fim: é onde todo editor põe o caret ao abrir um
        // arquivo — e, com a rolagem automática, deixá-lo no fim abriria o app na última folha.
        _caret = new Caret(0, 0.0);
        _lastPublishedAtMs = Environment.TickCount64;
        Paginated = PaginatedDocument.Empty(pageSettings);

        SchedulePagination();
    }

    /// <summary>
    /// Disparado na UI thread quando algo mudou o que se vê: layout novo ou caret movido. Nos
    /// dois casos a resposta da view é a mesma — redesenhar.
    /// </summary>
    public event EventHandler? Invalidated;

    /// <summary>
    /// Mudou o arquivo, o "salvo/não salvo" ou a última mensagem. Separado do
    /// <see cref="Invalidated"/> porque aquele dispara a cada publicação de layout — várias vezes
    /// por segundo enquanto se digita — e remontar o título da janela nesse ritmo é desperdício.
    /// </summary>
    public event EventHandler? DocumentStateChanged;

    /// <summary>Último layout publicado. A troca é de referência: quem está desenhando termina com o antigo, intacto.</summary>
    public PaginatedDocument Paginated { get; private set; }

    /// <summary>Onde o texto digitado entra, e para onde ↑/↓ miram.</summary>
    public Caret Caret => _caret;

    /// <summary>
    /// Geometria do caret na folha, recalculada quando o caret ou o layout muda — nunca no
    /// <c>Render</c>, que só desenha.
    /// </summary>
    public CaretPosition CaretPosition { get; private set; }

    /// <summary>Caminho do arquivo aberto, ou <c>null</c> num documento que nunca foi salvo.</summary>
    public string? FilePath { get; private set; }

    /// <summary>Há edição não gravada.</summary>
    /// <remarks>
    /// Marca em qualquer edição e só limpa ao salvar ou abrir. Desfazer até o estado gravado não
    /// limpa — para isso o histórico teria de guardar em que ponto o save aconteceu, e a conta
    /// erra a favor da segurança: no máximo se grava um arquivo idêntico ao que estava lá.
    /// </remarks>
    public bool IsModified { get; private set; }

    /// <summary>O que dizer a quem está escrevendo. A janela mostra isto no título.</summary>
    public string StatusMessage { get; private set; } = string.Empty;

    public PageSettings PageSettings
    {
        get => _pageSettings;
        set
        {
            if (_pageSettings == value)
            {
                return;
            }

            _pageSettings = value;
            SchedulePagination();
        }
    }

    public void InsertText(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return;
        }

        // O comprimento vem do documento, não da string: a normalização de fim de linha pode
        // encurtar o texto, e mover o caret por text.Length o deixaria adiante do buffer.
        var before = _caret.Offset;
        var edit = _document.Insert(before, text);

        if (edit.IsEmpty)
        {
            return;
        }

        // Só um caractere digitado se junta ao anterior no undo. Um Enter ou uma colagem abrem
        // grupo próprio: desfazer tem de devolver o documento a um estado que o autor reconheça.
        var kind = text.Length == 1 && text[0] != '\n' ? EditKind.Typing : EditKind.Other;

        _undo.Record(edit, kind, before, before + edit.LengthDelta);
        MoveCaretAfterEdit(before + edit.LengthDelta);
        MarkModified();
        SchedulePagination();
    }

    public void DeleteBackward()
    {
        if (_caret.Offset == 0)
        {
            return;
        }

        // Um par substituto é um caractere só para quem escreveu, e dois para o buffer. Apagar
        // metade dele deixaria um code unit órfão, que vira losango na tela e lixo no arquivo.
        var length = IsSurrogatePairEndingAt(_caret.Offset) ? 2 : 1;
        var before = _caret.Offset;
        var edit = _document.Delete(before - length, length);

        _undo.Record(edit, EditKind.Deleting, before, before - length);
        MoveCaretAfterEdit(before - length);
        MarkModified();
        SchedulePagination();
    }

    public void DeleteForward()
    {
        if (_caret.Offset >= _document.Length)
        {
            return;
        }

        var length = IsSurrogatePairStartingAt(_caret.Offset) ? 2 : 1;
        var before = _caret.Offset;
        var edit = _document.Delete(before, length);

        _undo.Record(edit, EditKind.Deleting, before, before);
        MoveCaretAfterEdit(before);
        MarkModified();
        SchedulePagination();
    }

    public void MoveCaretLeft() => Navigate(CaretNavigator.MoveLeft(_caret, Paginated, _measurer));

    public void MoveCaretRight() => Navigate(CaretNavigator.MoveRight(_caret, Paginated, _measurer));

    public void MoveCaretUp() => Navigate(CaretNavigator.MoveUp(_caret, Paginated, _measurer));

    public void MoveCaretDown() => Navigate(CaretNavigator.MoveDown(_caret, Paginated, _measurer));

    public void MoveCaretToLineStart() => Navigate(CaretNavigator.MoveToLineStart(_caret, Paginated, _measurer));

    public void MoveCaretToLineEnd() => Navigate(CaretNavigator.MoveToLineEnd(_caret, Paginated, _measurer));

    public void MoveCaretPageUp() => Navigate(CaretNavigator.MovePageUp(_caret, Paginated, _measurer));

    public void MoveCaretPageDown() => Navigate(CaretNavigator.MovePageDown(_caret, Paginated, _measurer));

    public bool CanUndo => _undo.CanUndo;

    public bool CanRedo => _undo.CanRedo;

    public void Undo() => Restore(_undo.Undo());

    public void Redo() => Restore(_undo.Redo());

    private void Restore(int? caretOffset)
    {
        if (caretOffset is not { } offset)
        {
            return;
        }

        MoveCaretAfterEdit(offset);
        MarkModified();
        SchedulePagination();
    }

    /// <summary>Move o caret por ordem de quem está escrevendo.</summary>
    /// <remarks>
    /// Fecha o grupo de digitação <b>antes</b> de olhar se o caret saiu do lugar: o que se escreve
    /// depois de andar pelo texto é outra edição. Fechar só quando o caret efetivamente anda
    /// deixaria o agrupamento dependente do layout estar fresco — logo após uma tecla ele ainda
    /// está no debounce, e a seta não acha para onde ir.
    /// </remarks>
    private void Navigate(Caret caret)
    {
        _undo.Break();
        SetCaret(caret);

        // Sair do bloco revelado muda o que se vê: a marcação dele se esconde e a do bloco novo
        // aparece. Enquanto o caret fica dentro do mesmo bloco, a seta não custa layout nenhum.
        if (!Paginated.RevealedBlock.Contains(_caret.Offset))
        {
            SchedulePagination();
        }
    }

    private void SetCaret(Caret caret)
    {
        if (caret == _caret)
        {
            return;
        }

        _caret = caret;
        _caretColumnStale = false;
        RefreshCaretPosition();
        Invalidated?.Invoke(this, EventArgs.Empty);
    }

    // Depois de uma edição o layout na tela ainda é o de antes, então a coluna alvo calculada
    // agora descreveria uma geometria que já não existe. Marca para recalcular quando o layout
    // novo chegar; até lá a barra fica na posição antiga, por um quadro.
    private void MoveCaretAfterEdit(int offset)
    {
        _caret = new Caret(offset, 0.0);
        _caretColumnStale = true;
    }

    /// <summary>Grava no arquivo aberto. Chame <see cref="SaveAsAsync"/> quando não houver um.</summary>
    public Task SaveAsync() =>
        FilePath is { } path ? SaveAsAsync(path) : Task.CompletedTask;

    public async Task SaveAsAsync(string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);

        // O snapshot é tirado aqui, na UI thread: gravar o que estava escrito quando o autor pediu
        // para gravar, mesmo que ele continue digitando enquanto o disco responde.
        var text = _document.CreateSnapshot().GetText();

        await _storage.SaveAsync(path, text, _encoding).ConfigureAwait(true);

        FilePath = path;
        IsModified = false;
        _undo.Break();
        Report($"salvo em {Path.GetFileName(path)}");
    }

    public async Task OpenAsync(string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);

        var loaded = await _storage.LoadAsync(path).ConfigureAwait(true);

        // Documento novo, histórico novo: os deltas do anterior descrevem peças de outra lista, e
        // desfazer por cima deles corromperia o buffer.
        _document = new EditorDocument(loaded.Text);
        _undo = new UndoRedoStack(_document);
        _encoding = loaded.Encoding;

        FilePath = path;
        IsModified = false;
        _caret = new Caret(0, 0.0);
        _caretColumnStale = true;

        Report($"aberto {Path.GetFileName(path)}");
        SchedulePagination();
    }

    private void MarkModified()
    {
        if (IsModified)
        {
            return;
        }

        IsModified = true;
        DocumentStateChanged?.Invoke(this, EventArgs.Empty);
    }

    public void Report(string message)
    {
        StatusMessage = message;
        DocumentStateChanged?.Invoke(this, EventArgs.Empty);
    }

    private void RefreshCaretPosition() =>
        CaretPosition = CaretGeometry.Locate(_caret.Offset, Paginated, _measurer, _caret.Affinity);

    private bool IsSurrogatePairEndingAt(int offset) =>
        offset >= 2
        && char.IsLowSurrogate(_document.CharAt(offset - 1))
        && char.IsHighSurrogate(_document.CharAt(offset - 2));

    private bool IsSurrogatePairStartingAt(int offset) =>
        offset + 1 < _document.Length
        && char.IsHighSurrogate(_document.CharAt(offset))
        && char.IsLowSurrogate(_document.CharAt(offset + 1));

    /// <summary>
    /// Pede uma repaginação. Cancela a anterior, espera o debounce, pagina fora da UI thread e
    /// publica de volta nela.
    /// </summary>
    /// <remarks>
    /// O snapshot é tirado <b>aqui</b>, na UI thread, e não lá dentro: é o que garante que o
    /// layout descreva o texto no momento em que foi pedido, mesmo que a digitação continue.
    /// </remarks>
    private void SchedulePagination()
    {
        _pending?.Cancel();

        // O CancellationTokenSource não é descartado. Sem timer e sem registro pendente, ele é
        // memória gerenciada comum, que o GC recolhe; descartá-lo corretamente exigiria
        // sincronizar a UI thread com a thread do layout para nada.
        var cancellation = new CancellationTokenSource();
        _pending = cancellation;

        var generation = ++_requestedGeneration;
        var snapshot = _document.CreateSnapshot();
        var settings = _pageSettings;
        var caretOffset = _caret.Offset;
        // A espera é contada desde a última publicação, não desde este pedido: sob repetição de
        // tecla ela encolhe a cada tecla até zerar no teto, publica, e recomeça inteira. O
        // resultado é uma publicação a cada MaxLatency, sem perder a coalescência no meio.
        var sincePublish = Environment.TickCount64 - _lastPublishedAtMs;
        var delay = (int)Math.Clamp(MaxLatencyMilliseconds - sincePublish, 0, DebounceMilliseconds);

        _ = PaginateAsync(snapshot, settings, caretOffset, generation, delay, cancellation.Token);
    }

    private async Task PaginateAsync(
        TextBufferSnapshot snapshot,
        PageSettings settings,
        int caretOffset,
        int generation,
        int delayMilliseconds,
        CancellationToken cancellationToken)
    {
        try
        {
            if (delayMilliseconds > 0)
            {
                await Task.Delay(delayMilliseconds, cancellationToken).ConfigureAwait(false);
            }

            var paginated = await Task.Run(
                () => LayoutEngine.Layout(
                    MarkupParser.Parse(snapshot.GetText()),
                    settings,
                    _measurer,
                    caretOffset,
                    cancellationToken),
                cancellationToken).ConfigureAwait(false);

            Dispatcher.UIThread.Post(() => Publish(paginated, generation));
        }
        catch (OperationCanceledException)
        {
            // Repaginação obsoleta: a tecla seguinte já pediu outra. Não é erro, é o caso comum.
        }
    }

    private void Publish(PaginatedDocument paginated, int generation)
    {
        // Cancelar não é instantâneo: um layout antigo pode chegar depois de um mais novo já ter
        // sido publicado. A geração é o que impede o documento de andar para trás na tela.
        if (generation <= _publishedGeneration)
        {
            return;
        }

        _publishedGeneration = generation;
        _lastPublishedAtMs = Environment.TickCount64;
        Paginated = paginated;

        if (_caretColumnStale)
        {
            _caret = CaretNavigator.At(_caret.Offset, paginated, _measurer);
            _caretColumnStale = false;
        }

        RefreshCaretPosition();
        Invalidated?.Invoke(this, EventArgs.Empty);
    }
}
