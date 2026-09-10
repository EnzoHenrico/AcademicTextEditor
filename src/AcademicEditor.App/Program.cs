using Avalonia;
using System;

namespace AcademicEditor.App;

class Program
{
    // Initialization code. Don't use any Avalonia, third-party APIs or any
    // SynchronizationContext-reliant code before AppMain is called: things aren't initialized
    // yet and stuff might break.
    [STAThread]
    public static void Main(string[] args)
    {
        // Modo de diagnóstico da Fatia 6: pagina um documento longo com o medidor real e imprime
        // os tempos, sem abrir janela. Ainda precisa da plataforma inicializada — o TextLayout
        // depende do gerenciador de fontes —, daí o SetupWithoutStarting em vez de medir direto.
        if (args.Contains("--measure-layout"))
        {
            BuildAvaloniaApp().SetupWithoutStarting();
            Diagnostics.LayoutBenchmark.Run(Console.Out);
            return;
        }

        if (args.Contains("--measure-render"))
        {
            BuildAvaloniaApp().SetupWithoutStarting();
            Diagnostics.LayoutBenchmark.RunRender(Console.Out);
            return;
        }

        // Conferência visual: grava uma folha como PNG, com o mesmo renderizador e a mesma
        // configuração que o aplicativo executa. Precisa da plataforma inicializada pelo mesmo
        // motivo das medições — o TextLayout depende do gerenciador de fontes.
        if (args is ["--screenshot", var target, .. var rest])
        {
            BuildAvaloniaApp().SetupWithoutStarting();

            var source = rest.FirstOrDefault(argument => !int.TryParse(argument, out _));
            var page = rest
                .Select(argument => int.TryParse(argument, out var number) ? number : 0)
                .FirstOrDefault(number => number > 0);

            var pages = Diagnostics.PageSnapshot.Capture(target, source, page == 0 ? 1 : page);

            Console.WriteLine($"folha {(page == 0 ? 1 : page)} de {pages} em {Path.GetFullPath(target)}");
            return;
        }

        if (args is ["--write-corpus", var path])
        {
            Diagnostics.LayoutBenchmark.WriteCorpus(path);
            Console.WriteLine($"corpus gravado em {Path.GetFullPath(path)}");
            return;
        }

        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    // Avalonia configuration, don't remove; also used by visual designer.
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
#if DEBUG
            .WithDeveloperTools()
#endif
            .WithInterFont()
            .LogToTrace();
}
