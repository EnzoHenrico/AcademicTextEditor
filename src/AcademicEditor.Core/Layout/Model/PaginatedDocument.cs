namespace AcademicEditor.Core.Layout.Model;

/// <summary>Trecho do buffer, em caracteres UTF-16.</summary>
public readonly record struct TextRange(int Start, int Length)
{
    public static readonly TextRange Empty = new(-1, 0);

    public int End => Start + Length;

    public bool Contains(int offset) => Start >= 0 && offset >= Start && offset <= End;
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

    /// <summary>Documento sem nenhuma página é estado inválido: há sempre ao menos uma folha.</summary>
    public static PaginatedDocument Empty(PageSettings settings, TypographyPreset? typography = null) =>
        new([new PageLayout([])], settings) { Typography = typography ?? TypographyPreset.Default };
}
