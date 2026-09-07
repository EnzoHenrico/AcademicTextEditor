namespace AcademicEditor.App.Assets.Samples;

public static class Text
{
    // Conteúdo inicial até a Fatia 5 trazer abrir arquivo. Cada parágrafo é UMA linha da fonte,
    // porque é assim que o editor grava o que o autor digita: um \n é uma quebra visível. Quebrar
    // este texto à mão faria o motor mostrar as linhas curtas, fielmente.
   public const string UniqueFeatures = """
        # Paginação em tempo real

        Este parágrafo é uma única linha na fonte, e o motor de layout a quebra conforme a largura útil da página — a largura do papel menos as margens. Redimensionar a janela não muda nada aqui, porque a quebra acontece em pontos tipográficos sobre a geometria da folha, não sobre o tamanho da tela.

        ## Um título de segundo nível

        Títulos são mais altos que o corpo do texto, então consomem mais da altura útil da página. É por isso que a contagem de páginas depende do estilo de cada bloco, e não apenas da quantidade de caracteres do documento.

        A linha em branco acima e a de baixo existem de verdade: têm altura, ocupam espaço na folha e o caret pousa nelas.

        \page

        Esta folha começou por uma quebra de página explícita no markup.
        """;

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
