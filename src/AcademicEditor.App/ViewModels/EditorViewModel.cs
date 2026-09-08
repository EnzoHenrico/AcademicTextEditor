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
    private readonly EditorDocument _document;

    private CancellationTokenSource? _pending;
    private long _lastPublishedAtMs;
    private int _requestedGeneration;
    private int _publishedGeneration;
    private PageSettings _pageSettings;
    private Caret _caret;
    private bool _caretColumnStale = true;

    public EditorViewModel(ITextMeasurer measurer, PageSettings pageSettings, string initialText)
    {
        ArgumentNullException.ThrowIfNull(measurer);
        ArgumentNullException.ThrowIfNull(initialText);

        _measurer = measurer;
        _pageSettings = pageSettings;
        _document = new EditorDocument(initialText);

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

    /// <summary>Último layout publicado. A troca é de referência: quem está desenhando termina com o antigo, intacto.</summary>
    public PaginatedDocument Paginated { get; private set; }

    /// <summary>Onde o texto digitado entra, e para onde ↑/↓ miram.</summary>
    public Caret Caret => _caret;

    /// <summary>
    /// Geometria do caret na folha, recalculada quando o caret ou o layout muda — nunca no
    /// <c>Render</c>, que só desenha.
    /// </summary>
    public CaretPosition CaretPosition { get; private set; }

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
        var inserted = _document.Insert(_caret.Offset, text);

        MoveCaretAfterEdit(_caret.Offset + inserted);
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

        _document.Delete(_caret.Offset - length, length);
        MoveCaretAfterEdit(_caret.Offset - length);
        SchedulePagination();
    }

    public void DeleteForward()
    {
        if (_caret.Offset >= _document.Length)
        {
            return;
        }

        var length = IsSurrogatePairStartingAt(_caret.Offset) ? 2 : 1;

        _document.Delete(_caret.Offset, length);
        MoveCaretAfterEdit(_caret.Offset);
        SchedulePagination();
    }

    public void MoveCaretLeft() => SetCaret(CaretNavigator.MoveLeft(_caret, Paginated, _measurer));

    public void MoveCaretRight() => SetCaret(CaretNavigator.MoveRight(_caret, Paginated, _measurer));

    public void MoveCaretUp() => SetCaret(CaretNavigator.MoveUp(_caret, Paginated, _measurer));

    public void MoveCaretDown() => SetCaret(CaretNavigator.MoveDown(_caret, Paginated, _measurer));

    public void MoveCaretToLineStart() => SetCaret(CaretNavigator.MoveToLineStart(_caret, Paginated, _measurer));

    public void MoveCaretToLineEnd() => SetCaret(CaretNavigator.MoveToLineEnd(_caret, Paginated, _measurer));

    public void MoveCaretPageUp() => SetCaret(CaretNavigator.MovePageUp(_caret, Paginated, _measurer));

    public void MoveCaretPageDown() => SetCaret(CaretNavigator.MovePageDown(_caret, Paginated, _measurer));

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
        // A espera é contada desde a última publicação, não desde este pedido: sob repetição de
        // tecla ela encolhe a cada tecla até zerar no teto, publica, e recomeça inteira. O
        // resultado é uma publicação a cada MaxLatency, sem perder a coalescência no meio.
        var sincePublish = Environment.TickCount64 - _lastPublishedAtMs;
        var delay = (int)Math.Clamp(MaxLatencyMilliseconds - sincePublish, 0, DebounceMilliseconds);

        _ = PaginateAsync(snapshot, settings, generation, delay, cancellation.Token);
    }

    private async Task PaginateAsync(
        TextBufferSnapshot snapshot,
        PageSettings settings,
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
