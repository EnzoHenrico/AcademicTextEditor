using AcademicEditor.App.Controls;
using AcademicEditor.Core.Input;

using Avalonia.Controls;
using Avalonia.Input;

namespace AcademicEditor.App.Input;

/// <summary>
/// Em que escopo o teclado está agora, olhando quem tem o foco.
/// </summary>
/// <remarks>
/// Hoje a resposta é quase sempre a mesma, porque só existe um painel. O tipo existe pela seam:
/// quando houver uma árvore de capítulos ou um campo de busca, é aqui que se decide que
/// <c>Ctrl+S</c> continua salvando e uma tecla nua não rouba o teclado de quem está escrevendo —
/// e não espalhado por dentro do despachante.
/// </remarks>
internal sealed class FocusScopeTracker(TopLevel topLevel)
{
    public ShortcutScope Current =>
        topLevel.FocusManager?.GetFocusedElement() is PageSurface
            ? ShortcutScope.Editor
            : ShortcutScope.Global;
}
