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
/// É a entrada do índice do documento: guarda coordenadas, não a linha, para que o índice seja um
/// array de valores contíguo — dezesseis mil deles numa tese — em vez de uma segunda lista de
/// referências para os mesmos objetos.
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

    /// <summary>Todas as linhas do documento, <b>em ordem de fonte</b>.</summary>
    /// <remarks>
    /// <para>
    /// <b>É a separação entre duas ordens que até aqui eram a mesma.</b> <c>PageLayout.Lines</c>
    /// está em ordem de <i>desenho</i> — de cima para baixo na folha —, e este índice está em ordem
    /// de <i>fonte</i>. Enquanto tudo que se desenha vem do fluxo do texto as duas coincidem; uma
    /// nota de rodapé desenhada no pé da folha onde está a chamada, com a definição escrita no fim
    /// do arquivo, é o primeiro caso em que elas divergem.
    /// </para>
    /// <para>
    /// Quem precisa de "qual linha contém este offset" usa este índice, e a resposta sai por busca
    /// binária — o que era varredura linear com saída antecipada, e o que o roadmap já previa desde
    /// a Fatia 4 da Fase 3. Quem precisa de "qual linha vem abaixo desta" continua andando pelas
    /// folhas, porque ali a pergunta é mesmo visual.
    /// </para>
    /// <para>
    /// Construído aqui, e não pelo chamador, porque esquecer de construí-lo não daria erro: daria
    /// um caret que não acha onde pousar. <b>Cuidado com <c>with { Pages = ... }</c></b> — a cópia
    /// de record não refaz este cálculo, e só é segura quando as linhas não mudaram (é o caso do
    /// <c>PageBands</c>, que só acrescenta faixas).
    /// </para>
    /// </remarks>
    public IReadOnlyList<LineRef> Index { get; } = BuildIndex(Pages);

    /// <summary>Documento sem nenhuma página é estado inválido: há sempre ao menos uma folha.</summary>
    public static PaginatedDocument Empty(PageSettings settings, TypographyPreset? typography = null) =>
        new([new PageLayout([])], settings) { Typography = typography ?? TypographyPreset.Default };

    /// <remarks>
    /// A ordenação só acontece quando a ordem de desenho <b>não</b> é a de fonte, que é o caso raro:
    /// o passeio já sai ordenado num documento sem nota de rodapé, e conferir isso custa uma
    /// comparação por linha. O desempate é a posição na folha, para que linhas que dividem o mesmo
    /// offset — as vazias — mantenham a ordem em que foram assentadas.
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
