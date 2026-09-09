using AcademicEditor.App.Rendering;
using AcademicEditor.App.ViewModels;

using AcademicEditor.Core.Layout;
using AcademicEditor.Core.State;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;

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

    // Dois cursores construídos uma vez, não um por movimento do mouse: OnPointerMoved dispara
    // dezenas de vezes por segundo, e cada Cursor novo é um recurso do sistema gráfico.
    private static readonly Cursor TextCursor = new(StandardCursorType.Ibeam);
    private static readonly Cursor ArrowCursor = new(StandardCursorType.Arrow);

    private readonly DispatcherTimer _blinkTimer;

    private EditorViewModel? _viewModel;
    private ScrollViewer? _scroller;
    private bool _caretVisible = true;
    private bool _dragging;
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

        PageRenderer.Render(
            context,
            _viewModel.Paginated,
            Bounds.Width,
            caret,
            Viewport,
            _viewModel.SelectionRects);
    }

    /// <summary>
    /// Retângulo visível, nas coordenadas desta superfície — <c>null</c> quando ela não vive
    /// dentro de um <c>ScrollViewer</c>, e aí desenha-se tudo.
    /// </summary>
    /// <remarks>
    /// A superfície é o conteúdo direto do <c>ScrollViewer</c>, então a conta é o deslocamento
    /// dele mais o tamanho da janela de rolagem — sem transformar coordenada nenhuma.
    /// </remarks>
    private Rect? Viewport => _scroller is { } scroller
        ? new Rect(scroller.Offset.X, scroller.Offset.Y, scroller.Viewport.Width, scroller.Viewport.Height)
        : null;

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

        var extend = e.KeyModifiers.HasFlag(KeyModifiers.Shift);

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
                _viewModel.InsertLineBreak();
                e.Handled = true;
                break;

            // Navegação: o controle só traduz a tecla numa chamada ao Core. Decidir para onde o
            // caret vai depende do documento paginado, que é do Core — e é lá que isso é testado.
            //
            // Shift não é um movimento diferente: é o mesmo movimento sem recolher a âncora. Por
            // isso ele entra como parâmetro, e não como oito casos a mais neste switch.
            case Key.Left:
                _viewModel.MoveCaretLeft(extend);
                e.Handled = true;
                break;

            case Key.Right:
                _viewModel.MoveCaretRight(extend);
                e.Handled = true;
                break;

            case Key.Up:
                _viewModel.MoveCaretUp(extend);
                e.Handled = true;
                break;

            case Key.Down:
                _viewModel.MoveCaretDown(extend);
                e.Handled = true;
                break;

            case Key.Home:
                _viewModel.MoveCaretToLineStart(extend);
                e.Handled = true;
                break;

            case Key.End:
                _viewModel.MoveCaretToLineEnd(extend);
                e.Handled = true;
                break;

            case Key.PageUp:
                _viewModel.MoveCaretPageUp(extend);
                e.Handled = true;
                break;

            case Key.PageDown:
                _viewModel.MoveCaretPageDown(extend);
                e.Handled = true;
                break;

            default:
                base.OnKeyDown(e);
                break;
        }
    }

    // O clique é a segunda forma de mover o caret, ao lado das setas — e, como elas, o controle
    // só traduz o evento numa chamada ao Core: decidir onde o caret pousa depende do documento
    // paginado, e é lá que isso é testado.
    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);

        if (_viewModel is null || !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            return;
        }

        // Clicar no texto tem de dar o foco de volta: sem isto, um clique depois de mexer na
        // barra de rolagem punha o caret sem que a tecla seguinte chegasse aqui.
        Focus();

        if (PageRenderer.HitTest(_viewModel.Paginated, e.GetPosition(this), Bounds.Width) is not { } hit)
        {
            return;
        }

        // Um clique põe o caret, dois pegam a palavra, três a linha visual. Shift no clique
        // estende a seleção a partir da âncora, como em qualquer editor.
        switch (e.ClickCount)
        {
            case 1:
                _viewModel.PlaceCaretAt(
                    hit.PageIndex, hit.XPt, hit.YPt, e.KeyModifiers.HasFlag(KeyModifiers.Shift));
                break;

            case 2:
                _viewModel.SelectWordAt(hit.PageIndex, hit.XPt, hit.YPt);
                break;

            default:
                _viewModel.SelectLineAt(hit.PageIndex, hit.XPt, hit.YPt);
                break;
        }

        // Captura para que o arrasto continue chegando aqui mesmo quando o ponteiro sai da
        // superfície — soltar o botão fora da janela tem de terminar a seleção, não abandoná-la.
        _dragging = true;
        e.Pointer.Capture(this);
        e.Handled = true;
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);

        if (_dragging)
        {
            _dragging = false;
            e.Pointer.Capture(null);
        }
    }

    // Cursor por região: barra de texto sobre o papel, seta sobre a margem e sobre o vão entre
    // folhas. Custa uma divisão e duas subtrações por movimento — nenhuma medição de texto — e dá
    // ao autor uma leitura visual de onde a margem está.
    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);

        if (_viewModel is null
            || PageRenderer.HitTest(_viewModel.Paginated, e.GetPosition(this), Bounds.Width) is not { } hit)
        {
            return;
        }

        Cursor = IsOverContent(hit, _viewModel.Paginated.Settings) ? TextCursor : ArrowCursor;

        // Arrastando: o ponto vira a ponta ativa da seleção, e a âncora fica onde o botão desceu.
        if (_dragging)
        {
            _viewModel.PlaceCaretAt(hit.PageIndex, hit.XPt, hit.YPt, extend: true);
        }
    }

    /// <summary>O ponto está dentro da área de conteúdo da folha, e não na margem nem no vão?</summary>
    /// <remarks>
    /// Lê o resultado <b>cru</b> do <c>HitTest</c>, que não grampeia: é justamente o sinal fora do
    /// intervalo que distingue o papel da margem. Grampear lá tornaria esta pergunta impossível de
    /// responder sem refazer a conta.
    /// </remarks>
    private static bool IsOverContent((int PageIndex, double XPt, double YPt) hit, PageSettings settings) =>
        hit.XPt >= 0.0
        && hit.XPt <= settings.ContentWidthPt
        && hit.YPt >= 0.0
        && hit.YPt <= settings.ContentHeightPt;

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);

        _scroller = this.FindAncestorOfType<ScrollViewer>();

        if (_scroller is not null)
        {
            // Rolar não chama Render por conta própria: o Avalonia translada o que já foi
            // desenhado, sem pedir um quadro novo. Isso era invisível enquanto o desenho cobria a
            // pilha inteira; com o culling, seria folha em branco atrás da rolagem.
            _scroller.PropertyChanged += OnScrollerPropertyChanged;
        }

        Focus();
    }

    // O temporizador em execução guarda o delegate do Tick, que guarda este PageSurface, que
    // guarda a árvore visual inteira sob ele. Parar ao desanexar é o que impede que uma janela
    // fechada continue viva — mesmo raciocínio da desinscrição do ViewModel, acima.
    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        _blinkTimer.Stop();
        _dragging = false;

        if (_scroller is not null)
        {
            _scroller.PropertyChanged -= OnScrollerPropertyChanged;
            _scroller = null;
        }

        base.OnDetachedFromVisualTree(e);
    }

    // O deslocamento mudou: outras folhas entraram no quadro, e o quadro precisa ser refeito.
    private void OnScrollerPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property == ScrollViewer.OffsetProperty)
        {
            InvalidateVisual();
        }
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
