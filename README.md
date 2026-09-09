# AcademicEditor

## Sobre o projeto

Editor de texto para desktop — Windows, Linux e macOS — feito para quem escreve trabalho
acadêmico: artigo, monografia, dissertação, tese.

Você escreve em Markdown e vê **a página como ela vai sair impressa**, folha por folha, enquanto
digita. Nada de exportar para conferir se ficou certo, nada de rolagem infinita: A4 com margens de
verdade, quebra de página no lugar certo, e o texto assentando no papel em tempo real.

Não é um editor de código com uma prévia ao lado, nem um processador de texto pesado. O motor que
divide o texto em linhas e distribui as linhas em folhas foi escrito do zero, para repaginar a cada
tecla sem engasgar — inclusive num documento de 300 páginas.

## TechStack

| | |
|---|---|
| Linguagem | C# / .NET 10 |
| Interface | Avalonia UI 12.1.2 — renderização Skia, mesma imagem nos três sistemas |
| Motor de texto | próprio: piece table, quebra de linha e paginação escritos do zero, sem editor de terceiros por baixo |
| Testes | xUnit — 301 testes, com o motor testado sem subsistema gráfico |
| Distribuição | executável self-contained (`linux-x64`, `win-x64`): roda sem instalar o .NET |

## Patch Notes — Fase 5: Mouse e seleção

O editor deixou de ser "protótipo em que se digita" e virou editor que se usa.

- **O mouse funciona.** Clique põe o cursor exatamente onde você clicou, e o ponteiro vira barra de
  texto sobre o papel e seta fora dele.
- **Seleção de texto.** Arrastar com o mouse, duplo clique para pegar a palavra, triplo para pegar
  a linha; pelo teclado, Shift com as setas, Home/End e PageUp/PageDown, e Ctrl+A para tudo.
- **Recortar, copiar e colar.** Ctrl+X, Ctrl+C e Ctrl+V. Digitar com um trecho selecionado
  substitui o trecho, e um Ctrl+Z devolve tudo de uma vez — não letra por letra.
- **Negrito e itálico aparecem formatados enquanto você escreve.** `**texto**` fica negrito e
  `*texto*` fica itálico na hora; os asteriscos só reaparecem na linha em que o cursor está, e
  somem quando você sai dela.

Nas fases anteriores tinham entrado abrir e salvar `.md`, desfazer e refazer, atalhos de teclado,
títulos, quebra de página manual — e o trabalho de desempenho que fez a tela acompanhar a digitação
num documento de trezentas páginas.

## Future Features — Fase 6: Documento acadêmico

O que vai mudar na tela para quem usa:

- **Cara de trabalho acadêmico.** A fonte, o corpo 12 e o espaço de 1,5 entre linhas que as normas
  pedem, aplicados ao documento inteiro. E uma barra embaixo da janela mostrando o estado do
  arquivo.
- **Cabeçalho, rodapé e número de página** em todas as folhas, mais a contagem de palavras e
  caracteres atualizada enquanto você escreve.
- **Exportar em PDF.** O arquivo gerado sai igual ao que você está vendo na tela, com o texto
  pesquisável.
- **Alinhamento do texto:** justificado, centralizado, à esquerda e à direita, com Ctrl+J
  alternando entre eles.
- **Recursos de escrita acadêmica no texto:** notas de rodapé no pé da página certa, citações,
  fórmulas e legendas.
- **Sumário automático**, com os títulos do trabalho e o número da página em que cada um está.
- **Tela de configurações e menu de opções.** A tela lista os atalhos e a formatação em uso; o menu
  reúne Abrir, Salvar, Exportar PDF e Configurações num lugar só, para não depender de decorar
  atalho.

---

Roadmap completo, fase a fase e com as decisões de projeto registradas, em
[`docs/ROADMAP.md`](docs/ROADMAP.md).
