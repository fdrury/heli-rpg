using System;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using NLayer;
using Rotorwash.Sim;

namespace Rotorwash;

/// <summary>
/// Pulls an Icecast/Shoutcast MP3 stream off the internet and turns it into PCM the
/// cockpit radio can push into an <c>AudioStreamGenerator</c>.
///
/// <b>Why any of this is necessary.</b> Godot cannot stream audio over the network.
/// <c>AudioStreamMP3</c> wants a complete buffer in memory, and there is no incremental
/// decoder exposed. So this takes the path the synths already use: get bytes, make PCM,
/// push blocks into a generator - exactly what <see cref="HelicopterAudio"/> and
/// <see cref="WeatherAudio"/> do, with an MP3 decoder in front instead of an oscillator.
/// <see href="https://github.com/naudio/NLayer">NLayer</see> is MIT-licensed, pure
/// managed and decodes from a <see cref="Stream"/>, which keeps the whole thing inside the
/// .NET project with no external binary and no shelling out to ffmpeg.
///
/// <b>Three stages, three threads, and the frame is on none of them.</b>
/// <list type="number">
/// <item>An HTTP task reads the socket in 8 KB chunks and feeds <see cref="ByteRing"/>.</item>
/// <item>A decode task pulls MP3 frames out of that ring through NLayer and writes
/// interleaved float PCM into <see cref="_pcm"/>, resampling if the station is not at the
/// mixer's rate.</item>
/// <item><see cref="Read"/> drains <see cref="_pcm"/> under a short lock. It is the only
/// method the game thread calls, it never blocks on anything but that lock, and if there
/// is nothing there it returns zero and the radio is briefly silent.</item>
/// </list>
///
/// <b>Failure is the normal case, not the exception.</b> This runs on a machine that may
/// be offline, against a server that may be down, over a connection that may drop
/// mid-song. Every one of those is caught, reported through <see cref="Link"/>, and
/// retried on a backoff; none of them can throw into the frame, and none of them can stop
/// the game. A radio that silently goes quiet because there is no network is exactly what
/// a radio in a ruined world should do anyway.
///
/// <b>What this does not do.</b> It does not send <c>Icy-MetaData: 1</c>, so the server
/// does not interleave title metadata into the audio and there is nothing to strip - at
/// the cost of not knowing what is playing. It does not handle Shoutcast v1 servers that
/// answer <c>ICY 200 OK</c> instead of a real HTTP status line; those fail cleanly and
/// report off air. It does not decode AAC or Ogg, only MP3, which is what the overwhelming
/// majority of Icecast stations serve.
/// </summary>
public sealed class RadioStream : IDisposable
{
    /// <summary>What the game mixer runs at. Anything else is resampled to it.</summary>
    public const int MixRate = 44100;

    /// <summary>Seconds of decoded audio to hold before declaring the link live.</summary>
    private const double PrebufferSeconds = 1.20;

    /// <summary>Seconds of decoded audio to hold at most. Beyond this the radio is late.</summary>
    private const double MaxBufferSeconds = 6.0;

    /// <summary>Bytes of undecoded MP3 to hold. About twelve seconds at 128 kbps.</summary>
    private const int MaxBytes = 192 * 1024;

    /// <summary>How long to wait for the first byte before calling it off air.</summary>
    private static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(12);

    /// <summary>Backoff after a failure: first retry, then doubling to the cap.</summary>
    private const double RetrySeconds = 6.0;
    private const double RetryCapSeconds = 60.0;

    // ------------------------------------------------------------------ state

    private static readonly HttpClient Http = MakeClient();

    private readonly object _gate = new();
    private float[] _pcm = new float[MixRate * 2 * 4];
    private int _head, _count;

    private CancellationTokenSource? _cts;
    private Task? _worker;
    private string _url = "";
    private double _retryIn;
    private double _backoff = RetrySeconds;
    private volatile int _link = (int)RadioLink.Idle;
    private volatile string _note = "";

    /// <summary>What the network end is doing. Safe to read from the game thread.</summary>
    public RadioLink Link => (RadioLink)_link;

    /// <summary>Last thing that went wrong, for the log. Never shown to the player.</summary>
    public string Note => _note;

    /// <summary>What is currently tuned. Empty when nothing is.</summary>
    public string Url => _url;

    /// <summary>Seconds of decoded audio in hand. Zero means the next block is silence.</summary>
    public double Buffered { get { lock (_gate) return _count / (double)(MixRate * 2); } }

    private static HttpClient MakeClient()
    {
        var c = new HttpClient
        {
            // The response never completes - that is what a radio station is - so the
            // client must not have a timeout. The connect attempt gets its own below.
            Timeout = Timeout.InfiniteTimeSpan,
        };
        // Some Icecast servers refuse a request with no user agent, and a few serve a
        // different playlist to browsers. Announce what this is.
        c.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", "Rotorwash/0.1 (Godot)");
        return c;
    }

    // ------------------------------------------------------------- the switch

    /// <summary>
    /// Tune to a URL. Idempotent: tuning the station already playing does nothing, which
    /// matters because the caller checks this every frame.
    /// </summary>
    public void Tune(string url)
    {
        if (url == _url) return;
        Stop();
        _url = url ?? "";
        _backoff = RetrySeconds;
        _retryIn = 0;
        if (_url.Length == 0) return;
        Begin();
    }

    /// <summary>Drop the connection and throw away what is buffered.</summary>
    public void Stop()
    {
        try { _cts?.Cancel(); } catch { /* already gone */ }
        _cts = null;
        _worker = null;
        _url = "";
        _link = (int)RadioLink.Idle;
        lock (_gate) { _head = 0; _count = 0; }
    }

    /// <summary>
    /// Give the retry timer a slice of time. Called once a frame; does no work at all
    /// unless a connection has failed and its backoff has run out.
    /// </summary>
    public void Poll(double delta)
    {
        if (_url.Length == 0) return;
        if (_worker is not null && !_worker.IsCompleted) return;
        if (Link != RadioLink.Failed && _worker is null && _retryIn <= 0) { Begin(); return; }

        _retryIn -= delta;
        if (_retryIn <= 0)
        {
            _backoff = Math.Min(_backoff * 2, RetryCapSeconds);
            Begin();
        }
    }

    private void Begin()
    {
        _cts = new CancellationTokenSource();
        _link = (int)RadioLink.Connecting;
        string url = _url;
        CancellationToken token = _cts.Token;
        _retryIn = _backoff;
        _worker = Task.Run(() => Pump(url, token), token);
    }

    // ------------------------------------------------------------- the drain

    /// <summary>
    /// Take up to <paramref name="frames"/> stereo frames of PCM.
    ///
    /// Returns how many were actually written; the caller fills the rest with silence.
    /// This is the only method the game thread calls and the only place the two sides
    /// meet. It copies under a lock rather than doing anything clever, because the lock is
    /// held for a memcpy of a few thousand floats and the alternative is a lock-free ring
    /// buffer nobody can prove correct.
    /// </summary>
    public int Read(Span<float> interleaved, int frames)
    {
        int want = frames * 2;
        lock (_gate)
        {
            int n = Math.Min(want, _count);
            for (int i = 0; i < n; i++) interleaved[i] = _pcm[(_head + i) % _pcm.Length];
            _head = (_head + n) % _pcm.Length;
            _count -= n;
            return n / 2;
        }
    }

    private void Push(float[] src, int count)
    {
        lock (_gate)
        {
            // If the game is not draining - paused, minimised, out of range - throw the
            // oldest audio away rather than growing without limit. A radio is live; being
            // six seconds behind is worse than a gap.
            int cap = (int)(MaxBufferSeconds * MixRate * 2);
            if (_count + count > cap)
            {
                int drop = Math.Min(_count, _count + count - cap);
                _head = (_head + drop) % _pcm.Length;
                _count -= drop;
            }
            if (count > _pcm.Length) count = _pcm.Length;
            int tail = (_head + _count) % _pcm.Length;
            for (int i = 0; i < count; i++)
            {
                // Clamped to the representable range, which is not paranoia: a live
                // SomaFM stream measured a decoded PEAK OF 1.470 against an RMS of -13.9
                // dBFS. MP3 decoders overshoot full scale routinely on a brickwalled
                // master - the overshoot is a reconstruction artefact rather than signal -
                // and 1.47 through the AGC's +9 dB limit would have clipped the mixer.
                // Every MP3 player does this; it was still worth finding by measuring
                // rather than by hearing it once and wondering.
                float v = src[i];
                _pcm[(tail + i) % _pcm.Length] = v > 1f ? 1f : v < -1f ? -1f : v;
            }
            _count = Math.Min(_count + count, _pcm.Length);
        }
    }

    // -------------------------------------------------------------- the pump

    private async Task Pump(string url, CancellationToken token)
    {
        ByteRing? bytes = null;
        try
        {
            using var connect = CancellationTokenSource.CreateLinkedTokenSource(token);
            connect.CancelAfter(ConnectTimeout);

            using HttpResponseMessage resp = await Http
                .GetAsync(url, HttpCompletionOption.ResponseHeadersRead, connect.Token)
                .ConfigureAwait(false);

            if (!resp.IsSuccessStatusCode)
            {
                Fail($"{(int)resp.StatusCode} {resp.ReasonPhrase}");
                return;
            }

            string? type = resp.Content.Headers.ContentType?.MediaType;
            if (type is not null && !type.Contains("mpeg", StringComparison.OrdinalIgnoreCase)
                                 && !type.Contains("mp3", StringComparison.OrdinalIgnoreCase)
                                 && !type.Contains("octet", StringComparison.OrdinalIgnoreCase))
            {
                // A playlist (.pls/.m3u) or an Ogg/AAC stream. Nothing here can decode it,
                // and guessing would mean feeding noise to the mixer.
                Fail($"content-type {type} is not MP3");
                return;
            }

            using Stream net = await resp.Content.ReadAsStreamAsync(token).ConfigureAwait(false);
            bytes = new ByteRing(MaxBytes);

            // Reader: socket -> byte ring.
            Task reader = Task.Run(async () =>
            {
                var buf = new byte[8192];
                try
                {
                    while (!token.IsCancellationRequested)
                    {
                        int n = await net.ReadAsync(buf.AsMemory(0, buf.Length), token)
                                         .ConfigureAwait(false);
                        if (n <= 0) break;
                        bytes.Feed(buf, 0, n);
                    }
                }
                catch (Exception ex) when (ex is not OperationCanceledException) { /* falls through */ }
                finally { bytes.Complete(); }
            }, token);

            // Decoder: byte ring -> PCM ring. Runs on this task, synchronously, because
            // NLayer is a blocking pull API and this task exists to be blocked.
            await Task.Run(() => Decode(bytes, token), token).ConfigureAwait(false);
            await reader.ConfigureAwait(false);

            if (!token.IsCancellationRequested) Fail("stream ended");
        }
        catch (OperationCanceledException)
        {
            // Tuned away, or the connect timed out. Neither is an error worth a line.
            if (!token.IsCancellationRequested) Fail("timed out");
        }
        catch (Exception ex)
        {
            Fail($"{ex.GetType().Name}: {ex.Message}");
        }
        finally
        {
            bytes?.Complete();
        }
    }

    private void Decode(ByteRing bytes, CancellationToken token)
    {
        MpegFile mpeg;
        try
        {
            // Blocks until there is enough in the ring to find the first frame header.
            mpeg = new MpegFile(bytes);
        }
        catch (Exception ex)
        {
            Fail($"not decodable: {ex.GetType().Name}");
            return;
        }

        int rate = mpeg.SampleRate, channels = mpeg.Channels;
        if (rate <= 0 || channels <= 0) { Fail("decoder gave no format"); return; }

        var raw = new float[16384];
        var outBuf = new float[32768];
        double phase = 0;
        float prevL = 0, prevR = 0;
        double step = rate / (double)MixRate;
        bool live = false;

        while (!token.IsCancellationRequested)
        {
            int got;
            try { got = mpeg.ReadSamples(raw, 0, raw.Length); }
            catch (Exception ex) { Fail($"decode: {ex.GetType().Name}"); return; }
            if (got <= 0) return;                  // the ring closed: the socket died

            int frames = got / channels;
            int outCount = 0;

            if (rate == MixRate && channels == 2)
            {
                // The overwhelmingly common case: 44.1 kHz stereo, straight through.
                if (outBuf.Length < got) outBuf = new float[got];
                Array.Copy(raw, outBuf, got);
                outCount = got;
            }
            else
            {
                // Linear resample and/or mono fan-out. Linear is enough: the error it
                // makes is high-frequency, the source is a 128 kbps MP3 that has already
                // thrown that band away, and it costs nothing.
                int estimate = (int)(frames / step + 2) * 2;
                if (outBuf.Length < estimate) outBuf = new float[estimate];

                for (int f = 0; f < frames; f++)
                {
                    float l = raw[f * channels];
                    float r = channels > 1 ? raw[f * channels + 1] : l;
                    while (phase < 1.0 && outCount + 2 <= outBuf.Length)
                    {
                        float t = (float)phase;
                        outBuf[outCount++] = prevL + (l - prevL) * t;
                        outBuf[outCount++] = prevR + (r - prevR) * t;
                        phase += step;
                    }
                    phase -= 1.0;
                    prevL = l; prevR = r;
                }
            }

            if (outCount > 0) Push(outBuf, outCount);

            if (!live && Buffered >= PrebufferSeconds)
            {
                live = true;
                _backoff = RetrySeconds;          // a good connection resets the backoff
                _link = (int)RadioLink.Live;
                _note = "";
            }
        }
    }

    private void Fail(string why)
    {
        _note = why;
        _link = (int)RadioLink.Failed;
        lock (_gate) { _head = 0; _count = 0; }
    }

    public void Dispose() => Stop();

    /// <summary>
    /// A blocking, forward-only, non-seekable byte pipe between the socket and the decoder.
    ///
    /// NLayer's stream reader needs three things and only three: <see cref="Read"/>,
    /// <see cref="CanSeek"/> false, and a <see cref="Position"/> that counts bytes
    /// consumed. The first attempt threw <c>NotSupportedException</c> out of the
    /// <c>MpegFile</c> constructor for want of that last one - which is the kind of thing
    /// that is invisible in the code and obvious the first time it is run against a real
    /// server.
    ///
    /// <see cref="Read"/> blocks while the ring is empty, which is exactly what is wanted:
    /// the decode task is allowed to wait, and it is the only thing that does.
    /// </summary>
    private sealed class ByteRing : Stream
    {
        private readonly object _gate = new();
        private readonly byte[] _buf;
        private int _head, _count;
        private long _consumed;
        private bool _done;

        public ByteRing(int capacity) => _buf = new byte[capacity];

        /// <summary>Producer side. Drops the oldest bytes if the decoder has stalled.</summary>
        public void Feed(byte[] src, int off, int count)
        {
            lock (_gate)
            {
                if (count > _buf.Length) { off += count - _buf.Length; count = _buf.Length; }
                int overflow = _count + count - _buf.Length;
                if (overflow > 0) { _head = (_head + overflow) % _buf.Length; _count -= overflow; }

                int tail = (_head + _count) % _buf.Length;
                int first = Math.Min(count, _buf.Length - tail);
                Array.Copy(src, off, _buf, tail, first);
                if (count > first) Array.Copy(src, off + first, _buf, 0, count - first);
                _count += count;
                Monitor.PulseAll(_gate);
            }
        }

        public void Complete() { lock (_gate) { _done = true; Monitor.PulseAll(_gate); } }

        public override int Read(byte[] dst, int off, int count)
        {
            lock (_gate)
            {
                while (_count == 0 && !_done)
                    if (!Monitor.Wait(_gate, 10000)) return 0;   // producer wedged
                int n = Math.Min(count, _count);
                if (n <= 0) return 0;
                int first = Math.Min(n, _buf.Length - _head);
                Array.Copy(_buf, _head, dst, off, first);
                if (n > first) Array.Copy(_buf, 0, dst, off + first, n - first);
                _head = (_head + n) % _buf.Length;
                _count -= n;
                _consumed += n;
                return n;
            }
        }

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();

        /// <summary>Bytes handed to the decoder so far. NLayer reads this; nothing sets it.</summary>
        public override long Position
        {
            get { lock (_gate) return _consumed; }
            set => throw new NotSupportedException();
        }

        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
