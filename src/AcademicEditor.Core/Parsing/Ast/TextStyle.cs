namespace AcademicEditor.Core.Parsing.Ast;

/// <summary>
/// Peso da fonte. Enum próprio do Core em vez de <c>Avalonia.Media.FontWeight</c>: o motor de
/// layout não pode conhecer o toolkit gráfico. A tradução acontece no <c>AvaloniaTextMeasurer</c>.
/// </summary>
public enum FontWeightKind
{
    Normal,
    Bold,
}

/// <summary>
/// Estilo tipográfico de um trecho de texto. Tamanho em <b>pontos</b> (1/72"), a unidade
/// interna do layout — a conversão para DIP só existe na camada de renderização.
/// </summary>
/// <remarks>
/// É <c>readonly record struct</c> por dois motivos: não aloca um objeto por trecho (cada
/// <see cref="InlineRun"/> carrega um), e a igualdade estrutural de graça serve como chave de cache
/// de medição no <c>ITextMeasurer</c>, onde medir duas vezes o mesmo par (texto, estilo) é
/// desperdício.
/// <para>
/// <b>O que a família custa nessa chave:</b> ela é <c>string</c>, e o hash de uma string percorre
/// os caracteres — o .NET não o guarda. São ~15 caracteres por busca no cache de medição, contra os
/// 258 mil acessos de uma repaginação completa de 300 páginas: milissegundos de um lado, 309ms do
/// outro. Vale a legibilidade; se um dia não valer, o remédio é um índice de família no lugar do
/// nome, não trocar o tipo do estilo.
/// </para>
/// </remarks>
public readonly record struct TextStyle(
    string FontFamily,
    double FontSizePt,
    FontWeightKind Weight,
    bool Italic,
    bool Superscript = false)
{
    /// <summary>Corpo de um sobrescrito, como fração do corpo que o envolve.</summary>
    public const double SuperscriptScale = 0.65;

    /// <summary>Quanto a baseline de um sobrescrito sobe, como fração do corpo <b>dele</b>.</summary>
    /// <remarks>
    /// Duas frações em vez de uma tabela de estilos porque é o mínimo que resolve, e porque as duas
    /// precisam ser lidas em três lugares — o parser, que decide o corpo, e os <b>dois</b>
    /// renderizadores, que levantam a baseline. Uma constante em cada lugar seria uma para divergir
    /// das outras, e o sintoma seria a chamada de nota fora de lugar só no PDF, ou só na tela.
    /// </remarks>
    public const double SuperscriptRise = 0.5;

    /// <summary>Família vazia: a fonte padrão do sistema.</summary>
    /// <remarks>
    /// O Core não tem como saber qual é — quem resolve é o <c>AvaloniaTextMeasurer</c>, no único
    /// ponto em que o estilo vira typeface do toolkit. Vazio em vez de <c>null</c> para que a
    /// comparação de estilos, que é chave de cache, não carregue um caso a mais.
    /// </remarks>
    public const string DefaultFontFamily = "";

    /// <summary>Estilo de referência do motor: corpo 11pt na fonte do sistema.</summary>
    /// <remarks>
    /// <b>Não é o corpo do documento</b> — esse vem do <c>TypographyPreset</c>, e é ele que a
    /// paginação usa. Este é o ponto fixo dos testes, e um teste guarda que os dois não divirjam
    /// sem querer.
    /// </remarks>
    public static readonly TextStyle Body = new(DefaultFontFamily, 11.0, FontWeightKind.Normal, Italic: false);

    /// <summary>Este estilo em versão sobrescrita: menor, e com a baseline levantada.</summary>
    public TextStyle AsSuperscript() => this with
    {
        FontSizePt = FontSizePt * SuperscriptScale,
        Superscript = true,
    };

    /// <summary>Quanto a baseline sobe, em pontos. Zero quando o estilo não é sobrescrito.</summary>
    /// <remarks>
    /// <para>
    /// Sai daqui, e não de cada renderizador, para que a tela e o PDF levantem pelo mesmo tanto.
    /// A largura não muda — só a altura em que o glifo é assentado —, então nada do caret, da
    /// seleção ou do line breaker precisa saber que este run é sobrescrito.
    /// </para>
    /// <para>
    /// <b>O que se aceita:</b> o motor de layout mede a altura da linha pelo corpo do sobrescrito,
    /// que é menor, e não sabe da subida. Num corpo 12 o topo do glifo passa ~0,75pt acima da
    /// ascendente do texto que o envolve — dentro da folga do entrelinhamento de 1,5, e por isso
    /// não vale um campo a mais no <c>LineMetrics</c>. Com entrelinhamento simples, encosta.
    /// </para>
    /// </remarks>
    public double BaselineRisePt => Superscript ? FontSizePt * SuperscriptRise : 0.0;
}
