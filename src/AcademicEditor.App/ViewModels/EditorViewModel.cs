using AcademicEditor.Core.Layout;
using AcademicEditor.Core.Layout.Model;
using AcademicEditor.Core.Parsing;
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

    private readonly ITextMeasurer _measurer;
    private readonly EditorDocument _document;

    private CancellationTokenSource? _pending;
    private int _requestedGeneration;
    private int _publishedGeneration;
    private PageSettings _pageSettings;

    public EditorViewModel(ITextMeasurer measurer, PageSettings pageSettings, string initialText)
    {
        ArgumentNullException.ThrowIfNull(measurer);
        ArgumentNullException.ThrowIfNull(initialText);

        _measurer = measurer;
        _pageSettings = pageSettings;
        _document = new EditorDocument(initialText);

        InsertionOffset = _document.Length;
        Paginated = PaginatedDocument.Empty(pageSettings);

        SchedulePagination();
    }

    /// <summary>Disparado na UI thread quando há um novo <see cref="Paginated"/> para desenhar.</summary>
    public event EventHandler? LayoutChanged;

    /// <summary>Último layout publicado. A troca é de referência: quem está desenhando termina com o antigo, intacto.</summary>
    public PaginatedDocument Paginated { get; private set; }

    /// <summary>
    /// Onde o texto digitado entra. Provisório: na Fatia 4 isto vira o <c>Caret</c> do Core, com
    /// navegação de verdade. Por enquanto é um ponto que só anda para frente conforme se digita.
    /// </summary>
    public int InsertionOffset { get; private set; }

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

        _document.Insert(InsertionOffset, text);
        InsertionOffset += text.Length;
        SchedulePagination();
    }

    public void DeleteBackward()
    {
        if (InsertionOffset == 0)
        {
            return;
        }

        // Um par substituto é um caractere só para quem escreveu, e dois para o buffer. Apagar
        // metade dele deixaria um code unit órfão, que vira losango na tela e lixo no arquivo.
        var length = IsSurrogatePairEndingAt(InsertionOffset) ? 2 : 1;

        _document.Delete(InsertionOffset - length, length);
        InsertionOffset -= length;
        SchedulePagination();
    }

    public void DeleteForward()
    {
        if (InsertionOffset >= _document.Length)
        {
            return;
        }

        var length = IsSurrogatePairStartingAt(InsertionOffset) ? 2 : 1;

        _document.Delete(InsertionOffset, length);
        SchedulePagination();
    }

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

        _ = PaginateAsync(snapshot, settings, generation, cancellation.Token);
    }

    private async Task PaginateAsync(
        TextBufferSnapshot snapshot,
        PageSettings settings,
        int generation,
        CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(DebounceMilliseconds, cancellationToken).ConfigureAwait(false);

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
        Paginated = paginated;
        LayoutChanged?.Invoke(this, EventArgs.Empty);
    }
}
