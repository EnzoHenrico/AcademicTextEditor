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
        //
        // De propósito a variante CRLF, que é o caso difícil: o EditorDocument normaliza na
        // entrada, então trocar por UniqueFeaturesLf tem de dar exatamente a mesma tela. Se um dia
        // não der, a regressão aparece já na primeira execução em vez de esperar um teste.
        Surface.ViewModel = new EditorViewModel(
            new AvaloniaTextMeasurer(),
            PageSettings.A4,
            Assets.Samples.Text.UniqueFeaturesCrLf);
    }
}
