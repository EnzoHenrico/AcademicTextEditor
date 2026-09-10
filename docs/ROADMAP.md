# Roadmap — AcademicEditor

Documento vivo: marque os checkboxes conforme avança e mova o status da fase.
Contexto do produto em [`context.md`](context.md); padrão de diretórios em
[`Directories.md`](Directories.md); regras de arquitetura e estilo em
[`CLAUDE.md`](../CLAUDE.md).

**Status:** `✅ concluída` · `🔨 em andamento` · `⬜ planejada`

**Critério de conclusão de qualquer item:** `./dev.sh check` verde (guarda de arquitetura +
build sem warnings + testes). Um item não é dado como pronto com o gate vermelho.

---

## Fase 0 — Scaffold + Git ✅

Commit `c6ab249`.

- [x] Solução `.slnx` com Core (sem UI), App (Avalonia) e Tests
- [x] Referências ligadas: App → Core, Tests → Core
- [x] `Directory.Build.props` (net10.0, Nullable, ImplicitUsings, TreatWarningsAsErrors)
- [x] `.gitignore`, `.editorconfig`, `CLAUDE.md`
- [x] Repositório na branch `main`
- [x] Validado: Avalonia 12.1.2 compila em net10.0 sem warnings

---

## Fase 1 — Tooling de build e gate de qualidade ✅

Commit `55f1ed6`.

- [x] `dev.sh` local (não versionado) com `check` / `build` / `test` / `run` / `publish` / `clean` / `install-hooks`
- [x] Guarda de arquitetura em dois níveis (PackageReference direto + vazamento transitivo)
- [x] Hook `pre-commit` bloqueando commits sem build e testes verdes
- [x] `do_test` falha quando nenhum teste é descoberto (`dotnet test` retorna 0 nesse caso)
- [x] `ArchitectureTests` — mesma regra verificada no assembly compilado
- [x] Testes negativos do gate executados: violação de arquitetura e erro de compilação, ambos bloqueando commit
- [x] `publish` gera executável self-contained que roda sem o SDK
- [x] `publish` multiplataforma: `linux-x64` e `win-x64` num comando, com o formato do binário
      conferido pelos bytes mágicos — `dotnet publish` devolve 0 sem checar isso
- [x] `dev.sh` passou a ser versionado: gerar release é decisão do projeto, não da máquina

---

## Fase 2 — Reestruturação de diretórios ✅

Padrão definido em [`Directories.md`](Directories.md). Feito antes do MVP porque mover pastas
com o Core vazio é trivial; depois de 40 arquivos de motor, é refatoração cara.

- [x] `docs/` com `context.md`, `ROADMAP.md`, `Directories.md` (`git mv`, histórico preservado)
- [x] Pastas do Core: `Text/`, `Parsing/Ast/`, `Layout/Model/`, `IO/`, `Input/`, `State/`
- [x] Pastas do App: `Views/`, `ViewModels/`, `Controls/`, `Rendering/`, `Input/`, `Assets/`
- [x] `MainWindow` movida para `Views/` (`x:Class`, namespace e `App.axaml.cs` alinhados)
- [x] `Directories.md` revisado: `Layout/` e `Rendering/` acrescentados, `Interfaces/` descartada
- [x] Guarda de namespaces no `dev.sh` — pasta = namespace, barrado no `check` e no `pre-commit`
- [x] Teste negativo: namespace errado falha o gate com mensagem do que corrigir

---

## Fase 3 — MVP ✅

**Objetivo:** abrir um `.md`, editar, ver a paginação recalcular, salvar, reabrir e obter o
mesmo conteúdo. É aqui que o diferencial do produto (paginação como layout) fica de pé.

A fase é atacada em **fatias verticais**, não camada por camada. O motivo é o risco: o
`ITextMeasurer` é implementado com Avalonia mas chamado de dentro do `LayoutEngine`, que roda
em `Task.Run`. Se as APIs de medição do Avalonia exigirem UI thread, a decisão "layout roda em
background" cai — e numa ordem horizontal isso só apareceria com o motor inteiro já escrito
em cima dela. A Fatia 2 põe texto na tela cedo justamente para resolver isso enquanto o motor
ainda é pequeno.

### Fatia 1 — Motor de layout no Core, sem UI ✅

Markup (subconjunto mínimo):
- [x] AST: `TextStyle` (enum de peso próprio do Core), `InlineRun` (Text, SourceStart, Style),
      `ParagraphNode`, `HeadingNode`, `PageBreakNode`, `DocumentNode`
- [x] `MarkupTokenizer` + `MarkupParser` — parágrafo, `#`..`######`, quebra explícita;
      cada bloco emite **um** `InlineRun`
- [x] `MarkupParserTests`: tabela markup → AST esperado

Layout/paginação:
- [x] `PageSettings` (A4/Letter + margens, em pontos; `ContentWidthPt`/`ContentHeightPt`;
      `HeaderReservedHeightPt`/`FooterReservedHeightPt` já no tipo, 0 no MVP)
- [x] `ITextMeasurer` no Core: `MeasureWidthPt(ReadOnlySpan<char>, TextStyle)` +
      `GetLineMetrics(TextStyle)` — `Span` para o line breaker medir fatias sem alocar
- [x] `LineBreaker` greedy word-wrap sobre a sequência de runs do bloco
- [x] `PageBreaker` empilhando linhas até `ContentHeightPt`, respeitando `PageBreakNode`
- [x] `LayoutEngine` produzindo `PaginatedDocument` imutável
- [x] Model imutável: `LaidOutRun`, `LaidOutLine` (com `SourceStart`/`SourceLength`),
      `PageLayout`, `PaginatedDocument`
- [x] `LineBreakerTests`/`PageBreakerTests` com `FakeTextMeasurer` determinístico
- [x] Casos de borda: palavra mais larga que a página, parágrafo vazio, quebra explícita,
      documento vazio produzindo 1 página (não 0)

### Fatia 2 — Página A4 na tela ✅

- [x] **Spike:** medição do Avalonia dentro de um `Task.Run`. **O risco não se materializou:**
      `TextLayout`, `FormattedText` e a soma de avanços de glifo funcionam fora da UI thread e
      devolvem exatamente o mesmo valor que nela, inclusive com 8 threads medindo em paralelo.
      A decisão "layout roda em background" fica de pé como está
- [x] `AvaloniaTextMeasurer` em `Rendering/`, medindo com o **mesmo `TextLayout` que desenha**.
      Avanços de glifo seriam mais baratos (180ms/20k medições e 2,7MB contra 251ms e 50,5MB),
      mas ignoram o shaping: mediram o mesmo texto 0,76% mais largo que o desenhado, meio
      caractere de deriva por linha de A4 até vazar a margem. `FormattedText` foi descartado
      por ser 4,6× mais lento que `TextLayout` sem vantagem nenhuma
- [x] Altura e baseline dependem só do estilo, nunca do texto — verificado — e ficam num
      `ConcurrentDictionary` por estilo
- [x] `PageRenderer` desenhando as folhas com espaçamento visual entre elas
- [x] Conversão pt → DIP numa constante única do `PageRenderer`
- [x] `PageSurface : Control` dentro de `ScrollViewer`; `Render` só desenha; `MeasureOverride`
      devolve a altura total da pilha
- [x] `EditorViewModel` (MVVM leve, sem framework) guardando o `PaginatedDocument` corrente
- [x] Critério: `./dev.sh run` mostra folha A4 com margens e texto fixo já paginado

Duas coisas que a fatia expôs e ficam registradas:

- **Falta espaçamento entre blocos.** As linhas saem com o entrelinhamento natural da fonte e
  nada separa um parágrafo do seguinte, então um heading encosta no corpo. Não é bug do motor:
  `BlockNode` simplesmente não tem `SpaceBeforePt`/`SpaceAfterPt`, e entrelinhamento de 1,5 é
  parte do preset de norma — que, quando a Fase 5 mudou de assunto, foi parar na Fase 6,
  Fatia 1. Entra junto com ele
- **O bug que só o app real pegou:** dentro de um `ScrollViewer` o `availableSize` do
  `MeasureOverride` chega infinito, e devolvê-lo aborta o passo de layout. Nenhum teste do Core
  pegaria isso — é o primeiro argumento concreto a favor do `tests/AcademicEditor.App.Tests/`
  previsto para a Fase 4

### Fatia 3 — Buffer editável e digitação ✅

- [x] `Piece` (`readonly struct`: Source, Start, Length) e `PieceTable` com `Insert`/`Delete`
- [x] Caminho rápido da digitação: escrever no fim estica a última peça em vez de criar outra —
      digitar N caracteres seguidos não faz a lista de peças crescer N vezes
- [x] `TextBufferSnapshot` imutável, consumido pelo layout sem travar a digitação. Não copia
      texto: o buffer de adição só recebe escrita além do comprimento registrado, e quando
      precisa crescer é realocado, deixando o array anterior íntegro para quem o segurava —
      é o que dispensa lock entre a digitação e o layout
- [x] `EditorDocument` orquestrando buffer + evento de mudança (`TextEdit`)
- [x] `PieceTableTests`: insert início/meio/fim, delete dentro/cruzando/consumindo pieces,
      snapshot estável a edições posteriores e ao crescimento do buffer
- [x] Teste diferencial de stress: 3 seeds × 2.000 edições aleatórias vs. `StringBuilder`
- [x] Texto digitado vindo do evento `TextInput` (não `KeyDown.Key`), por IME e layouts
      internacionais; Backspace/Delete/Enter tratados no `KeyDown`, que é onde comando mora
- [x] Backspace e Delete respeitam par substituto: apagam o caractere, não meio code point
- [x] Recompute em background (`Task.Run`) + publicação via `Dispatcher.UIThread.Post` +
      debounce de 50ms; layout obsoleto cancelado por `CancellationToken` (o `LayoutEngine`
      verifica entre blocos) e número de geração impedindo publicação fora de ordem

Registrado desta fatia:

- **O ponto de inserção ainda não é um caret.** É um offset que anda para frente conforme se
  digita, e o texto entra no fim do documento. Vira o `Caret` do Core na Fatia 4 — é a fatia
  seguinte justamente porque navegar exige consultar o `PaginatedDocument`
- **O debounce de 50ms é chute informado**, não medição. O número definitivo sai do documento
  de ~300 páginas da Fatia 6

### Fatia 4 — Caret e navegação (no Core) ✅

- [x] `Caret` em `State/`: `Offset` no buffer + `DesiredColumnPt` (coluna alvo que sobrevive
      a ↑/↓ passando por linhas curtas)
- [x] `CaretGeometry` convertendo nos dois sentidos: offset → coluna (medindo o prefixo do run)
      e coluna → offset (busca binária, pousando na fronteira mais próxima)
- [x] `CaretNavigator` — funções puras sobre `(Caret, PaginatedDocument, ITextMeasurer)` para
      setas, Home/End, PageUp/PageDown, usando `LaidOutLine.SourceStart`/`SourceLength`
- [x] O caret anda sobre o que está desenhado: ← / → pulam a marcação do heading e a linha em
      branco entre parágrafos, que a folha não mostra
- [x] Setas atravessam par substituto de uma vez, e a coluna nunca pousa entre as duas metades
- [x] `CaretNavigatorTests`/`CaretGeometryTests`: bordas do documento, coluna alvo preservada,
      Home/End em linha com wrap (limite visual, não do parágrafo), navegação cruzando fronteira
      de página, ida e volta coluna ↔ offset
- [x] Documento vazio passa a produzir uma linha vazia — sem ela, apagar todo o texto tirava do
      caret a altura e a posição, e ele sumia da tela
- [x] Caret desenhado pelo `PageRenderer` (1 DIP de largura, altura da linha); `PageSurface` só
      traduz tecla em chamada ao Core

Registrado desta fatia:

- **A navegação recebe o `ITextMeasurer`.** O plano previa função pura de
  `(offset, PaginatedDocument)`, mas achar a coluna dentro de um run exige medir o prefixo:
  interpolar poria o caret visivelmente fora do lugar no meio de uma palavra, em fonte
  proporcional. Continua sem estado e continua testável com o medidor determinístico
- **A geometria já é exata**, então o item "geometria exata do caret" da Fase 4 fica satisfeito
  desde aqui
- **`CaretGeometry.FindLine` é varredura linear** com saída antecipada — custo proporcional ao
  que existe antes do caret. Um índice achatado torna isso O(log n) e entra quando o profiling
  da Fatia 6 pedir
- **Navegar logo após digitar usa o layout anterior por um quadro**, porque o novo ainda está no
  debounce. A coluna alvo é recalculada quando o layout chega
- **O caret não pisca** e não há rolagem automática até ele. Nenhum dos dois estava na fatia;
  ficam para quando a edição tiver uso real

### Fatia 4.1 — Linha = linha, caret piscando e rolagem ✅

Fatia aberta depois de usar o editor de verdade. Os seis sintomas anotados eram **duas causas**:
o parser era de Markdown num editor que é WYSIWYG, e o debounce da repaginação matava a
repetição de tecla.

Semântica de linha — a decisão de fundo:

- [x] **Uma linha da fonte é uma linha na página.** O parser não junta mais linhas consecutivas
      num parágrafo, como faz o CommonMark: um `\n` é quebra visível e uma linha em branco é uma
      linha em branco, com altura, posição e um offset onde o caret pousa. A quebra automática
      que resta é só a da largura da página, do `LineBreaker` — que é a que o produto vende
- [x] `MarkupTokenizer` emite a linha final vazia quando a fonte termina em `\n`. Sem ela, o
      Enter no fim do documento levava o caret para um offset que nenhuma linha cobria:
      `FindLine` devolvia `(-1,-1)`, `Locate` devolvia altura zero e a barra **sumia da tela**
      até outra tecla trazê-la de volta
- [x] Some o espaço de junção, e com ele a única ressalva da invariante do `InlineRun`: o texto
      de um run voltou a ser cópia literal da fonte. `BuildParagraph` encolheu para três linhas
- [x] Enter uma vez já quebra a linha; Enter no início empurra a linha para baixo e deixa a
      branca no lugar; ↑↓ pousam na linha em branco preservando a coluna alvo. Os quatro itens
      caíram juntos, sem tocar em `CaretNavigator` nem em `CaretGeometry`
- [x] Linha acrescentada no fim de uma folha cheia abre a folha seguinte, com a linha nova em
      `YPt = 0` e altura própria

Latência da repaginação:

- [x] Teto de latência de 120ms ao lado do debounce de 50ms. A repetição automática do teclado
      dispara a cada ~33-40ms, **menor que o debounce**: cada tecla cancelava a repaginação
      pendente antes que ela rodasse, e a tela só atualizava ao soltar a tecla. Medido no harness
      headless: 30 inserções a cada 35ms davam **0 publicações** durante a rajada; com o teto,
      **8** em 1060ms — uma a cada ~132ms, como o teto prevê
- [x] A contagem é desde a última publicação, não desde o pedido: sob repetição a espera encolhe
      a cada tecla até zerar, publica e recomeça inteira. Digitação normal continua coalescendo

Caret na tela:

- [x] Piscar de 530ms (padrão do Windows; GTK ~600, macOS ~500), sólido enquanto se digita —
      a publicação de layout reinicia a contagem, então nenhum evento novo foi preciso
- [x] Temporizador parado ao perder o foco e ao desanexar da árvore visual: em execução ele
      guarda o delegate, que guarda o `PageSurface`, que guarda a árvore inteira
- [x] Sem foco não há caret desenhado — barra piscando em janela inativa promete uma tecla que
      iria para outro lugar
- [x] Rolagem automática atrás do caret via `RequestBringIntoViewEvent`, postado em prioridade
      `Loaded`: o `ScrollViewer` só conhece a nova extensão depois do passo de layout, e o caso
      que importa é justamente o Enter que acabou de criar uma folha
- [x] `PageRenderer.CaretRectDip` público — a rolagem usa exatamente o retângulo que o desenho
      usa, em vez de refazer a conta e divergir dele depois
- [x] Caret nasce no **começo** do documento, não no fim. Era detalhe invisível até a rolagem
      automática existir; com ela, abrir o app mostraria a última folha

Registrado desta fatia:

- **O custo aceito da decisão:** um `.md` quebrado à mão em 80 colunas por outra ferramenta
  aparece com linhas curtas, fielmente. É o preço de o autor ver o que digitou, e reunir as
  linhas de volta é trabalho de importação/exportação, não do editor
- **Espaçamento entre blocos muda de significado.** Com um `ParagraphNode` por linha de fonte, um
  `SpaceAfterPt` automático por bloco separaria toda linha de toda linha. O espaço entre
  parágrafos passa a vir da linha em branco e do preset de norma — hoje na Fase 6, Fatia 1 — e o
  item continua lá, com outro desenho
- **Avalonia 12 renomeou o que a fatia precisava:** `OnGotFocus`/`OnLostFocus` recebem
  `FocusChangedEventArgs`, e não há `Control.BringIntoView(Rect)` — a rolagem se pede levantando
  `RequestBringIntoViewEvent`
- **Os cinco testes que quebraram estavam certos ao quebrar.** Todos escreviam `\n\n` para obter
  duas linhas; agora obtêm três, que é o comportamento novo. Nenhum apontou defeito no código
- **Terceiro argumento para `tests/AcademicEditor.App.Tests/`**: teto de latência, piscar e
  rolagem só existem no App e ficaram cobertos por harness manual. Continua na Fase 4

### Fatia 4.2 — Fronteira de quebra e fim de linha ✅

Aberta ao testar a 4.1 no app: **End mandava o caret para a linha de baixo** e **Enter no fim da
linha punha um espaço na linha de baixo**. A suspeita foi o fim de linha do mock. Acertou o
sintoma e errou a causa — eram dois problemas distintos, e o principal não tinha nada a ver com
o mock.

Afinidade do caret — a causa dos dois bugs relatados:

- [x] Numa quebra **por largura** o espaço fica com a linha de cima, então o fim de uma linha e o
      começo da seguinte são **o mesmo offset**. `FindLine` devolvia sempre a linha de baixo, e por
      isso End saltava de linha; ← no começo de uma linha quebrada devolvia o mesmo offset com a
      mesma afinidade e não movia nada visível
- [x] `CaretAffinity` (`Downstream`/`Upstream`) no `Caret`. Um offset, duas posições na tela: isso
      é estado, não se deduz do offset. Numa quebra explícita não há empate, porque o `\n` ocupa
      uma posição entre as duas linhas
- [x] Cada movimento declara o lado que quer: End e ← que sobe → `Upstream`; Home, → e vertical →
      `Downstream`. A afinidade **não** é carregada por ↑↓, senão descreveria uma fronteira da
      linha de origem
- [x] End pousa **depois** do espaço da quebra. Parece igual na tela — o espaço é invisível no fim
      da linha — mas é o que faz o Enter ali deixar o espaço em cima. Medido: quebrar antes do
      espaço produz ` palavra palavra…` na linha de baixo; depois dele, `palavra palavra…`
- [x] Os quatro testes novos foram verificados vermelhos com a correção desligada

Fim de linha — problema real, separado, encontrado na investigação:

- [x] O mock era CRLF, confirmado **no binário**: o raw string literal do C# preserva o fim de
      linha do arquivo-fonte, e a constante extraída do heap de strings do DLL tinha `\r\n`
- [x] Com CRLF cru no buffer o `\r` fica fora de todo run, e **Backspace no início de uma linha
      apagava só o `\n`, deixando um `\r` órfão** no meio do texto. Como Enter insere `"\n"`,
      editar um documento CRLF ainda produzia fim de linha misto
- [x] `LineEndings.NormalizeToLf` + normalização no `EditorDocument`, na construção e no `Insert`.
      A invariante passa a ser **o buffer nunca contém `\r`** — é ela que dispensa o parser, o line
      breaker e cada tecla de edição de saber o que fazer com CRLF
- [x] `Insert` devolve quantos caracteres entraram: a normalização encurta o texto, e mover o caret
      por `text.Length` o deixaria adiante do buffer numa colagem CRLF
- [x] Dois mocks, `UniqueFeaturesLf` e `UniqueFeaturesCrLf`, **derivados de uma fonte só** — é o
      que garante que difiram apenas no fim de linha e o que os torna imunes a alguém regravar o
      arquivo. A `MainWindow` usa o CRLF de propósito: uma regressão aparece na primeira execução

Registrado desta fatia:

- **Gravação será sempre em LF.** Abrir um `.md` CRLF e salvar converte o arquivo — aceito, em
  troca de um ponto único de conversão em vez de CRLF espalhado por todo o motor
- **Aparar o espaço final ao quebrar a linha ficou de fora.** Alguns editores fazem; é opinião, e
  um espaço invisível no fim da linha de cima não incomoda ninguém
- **O teste do Enter não fica vermelho sem a correção**, porque `MoveToLineEnd` devolve o mesmo
  offset com ou sem afinidade — o que mudava era só onde ele era desenhado. Ele guarda a decisão
  de End pousar depois do espaço, não o bug em si

### Fatia 5 — Undo/redo, arquivo e atalhos ✅

Undo/redo:

- [x] `PieceEdit` — o trecho da lista de peças que virou outro trecho. **Não guarda texto:** apagar
      no piece table nunca toca nos buffers, então o texto removido continua lá e as peças antigas
      ainda apontam para ele. Colar dez páginas e desfazer custa uma emenda na lista
- [x] `Revert` e `Reapply` são o mesmo movimento com `Before` e `After` trocados — não há um
      segundo caminho de código para manter correto
- [x] `UndoRedoStack` agrupando: cada tecla é uma edição no buffer, mas desfazer letra por letra é
      insuportável. Teclas seguidas que continuam de onde a anterior parou entram no mesmo grupo
- [x] Mover o caret, Enter, colar ou trocar de tipo de edição abrem grupo novo. O `Break` acontece
      na **intenção** de mover, não no movimento efetivo: logo após digitar o layout ainda está no
      debounce e a seta não acha para onde ir — o agrupamento não pode depender disso
- [x] Edição nova invalida o redo: aquela história descreve peças que não existem mais na lista
- [x] Stress diferencial de 3 sementes × 2.000 operações aleatórias, intercalando edição, undo e
      redo contra um modelo ingênuo de string. É o que prova o delta estrutural: a correção depende
      de undo/redo serem estritamente LIFO, que é o que mantém válido o índice de peça de cada delta

Arquivo:

- [x] `IDocumentStorage` + `FileDocumentStorage` async
- [x] **Save atômico**: escreve num temporário e move por cima. Um `WriteAllText` direto trunca o
      arquivo antes de escrever — morrer no meio deixa o original pela metade, e ele já não existe
      para recuperar. O temporário fica no mesmo diretório porque mover só é atômico no mesmo volume
- [x] Detecção de BOM (UTF-8, UTF-16 LE/BE) e gravação na mesma codificação. Sem BOM é UTF-8, que é
      o padrão da web, do git e de todo `.md` — adivinhar por estatística erra em texto curto e
      erra calado
- [x] Normalização CRLF/LF: já feita na Fatia 4.2, na entrada do `EditorDocument`
- [x] `FileDocumentStorageTests`: round-trip nas quatro codificações, falha no meio da escrita não
      corrompe o original, nem deixa temporário para trás
- [x] Abrir troca documento **e** histórico: os deltas do anterior descrevem outra lista de peças

Atalhos:

- [x] `KeyCode`/`ModifierKeys` próprios do Core — ele não referencia Avalonia. Os nomes são iguais
      aos do `Avalonia.Input.Key` de propósito: a tradução vira um mapa por nome, montado uma vez
- [x] `CommandId`, `ShortcutScope`, `ChordSequence`, `KeyBindingRegistry` com N passos desde já —
      um atalho de dois passos exige estado entre teclas, e acrescentar isso depois seria reescrever
      o laço de teclado, não acrescentar um caso
- [x] `ShortcutDispatcher` no **tunelamento**, não no borbulhamento: é o que faz `Ctrl+S` salvar em
      vez de virar texto. No borbulhamento o `PageSurface` já teria tratado a tecla
- [x] `FocusScopeTracker` — hoje quase vacuoso com um painel só, existe pela seam
- [x] Bindings: `Ctrl+S`, `Ctrl+Shift+S`, `Ctrl+O`, `Ctrl+Z`, `Ctrl+Y` e `Ctrl+Shift+Z`. Os dois
      últimos apontam para o mesmo `CommandId`, que é a razão de o atalho não apontar para o método
- [x] Diálogos nativos via `IStorageProvider`; título da janela mostra arquivo, "não salvo" e a
      última mensagem — um save que falha é a falha que mais importa, e sem isso sumiria numa Task

Registrado desta fatia:

- **O bug que só o pipeline real pegou:** o enum `Key` do Avalonia tem apelidos (`Prior` e `PageUp`
  são o mesmo valor, e `ToString()` devolve o nome canônico para os dois), então o mapa por nome
  lançava na primeira tecla traduzida. Nenhum teste do Core alcançaria isso
- **`Revert`/`Reapply` não disparam `Changed`.** O evento carrega o texto que entrou, e montá-lo
  exigiria materializar o trecho restaurado — a cópia que o delta existe para evitar. Ninguém perde
  nada hoje; a decisão se paga ou se reabre no reflow incremental da Fase 4
- **`IsModified` não volta a falso ao desfazer até o estado gravado.** Para isso o histórico teria
  de marcar onde o save aconteceu. A conta erra a favor da segurança: no máximo grava um arquivo
  idêntico ao que já estava lá
- **Não há aviso de documento não salvo ao fechar a janela.** Fica para quando houver mais de um
  documento aberto

### Fatia 5.1 — Marcação revelada, quebra atômica e navegação vertical ✅

Três bugs encontrados usando o editor. Dois eram o mesmo defeito de fronteira que a 4.2 tratou
pela metade; o terceiro era marcação que existe no arquivo e não existia na tela.

Navegação vertical:

- [x] ↑/↓ "travando" depois de Home/End **não era** falta de linha adjacente, que era a hipótese.
      `MoveToAdjacentLine` fixava `Downstream`, e depois de End a coluna alvo vira a largura cheia
      da linha — então o movimento cai exatamente na fronteira de uma quebra por largura, onde o
      fim de uma linha é o começo da seguinte. Na prática ↑ ia ao começo da mesma linha e ↓ pulava uma
- [x] Escolhe-se a afinidade que resolve para a linha de destino, e ela só é reivindicada onde
      desempata algo. Um teste antigo que comparava carets inteiros voltou a passar sozinho ao
      apertar essa definição
- [x] Borda do documento: ↑ na primeira linha vai ao começo dela, ↓ na última ao fim. Tecla morta
      parece editor travado

Marcação revelada:

- [x] `# Título` tinha os offsets 0 e 1 fora de toda linha: **posição no arquivo sem posição na
      tela**. Daí o caret pousar depois da tag e o Enter partir o bloco em heading vazio + parágrafo
- [x] A marcação passa a aparecer enquanto o caret está no bloco, como Obsidian e Typora. Sem
      posição oculta, o Enter antes dela empurra o título inteiro para baixo sem caso especial
- [x] A marcação sai do parser com o **mesmo estilo** do título: revelar muda a largura da linha,
      nunca a altura, senão o documento subiria e desceria a cada entrada e saída do caret
- [x] `LineBreaker` descarta os runs de marcação ao montar os chunks, e não recebendo lista
      filtrada — seria uma alocação por bloco a cada repaginação. A âncora da linha vazia é o
      primeiro run que a linha usa, senão um `## ` recém-aberto reivindica o offset do sustenido
- [x] `PaginatedDocument` carrega o trecho revelado: enquanto o caret ficar dentro dele, a seta não
      dispara layout nenhum

Quebra de página atômica:

- [x] `\page` não gerava linha alguma — invisível, inalcançável pelo caret, mas editável por
      acidente: Backspace na linha seguinte comia o `\n` que o isolava e ele virava texto na folha
- [x] Passa a ocupar uma linha desenhada como filete tracejado, no rodapé da folha que encerra
- [x] A linha cobre o trecho **real** do bloco (`  \page  ` também vale), e quem o trata como
      unidade é o `CaretNavigator` pelo `LineKind` — as setas o atravessam de uma tecla
- [x] `BlockMarkers` decide no Core o que Backspace e Delete removem quando um marcador está no
      caminho: o marcador inteiro mais o `\n`, ou nada
- [x] Apagar um marcador é um grupo de undo próprio: desfazer devolve a quebra de página de uma vez

Registrado desta fatia:

- **Revelar custa uma repaginação por travessia de bloco.** É o segundo argumento concreto para o
  reflow incremental da Fase 4 — o primeiro é a repaginação por tecla. A Fatia 6 mede
- **A quebra de página não é revelada como texto**, ao contrário do heading: é um objeto sem texto
  para o autor editar, e o Word faz o mesmo. A inconsistência é deliberada
- **Marcação inline (`**negrito**`) ainda não existe** — o parser emite um run de texto por bloco.
  O mecanismo de revelar já está pronto para ela

### Fatia 5.2 — Editar sobre a fronteira de uma quebra por largura ✅

Enter e Backspace no início de uma linha "não faziam nada": era preciso apertar duas vezes. A
4.2 e a 5.1 trataram a fronteira para **navegar**; faltava tratá-la para **editar**.

- [x] Não havia tecla perdida nem `\n` engolido. Numa quebra por largura o espaço fica com a
      linha de cima, então `linhaA.SourceEnd == linhaB.SourceStart` — e um `\n` inserido ali só
      torna explícita a quebra que a margem já impunha. `aaaaa bbbbbbbbb` e `aaaaa \nbbbbbbbbb`
      desenham **os mesmos pixels**; só o segundo `\n` abre a linha em branco que o autor pediu
- [x] A regra: **materializar a quebra antes de editar.** Na fronteira, a tecla primeiro
      transforma a quebra implícita em `\n` — as duas posições de tela voltam a ser dois offsets
      distintos — e só então faz o que faria numa quebra explícita
- [x] `CaretGeometry.IsSharedBoundary` expõe o teste que já existia enterrado no `Resolve`, e o
      `Resolve` passa a usá-lo: uma definição só de "as duas linhas dividem este offset"
- [x] `LineBreaks.ForEnter` conta os `\n` e diz onde o caret pousa. No Core, e não no ViewModel,
      porque a decisão depende do `PaginatedDocument` e porque é a regra que precisa de teste —
      mesmo raciocínio do `BlockMarkers`
- [x] O lado da fronteira decide o caret: `Downstream` (começo da linha de baixo) desce junto com
      o texto que empurrou; `Upstream` (fim da linha de cima, onde End pousa) fica na linha nova
      em branco. É o que End seguido de Enter faz em qualquer editor
- [x] **A afinidade sobrevive à edição.** `MoveCaretAfterEdit` e o `Publish` fixavam `Downstream`,
      e `CaretNavigator.At` nunca recebia o parâmetro que sempre aceitou. Era a segunda metade do
      defeito: apagar um `\n` cuja quebra a margem refaz no mesmo lugar devolve uma tela idêntica,
      e o caret era redesenhado no começo da linha de baixo, exatamente de onde saiu
- [x] Backspace pousa `Upstream` — no fim da linha de cima; Delete preserva o lado de onde apagou;
      digitar preserva o lado, senão escrever no fim de uma linha quebrada pela margem faria o
      caret saltar para a linha de baixo a cada tecla
- [x] O teste que fixa o sintoma é o do layout, não o da função: parágrafo que quebra em duas
      linhas → Enter na fronteira → **três linhas, a do meio com `SourceLength == 0`**, e o caret
      na terceira. Ao lado dele, o que prova a premissa: um `\n` só devolve as mesmas duas linhas

Registrado desta fatia:

- **Backspace na fronteira apaga o espaço da quebra**, e não atravessa a fronteira sem apagar. As
  duas leituras são defensáveis — a segunda é o espelho exato do Enter —, e a escolha é do autor:
  nenhuma tecla de apagar deve gastar um toque sem tirar nada do texto
- **`InsertLineBreak` e `DeleteBackward` consultam o último layout publicado**, até ~50ms atrás do
  buffer por causa do debounce. Digitar e apertar Enter dentro dessa janela pode ler uma fronteira
  deslocada e inserir um `\n` a mais ou a menos. É a mesma exposição do `BlockMarkers`; um layout
  síncrono na UI thread fecharia a janela e não vale o preço agora. A hora de reabrir é se a linha
  em branco indesejada aparecer de verdade
- **Continua sem teste de `EditorViewModel`** — os testes cobrem só o Core, e o ViewModel vive no
  App. É onde mora a propagação da afinidade, e é o pedaço desta fatia verificado à mão

### Fatia 5.3 — Margem com tolerância de um branco ✅

`LineBreaker` assumia que **espaço nunca provoca quebra**: o que não cabia era anexado à linha assim
mesmo e passava da margem, sem limite. A suposição foi questionada — "as margens devem SEMPRE ser
respeitadas" — e a fatia levou três estados até achar a regra que serve. As duas primeiras estão
registradas porque cada uma tem um argumento verdadeiro, e é isso que impede a discussão de reabrir:

- **Pendurar sem limite** (o original). Nenhum glifo passa da margem, porque um branco no fim da
  linha não desenha nada. Mas um grupo de brancos projeta a linha arbitrariamente para fora do papel,
  e o caret vai junto quando pousa depois deles
- **Margem estrita para tudo** (a primeira tentativa). O branco que não cabe desce — e a linha de
  baixo nasce indentada. Não é caso raro: a folga que sobra numa quebra gulosa é uniforme entre zero
  e a largura da próxima palavra, então cai abaixo da largura de um espaço em **cerca de uma quebra
  a cada seis**. Em texto real salta aos olhos, e foi visto na tela antes de cair

O que ficou:

- [x] **Tolerância de um caractere em branco, e só um.** A linha leva o que cabe mais um branco; o
      resto do grupo desce. Na quebra comum — `palavra espaço palavra` — o que cabe é zero, então o
      espaço fica pendurado e a linha de baixo começa na palavra, encostada na margem esquerda
- [x] Palavra nenhuma ganha tolerância: continua descendo inteira, ou partida no que cabe quando
      está sozinha numa linha vazia (URL, fórmula)
- [x] A tolerância **não se acumula** — só é oferecida com a linha ainda dentro da margem. Fecha o
      caso de dois chunks de branco seguidos, que acontece quando um run muda de estilo no meio deles
- [x] `LargestPrefixThatFits` ganhou `minimum`: zero é corte legítimo para o branco, porque quem
      leva a linha adiante é o caractere da tolerância. Para palavra continua sendo um, que é o que
      garante progresso
- [x] Duas garantias, dois testes sobre os mesmos seis textos: `InkExtentPt` desconta o branco final
      e exige que **nenhum glifo** passe da margem; `ExtentPt` cru exige que **nenhuma linha** passe
      dela por mais de um branco. O segundo fica vermelho se alguém voltar a pendurar o grupo inteiro
- [x] A fronteira compartilhada não muda em nenhum dos três estados: a linha de cima termina onde a
      de baixo começa. `CaretAffinity`, `IsSharedBoundary` e o Enter da Fatia 5.2 nunca foram tocados

Registrado desta fatia:

- **A Fatia 4.2 já tinha esbarrado nisto** pelo lado do Enter no fim da linha, e concluiu "pendura
  porque é invisível". Estava meia certa: invisível não é o mesmo que ilimitado
- **O caret ainda é desenhado até um branco além da margem** quando pousa depois do espaço pendurado.
  É o único traço que atravessa a margem, e agora está limitado a um caractere. Some se a geometria
  do caret o grampear na margem; fica anotado para quando incomodar
- **Ir e voltar custou pouco porque o gate é verde a cada passo.** Os três estados foram uma troca no
  mesmo `if` e a reescrita dos testes que afirmavam o comportamento anterior — o que dói é reverter
  sem ter proposto a alternativa antes

### Fatia 6 — Fechamento da fase
- [x] Teste manual end-to-end via `./dev.sh run`
- [x] Documento longo (~300 páginas) para medir latência de repaginação e decidir se o
      reflow incremental precisa ser antecipado da Fase 4

**Medido.** "Tempo" é o cronômetro em volta de **uma repaginação completa** — `MarkupParser.Parse`
mais `LayoutEngine.Layout` sobre o documento inteiro, já aquecido, sem desenho e sem I/O. É o
número que importa porque hoje **toda** repaginação é completa: não há reflow incremental, então é
isso que cada tecla dispara.

Duas parcelas, com o mesmo formato de documento — parágrafos de 100 palavras separados por linha
em branco, A4 com margens de 1" — mas **corpus de tamanhos diferentes**: cada lado foi
dimensionado para chegar perto de 300 páginas, e o medidor real cabe muito mais caractere por
linha. **Os totais não se subtraem; o que se compara é o custo por linha.**

| | algoritmo (`FakeTextMeasurer`) | real (`AvaloniaTextMeasurer`) |
|---|---|---|
| caracteres do corpus | 409.529 | 1.017.245 |
| páginas | 314 | 301 |
| linhas | 10.668 | 16.056 |
| tempo de uma repaginação | 33 ms | **910 ms** |
| **por linha** | **3,11 µs** | **56,66 µs** |

`LayoutPerformanceTests` mede a primeira coluna e guarda um teto de 3s contra regressão de ordem
de grandeza; `--measure-layout` no App mede a segunda, com o medidor instrumentado.

*(Os números da segunda coluna foram refeitos quando o corpus do benchmark ganhou vocabulário
realista e títulos, na fatia do cache — ver Fase 4. A conclusão não mudou; os números do commit
original são os do corpus de dezoito palavras.)*

**O motor não é o gargalo — a medição de texto é.** 56,66 ÷ 3,11 ≈ **18**: medir texto de verdade
custa dezoito vezes o que custa todo o resto do motor junto. A passada instrumentada confirma pelo
outro lado — **893ms dos 910ms (98,1%) estão dentro do `ITextMeasurer`**, em 258.002 chamadas a
`MeasureWidthPt`, cada uma construindo um `TextLayout` e alocando uma string, porque o line breaker
mede chunk a chunk (~16 por linha). As outras 243.217 chamadas são `GetLineMetrics`, que o cache
por estilo já resolve: são buscas em dicionário e não pesam.

Na prática: **digitar num documento de 301 páginas deixa a tela 0,9s atrás do buffer.** A janela
não trava, porque o layout roda em background e é cancelável — mas cada tecla cancela a
repaginação pendente, então a tela só alcança o texto quando o autor para de digitar.

Decisão que sai daí:

- **O reflow incremental não é o primeiro remédio.** Ele reduz *quantas* linhas são medidas de
  novo, o que ajuda muito na digitação, mas o custo por linha continua o mesmo — e a primeira
  paginação ao abrir o arquivo continua custando 1,3s
- **O primeiro remédio é o cache de medição por `(texto, estilo)`**, exatamente o que o comentário
  do `AvaloniaTextMeasurer` já apontava. Palavra se repete muito em prosa, e o line breaker mede a
  mesma palavra toda vez que ela aparece
- **Ressalva honesta sobre o número:** o corpus sintético tem 18 palavras distintas, então um cache
  acertaria quase 100% ali e mediria a si mesmo. O ganho real precisa ser medido com texto de
  verdade antes de se dar por resolvido
- **Os dois se somam, e nessa ordem:** o cache derruba o custo por linha, o reflow incremental
  derruba o número de linhas remedidas. Só o segundo deixaria a abertura do arquivo em 1,3s

---

## Fase 4 — Desempenho com documento longo ✅

**A fase mudou de nome porque mudou de assunto.** Abriu chamada "Editor de verdade", com quatro
itens de editor e nenhum de desempenho; foi inteiramente consumida pelo que a medição da Fatia 6
apontou, e as quatro fatias entregues são as quatro que aquele número pediu — cache de medição,
reflow incremental, culling do desenho e coalescer a repaginação. O commit de merge já a chamava
assim. O nome novo é o que ela é; os itens de editor que sobraram foram para onde pertencem, e
estão na tabela abaixo.

- [x] **Cache de medição de texto** por `(texto, estilo)` — não estava nesta lista, entrou na
      frente porque a medição da Fatia 6 mostrou que era ele, e não o reflow, o primeiro gargalo
- [x] **Reflow incremental** — em um nível, não três: a medição mostrou que o parser é de graça
- [x] **Culling de páginas fora do viewport no `Render`** — subiu na fila pelo mesmo motivo do
      cache: a medição apontou para ele
- [x] **Digitação: coalescer a repaginação em vez de cancelá-la** — o único dos quatro que não
      saiu de um número, e sim de abrir o app e segurar uma tecla
- [x] Geometria exata do caret via `LaidOutLine.SourceStart` ↔ `TextLayout` — **satisfeita desde
      a Fatia 4 da Fase 3**, e o registro dela já dizia isso: medir o prefixo do run dá a posição
      exata, e foi assim que ela nasceu

Os três itens de editor que restavam:

| item | destino | por quê |
|---|---|---|
| Highlighting em tempo real reaproveitando os `InlineRun` do AST | Fase 5, Fatia 4 | é a mesma máquina: marcação inline é o parser emitindo runs de estilos diferentes, que é o que "highlighting sem motor separado" quer dizer |
| Seleção múltipla (`IReadOnlyList<SelectionRange>`) | estacionamento de ideias | a Fase 5 faz **uma** seleção. Multi-cursor é feature de code editor, e a lista plural custaria indireção em cada tecla, cada desenho e cada edição por algo que talvez nunca venha |
| Chords reais registrados (ex: `Ctrl+K, Ctrl+S`) | estacionamento de ideias | `KeyBindingRegistry` já suporta N passos desde a Fatia 5 e tem teste; o que falta não é máquina, é um comando que queira um chord |

### Reflow incremental ✅

Aberta com o sintoma que sobrou depois do culling: "só passo a sentir o delay quando gero uma
grande quantidade de inputs". Era a repaginação completa a cada tecla — 74ms e ~7MB de lixo, oito
vezes por segundo enquanto se digita.

A decomposição decidiu o desenho da fatia:

| | |
|---|---|
| parser | **0,5 ms** |
| line breaker + page breaker | **74,3 ms** |

- [x] **O nível de parser do roadmap não existe.** Reparsear o documento inteiro custa meio
      milissegundo; o custo é construir 16.056 `LaidOutLine` para um documento em que uma linha
      mudou. Três níveis viraram um
- [x] **O trecho alterado sai de comparar os dois textos** — prefixo e sufixo comuns —, não de
      rastrear edições. Exato por construção, sobrevive a uma rajada de teclas coalescida num
      layout só, e trata colar, desfazer e refazer sem caso especial. Custa ~1ms por megabyte
- [x] **Só o caso comum entra**: alteração que não cria nem apaga `\n`. Aí os blocos são os mesmos
      um a um, e só um precisa ser requebrado. Enter, Backspace numa fronteira, colar um parágrafo
      ou trocar a geometria devolvem `null`, e o motor pagina do zero pelo caminho de sempre
- [x] Deslocar uma linha reaproveitada custa uma cópia de record — a mesma que o page breaker já
      paga ao assentá-la numa folha. O que some é o caro: montar chunks, medir cada palavra e
      alocar o texto dos runs
- [x] O bloco revelado tem de ser o bloco sujo, antes e depois. Revelar muda a largura da linha,
      então um caret que atravessa fronteira muda a aparência de dois blocos sem mudar o texto
      deles — e aí não há o que reaproveitar

**Medido:** uma tecla no meio do documento de 301 páginas passou de **70,7 ms para 11,2 ms**
(6,3x). O que sobra é o parser, a comparação dos textos e o page breaker reempilhando as 16 mil
linhas.

Registrado desta fatia:

- **A asserção dos testes não é o ganho, é a identidade**: reaproveitar tem de devolver exatamente
  o documento que a paginação completa devolveria, linha por linha e offset por offset, em sete
  cenários. Um reaproveitamento errado não quebra o desenho — quebra o caret, e isso aparece longe
  de onde errou
- **Duas testemunhas contam as medições** que chegam ao `ITextMeasurer`: 30 no caminho completo
  contra 3 no incremental. Sem elas, os testes de identidade passariam comparando a paginação
  completa com ela mesma
- **O motor recusa em vez de arriscar.** Toda condição duvidosa devolve `null` e cai no caminho
  completo, que é código provado — inclusive uma verificação final de que as linhas antigas foram
  consumidas exatamente até o fim
- **O page breaker ainda reempilha tudo.** Parar cedo quando uma quebra de página cai na mesma
  linha de antes ("reflow until resync") é o próximo corte, se 11ms incomodar

### Digitação: coalescer em vez de cancelar ✅

Com o culling e o reflow no lugar, sobrou o pior sintoma: **segurar uma tecla não mostrava nada e o
caret congelava; ao soltar, tudo aparecia de uma vez.**

O culpado era o debounce de 50ms com teto de 120ms, e o comentário dele descrevia este mesmo
sintoma como algo já resolvido na Fatia 4.1. Estava resolvido pela metade: **o teto garantia que o
layout começasse em até 120ms, não que ele terminasse.** `SchedulePagination` abria com
`_pending?.Cancel()`, e o token ia para dentro do `Task.Run` e do `LayoutEngine.Layout`.

- [x] O ciclo que travava: um layout passa do intervalo de repetição do teclado (~33ms — um pico de
      GC basta) → a tecla seguinte o mata antes de publicar → o relógio da última publicação não
      avança → a espera desaba para zero → daí em diante cada tecla mata a anterior, e nada publica
      até soltar
- [x] **Zerar o debounce pioraria**, que era a hipótese natural: toda tecla passaria a iniciar um
      layout que a seguinte mata, com o mesmo desfecho e mais trabalho jogado fora. O defeito não
      era o valor do número, era cancelar quem já estava trabalhando
- [x] Trocado por **um layout em voo por vez, com pedido pendente**. Quem chega durante um layout só
      marca; quem está rodando atende ao terminar. **Inanição deixa de ser possível por
      construção** — não há cancelamento no caminho da digitação
- [x] A taxa se auto-regula: publica-se na velocidade em que os layouts terminam. Com os 11ms do
      reflow, isso é mais rápido que a repetição do teclado, e cada tecla ganha o seu quadro
- [x] Sumiram junto `DebounceMilliseconds`, `MaxLatencyMilliseconds`, o `CancellationTokenSource`, o
      relógio da última publicação, as duas gerações e o `Dispatcher.Post`. A guarda de geração
      existia porque um layout cancelado podia chegar depois de um mais novo; com um de cada vez, a
      ordem é garantida. O `ConfigureAwait(true)` traz a continuação para a UI thread, onde o
      estado vive

Registrado desta fatia:

- **Esta é a única das quatro que não pôde ser medida antes.** As outras três saíram de um número;
  esta saiu de abrir o app e segurar uma tecla. O pipeline depende do dispatcher do Avalonia, que
  não roda laço de mensagens sob `SetupWithoutStarting`, e `EditorViewModel` vive no App, sem teste
- **Cada layout ainda aloca o documento inteiro como string** — ~2MB nas 301 páginas, o que passa
  dos 85KB do **Large Object Heap** e cobra uma coleta de geração 2, que pausa a UI. É a explicação
  mais provável para um layout estourar os 33ms e disparar o ciclo acima. `TextBufferSnapshot` já
  tem `CopyTo(Span<char>)` esperando por um buffer reaproveitado, e o comentário de lá já previa
  isto. Próximo alvo, com medição antes
- **O laço captura exceção e reporta.** Sem isso, uma falha dentro dele deixaria o documento
  congelado sem explicação nenhuma na tela

### Culling do desenho ✅

Aberta depois de abrir o documento de 301 páginas no app e ele ficar "consistentemente lento, como
se o processador estivesse sempre atrás de uma fila, independente da operação". Não era a
paginação — aquela já estava em 68ms e roda em background. Era o **desenho**.

`PageRenderer.Render` percorria a pilha inteira e construía um `TextLayout` por linha: **16.056 por
quadro, na UI thread**. E o quadro não era raro — `InvalidateVisual` vem do timer de piscar do
caret, **a cada 530ms, para sempre**.

Medido com `--measure-render`, desenhando num `RenderTargetBitmap` de 900×700 com o
`DrawingContext` de verdade:

| | ms por quadro |
|---|---|
| pilha inteira | 248,9 |
| só o que o viewport cruza | **1,89** — **131x** |

- [x] **Parado, o app queimava metade de um núcleo**: 498 ms de desenho por segundo de relógio, sem
      ninguém tocar no teclado. Passou a 3,8 ms/s. É o número que explica o sintoma — a lentidão era
      constante e independente da operação porque não dependia de operação nenhuma
- [x] Digitando, os 249ms eram na **UI thread**, somados por cima da repaginação em background. Era
      a fila
- [x] O intervalo de folhas sai por **aritmética**, não varredura: a pilha é uniforme, então o
      índice é uma divisão pelo passo (altura da folha mais o vão). Varrer as páginas para saber
      quais entram custaria O(páginas) por quadro — o que o culling existe para não pagar
- [x] **Rolar não chama `Render` por conta própria** — o Avalonia translada o que já foi desenhado.
      Isso era invisível enquanto o desenho cobria tudo; com culling, seria folha em branco atrás da
      rolagem. O `PageSurface` assina `ScrollViewer.OffsetProperty` e invalida, e solta a inscrição
      no detach, como já fazia com o ViewModel e o timer
- [x] `viewport: null` desenha a pilha inteira: é o que a medição usa para o "antes" e o que mantém
      o `Render` utilizável fora de um `ScrollViewer` — um exportador PDF, por exemplo

Registrado desta fatia:

- **A conta de páginas visíveis não tem teste unitário.** `PageRenderer` vive no App e depende de
  `Rect` do Avalonia; levá-la ao Core exigiria um tipo de geometria próprio só para isso. A
  verificação é o benchmark mais a rolagem manual, e as bordas do intervalo — primeira e última
  folha — são o que olhar
- **Glyph runs cacheados continuam fora.** Com ~3 folhas por quadro em vez de 301, cachear seria
  otimizar o que deixou de doer. Entra se a medição voltar a apontar para cá
- **O caret ainda repinta a superfície inteira a cada 530ms.** Agora custa 1,89ms, então não vale
  máquina para repintar só o retângulo dele

### Cache de medição ✅

`CachingTextMeasurer` no Core, decorando o `AvaloniaTextMeasurer`. Decorador, e não um dicionário
dentro da implementação Avalonia, por dois motivos: a política de cache fica testável sem
subsistema gráfico — que é a razão de `ITextMeasurer` existir — e um exportador PDF a herda junto
com o motor de layout.

Medido com `--measure-layout`, no documento de 301 páginas e 16.056 linhas da Fatia 6:

| | tempo | ganho |
|---|---|---|
| sem cache | 909,7 ms | — |
| cache frio (abrir o arquivo) | 308,7 ms | **2,9x** |
| cache quente (uma tecla) | 68,1 ms | **13,4x** |

- [x] O corpus do benchmark foi refeito **antes** de medir qualquer coisa. Com as dezoito palavras
      que ele tinha, o cache acertaria quase 100% e estaria medindo a si mesmo. Agora são 12 mil
      formas distintas sorteadas por Zipf, montadas por sílabas — o arquivo dá para abrir e ler —,
      com um título a cada oito parágrafos. A repetição observada, **96,7%** (8.423 trechos
      distintos em 258.002 medições), é a da língua, não a do gerador
- [x] O acerto **não aloca**: `GetAlternateLookup<ReadOnlySpan<char>>` compara o span com as chaves
      sem materializar string. Metade do ganho é essa; a outra metade é não construir o `TextLayout`
- [x] Teto de 32.768 trechos por estilo, e ao estourar limpa em vez de despejar. O conjunto de
      trabalho é o vocabulário do documento — menos de dez mil numa tese —, e o que passa disso são
      os prefixos transitórios da busca binária que posiciona o caret. Um LRU custaria mais em
      contabilidade do que economizaria num cache que quase nunca chega ao teto
- [x] Só a largura é cacheada aqui. As métricas de linha dependem apenas do estilo, são meia dúzia
      de entradas e já eram cacheadas por quem as produz
- [x] Oito testes no Core, com um medidor que conta as chamadas que chegam embaixo: repetição não
      desce, estilos não se confundem, a largura é idêntica à de quem não tem cache, e estourar o
      teto volta a medir em vez de devolver número errado

Registrado desta fatia:

- **Os 68ms que sobram são o piso do rebuild completo**, não medição: 16.056 linhas a ~3,11 µs é
  ~50ms, e o resto são as buscas no cache. A medição de texto deixou de ser o gargalo — quem é,
  agora, é reconstruir o documento inteiro a cada tecla. **É a vez do reflow incremental**
- **68ms por tecla cabe no teto de latência de 120ms** do `EditorViewModel`, então digitar num
  documento de 300 páginas passa a acompanhar. O que ainda não cabe é o custo em lixo: cada tecla
  ainda aloca a string do documento inteiro, a AST inteira e as linhas todas
- **Abrir o arquivo continua custando 309ms** e sempre vai custar uma paginação completa — é o
  único caminho que o reflow incremental não ajuda, e por isso o cache tinha de vir primeiro
- **`--write-corpus <caminho>` grava esse documento em disco** e o App passou a aceitar um arquivo
  na linha de comando, que é como se sente a latência em vez de só ler o número

---

## Fase 5 — Mouse e seleção ✅

**Objetivo:** o editor passa a responder ao mouse e a ter seleção. São as quatro coisas que
separam "protótipo que se digita" de "editor que se usa": pôr o caret com um clique, selecionar
com o mouse e com o teclado, recortar/copiar/colar, e ver negrito e itálico formatados.

Como na Fase 3, em fatias verticais. A ordem não é arbitrária: a Fatia 1 constrói a entrada por
ponto, de que a Fatia 2 precisa para arrastar; a Fatia 2 constrói a seleção, de que a Fatia 3
precisa para copiar. A Fatia 4 é independente das outras três e podia vir em qualquer lugar.

### Fatia 1 — Ponteiro: caret por clique e cursor de texto ✅

- [x] `CaretNavigator.AtPoint(pageIndex, xPt, yPt, …)` — o caminho que faltava. Todo movimento
      até aqui partia de um caret e chegava a outro; não havia como **entrar** por um ponto da
      folha
- [x] O ponto chega em pontos, relativo ao canto da área de conteúdo — a mesma convenção que
      `CaretPosition` devolve, e é o que torna isto o inverso exato de `CaretGeometry.Locate`
- [x] **Todo clique pousa em algum lugar.** Acima da primeira linha e abaixo da última, nos
      extremos da folha; à esquerda e à direita da margem, nos extremos da linha; numa folha sem
      linha alguma, na vizinha com conteúdo — para trás primeiro, porque o caret pertence ao texto
      que a quebra encerrou, não ao que ainda não começou
- [x] Marcador de bloco recebe o caret no início, como as setas já faziam pelo `Stop()`
- [x] `PageRenderer.HitTest` — o inverso de `CaretRectDip`, ao lado dele, pelo motivo que aquele
      método já documenta: a soma de vão, origem da folha e margem recalculada em outro arquivo é
      como as duas contas divergem uma da outra depois. A folha sai por aritmética, não por
      varredura — a mesma divisão pelo passo que o culling usa
- [x] Cursor I-beam dentro da área de conteúdo, seta na margem e no vão entre folhas
- [x] `AtPoint` é o inverso de `Locate`, verificado como propriedade: ir e voltar devolve o mesmo
      offset em **toda** posição que uma linha cobre, nas duas afinidades, em três documentos

Registrado desta fatia:

- **O grampeamento tem um dono só, e é o Core.** `HitTest` devolve o ponto **cru** — `YPt`
  negativo acima do texto, `XPt` maior que a largura útil à direita da margem — e é `AtPoint` que
  o traz para dentro. Foi o que permitiu a regra "todo clique pousa em algum lugar" ter teste sem
  subsistema gráfico, e é também o que faz o cursor funcionar: o sinal fora do intervalo é
  exatamente o que distingue o papel da margem, e grampear no renderizador tornaria essa pergunta
  impossível de responder sem refazer a conta
- **Folha sem linha alguma o parser já não produz.** Desde que o `\page` passou a ocupar uma linha
  desenhada (Fatia 5.1 da Fase 3), toda folha tem ao menos o marcador — a ressalva que
  `CaretGeometry.PreviousLine` carrega no comentário está desatualizada quanto à rota, não quanto
  à regra. O caso continua tratado e continua testado, com o documento montado à mão
- **O quarto argumento para `tests/AcademicEditor.App.Tests/`, e o primeiro com asserção óbvia.**
  `HitTest(CaretRectDip(c)) == c` é uma propriedade de uma linha que pegaria um sinal trocado na
  hora, e não tem onde morar: `PageRenderer` depende de `Rect` do Avalonia e `Core.Tests` não
  referencia o App. O que dá para testar do lado do Core — o round-trip `AtPoint`/`Locate` — está
  testado; o que sobra é a conversão DIP↔pt, verificada à mão

### Fatia 2 — Seleção: mouse e teclado ✅

- [x] `Selection(Anchor, Caret)` no Core. Âncora é offset nu; a ponta ativa é um `Caret` inteiro,
      porque ela **é** o caret — com afinidade e coluna alvo, que é do que a seta seguinte precisa
- [x] **O ViewModel guarda a seleção, não o caret.** `Caret` virou `_selection.Active`: um caret e
      uma seleção em campos separados seriam dois estados para a mesma coisa, e o dia em que
      divergissem o texto apagado não seria o texto destacado
- [x] `SelectionGeometry.RectsFor` — um retângulo por linha visual que o trecho cruza
- [x] `WordBoundaries.WordAt` para o duplo clique; o triplo é a linha **visual**, a mesma
      definição que Home e End já usam
- [x] Os oito `MoveCaret*` do ViewModel ganham `extend`. **O `CaretNavigator` não mudou uma
      linha:** Shift não é um movimento diferente, é o mesmo sem recolher a âncora — e quem
      mantém ou recolhe é o ViewModel
- [x] Arrastar com captura de ponteiro (`ClickCount` 1/2/3), `Shift+clique`, `Shift+setas`,
      `Shift+Home/End`, `Shift+PageUp/PageDown`
- [x] Retângulos desenhados **atrás** do texto, só nas folhas que o culling já selecionou, e
      percorridos com um índice que avança junto com as folhas em vez de uma varredura por folha

Registrado desta fatia:

- **As duas pontas resolvem a fronteira compartilhada para dentro do trecho** — começo
  `Downstream`, fim `Upstream`. Não é heurística: numa quebra por largura o fim de uma linha e o
  começo da seguinte são o mesmo offset, e resolver ao contrário penduraria um retângulo de
  largura zero numa das pontas. É também o que dispensa a âncora de ter afinidade própria
- **A lasca da linha em branco vale pela quebra, não pela linha.** Uma linha sem tinta dentro do
  trecho ganha a largura de um espaço, senão pareceria um buraco no meio da seleção — mas só
  quando o `\n` dela está dentro do trecho. Um trecho que apenas termina no começo dela não pegou
  nada, e uma lasca ali prometeria um caractere que não existe
- **Palavra não atravessa run.** `WordAt` expande dentro de um run, então a fronteira de estilo —
  o começo de um `**negrito**` — vale como fronteira de palavra. Em texto comum a linha inteira é
  um run, que é o caso que importa
- **Meio caractere de imprecisão no duplo clique, e simétrica.** O offset chega arredondado para a
  fronteira mais próxima, então a metade direita da última letra de uma palavra já cai no branco
  seguinte. A regra alternativa — olhar para trás quando o caractere à direita não é de palavra —
  tem a sua própria metade errada: clicar na metade esquerda de uma vírgula pegaria a palavra
  antes dela. Ficou a regra simples, de um caso só
- **`Ctrl+A` na tese inteira custa 3,65ms** para 10.667 retângulos — 0,34 µs por linha, medido no
  `SelectionPerformanceTests` com teto de 500ms contra regressão. É barato porque as linhas
  inteiramente dentro do trecho **não medem nada**: a esquerda é a origem da linha e a direita é a
  tinta que o line breaker já posicionou. Só as duas linhas de fronteira medem um prefixo. O corte
  de pedir os retângulos por folha visível não foi preciso, e fica anotado se um dia for

### Fatia 3 — Recortar, copiar, colar e apagar a seleção ✅

- [x] `TextBufferSnapshot.GetText(start, length)` — copiar uma linha não pode materializar o
      documento inteiro, que nas 301 páginas são ~2MB direto no **Large Object Heap**. Atravessa
      só as peças que o trecho cruza
- [x] `UndoRedoStack.RecordCompound` — substituir a seleção é **um** undo. `Record` agrupa apenas
      edições do mesmo `EditKind` que continuam de onde a anterior parou, e apagar mais inserir
      são duas `Other`: pelo caminho normal virariam dois grupos, e um Ctrl+Z devolveria o texto
      digitado sem devolver o que foi apagado
- [x] Digitar, colar, Enter, Del e Backspace com seleção substituem ou apagam o trecho
- [x] `Ctrl+C`, `Ctrl+X`, `Ctrl+V`, `Ctrl+A` no `KeyBindingRegistry`, escopo `Editor`
- [x] Clipboard na `MainWindow`, como o `IStorageProvider` dos diálogos — o `EditorViewModel`
      continua sem um `using` do Avalonia
- [x] O stress diferencial do undo ganhou a operação "substituir trecho": 3 sementes × 2.000
      operações, e é ela que prova o `RecordCompound` contra o modelo ingênuo de string

Registrado desta fatia:

- **`Undo` já revertia na ordem certa.** Ele desfaz de trás para a frente, que é exatamente a
  ordem de desfazer apagar-e-inserir — o `RecordCompound` não precisou de nenhum caminho novo de
  reversão, só de um grupo com mais de uma edição. É o mesmo argumento do `Revert`/`Reapply`: um
  caminho de código só para manter correto
- **Enter com seleção não materializa fronteira.** `LineBreaks.ForEnter` fala do caret; com um
  trecho selecionado, a fronteira que interessava era a do caret que acaba de deixar de existir,
  e o Enter vira um `\n` comum sobre a substituição
- **Colar não precisou de caso especial.** `EditorDocument.Insert` já normalizava o fim de linha e
  já devolvia quantos caracteres de fato entraram, e `LayoutReuse.Between` já recusava um trecho
  com `\n` e caía na paginação completa. Uma colagem multilinha atravessa tudo isso sem uma linha
  de código nova — é o retorno das decisões da Fatia 4.2 e do reflow incremental
- **Recortar copia antes de apagar.** A ordem inversa tiraria o texto do documento sem ter onde
  buscá-lo de volta se a área de transferência falhasse
- **O Avalonia 12 reescreveu o clipboard.** `IClipboard.GetTextAsync`/`SetTextAsync` não existem
  mais; texto passa por `ClipboardExtensions.SetTextAsync`/`TryGetTextAsync`, sobre
  `DataFormat.Text`. Terceira surpresa da mesma família, depois do `FocusChangedEventArgs` e da
  ausência de `Control.BringIntoView(Rect)` na Fatia 4.1
- **Copiar e colar em si continuam sem teste automatizado**: dependem do clipboard do sistema e da
  `MainWindow`. O que dá para testar — o trecho materializado e o undo composto — está testado

### Fatia 4 — Marcação inline: negrito e itálico ✅

Fecha o item "Highlighting em tempo real reaproveitando os `InlineRun` do AST" da Fase 4 e cobra
a promessa que o `LineBreaker` faz desde a Fatia 1 da Fase 3: *"o motor já está pronto para
`**negrito**` no meio da frase"*.

- [x] `**` negrito, `*` itálico, `***` os dois; delimitador sem par sai como texto literal
- [x] `_` continua literal — uma regra em vez de duas, e `x_1` numa fórmula não vira itálico
- [x] A marcação sai com o **mesmo estilo** do texto que envolve: revelar muda a largura da
      linha, nunca a altura. Mesma decisão do `## ` do heading, verificada com o mesmo teste
- [x] **Nada mudou fora do parser**, e era a aposta da fatia: `TextStyle` já tinha `Weight` e
      `Italic`, o `AvaloniaTextMeasurer` já traduzia os dois, o `LineBreaker` já quebrava sobre
      runs heterogêneos e já descartava os `IsMarkup`, e o `LayoutEngine` já revelava por bloco.
      Nem o motor de layout, nem o medidor, nem o renderizador foram tocados
- [x] Pareamento por pilha, com busca do topo para baixo: `**a *b**` fecha o negrito e devolve o
      asterisco solto ao texto. É também o que garante que os pares saiam aninhados, que é o que
      permite ao estilo ser uma pilha na montagem
- [x] Ênfase dentro de um título volta ao estilo do título, e não a "normal" — fechar um
      `**negrito**` num `# título` não pode desemboldar o resto dele
- [x] O documento de exemplo mostra a marcação já na primeira execução, como o CRLF faz com a
      normalização de fim de linha desde a Fatia 4.2

Registrado desta fatia:

- **Quatro ou mais asteriscos seguidos não são delimitador**, e é o que resolve `****` sem caso
  especial: pareá-lo daria uma ênfase vazia, que some da tela e deixa o autor sem entender para
  onde foi o que digitou
- **Duas condições de flanqueamento, não a regra inteira do CommonMark:** um delimitador seguido
  de branco não abre e um precedido de branco não fecha. É o que impede `2 * 3 * 4` de virar
  itálico. Colado em palavra, `pre*meio*pos` enfatiza — que é o que o CommonMark também faz
- **O caret não fica preso em marcação escondida**, e não foi preciso tocar no `CaretNavigator`
  para isso: revelar é por bloco, e um bloco aqui é uma linha da fonte. No instante em que o
  caret entra na linha, todo o `**` dela aparece e as setas andam sobre ele como sobre qualquer
  letra. Estar dentro de marcação escondida exigiria o caret estar em outra linha
- **Chegar por baixo a uma linha que termina em marcação pousa um delimitador antes do fim.** Com
  a marcação escondida a linha cobre até o último caractere visível, e é para lá que `←` e `↑`
  mandam o caret; a repaginação revela em seguida, e aí o `**` final aparece à direita dele. Nada
  fica inalcançável — `End` e `→` chegam lá —, e é o espelho do que o heading já fazia pelo outro
  lado
- **Sem escape (`\*`).** Exigiria um segundo passe no tokenizer e não há demanda; entra quando
  alguém precisar de um asterisco literal dentro de uma frase enfatizada
- **Sublinhado ficou de fora por decisão de norma**, não por custo: ABNT e APA usam itálico em
  texto corrido, e o Markdown não tem sintaxe própria para ele. A sintaxe se decide quando o uso
  aparecer

---

## Fase 6 — Documento acadêmico ⬜

**Objetivo:** o que sai do editor passa a ser um trabalho acadêmico, e não um texto paginado —
folha numerada, tipografia de norma, PDF, e as extensões de markup que uma tese usa.

Catorze fatias, nenhuma com mais de três tópicos — eram sete. A 5 se partiu em quatro ao ser
desenhada, porque cada metade abaixo da marcação inline precisa de uma capacidade que o motor não
tem, e são capacidades diferentes; e quatro entraram depois **como correção** (4.1, 4.2, 4.3 e
5a.1). A ordem tem duas dependências reais e o resto é independente: a Fatia 2 precisa do preset da Fatia 1 para saber com que fonte desenhar o
cabeçalho, e a Fatia 7 é a última porque um menu só oferece o que já existe. **O PDF vem logo
depois do cabeçalho de propósito:** ele é a prova da aposta que abriu o projeto — o motor de
layout nasceu independente de tela —, e deixá-lo para o fim seria descobrir no fim se ela valia.
Notas de rodapé e sumário fluem para dentro dele depois, sem tocá-lo.

> **A primeira entrega da 5b foi recusada, e as quatro fatias corretivas saíram daí.** A PR #6 tinha
> o gate verde e o aplicativo quebrado em dois pontos que aparecem em menos de um minuto de digitação real:
> espaços em branco passando da margem outra vez, e o caret se perdendo com a digitação lenta num
> documento grande. **Nenhum dos dois é sutil, e nenhum dos dois foi visto pelo gate** — porque os
> testes e as medições descreviam uma configuração que o aplicativo não executa: alinhado à
> esquerda, corpo 11, num corpus sem uma única marcação inline. O aplicativo roda
> `TypographyPreset.Abnt` — justificado, corpo 12 — sobre texto com `**negrito**`, `$math$`, `[^id]`
> e `:-:`. As Fatias 4 e 5a entregaram exatamente nesse vão. As regras que saíram disso estão no
> `CLAUDE.md`, sob "Uma fatia não é aceita pelo gate".

### Fatia 1 — Preset tipográfico e cromo da janela ✅

O preset era **dívida da Fase 3**, não item novo: as Fatias 2 e 4.1 adiaram espaçamento entre
blocos e entrelinhamento de 1,5 para "o preset de norma", e o comentário do `MarkupParser` ainda
apontava para cá. O `AvaloniaTextMeasurer` usava `FontFamily.Default` e o corpo era 11pt — ou seja,
o editor não tinha família de fonte nenhuma, e a ABNT quer Times ou Arial 12pt com entrelinhamento
1,5.

- [x] `TypographyPreset` no Core (`Layout/`), ao lado do `PageSettings`: as duas metades da norma —
      geometria da folha e tipografia do que cai nela. `TextStyle` ganhou a família, que era o que
      faltava para medidor e renderizador concordarem sobre qualquer fonte que não a padrão, e as
      constantes de tamanho saíram do `MarkupParser`: preset tipográfico não é decisão de quem lê a
      marcação. `TypographyPreset.Abnt` é o que o aplicativo usa; `Default` é o do MVP, e é o que
      mantém as medidas dos testes de layout em números redondos
- [x] Cromo da janela: `DockPanel` em volta do `ScrollViewer`, com barra de status. `StatusMessage`
      saiu do título e foi para lá; o título passou a dizer só arquivo e "não salvo", e a barra
      ganhou o caminho completo à direita. É a mesma mudança estrutural que o menu da Fatia 7 vai
      precisar, feita uma vez
- [x] `tests/AcademicEditor.App.Tests/` criado e ligado ao `./dev.sh check` — 15 testes. O roadmap
      já acumulava quatro argumentos a favor dele; o primeiro a virar teste foi o round-trip
      `HitTest(CaretRectDip(c)) == c`, mais a costura de tradução do estilo para o typeface

**O entrelinhamento é aplicado onde a altura da linha é decidida** — no `LineBreaker`, via
`TypographyPreset.Apply` —, e nunca no desenho: quem consome `LaidOutLine` (page breaker, caret,
seleção, futuro exportador) tem de ver a mesma caixa que a renderização vê. A folga extra fica
**acima** da linha, como no Word: o que sobra abaixo da baseline não muda, então o texto não sobe
meio espaço dentro da própria caixa. Consequência aceita: caret e retângulo de seleção ocupam a
caixa inteira — que é o que se quer na seleção, linhas seguidas destacadas sem buraco entre elas.

Registrado desta fatia:

- **"Espaço entre blocos" saiu do preset, e a fatia descobriu por quê.** O item vinha da Fase 3 e
  parecia trivial: `HeadingSpaceBeforePt`/`HeadingSpaceAfterPt`, que é o que a ABNT literalmente
  manda (título separado do texto por um espaço de 1,5 antes e depois). Só que **neste editor o
  autor digita essa linha em branco**, e ela é visível: o espaço automático somaria por cima do que
  já está escrito, e apareceria na tela um espaço que ninguém digitou. É a mesma decisão da Fatia
  4.1 vista pelo outro lado — o espaço entre blocos vem da linha em branco, e o que o preset
  acrescenta é que ela agora cresce com o entrelinhamento, como qualquer linha. Some junto a
  necessidade de `SpaceBeforePt`/`SpaceAfterPt` no `LaidOutLine` e de mexer no `PageBreaker`
- **O reaproveitamento tinha um buraco, e ele foi fechado aqui.** `LayoutEngine.Reuse` só comparava
  `PageSettings`. Trocar o preset sem tocar no texto produz `LayoutReuse.Between` **não nulo** — os
  textos são idênticos, então não há `\n` no trecho alterado —, e com o caret dentro do último
  bloco as duas guardas seguintes passam: linhas medidas na fonte antiga seriam reaproveitadas.
  `PaginatedDocument` passou a carimbar o preset que o produziu, pelo mesmo motivo por que já
  carregava o `PageSettings`, e a guarda virou "geometria **ou** tipografia mudou, pagina do zero"
- **`HeadingSizes` é struct de seis campos, não lista.** O preset é comparado por valor nessa
  guarda, e um `IReadOnlyList<double>` dentro de um record compara por **referência**: dois presets
  iguais que comparassem diferente repaginariam do zero à toa, e dois diferentes que comparassem
  iguais reaproveitariam linhas medidas em outra fonte — que é o erro caro, porque não quebra o
  desenho, quebra o caret
- **O parser passou a consultar `Layout/`.** É a única direção, além da AST, em que parser e layout
  se conhecem, e é deliberada: resolver o corpo de um `## ` é decisão de norma, não de quem lê a
  marcação. A alternativa — estilos semânticos resolvidos só na medição — mudaria a chave do cache
  de medição e o renderizador, e não é desta fatia
- **Times New Roman não existe em toda máquina, e foi verificado, não suposto.** A instalação WSL
  desta máquina tem dezesseis fontes, todas DejaVu: nem a da Microsoft, nem a Liberation Serif.
  Com dois nomes na lista, o preset da ABNT cairia ali na fonte padrão — **sem serifa** — e nada na
  tela diria por quê. São três: `Times New Roman, Liberation Serif, DejaVu Serif`
- **A família custa hash de string na chave do cache de medição**, que é `TextStyle`. São ~15
  caracteres por busca contra os 258 mil acessos de uma repaginação completa: milissegundos contra
  309ms. **Conta de guardanapo, não cronômetro** — se um dia doer, o remédio é um índice de família
  no lugar do nome, não trocar o tipo do estilo
- **O `App.Tests` não precisou de `Avalonia.Headless`.** `Rect`, `Point`, `FontFamily` e `Typeface`
  se constroem sem plataforma inicializada, e as duas contas testadas dependem só da contagem de
  folhas e da geometria da página. Nomear uma fonte não é resolvê-la contra o sistema
- **Os números de desempenho do roadmap continuam sendo os do preset `Default`**, e o
  `LayoutBenchmark` agora o fixa explicitamente para que não derivem em silêncio. Medir o preset da
  ABNT é um número **novo**, não uma correção: corpo 12 em vez de 11 dá ~9% mais linhas, e
  entrelinhamento 1,5 dá ~50% mais folhas para o mesmo texto. Fica devido
- **Não houve conferência visual.** A máquina não tem ferramenta de captura de tela; o app foi
  aberto e ficou de pé, e o resto está coberto por teste. A tela em si é para olhar no `./dev.sh run`

### Fatia 2 — Cabeçalho, rodapé e contagem ✅

- [x] `HeaderFooterSettings` no Core: três campos (esquerda, centro, direita) para o cabeçalho e
      três para o rodapé, com os marcadores `{page}`, `{pages}` e `{title}`. Carrega **só o texto**
      — a altura continua sendo de `HeaderReservedHeightPt`/`FooterReservedHeightPt`, zerados no
      `PageSettings` desde a Fase 3 justamente à espera disto
- [x] Numeração de página — é o `{page}`, e não um item à parte: numerar é o caso mais simples do
      mesmo template. `HeaderFooterSettings.Abnt` põe o número no canto superior direito
- [x] Contador de caracteres e palavras na barra de status da janela (a da Fatia 1 — não confundir
      com o rodapé da página, que é o item acima)

**Cabeçalho e rodapé NÃO entram em `PageLayout.Lines`; `PageLayout` ganhou campos próprios.**
`CaretGeometry.FindLine` varre exatamente essa lista mapeando offset → linha, e `LayoutEngine.Reuse`
exige que ela case um-para-um com os blocos e seja consumida até o fim. Uma linha de cabeçalho não
tem offset no buffer: ela capturaria o caret e derrubaria o reflow incremental — e, como toda falha
de offset, apareceria longe de onde nasceu. Com campo próprio, nada do caret é tocado, por
construção.

**`{pages}` só se sabe depois de paginar**, e é o que parece um ciclo e não é: a altura reservada
não depende do texto do cabeçalho, então a contagem total entra num segundo passe que só substitui
texto, sem repaginar nada.

**A contagem roda no `Task.Run` da paginação**, não na UI thread. Contar é O(n) e o documento de
referência tem ~1MB — mas aquele `Task.Run` já materializa a fonte inteira para paginar, então
contar ali custa a passada e nada mais, e já sai coalescida pelo mesmo laço. Conta sobre o buffer
cru, marcação inclusa (é o simples e o previsível); por **code point**, não por unidade UTF-16,
senão um emoji conta dois; e com um trecho selecionado a barra mostra o trecho, como todo editor
faz.

Registrado desta fatia:

- **As faixas viraram um passe sobre o resultado, não um parâmetro do motor.** O plano era o
  `HeaderFooterSettings` chegar ao `LayoutEngine`; escrevendo, ficou claro que a faixa **não
  influencia quebra de linha nem de página** — a reserva é fixa e decidida antes de qualquer linha
  ser quebrada. Então `PageBands.Apply(documento, faixas, medidor)` é um passe puro sobre o
  documento pronto, e três coisas caem no colo: é literalmente o "segundo passe" que o `{pages}`
  pedia, o `LayoutEngine.Layout` não vai para oito parâmetros, e o caminho incremental herda as
  faixas de graça porque quem as aplica é quem publica o layout
- **`BandRun` não tem `SourceStart`, e é para isso que o tipo existe.** É a forma forte da decisão
  acima: não basta a faixa morar fora de `Lines`, ela não pode nem **carregar** um offset. Um
  `LaidOutRun` com offset falso mais cedo ou mais tarde seria consumido como conteúdo, e uma falha
  de offset não quebra o desenho — quebra o caret
- **A reserva tem um dono só, e a regra é "sem reserva não há faixa".** Duas fontes de verdade para
  a mesma altura seria uma para divergir da outra. Se houver texto configurado e reserva zero, nada
  é desenhado — a alternativa seria o cabeçalho por cima da primeira linha do texto. Tem teste
- **A contagem da seleção não aloca.** Ela sai do **texto publicado**, com `AsSpan` — pedir o trecho
  ao buffer seria ~2MB direto no Large Object Heap num `Ctrl+A` da tese, e isso a cada movimento do
  ponteiro durante um arrasto. Custo aceito: fica até um layout atrás do buffer, que é o mesmo
  atraso que a tela inteira já tem e é invisível num contador
- **"Palavra" aqui não é a palavra do `WordBoundaries`.** Aquela responde "o que o duplo clique
  seleciona", e ali uma vírgula é uma unidade própria; esta responde "quantas palavras o autor
  escreveu", e ali `Silva,` é uma palavra — que é o que qualquer contador faz. As duas definições
  convivem porque as perguntas são diferentes, e cada uma diz isso no seu comentário
- **Gap encontrado, e não corrigido de propósito: `PageSettings.A4` usa margens de 1" uniformes, e
  a ABNT quer 3cm à esquerda e no topo, 2cm à direita e embaixo.** É o irmão geométrico do que a
  Fatia 1 corrigiu na tipografia, mas mudar margem move **toda** quebra de página do documento —
  decisão própria, não item de outra fatia. Fica devido, ao lado da medição do preset da ABNT

### Fatia 3 — Exportação PDF ✅

- [x] `PdfExporter` no App, sobre `SKDocument.CreatePdf` do SkiaSharp, consumindo **o mesmo**
      `PaginatedDocument` que a tela desenha
- [x] Fontes embutidas (`/FontFile2`) e texto pesquisável (`/ToUnicode`) no arquivo gerado — um PDF
      de tese que não se pesquisa não serve
- [x] `editor.exportPdf` no `KeyBindingRegistry` em `Ctrl+P`, com diálogo de arquivo ao lado de
      Abrir e Salvar

**SkiaSharp, e não uma biblioteca de PDF de alto nível.** O Skia já vem com o Avalonia e molda o
texto com o mesmo motor que desenha na tela: é o mesmo argumento que escolheu `TextLayout` em vez
de somar avanços de glifo na Fatia 2 da Fase 3 — onde medir e desenhar discordam, o texto vaza da
folha, e aqui discordar significa entregar um PDF diferente do que o autor viu. Uma biblioteca com
motor de layout próprio (QuestPDF) competiria com o nosso e teria de ser contornada, além da
licença Community com limite de receita.

**O exportador mora no App, e isso não é concessão.** Ele consome `PaginatedDocument` de fora do
Core — que é exatamente o que a regra "o Core nunca referencia o toolkit" existia para permitir.

Registrado desta fatia:

- **A aposta que abriu o projeto se pagou, e sem uma conversão sequer.** O canvas de PDF do Skia já
  é em **pontos tipográficos**, que é a unidade interna do motor desde o MVP: este é o único
  consumidor do layout que não multiplica nada — o `PageRenderer` multiplica por 96/72 porque a
  tela é que tem outra unidade. O exportador não repagina, não remede e não conhece o Avalonia:
  recebe o `PaginatedDocument` e desenha
- **A lista de famílias tem de ser percorrida nome a nome, e isso não era óbvio.** A família do
  preset é `"Times New Roman, Liberation Serif, DejaVu Serif"`; passá-la inteira ao Skia como um
  nome não casa com fonte nenhuma, ele cai na padrão dele, e o PDF sai numa fonte diferente da que
  está na tela. `SKFontManager.MatchFamily` devolve `null` quando não encontra — é o que permite
  tentar a seguinte; `SKTypeface.FromFamilyName` não serviria, porque nunca falha. **Verificado na
  saída:** nesta máquina, sem Times e sem Liberation, o PDF traz `/BaseFont /AAAAAA+DejaVuSerif` —
  a terceira alternativa da Fatia 1 chegando ao papel
- **O PDF desenha o documento, não o editor.** Ficam de fora o caret, o destaque da seleção, a
  borda da folha e o filete tracejado do `\page`: os quatro existem para quem está escrevendo. O
  marcador em especial já fez o trabalho dele quando a folha terminou ali. Testado pelo avesso —
  uma folha só com o marcador não tem glifo a desenhar, e a prova é o arquivo não precisar embutir
  fonte alguma
- **Medido, com o tamanho do arquivo:**

|  | tamanho |
|---|---|
| 1 folha | 213,1 KB |
| 3 folhas | 214,0 KB |
| 50 folhas | 236,3 KB |
| 300 folhas | 356,1 KB |

  **A fonte embutida domina e é constante por documento** — ~213KB fixos e ~0,48KB por folha. Uma
  tese inteira sai em ~356KB, e não adianta cortar página para encolher o arquivo: o que pesa é o
  subconjunto da fonte, que é justamente o que faz o PDF não depender das fontes de quem o abrir

- **Exportar em background é seguro por construção**, e é a mesma propriedade que faz o layout
  rodar fora da UI thread desde o MVP: `PaginatedDocument` é imutável do topo às folhas. Quem
  continua digitando durante a exportação troca a referência; o que foi para o disco é o documento
  que existia quando o autor pediu — o mesmo contrato do save
- **`Ctrl+P`, e não um atalho de exportação.** É o que a mão procura para produzir um PDF, e não
  disputa com nada: este programa não imprime nem vai imprimir — **o PDF é o caminho da impressão**,
  e dois nomes para o mesmo botão só fariam o autor escolher entre eles
- **Gravação atômica, como a do `.md`:** temporário no mesmo diretório e `File.Move` por cima.
  Morrer no meio da escrita deixaria um PDF pela metade no lugar do anterior, que já não existiria
  para recuperar
- **`SkiaSharp` passou a ser referência declarada do App**, apesar de já chegar transitivamente
  pelo `Avalonia.Skia`. A versão é a mesma que ele traz, então não há resolução nova a fazer —
  o que muda é a dependência deixar de quebrar de forma obscura no dia em que o Avalonia trocar de
  versão ou de mecanismo de desenho
- **Nenhuma biblioteca de leitura de PDF nos testes, e não faz falta.** O que a fatia promete —
  tamanho da folha, contagem de páginas, fonte embutida, texto pesquisável — são objetos **nomeados**
  no arquivo, e procurá-los pelo nome é verificação direta, não proxy. O teste do rodízio de
  famílias tira a fonte de verdade do próprio gerenciador do sistema, então não depende da máquina

### Fatia 4 — Alinhamento e justificação ✅

- [x] Marcação de bloco para alinhamento, revelada na linha do caret como o `# ` de um título:
      `:-: centralizado`, `-: à direita`, `:- à esquerda`; sem marcação vale o padrão do preset
      (justificado, na ABNT)
- [x] Passe de justificação sobre a linha já quebrada, distribuindo a sobra entre os vãos
- [x] `Ctrl+J` cicla os quatro alinhamentos, aplicado a **todo bloco que a seleção toca**

**A sintaxe é a que o Markdown já usa para alinhar coluna de tabela** (`| :-- | :-: | --: |`):
mesma ideia, mesmo símbolo, e o dois-pontos marca o lado a que o texto se prende. Sendo marcação no
arquivo, o alinhamento persiste no `.md`, o undo sai de graça porque é edição de texto, e o reflow
incremental e o "revelar por bloco" já o tratam sem uma linha de código nova.

**Alinhamento é propriedade de bloco, não de trecho.** Centralizar meio parágrafo não quer dizer
nada; por isso o `Ctrl+J` age sobre os blocos que a seleção cruza, inteiros.

**Justificar exige partir os runs no branco — é o ponto de risco da fatia.** `CaretGeometry.ColumnPt`
e `OffsetInRun` medem prefixos **dentro** do run, e o `LineAccumulator` funde chunks contíguos:
`"abc def"` é um run só, com o espaço dentro. Esticar esse espaço faz a posição desenhada divergir
do prefixo medido, e derivam juntos o caret, a seleção e o hit test — e o `PageRenderer` sequer
esticaria, porque desenha o run com um `TextLayout`. A invariante a preservar é **dentro de um run,
prefixo medido = posição desenhada**: o passe parte a linha nas fronteiras de branco e reposiciona
`XPt`, em vez de mexer no texto. Custa mais runs por linha, que é o que o culling da Fase 4 já
absorve.

Dois corolários, cada um com seu teste:

- **O branco pendurado pela tolerância da Fatia 5.3 não entra no esticamento.** Ele já está fora da
  margem por decisão; esticá-lo levaria a linha adiante do papel
- **Centralizar e alinhar à direita usam a extensão de tinta, não a crua.** Um espaço final
  invisível deslocaria a linha centralizada por um caractere — é a mesma distinção que `InkExtentPt`
  e `ExtentPt` já fazem nos testes de margem

Registrado desta fatia:

- **A invariante sobreviveu, e tem teste de propriedade.** Para toda linha justificada e **todo
  offset que ela cobre**, `OffsetAtColumn(ColumnPt(offset)) == offset`. Era o risco inteiro da
  fatia num assert: se o passe tivesse esticado o texto dentro de um run em vez de partir a linha
  nos brancos, a posição desenhada deixaria de bater com o prefixo medido, e o caret pousaria fora
  do lugar — longe de onde se errou
- **O alinhamento é aplicado dentro do `LineBreaker`, e não num passe sobre o documento** como o
  cabeçalho da Fatia 2. Dois motivos, e os dois só aparecem escrevendo: "a última linha do bloco
  não é justificada" exige saber onde o bloco termina, que é justamente o que `BreakIntoLines`
  devolve; e o reflow incremental requebra **um** bloco — se o alinhamento morasse fora, a linha
  requebrada sairia desalinhada em relação às vizinhas. Há teste de identidade para isso, com o
  documento justificado
- **O destaque da seleção começava em zero, e o alinhamento transformou isso em bug.**
  `SelectionGeometry` assumia que "origem da linha" e "margem esquerda" eram a mesma coisa — e
  eram, até esta fatia. Numa linha centralizada, o destaque marcaria papel em branco antes da
  primeira letra. Nenhum teste anterior pegaria: a premissa era verdadeira
- **Medido, com o medidor determinístico, sobre a mesma tese de 300 páginas:**

| | tempo | por linha |
|---|---|---|
| à esquerda | 29,4 ms | 2,75 µs |
| justificado | 74,9 ms | 7,02 µs |

  **2,55×**, e é o preço de manter a invariante: partir cada linha nas fronteiras de branco custa
  uma string por segmento, contra uma por run. **Paga-se na abertura do arquivo, não por tecla** —
  digitar passa pelo reflow incremental, que requebra um bloco só. Duas coisas apareceram na
  medição: a primeira versão usava uma `List` que dobrava quatro vezes por linha (~16 segmentos
  numa lista de capacidade 2), e passou a contar antes para alocar o array exato; e uma passada só
  variava 15% entre execuções, mais do que a diferença que se queria enxergar, então o número
  passou a ser o melhor de cinco
- **`:-: # Título` funciona, e de graça.** O alinhamento é lido **antes** do resto da linha, então
  heading e alinhamento não precisam se conhecer — o que sobra depois do prefixo continua sendo
  classificado como sempre. É o que a ABNT pede para "RESUMO" e "REFERÊNCIAS"
- **`Ctrl+J` é edição de texto, e é o que faz a feature caber sem máquina nova.** A marcação vai
  para o arquivo, o undo funciona porque é edição como digitar, o reflow incremental já trata, e a
  revelação na linha do caret já trata. O destino sai da **primeira** linha tocada, para que uma
  seleção com alinhamentos mistos convirja para um só em vez de cada linha seguir o seu rodízio
- **Linha em branco e `\page` ficam de fora do rodízio.** Marcar uma faria dela texto; marcar a
  outra desfaria a quebra de página. Nenhuma das duas é o que se pede ao apertar `Ctrl+J`
- **O `Ctrl+J` lê o buffer, não o layout publicado.** Aqui a análise é de **texto** — onde cada
  linha começa —, e ler uma versão atrasada dele daria fronteiras erradas e uma edição no lugar
  errado. Custa materializar o documento, o mesmo que salvar, e `Ctrl+J` não é caminho de tecla
- **Corner registrado:** uma linha **vazia** com marcação de alinhamento não produz run nenhum, e
  aí o caret pousa na margem esquerda em vez de no centro. É invisível na prática — com o caret na
  linha a marcação é revelada, e aí há runs

### Fatia 4.1 — A margem volta a valer ✅

Fatia corretiva: a Fatia 4 desfez a garantia da Fase 3, Fatia 5.3, e nada ficou vermelho.

- [x] `LineExtents` em `Layout/` — **um dono só** para "até onde a linha chega": `ExtentPt` (crua),
      `InkExtentPt` (a tinta) e `AlignExtentPt` (a extensão que **tem** de caber na margem, isto é,
      a crua menos no máximo um branco). As cinco cópias divergentes passaram a chamá-lo
- [x] `LineAlignment` alinha por `AlignExtentPt`, e não pela tinta — nos três alinhamentos
- [x] As duas garantias da 5.3 viraram `MarginInvariants`, rodado nos **quatro** alinhamentos

**A tolerância era estado da caneta, não medida.** O `LineBreaker` a aplica corretamente ao quebrar
(`taken = fits + 1`: pendura um branco, desce o resto do grupo) — mas ela vivia inteira dentro
daquele laço, e o passe de alinhamento roda **depois** dele. Alinhando pela tinta, `TrimEnd()`
devolvia o grupo **inteiro** de brancos à sobra; a justificação encostava a tinta na margem e
reemitia o grupo depois dela. `AlignExtentPt` é a mesma regra escrita como medida, e por isso os
dois passes voltam a falar da mesma coisa.

**Centralizar continua sendo pela tinta, com teto.** Um espaço final invisível não pode deslocar a
linha meio caractere — é o que a Fatia 4 acertou —, mas o deslocamento nunca passa do que a
tolerância permite pendurar: `Math.Min(tinta / 2, sobra alinhável)`. À direita o teto sempre morde,
então lá é só a sobra alinhável.

Medido com o medidor determinístico (margem 100pt, 10pt por caractere), antes da correção:

| caso | extensão | |
|---|---|---|
| `"a  b   c    dddddddddddd"` justificado | **130pt** | 3 brancos fora |
| `"ab cd    efghijklmno"` justificado | **140pt** | 4 brancos fora, e um vão interno de 60pt |
| `"abc    "` à direita | **140pt** | 4 brancos fora |

E conferido com o medidor **real**, no preset da ABNT, sobre texto com marcação inline, blocos
centralizados e à direita, e grupos de brancos digitados de propósito: largura útil 451,28pt,
branco de 3,81pt, **maior extensão 455,09pt** — a margem mais exatamente um branco. Com a correção
desligada, a mesma conferência dá 470,35pt.

Registrado desta fatia:

- **O caso que mais dói não precisa da tolerância.** `"ab cd    efghijklmno"`: o grupo de quatro
  brancos **cabia** dentro da margem e foi anexado pelo caminho feliz do line breaker. Era a
  justificação sozinha que o tirava de dentro dela — e ainda abria um vão de 60pt entre duas
  palavras, porque a largura daqueles quatro brancos virava sobra a distribuir
- **Os dados que provam o bug já estavam no repositório.** `"a  b   c    dddddddddddd"` era
  `InlineData` de dois testes da Fatia 4 e passava verde nos dois: um media só a tinta, o outro só a
  ida e volta coluna ↔ offset. O dado estava certo; faltava a asserção
- **A invariante é afirmada contra um número, não contra `AlignExtentPt`.** O alinhamento agora
  *usa* aquela conta para decidir onde parar; afirmar a garantia com ela esconderia um erro dela
- **A seleção pinta o branco pendurado**, e isso está certo: ele faz parte do trecho selecionado, e
  um destaque que parasse antes dele diria que ele não está lá. O que era errado é que numa linha
  justificada isso passava da margem pelo **grupo inteiro**; agora é um branco, por construção.
  `SelectionGeometry.InkEndPt` chamava de tinta o que era extensão crua, e o nome foi corrigido
  junto com a chamada
- **Fica devido, e é da Fatia 5a.1:** a justificação infla a `WidthPt` do segmento de branco
  enquanto `CaretGeometry.ColumnPt` mede o texto real, então o caret entre duas palavras fica
  `extraPt` à esquerda da seguinte. Com a sobra de volta ao normal isso é fração de ponto; era
  visível porque a sobra estava inflada por este bug

### Fatia 4.2 — Uma tecla volta a custar uma tecla ✅

Fatia corretiva. **Medir antes de mexer:** nada aqui foi otimizado sem número de antes e de depois.

- [x] A medição passa a descrever o aplicativo — preset `Abnt` ao lado do `Default`, corpus **com**
      marcação, e uma rajada encadeada de teclas, no começo, no meio, **no fim do parágrafo** e com
      a seta atravessando bloco
- [x] `LayoutEngine`: a guarda do caret compara o offset pós-edição com o intervalo **novo** do
      bloco, e a travessia de bloco passa a requebrar **dois** blocos em vez de recusar
- [x] Guard determinístico no Core, contando medições em vez de milissegundos

**A guarda do caret comparava duas convenções diferentes, e recusava quase sempre.** `dirtyWas` é o
intervalo **pré-edição** do bloco e `caretOffset` chega **pós-edição** — quem digita move o caret e
só então pede a repaginação. Digitar `d` no fim de um bloco `"abc"` dá `dirtyWas = (0,3)` e caret 4,
`Contains` falso, e o motor paginava as 300 folhas do zero: **toda tecla digitada no fim de um
parágrafo**, que é como se escreve. As duas metades da guarda continuam existindo porque comparam
coisas diferentes — o `RevealedBlock` veio do layout anterior e só casa com o intervalo antigo.

**Travessia de bloco custava o documento inteiro, e um bloco é uma linha.** O custo "uma repaginação
por travessia de bloco" foi aceito quando bloco queria dizer parágrafo; desde a Fatia 4.1 da Fase 3
— *uma linha da fonte é uma linha na página* — um bloco **é** uma linha da fonte, e o preço virou uma
repaginação completa por ↑/↓. `Rebuild` passa a requebrar **dois** blocos: o que perde a revelação e
o que a ganha. Dois, e não um, porque revelar é uma troca — e a armadilha é o `includeMarkup`, que
tem de vir do bloco **revelado** e não do bloco requebrado, senão quem perdeu a revelação sai ainda
mostrando o `## `. Tem teste, e ele fica vermelho com `includeMarkup: true`.

Medido com `--measure-layout`, milissegundos por tecla numa rajada de dez, melhor de três:

    ANTES                começo      meio   fim de parágrafo      seta
    Default, liso          17,0      12,9         88,6              —
    Default, marcado       24,5      20,0        100,0              —
    ABNT, liso             41,3      25,4        204,2              —
    ABNT, marcado          50,8      33,3        207,9              —

    DEPOIS               começo      meio   fim de parágrafo      seta
    Default, liso          17,1      12,5         12,6            6,5
    Default, marcado       24,3      19,6         20,1           13,4
    ABNT, liso             44,2      24,2         25,4            7,5
    ABNT, marcado          49,3      31,5         31,3           14,0

Registrado desta fatia:

- **O número que o roadmap publicava era 8,6 ms, e a configuração do aplicativo custava 207,9.**
  Vinte e quatro vezes, e não por uma regressão: o número sempre descreveu o preset do MVP, alinhado
  à esquerda, num corpus sem marcação, medindo **uma** tecla isolada no meio de um parágrafo. Três
  diferenças em relação ao que o autor faz, e cada uma escondia um caminho diferente
- **A rajada encadeada é o que revela recusa, e uma tecla isolada não revela.** Uma medição isolada
  sempre parte de um estado recém-paginado; a recusa aparece a partir da segunda tecla
- **O guard conta medições, não milissegundos.** Um teto de tempo passa verde numa máquina rápida
  com o reflow recusando toda tecla. A contagem é a propriedade que importa — *o caminho incremental
  foi tomado* —, não depende da máquina, e diz o porquê quando falha: a pior tecla custa **7**
  medições contra as **266.071** de uma paginação completa
- **Nenhum dos testes de reaproveitamento passava o caret pós-edição.** Todos usavam o mesmo caret
  no layout anterior e no novo, o que só descreve uma tecla digitada no meio do texto. O bug estava
  a um `InlineData` de distância de ser visto
- **Sobrou o deslocamento, e ele está medido**: a diferença entre digitar no começo (49,3 ms) e no
  meio (31,5 ms) do documento é `Shift` copiando run a run as linhas que vieram depois. Vira a
  Fatia 4.3 — é mudança de modelo, e não cabia junto de uma correção de guarda

### Fatia 4.3 — O deslocamento deixa de copiar run a run ✅

- [x] `LaidOutRun.SourceStart` virou `LineOffset`, **relativo à linha** — deslocar uma linha passou
      a ser uma cópia de record em vez de um array por linha
- [x] Teste estrutural: a linha reaproveitada e deslocada compartilha o **mesmo objeto** de runs

**O reflow reaproveita a medição, não a estrutura**, e foi por isso que a justificação o encareceu
sem que ninguém percebesse. `Shift` copiava cada run de cada linha depois do bloco sujo: à esquerda
são 1–2 runs por linha; **justificado são ~20**, porque o passe parte a linha em cada fronteira de
branco. Digitar no **meio** desloca metade do documento, e era exatamente ali que o sintoma aparecia.

**O campo foi renomeado, e isso é parte do conserto.** Trocar o significado de `SourceStart` sem
trocar o nome deixaria cada consumidor compilando e mentindo — e uma falha de offset não quebra o
desenho, quebra o caret. Com o nome novo, o compilador apontou os dezessete lugares.

Medido com `--measure-layout`, milissegundos por tecla:

    ANTES                começo      meio   fim de parágrafo      seta
    Default, liso          17,3      12,8         11,5            7,0
    Default, marcado       23,3      19,8         20,4           12,8
    ABNT, liso             43,6      22,8         25,0            7,1
    ABNT, marcado          48,2      30,5         31,6           13,4

    DEPOIS               começo      meio   fim de parágrafo      seta
    Default, liso          14,5      10,7         11,1            6,5
    Default, marcado       21,0      18,1         18,8           14,3
    ABNT, liso             15,2      12,1         12,2            8,1
    ABNT, marcado          20,6      19,0         18,5           15,8

Registrado desta fatia:

- **A coluna "começo" era a mais cara e deixou de ser.** Digitar no início do documento desloca
  todas as linhas; digitar no meio, metade. Com o deslocamento de graça, as duas colunas
  praticamente empatam (20,6 contra 19,0) — que é a forma da tabela dizendo que o custo saiu
- **`ABNT, liso` caiu 2,9×** (43,6 → 15,2) e é a medida limpa do que a justificação custava no
  deslocamento: as mesmas linhas, os mesmos blocos, só o número de runs por linha mudando
- **Os 409 testes passaram sem uma linha alterada**, salvo o único que afirmava a coordenada antiga.
  É a prova de equivalência que a troca precisava — o caret, a seleção, o `WordBoundaries` e o hit
  test são os mesmos por caminhos novos
- **O teste que pina o ganho é estrutural, não cronometrado:** a linha deslocada compartilha o
  **mesmo objeto** de runs com a anterior. Um teto de tempo mediria a máquina; `Assert.Same` mede a
  propriedade
- **Sobrou o parser, e ele está medido:** a diferença entre `liso` e `marcado` é de ~7ms por tecla
  em qualquer preset, e é o `MarkupParser` refazendo o documento inteiro — uma `Substring` por
  linha, uma `List` de delimitadores por linha e um LINQ por linha, num documento de 16 mil linhas.
  É o próximo candidato, e não desta fatia

### Fatia 5a — Marcação acadêmica inline ✅

A Fatia 5 se partiu em duas ao ser desenhada, e o corte não foi de conveniência: **a metade de
baixo precisa de capacidades que o motor não tem**, e a de cima não precisa de nenhuma. O que está
aqui é o que dá para mostrar exatamente como sairá impresso.

- [x] Sobrescrito no motor: `TextStyle.AsSuperscript()` e `BaselineRisePt`, aplicados pelos
      **dois** renderizadores — a tela e o PDF
- [x] `[^id]` — chamada de nota de rodapé, com o identificador sobrescrito
- [x] `$math$` — fórmula em itálico, que é como uma variável se compõe em texto matemático

**O que "pronto" significa aqui, dito antes de começar**, senão a fatia engole a fase: `$math$` sai
como run de estilo próprio, **não composto** — não há quem tipografe TeX no Avalonia, e um
compositor de fórmulas é uma fase inteira sozinho.

Registrado desta fatia:

- **`AcademicMarkup` roda antes do `InlineMarkup`, e não dentro dele.** Aquele é uma máquina de
  parear delimitadores repetidos; estes são trechos cercados, com abertura e fechamento distintos.
  Fatiar a linha primeiro e entregar os pedaços de texto comum para lá mantém as duas regras
  separadas — e é o que faz um `*` dentro de uma fórmula continuar sendo multiplicação
- **A linha sem notação nenhuma não paga nada.** Um teste de `Contains` antes de tudo, e ela vai
  direto para quem já cuidava dela: nem varredura de trechos, nem lista, nem substring
- **A subida do sobrescrito sai do `TextStyle`, não de cada renderizador.** São três leitores — o
  parser decide o corpo, a tela e o PDF levantam a baseline —, e uma constante em cada lugar seria
  uma para divergir das outras. O sintoma seria a chamada de nota fora de lugar **só no PDF**, ou
  só na tela
- **Custo aceito:** o motor mede a altura da linha pelo corpo do sobrescrito, que é menor, e não
  sabe da subida. Num corpo 12 o topo do glifo passa ~0,75pt acima da ascendente do texto que o
  envolve — dentro da folga do entrelinhamento de 1,5, e por isso não vale um campo a mais no
  `LineMetrics`. Com entrelinhamento simples, encosta
- **`[@cite]` saiu da fatia, e o motivo é estrutural.** Resolver `[@silva2020]` para `(Silva, 2020)`
  exige um run cujo **texto exibido é diferente do texto da fonte** — doze caracteres virando
  catorze. A invariante do `InlineRun` é `Text[i] ↔ SourceStart + i`, e `CaretGeometry.OffsetInRun`
  mede prefixos em cima dela: clicar numa citação resolvida devolveria um offset errado. Sem
  resolver, `[@cite]` seria só texto — e mostrar `silva2020` no meio de uma frase como se fosse
  prosa é a wrongness silenciosa que este projeto evita. Vai junto com a capacidade, na 5b
- **Pelo mesmo motivo, a nota não é numerada automaticamente.** O que aparece é o próprio
  identificador: `[^1]` mostra `1`, `[^nota]` mostra `nota`. `¹` não está no arquivo

### Fatia 5a.1 — O caret não pousa fora da linha ✅

Fatia corretiva: a marcação escondida deixava offsets **sem linha nenhuma**, e o caret era desenhado
no parágrafo de cima. A 5a alargou o buraco; a Fatia 4 já o tinha alargado antes.

- [x] As linhas de um bloco passam a cobri-lo **inteiro** — `LineBreaker.ClaimHiddenGaps`
- [x] Teste de propriedade: **todo offset do documento pertence a uma linha**, e o caret pousa nela
- [x] O resíduo da Fatia 4.1: numa linha justificada, o caret entre palavras encosta na seguinte

**O mapa offset → linha tem de ser total, e não era.** `BuildChunks` descarta os runs de marcação
quando ela está escondida, e cada linha nascia ancorada no primeiro chunk que de fato usou: num
bloco que começa com `# `, `:-: ` ou `**negrito**`, os primeiros offsets do bloco não pertenciam a
linha nenhuma. `FindLine` os resolve para a **última linha do bloco anterior** — e o caret aparece
no parágrafo de cima. É o mesmo caso que `ColumnPt` já tratava para a marcação no **meio** da linha
(`offset < run.SourceStart` → começo do run seguinte); o que faltava era a linha reivindicar o vão.

**O buraco não era só das pontas, e é isso que o teste de propriedade encontrou.** Quando a quebra
por largura cai em cima de marcação escondida no meio do bloco, a linha de cima termina antes dela e
a de baixo começa depois: o vão fica no **meio do parágrafo**. Um conserto só das pontas passaria
verde nos exemplos e continuaria errado no documento de verdade. A regra virou uma só: *cada linha
reivindica para trás até onde a anterior terminou*, e a primeira até o começo do bloco.

**Reivindicar para trás, e não para a frente**, tem consequência e ela é desejável: a fronteira entre
as duas linhas passa a ser **compartilhada**, exatamente como numa quebra por largura comum, e a
`CaretAffinity` já sabe desempatar. E o offset da marcação de uma palavra fica com a linha em que a
palavra está.

Conferido com o medidor **real** e o preset da ABNT, sobre um documento com marcação no começo, no
meio e no fim de blocos, blocos alinhados e grupos de brancos: **608 offsets, nenhum descoberto** e
nenhum caret na folha errada. Com `ClaimHiddenGaps` desligado, 16 offsets descobertos.

Registrado desta fatia:

- **Dois testes quebraram, e os dois estavam certos ao quebrar** — ambos codificavam o contorno
  antigo. `Heading_vazio_com_marcacao_escondida_ancora_no_texto` afirmava que a linha ancorava no
  texto, e a razão registrada na Fatia 5.1 era "senão o caret pousa antes da marcação que ele nem
  vê". **A premissa expirou na própria 5.1:** desde que a marcação é revelada no bloco do caret,
  quem pousa ali *vê*. O que sobrava do contorno era o buraco
- **Cobrir não é revelar.** A linha de um `## ` vazio cobre os três offsets do bloco e continua sem
  desenhar run nenhum — o teste afirma as duas coisas juntas, senão a correção seria confundida com
  "revelar sempre"
- **As pontas do bloco saem dos próprios runs**, e não de um parâmetro novo: a invariante do
  `InlineRun` é que todo caractere do bloco entra em exatamente um run, na ordem. O `LineBreaker`
  não precisou saber o que é um bloco
- **Saiu de escopo o `PreviousLine`/`NextLine` andando pelo índice**: o índice é da Fatia 5b, que
  não está na `main`. O item vai junto com ele, que é onde ele passa a fazer diferença
- **Saiu também "o caret mantém a última posição válida durante a repaginação".** Ele existia para
  esconder este bug: com o mapa total, o offset sempre tem linha, e o pior caso passa a ser o caret
  um quadro atrasado dentro da própria linha — que é o atraso que a tela inteira já tem

### Fatia 5b — Índice de linhas em ordem de fonte ✅

A Fatia 5 se partiu em três ao ser desenhada, e o corte não foi de conveniência: **cada metade de
baixo precisa de uma capacidade que o motor não tem**, e são capacidades diferentes. Esta é a de
baixo de todas, e não depende de nenhuma.

- [x] `PaginatedDocument.Index` — todas as linhas em **ordem de fonte**, construído no próprio
      construtor porque esquecer de construí-lo não daria erro, daria um caret que não acha onde
      pousar
- [x] `CaretGeometry.FindLine` por **busca binária** sobre ele, no lugar da varredura das folhas
- [x] `LayoutEngine` passa a andar pelo índice ao reaproveitar, e `PageBands` pelo `WithSameLines`

**A ordem de desenho e a ordem de fonte eram a mesma coisa até aqui**, e o motor inteiro se apoiava
nisso sem que estivesse escrito em lugar nenhum. Uma nota de rodapé desenhada na folha da chamada,
com a definição escrita no fim do arquivo, é o primeiro caso em que as duas divergem — e aí param
juntos o caret, a seleção, o `WordBoundaries` e o hit test. `PageLayout.Lines` passa a ser
oficialmente ordem de **desenho**, e o índice é a visão por ordem de **fonte**.

**Medido**, no documento de 300 páginas e 10.668 linhas, no pior caso da varredura — o último offset
do documento, com tudo antes dele para andar:

| | |
|---|---|
| por busca | **0,25 µs** |
| passos | **~14**, contra 10.668 da varredura |

Registrado desta fatia:

- **Os 410 testes existentes passaram sem uma linha alterada**, e é a prova de equivalência que a
  troca precisava: a busca binária devolve a mesma linha que a varredura devolvia, empates das
  linhas vazias inclusive — que o desempate por posição na folha preserva
- **A armadilha do `with { Pages = ... }` virou erro alto.** A cópia de record copia campos e não
  reexecuta o cálculo do índice, e um índice velho não dá exceção: dá um caret que pousa na linha
  errada. `WithSameLines` confere **por referência** que as listas de linhas são os mesmos objetos —
  que é exatamente a condição sob a qual reaproveitar o índice é correto — e é o que o passe de
  cabeçalho e rodapé faz. Antes isso era um comentário
- **O reaproveitamento anda pelo índice porque a ordem importa, não porque é mais rápido.** O laço
  casa linha com bloco, e blocos estão em ordem de fonte; achatar as folhas dava ordem de desenho.
  Hoje é a mesma coisa, e a medição confirma que a troca é neutra no tempo (a indireção por linha
  paga o array de dezesseis mil que deixou de ser materializado a cada tecla). Com a nota no pé da
  folha, é o que impede o reuso de mapear bloco errado para linha errada
- **A ordenação só acontece quando as duas ordens divergem**, que hoje é nunca: o passeio pelas
  folhas já sai ordenado, e conferir isso custa uma comparação por linha. Quando a nota chegar, é
  esse ramo que passa a valer
- **A correção que a 5a registrou está errada, e fica corrigida aqui.** Ela dizia que o "run com
  texto exibido diferente do da fonte" destravava a nota no pé da folha. **Não destrava:** o texto
  da definição é literal. Aquela capacidade destrava a *numeração automática* e o `[@cite]`
  resolvido, que são a Fatia 5d. A nota no pé da folha depende deste índice

### Fatia 5c — Notas de rodapé no pé da folha ⬜

- [ ] `[^id]: texto` como bloco próprio, fora do fluxo; **definição nunca chamada continua sendo
      parágrafo comum**, no lugar onde foi escrita — nenhum texto pode sumir da tela
- [ ] Reserva da altura das notas na folha, e as linhas delas assentadas no pé da área de conteúdo,
      abaixo de um filete
- [ ] `PreviousLine`/`NextLine` deixam de ser uma coisa só

**A reserva não precisa de laço de convergência, e essa é a boa surpresa.** A altura de uma nota é
conhecida *antes* de a linha que a chama ser assentada — basta quebrar as definições primeiro. O
page breaker decide com antecedência: se a linha **mais** as notas que ela estreia não cabem no que
resta, as duas descem juntas para a folha seguinte. O ponto fixo que o roadmap temia era do desenho
antigo, em que a nota era descoberta depois de a linha já estar posta.

**"Linha anterior" vira duas perguntas, e hoje é uma só.** `CaretGeometry.PreviousLine`/`NextLine`
andam pelas folhas — ordem de **desenho** —, e são usados por dois consumidores que querem coisas
diferentes: ↑/↓ querem a linha visualmente adjacente, e ←/→, a seleção e a afinidade querem a linha
**anterior no texto**. As duas coincidem enquanto não houver nota de rodapé, e por isso trocá-las
agora seria mudança sem como testar. Com a nota, o passeio por ordem de fonte sai do `Index`, que a
5b construiu, e o de desenho continua onde está — cada um com seu nome.

### Fatia 5d — Numeração automática e `[@cite]` resolvido ⬜

- [ ] Run com **texto exibido diferente do texto da fonte**, tratado como unidade indivisível pelo
      caret — `InlineRun` e `LaidOutRun` ganham um comprimento de fonte próprio, e `ColumnPt`,
      `OffsetInRun`, as setas e o `WordBoundaries` passam a resolver para as pontas
- [ ] `[^abc]` aparecendo como `¹`, numerado por ordem de chamada
- [ ] `[@silva2020]` resolvido contra um `.bib` simples, saindo como `(Silva, 2020)`

**A capacidade que falta tem nome:** hoje a invariante do `InlineRun` é `Text[i] ↔ SourceStart + i`,
e `CaretGeometry.OffsetInRun` mede prefixos em cima dela. Doze caracteres virando catorze fazem
clicar numa citação resolvida devolver um offset errado. O run sintético é a mesma ideia do marcador
de bloco `\page`, um nível abaixo: **atômico, o caret pousa nas pontas e as teclas de apagar o
tratam como unidade**.

**E ele precisa de um terceiro estado de visibilidade.** Hoje um run ou é marcação (some quando o
caret sai do bloco) ou é texto (fica sempre). A numeração pede o inverso do primeiro: um run que
aparece **só quando a marcação está escondida** — revelado, o autor vê `[^abc]`; escondido, vê `¹`.
O `IsMarkup` booleano vira três estados.

### Fatia 6 — Sumário automático ⬜

- [ ] `\toc` como marcador de bloco, reusando o que o `\page` já construiu
- [ ] Dois passes de layout, com o número de passes **fixo**

**`\toc` reusa `BlockMarkers` e o caminho do `PageBreakNode` inteiro:** linha atômica desenhada,
caret que pousa nela como unidade, teclas de apagar que removem o marcador inteiro. Nada disso
precisa ser escrito de novo.

**Mesmo ponto fixo das notas:** inserir o sumário empurra o texto e muda os números de página que o
próprio sumário mostra. Dois passes, e aceitar o erro de uma página quando ele acontecer —
perseguir o ponto fixo é um laço que pode não terminar, num caminho que roda a cada tecla.

### Fatia 7 — Modal de configurações e menu ⬜

- [ ] Modal de configurações, aberto por `Ctrl+,` e pelo menu, sobre as páginas e **sem nada
      editável**: duas seções, Atalhos e Formatação
- [ ] Seção Atalhos **gerada** do `KeyBindingRegistry`, não escrita à mão, mais a tabela de
      marcação (`# ` título, `**negrito**`, `:-:` centralizado, `\page`, `\toc`)
- [ ] Menu superior 'Opções': Abrir, Salvar, Salvar como, Exportar PDF, Configurações — todas já
      desenvolvidas quando esta fatia começa, que é a razão de ela ser a última

**A lista de atalhos é gerada porque uma lista escrita mente na primeira mudança de binding.** O
registry já mapeia chord → `CommandId`; falta um nome de exibição por comando. E é o mesmo
`CommandId` que o menu despacha — o comentário do próprio tipo prevê isso desde a Fase 3, e esta é
a hora de cobrá-lo: menu, modal e atalho pedindo `editor.save`, sem três caminhos para o mesmo
trabalho.

**A seção Formatação exibe o `TypographyPreset` de verdade**, e é por isso que ele veio na Fatia 1:
com o preset, a tabela mostra a formatação que está desenhando a folha, e não um exemplo. É a
precursora de "Normas configuráveis" — o que falta depois é torná-la editável, não construí-la.

```json
{
  "general": { "fontFamily": "Times New Roman", "fontSize": 12, "lineHeight": 1.5 },
  "title1":  { "fontFamily": "Times New Roman", "fontSize": 24, "bold": true }
}
```

**O modal é o primeiro uso real do `FocusScopeTracker`.** Com ele aberto, as setas não podem mover
o caret atrás dele — que é exatamente a distinção `Global`/`Editor` que o `ShortcutScope` carrega
desde a Fase 3, quando havia um painel só e ela parecia vacuosa.

---

## Ideias fora de escopo (por enquanto)

Nada aqui está prometido; é estacionamento para não perder a ideia nem inflar as fases acima.

- Justificação de texto Knuth-Plass — veio da Fase 6. A justificação da Fatia 4 já entrega o efeito
  visual (margem direita reta) sobre a quebra gulosa; o que KP acrescenta é escolher melhor *onde*
  quebrar, e sem hifenização o ganho em português é modesto. Volta quando uma linha justificada
  feia aparecer na tela — que é o critério com que a Fatia 5.3 decidiu a tolerância da margem
- Interpretador de tabelas markdown — veio da Fase 6, e é a maior feature que passou por lá.
  Contraria a decisão que mais sustenta o motor: uma tabela são N linhas da fonte formando **uma**
  grade, quando "uma linha da fonte é uma linha na página". E revelar a fonte de uma tabela ao
  caret entrar nela mudaria a **altura** do bloco — invariante que o roadmap afirma duas vezes, no
  heading e na marcação inline. Entra com fatia própria e com a decisão emendada por escrito, nunca
  como um item no meio de outra
- Legendas — vieram da Fase 6, Fatia 5. Sem imagem nem tabela no editor não há o que legendar, e
  uma legenda hoje seria só um parágrafo centralizado em corpo menor, que a marcação de alinhamento
  da Fatia 4 já faz. Voltam junto com o que elas descrevem, e aí trazem a numeração automática —
  que é a parte que o autor não consegue fazer à mão
- Sublinhado — Markdown não tem sintaxe para ele, e ABNT e APA usam itálico em texto corrido. A
  sintaxe se decide quando o uso aparecer
- Escape de marcação inline (`\*`), para um asterisco literal dentro de uma frase enfatizada
- Rolagem automática ao arrastar a seleção para fora da janela
- Seleção múltipla (`IReadOnlyList<SelectionRange>`) — veio da Fase 4. Multi-cursor é feature de
  code editor; num editor de tese a lista plural custaria indireção em cada tecla, cada desenho e
  cada edição por algo que talvez nunca venha
- Chords reais registrados (ex: `Ctrl+K, Ctrl+S`) — veio da Fase 4. `KeyBindingRegistry` já
  suporta N passos e tem teste; falta um comando que queira um chord, não máquina
- Árvore balanceada na piece list (só se o profiling exigir)
- Exportação DOCX
- Normas configuráveis (ABNT/APA): a Fatia 1 constrói o `TypographyPreset` e a Fatia 7 o exibe. O
  que falta é torná-lo **editável**, ter mais de um preset e um `PageSettings` que venha junto com
  cada um
- Trimming/ReadyToRun no publish para reduzir os 95MB do executável
