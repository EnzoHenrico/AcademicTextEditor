namespace AcademicEditor.Core.Parsing.Ast;

/// <summary>
/// Parágrafo: uma ou mais linhas consecutivas da fonte que refluem como um bloco só.
/// Cada linha vira um <see cref="InlineRun"/> — assim cada run continua contíguo no buffer.
/// </summary>
public sealed class ParagraphNode : BlockNode
{
    public ParagraphNode(
        int sourceStart,
        int sourceLength,
        IReadOnlyList<InlineRun> runs,
        TextAlignment alignment = TextAlignment.Left)
        : base(sourceStart, sourceLength, runs, alignment)
    {
    }
}
