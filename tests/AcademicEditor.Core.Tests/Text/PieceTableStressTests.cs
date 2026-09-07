using System.Text;

using AcademicEditor.Core.Text;

namespace AcademicEditor.Core.Tests.Text;

/// <summary>
/// Teste diferencial: a piece table tem que concordar com um <see cref="StringBuilder"/> depois
/// de milhares de edições aleatórias.
/// </summary>
/// <remarks>
/// Casos escritos à mão cobrem as situações em que a gente pensou. Os erros de uma piece table
/// moram nas combinações que a gente não pensou — apagar exatamente na fronteira entre duas
/// peças que a inserção anterior acabou de criar, por exemplo. A seed é fixa para que uma falha
/// seja reproduzível.
/// </remarks>
public sealed class PieceTableStressTests
{
    [Theory]
    [InlineData(20260907)]
    [InlineData(1)]
    [InlineData(int.MaxValue)]
    public void Concorda_com_StringBuilder_apos_milhares_de_edicoes(int seed)
    {
        const string Initial = "O documento começa com algum conteúdo vindo do arquivo.\n";

        var random = new Random(seed);
        var table = new PieceTable(Initial);
        var reference = new StringBuilder(Initial);

        for (var step = 0; step < 2_000; step++)
        {
            // Insere com mais frequência do que apaga, senão o documento encolhe até vazio e
            // metade das operações vira no-op.
            if (reference.Length == 0 || random.Next(3) != 0)
            {
                var offset = random.Next(reference.Length + 1);
                var text = RandomText(random);

                table.Insert(offset, text);
                reference.Insert(offset, text);
            }
            else
            {
                var offset = random.Next(reference.Length);
                var length = random.Next(1, Math.Min(20, reference.Length - offset) + 1);

                table.Delete(offset, length);
                reference.Remove(offset, length);
            }

            Assert.Equal(reference.Length, table.Length);
        }

        Assert.Equal(reference.ToString(), table.CreateSnapshot().GetText());
    }

    [Fact]
    public void Snapshots_tirados_no_meio_do_caminho_continuam_corretos()
    {
        var random = new Random(42);
        var table = new PieceTable("início");
        var reference = new StringBuilder("início");
        var frozen = new List<(TextBufferSnapshot Snapshot, string Expected)>();

        for (var step = 0; step < 500; step++)
        {
            var offset = random.Next(reference.Length + 1);
            var text = RandomText(random);

            table.Insert(offset, text);
            reference.Insert(offset, text);

            if (step % 50 == 0)
            {
                frozen.Add((table.CreateSnapshot(), reference.ToString()));
            }
        }

        Assert.All(frozen, item => Assert.Equal(item.Expected, item.Snapshot.GetText()));
    }

    private static string RandomText(Random random)
    {
        const string Alphabet = "abcdef ÇÃ\n";

        return string.Create(random.Next(1, 12), random, static (span, rng) =>
        {
            for (var i = 0; i < span.Length; i++)
            {
                span[i] = Alphabet[rng.Next(Alphabet.Length)];
            }
        });
    }
}
