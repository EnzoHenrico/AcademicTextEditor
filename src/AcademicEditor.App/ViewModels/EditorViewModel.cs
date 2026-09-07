using AcademicEditor.Core.Layout;
using AcademicEditor.Core.Layout.Model;
using AcademicEditor.Core.Parsing;

namespace AcademicEditor.App.ViewModels;

/// <summary>
/// Estado do editor entre o motor do Core e a superfície de desenho. MVVM leve, sem framework:
/// a view liga num único evento e volta a desenhar.
/// </summary>
/// <remarks>
/// Na Fatia 2 a repaginação é síncrona, com texto fixo. O pipeline em background (debounce,
/// <c>Task.Run</c>, publicação por <c>Dispatcher.UIThread.Post</c> e descarte de layout obsoleto)
/// entra junto com a digitação, na Fatia 3 — é ela que cria a necessidade.
/// </remarks>
public sealed class EditorViewModel
{
    private readonly ITextMeasurer _measurer;
    private PageSettings _pageSettings;
    private string _source = string.Empty;

    public EditorViewModel(ITextMeasurer measurer, PageSettings pageSettings)
    {
        ArgumentNullException.ThrowIfNull(measurer);

        _measurer = measurer;
        _pageSettings = pageSettings;
        Paginated = PaginatedDocument.Empty(pageSettings);
    }

    /// <summary>Disparado quando há um novo <see cref="Paginated"/> para desenhar.</summary>
    public event EventHandler? LayoutChanged;

    /// <summary>Último layout publicado. A troca é de referência: quem está desenhando termina com o antigo, intacto.</summary>
    public PaginatedDocument Paginated { get; private set; }

    public string Source
    {
        get => _source;
        set
        {
            ArgumentNullException.ThrowIfNull(value);

            if (_source == value)
            {
                return;
            }

            _source = value;
            Repaginate();
        }
    }

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
            Repaginate();
        }
    }

    private void Repaginate()
    {
        Paginated = LayoutEngine.Layout(MarkupParser.Parse(_source), _pageSettings, _measurer);
        LayoutChanged?.Invoke(this, EventArgs.Empty);
    }
}
