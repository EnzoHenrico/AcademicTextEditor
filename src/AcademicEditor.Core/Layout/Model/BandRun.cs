using AcademicEditor.Core.Parsing.Ast;

namespace AcademicEditor.Core.Layout.Model;

/// <summary>
/// Um trecho de cabeçalho ou rodapé, já medido e posicionado na folha.
/// </summary>
/// <remarks>
/// <para>
/// <b>Não tem <c>SourceStart</c>, e é para isso que este tipo existe</b> em vez de reusar o
/// <see cref="LaidOutRun"/>. Cabeçalho e rodapé não saem do buffer: não há offset onde o caret
/// possa pousar, nem texto que a seleção possa pegar. Um tipo que carregasse um offset falso
/// mais cedo ou mais tarde seria consumido como se fosse conteúdo — e uma falha de offset não
/// quebra o desenho, quebra o caret, longe de onde nasceu.
/// </para>
/// <para>
/// Pelo mesmo motivo as faixas moram em campos próprios do <see cref="PageLayout"/> e nunca em
/// <see cref="PageLayout.Lines"/>: <c>CaretGeometry.FindLine</c> varre exatamente aquela lista
/// mapeando offset para linha, e <c>LayoutEngine</c> exige que ela case um-para-um com os blocos
/// do documento para poder reaproveitá-la.
/// </para>
/// </remarks>
/// <param name="XPt">
/// Distância do início da área de conteúdo — a mesma origem horizontal das linhas de texto, que é
/// o que faz a faixa alinhar com o bloco de texto abaixo dela.
/// </param>
/// <param name="BaselinePt">
/// Distância do <b>topo da folha</b>, e não da área de conteúdo: a faixa vive fora dela, onde um
/// Y relativo ao conteúdo seria negativo. Sai calculada daqui para que o renderizador não refaça a
/// soma de margem e reserva e divirja dela depois.
/// </param>
public readonly record struct BandRun(string Text, TextStyle Style, double XPt, double BaselinePt);
