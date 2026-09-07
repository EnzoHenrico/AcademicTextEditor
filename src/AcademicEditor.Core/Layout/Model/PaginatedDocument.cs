namespace AcademicEditor.Core.Layout.Model;

/// <summary>
/// Resultado completo de uma paginação. Imutável do topo às folhas: o layout roda em background e
/// publica trocando esta referência, sem lock e sem risco de a UI ler um estado meio construído.
/// </summary>
public sealed record PaginatedDocument(IReadOnlyList<PageLayout> Pages, PageSettings Settings)
{
    /// <summary>Documento sem nenhuma página é estado inválido: há sempre ao menos uma folha.</summary>
    public static PaginatedDocument Empty(PageSettings settings) => new([new PageLayout([])], settings);
}
