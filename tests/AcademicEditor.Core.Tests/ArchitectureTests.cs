using System.Reflection;

namespace AcademicEditor.Core.Tests;

public sealed class ArchitectureTests
{
    // O Core precisa rodar sem subsistema gráfico — é o que mantém estes testes em
    // milissegundos — e ser reutilizável por um exportador PDF/CLI depois. Uma
    // dependência de Avalonia quebraria as duas coisas.
    //
    // O dev.sh já barra isso lendo o .csproj; aqui a verificação é sobre o assembly
    // compilado, que é a verdade final sobre o que o Core realmente carrega.
    [Fact]
    public void Core_nao_referencia_Avalonia()
    {
        var core = Assembly.Load("AcademicEditor.Core");

        var avaloniaRefs = core.GetReferencedAssemblies()
            .Select(name => name.Name)
            .Where(name => name is not null && name.StartsWith("Avalonia", StringComparison.OrdinalIgnoreCase))
            .ToArray();

        Assert.Empty(avaloniaRefs);
    }
}
