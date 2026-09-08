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

## Fase 3 — MVP 🔨

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
  parte do preset de norma da Fase 5. Entra junto com ele
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
  parágrafos passa a vir da linha em branco e do preset de norma da Fase 5 — o item continua lá,
  com outro desenho
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

### Fatia 6 — Fechamento da fase
- [ ] Teste manual end-to-end via `./dev.sh run`
- [ ] Documento longo (~300 páginas) para medir latência de repaginação e decidir se o
      reflow incremental precisa ser antecipado da Fase 4. Registrar o número medido aqui

---

## Fase 4 — Editor de verdade ⬜

- [ ] Reflow incremental (dirty-range em 3 níveis: parser → line-breaker → page-breaker)
- [ ] Highlighting em tempo real reaproveitando os `InlineRun` do AST (sem motor separado)
- [ ] Seleção múltipla (`IReadOnlyList<SelectionRange>`)
- [ ] Geometria exata do caret via `LaidOutLine.SourceStart` ↔ `TextLayout`
- [ ] Chords reais registrados (ex: `Ctrl+K, Ctrl+S`)
- [ ] Culling de páginas fora do viewport no `Render`

---

## Fase 5 — Documento acadêmico ⬜

- [ ] Cabeçalho/rodapé (populando os campos reservados desde a Fase 3)
- [ ] Numeração de página
- [ ] Extensões acadêmicas do markup: notas de rodapé, `[@cite]`, `$math$`, legendas
- [ ] Notas de rodapé no layout (segundo passe do page-breaker)
- [ ] Sumário automático (dois passes de layout)
- [ ] **Exportação PDF**, consumindo o mesmo `PaginatedDocument` — possível porque o motor
      de layout nasceu independente de tela

---

## Ideias fora de escopo (por enquanto)

Nada aqui está prometido; é estacionamento para não perder a ideia nem inflar as fases acima.

- Justificação de texto Knuth-Plass (o MVP usa greedy word-wrap)
- Árvore balanceada na piece list (só se o profiling exigir)
- Exportação DOCX
- Normas configuráveis (ABNT/APA) como presets de `PageSettings`
- Trimming/ReadyToRun no publish para reduzir os 95MB do executável
