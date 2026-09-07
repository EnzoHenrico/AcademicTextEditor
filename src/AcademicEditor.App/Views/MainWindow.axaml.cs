using AcademicEditor.App.Rendering;
using AcademicEditor.App.ViewModels;

using AcademicEditor.Core.Layout;

using Avalonia.Controls;

namespace AcademicEditor.App.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();

        // O conteúdo inicial continua fixo porque abrir arquivo é da Fatia 5. Daqui em diante o
        // documento é editável: digitar reflui a paginação.
        Surface.ViewModel = new EditorViewModel(
            new AvaloniaTextMeasurer(),
            PageSettings.A4,
            Assets.Samples.Text.UniqueFeatures);
    }
}
