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

    /// <summary>Documento sem nenhuma página é estado inválido: há sempre ao menos uma folha.</summary>
    public static PaginatedDocument Empty(PageSettings settings) => new([new PageLayout([])], settings);
}
