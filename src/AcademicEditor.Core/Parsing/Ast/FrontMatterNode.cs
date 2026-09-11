namespace AcademicEditor.Core.Parsing.Ast;

/// <summary>
/// O cabeçalho de metadados do topo do arquivo (<c>---</c> … <c>---</c>). Não tem texto a desenhar:
/// ele existe no buffer e <b>nunca</b> aparece na folha.
/// </summary>
/// <remarks>
/// <b>Cobre o trecho mesmo sem desenhá-lo</b>, e é essa a razão de ele ser um bloco em vez de um
/// pedaço da fonte que o parser descarta. O mapa <c>offset → linha</c> tem de ser total: se os
/// offsets do cabeçalho não pertencessem a linha nenhuma, a invariante que a Fatia 5a.1 provou com
/// teste de propriedade passaria a valer só para o corpo — e ela custou caro justamente onde foi
/// enfraquecida antes.
/// </remarks>
public sealed class FrontMatterNode : BlockNode
{
    public FrontMatterNode(int sourceStart, int sourceLength)
        : base(sourceStart, sourceLength, [])
    {
    }
}
