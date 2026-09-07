using AcademicEditor.App.Rendering;
using AcademicEditor.App.ViewModels;

using AcademicEditor.Core.Layout;

using Avalonia.Controls;

namespace AcademicEditor.App.Views;

public partial class MainWindow : Window
{
    // Texto fixo até a Fatia 3 trazer o buffer editável. Serve para uma coisa só: provar que o
    // motor do Core pagina de verdade na tela — reflow das linhas, heading mais alto que o corpo
    // e quebra explícita produzindo a segunda folha.
    private const string SampleSource = """
        # Paginação em tempo real

        Este parágrafo existe para mostrar o reflow: ele é uma única linha na fonte e o motor de
        layout a quebra conforme a largura útil da página, que é a largura do papel menos as
        margens. Redimensionar a janela não muda nada aqui, porque a quebra acontece em pontos
        tipográficos sobre a geometria da folha, não sobre o tamanho da tela.

        ## Um título de segundo nível

        Títulos são mais altos que o corpo do texto, então consomem mais da altura útil da página.
        É por isso que a contagem de páginas depende do estilo de cada bloco, e não apenas da
        quantidade de caracteres do documento.

        \page

        Esta folha começou por uma quebra de página explícita no markup.
        """;

    public MainWindow()
    {
        InitializeComponent();

        Surface.ViewModel = new EditorViewModel(new AvaloniaTextMeasurer(), PageSettings.A4)
        {
            Source = SampleSource,
        };
    }
}
