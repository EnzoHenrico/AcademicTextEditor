using AcademicEditor.Core.Layout.Model;

namespace AcademicEditor.Core.Layout;

/// <summary>
/// Empilha linhas numa página até a altura útil acabar e então abre a próxima. É o passo que dá
/// ao editor a noção de folha — o que um code-editor de rolagem contínua não tem.
/// </summary>
/// <remarks>
/// Acumulador, não função pura, porque a decisão de quebra é sequencial e depende do que já foi
/// posto na página corrente. O resultado, esse sim, é imutável.
/// </remarks>
public sealed class PageBreaker
{
    private readonly double _contentHeightPt;
    private readonly List<PageLayout> _pages = [];
    private List<LaidOutLine> _current = [];
    private double _usedHeightPt;

    public PageBreaker(double contentHeightPt)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(contentHeightPt);
        _contentHeightPt = contentHeightPt;
    }

    public void AddLine(LaidOutLine line)
    {
        ArgumentNullException.ThrowIfNull(line);

        // A linha mais alta que a própria página não tem para onde ir: fica sozinha na dela e
        // estoura a margem inferior, em vez de sumir ou travar o laço.
        if (_usedHeightPt + line.HeightPt > _contentHeightPt && _current.Count > 0)
        {
            ClosePage();
        }

        _current.Add(line with { YPt = _usedHeightPt });
        _usedHeightPt += line.HeightPt;
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

    private void ClosePage()
    {
        _pages.Add(new PageLayout(_current.ToArray()));
        _current = [];
        _usedHeightPt = 0.0;
    }
}
