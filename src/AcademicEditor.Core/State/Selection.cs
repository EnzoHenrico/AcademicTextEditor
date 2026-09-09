using AcademicEditor.Core.Layout.Model;

namespace AcademicEditor.Core.State;

/// <summary>
/// O trecho selecionado: de onde o autor começou até onde o caret está agora.
/// </summary>
/// <remarks>
/// <para>
/// <b>A âncora é um offset nu; a ponta ativa é um <see cref="Caret"/> inteiro</b> — porque ela
/// <i>é</i> o caret. É nela que a barra é desenhada, é dela que a seta seguinte parte, e é ela
/// que carrega afinidade e coluna alvo. A âncora não precisa de nenhuma das duas: ninguém
/// navega a partir dela.
/// </para>
/// <para>
/// <b>Uma, não várias.</b> Multi-cursor é ferramenta de code editor; num editor de tese a lista
/// plural custaria indireção em cada tecla, cada desenho e cada edição por algo que talvez nunca
/// venha. Está no estacionamento de ideias do roadmap, e o dia em que entrar, entra aqui.
/// </para>
/// </remarks>
public readonly record struct Selection(int Anchor, Caret Active)
{
    /// <summary>Seleção recolhida no caret: nada selecionado, e é o estado normal do editor.</summary>
    public static Selection At(Caret caret) => new(caret.Offset, caret);

    /// <summary>Nada selecionado — a âncora e o caret são o mesmo ponto.</summary>
    public bool IsEmpty => Anchor == Active.Offset;

    /// <summary>O trecho, sempre do menor offset para o maior. Arrastar para trás dá o mesmo.</summary>
    public TextRange Range =>
        new(Math.Min(Anchor, Active.Offset), Math.Abs(Active.Offset - Anchor));

    /// <summary>A mesma âncora, com o caret em outro lugar. É o que Shift+seta e arrastar fazem.</summary>
    public Selection ExtendTo(Caret caret) => this with { Active = caret };
}
