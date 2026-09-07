using AcademicEditor.App.Rendering;
using AcademicEditor.App.ViewModels;

using AcademicEditor.Core.State;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;

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
    // Meio período do piscar. 530ms é o padrão do Windows (GetCaretBlinkTime); GTK usa ~600 e o
    // macOS ~500, então qualquer um dos três passa por "normal" nas três plataformas.
    private static readonly TimeSpan BlinkInterval = TimeSpan.FromMilliseconds(530.0);

    private readonly DispatcherTimer _blinkTimer;

    private EditorViewModel? _viewModel;
    private bool _caretVisible = true;
    private CaretPosition? _scrolledTo;

    public PageSurface()
    {
        Focusable = true;

        _blinkTimer = new DispatcherTimer { Interval = BlinkInterval };
        _blinkTimer.Tick += OnBlink;
    }

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
                _viewModel.Invalidated -= OnInvalidated;
            }

            _viewModel = value;

            if (_viewModel is not null)
            {
                _viewModel.Invalidated += OnInvalidated;
            }

            OnInvalidated(this, EventArgs.Empty);
        }
    }

    public override void Render(DrawingContext context)
    {
        if (_viewModel is null)
        {
            return;
        }

        // Sem foco não há caret: a barra piscando numa janela inativa promete uma tecla que
        // iria para outro lugar.
        var caret = IsFocused && _caretVisible ? _viewModel.CaretPosition : (CaretPosition?)null;

        PageRenderer.Render(context, _viewModel.Paginated, Bounds.Width, caret);
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

            // Navegação: o controle só traduz a tecla numa chamada ao Core. Decidir para onde o
            // caret vai depende do documento paginado, que é do Core — e é lá que isso é testado.
            case Key.Left:
                _viewModel.MoveCaretLeft();
                e.Handled = true;
                break;

            case Key.Right:
                _viewModel.MoveCaretRight();
                e.Handled = true;
                break;

            case Key.Up:
                _viewModel.MoveCaretUp();
                e.Handled = true;
                break;

            case Key.Down:
                _viewModel.MoveCaretDown();
                e.Handled = true;
                break;

            case Key.Home:
                _viewModel.MoveCaretToLineStart();
                e.Handled = true;
                break;

            case Key.End:
                _viewModel.MoveCaretToLineEnd();
                e.Handled = true;
                break;

            case Key.PageUp:
                _viewModel.MoveCaretPageUp();
                e.Handled = true;
                break;

            case Key.PageDown:
                _viewModel.MoveCaretPageDown();
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

    // O temporizador em execução guarda o delegate do Tick, que guarda este PageSurface, que
    // guarda a árvore visual inteira sob ele. Parar ao desanexar é o que impede que uma janela
    // fechada continue viva — mesmo raciocínio da desinscrição do ViewModel, acima.
    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        _blinkTimer.Stop();
        base.OnDetachedFromVisualTree(e);
    }

    protected override void OnGotFocus(FocusChangedEventArgs e)
    {
        base.OnGotFocus(e);
        RestartBlink();
    }

    protected override void OnLostFocus(FocusChangedEventArgs e)
    {
        base.OnLostFocus(e);
        _blinkTimer.Stop();
        InvalidateVisual();
    }

    private void OnBlink(object? sender, EventArgs e)
    {
        _caretVisible = !_caretVisible;
        InvalidateVisual();
    }

    // Sólido, e a contagem do zero. É a regra de todo editor: quem está digitando não vê a barra
    // piscar, senão ela some justamente no caractere que se está olhando.
    private void RestartBlink()
    {
        _caretVisible = true;
        _blinkTimer.Stop();

        if (IsFocused)
        {
            _blinkTimer.Start();
        }
    }

    // Rolar depois do passo de layout, não durante: o ScrollViewer só conhece a nova extensão
    // quando o MeasureOverride já rodou — e o caso que importa é justamente o Enter que acabou de
    // criar uma folha. Daí o Post em prioridade Loaded em vez da chamada direta.
    private void BringCaretIntoView()
    {
        if (_viewModel is null)
        {
            return;
        }

        var caret = _viewModel.CaretPosition;

        // Publicação de layout que não mexeu no caret não deve arrastar a página de volta para
        // ele: quem rolou com a barra continua onde estava.
        if (_scrolledTo == caret)
        {
            return;
        }

        _scrolledTo = caret;

        Dispatcher.UIThread.Post(
            () =>
            {
                if (_viewModel is not null
                    && PageRenderer.CaretRectDip(_viewModel.Paginated, caret, Bounds.Width) is { } rect)
                {
                    // O ScrollViewer acima escuta este evento e ajusta o Offset. Levantá-lo é a
                    // forma de pedir a rolagem sem que o controle conheça quem o hospeda.
                    RaiseEvent(new RequestBringIntoViewEventArgs
                    {
                        RoutedEvent = RequestBringIntoViewEvent,
                        TargetObject = this,
                        TargetRect = rect,
                    });
                }
            },
            DispatcherPriority.Loaded);
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

    private void OnInvalidated(object? sender, EventArgs e)
    {
        RestartBlink();
        InvalidateMeasure();
        InvalidateVisual();
        BringCaretIntoView();
    }
}
