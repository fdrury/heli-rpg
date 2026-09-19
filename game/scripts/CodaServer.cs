using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Godot;
using Rotorwash.Sim;

namespace Rotorwash;

/// <summary>
/// Manages a llama-server subprocess behind a Windows Job Object.
///
/// D-006a names the architecture: process isolation IS the graceful degradation,
/// implemented by the OS for free. If the binary or model is absent, ModelAvailable
/// is false and the game runs on baked lines alone — which is exactly the intended
/// fallback. When the server is present, it speaks HTTP on localhost and nothing
/// about it can take the game down with it.
///
/// The Job Object ensures cleanup on crash: JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE
/// kills the server when Godot's process handle closes, even on an unhandled abort.
/// </summary>
public sealed partial class CodaServer : Node
{
    public CodaPolicy Policy { get; } = new();

    /// <summary>True once the server is running and has passed a health check.</summary>
    public bool ModelAvailable { get; private set; }

    private Process? _process;
    private IntPtr _jobHandle;
    private HttpClient? _http;
    private int _port = 8384;

    public override void _Ready()
    {
        string projectDir = ProjectSettings.GlobalizePath("res://");
        string baseDir = Path.GetFullPath(Path.Combine(projectDir, ".."));
        string binary = Path.Combine(baseDir, "tools", "llama", "llama-server.exe");
        string model = Path.Combine(baseDir, "tools", "llama", "models",
                                     "qwen3-1.7b-q4_k_m.gguf");

        if (!File.Exists(binary))
        {
            GD.Print("[coda] no llama-server binary — running on baked lines alone");
            return;
        }
        if (!File.Exists(model))
        {
            GD.Print("[coda] no model file — running on baked lines alone");
            return;
        }

        Task.Run(() => StartServer(binary, model));
    }

    public override void _ExitTree() => StopServer();

    // ----------------------------------------------------------- lifecycle

    private void StartServer(string binary, string model)
    {
        try
        {
            // Find a free port so multiple instances do not collide.
            using (var probe = new System.Net.Sockets.TcpListener(
                       System.Net.IPAddress.Loopback, 0))
            {
                probe.Start();
                _port = ((System.Net.IPEndPoint)probe.LocalEndpoint).Port;
                probe.Stop();
            }

            var psi = new ProcessStartInfo
            {
                FileName = binary,
                Arguments = $"--model \"{model}\" " +
                            $"--ctx-size 2048 " +
                            $"--host 127.0.0.1 --port {_port} " +
                            $"--n-gpu-layers 99 --threads 2",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };

            _process = Process.Start(psi);
            if (_process is null)
            {
                GD.PrintErr("[coda] failed to start llama-server");
                return;
            }

            GD.Print($"[coda] llama-server pid {_process.Id} on port {_port}");

            // A Job Object with KILL_ON_JOB_CLOSE is the safety net for crashes.
            if (OperatingSystem.IsWindows()) AttachJobObject();

            // Poll the health endpoint until the model is loaded.
            _http = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
            string healthUrl = $"http://127.0.0.1:{_port}/health";

            for (int i = 0; i < 60; i++)
            {
                if (_process.HasExited)
                {
                    GD.PrintErr("[coda] llama-server exited during startup");
                    return;
                }
                Thread.Sleep(500);
                try
                {
                    var resp = _http.GetAsync(healthUrl).GetAwaiter().GetResult();
                    if (resp.IsSuccessStatusCode)
                    {
                        ModelAvailable = true;
                        Policy.ModelAvailable = true;
                        GD.Print($"[coda] model ready on port {_port}");
                        return;
                    }
                }
                catch { /* server not ready yet */ }
            }
            GD.PrintErr("[coda] llama-server did not become healthy in 30 s");
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[coda] startup failed: {ex.Message}");
        }
    }

    private void StopServer()
    {
        if (_process is { HasExited: false })
        {
            try { _process.Kill(entireProcessTree: true); }
            catch { /* best effort */ }
            GD.Print("[coda] llama-server stopped");
        }
        _process?.Dispose();
        _process = null;

        if (_jobHandle != IntPtr.Zero)
        {
            CloseHandle(_jobHandle);
            _jobHandle = IntPtr.Zero;
        }

        _http?.Dispose();
        _http = null;
        ModelAvailable = false;
        Policy.ModelAvailable = false;
    }

    // ----------------------------------------------------------- requests

    /// <summary>
    /// Request a coda from the local model. Returns raw text, or null on any failure.
    /// The caller validates through <see cref="CodaPolicy.Validate"/>.
    /// </summary>
    public async Task<string?> RequestCodaAsync(CodaRequest request)
    {
        if (!ModelAvailable || _http is null) return null;

        try
        {
            string prompt = request.BuildPrompt();
            var payload = new
            {
                prompt,
                n_predict = 60,
                stop = new[] { "\n" },
                temperature = 0.7,
                top_p = 0.9,
            };

            string json = JsonSerializer.Serialize(payload);
            using var content = new StringContent(json, System.Text.Encoding.UTF8,
                                                  "application/json");
            using var cts = new CancellationTokenSource(
                TimeSpan.FromSeconds(Policy.HardAbandon));

            var sw = Stopwatch.StartNew();
            var resp = await _http.PostAsync(
                $"http://127.0.0.1:{_port}/completion", content, cts.Token);
            double elapsed = sw.Elapsed.TotalSeconds;

            if (!resp.IsSuccessStatusCode) return null;

            string body = await resp.Content.ReadAsStringAsync(cts.Token);
            using var doc = JsonDocument.Parse(body);
            string? text = doc.RootElement.GetProperty("content").GetString();

            // Update measured p95 with an exponential moving average, so the gate
            // tracks the actual hardware rather than a guess from a benchmark.
            Policy.MeasuredP95 = Policy.MeasuredP95 * 0.8 + elapsed * 0.2;

            return text;
        }
        catch (OperationCanceledException)
        {
            GD.Print("[coda] request timed out");
            return null;
        }
        catch (Exception ex)
        {
            GD.Print($"[coda] request failed: {ex.Message}");
            return null;
        }
    }

    // ------------------------------------------------------- Job Object (Win32)

    private void AttachJobObject()
    {
        _jobHandle = CreateJobObjectW(IntPtr.Zero, null);
        if (_jobHandle == IntPtr.Zero) return;

        var info = new JOBOBJECT_EXTENDED_LIMIT_INFORMATION();
        info.BasicLimitInformation.LimitFlags = 0x2000; // KILL_ON_JOB_CLOSE
        SetInformationJobObject(_jobHandle, 9, ref info,
            Marshal.SizeOf<JOBOBJECT_EXTENDED_LIMIT_INFORMATION>());

        if (_process is not null)
            AssignProcessToJobObject(_jobHandle, _process.Handle);
    }

    // --- P/Invoke ---

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr handle);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern IntPtr CreateJobObjectW(IntPtr security, string? name);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool SetInformationJobObject(IntPtr job, int infoClass,
        ref JOBOBJECT_EXTENDED_LIMIT_INFORMATION info, int size);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool AssignProcessToJobObject(IntPtr job, IntPtr process);

    [StructLayout(LayoutKind.Sequential)]
    private struct JOBOBJECT_BASIC_LIMIT_INFORMATION
    {
        public long PerProcessUserTimeLimit, PerJobUserTimeLimit;
        public uint LimitFlags;
        public UIntPtr MinimumWorkingSetSize, MaximumWorkingSetSize;
        public uint ActiveProcessLimit;
        public UIntPtr Affinity;
        public uint PriorityClass, SchedulingClass;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct IO_COUNTERS
    {
        public ulong ReadOps, WriteOps, OtherOps, ReadBytes, WriteBytes, OtherBytes;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct JOBOBJECT_EXTENDED_LIMIT_INFORMATION
    {
        public JOBOBJECT_BASIC_LIMIT_INFORMATION BasicLimitInformation;
        public IO_COUNTERS IoInfo;
        public UIntPtr ProcessMemoryLimit, JobMemoryLimit;
        public UIntPtr PeakProcessMemoryUsed, PeakJobMemoryUsed;
    }
}
