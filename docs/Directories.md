# Estrutura de diretórios

Padrão do projeto. **Pasta = namespace**, verificado pela guarda de namespaces do
`dev.sh` (roda no `check` e no `pre-commit`).

```
/AcademicEditor
├── AcademicEditor.slnx          # Solução central (formato .slnx do .NET 10)
├── Directory.Build.props        # TFM, Nullable, TreatWarningsAsErrors
├── .editorconfig  .gitignore
├── CLAUDE.md                    # Regras do projeto (fica na raiz por convenção da ferramenta)
├── dev.sh                       # Build, testes, run e publish multiplataforma
├── docs/                        # context.md, ROADMAP.md, este arquivo
│
├── src/
│   ├── AcademicEditor.Core/     # O motor (Class Library) — ZERO referência a Avalonia
│   │   ├── Text/                # PieceTable, Piece, TextBufferSnapshot, EditorDocument
│   │   ├── Parsing/             # Tokenizer, parser de markup
│   │   │   └── Ast/             # ParagraphNode, HeadingNode, InlineRun
│   │   ├── Layout/              # Motor de paginação: PageSettings, LineBreaker,
│   │   │   │                    # PageBreaker, LayoutEngine, ITextMeasurer
│   │   │   └── Model/           # PaginatedDocument, PageLayout, LaidOutLine
│   │   ├── IO/                  # IDocumentStorage, FileDocumentStorage (async, save atômico)
│   │   ├── Input/               # Atalhos agnósticos de UI: ChordSequence, KeyBindingRegistry
│   │   └── State/               # UndoRedoStack, estado de sessão
│   │
│   └── AcademicEditor.App/      # Camada visual (Avalonia)
│       ├── Program.cs           # Entry point
│       ├── App.axaml(.cs)       # Estilos globais e recursos
│       ├── app.manifest
│       ├── Views/               # Janelas e painéis (.axaml + code-behind)
│       ├── ViewModels/          # MVVM leve, sem framework
│       ├── Controls/            # Controles customizados (PageSurface)
│       ├── Rendering/           # PageRenderer, AvaloniaTextMeasurer
│       ├── Input/               # ShortcutDispatcher, FocusScopeTracker
│       ├── Diagnostics/         # Medições que precisam do medidor real (LayoutBenchmark)
│       └── Assets/              # Fontes, ícones, temas
│
└── tests/
    ├── AcademicEditor.Core.Tests/   # Espelha as pastas do Core
    └── AcademicEditor.App.Tests/    # Espelha as pastas do App
```

## Decisões

**Sem pasta `Interfaces/`.** Cada contrato fica junto de quem o implementa: `ITextMeasurer`
em `Layout/`, `IDocumentStorage` em `IO/`. Agrupar por natureza técnica separaria o contrato
da implementação e faria toda navegação virar dois saltos de pasta.

**Interface só quando ela paga por si.** `IDocumentStorage` e `ITextMeasurer` existem para
permitir fakes nos testes e manter o Avalonia fora do Core. Já um `ITextBuffer` com uma única
implementação (`PieceTable`) seria abstração especulativa — não criar.

**`Layout/` é topo de nível.** É o subsistema mais complexo do projeto e o diferencial do
produto; não é um detalhe de `Text/`.

**`tests/AcademicEditor.App.Tests/` existe desde a Fase 6, Fatia 1.** Ficou de fora até haver
asserção que valesse a pena: o roadmap acumulou quatro argumentos para ele — o `availableSize`
infinito, o teto de latência da repaginação, o clipboard e a conversão DIP↔pt —, e o quarto é o
primeiro com uma propriedade de uma linha (`HitTest(CaretRectDip(c)) == c`). O que se testa aqui é
o que o Core não alcança: `PageRenderer` depende de `Rect` do Avalonia, e `Core.Tests` não
referencia o App. Não precisa de subsistema gráfico — os tipos de geometria do Avalonia se
constroem sem plataforma inicializada.
