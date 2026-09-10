using AcademicEditor.App.Rendering;

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
    private TypographyPreset _typography;
    private HeaderFooterSettings _bands;

    // O texto que gerou o layout publicado. É contra ele que a próxima paginação descobre o que
    // mudou — comparar dois textos é exato e dispensa rastrear edição por edição.
    private string _publishedSource = string.Empty;

    private Selection _selection;
    private bool _caretColumnStale = true;
    private DocumentEncoding _encoding = DocumentEncoding.Utf8;

    /// <param name="typography">
    /// A norma tipográfica. <c>null</c> usa o <see cref="TypographyPreset.Default"/>, que é o do
    /// MVP — a janela passa o da ABNT.
    /// </param>
    /// <param name="bands">
    /// Cabeçalho e rodapé. <c>null</c> é documento sem nenhum dos dois.
    /// </param>
    public EditorViewModel(
        ITextMeasurer measurer,
        PageSettings pageSettings,
        string initialText,
        IDocumentStorage? storage = null,
        TypographyPreset? typography = null,
        HeaderFooterSettings? bands = null)
    {
        ArgumentNullException.ThrowIfNull(measurer);
        ArgumentNullException.ThrowIfNull(initialText);

        _measurer = measurer;
        _storage = storage ?? new FileDocumentStorage();
        _pageSettings = pageSettings;
        _typography = typography ?? TypographyPreset.Default;
        _bands = bands ?? HeaderFooterSettings.None;
        _document = new EditorDocument(initialText);
        _undo = new UndoRedoStack(_document);

        // No começo do documento, não no fim: é onde todo editor põe o caret ao abrir um
        // arquivo — e, com a rolagem automática, deixá-lo no fim abriria o app na última folha.
        _selection = Selection.At(new Caret(0, 0.0));
        Paginated = PaginatedDocument.Empty(pageSettings, _typography);

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
    /// <remarks>
    /// É a ponta ativa da seleção, e não um campo à parte: um caret e uma seleção que se
    /// contradizem seriam dois estados para a mesma coisa, e o dia em que divergissem o texto
    /// apagado não seria o texto destacado.
    /// </remarks>
    public Caret Caret => _selection.Active;

    /// <summary>O trecho selecionado. Recolhida no caret quando não há nada selecionado.</summary>
    public Selection Selection => _selection;

    /// <summary>
    /// Onde pintar o destaque, recalculado quando a seleção ou o layout muda — nunca no
    /// <c>Render</c>, pelo mesmo motivo do <see cref="CaretPosition"/>.
    /// </summary>
    public IReadOnlyList<SelectionRect> SelectionRects { get; private set; } = [];

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

    /// <summary>A norma tipográfica em uso. Trocá-la repagina o documento inteiro.</summary>
    /// <remarks>
    /// Do zero, e não por reaproveitamento: as linhas publicadas foram medidas na fonte anterior, e
    /// o <c>LayoutEngine</c> recusa reaproveitá-las justamente por isso.
    /// </remarks>
    public TypographyPreset Typography
    {
        get => _typography;
        set
        {
            ArgumentNullException.ThrowIfNull(value);

            if (_typography == value)
            {
                return;
            }

            _typography = value;
            SchedulePagination();
        }
    }

    /// <summary>Cabeçalho e rodapé. Trocá-los remonta as faixas na próxima publicação.</summary>
    /// <remarks>
    /// Repagina, e não só remonta: é o caminho simples, e trocar cabeçalho não é operação de
    /// digitação — acontece ao abrir ou salvar um arquivo com outro nome, não a cada tecla.
    /// </remarks>
    public HeaderFooterSettings Bands
    {
        get => _bands;
        set
        {
            ArgumentNullException.ThrowIfNull(value);

            if (_bands == value)
            {
                return;
            }

            _bands = value;
            SchedulePagination();
        }
    }

    /// <summary>Caracteres e palavras do documento, do último layout publicado.</summary>
    /// <remarks>
    /// Do layout publicado, e não do buffer: a contagem é feita na mesma passada de background que
    /// já materializou a fonte inteira para paginar, então custa a varredura e nada mais. Fica até
    /// um layout atrás do que se acabou de digitar — atraso invisível num contador, e o mesmo que a
    /// tela inteira tem.
    /// </remarks>
    public DocumentStatistics Statistics { get; private set; }

    /// <summary>Caracteres e palavras do trecho selecionado, ou <c>null</c> sem seleção.</summary>
    public DocumentStatistics? SelectionStatistics { get; private set; }

    public void InsertText(string text) => Insert(text, caretAdvance: null);

    /// <summary>Enter: abre uma linha em branco onde o caret está.</summary>
    /// <remarks>
    /// Não é um <c>InsertText("\n")</c>. Na fronteira de uma quebra por largura um <c>\n</c> só
    /// torna explícita a quebra que a margem já impunha, e a tela não muda — quem conta quantos
    /// são precisos, e para onde o caret vai depois, é <see cref="LineBreaks.ForEnter"/>, no Core.
    /// </remarks>
    public void InsertLineBreak()
    {
        // Com um trecho selecionado o Enter o substitui, e aí não há fronteira a materializar: a
        // que interessava era a do caret que acaba de deixar de existir.
        if (!_selection.IsEmpty)
        {
            Insert("\n", caretAdvance: null);
            return;
        }

        var lineBreak = LineBreaks.ForEnter(Caret, Paginated);

        Insert(lineBreak.Text, lineBreak.CaretDelta);
    }

    /// <summary>Apaga o trecho selecionado. Devolve se havia o que apagar.</summary>
    public bool DeleteSelection()
    {
        if (_selection.IsEmpty)
        {
            return false;
        }

        var range = _selection.Range;
        var before = Caret.Offset;
        var edit = _document.Delete(range.Start, range.Length);

        // EditKind.Other: apagar um trecho não se junta a uma rajada de Backspace. Desfazer tem de
        // devolver o trecho inteiro numa vez só.
        _undo.Record(edit, EditKind.Other, before, range.Start);
        MoveCaretAfterEdit(range.Start, CaretAffinity.Downstream);
        MarkModified();
        SchedulePagination();

        return true;
    }

    /// <summary>O texto destacado, ou vazio quando não há seleção.</summary>
    /// <remarks>
    /// Materializa só o trecho: copiar uma linha não pode custar o documento inteiro, que nas 301
    /// páginas do corpus de referência são ~2MB direto no Large Object Heap.
    /// </remarks>
    public string SelectedText =>
        _selection.IsEmpty
            ? string.Empty
            : _document.CreateSnapshot().GetText(_selection.Range.Start, _selection.Range.Length);

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

        var before = Caret.Offset;
        var affinity = Caret.Affinity;
        var range = _selection.Range;
        var replacing = !_selection.IsEmpty;

        // Digitar, colar ou apertar Enter com um trecho selecionado o substitui — que é o que
        // qualquer editor faz, e é o que a seleção existe para permitir.
        var removal = replacing ? _document.Delete(range.Start, range.Length) : PieceEdit.Empty;
        var start = replacing ? range.Start : before;

        // O comprimento vem do documento, não da string: a normalização de fim de linha pode
        // encurtar o texto, e mover o caret por text.Length o deixaria adiante do buffer.
        var insertion = _document.Insert(start, text);

        if (insertion.IsEmpty && removal.IsEmpty)
        {
            return;
        }

        var after = start + (caretAdvance ?? insertion.LengthDelta);

        if (replacing)
        {
            // As duas edições num grupo só: um Ctrl+Z tem de devolver o texto que estava
            // destacado, e não a metade dele.
            _undo.RecordCompound([removal, insertion], before, after);
        }
        else
        {
            // Só um caractere digitado se junta ao anterior no undo. Um Enter ou uma colagem abrem
            // grupo próprio: desfazer tem de devolver o documento a um estado que o autor reconheça.
            var kind = text.Length == 1 && text[0] != '\n' ? EditKind.Typing : EditKind.Other;

            _undo.Record(insertion, kind, before, after);
        }

        // A afinidade sobrevive à digitação: quem está escrevendo no fim de uma linha quebrada
        // pela margem continua escrevendo lá, e não salta para o começo da linha de baixo a cada
        // tecla que recoloca o caret exatamente sobre a fronteira. Numa substituição ela não
        // sobrevive a nada — a fronteira que ela descrevia estava no texto que acabou de sair.
        MoveCaretAfterEdit(after, replacing ? CaretAffinity.Downstream : affinity);
        MarkModified();
        SchedulePagination();
    }

    public void DeleteBackward()
    {
        // Com um trecho selecionado, é ele que sai — antes da guarda do início do documento, que
        // fala do caret e não do trecho.
        if (DeleteSelection())
        {
            return;
        }

        if (Caret.Offset == 0)
        {
            return;
        }

        // Um marcador de bloco sai inteiro ou não sai: apagar o '\n' que isola um \page o grudaria
        // no texto de cima, e ele deixaria de ser quebra de página para virar texto na folha.
        if (BlockMarkers.BackspaceRange(Caret.Offset, Paginated) is { } marker)
        {
            RemoveRange(marker);
            return;
        }

        // Um par substituto é um caractere só para quem escreveu, e dois para o buffer. Apagar
        // metade dele deixaria um code unit órfão, que vira losango na tela e lixo no arquivo.
        var length = IsSurrogatePairEndingAt(Caret.Offset) ? 2 : 1;
        var before = Caret.Offset;
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
        if (DeleteSelection())
        {
            return;
        }

        if (Caret.Offset >= _document.Length)
        {
            return;
        }

        if (BlockMarkers.DeleteRange(Caret.Offset, Paginated) is { } marker)
        {
            RemoveRange(marker);
            return;
        }

        var length = IsSurrogatePairStartingAt(Caret.Offset) ? 2 : 1;
        var before = Caret.Offset;
        var edit = _document.Delete(before, length);

        // O caret não sai do lugar: preservar a afinidade é o que o mantém desenhado do mesmo
        // lado da fronteira de onde o autor apagou.
        _undo.Record(edit, EditKind.Deleting, before, before);
        MoveCaretAfterEdit(before, Caret.Affinity);
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

        var before = Caret.Offset;
        var edit = _document.Delete(range.Start, length);

        // EditKind.Other: apagar um marcador não se junta a uma rajada de Backspace. Desfazer tem
        // de devolver a quebra de página numa vez só.
        _undo.Record(edit, EditKind.Other, before, range.Start);
        MoveCaretAfterEdit(range.Start, CaretAffinity.Downstream);
        MarkModified();
        SchedulePagination();
    }

    /// <param name="extend">
    /// Segurando Shift: a âncora fica onde está e o trecho cresce. Sem ele, a seleção se recolhe
    /// no caret novo — que é o que faz uma seta desmarcar, em qualquer editor.
    /// </param>
    public void MoveCaretLeft(bool extend = false) =>
        Navigate(CaretNavigator.MoveLeft(Caret, Paginated, _measurer), extend);

    public void MoveCaretRight(bool extend = false) =>
        Navigate(CaretNavigator.MoveRight(Caret, Paginated, _measurer), extend);

    public void MoveCaretUp(bool extend = false) =>
        Navigate(CaretNavigator.MoveUp(Caret, Paginated, _measurer), extend);

    public void MoveCaretDown(bool extend = false) =>
        Navigate(CaretNavigator.MoveDown(Caret, Paginated, _measurer), extend);

    public void MoveCaretToLineStart(bool extend = false) =>
        Navigate(CaretNavigator.MoveToLineStart(Caret, Paginated, _measurer), extend);

    public void MoveCaretToLineEnd(bool extend = false) =>
        Navigate(CaretNavigator.MoveToLineEnd(Caret, Paginated, _measurer), extend);

    public void MoveCaretPageUp(bool extend = false) =>
        Navigate(CaretNavigator.MovePageUp(Caret, Paginated, _measurer), extend);

    public void MoveCaretPageDown(bool extend = false) =>
        Navigate(CaretNavigator.MovePageDown(Caret, Paginated, _measurer), extend);

    /// <summary>Põe o caret onde o autor clicou, em coordenadas da área de conteúdo da folha.</summary>
    public void PlaceCaretAt(int pageIndex, double xPt, double yPt, bool extend = false) =>
        Navigate(CaretNavigator.AtPoint(pageIndex, xPt, yPt, Paginated, _measurer), extend);

    /// <summary>Duplo clique: a palavra sob o ponto.</summary>
    public void SelectWordAt(int pageIndex, double xPt, double yPt) =>
        SelectRange(WordBoundaries.WordAt(
            CaretNavigator.AtPoint(pageIndex, xPt, yPt, Paginated, _measurer).Offset,
            Paginated));

    /// <summary>Triplo clique: a linha <b>visual</b>, a mesma que Home e End delimitam.</summary>
    public void SelectLineAt(int pageIndex, double xPt, double yPt)
    {
        var caret = CaretNavigator.AtPoint(pageIndex, xPt, yPt, Paginated, _measurer);
        var start = CaretNavigator.MoveToLineStart(caret, Paginated, _measurer).Offset;
        var end = CaretNavigator.MoveToLineEnd(caret, Paginated, _measurer).Offset;

        SelectRange(new TextRange(start, end - start));
    }

    public void SelectAll() => SelectRange(new TextRange(0, _document.Length));

    /// <summary>Avança o alinhamento de todo bloco que a seleção toca.</summary>
    /// <remarks>
    /// <para>
    /// <b>É uma edição de texto como outra qualquer</b>, e é o que faz a feature inteira caber sem
    /// máquina nova: a marcação vai para o arquivo, o undo funciona porque é edição, e a
    /// repaginação seguinte já a revela na linha do caret.
    /// </para>
    /// <para>
    /// O texto vem do <b>buffer</b>, e não do último layout publicado. Aqui a análise é de texto —
    /// onde cada linha começa —, e ler uma versão atrasada dele daria fronteiras erradas e uma
    /// edição no lugar errado. Custa materializar o documento, o mesmo que salvar; <c>Ctrl+J</c>
    /// não é caminho de tecla.
    /// </para>
    /// </remarks>
    public void CycleAlignment()
    {
        var source = _document.CreateSnapshot().GetText();
        var edits = BlockAlignment.Next(source, _selection.Range);

        if (edits.Count == 0)
        {
            return;
        }

        var before = Caret.Offset;
        var applied = new List<PieceEdit>(edits.Count * 2);

        // De trás para a frente, que é como BlockAlignment as devolve: cada troca acontece num
        // texto que as anteriores ainda não deslocaram.
        foreach (var edit in edits)
        {
            if (edit.Length > 0)
            {
                applied.Add(_document.Delete(edit.Start, edit.Length));
            }

            if (edit.Replacement.Length > 0)
            {
                applied.Add(_document.Insert(edit.Start, edit.Replacement));
            }
        }

        var active = BlockAlignment.Reposition(before, edits);

        // Um grupo só: um Ctrl+Z desfaz o alinhamento inteiro, e não linha por linha.
        _undo.RecordCompound(applied, before, active);

        // A seleção sobrevive, sobre o mesmo texto: sem repor os dois offsets ela escorregaria
        // alguns caracteres a cada Ctrl+J, e o segundo toque pegaria outras linhas.
        _selection = new Selection(
            BlockAlignment.Reposition(_selection.Anchor, edits),
            new Caret(active, 0.0));

        _caretColumnStale = true;
        MarkModified();
        SchedulePagination();
    }

    /// <summary>Seleciona um trecho e põe o caret no fim dele.</summary>
    /// <remarks>
    /// <c>Upstream</c>: numa quebra por largura o fim do trecho é também o começo da linha de
    /// baixo, e o caret pertence ao fim do que ficou selecionado.
    /// </remarks>
    private void SelectRange(TextRange range)
    {
        _undo.Break();
        SetSelection(new Selection(
            range.Start,
            CaretNavigator.At(range.End, Paginated, _measurer, CaretAffinity.Upstream)));
    }

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
    private void Navigate(Caret caret, bool extend)
    {
        _undo.Break();
        SetSelection(extend ? _selection.ExtendTo(caret) : Selection.At(caret));

        // Sair do bloco revelado muda o que se vê: a marcação dele se esconde e a do bloco novo
        // aparece. Enquanto o caret fica dentro do mesmo bloco, a seta não custa layout nenhum.
        if (!Paginated.RevealedBlock.Contains(Caret.Offset))
        {
            SchedulePagination();
        }
    }

    // Compara a seleção inteira, e não só o caret: uma seta na borda do documento não move o
    // caret mas tem de desmanchar o trecho selecionado, e comparar só o caret o deixaria destacado.
    private void SetSelection(Selection selection)
    {
        if (selection == _selection)
        {
            return;
        }

        _selection = selection;
        _caretColumnStale = false;
        RefreshCaretPosition();
        SetStatistics(Statistics);
        Invalidated?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Publica as contagens, e avisa a janela só quando algum dos dois números mudou.
    /// </summary>
    /// <remarks>
    /// A contagem da seleção sai do <b>texto publicado</b>, com <c>AsSpan</c>: nada é
    /// materializado. Um <c>Ctrl+A</c> na tese inteira seria ~2MB direto no Large Object Heap se
    /// pedisse o trecho ao buffer, e isso a cada movimento do ponteiro durante um arrasto.
    /// </remarks>
    private void SetStatistics(DocumentStatistics document)
    {
        var selection = SelectionStatisticsOf(_selection);

        if (Statistics == document && SelectionStatistics == selection)
        {
            return;
        }

        Statistics = document;
        SelectionStatistics = selection;
        DocumentStateChanged?.Invoke(this, EventArgs.Empty);
    }

    private DocumentStatistics? SelectionStatisticsOf(Selection selection)
    {
        if (selection.IsEmpty)
        {
            return null;
        }

        // O texto publicado pode estar um layout atrás do buffer, e aí o trecho selecionado
        // descreveria posições que ele ainda não tem. Grampeia: um número momentaneamente curto é
        // melhor que uma exceção no caminho do ponteiro.
        var start = Math.Clamp(selection.Range.Start, 0, _publishedSource.Length);
        var end = Math.Clamp(selection.Range.End, start, _publishedSource.Length);

        return DocumentStatistics.Of(_publishedSource.AsSpan(start, end - start));
    }

    // Depois de uma edição o layout na tela ainda é o de antes, então a coluna alvo calculada
    // agora descreveria uma geometria que já não existe. Marca para recalcular quando o layout
    // novo chegar; até lá a barra fica na posição antiga, por um quadro.
    private void MoveCaretAfterEdit(int offset, CaretAffinity affinity)
    {
        // Recolhe a seleção: o que estava destacado ou saiu do texto, ou deixou de ser o assunto.
        _selection = Selection.At(new Caret(offset, 0.0, affinity));
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
        UpdateBandTitle();
        Report($"salvo em {Path.GetFileName(path)}");
    }

    /// <summary>Grava o documento paginado como PDF.</summary>
    /// <remarks>
    /// <para>
    /// Em background, e o que torna isso seguro é a mesma propriedade que faz o layout rodar fora
    /// da UI thread desde o MVP: <see cref="PaginatedDocument"/> é imutável do topo às folhas.
    /// Quem continua digitando durante a exportação troca a referência de <see cref="Paginated"/>;
    /// o documento que foi para o disco é o que existia quando o autor pediu — que é o mesmo
    /// contrato do save.
    /// </para>
    /// <para>
    /// O exportador vive no App e consome o <see cref="PaginatedDocument"/> de fora do Core. É
    /// exatamente o que a regra "o Core nunca referencia o toolkit" existia para permitir.
    /// </para>
    /// </remarks>
    public async Task ExportPdfAsync(string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);

        var document = Paginated;
        var title = FilePath is { } current
            ? Path.GetFileNameWithoutExtension(current)
            : Path.GetFileNameWithoutExtension(path);

        await Task.Run(() => PdfExporter.Export(document, path, title)).ConfigureAwait(true);

        Report($"exportado para {Path.GetFileName(path)}");
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
        _selection = Selection.At(new Caret(0, 0.0));
        _caretColumnStale = true;
        UpdateBandTitle();

        Report($"aberto {Path.GetFileName(path)}");
        SchedulePagination();
    }

    /// <summary>O <c>{title}</c> do cabeçalho segue o arquivo aberto.</summary>
    /// <remarks>
    /// Passa pela propriedade, e não pelo campo, para herdar a comparação por valor do record:
    /// salvar por cima do mesmo arquivo não repagina nada.
    /// </remarks>
    private void UpdateBandTitle() =>
        Bands = _bands with
        {
            DocumentTitle = FilePath is { } path ? Path.GetFileNameWithoutExtension(path) : string.Empty,
        };

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

    private void RefreshCaretPosition()
    {
        CaretPosition = CaretGeometry.Locate(Caret.Offset, Paginated, _measurer, Caret.Affinity);
        SelectionRects = SelectionGeometry.RectsFor(_selection, Paginated, _measurer);
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
                var typography = _typography;
                var bands = _bands;
                var caretOffset = Caret.Offset;
                var published = new Published(_publishedSource, Paginated);

                // ConfigureAwait(true): a continuação volta para a UI thread, então Publish e a
                // condição do laço são lidos onde o estado vive. Some o Dispatcher.Post, e some a
                // guarda de geração — com um layout de cada vez, a ordem já é garantida.
                var laidOut = await Task.Run(() =>
                {
                    var source = snapshot.GetText();
                    var ast = MarkupParser.Parse(source, typography);

                    // Paginar, preencher o sumário e montar as faixas são três passes com uma
                    // ordem obrigatória, e quem a conhece é o LayoutEngine: montá-la aqui seria a
                    // segunda cópia dela — a primeira coisa a divergir no dia em que entrar um
                    // quarto passe.
                    return (
                        Source: source,
                        Document: LayoutEngine.Publish(
                            ast,
                            settings,
                            _measurer,
                            bands,
                            caretOffset,
                            LayoutReuse.Between(published.Source, source, published.Document),
                            typography),

                        // Contar aqui é de graça: a fonte inteira já está materializada para o
                        // parser, e a varredura é uma passada sem alocar. Na UI thread, a cada
                        // tecla, seria um megabyte percorrido no caminho do teclado.
                        Statistics: DocumentStatistics.Of(source));
                }).ConfigureAwait(true);

                Publish(laidOut.Document, laidOut.Source, laidOut.Statistics);
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

    private void Publish(PaginatedDocument paginated, string source, DocumentStatistics statistics)
    {
        Paginated = paginated;
        _publishedSource = source;
        SetStatistics(statistics);

        if (_caretColumnStale)
        {
            // Com a afinidade que a edição escolheu, não com a padrão: é ela que decide de que
            // lado de uma quebra por largura o caret é desenhado, e o layout novo é a primeira
            // oportunidade de resolvê-la contra as linhas de verdade.
            // 'with': a âncora sobrevive à repaginação. Quem estava com um trecho selecionado e
            // viu um layout novo chegar continua com ele selecionado.
            _selection = _selection with
            {
                Active = CaretNavigator.At(Caret.Offset, paginated, _measurer, Caret.Affinity),
            };

            _caretColumnStale = false;
        }

        RefreshCaretPosition();
        Invalidated?.Invoke(this, EventArgs.Empty);
    }
}
