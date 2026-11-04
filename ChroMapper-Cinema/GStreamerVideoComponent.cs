using System.IO;
using UnityEngine;

/// <summary>Attach to a mesh with a textured material. Mod callers configure native paths before Start.</summary>
[RequireComponent(typeof(MeshRenderer))]
public sealed class GStreamerVideoComponent : MonoBehaviour
{
    public string VideoPath = "sample.webm";
    public int Width = 1920;
    public int Height = 1080;
    public bool EnableAudio;

    public GStreamerVideoPlayer? Player { get; private set; }

    private Renderer? output;
    private Material? originalMaterial;
    private Material? videoMaterial;

    private void Start()
    {
        if (Player == null) Open();
    }

    public void Open()
    {
        Close();
        var path = Path.IsPathRooted(VideoPath)
            ? VideoPath : Path.Combine(Application.streamingAssetsPath, VideoPath);
        Player = new GStreamerVideoPlayer(path, Width, Height, true, EnableAudio);
        try
        {
            output = GetComponent<Renderer>();
            originalMaterial = output.sharedMaterial;
            videoMaterial = new Material(originalMaterial);
            output.sharedMaterial = videoMaterial;
            videoMaterial.mainTexture = Player.Texture;
        }
        catch (System.Exception e)
        {
            Debug.LogException(e);
            Close();
            throw;
        }
    }
    
    public void Play() => Player!.Play();
    public void Pause() => Player!.Pause();
    public void Seek(double time) => Player!.Seek(time);
    public void SetRate(float speed) => Player!.SetRate(speed);
    
    private void Update()
    {
        if (Player == null) return;
        var state = Player.GetSnapshot();
        //Debug.Log($"{state.State} {state.ErrorCode} {state.HasFrame} {state.DurationMilliseconds}");
        if (Player.State == GStreamerVideoPlayer.PlaybackState.Error)
        {
            Debug.LogError(Player.Error, this);
            Close();
            return;
        }
        Player.TryUploadFrame();
    }

    private void OnDisable() => Player?.Pause();
    private void OnDestroy() => Close();

    public void Close()
    {
        if (output != null && output.sharedMaterial == videoMaterial)
            output.sharedMaterial = originalMaterial;
        Player?.Dispose();
        Player = null;
        if (videoMaterial != null) Destroy(videoMaterial);
        videoMaterial = null;
    }
}
