﻿namespace AcademicEditor.App.Assets.Samples;

public static class Text
{
    // Conteúdo inicial até a Fatia 5 trazer abrir arquivo. Cada parágrafo é UMA linha da fonte,
    // porque é assim que o editor grava o que o autor digita: um \n é uma quebra visível. Quebrar
    // este texto à mão faria o motor mostrar as linhas curtas, fielmente.
   private const string UniqueFeaturesSource = """
        # Paginação em tempo real

        Este parágrafo é uma única linha na fonte, e o motor de layout a quebra conforme a largura útil da página — a largura do papel menos as margens. Redimensionar a janela não muda nada aqui, porque a quebra acontece em pontos tipográficos sobre a geometria da folha, não sobre o tamanho da tela.

        ## Um título de segundo nível

        Títulos são mais altos que o corpo do texto, então consomem mais da altura útil da página. É por isso que a contagem de páginas depende do estilo de cada bloco, e não apenas da quantidade de caracteres do documento.

        A linha em branco acima e a de baixo existem de verdade: têm altura, ocupam espaço na folha e o caret pousa nelas.

        A marcação inline é **negrito**, *itálico* e ***os dois***, e ela aparece na linha onde o caret está — como o `##` de um título. Um asterisco sem par, ou um usado como multiplicação (2 * 3), continua sendo texto.

        A chamada de nota de rodapé é `[^1]` no meio da frase[^1], e o que aparece é o identificador, sobrescrito. Para declarar a nota, escreva `[^1]: o texto dela` sozinho numa linha, em qualquer lugar do arquivo — o rótulo entre os colchetes é o que casa uma com a outra, e pode ser qualquer palavra sem espaço. Uma fórmula vai entre cifrões — seja $E = mc^2$ a energia —, e sai em itálico, que é como uma variável se compõe. Nenhum dos dois é tipografado de verdade: o compositor de fórmulas é outra fase.

        [^1]: Esta definição está escrita aqui, no meio do arquivo, e é desenhada lá embaixo — no pé da folha em que a chamada aparece, abaixo do filete. Uma definição que ninguém chama continua sendo parágrafo comum, no lugar onde foi escrita. Editá-la é editar um parágrafo: o caret pousa nela, a seleção a cobre, e as setas a atravessam.

        Este parágrafo não tem marcação de alinhamento, então vale o padrão da norma — justificado, com as duas margens retas. A sobra de cada linha é distribuída entre os vãos entre palavras, e a última linha do parágrafo fica de fora: ela terminou porque o texto acabou, não porque a margem a interrompeu.

        :-: # Um título centralizado

        :-: Uma linha centralizada. O `:-: ` é a mesma notação que o Markdown usa para alinhar coluna de tabela, e some quando o caret sai da linha.

        -: Esta encosta na margem direita, e Ctrl+J percorre os quatro alinhamentos.

        \page

        Esta folha começou por uma quebra de página explícita no markup.
        """;

   // As duas variantes são derivadas de uma fonte só, e não escritas à mão, por dois motivos: é o
   // que garante que difiram APENAS no fim de linha — a propriedade sob teste — e é o que as torna
   // imunes a alguém regravar este arquivo com outro fim de linha. O raw string literal do C#
   // preserva o fim de linha do arquivo-fonte, e foi exatamente assim que um CRLF entrou aqui sem
   // ninguém notar.

   /// <summary>O texto como o editor o grava.</summary>
   public static readonly string UniqueFeaturesLf = UniqueFeaturesSource.Replace("\r\n", "\n");

   /// <summary>O mesmo texto vindo de um arquivo do Windows. Tem de dar a mesma tela.</summary>
   public static readonly string UniqueFeaturesCrLf = UniqueFeaturesLf.Replace("\n", "\r\n");

   public const string MarkdownFeatures = """"
        md_content = """# Header Nível 1 (#)
        ## Header Nível 2 (##)
        ### Header Nível 3 (###)
        #### Header Nível 4 (####)
        ##### Header Nível 5 (#####)
        ###### Header Nível 6 (######)

        ---

        ## 1. Ênfase e Estilização de Texto

        Aqui estão as variações comuns de formatação de texto em linha:

        * **Texto em Negrito** usando dois asteriscos (`**Negrito**`)
        * __Texto em Negrito__ usando dois underlines (`__Negrito__`)
        * *Texto em Itálico* usando um asterisco (`*Itálico*`)
        * _Texto em Itálico_ usando um underline (`_Itálico_`)
        * ***Texto em Negrito e Itálico*** usando três asteriscos (`***Negrito e Itálico***`)
        * ~~Texto Tachado (Strikethrough)~~ usando dois tiles (`~~Tachado~~`)
        * Código em linha (`Inline Code`) usando crases (`` `código` ``)
        * Texto em subscrito H<sub>2</sub>O e sobrescrito E = mc<sup>2</sup> (via tags HTML `<sub>` e `<sup>`)

        ---

        ## 2. Listas

        ### Lista Não Ordenada (Bullet Points)
        * Item de nível 1
            * Subitem de nível 2 (com 2 ou 4 espaços de recuo)
            * Subitem de nível 3
        * Outro item de nível 1
            - Usando hífen em vez de asterisco
            + Usando sinal de adição em vez de asterisco

        ### Lista Ordenada (Numerada)
        1. Primeiro item
        2. Segundo item
        3. Terceiro item
            1. Subitem ordenado A
            2. Subitem ordenado B
        4. Quarto item (a numeração automática ajusta se for tudo `1.`)

        ### Lista de Tarefas (Task List)
        - [x] Tarefa concluída (marcada com `x`)
        - [ ] Tarefa pendente
        - [ ] Outra tarefa pendente
            - [x] Subtarefa concluída
            - [ ] Subtarefa pendente

        ---
        
        ## 3. Citações (Blockquotes)

        > Esta é uma citação em bloco de nível 1.
        > Ela pode ocupar múltiplas linhas.
        >
        > > Esta é uma citação aninhada de nível 2.
        > > Pode ser usada para respostas ou trechos em destaque.
        >
        > Voltando para o nível 1 da citação.

        ---
        
        \page 

        ## 4. Links e Imagens

        * [Link inline com texto](https://www.example.com)
        * [Link com título no hover](https://www.example.com "Título do Link")
        * Link direto / Auto-link: <https://www.example.com>
        * E-mail direto: <usuario@exemplo.com>

        ### Imagem Inline
        ![Texto Alternativo da Imagem](https://via.placeholder.com/600x200.png?text=Exemplo+de+Imagem "Título da Imagem")

        ---

        ## 5. Blocos de Código (Code Blocks & Syntax Highlighting)

        ### Bloco de Código Genérico
        """";

}
