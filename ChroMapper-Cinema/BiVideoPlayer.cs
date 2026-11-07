using System.IO;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.Video;

namespace ChroMapper_Cinema {

// Bisexual video player, chooses GStreamer or Unity VideoPlayer backend
[RequireComponent(typeof(MeshRenderer))]
public class BiVideoPlayer : MonoBehaviour {
	public bool EnableAudio;
	public readonly UnityEvent OnReady = new();
	public readonly UnityEvent<string> OnError = new();
	
	private GStreamerVideoPlayer? gst_player;
	private VideoPlayer? unity_player;
	private AudioSource? unity_audio; // Used to allow panning
	
	private Renderer? output;
	private Material? originalMaterial;
	private Material? videoMaterial;
	
	public void Open(string filename)
	{
		Close();
		var path = Path.IsPathRooted(filename)
			? filename
			: Path.Combine(Application.streamingAssetsPath, filename);
		
		switch (Application.platform) {
		case RuntimePlatform.LinuxEditor:
		case RuntimePlatform.LinuxPlayer:
			Plugin.Trace("Init GST VideoPlayer");
			try {
				gst_player = new GStreamerVideoPlayer(path, true, true);
				output = GetComponent<Renderer>();
				originalMaterial = output.sharedMaterial;
				videoMaterial = new Material(originalMaterial);
				output.sharedMaterial = videoMaterial;
				gst_player.OnResize.AddListener(() => OnReady.Invoke());
			}
			catch (System.Exception e) {
				Debug.LogException(e);
				OnError.Invoke(e.Message);
				Close();
				return;
			}
			break;
		default:
			Plugin.Trace("Init Unity VideoPlayer");
			unity_player = gameObject.AddComponent<VideoPlayer>();
			unity_player.errorReceived += (_, msg) => OnError.Invoke(msg);
			unity_player.prepareCompleted += (_) => OnReady.Invoke();
			unity_player.seekCompleted += (_) => { if (unity_player.isPaused) unity_player.StepForward(); };
			unity_player.playOnAwake = false;
			unity_player.url = path;
			unity_audio = gameObject.AddComponent<AudioSource>();
			unity_player.audioOutputMode = VideoAudioOutputMode.AudioSource;
			unity_player.SetTargetAudioSource(0, unity_audio);
			unity_player.Prepare();
			break;
		}
	}
	
	public void Play() {
		gst_player?.Play();
		unity_player?.Play();
	}
	public void Pause() {
		gst_player?.Pause();
		unity_player?.Pause();
	}
	public void Seek(double time) {
		gst_player?.Seek(time);
		if (unity_player != null) unity_player.time = time;
	}
	public void SetRate(float speed) {
		gst_player?.SetRate(speed);
		if (unity_player != null) unity_player.playbackSpeed = speed;
	}
	
	public void SetPanning(float panning) {
		gst_player?.SetPanning(panning);
		if (unity_audio != null) unity_audio.panStereo = panning;
	}
	
	public void SetVolume(float volume) {
		gst_player?.SetVolume(volume);
		if (unity_audio != null) unity_audio.volume = volume;
	}
	
	public void SetMuted(bool muted) {
		gst_player?.SetMuted(muted);
		if (unity_audio != null) unity_audio.mute = muted;
	}
	
	public uint Width { get { return gst_player?.Width ?? unity_player?.width ?? 0; } }
	public uint Height { get { return gst_player?.Height ?? unity_player?.height ?? 0; } }
	
	private void Update()
	{
		// Unity player doesn't need any of this
		if (gst_player == null) return;
		var state = gst_player.GetSnapshot();
		if (gst_player.State == GStreamerVideoPlayer.PlaybackState.Error)
		{
			Debug.LogError(gst_player.Error, this);
			OnError.Invoke(gst_player.Error!);
			Close();
			return;
		}
		if (videoMaterial!.mainTexture == null) {
			if (gst_player.Resize()) videoMaterial.mainTexture = gst_player.Texture;
			else return;
		}
		gst_player.TryUploadFrame();
	}

	private void OnDisable() => gst_player?.Pause();
	private void OnDestroy() => Close();

	public void Close()
	{
		if (output != null && output.sharedMaterial == videoMaterial)
			output.sharedMaterial = originalMaterial;
		gst_player?.Dispose();
		gst_player = null;
		if (videoMaterial != null) Destroy(videoMaterial);
		videoMaterial = null;
		if (unity_player != null) Destroy(unity_player);
		unity_player = null;
	}
}

}
