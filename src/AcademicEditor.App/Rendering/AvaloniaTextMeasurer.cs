using System.Collections.Concurrent;

using AcademicEditor.Core.Layout;
using AcademicEditor.Core.Parsing.Ast;

using Avalonia.Media;
using Avalonia.Media.TextFormatting;

namespace AcademicEditor.App.Rendering;

/// <summary>
/// Implementação de <see cref="ITextMeasurer"/> sobre o Avalonia: o outro lado da única costura
/// entre o motor de layout e o toolkit gráfico.
/// </summary>
/// <remarks>
/// <para>
/// <b>Mede com o mesmo <see cref="TextLayout"/> que o <see cref="PageRenderer"/> usa para
/// desenhar.</b> Somar avanços de glifo seria mais barato e não alocaria, mas ignora o shaping —
/// medido aqui, o mesmo texto saía 0,76% mais largo que o desenhado, o que numa linha de A4 é
/// meio caractere de deriva acumulando até passar da margem direita. Medir e desenhar precisam
/// concordar; onde não concordam, o texto vaza da folha.
/// </para>
/// <para>
/// <b>Seguro fora da UI thread</b> — verificado antes de escrever esta classe: TextLayout medindo
/// dentro de um <c>Task.Run</c> devolve exatamente o mesmo valor que na UI thread, e oito threads
/// medindo em paralelo também. É o que sustenta a decisão de rodar o layout em background.
/// </para>
/// </remarks>
public sealed class AvaloniaTextMeasurer : ITextMeasurer
{
    // Altura e baseline dependem só do estilo, nunca do texto — verificado: TextLayout devolve as
    // mesmas para "", " " e "Documento". Sem o cache, o line breaker construiria um TextLayout
    // inteiro por palavra apenas para descobrir a altura da linha, que ele já sabia.
    //
    // ConcurrentDictionary e não Dictionary: o layout roda numa thread de background e nada no
    // tipo impede que uma segunda repaginação comece antes de a anterior ser descartada.
    private readonly ConcurrentDictionary<TextStyle, LineMetrics> _lineMetrics = new();

    // Uma FontFamily por nome, não uma por chamada. ToTypeface é chamado no desenho de CADA run de
    // CADA linha visível, a cada quadro — e `new FontFamily(nome)` analisa a string e aloca. Com o
    // culling são ~150 runs por quadro; sem o cache, seriam 150 objetos de vida curtíssima na
    // geração 0 a cada repintura do caret, duas vezes por segundo, para sempre.
    //
    // ConcurrentDictionary pelo mesmo motivo do resto da classe: a medição roda em background.
    private static readonly ConcurrentDictionary<string, FontFamily> Families = new(StringComparer.Ordinal);

    /// <summary>
    /// Traduz o estilo do Core para o toolkit. É o único ponto onde essa tradução acontece:
    /// medição e desenho precisam do mesmo typeface, ou um mede o que o outro não desenha.
    /// </summary>
    /// <remarks>
    /// Família vazia é a fonte padrão do sistema — o Core não tem como saber qual é, e é aqui que
    /// isso se resolve. O nome pode trazer alternativas separadas por vírgula ("Times New Roman,
    /// Liberation Serif"): o gerenciador de fontes tenta na ordem, que é o que faz o mesmo preset
    /// sair com serifa tanto no Windows quanto no Linux.
    /// </remarks>
    public static Typeface ToTypeface(TextStyle style) => new(
        style.FontFamily.Length == 0
            ? FontFamily.Default
            : Families.GetOrAdd(style.FontFamily, static name => new FontFamily(name)),
        style.Italic ? FontStyle.Italic : FontStyle.Normal,
        style.Weight == FontWeightKind.Bold ? FontWeight.Bold : FontWeight.Normal);

    public double MeasureWidthPt(ReadOnlySpan<char> text, TextStyle style)
    {
        if (text.IsEmpty)
        {
            return 0.0;
        }

        // A string é o custo de usar o TextLayout: ele não aceita span. O ganho do span fica com
        // as implementações que não precisam dele — o medidor determinístico dos testes, e um
        // eventual medidor por avanços de glifo. Se a repaginação de documento longo (Fatia 6)
        // acusar isto como gargalo, o remédio é um cache por (texto, estilo), não trocar a API.
        using var layout = BuildLayout(new string(text), style);

        return layout.WidthIncludingTrailingWhitespace / PageRenderer.PtToDip;
    }

    public LineMetrics GetLineMetrics(TextStyle style) => _lineMetrics.GetOrAdd(style, static key =>
    {
        using var layout = BuildLayout(string.Empty, key);

        return new LineMetrics(layout.Height / PageRenderer.PtToDip, layout.Baseline / PageRenderer.PtToDip);
    });

    // Mede na mesma escala em que desenha e converte o resultado de volta para pontos, em vez de
    // medir direto em pontos. A escala do Avalonia é exatamente linear, então os dois caminhos
    // dão o mesmo número — mas este não depende disso ser verdade.
    private static TextLayout BuildLayout(string text, TextStyle style) =>
        new(text, ToTypeface(style), style.FontSizePt * PageRenderer.PtToDip, Brushes.Black);
}
