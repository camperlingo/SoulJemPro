using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Diagnostics;
using System.Collections.Generic;
using FFmpeg.AutoGen;

namespace SoulJemApp.Plugins
{
    public unsafe class MpvLiteEngine : IDisposable
    {
        private AVFormatContext* _formatCtx;
        private AVFormatContext* _formatCtxSecondary = null; 
        
        private AVCodecContext* _videoCodecCtx;
        private AVCodecContext* _audioCodecCtx;
        private SwsContext* _swsCtx; 

        private AVFilterGraph* _filterGraph;
        private AVFilterContext* _buffersrcCtx;
        private AVFilterContext* _buffersinkCtx;

        private int _videoStreamIndex = -1;
        private int _secondaryVideoStreamIndex = -1; 
        private int _audioStreamIndex = -1;

        private Thread? _decodeThread;
        private bool _running;
        private volatile bool _isPaused = false;
        private AudioEngine? _audioEngine;

        private Stopwatch _clock = new Stopwatch();
        private double _videoTimeBase;
        private double _audioTimeBase;

        public double TotalDuration { get; private set; }
        
        public double CurrentTime => _seekRequested ? _seekTarget : _startSeekTime + _clock.Elapsed.TotalSeconds;
        
        private double _startSeekTime = 0;
        private volatile bool _seekRequested = false;
        private double _seekTarget = 0;

        private double _pitchFactor = 1.0;
        private volatile bool _pitchChanged = false;

        public event Action<byte[], int, int>? OnFrameReady;
        
        // LA NOSTRA LISTA DI EVENTI TESTUALI IN RAM
        public List<SyltEvent> SyltEvents { get; private set; } = new List<SyltEvent>();

        public MpvLiteEngine()
        {
            ffmpeg.RootPath = "/usr/lib/x86_64-linux-gnu";
            try { ffmpeg.av_log_set_level(ffmpeg.AV_LOG_ERROR); } catch { }
        }

        // --- INIZIO BLOCCO WARM-UP (Versione Indistruttibile) ---
        public static void WarmUp()
        {
            System.Threading.Tasks.Task.Run(() => 
            {
                try
                {
                    Console.WriteLine("[MOTORE] 🔥 Inizio Pre-riscaldamento silente in background...");
                    
                    // Allocazione reale: questo FORZA il caricamento delle .so in RAM
                    // perché stiamo chiedendo al sistema di allocare memoria per FFmpeg.
                    var fmtCtx = ffmpeg.avformat_alloc_context();
                    var codecCtx = ffmpeg.avcodec_alloc_context3(null);
                    
                    // Pulizia immediata (liberiamo la RAM subito dopo aver "svegliato" le librerie)
                    if (codecCtx != null) ffmpeg.avcodec_free_context(&codecCtx);
                    if (fmtCtx != null) ffmpeg.avformat_free_context(fmtCtx);

                    // Inizializziamo a vuoto l'AudioEngine per assicurarci che OpenAL sia pronto
                    using (var audio = new AudioEngine()) 
                    {
                        audio.Init();
                    }

                    Console.WriteLine("[MOTORE] ✅ Pre-riscaldamento completato! Tutti i componenti sono in RAM.");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[MOTORE] ⚠️ Warm-up interrotto (non critico): {ex.Message}");
                }
            });
        }
        // --- FINE BLOCCO WARM-UP ---

        public void Open(string path)
        {
            string primaryPath = path;
            string secondaryPath = "";
            bool isDualStream = false;
            
            SyltEvents.Clear(); // Pulisce i testi della canzone precedente

            string ext = Path.GetExtension(path).ToLower();
            if (ext == ".cdg")
            {
                string mp3Path = Path.ChangeExtension(path, ".mp3");
                if (File.Exists(mp3Path))
                {
                    primaryPath = mp3Path;
                    secondaryPath = path;
                    isDualStream = true;
                    Console.WriteLine("[MOTORE] CDG rilevato: Uso l'MP3 gemello come Master Audio.");
                }
            }
            else if (ext == ".mp3")
            {
                // --- LA MAGIA DELLA LETTURA DEL PENSIERO (SYLT NATIVO) ---
                var extractedSylt = SyltParser.ExtractLyrics(path);
                if (extractedSylt != null && extractedSylt.Count > 0)
                {
                    SyltEvents = extractedSylt;
                    primaryPath = path;
                    isDualStream = false; // NON cerchiamo nessun CDG, abbiamo i testi in RAM!
                    Console.WriteLine($"[MOTORE] 🧠 LETTURA DEL PENSIERO RIUSCITA! Estratte {SyltEvents.Count} sillabe sincronizzate dall'MP3. Modalità Testo Nativo attivata!");
                }
                else
                {
                    string cdgPath = Path.ChangeExtension(path, ".cdg");
                    if (File.Exists(cdgPath))
                    {
                        primaryPath = path;
                        secondaryPath = cdgPath;
                        isDualStream = true;
                        Console.WriteLine("[MOTORE] MP3 rilevato: Nessun SYLT trovato, ma c'è un CDG gemello. Avvio Dual-Stream.");
                    }
                }
            }

            Console.WriteLine($"[MOTORE] Apertura file primario: {primaryPath}");
            
            fixed (AVFormatContext** pFormatCtx = &_formatCtx)
            {
                if (ffmpeg.avformat_open_input(pFormatCtx, primaryPath, null, null) < 0) return;
            }
            
            ffmpeg.avformat_find_stream_info(_formatCtx, null);

            if (_formatCtx->duration != ffmpeg.AV_NOPTS_VALUE)
            {
                TotalDuration = _formatCtx->duration / (double)ffmpeg.AV_TIME_BASE;
            }

            _videoStreamIndex = FindStream(_formatCtx, AVMediaType.AVMEDIA_TYPE_VIDEO);
            
            if (_videoStreamIndex == -1 && isDualStream)
            {
                fixed (AVFormatContext** pSecCtx = &_formatCtxSecondary)
                {
                    if (ffmpeg.avformat_open_input(pSecCtx, secondaryPath, null, null) == 0)
                    {
                        ffmpeg.avformat_find_stream_info(_formatCtxSecondary, null);
                        _secondaryVideoStreamIndex = FindStream(_formatCtxSecondary, AVMediaType.AVMEDIA_TYPE_VIDEO);

                        if (_secondaryVideoStreamIndex != -1)
                        {
                            var videoCodec = ffmpeg.avcodec_find_decoder(_formatCtxSecondary->streams[_secondaryVideoStreamIndex]->codecpar->codec_id);
                            _videoCodecCtx = ffmpeg.avcodec_alloc_context3(videoCodec);
                            ffmpeg.avcodec_parameters_to_context(_videoCodecCtx, _formatCtxSecondary->streams[_secondaryVideoStreamIndex]->codecpar);
                            ffmpeg.avcodec_open2(_videoCodecCtx, videoCodec, null);
                            _videoTimeBase = (double)_formatCtxSecondary->streams[_secondaryVideoStreamIndex]->time_base.num / _formatCtxSecondary->streams[_secondaryVideoStreamIndex]->time_base.den;
                        }
                    }
                }
            }
            else if (_videoStreamIndex != -1)
            {
                var videoCodec = ffmpeg.avcodec_find_decoder(_formatCtx->streams[_videoStreamIndex]->codecpar->codec_id);
                _videoCodecCtx = ffmpeg.avcodec_alloc_context3(videoCodec);
                ffmpeg.avcodec_parameters_to_context(_videoCodecCtx, _formatCtx->streams[_videoStreamIndex]->codecpar);
                //TryInitVAAPI(); 
                ffmpeg.avcodec_open2(_videoCodecCtx, videoCodec, null);
                _videoTimeBase = (double)_formatCtx->streams[_videoStreamIndex]->time_base.num / _formatCtx->streams[_videoStreamIndex]->time_base.den;
            }

            _audioStreamIndex = FindStream(_formatCtx, AVMediaType.AVMEDIA_TYPE_AUDIO);
            if (_audioStreamIndex != -1)
            {
                var audioCodec = ffmpeg.avcodec_find_decoder(_formatCtx->streams[_audioStreamIndex]->codecpar->codec_id);
                _audioCodecCtx = ffmpeg.avcodec_alloc_context3(audioCodec);
                ffmpeg.avcodec_parameters_to_context(_audioCodecCtx, _formatCtx->streams[_audioStreamIndex]->codecpar);
                ffmpeg.avcodec_open2(_audioCodecCtx, audioCodec, null);
                
                _audioTimeBase = (double)_formatCtx->streams[_audioStreamIndex]->time_base.num / _formatCtx->streams[_audioStreamIndex]->time_base.den;

                _audioEngine = new AudioEngine();
                _audioEngine.Init();

                ReinitFilterGraph();
            }
        }

        private void ReinitFilterGraph()
        {
            if (_audioCodecCtx == null) return;

            if (_filterGraph != null)
            {
                fixed (AVFilterGraph** fg = &_filterGraph)
                    ffmpeg.avfilter_graph_free(fg);
            }

            _filterGraph = ffmpeg.avfilter_graph_alloc();
            var abuffersrc = ffmpeg.avfilter_get_by_name("abuffer");
            var abuffersink = ffmpeg.avfilter_get_by_name("abuffersink");

            string sampleFmt = ffmpeg.av_get_sample_fmt_name(_audioCodecCtx->sample_fmt);
            if (string.IsNullOrEmpty(sampleFmt) || sampleFmt == "none") sampleFmt = "fltp";
            
            int channels = _audioCodecCtx->ch_layout.nb_channels;
            if (channels <= 0) channels = 2; 
            
            long inChannelLayout = channels == 1 ? (long)ffmpeg.AV_CH_LAYOUT_MONO : (long)ffmpeg.AV_CH_LAYOUT_STEREO;
            int sampleRate = _audioCodecCtx->sample_rate > 0 ? _audioCodecCtx->sample_rate : 44100;

            AVRational tb = _formatCtx->streams[_audioStreamIndex]->time_base;
            string srcArgs = $"time_base={tb.num}/{tb.den}:sample_rate={sampleRate}:sample_fmt={sampleFmt}:channel_layout={inChannelLayout}";

            AVFilterContext* srcCtx;
            ffmpeg.avfilter_graph_create_filter(&srcCtx, abuffersrc, "in", srcArgs, null, _filterGraph);
            _buffersrcCtx = srcCtx;

            AVFilterContext* sinkCtx;
            ffmpeg.avfilter_graph_create_filter(&sinkCtx, abuffersink, "out", null, null, _filterGraph);
            _buffersinkCtx = sinkCtx;

            string rateStr = (sampleRate * _pitchFactor).ToString(System.Globalization.CultureInfo.InvariantCulture);
            string tempoStr = (1.0 / _pitchFactor).ToString(System.Globalization.CultureInfo.InvariantCulture);
            
            string filterSpec = $"asetrate={rateStr},atempo={tempoStr},aformat=sample_fmts=s16:sample_rates=44100:channel_layouts=stereo";

            AVFilterInOut* inputs = ffmpeg.avfilter_inout_alloc();
            AVFilterInOut* outputs = ffmpeg.avfilter_inout_alloc();

            outputs->name = ffmpeg.av_strdup("in");
            outputs->filter_ctx = _buffersrcCtx;
            outputs->pad_idx = 0;
            outputs->next = null;

            inputs->name = ffmpeg.av_strdup("out");
            inputs->filter_ctx = _buffersinkCtx;
            inputs->pad_idx = 0;
            inputs->next = null;

            ffmpeg.avfilter_graph_parse_ptr(_filterGraph, filterSpec, &inputs, &outputs, null);
            ffmpeg.avfilter_graph_config(_filterGraph, null);

            ffmpeg.avfilter_inout_free(&inputs);
            ffmpeg.avfilter_inout_free(&outputs);
        }

        public void SetPitch(int semitones)
        {
            _pitchFactor = Math.Pow(2.0, (double)semitones / 12.0);
            _pitchChanged = true; 
            Console.WriteLine($"[MOTORE] Richiesto Pitch: {semitones} Semitoni");
        }

        private void TryInitVAAPI()
        {
            Environment.SetEnvironmentVariable("LIBVA_DRIVER_NAME", "radeonsi");
            try
            {
                AVBufferRef* hwDeviceCtx = null;
                if (ffmpeg.av_hwdevice_ctx_create(&hwDeviceCtx, AVHWDeviceType.AV_HWDEVICE_TYPE_VAAPI, "/dev/dri/renderD128", null, 0) >= 0) 
                {
                    _videoCodecCtx->hw_device_ctx = ffmpeg.av_buffer_ref(hwDeviceCtx);
                }
            }
            catch { }
        }

        public void Start()
        {
            _running = true;
            _startSeekTime = 0;
            _clock.Restart();
            
            // Creiamo il thread con Priorità Massima per evitare gracchiamenti
            _decodeThread = new Thread(DecodeLoop) 
            { 
                IsBackground = true,
                Priority = ThreadPriority.Highest 
            };
            
            _decodeThread.Start();
        }

        public void Seek(double timeInSeconds)
        {
            _seekTarget = timeInSeconds;
            _seekRequested = true;
        }

        public void TogglePause()
        {
            _isPaused = !_isPaused;
            if (_isPaused) { _clock.Stop(); _audioEngine?.PauseAudio(); }
            else { _clock.Start(); _audioEngine?.ResumeAudio(); }
        }

        public void SetVolume(float volume) { _audioEngine?.SetVolume(volume); }

        private void DecodeLoop()
        {
            AVPacket packet;
            AVFrame* videoFrame = ffmpeg.av_frame_alloc();
            AVFrame* audioFrame = ffmpeg.av_frame_alloc();
            AVFrame* rgbFrame = ffmpeg.av_frame_alloc();

            double lastAudioPtsTime = 0;
            double lastVideoPtsTime = 0;
            bool eofCdg = false;

            while (_running && _formatCtx != null)
            {
                if (_seekRequested)
                {
                    long targetPtsAudio = (long)(_seekTarget * ffmpeg.AV_TIME_BASE);
                    
                    ffmpeg.av_seek_frame(_formatCtx, -1, targetPtsAudio, ffmpeg.AVSEEK_FLAG_BACKWARD);
                    
                    if (_formatCtxSecondary != null) 
                    {
                        ffmpeg.av_seek_frame(_formatCtxSecondary, -1, targetPtsAudio, ffmpeg.AVSEEK_FLAG_BACKWARD);
                        eofCdg = false;
                    }

                    if (_videoCodecCtx != null) ffmpeg.avcodec_flush_buffers(_videoCodecCtx);
                    if (_audioCodecCtx != null) ffmpeg.avcodec_flush_buffers(_audioCodecCtx);
                    _audioEngine?.ClearBuffers();
                    
                    ReinitFilterGraph(); 
                    _pitchChanged = false;

                    _startSeekTime = _seekTarget;
                    _clock.Restart();
                    
                    lastAudioPtsTime = _seekTarget;
                    lastVideoPtsTime = _formatCtxSecondary != null ? 0 : _seekTarget; 
                    
                    _seekRequested = false;
                    continue;
                }

                if (_isPaused)
                {
                    Thread.Sleep(30);
                    continue;
                }

                bool readFromSecondary = false;

                if (_formatCtxSecondary != null && !eofCdg && lastVideoPtsTime <= lastAudioPtsTime)
                {
                    if (ffmpeg.av_read_frame(_formatCtxSecondary, &packet) >= 0) 
                    {
                        readFromSecondary = true;
                    }
                    else
                    {
                        eofCdg = true; 
                        if (ffmpeg.av_read_frame(_formatCtx, &packet) < 0) break;
                    }
                }
                else
                {
                    if (ffmpeg.av_read_frame(_formatCtx, &packet) < 0) break; 
                }

                if (readFromSecondary)
                {
                    if (packet.stream_index == _secondaryVideoStreamIndex && _videoCodecCtx != null)
                    {
                        if (packet.pts != ffmpeg.AV_NOPTS_VALUE) lastVideoPtsTime = packet.pts * _videoTimeBase;
                        
                        ffmpeg.avcodec_send_packet(_videoCodecCtx, &packet);
                        while (ffmpeg.avcodec_receive_frame(_videoCodecCtx, videoFrame) == 0)
                        {
                            if (!_running || _seekRequested) break;
                            
                            if (lastVideoPtsTime < CurrentTime - 0.2) continue; 
                                
                            ProcessVideoFrame(videoFrame, rgbFrame);
                        }
                    }
                }
                else
                {
                    if (packet.stream_index == _videoStreamIndex && _videoCodecCtx != null)
                    {
                        if (packet.pts != ffmpeg.AV_NOPTS_VALUE) lastVideoPtsTime = packet.pts * _videoTimeBase;
                        ffmpeg.avcodec_send_packet(_videoCodecCtx, &packet);
                        while (ffmpeg.avcodec_receive_frame(_videoCodecCtx, videoFrame) == 0)
                        {
                            if (!_running || _seekRequested) break;
                            ProcessVideoFrame(videoFrame, rgbFrame);
                        }
                    }
                    else if (packet.stream_index == _audioStreamIndex && _audioCodecCtx != null)
                    {
                        if (packet.pts != ffmpeg.AV_NOPTS_VALUE) lastAudioPtsTime = packet.pts * _audioTimeBase;
                        ffmpeg.avcodec_send_packet(_audioCodecCtx, &packet);
                        while (ffmpeg.avcodec_receive_frame(_audioCodecCtx, audioFrame) == 0)
                        {
                            if (!_running || _seekRequested) break;
                            ProcessAudioFrame(audioFrame);
                        }
                    }
                }
                ffmpeg.av_packet_unref(&packet);
            }

            ffmpeg.av_frame_free(&videoFrame);
            ffmpeg.av_frame_free(&audioFrame);
            ffmpeg.av_frame_free(&rgbFrame);
        }

        private void ProcessVideoFrame(AVFrame* frame, AVFrame* rgbFrame)
        {
            if (_videoCodecCtx == null || !_running) return;

            if (frame->pts != ffmpeg.AV_NOPTS_VALUE && !_seekRequested)
            {
                double ptsTime = frame->pts * _videoTimeBase;

                if (!_isPaused)
                {
                    while (_running && !_isPaused && !_seekRequested)
                    {
                        double elapsed = CurrentTime;
                        if (elapsed >= ptsTime) break; 

                        double diffMs = (ptsTime - elapsed) * 1000;
                        
                        if (diffMs > 20) 
                            Thread.Sleep(2); 
                        else if (diffMs > 2)
                            Thread.Sleep(1); // Sostituito SpinWait con Sleep per liberare la CPU
                        else
                            break;
                    }
                }
            }

            if (_seekRequested || _isPaused) return;

            if (frame->format == (int)AVPixelFormat.AV_PIX_FMT_VAAPI)
            {
                AVFrame* swFrame = ffmpeg.av_frame_alloc();
                if (ffmpeg.av_hwframe_transfer_data(swFrame, frame, 0) >= 0) 
                {
                    ConvertToRGB(swFrame, rgbFrame);
                }
                ffmpeg.av_frame_free(&swFrame);
            }
            else
            {
                ConvertToRGB(frame, rgbFrame);
            }
        }

        private void ConvertToRGB(AVFrame* srcFrame, AVFrame* rgbFrame)
        {
            int trueWidth = _videoCodecCtx->width;
            int trueHeight = _videoCodecCtx->height; 

            if (_swsCtx == null)
            {
                _swsCtx = ffmpeg.sws_getContext(srcFrame->width, srcFrame->height, (AVPixelFormat)srcFrame->format,
                    trueWidth, trueHeight, AVPixelFormat.AV_PIX_FMT_BGRA, 1, null, null, null);
            }
            if (_swsCtx == null) return;

            int size = ffmpeg.av_image_get_buffer_size(AVPixelFormat.AV_PIX_FMT_BGRA, trueWidth, trueHeight, 1);
            byte* buffer = (byte*)ffmpeg.av_malloc((ulong)size);
            
            rgbFrame->data[0] = buffer;
            rgbFrame->linesize[0] = trueWidth * 4;
            
            ffmpeg.sws_scale(_swsCtx, srcFrame->data, srcFrame->linesize, 0, srcFrame->height, rgbFrame->data, rgbFrame->linesize);

            byte[] frameData = new byte[size];
            Marshal.Copy((IntPtr)buffer, frameData, 0, size);
            ffmpeg.av_free(buffer);

            if (_running) OnFrameReady?.Invoke(frameData, trueWidth, trueHeight);
        }

        private void ProcessAudioFrame(AVFrame* frame)
        {
            if (_audioEngine == null || !_running || _seekRequested) return;

            if (_pitchChanged)
            {
                ReinitFilterGraph();
                _audioEngine.ClearBuffers(); 
                _pitchChanged = false;
            }

            if (_filterGraph == null) return;

            if (ffmpeg.av_buffersrc_add_frame_flags(_buffersrcCtx, frame, 8) >= 0)
            {
                AVFrame* filtFrame = ffmpeg.av_frame_alloc();
                
                while (true)
                {
                    int ret = ffmpeg.av_buffersink_get_frame(_buffersinkCtx, filtFrame);
                    if (ret < 0) break;

                    int channels = filtFrame->ch_layout.nb_channels > 0 ? filtFrame->ch_layout.nb_channels : 2;

                    int dataSize = ffmpeg.av_samples_get_buffer_size(null, channels, filtFrame->nb_samples, (AVSampleFormat)filtFrame->format, 1);
                    if (dataSize > 0)
                    {
                        byte[] pcmData = new byte[dataSize];
                        Marshal.Copy((IntPtr)filtFrame->data[0], pcmData, 0, dataSize);
                        
                        _audioEngine.QueueAudio(pcmData, 44100);
                    }
                    ffmpeg.av_frame_unref(filtFrame);
                }
                ffmpeg.av_frame_free(&filtFrame);
            }
        }

        private int FindStream(AVFormatContext* ctx, AVMediaType type)
        {
            if (ctx == null) return -1;
            for (int i = 0; i < ctx->nb_streams; i++)
                if (ctx->streams[i]->codecpar->codec_type == type) return i;
            return -1;
        }

        public void Dispose()
        {
            _running = false;
            _seekRequested = true; 
            _clock.Stop();

            if (_decodeThread != null && _decodeThread.IsAlive) _decodeThread.Join(500); 

            _audioEngine?.Dispose();

            if (_filterGraph != null)
            {
                fixed (AVFilterGraph** fg = &_filterGraph)
                    ffmpeg.avfilter_graph_free(fg);
                _filterGraph = null;
            }

            fixed (AVCodecContext** codecCtx = &_videoCodecCtx) { if (codecCtx != null && *codecCtx != null) ffmpeg.avcodec_free_context(codecCtx); }
            fixed (AVCodecContext** codecCtx = &_audioCodecCtx) { if (codecCtx != null && *codecCtx != null) ffmpeg.avcodec_free_context(codecCtx); }
            
            fixed (AVFormatContext** formatCtx = &_formatCtx) { if (formatCtx != null && *formatCtx != null) ffmpeg.avformat_close_input(formatCtx); }
            fixed (AVFormatContext** secFormatCtx = &_formatCtxSecondary) { if (secFormatCtx != null && *secFormatCtx != null) ffmpeg.avformat_close_input(secFormatCtx); }
            
            if (_swsCtx != null) { ffmpeg.sws_freeContext(_swsCtx); _swsCtx = null; }
        }
    }
}
