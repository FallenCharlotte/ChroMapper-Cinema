using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using UnityEngine;
using Object = UnityEngine.Object;

/// <summary>Decodes original local media. All managed calls and texture uploads belong to Unity's main thread.</summary>
public sealed class GStreamerVideoPlayer : IDisposable
{
    public enum PlaybackState { Opening, Paused, Playing, Seeking, Ended, Error }

    private const string Library = "cmv_gstreamer";
    private const string RuntimeVersion = "1.28.8";
    private const int ErrorCapacity = 2048;
    private static bool configured;
    private readonly int frameBytes;
    private readonly int rowBytes;
    private string? error;
    private bool? muted;
    private IntPtr pixels;
    private IntPtr player;
    private ulong uploadedSerial;
    private float volume = float.NaN;

    public GStreamerVideoPlayer(string path, int width, int height, bool isSrgb, bool enableAudio)
    {
        if (!File.Exists(path)) throw new FileNotFoundException("Video source was not found.", path);
        if (width <= 0) throw new ArgumentOutOfRangeException(nameof(width));
        if (height <= 0) throw new ArgumentOutOfRangeException(nameof(height));
        rowBytes = checked(width * 4);
        frameBytes = checked(rowBytes * height);
        ConfigureRuntime();
        try
        {
            var message = new byte[ErrorCapacity];
            if (cmv_gst_create(
                    path,
                    width,
                    height,
                    enableAudio ? 1 : 0,
                    out player,
                    message,
                    (ulong)message.Length)
                == 0)
                throw new InvalidOperationException(DecodeError(message));
            pixels = Marshal.AllocHGlobal(frameBytes);
            Texture = new Texture2D(width, height, TextureFormat.RGBA32, false, !isSrgb)
            {
                name = "Bundled video", wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear
            };
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    public Texture2D? Texture { get; private set; }
    public VideoFrame LastFrame { get; private set; }
    public PlaybackState State => GetSnapshot().State;
    public long TimeMilliseconds => GetSnapshot().TimeMilliseconds;

    public string? Error
    {
        get
        {
            if (GetSnapshot().State != PlaybackState.Error) return null;
            if (error != null) return error;
            var message = new byte[ErrorCapacity];
            cmv_gst_get_error(player, message, (ulong)message.Length);
            return error = DecodeError(message);
        }
    }

    public void Dispose()
    {
        if (player != IntPtr.Zero) cmv_gst_destroy(player);
        player = IntPtr.Zero;
        if (pixels != IntPtr.Zero) Marshal.FreeHGlobal(pixels);
        pixels = IntPtr.Zero;
        if (Texture != null) Object.Destroy(Texture);
        Texture = null;
    }

    private static void ConfigureRuntime()
    {
        if (configured) return;
        var runtimeDirectory = Application.isEditor
            ? Path.Combine(Application.dataPath, "Plugins/GStreamer/Linux64")
            : Path.Combine(Application.dataPath, "Plugins");
        ConfigureRuntime(runtimeDirectory, Path.Combine(Application.temporaryCachePath, "GStreamer", RuntimeVersion));
    }

    /// <summary>Overrides the default paths before opening the first player. Configuration is process-wide.</summary>
    /// <param name="runtimeDirectory">Package root containing the native bridge and its runtime/ directory.</param>
    /// <param name="cacheDirectory">Writable directory for registry.bin; created if missing.</param>
    /// <remarks>
    ///     The host must make the native bridge available to DllImport. Once initialization starts,
    ///     only the same paths can be configured again, including after a managed domain reload.
    /// </remarks>
    public static void ConfigureRuntime(string runtimeDirectory, string cacheDirectory)
    {
        if (string.IsNullOrWhiteSpace(runtimeDirectory))
            throw new ArgumentException("A runtime directory is required.", nameof(runtimeDirectory));
        if (string.IsNullOrWhiteSpace(cacheDirectory))
            throw new ArgumentException("A cache directory is required.", nameof(cacheDirectory));
        var runtime = Path.Combine(Path.GetFullPath(runtimeDirectory), "runtime/gstreamer");
        var cache = Path.GetFullPath(cacheDirectory);
        Directory.CreateDirectory(cache);
        var message = new byte[ErrorCapacity];
        if (cmv_gst_configure(
                Path.Combine(runtime, "plugins"),
                Path.Combine(cache, "registry.bin"),
                Path.Combine(runtime, "gst-plugin-scanner"),
                message,
                (ulong)message.Length)
            == 0)
            throw new InvalidOperationException(DecodeError(message));
        configured = true;
    }

    public PlaybackSnapshot GetSnapshot()
    {
        ThrowIfDisposed();
        cmv_gst_get_snapshot(player, out var snapshot);
        return snapshot;
    }

    public void Play()
    {
        ThrowIfDisposed();
        cmv_gst_play(player);
    }

    public void Pause()
    {
        ThrowIfDisposed();
        cmv_gst_pause(player);
    }

    public void Seek(double seconds)
    {
        ThrowIfDisposed();
        if (double.IsNaN(seconds) || double.IsInfinity(seconds) || seconds < 0)
            throw new ArgumentOutOfRangeException(nameof(seconds));
        cmv_gst_seek_ns(player, checked((long)Math.Round(seconds * 1_000_000_000)));
    }

    public void SetRate(float rate)
    {
        ThrowIfDisposed();
        if (float.IsNaN(rate) || float.IsInfinity(rate) || rate <= 0)
            throw new ArgumentOutOfRangeException(nameof(rate));
        cmv_gst_set_rate(player, rate);
    }

    public void SetMuted(bool value)
    {
        ThrowIfDisposed();
        if (muted == value) return;
        cmv_gst_set_muted(player, value ? 1 : 0);
        muted = value;
    }

    public void SetVolume(float value)
    {
        ThrowIfDisposed();
        if (value is < 0 or > 1 || float.IsNaN(value)) throw new ArgumentOutOfRangeException(nameof(value));
        if (volume == value) return;
        cmv_gst_set_volume(player, value);
        volume = value;
    }

    public bool TryUploadFrame()
    {
        ThrowIfDisposed();
        var result = cmv_gst_copy_rgba(player, pixels, rowBytes, (ulong)frameBytes, uploadedSerial, out var frame);
        switch (result)
        {
            case < 0:
                throw new InvalidOperationException("The decoded video frame does not fit its authored dimensions.");
            case 0:
                return false;
        }

        Texture!.LoadRawTextureData(pixels, frameBytes);
        Texture!.Apply(false, false);
        uploadedSerial = frame.Serial;
        LastFrame = frame;
        return true;
    }

    private void ThrowIfDisposed()
    {
        if (player == IntPtr.Zero) throw new ObjectDisposedException(nameof(GStreamerVideoPlayer));
    }

    private static string DecodeError(byte[] bytes)
    {
        var end = Array.IndexOf(bytes, (byte)0);
        return Encoding.UTF8.GetString(bytes, 0, end < 0 ? bytes.Length : end);
    }

    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
    private static extern int cmv_gst_configure(
        [MarshalAs(UnmanagedType.LPUTF8Str)] string plugins,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string registry,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string scanner,
        [Out] byte[] error,
        ulong capacity);

    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
    private static extern int cmv_gst_create(
        [MarshalAs(UnmanagedType.LPUTF8Str)] string path,
        int width,
        int height,
        int audioMode,
        out IntPtr player,
        [Out] byte[] error,
        ulong capacity);

    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
    private static extern int cmv_gst_play(IntPtr player);

    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
    private static extern int cmv_gst_pause(IntPtr player);

    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
    private static extern ulong cmv_gst_seek_ns(IntPtr player, long nanoseconds);

    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
    private static extern int cmv_gst_set_rate(IntPtr player, double rate);

    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
    private static extern int cmv_gst_set_volume(IntPtr player, double volume);

    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
    private static extern int cmv_gst_set_muted(IntPtr player, int muted);

    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
    private static extern int cmv_gst_get_snapshot(IntPtr player, out PlaybackSnapshot snapshot);

    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
    private static extern ulong cmv_gst_get_error(IntPtr player, [Out] byte[] error, ulong capacity);

    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
    private static extern int cmv_gst_copy_rgba(
        IntPtr player,
        IntPtr destination,
        int stride,
        ulong capacity,
        ulong afterSerial,
        out VideoFrame frame);

    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
    private static extern void cmv_gst_destroy(IntPtr player);

    [StructLayout(LayoutKind.Sequential)]
    public struct PlaybackSnapshot
    {
        public PlaybackState State;
        public int DesiredPlaying, HasFrame, ErrorCode;
        public ulong Generation, CompletedGeneration, FrameSerial;
        public long TimeMilliseconds, DurationMilliseconds;
        public double Rate;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct VideoFrame
    {
        public ulong Serial, Generation;
        public long PresentationNanoseconds, StreamNanoseconds, DurationNanoseconds;
        public int Width, Height;
    }
}
