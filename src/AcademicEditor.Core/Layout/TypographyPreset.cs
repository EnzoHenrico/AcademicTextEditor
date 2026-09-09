using AcademicEditor.Core.Parsing.Ast;

namespace AcademicEditor.Core.Layout;

/// <summary>Corpo dos títulos de nível 1 a 6, em pontos.</summary>
/// <remarks>
/// Seis campos, e não uma lista, porque o preset é comparado por valor no reaproveitamento de
/// layout: um <c>IReadOnlyList&lt;double&gt;</c> dentro de um record compara por referência. Dois
/// presets iguais que comparassem diferente repaginariam do zero à toa; dois diferentes que
/// comparassem iguais reaproveitariam linhas medidas em outra fonte — e é esse o erro caro, porque
/// não quebra o desenho, quebra o caret.
/// </remarks>
public readonly record struct HeadingSizes(
    double Level1,
    double Level2,
    double Level3,
    double Level4,
    double Level5,
    double Level6)
{
    /// <summary>O corpo do título de nível <paramref name="level"/>, de 1 a 6.</summary>
    public double this[int level] => level switch
    {
        1 => Level1,
        2 => Level2,
        3 => Level3,
        4 => Level4,
        5 => Level5,
        6 => Level6,
        _ => throw new ArgumentOutOfRangeException(nameof(level), level, "Nível de título fora de 1..6."),
    };
}

/// <summary>
/// A norma tipográfica do documento: com que fonte, em que corpo e com que entrelinhamento ele é
/// composto.
/// </summary>
/// <remarks>
/// <para>
/// <b>É a outra metade da norma</b>, ao lado de <see cref="PageSettings"/>: aquela é a geometria da
/// folha, esta é a tipografia do que cai dentro dela. Moram juntas por isso — e é por isso que o
/// <c>MarkupParser</c> a consulta, apesar de o parser vir antes do layout no pipeline: resolver o
/// corpo de um <c>## </c> é decisão de norma, não de quem lê a marcação.
/// </para>
/// <para>
/// <b>Não há espaço automático entre blocos, e é decisão.</b> Neste editor uma linha da fonte é uma
/// linha na página: o espaço entre um parágrafo e o seguinte é a linha em branco que o autor
/// digitou, e ela cresce com o entrelinhamento como qualquer outra linha. Um <c>SpaceAfterPt</c>
/// por bloco somaria por cima do que já está escrito, e o autor veria um espaço que não digitou —
/// que é exatamente o que este editor promete não fazer.
/// </para>
/// </remarks>
/// <param name="FontFamily">
/// Vazia significa a fonte padrão do sistema — ver <see cref="TextStyle.DefaultFontFamily"/>.
/// </param>
/// <param name="LineSpacing">
/// Multiplicador da altura natural da linha: 1,0 é entrelinhamento simples, 1,5 é o da ABNT.
/// </param>
/// <param name="Alignment">
/// O alinhamento de um bloco <b>sem marcação</b>. É por isso que não existe marcação para
/// justificado: com o preset da norma, "sem marcação" já é justificado, e a marcação serve para
/// sair dele.
/// </param>
public sealed record TypographyPreset(
    string FontFamily,
    double BodySizePt,
    HeadingSizes HeadingSizesPt,
    double LineSpacing,
    TextAlignment Alignment)
{
    /// <summary>O preset do MVP: fonte do sistema, corpo 11pt, entrelinhamento simples.</summary>
    /// <remarks>
    /// É o que o motor usa quando ninguém escolhe outro, e é o que mantém as medidas dos testes de
    /// layout em números redondos. O aplicativo usa o <see cref="Abnt"/>.
    /// </remarks>
    public static TypographyPreset Default { get; } = new(
        TextStyle.DefaultFontFamily,
        11.0,
        new HeadingSizes(20.0, 17.0, 14.0, 12.0, 11.0, 11.0),
        LineSpacing: 1.0,
        TextAlignment.Left);

    /// <summary>ABNT: Times New Roman 12pt, entrelinhamento 1,5.</summary>
    /// <remarks>
    /// <para>
    /// A norma não fixa o corpo dos títulos — exige que se distingam do texto. A escala aqui é a do
    /// MVP com os dois últimos níveis corrigidos: com corpo 12, um H5 de 11pt sairia <i>menor</i>
    /// que o texto que ele titula.
    /// </para>
    /// <para>
    /// <b>Três nomes, e os três são precisos.</b> A maioria das distribuições Linux não traz a
    /// fonte da Microsoft; a Liberation Serif é metricamente compatível com ela e vem na maior
    /// parte delas; e a DejaVu Serif fecha o caso mínimo — foi o que se achou numa instalação WSL
    /// enxuta, que não tinha nenhuma das duas primeiras. Sem a terceira, o documento sairia ali em
    /// fonte <i>sem serifa</i> e nada na tela diria por quê. A DejaVu não tem as métricas do Times,
    /// então a quebra de linha muda — o que não muda é o documento continuar parecendo uma tese.
    /// </para>
    /// </remarks>
    public static TypographyPreset Abnt { get; } = new(
        "Times New Roman, Liberation Serif, DejaVu Serif",
        12.0,
        new HeadingSizes(20.0, 17.0, 14.0, 13.0, 12.0, 12.0),
        LineSpacing: 1.5,
        TextAlignment.Justify);

    /// <summary>Estilo do corpo de texto.</summary>
    public TextStyle Body => new(FontFamily, BodySizePt, FontWeightKind.Normal, Italic: false);

    /// <summary>Estilo do título de nível <paramref name="level"/>, de 1 a 6.</summary>
    public TextStyle Heading(int level) =>
        new(FontFamily, HeadingSizesPt[level], FontWeightKind.Bold, Italic: false);

    /// <summary>As métricas naturais da fonte, com o entrelinhamento da norma já aplicado.</summary>
    /// <remarks>
    /// <b>A folga extra fica acima da linha</b>, como no Word: a baseline desce junto com a altura,
    /// então o que aparece entre duas linhas é espaço, e não texto deslocado dentro da própria
    /// caixa. Consequência aceita: caret e retângulo de seleção ocupam a caixa inteira — que é o
    /// que se quer na seleção, linhas seguidas destacadas sem buraco entre elas.
    /// </remarks>
    public LineMetrics Apply(LineMetrics natural) => new(
        natural.HeightPt * LineSpacing,
        natural.BaselinePt + (natural.HeightPt * (LineSpacing - 1.0)));
}
