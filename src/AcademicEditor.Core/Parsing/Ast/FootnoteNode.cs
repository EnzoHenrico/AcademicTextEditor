namespace AcademicEditor.Core.Parsing.Ast;

/// <summary>Onde uma nota é chamada no texto.</summary>
/// <remarks>
/// Sai do parser junto com os runs porque é ele quem já reconhece o <c>[^id]</c>. Reconstruir isso
/// depois exigiria varrer os runs de todo bloco a cada repaginação, procurando um padrão de três
/// runs — trabalho por linha para uma coisa que é rara por documento.
/// </remarks>
/// <param name="SourceStart">Offset do <c>[</c> da chamada, no buffer.</param>
public readonly record struct FootnoteCall(int SourceStart, string Id);

/// <summary>
/// Definição de nota de rodapé: <c>[^id]: texto</c>, sozinha numa linha.
/// </summary>
/// <remarks>
/// <para>
/// <b>É um bloco como qualquer outro, e é o layout que decide onde ele aparece.</b> Chamada em
/// algum lugar do texto, a nota é desenhada no pé da folha da chamada; <b>nunca chamada, continua
/// sendo parágrafo comum</b>, no lugar em que foi escrita — nenhum texto pode sumir da tela por
/// causa de um identificador que ninguém referenciou.
/// </para>
/// <para>
/// O identificador <b>não</b> é marcação: os colchetes somem quando o caret sai do bloco, mas o
/// <c>id</c> fica, sobrescrito. É o número que o leitor procura no pé da folha para saber a que
/// chamada aquela nota responde — escondê-lo junto com a pontuação deixaria a nota órfã.
/// </para>
/// </remarks>
public sealed class FootnoteNode : BlockNode
{
    public FootnoteNode(
        string id,
        int sourceStart,
        int sourceLength,
        IReadOnlyList<InlineRun> runs,
        TextAlignment alignment = TextAlignment.Left)
        : base(sourceStart, sourceLength, runs, alignment) => Id = id;

    /// <summary>O rótulo entre <c>[^</c> e <c>]</c>. É o que casa a definição com a chamada.</summary>
    public string Id { get; }
}
