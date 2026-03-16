using System;
using System.Threading.Tasks;
using SoulJemApp.Plugins;

namespace SoulJemApp.Services
{
    public class DuckingService
    {
        private MpvPlugin _engine;
        
        // I volumi e la morbidezza della sfumatura
        public int NormalVolume { get; set; } = 100;
        private int _duckVolume = 20;
        private int _steps = 8;
        private int _stepDelay = 50;

        public DuckingService(MpvPlugin engine)
        {
            _engine = engine;
        }

        public async Task DuckAsync()
        {
            try
            {
                int start = NormalVolume; // CORRETTO QUI!
                int end = _duckVolume;
                int delta = (start - end) / _steps;

                for (int i = 0; i < _steps; i++)
                {
                    start -= delta;
                    _engine.SetRadioVolume(start);
                    await Task.Delay(_stepDelay);
                }
                _engine.SetRadioVolume(end);
            }
            catch { }
        }

        public async Task RestoreAsync()
        {
            try
            {
                int start = _duckVolume;
                int end = NormalVolume; // E CORRETTO QUI!
                int delta = (end - start) / _steps;

                for (int i = 0; i < _steps; i++)
                {
                    start += delta;
                    _engine.SetRadioVolume(start);
                    await Task.Delay(_stepDelay);
                }
                _engine.SetRadioVolume(end);
            }
            catch { }
        }
    }
}
