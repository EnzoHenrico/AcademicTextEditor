namespace AcademicEditor.Core.Layout.Model;

/// <summary>
/// Uma página: as linhas que couberam nela, já posicionadas em relação ao topo da área de conteúdo.
/// </summary>
public sealed record PageLayout(IReadOnlyList<LaidOutLine> Lines);
