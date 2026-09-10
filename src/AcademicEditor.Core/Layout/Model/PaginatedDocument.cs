namespace AcademicEditor.Core.Layout.Model;

/// <summary>Trecho do buffer, em caracteres UTF-16.</summary>
public readonly record struct TextRange(int Start, int Length)
{
    public static readonly TextRange Empty = new(-1, 0);

    public int End => Start + Length;

    public bool Contains(int offset) => Start >= 0 && offset >= Start && offset <= End;
}

/// <summary>
/// Onde uma linha está na pilha de folhas, e que trecho da fonte ela cobre.
/// </summary>
/// <remarks>
/// Guarda coordenadas, e não a linha: assim o índice é um array de valores contíguo — dezesseis mil
/// deles numa tese — em vez de uma segunda lista de referências para os mesmos objetos.
/// </remarks>
public readonly record struct LineRef(int PageIndex, int LineIndex, int SourceStart, int SourceLength)
{
    public int SourceEnd => SourceStart + SourceLength;
}

/// <summary>
/// Resultado completo de uma paginação. Imutável do topo às folhas: o layout roda em background e
/// publica trocando esta referência, sem lock e sem risco de a UI ler um estado meio construído.
/// </summary>
/// <param name="RevealedBlock">
/// O bloco que estava sob o caret quando isto foi paginado — o único cuja marcação aparece. Sai no
/// resultado para que quem move o caret saiba quando precisa repaginar: enquanto ele continuar
/// dentro deste trecho, o que se vê não muda, e uma seta não custa um layout.
/// </param>
public sealed record PaginatedDocument(
    IReadOnlyList<PageLayout> Pages,
    PageSettings Settings,
    TextRange RevealedBlock)
{
    public PaginatedDocument(IReadOnlyList<PageLayout> pages, PageSettings settings)
        : this(pages, settings, TextRange.Empty)
    {
    }

    /// <summary>A norma tipográfica com que estas linhas foram medidas.</summary>
    /// <remarks>
    /// Sai no resultado pelo mesmo motivo de <see cref="Settings"/>: as duas são o que produziu
    /// este layout, e o <c>LayoutEngine</c> recusa reaproveitar linhas quando qualquer uma delas
    /// mudou. Reaproveitar linhas medidas em outra fonte não quebra o desenho — quebra o caret, e
    /// isso aparece longe de onde errou.
    /// <para>
    /// Propriedade <c>init</c>, e não parâmetro posicional, para que as construções que não se
    /// importam com tipografia continuem com três argumentos. Entra na igualdade do record do
    /// mesmo jeito, que é o que a guarda do reaproveitamento precisa.
    /// </para>
    /// </remarks>
    public TypographyPreset Typography { get; init; } = TypographyPreset.Default;

    /// <summary>
    /// Todas as linhas do documento em <b>ordem de fonte</b>, com onde cada uma foi desenhada.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>A ordem de desenho e a ordem de fonte eram a mesma coisa</b>, e o motor inteiro se
    /// apoiava nisso sem que estivesse escrito em lugar nenhum: quem procura a linha de um offset
    /// passeava pelas folhas assumindo que os <c>SourceStart</c> só crescem. Uma nota de rodapé
    /// desenhada na folha da chamada, com a definição escrita no fim do arquivo, é o primeiro caso
    /// em que as duas divergem — e aí param juntos o caret, a seleção, o <c>WordBoundaries</c> e o
    /// hit test. <see cref="PageLayout.Lines"/> passa a ser oficialmente ordem de <i>desenho</i>, e
    /// este índice é a visão por ordem de <i>fonte</i>.
    /// </para>
    /// <para>
    /// Construído no próprio construtor, e não sob demanda, porque esquecer de construí-lo não
    /// daria erro: daria um caret que não acha onde pousar. <b>Cuidado com
    /// <c>with { Pages = ... }</c></b> — a cópia de record copia campos e não reexecuta este
    /// cálculo. Use <see cref="WithSameLines"/>, que confere.
    /// </para>
    /// </remarks>
    public IReadOnlyList<LineRef> Index { get; } = BuildIndex(Pages);

    /// <summary>
    /// O mesmo documento com folhas novas que carregam <b>exatamente as mesmas linhas</b>.
    /// </summary>
    /// <remarks>
    /// Existe para que a armadilha do <c>with { Pages = ... }</c> seja um erro alto em vez de um
    /// caret perdido: o <see cref="Index"/> é reaproveitado, e reaproveitá-lo só é correto quando
    /// as listas de linhas são os mesmos objetos. É o caso do passe de cabeçalho e rodapé, que só
    /// acrescenta faixas às folhas. A conferência é por referência, que é exatamente a condição, e
    /// custa uma comparação por folha.
    /// </remarks>
    public PaginatedDocument WithSameLines(IReadOnlyList<PageLayout> pages)
    {
        ArgumentNullException.ThrowIfNull(pages);

        if (pages.Count != Pages.Count)
        {
            throw new ArgumentException(
                $"{pages.Count} folhas contra {Pages.Count}: o índice de linhas não sobreviveria.",
                nameof(pages));
        }

        for (var index = 0; index < pages.Count; index++)
        {
            if (!ReferenceEquals(pages[index].Lines, Pages[index].Lines))
            {
                throw new ArgumentException(
                    $"a folha {index} trocou de linhas: use o construtor, que refaz o índice.",
                    nameof(pages));
            }
        }

        return this with { Pages = pages };
    }

    /// <summary>Documento sem nenhuma página é estado inválido: há sempre ao menos uma folha.</summary>
    public static PaginatedDocument Empty(PageSettings settings, TypographyPreset? typography = null) =>
        new([new PageLayout([])], settings) { Typography = typography ?? TypographyPreset.Default };

    /// <remarks>
    /// <b>A ordenação só acontece quando as duas ordens divergem</b>, que hoje é nunca: o passeio
    /// pelas folhas já sai ordenado num documento sem nota de rodapé, e conferir isso custa uma
    /// comparação por linha. O desempate é a posição na folha, para que linhas que dividem o mesmo
    /// offset — as vazias — mantenham a ordem em que foram assentadas, que é a que a varredura
    /// linear devolvia.
    /// </remarks>
    private static LineRef[] BuildIndex(IReadOnlyList<PageLayout> pages)
    {
        var count = 0;

        foreach (var page in pages)
        {
            count += page.Lines.Count;
        }

        var index = new LineRef[count];
        var at = 0;
        var sorted = true;
        var previousStart = int.MinValue;

        for (var pageIndex = 0; pageIndex < pages.Count; pageIndex++)
        {
            var lines = pages[pageIndex].Lines;

            for (var lineIndex = 0; lineIndex < lines.Count; lineIndex++)
            {
                var line = lines[lineIndex];

                index[at++] = new LineRef(pageIndex, lineIndex, line.SourceStart, line.SourceLength);
                sorted &= line.SourceStart >= previousStart;
                previousStart = line.SourceStart;
            }
        }

        if (!sorted)
        {
            Array.Sort(index, static (left, right) =>
            {
                var byStart = left.SourceStart.CompareTo(right.SourceStart);

                if (byStart != 0)
                {
                    return byStart;
                }

                var byPage = left.PageIndex.CompareTo(right.PageIndex);

                return byPage != 0 ? byPage : left.LineIndex.CompareTo(right.LineIndex);
            });
        }

        return index;
    }
}
