using AcademicEditor.Core.Layout.Model;

namespace AcademicEditor.Core.Layout;

/// <summary>
/// Empilha linhas numa página até a altura útil acabar e então abre a próxima. É o passo que dá
/// ao editor a noção de folha — o que um code-editor de rolagem contínua não tem.
/// </summary>
/// <remarks>
/// <para>
/// Acumulador, não função pura, porque a decisão de quebra é sequencial e depende do que já foi
/// posto na página corrente. O resultado, esse sim, é imutável.
/// </para>
/// <para>
/// <b>As notas de rodapé entram aqui, e não num passe depois</b>, porque elas mudam a decisão de
/// quebra: a folha em que uma nota estreia perde a altura dela, e a linha que a chamou pode deixar
/// de caber. <b>Não há laço de convergência</b>, e essa é a boa surpresa do desenho: a altura de
/// uma nota é conhecida <i>antes</i> de a linha que a chama ser assentada, porque as definições são
/// quebradas primeiro. O breaker decide com antecedência — se a linha <b>mais</b> as notas que ela
/// estreia não cabem no que resta, as duas descem juntas.
/// </para>
/// </remarks>
public sealed class PageBreaker
{
    private readonly double _contentHeightPt;
    private readonly IReadOnlyDictionary<string, IReadOnlyList<LaidOutLine>>? _notes;
    private readonly double _ruleGapPt;

    private readonly List<PageLayout> _pages = [];
    private readonly HashSet<string> _placed = [];
    private readonly List<string> _pageNotes = [];
    private readonly List<string> _starting = [];

    private List<LaidOutLine> _current = [];
    private double _usedHeightPt;
    private double _notesHeightPt;

    /// <param name="notes">
    /// As linhas já quebradas de cada nota, por identificador. <c>null</c> num documento sem notas
    /// chamadas — que é o caminho que não paga nada.
    /// </param>
    /// <param name="ruleGapPt">
    /// A folga entre o texto e as notas, com o filete no meio dela. Zero quando não há notas.
    /// </param>
    public PageBreaker(
        double contentHeightPt,
        IReadOnlyDictionary<string, IReadOnlyList<LaidOutLine>>? notes = null,
        double ruleGapPt = 0.0)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(contentHeightPt);

        _contentHeightPt = contentHeightPt;
        _notes = notes is { Count: > 0 } ? notes : null;
        _ruleGapPt = ruleGapPt;
    }

    public void AddLine(LaidOutLine line)
    {
        ArgumentNullException.ThrowIfNull(line);

        var extraPt = Reserve(line);

        // A linha mais alta que a própria página não tem para onde ir: fica sozinha na dela e
        // estoura a margem inferior, em vez de sumir ou travar o laço. Vale igual para a linha que
        // só não cabe por causa das notas que ela estreia.
        if (_usedHeightPt + line.HeightPt + _notesHeightPt + extraPt > _contentHeightPt
            && _current.Count > 0)
        {
            ClosePage();
            extraPt = Reserve(line);
        }

        _current.Add(line with { YPt = _usedHeightPt });
        _usedHeightPt += line.HeightPt;

        foreach (var id in _starting)
        {
            _pageNotes.Add(id);
            _placed.Add(id);
        }

        _notesHeightPt += extraPt;
    }

    /// <summary>
    /// Fecha a página corrente por ordem explícita do autor (<c>\page</c>).
    /// </summary>
    /// <remarks>
    /// Fecha mesmo que a página esteja vazia: duas quebras seguidas produzem uma folha em branco,
    /// que é literalmente o que foi pedido. Adivinhar a intenção aqui seria pior que obedecer.
    /// </remarks>
    public void ForcePageBreak() => ClosePage();

    /// <summary>Fecha a página corrente e devolve o documento paginado.</summary>
    public IReadOnlyList<PageLayout> Build()
    {
        ClosePage();
        return _pages;
    }

    /// <summary>
    /// Quanto de folha as notas que esta linha estreia vão custar, e quais são elas.
    /// </summary>
    /// <remarks>
    /// Uma nota chamada duas vezes aparece <b>uma</b>, na folha da primeira chamada — daí o
    /// conjunto do que já foi assentado ser do documento e não da folha.
    /// </remarks>
    private double Reserve(LaidOutLine line)
    {
        _starting.Clear();

        if (_notes is null || line.FootnoteCalls.Count == 0)
        {
            return 0.0;
        }

        var extraPt = 0.0;

        foreach (var id in line.FootnoteCalls)
        {
            if (_placed.Contains(id) || _starting.Contains(id) || !_notes.TryGetValue(id, out var lines))
            {
                continue;
            }

            _starting.Add(id);
            extraPt += HeightOf(lines);
        }

        // O filete e a folga em volta dele são cobrados uma vez por folha, na primeira nota.
        return _starting.Count > 0 && _pageNotes.Count == 0 ? extraPt + _ruleGapPt : extraPt;
    }

    private void ClosePage()
    {
        var page = new PageLayout(_pageNotes.Count == 0 ? _current.ToArray() : WithNotes());

        _pages.Add(_pageNotes.Count == 0
            ? page
            : page with { FootnoteRulePt = _contentHeightPt - _notesHeightPt + (_ruleGapPt / 2.0) });

        _current = [];
        _pageNotes.Clear();
        _usedHeightPt = 0.0;
        _notesHeightPt = 0.0;
    }

    /// <summary>
    /// As linhas da folha mais as das notas, estas assentadas no pé da área de conteúdo.
    /// </summary>
    /// <remarks>
    /// As notas vão <b>depois</b> na lista, e isso é ordem de desenho: em ordem de fonte elas
    /// costumam vir do fim do arquivo. Quem precisa de ordem de fonte usa
    /// <c>PaginatedDocument.Index</c>, que existe exatamente para esta divergência.
    /// </remarks>
    private LaidOutLine[] WithNotes()
    {
        var total = _current.Count;

        foreach (var id in _pageNotes)
        {
            total += _notes![id].Count;
        }

        var lines = new LaidOutLine[total];
        _current.CopyTo(lines);

        var at = _current.Count;
        var yPt = _contentHeightPt - (_notesHeightPt - _ruleGapPt);

        foreach (var id in _pageNotes)
        {
            foreach (var line in _notes![id])
            {
                lines[at++] = line with { YPt = yPt };
                yPt += line.HeightPt;
            }
        }

        return lines;
    }

    private static double HeightOf(IReadOnlyList<LaidOutLine> lines)
    {
        var heightPt = 0.0;

        foreach (var line in lines)
        {
            heightPt += line.HeightPt;
        }

        return heightPt;
    }
}
