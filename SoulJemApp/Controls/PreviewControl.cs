using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using SoulJemApp.Plugins;

namespace SoulJemApp.Controls
{
    public class PreviewControl : Control
    {
        private WriteableBitmap? _bitmap;
        private readonly object _bitmapLock = new object();
        
        public List<SyltEvent> SyltEvents { get; set; } = new List<SyltEvent>();
        public double CurrentTime { get; set; }

        public void PushFrame(byte[] pixels, int width, int height)
        {
            lock (_bitmapLock)
            {
                if (_bitmap == null || _bitmap.PixelSize.Width != width || _bitmap.PixelSize.Height != height)
                {
                    _bitmap?.Dispose();
                    _bitmap = new WriteableBitmap(new PixelSize(width, height), new Vector(96, 96), PixelFormats.Bgra8888, AlphaFormat.Opaque);
                }
                using (var buf = _bitmap.Lock())
                {
                    Marshal.Copy(pixels, 0, buf.Address, pixels.Length);
                }
            }
            Dispatcher.UIThread.Post(InvalidateVisual, DispatcherPriority.Render);
        }

        public void ClearScreen()
        {
            lock (_bitmapLock)
            {
                _bitmap?.Dispose();
                _bitmap = null;
            }
            SyltEvents.Clear(); 
            Dispatcher.UIThread.Post(InvalidateVisual, DispatcherPriority.Render);
        }

        public override void Render(DrawingContext context)
        {
            base.Render(context);

            lock (_bitmapLock)
            {
                if (_bitmap != null)
                {
                    double scaleX = Bounds.Width / _bitmap.Size.Width;
                    double scaleY = Bounds.Height / _bitmap.Size.Height;
                    double scale = Math.Min(scaleX, scaleY); 

                    double drawWidth = _bitmap.Size.Width * scale;
                    double drawHeight = _bitmap.Size.Height * scale;
                    double drawX = (Bounds.Width - drawWidth) / 2;
                    double drawY = (Bounds.Height - drawHeight) / 2;

                    var sourceRect = new Rect(0, 0, _bitmap.Size.Width, _bitmap.Size.Height);
                    var destRect = new Rect(drawX, drawY, drawWidth, drawHeight);

                    context.FillRectangle(Brushes.Black, new Rect(0, 0, Bounds.Width, Bounds.Height));
                    context.DrawImage(_bitmap, sourceRect, destRect);
                }
                else
                {
                    context.FillRectangle(Brushes.Black, new Rect(0, 0, Bounds.Width, Bounds.Height));
                }
            }

            if (SyltEvents != null && SyltEvents.Count > 0)
            {
                DrawNativeKaraoke(context);
            }
        }

        private void DrawNativeKaraoke(DrawingContext context)
        {
            double currentMs = CurrentTime * 1000;
            
            var lines = new List<List<SyltEvent>>();
            var currentLine = new List<SyltEvent>();
            
            foreach (var ev in SyltEvents)
            {
                string cleanText = ev.Text.Replace("\n", "").Replace("\r", "");
                if (ev.Text.Contains("\n") || ev.Text.Contains("\r"))
                {
                    if (currentLine.Count > 0) lines.Add(currentLine);
                    currentLine = new List<SyltEvent>();
                    if (!string.IsNullOrWhiteSpace(cleanText)) 
                        currentLine.Add(new SyltEvent { TimeMs = ev.TimeMs, Text = cleanText });
                }
                else
                {
                    currentLine.Add(new SyltEvent { TimeMs = ev.TimeMs, Text = cleanText });
                }
            }
            if (currentLine.Count > 0) lines.Add(currentLine);

            if (lines.Count == 0) return;

            int activeIdx = -1;
            for (int i = 0; i < lines.Count; i++)
            {
                double start = lines[i].First().TimeMs;
                double end = (i < lines.Count - 1) ? lines[i + 1].First().TimeMs : start + 5000;
                
                if (currentMs >= start - 1500 && currentMs < end) 
                {
                    activeIdx = i;
                    break;
                }
            }

            if (activeIdx == -1) activeIdx = lines.FindIndex(l => l.First().TimeMs > currentMs);

            if (activeIdx >= 0 && activeIdx < lines.Count)
            {
                // TESTO CENTRATO: Riga principale al 45% (quasi centro esatto)
                DrawLine(context, lines[activeIdx], currentMs, Bounds.Height * 0.45, true);
                
                // Riga successiva posizionata al 60% (subito sotto)
                if (activeIdx + 1 < lines.Count)
                {
                    DrawLine(context, lines[activeIdx + 1], currentMs, Bounds.Height * 0.60, false);
                }
            }
        }

        private void DrawLine(DrawingContext context, List<SyltEvent> line, double currentMs, double yPos, bool isActive)
        {
            string fullText = string.Join("", line.Select(s => s.Text)).Trim();
            if (string.IsNullOrWhiteSpace(fullText)) return;

            var typeface = new Typeface("Arial", FontStyle.Normal, FontWeight.Bold);
            double fontSize = isActive ? 36 : 28;
            
            var tempFmt = new FormattedText(fullText, System.Globalization.CultureInfo.CurrentCulture, FlowDirection.LeftToRight, typeface, fontSize, Brushes.Transparent);
            double cursorX = (Bounds.Width - tempFmt.Width) / 2;

            foreach (var syl in line)
            {
                if (string.IsNullOrEmpty(syl.Text)) continue;
                
                IBrush color = !isActive ? Brushes.LightGray : (currentMs >= syl.TimeMs ? Brushes.Yellow : Brushes.White);

                var shadowFmt = new FormattedText(syl.Text, System.Globalization.CultureInfo.CurrentCulture, FlowDirection.LeftToRight, typeface, fontSize, Brushes.Black);
                var txtFmt = new FormattedText(syl.Text, System.Globalization.CultureInfo.CurrentCulture, FlowDirection.LeftToRight, typeface, fontSize, color);
                
                context.DrawText(shadowFmt, new Point(cursorX + 2, yPos + 2));
                context.DrawText(txtFmt, new Point(cursorX, yPos));
                
                cursorX += txtFmt.Width; 
            }
        }
    }
}
