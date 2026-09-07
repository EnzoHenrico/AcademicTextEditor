# Estrutura de diretórios

Padrão do projeto. **Pasta = namespace**, verificado pela guarda de namespaces do
`dev.sh` (roda no `check` e no `pre-commit`).

```
/AcademicEditor
├── AcademicEditor.slnx          # Solução central (formato .slnx do .NET 10)
├── Directory.Build.props        # TFM, Nullable, TreatWarningsAsErrors
├── .editorconfig  .gitignore
├── CLAUDE.md                    # Regras do projeto (fica na raiz por convenção da ferramenta)
├── dev.sh                       # Script local de build/test/run — NÃO versionado
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
│       └── Assets/              # Fontes, ícones, temas
│
└── tests/
    └── AcademicEditor.Core.Tests/   # Espelha as pastas do Core
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

**`tests/AcademicEditor.App.Tests/` ainda não existe.** Entra quando houver ViewModels e
controles para testar (Fase 4) — um projeto de testes vazio só seria peso morto que o gate
não executa.
