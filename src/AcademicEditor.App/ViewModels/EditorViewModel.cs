using AcademicEditor.Core.IO;
using AcademicEditor.Core.Layout;
using AcademicEditor.Core.Layout.Model;
using AcademicEditor.Core.Parsing;
using AcademicEditor.Core.State;
using AcademicEditor.Core.Text;

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
    private readonly ITextMeasurer _measurer;
    private readonly IDocumentStorage _storage;

    private EditorDocument _document;
    private UndoRedoStack _undo;

    // Um layout em voo por vez, e um pedido pendente. Não há mais token de cancelamento: matar a
    // repaginação em andamento era o que fazia a tela parar enquanto uma tecla ficava pressionada.
    private bool _paginating;
    private bool _pendingPagination;

    private PageSettings _pageSettings;

    // O texto que gerou o layout publicado. É contra ele que a próxima paginação descobre o que
    // mudou — comparar dois textos é exato e dispensa rastrear edição por edição.
    private string _publishedSource = string.Empty;

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

    public void InsertText(string text) => Insert(text, caretAdvance: null);

    /// <summary>Enter: abre uma linha em branco onde o caret está.</summary>
    /// <remarks>
    /// Não é um <c>InsertText("\n")</c>. Na fronteira de uma quebra por largura um <c>\n</c> só
    /// torna explícita a quebra que a margem já impunha, e a tela não muda — quem conta quantos
    /// são precisos, e para onde o caret vai depois, é <see cref="LineBreaks.ForEnter"/>, no Core.
    /// </remarks>
    public void InsertLineBreak()
    {
        var lineBreak = LineBreaks.ForEnter(_caret, Paginated);

        Insert(lineBreak.Text, lineBreak.CaretDelta);
    }

    /// <param name="caretAdvance">
    /// Quanto o caret anda, quando quem chamou sabe mais que o comprimento inserido. É o caso do
    /// Enter na fronteira de uma quebra: entram dois <c>\n</c>, mas o caret pode parar no
    /// primeiro. <c>null</c> é o caso comum — o caret vai para depois do que entrou.
    /// </param>
    private void Insert(string text, int? caretAdvance)
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

        var after = before + (caretAdvance ?? edit.LengthDelta);

        // Só um caractere digitado se junta ao anterior no undo. Um Enter ou uma colagem abrem
        // grupo próprio: desfazer tem de devolver o documento a um estado que o autor reconheça.
        var kind = text.Length == 1 && text[0] != '\n' ? EditKind.Typing : EditKind.Other;

        _undo.Record(edit, kind, before, after);

        // A afinidade sobrevive à digitação: quem está escrevendo no fim de uma linha quebrada
        // pela margem continua escrevendo lá, e não salta para o começo da linha de baixo a cada
        // tecla que recoloca o caret exatamente sobre a fronteira.
        MoveCaretAfterEdit(after, _caret.Affinity);
        MarkModified();
        SchedulePagination();
    }

    public void DeleteBackward()
    {
        if (_caret.Offset == 0)
        {
            return;
        }

        // Um marcador de bloco sai inteiro ou não sai: apagar o '\n' que isola um \page o grudaria
        // no texto de cima, e ele deixaria de ser quebra de página para virar texto na folha.
        if (BlockMarkers.BackspaceRange(_caret.Offset, Paginated) is { } marker)
        {
            RemoveRange(marker);
            return;
        }

        // Um par substituto é um caractere só para quem escreveu, e dois para o buffer. Apagar
        // metade dele deixaria um code unit órfão, que vira losango na tela e lixo no arquivo.
        var length = IsSurrogatePairEndingAt(_caret.Offset) ? 2 : 1;
        var before = _caret.Offset;
        var edit = _document.Delete(before - length, length);

        _undo.Record(edit, EditKind.Deleting, before, before - length);

        // Upstream: apagar para trás pousa no FIM da linha de cima. Quando o caractere que saiu
        // era o '\n' de uma quebra que a margem refaz no mesmo lugar, a tela fica idêntica — e
        // sem a afinidade o caret seria redesenhado no começo da linha de baixo, exatamente onde
        // estava, como se a tecla não tivesse feito nada. Fora de uma fronteira compartilhada ela
        // é inerte, porque CaretGeometry só recua quando as duas linhas dividem o offset.
        MoveCaretAfterEdit(before - length, CaretAffinity.Upstream);
        MarkModified();
        SchedulePagination();
    }

    public void DeleteForward()
    {
        if (_caret.Offset >= _document.Length)
        {
            return;
        }

        if (BlockMarkers.DeleteRange(_caret.Offset, Paginated) is { } marker)
        {
            RemoveRange(marker);
            return;
        }

        var length = IsSurrogatePairStartingAt(_caret.Offset) ? 2 : 1;
        var before = _caret.Offset;
        var edit = _document.Delete(before, length);

        // O caret não sai do lugar: preservar a afinidade é o que o mantém desenhado do mesmo
        // lado da fronteira de onde o autor apagou.
        _undo.Record(edit, EditKind.Deleting, before, before);
        MoveCaretAfterEdit(before, _caret.Affinity);
        MarkModified();
        SchedulePagination();
    }

    /// <summary>Apaga um trecho inteiro — um marcador de bloco — como uma única edição.</summary>
    private void RemoveRange(TextRange range)
    {
        var length = Math.Min(range.Length, _document.Length - range.Start);

        if (length <= 0)
        {
            return;
        }

        var before = _caret.Offset;
        var edit = _document.Delete(range.Start, length);

        // EditKind.Other: apagar um marcador não se junta a uma rajada de Backspace. Desfazer tem
        // de devolver a quebra de página numa vez só.
        _undo.Record(edit, EditKind.Other, before, range.Start);
        MoveCaretAfterEdit(range.Start, CaretAffinity.Downstream);
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

        MoveCaretAfterEdit(offset, CaretAffinity.Downstream);
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
    private void MoveCaretAfterEdit(int offset, CaretAffinity affinity)
    {
        _caret = new Caret(offset, 0.0, affinity);
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

        // Documento novo: o texto publicado descreve o anterior, e comparar contra ele diria que
        // "mudou tudo" — ou, pior, que mudou pouco. A paginação seguinte é completa.
        _publishedSource = string.Empty;

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
    /// Pede uma repaginação: um layout em voo por vez, e o próximo começa assim que ele termina.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Coalescer, não cancelar.</b> A versão anterior tinha debounce de 50ms com teto de 120ms
    /// e matava a repaginação pendente a cada tecla. O teto garantia que o layout <i>começasse</i>
    /// em até 120ms, mas nada garantia que ele <i>terminasse</i>: bastava um layout passar do
    /// intervalo de repetição do teclado (~33ms — um pico de GC basta) para a tecla seguinte
    /// matá-lo, o relógio da última publicação não avançar, a espera desabar para zero e cada
    /// tecla passar a matar a anterior. A tela travava até soltar a tecla.
    /// </para>
    /// <para>
    /// Aqui a inanição não é possível: nada é cancelado. Quem chega durante um layout só marca o
    /// pedido, e quem está rodando o atende ao terminar. A taxa se auto-regula — publica-se na
    /// velocidade em que os layouts terminam — e uma rajada continua virando um layout só.
    /// </para>
    /// </remarks>
    private void SchedulePagination()
    {
        _pendingPagination = true;

        if (!_paginating)
        {
            _ = PaginateWhileDirtyAsync();
        }
    }

    /// <summary>O que foi publicado da última vez: o texto e o layout que saiu dele.</summary>
    private readonly record struct Published(string Source, PaginatedDocument Document);

    private async Task PaginateWhileDirtyAsync()
    {
        _paginating = true;

        try
        {
            while (_pendingPagination)
            {
                _pendingPagination = false;

                // Tudo lido aqui, na UI thread, e por isso descrevendo o mesmo instante: o
                // snapshot do buffer, a geometria, o caret, e o par (texto, layout) publicados —
                // que são trocados na mesma linha do Publish e nunca chegam lá descasados.
                var snapshot = _document.CreateSnapshot();
                var settings = _pageSettings;
                var caretOffset = _caret.Offset;
                var published = new Published(_publishedSource, Paginated);

                // ConfigureAwait(true): a continuação volta para a UI thread, então Publish e a
                // condição do laço são lidos onde o estado vive. Some o Dispatcher.Post, e some a
                // guarda de geração — com um layout de cada vez, a ordem já é garantida.
                var laidOut = await Task.Run(() =>
                {
                    var source = snapshot.GetText();

                    return (Source: source, Document: LayoutEngine.Layout(
                        MarkupParser.Parse(source),
                        settings,
                        _measurer,
                        caretOffset,
                        LayoutReuse.Between(published.Source, source, published.Document)));
                }).ConfigureAwait(true);

                Publish(laidOut.Document, laidOut.Source);
            }
        }
        catch (Exception exception)
        {
            // O laço não pode morrer em silêncio: sem isto uma exceção deixaria o documento
            // congelado sem explicação. O finally devolve o estado, e a tecla seguinte recomeça.
            Report($"erro ao paginar: {exception.Message}");
        }
        finally
        {
            _paginating = false;
        }
    }

    private void Publish(PaginatedDocument paginated, string source)
    {
        Paginated = paginated;
        _publishedSource = source;

        if (_caretColumnStale)
        {
            // Com a afinidade que a edição escolheu, não com a padrão: é ela que decide de que
            // lado de uma quebra por largura o caret é desenhado, e o layout novo é a primeira
            // oportunidade de resolvê-la contra as linhas de verdade.
            _caret = CaretNavigator.At(_caret.Offset, paginated, _measurer, _caret.Affinity);
            _caretColumnStale = false;
        }

        RefreshCaretPosition();
        Invalidated?.Invoke(this, EventArgs.Empty);
    }
}
