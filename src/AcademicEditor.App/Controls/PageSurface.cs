using AcademicEditor.App.Rendering;
using AcademicEditor.App.ViewModels;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;

namespace AcademicEditor.App.Controls;

/// <summary>
/// A superfície onde o documento paginado é desenhado. Vive dentro de um <c>ScrollViewer</c>.
/// </summary>
/// <remarks>
/// Não é um <c>TextBox</c> nem herda de nada com noção própria de texto: o layout já vem pronto
/// do Core, e o controle só o transforma em pixels.
/// </remarks>
public sealed class PageSurface : Control
{
    private EditorViewModel? _viewModel;

    public PageSurface() => Focusable = true;

    public EditorViewModel? ViewModel
    {
        get => _viewModel;
        set
        {
            if (ReferenceEquals(_viewModel, value))
            {
                return;
            }

            // O ViewModel sobrevive ao controle. Sem tirar a inscrição, o delegate que ele guarda
            // manteria este PageSurface — e a árvore visual inteira sob ele — vivo para o GC.
            if (_viewModel is not null)
            {
                _viewModel.LayoutChanged -= OnLayoutChanged;
            }

            _viewModel = value;

            if (_viewModel is not null)
            {
                _viewModel.LayoutChanged += OnLayoutChanged;
            }

            OnLayoutChanged(this, EventArgs.Empty);
        }
    }

    public override void Render(DrawingContext context)
    {
        if (_viewModel is null)
        {
            return;
        }

        PageRenderer.Render(context, _viewModel.Paginated, Bounds.Width);
    }

    // O texto digitado vem daqui, e não de KeyDown.Key. KeyDown entrega a tecla física; este
    // evento entrega o caractere já composto pelo layout de teclado e pelo IME — é a diferença
    // entre receber "ç" e receber a tecla que, num teclado ABNT2, calha de produzi-lo.
    protected override void OnTextInput(TextInputEventArgs e)
    {
        if (_viewModel is null || string.IsNullOrEmpty(e.Text))
        {
            return;
        }

        // Algumas plataformas mandam controle por aqui (backspace, retorno). Esses são comandos,
        // tratados no KeyDown; deixá-los entrar como texto escreveria lixo no buffer.
        if (e.Text.Length == 1 && char.IsControl(e.Text[0]))
        {
            return;
        }

        _viewModel.InsertText(e.Text);
        e.Handled = true;
    }

    // Teclas que não produzem texto. Marcar Handled impede que o Enter chegue depois como "\r"
    // pelo TextInput e vire uma segunda quebra de linha.
    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (_viewModel is null)
        {
            base.OnKeyDown(e);
            return;
        }

        switch (e.Key)
        {
            case Key.Back:
                _viewModel.DeleteBackward();
                e.Handled = true;
                break;

            case Key.Delete:
                _viewModel.DeleteForward();
                e.Handled = true;
                break;

            case Key.Enter:
                _viewModel.InsertText("\n");
                e.Handled = true;
                break;

            default:
                base.OnKeyDown(e);
                break;
        }
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        Focus();
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        if (_viewModel is null)
        {
            return default;
        }

        // Devolve o tamanho da pilha inteira, não o do viewport: é assim que o ScrollViewer sabe
        // quanto há para rolar. A largura pode passar do disponível, e aí ele rola na horizontal.
        //
        // availableSize é deliberadamente ignorado: dentro de um ScrollViewer ele chega infinito
        // nas duas direções (é assim que o presenter descobre o tamanho natural do conteúdo), e
        // devolver infinito faz o Avalonia abortar o passo de layout com "Invalid size returned
        // for Measure". O tamanho desejado aqui depende só do documento.
        return PageRenderer.MeasureStack(_viewModel.Paginated);
    }

    private void OnLayoutChanged(object? sender, EventArgs e)
    {
        InvalidateMeasure();
        InvalidateVisual();
    }
}
