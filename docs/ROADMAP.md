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

### Fatia 5 — Undo/redo, arquivo e atalhos

- [ ] `UndoRedoStack` guardando delta estrutural da piece list, não cópias de texto
- [ ] Testes de undo/redo encadeado e de redo invalidado por edição nova
- [ ] `IDocumentStorage` + `FileDocumentStorage` async
- [ ] Save atômico: `.tmp` no mesmo diretório (mesmo volume) e `File.Move(..., overwrite: true)`
- [ ] Detecção de BOM/encoding e normalização CRLF/LF
- [ ] `FileDocumentStorageTests`: falha no meio da escrita não corrompe o original; round-trip
- [ ] `CommandId`, `ShortcutScope`, `ChordSequence`, `KeyBindingRegistry` (já suportando N passos)
- [ ] `ShortcutDispatcher` no `PreviewKeyDown` (tunneling) + `FocusScopeTracker`
- [ ] Bindings: `Ctrl+S`, `Ctrl+O`, `Ctrl+Z`/`Ctrl+Y`
- [ ] Diálogos nativos via `IStorageProvider`

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
