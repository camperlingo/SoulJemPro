# 🎤 SoulJem Pro v5.1 - Linux Edition

**SoulJem Pro** è un software avanzato di regia per Karaoke, Piano Bar e Live Music, progettato per offrire il controllo totale sulle esibizioni dal vivo. Costruito con C# e Avalonia UI, offre un'interfaccia moderna e fluida, ottimizzata per setup a doppio monitor.

## 🚀 Funzionalità Principali
* **Doppio Motore Indipendente:** Gestione separata per l'operatore (Preview) e per il pubblico (Sala a schermo intero) tramite il potentissimo motore `mpv`.
* **Smart Queue & Storico:** Gestione intelligente della coda cantanti, ripristino istantaneo in caso di errori e storico automatico della serata.
* **Live Pitching Integrato:** Modifica della tonalità in tempo reale su file audio e video senza perdite di sincronia, con sistema di "Smart Cache" tramite `ffmpeg` per alleggerire la CPU.
* **Web Download Engine:** Integrazione diretta con YouTube tramite `yt-dlp` per scaricare e processare le basi al volo, direttamente dalle richieste del pubblico.
* **Web Radio Integrata:** Intrattenimento musicale automatico tra un'esibizione e l'altra con dissolvenze incrociate.

## 🛠️ Requisiti di Sistema (Linux)
Per funzionare correttamente, SoulJem Pro richiede l'installazione delle seguenti dipendenze di sistema:
* **.NET 8.0 SDK/Runtime** (Per l'esecuzione dell'app Avalonia)
* **mpv** (Motore di riproduzione video/audio)
* **ffmpeg** (Motore per il processamento del Pitch)
* **yt-dlp** (Motore per il download delle basi dal web)
* **wmctrl** e **xrandr** (Per la gestione avanzata delle finestre sul secondo monitor HDMI)

## 📦 Installazione
1. Clona questo repository sul tuo PC.
2. Assicurati di avere le dipendenze installate (su base Debian/Ubuntu: `sudo apt install mpv ffmpeg wmctrl xrandr`).
3. Compila ed esegui il progetto tramite la CLI di .NET:
   ```bash
   dotnet run --project SoulJemApp
