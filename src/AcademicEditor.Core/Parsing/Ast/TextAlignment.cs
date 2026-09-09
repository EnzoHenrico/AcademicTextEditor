namespace AcademicEditor.Core.Parsing.Ast;

/// <summary>
/// Como as linhas de um bloco se distribuem na largura útil da folha.
/// </summary>
/// <remarks>
/// <para>
/// É propriedade de <b>bloco</b>, não de trecho: centralizar meio parágrafo não quer dizer nada.
/// Por isso vive no <see cref="BlockNode"/> e não no <see cref="TextStyle"/>, que é por caractere.
/// </para>
/// <para>
/// A marcação que a produz é a que o próprio Markdown já usa para alinhar coluna de tabela —
/// <c>:-:</c>, <c>-:</c> e <c>:-</c> —, e o dois-pontos marca o lado a que o texto se prende.
/// <b>Não há marcação para justificado</b>, e é deliberado: justificado é o padrão da norma, e o
/// estado "sem marcação" é como se volta a ele.
/// </para>
/// </remarks>
public enum TextAlignment
{
    /// <summary>Encostado à esquerda, com a direita irregular. É o que o motor faz desde o MVP.</summary>
    Left,

    Center,

    Right,

    /// <summary>
    /// As duas margens retas: a sobra da linha é distribuída entre os vãos entre palavras.
    /// </summary>
    /// <remarks>
    /// <b>A última linha de um bloco nunca é justificada</b> — ela não foi interrompida pela
    /// margem, terminou porque o parágrafo acabou. Esticá-la espalharia duas palavras pela folha
    /// inteira, que é o erro clássico de quem justifica sem essa exceção.
    /// </remarks>
    Justify,
}
