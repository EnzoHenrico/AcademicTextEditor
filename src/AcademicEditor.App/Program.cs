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
