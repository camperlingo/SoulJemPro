using Avalonia.Media.Imaging;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;

namespace SoulJemApp.Models
{
    public class YouTubeResultItem
    {
        public string Title { get; set; } = "Sconosciuto";
        public string Url { get; set; } = "";
        public string Duration { get; set; } = "00:00";
        public string ThumbnailUrl { get; set; } = "";
        public Bitmap? ThumbnailImage { get; set; } // L'immagine vera e propria per l'interfaccia

        // Funzione per scaricare la copertina al volo
        public async Task LoadImageAsync()
        {
            if (string.IsNullOrEmpty(ThumbnailUrl)) return;
            try
            {
                using var client = new HttpClient();
                var bytes = await client.GetByteArrayAsync(ThumbnailUrl);
                using var ms = new MemoryStream(bytes);
                ThumbnailImage = new Bitmap(ms);
            }
            catch { }
        }
    }
}
