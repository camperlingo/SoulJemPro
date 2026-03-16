using System;
using System.IO;
using System.Net;
using System.Text;
using System.Threading.Tasks;

namespace SoulJemApp.Services
{
    public class WebServerService
    {
        private HttpListener? _listener;
        private bool _isRunning = false;
        
        // Questo è il "campanello" che suonerà quando arriva una richiesta
        public Action<string, string, string>? OnSongRequested; 

        public void Start(int port = 8080)
        {
            try
            {
                _listener = new HttpListener();
                // Ascolta su tutte le schede di rete (Wi-Fi, LAN) su quella porta
                _listener.Prefixes.Add($"http://*:{port}/"); 
                _listener.Start();
                _isRunning = true;
                Task.Run(ListenAsync);
                Console.WriteLine($"[WEB SERVER] 🌐 Motore Web avviato con successo sulla porta {port}!");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[WEB SERVER ERRORE] C'è un problema ad avviare il server (forse serve permessi di amministratore?): {ex.Message}");
            }
        }

        public void Stop()
        {
            _isRunning = false;
            _listener?.Stop();
            _listener?.Close();
        }

        private async Task ListenAsync()
        {
            while (_isRunning && _listener != null)
            {
                try
                {
                    var context = await _listener.GetContextAsync();
                    ProcessRequest(context);
                }
                catch { }
            }
        }

        private void ProcessRequest(HttpListenerContext context)
        {
            var request = context.Request;
            var response = context.Response;

            // Se il telefono chiede la pagina web, gliela disegniamo!
            if (request.HttpMethod == "GET")
            {
                ServeHtml(response);
            }
            // Se il telefono ha cliccato "INVIA", leggiamo i dati!
            else if (request.HttpMethod == "POST")
            {
                HandlePost(request, response);
            }
        }

        private void ServeHtml(HttpListenerResponse response)
        {
            // Questo è letteralmente il "Sito Web" che i clienti vedranno sul telefono! 📱
            string html = @"
            <!DOCTYPE html>
            <html lang='it'>
            <head>
                <meta charset='UTF-8'>
                <meta name='viewport' content='width=device-width, initial-scale=1.0'>
                <title>SoulJem - Richiesta Brano</title>
                <style>
                    body { font-family: Arial, sans-serif; background-color: #0D0D0D; color: #ffffff; text-align: center; padding: 20px; margin: 0;}
                    .container { background-color: #1A1A1A; padding: 25px; border-radius: 12px; max-width: 400px; margin: auto; box-shadow: 0 4px 15px rgba(0,0,0,0.8); border: 1px solid #333;}
                    h1 { color: #6200EE; margin-bottom: 5px;}
                    h3 { color: #FF9800; margin-top: 0; font-size: 14px; font-weight: normal;}
                    input, textarea { width: 90%; padding: 12px; margin: 10px 0; border-radius: 8px; border: 1px solid #444; background: #222; color: white; font-size: 16px;}
                    input:focus, textarea:focus { outline: none; border-color: #6200EE;}
                    button { background-color: #2E7D32; color: white; border: none; padding: 16px 20px; font-size: 18px; border-radius: 8px; cursor: pointer; width: 100%; font-weight: bold; margin-top: 10px;}
                    button:hover { background-color: #1B5E20; }
                </style>
            </head>
            <body>
                <div class='container'>
                    <h1>🎤 SOULJEM</h1>
                    <h3>Fai la tua Richiesta al DJ</h3>
                    <form method='POST' action='/'>
                        <input type='text' name='nome' placeholder='Il tuo Nome (es. Marco)' required autocomplete='off'>
                        <input type='text' name='canzone' placeholder='Artista e Titolo (es. Vasco - Sally)' required autocomplete='off'>
                        <textarea name='note' rows='3' placeholder='Note per il DJ (es. Tonalità +1, Versione acustica...) (Opzionale)'></textarea>
                        <button type='submit'>INVIA RICHIESTA 🚀</button>
                    </form>
                </div>
            </body>
            </html>";

            byte[] buffer = Encoding.UTF8.GetBytes(html);
            response.ContentType = "text/html";
            response.ContentLength64 = buffer.Length;
            response.OutputStream.Write(buffer, 0, buffer.Length);
            response.OutputStream.Close();
        }

        private void HandlePost(HttpListenerRequest request, HttpListenerResponse response)
        {
            try
            {
                using (var reader = new StreamReader(request.InputStream, request.ContentEncoding))
                {
                    string body = reader.ReadToEnd();
                    
                    string nome = ExtractValue(body, "nome");
                    string canzone = ExtractValue(body, "canzone");
                    string note = ExtractValue(body, "note");

                    // Suona il "campanello" verso la nostra interfaccia!
                    OnSongRequested?.Invoke(nome, canzone, note);

                    // Pagina di ringraziamento sul telefono
                    string html = @"
                    <!DOCTYPE html>
                    <html><head><meta charset='UTF-8'><meta name='viewport' content='width=device-width, initial-scale=1.0'>
                    <style>body { font-family: Arial; background: #0D0D0D; color: #4CAF50; text-align: center; padding: 50px; }</style>
                    </head><body>
                    <h1 style='font-size: 50px; margin: 0;'>✅</h1>
                    <h2>RICHIESTA INVIATA!</h2>
                    <p style='color: white;'>Il DJ ha ricevuto la tua canzone in console.</p>
                    <p style='color: white;'>Preparati a cantare!</p>
                    <button onclick='window.location.href=""/""' style='margin-top:30px; padding:15px; background:#6200EE; color:white; border:none; border-radius:8px; font-size: 16px; font-weight: bold;'>Nuova Richiesta</button>
                    </body></html>";

                    byte[] buffer = Encoding.UTF8.GetBytes(html);
                    response.ContentType = "text/html";
                    response.ContentLength64 = buffer.Length;
                    response.OutputStream.Write(buffer, 0, buffer.Length);
                    response.OutputStream.Close();
                }
            }
            catch { }
        }

        private string ExtractValue(string body, string key)
        {
            try {
                var pairs = body.Split('&');
                foreach (var pair in pairs) {
                    var parts = pair.Split('=');
                    if (parts[0] == key && parts.Length > 1) {
                        return Uri.UnescapeDataString(parts[1].Replace("+", " "));
                    }
                }
            } catch {}
            return "";
        }
    }
}
