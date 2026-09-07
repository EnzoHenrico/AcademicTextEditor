namespace AcademicEditor.Core.Text;

/// <summary>De qual dos dois buffers uma peça lê.</summary>
public enum PieceSource
{
    /// <summary>O texto como veio do arquivo. Imutável da abertura ao fechamento.</summary>
    Original,

    /// <summary>Tudo que foi digitado nesta sessão. Só cresce, nunca é reescrito.</summary>
    Added,
}

/// <summary>
/// Uma janela sobre um dos buffers. O documento é a concatenação das peças, na ordem da lista.
/// </summary>
/// <remarks>
/// Struct e não classe: a lista de peças é percorrida inteira a cada snapshot, e uma peça por
/// objeto no heap trocaria uma varredura sequencial de memória contígua por uma perseguição de
/// ponteiros — além de dar ao GC milhares de objetos para rastrear num documento longo.
/// </remarks>
public readonly record struct Piece(PieceSource Source, int Start, int Length);
