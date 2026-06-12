using Silk.NET.OpenAL;
using System;

#pragma warning disable CS8618 

namespace SoulJemApp.Plugins
{
    public unsafe class AudioEngine : IDisposable
    {
        private ALContext _alc;
        private AL _al;
        private Device* _device;
        private Context* _context;
        private uint _source;

        public void Init()
        {
            _alc = ALContext.GetApi();
            _al = AL.GetApi();

            _device = _alc.OpenDevice(""); 
            if (_device == null) throw new Exception("Impossibile aprire la scheda audio.");

            _context = _alc.CreateContext(_device, null);
            _alc.MakeContextCurrent(_context);

            _source = _al.GenSource();
        }

        public void QueueAudio(byte[] pcmData, int sampleRate)
        {
            if (_al == null) return;

            _al.GetSourceProperty(_source, GetSourceInteger.BuffersProcessed, out int processed);
            uint bufferToReuse = 0;

            // [FIX] Svuota TUTTI i buffer accumulati per evitare l'ingorgo della scheda audio
            while (processed > 0)
            {
                uint unqueued;
                _al.SourceUnqueueBuffers(_source, 1, &unqueued);
                
                if (bufferToReuse == 0) 
                {
                    bufferToReuse = unqueued; // Tiene il primo per riciclarlo (Ottimizzazione CPU)
                }
                else 
                {
                    _al.DeleteBuffer(unqueued); // Demolisce l'eccesso per non intasare la RAM
                }
                processed--;
            }

            // Se non c'erano buffer da riciclare, ne crea uno nuovo
            if (bufferToReuse == 0)
            {
                bufferToReuse = _al.GenBuffer();
            }

            // Riempiamo il buffer con i nuovi dati audio
            fixed (byte* p = pcmData)
            {
                _al.BufferData(bufferToReuse, BufferFormat.Stereo16, p, pcmData.Length, sampleRate);
            }
            
            _al.SourceQueueBuffers(_source, 1, &bufferToReuse);

            _al.GetSourceProperty(_source, GetSourceInteger.SourceState, out int state);
            if (state != (int)SourceState.Playing && state != (int)SourceState.Paused)
            {
                _al.SourcePlay(_source);
            }
        }

        public int GetPendingBuffers()
        {
            if (_al == null) return 0;
            _al.GetSourceProperty(_source, GetSourceInteger.BuffersQueued, out int queued);
            _al.GetSourceProperty(_source, GetSourceInteger.BuffersProcessed, out int processed);
            return queued - processed;
        }

        // --- NUOVI COMANDI DEL CRUSCOTTO ---

        // Il Volume in OpenAL va da 0.0f (Muto) a 1.0f (100%). Può anche andare oltre (es. 2.0f) per amplificare!
        public void SetVolume(float volume)
        {
            _al?.SetSourceProperty(_source, SourceFloat.Gain, volume);
        }

        public void PauseAudio()
        {
            _al?.SourcePause(_source);
        }

        public void ResumeAudio()
        {
            _al?.SourcePlay(_source);
        }

        // -----------------------------------

        public void ClearBuffers()
        {
            if (_al == null) return;
            
            _al.SourceStop(_source); // Blocca l'audio
            
            // Distrugge tutti i frammenti rimasti in canna
            _al.GetSourceProperty(_source, GetSourceInteger.BuffersQueued, out int queued);
            while (queued > 0)
            {
                uint unqueued;
                _al.SourceUnqueueBuffers(_source, 1, &unqueued);
                _al.DeleteBuffer(unqueued);
                queued--;
            }
        }

        public void Dispose()
        {
            if (_al != null)
            {
                _al.SourceStop(_source);
                
                _al.GetSourceProperty(_source, GetSourceInteger.BuffersQueued, out int queued);
                while (queued > 0)
                {
                    uint unqueued;
                    _al.SourceUnqueueBuffers(_source, 1, &unqueued);
                    _al.DeleteBuffer(unqueued);
                    queued--;
                }

                _al.DeleteSource(_source);
            }
            if (_alc != null && _context != null)
            {
                _alc.MakeContextCurrent(null);
                _alc.DestroyContext(_context);
                _alc.CloseDevice(_device);
            }
            _al?.Dispose();
            _alc?.Dispose();
        }
    }
}
