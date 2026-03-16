using Avalonia.Controls;
using Avalonia.Media.Imaging;
using System;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;

namespace SoulJemApp.Views
{
    public partial class QrCodeWindow : Window
    {
        public QrCodeWindow() { InitializeComponent(); }

        public QrCodeWindow(string url) : this()
        {
            var urlText = this.FindControl<TextBlock>("UrlText");
            if (urlText != null) urlText.Text = url;
            _ = LoadQrCodeAsync(url);
        }

        private async Task LoadQrCodeAsync(string url)
        {
            try
            {
                // Formattiamo l'URL in modo sicuro per internet
                string encodedUrl = Uri.EscapeDataString(url);
                string qrApi = $"https://api.qrserver.com/v1/create-qr-code/?size=500x500&data={encodedUrl}";
                
                using var client = new HttpClient();
                client.Timeout = TimeSpan.FromSeconds(10); // Salvavita 1: se ci mette troppo, molla la presa.
                
                // Scarichiamo solo i byte grezzi
                byte[] bytes = await client.GetByteArrayAsync(qrApi);
                
                var imageControl = this.FindControl<Image>("QrImage");
                if (imageControl != null)
                {
                    // Passiamo la patata bollente alla Scheda Grafica in modo sicuro
                    Avalonia.Threading.Dispatcher.UIThread.Post(() => {
                        try 
                        {
                            // LA MAGIA: Creiamo la memoria QUI dentro, così non scompare!
                            using var ms = new MemoryStream(bytes);
                            imageControl.Source = new Bitmap(ms);
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"[ERRORE GRAFICO] Impossibile disegnare il QR Code: {ex.Message}");
                        }
                    });
                }
            }
            catch (Exception e) 
            { 
                Console.WriteLine($"[ERRORE RETE] Impossibile scaricare il QR Code. (Forse niente internet?): {e.Message}"); 
            }
        }
    }
}
