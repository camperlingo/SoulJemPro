using Avalonia;
using System;

namespace SoulJemApp;

sealed class Program
{
    // Initialization code. Don't use any Avalonia, third-party APIs or any
    // SynchronizationContext-reliant code before AppMain is called: things aren't initialized
    // yet and stuff might break.
    [STAThread]
    public static void Main(string[] args)
    {
        // --- INIZIO PATCH FFmpeg BLINDATO ---
        // Diciamo ad AutoGen di pescare le librerie .so ESATTAMENTE nella cartella dell'eseguibile,
        // ignorando quelle installate nel sistema operativo.
        FFmpeg.AutoGen.ffmpeg.RootPath = AppDomain.CurrentDomain.BaseDirectory;
        // --- FINE PATCH ---

        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    // Avalonia configuration, don't remove; also used by visual designer.
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace()
            // --- INIZIO PATCH OTTIMIZZAZIONE GRAFICA AVALONIA 11 ---
            // Su Avalonia 11 l'hardware GPU è già attivo di default. 
            // Qui diamo semplicemente molta più RAM Video (VRAM) al motore di disegno Skia
            // per gestire il doppio monitor a 60fps senza pesare sulla CPU dell'audio!
            .With(new SkiaOptions { 
                MaxGpuResourceSizeBytes = 8096000 * 256 
            });
            // --- FINE PATCH ---
}
