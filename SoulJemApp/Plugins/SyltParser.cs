using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace SoulJemApp.Plugins
{
    // Struttura che conterrà le singole sillabe e il momento esatto in cui cantarle
    public class SyltEvent
    {
        public int TimeMs { get; set; }
        public string Text { get; set; } = "";
    }

    public static class SyltParser
    {
        public static List<SyltEvent> ExtractLyrics(string filePath)
        {
            var events = new List<SyltEvent>();
            try
            {
                // Leggiamo i byte dell'MP3 direttamente in RAM (velocità fulminea)
                byte[] data = File.ReadAllBytes(filePath); 
                
                // Cerchiamo l'intestazione ID3 ovunque si trovi (come faceva il tuo Python)
                int startIdx = IndexOf(data, new byte[] { (byte)'I', (byte)'D', (byte)'3' });
                if (startIdx == -1) return events; // Non è un MP3 taggato

                // Calcoliamo la dimensione totale del blocco ID3 (Synchsafe integer)
                int tagSize = SynchsafeToInt(data, startIdx + 6);
                int pos = startIdx + 10;
                int maxPos = Math.Min(startIdx + 10 + tagSize, data.Length);

                // Scansione dei "Frame" interni finché non troviamo SYLT
                while (pos + 10 <= maxPos)
                {
                    string fid = Encoding.ASCII.GetString(data, pos, 4);
                    // Dimensione del frame (4 bytes big-endian)
                    int size = (data[pos+4] << 24) | (data[pos+5] << 16) | (data[pos+6] << 8) | data[pos+7];
                    
                    if (string.IsNullOrWhiteSpace(fid.Replace("\0", "")) || size <= 0 || pos + 10 + size > maxPos)
                        break;

                    if (fid == "SYLT")
                    {
                        events = ParseSyltFrame(data, pos + 10, size);
                        break; // SYLT trovato! Possiamo smettere di leggere il file.
                    }
                    pos += 10 + size;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[SYLT PARSER] Errore lettura MP3: {ex.Message}");
            }
            return events;
        }

        private static List<SyltEvent> ParseSyltFrame(byte[] data, int start, int size)
        {
            var items = new List<SyltEvent>();
            try
            {
                int p = start;
                byte enc = data[p++]; // Encoding (0 = ISO, 1 = UTF16, 3 = UTF8)
                
                p += 3; // Salta la lingua (es. "ita" o "eng")
                byte tsFmt = data[p++]; // Formato tempo (1 = frame, 2 = millisecondi)
                byte cType = data[p++]; // Tipo di contenuto (Karaoke)
                
                // Salta la descrizione testuale fino al terminatore NULL
                while (p < start + size && data[p] != 0) p++;
                p++; // Salta lo zero

                // Inizio estrazione delle sillabe!
                while (p + 4 < start + size)
                {
                    int txtStart = p;
                    while (p < start + size && data[p] != 0) p++;
                    
                    string text = "";
                    if (p > txtStart)
                    {
                        if (enc == 0 || enc == 3) // ISO o UTF-8
                            text = Encoding.UTF8.GetString(data, txtStart, p - txtStart);
                        else if (enc == 1) // UTF-16
                            text = Encoding.Unicode.GetString(data, txtStart, p - txtStart);
                    }
                    
                    p++; // Salta lo zero della stringa

                    if (p + 4 > start + size) break;

                    // Legge il timestamp esatto della sillaba (4 bytes big-endian)
                    int time = (data[p] << 24) | (data[p+1] << 16) | (data[p+2] << 8) | data[p+3];
                    p += 4;

                    if (!string.IsNullOrEmpty(text))
                    {
                        items.Add(new SyltEvent { TimeMs = time, Text = text });
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[SYLT PARSER] Errore decodifica frame SYLT: {ex.Message}");
            }
            return items;
        }

        private static int SynchsafeToInt(byte[] data, int offset)
        {
            int val = 0;
            for (int i = 0; i < 4; i++)
            {
                val = (val << 7) | (data[offset + i] & 0x7F);
            }
            return val;
        }

        private static int IndexOf(byte[] array, byte[] pattern)
        {
            for (int i = 0; i <= array.Length - pattern.Length; i++)
            {
                bool match = true;
                for (int j = 0; j < pattern.Length; j++)
                {
                    if (array[i + j] != pattern[j])
                    {
                        match = false;
                        break;
                    }
                }
                if (match) return i;
            }
            return -1;
        }
    }
}
