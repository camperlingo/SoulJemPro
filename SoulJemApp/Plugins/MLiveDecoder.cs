using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SoulJemApp.Plugins
{
    public class MLiveDecoder
    {
        private class MidiEvent
        {
            public long AbsoluteTick;
            public byte Status;
            public byte Param1;
            public byte Param2;
            public byte[] MetaData = Array.Empty<byte>();
            public bool IsCC => (Status & 0xF0) == 0xB0;
            public int Channel => Status & 0x0F;
        }

        public static async Task<string> UnlockFileAsync(string inputPath)
        {
            return await Task.Run(() =>
            {
                byte[] data = File.ReadAllBytes(inputPath);
                if (data.Length < 14 || Encoding.ASCII.GetString(data, 0, 4) != "MThd")
                    throw new Exception("File non è un MIDI valido.");

                int nTrks = (data[10] << 8) | data[11];
                int division = (data[12] << 8) | data[13];

                int pos = 14;
                var allEvents = new List<MidiEvent>();

                // 1. LETTURA BINARIA DEL MIDI
                for (int i = 0; i < nTrks; i++)
                {
                    if (pos >= data.Length || Encoding.ASCII.GetString(data, pos, 4) != "MTrk") break;
                    pos += 4;
                    int trkLen = (data[pos] << 24) | (data[pos + 1] << 16) | (data[pos + 2] << 8) | data[pos + 3];
                    pos += 4;

                    int endPos = pos + trkLen;
                    long currentTick = 0;
                    byte runningStatus = 0;

                    while (pos < endPos)
                    {
                        currentTick += ReadVlq(data, ref pos);
                        byte status = data[pos];
                        
                        if ((status & 0x80) == 0) { status = runningStatus; } 
                        else { pos++; if (status < 0xF0) runningStatus = status; }

                        var ev = new MidiEvent { AbsoluteTick = currentTick, Status = status };

                        if (status == 0xFF) // Meta Event
                        {
                            byte type = data[pos++];
                            int len = ReadVlq(data, ref pos);
                            ev.Param1 = type;
                            ev.MetaData = new byte[len];
                            Array.Copy(data, pos, ev.MetaData, 0, len);
                            pos += len;
                            allEvents.Add(ev);
                        }
                        else if (status == 0xF0 || status == 0xF7) // SysEx
                        {
                            int len = ReadVlq(data, ref pos);
                            pos += len; 
                        }
                        else
                        {
                            int hi = status & 0xF0;
                            ev.Param1 = data[pos++];
                            if (hi != 0xC0 && hi != 0xD0) ev.Param2 = data[pos++];
                            allEvents.Add(ev);
                        }
                    }
                }

                // 2. ESTRAZIONE TESTO (CC 99)
                var cc99 = allEvents.Where(e => e.IsCC && e.Param1 == 99 && e.Param2 >= 32 && e.Param2 <= 126).OrderBy(e => e.AbsoluteTick).ToList();
                if (cc99.Count == 0) return inputPath; // Non è M-Live offuscato

                // 3. INTELLIGENZA ARTIFICIALE: RICONOSCIMENTO DEL LUCCHETTO
                var cc31 = allEvents.Where(e => e.IsCC && e.Param1 == 31).ToList();
                var cc80 = allEvents.Where(e => e.IsCC && e.Param1 == 80).ToList();
                var cc64 = allEvents.Where(e => e.IsCC && e.Param1 == 64 && e.Channel != 0).ToList(); // Sustain ma NON sul piano

                List<MidiEvent> triggers = cc31;
                string lockType = "CC31 (Standard)";

                if (cc80.Count > cc31.Count) { triggers = cc80; lockType = "CC80 (Note Nascoste)"; }
                if (cc64.Count > triggers.Count && cc64.Count > 10) { triggers = cc64; lockType = "CC64 (Pedale Anomalo)"; }

                triggers = triggers.OrderBy(e => e.AbsoluteTick).ToList();
                
                Console.ForegroundColor = ConsoleColor.Magenta;
                Console.WriteLine($"[OMNI-BRAIN] Rilevato formato M-Live. Lucchetto decifrato: {lockType}");
                Console.ResetColor();

                // 4. RICOSTRUZIONE SINCRONIZZATA (Le sillabe vengono incastrate ai tempi esatti dei trigger)
                var newTrack = new List<byte>();
                long lastTick = 0;
                
                // Creiamo le sillabe in formato standard KAR/MID (Meta 0x05)
                int textIdx = 0;
                foreach (var trigger in triggers)
                {
                    if (textIdx >= cc99.Count) break;
                    if (trigger.Param2 == 0 || trigger.Param2 == 127) continue; // Segnali di reset

                    long delta = trigger.AbsoluteTick - lastTick;
                    lastTick = trigger.AbsoluteTick;
                    
                    newTrack.AddRange(WriteVlq((int)delta));
                    newTrack.Add(0xFF);
                    newTrack.Add(0x05); // Standard Lyric Meta
                    
                    // Raccoglie i caratteri fino al prossimo spazio
                    string syllable = "";
                    while (textIdx < cc99.Count)
                    {
                        char c = (char)cc99[textIdx].Param2;
                        syllable += c;
                        textIdx++;
                        if (c == ' ' || c == '-') break;
                    }

                    byte[] textBytes = Encoding.Default.GetBytes(syllable);
                    newTrack.AddRange(WriteVlq(textBytes.Length));
                    newTrack.AddRange(textBytes);
                }

                newTrack.AddRange(WriteVlq(0));
                newTrack.AddRange(new byte[] { 0xFF, 0x2F, 0x00 }); // End of Track

                // 5. SALVATAGGIO DEL KARAOKE UNIVERSALE E PULITO
                string dir = Path.GetDirectoryName(inputPath) ?? "";
                string name = Path.GetFileNameWithoutExtension(inputPath);
                string outPath = Path.Combine(dir, $"{name}_Sbloccato.kar");

                using (var ms = new MemoryStream())
                {
                    ms.Write(Encoding.ASCII.GetBytes("MThd"), 0, 4);
                    ms.Write(new byte[] { 0, 0, 0, 6 }, 0, 4);
                    ms.Write(new byte[] { 0, 1, 0, 2 }, 0, 4); // Format 1, 2 Tracks
                    ms.Write(data, 12, 2); // Division

                    // Traccia Master (Musica pulita dai CC di controllo testo)
                    var cleanMusic = allEvents.Where(e => !(e.IsCC && (e.Param1 == 99 || e.Param1 == 31 || e.Param1 == 80))).OrderBy(e => e.AbsoluteTick).ToList();
                    var musicBytes = BuildTrack(cleanMusic);
                    ms.Write(Encoding.ASCII.GetBytes("MTrk"), 0, 4);
                    ms.Write(BitConverter.GetBytes(System.Net.IPAddress.HostToNetworkOrder(musicBytes.Length)), 0, 4);
                    ms.Write(musicBytes, 0, musicBytes.Length);

                    // Traccia Testo Perfetta
                    ms.Write(Encoding.ASCII.GetBytes("MTrk"), 0, 4);
                    ms.Write(BitConverter.GetBytes(System.Net.IPAddress.HostToNetworkOrder(newTrack.Count)), 0, 4);
                    ms.Write(newTrack.ToArray(), 0, newTrack.Count);

                    File.WriteAllBytes(outPath, ms.ToArray());
                }

                return outPath;
            });
        }

        private static int ReadVlq(byte[] data, ref int pos)
        {
            int value = 0;
            while (true)
            {
                byte b = data[pos++];
                value = (value << 7) | (b & 0x7F);
                if ((b & 0x80) == 0) break;
            }
            return value;
        }

        private static byte[] WriteVlq(int value)
        {
            var buf = new List<byte> { (byte)(value & 0x7F) };
            value >>= 7;
            while (value > 0) { buf.Insert(0, (byte)((value & 0x7F) | 0x80)); value >>= 7; }
            return buf.ToArray();
        }

        private static byte[] BuildTrack(List<MidiEvent> events)
        {
            var trk = new List<byte>();
            long lastTick = 0;
            foreach (var e in events)
            {
                trk.AddRange(WriteVlq((int)(e.AbsoluteTick - lastTick)));
                lastTick = e.AbsoluteTick;
                trk.Add(e.Status);
                if (e.Status == 0xFF) { trk.Add(e.Param1); trk.AddRange(WriteVlq(e.MetaData.Length)); trk.AddRange(e.MetaData); }
                else if (e.Status == 0xF0 || e.Status == 0xF7) { trk.AddRange(WriteVlq(e.MetaData.Length)); trk.AddRange(e.MetaData); }
                else { trk.Add(e.Param1); if ((e.Status & 0xF0) != 0xC0 && (e.Status & 0xF0) != 0xD0) trk.Add(e.Param2); }
            }
            trk.AddRange(WriteVlq(0)); trk.AddRange(new byte[] { 0xFF, 0x2F, 0x00 });
            return trk.ToArray();
        }
    }
}
