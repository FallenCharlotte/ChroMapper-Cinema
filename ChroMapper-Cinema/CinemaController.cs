using System.IO;
using System.Reflection;
using UnityEngine;
using SimpleJSON;

namespace ChroMapper_Cinema {

public class CinemaController {
	public JSONObject cinema_info = new JSONObject();
	
	internal object? options_window = null;
	
	private AudioTimeSyncController? atsc = null;
	
	public MainThreadRunner? mtr { get; private set; }
	
	public bool showPlayer = false;
	private GameObject? screen;
	public BiVideoPlayer? player;
	private string platform = "";
	private PlatformSettings plat_settings;
	
	private float offset;
	private bool playing;
	
	public CinemaController() { }
	
	public void Init(GameObject platform) {
		Plugin.Trace($"CinemaController.Init({platform})");
		this.atsc = Object.FindAnyObjectByType<AudioTimeSyncController>();
		
		this.platform = BeatSaberSongContainer.Instance.Info.EnvironmentName;
		Plugin.Trace($"Platform: {this.platform}");
		
		if (mtr != null) {
			GameObject.Destroy(mtr.gameObject);
		}
		mtr = new GameObject("MainThreadRunner").AddComponent<MainThreadRunner>();
		
		if (screen != null) {
			GameObject.Destroy(screen);
		}
		screen = new GameObject("Cinema Screen");
		screen.transform.SetParent(platform.transform);
		screen.SetActive(false);
		
		var mesh = new Mesh();
		mesh.name = "Scripted_Plane_New_Mesh";
		mesh.vertices = new Vector3[] { new Vector3(-0.5f, -0.5f, 0.01f), new Vector3(0.5f, -0.5f, 0.01f), new Vector3(0.5f, 0.5f, 0.01f), new Vector3(-0.5f, 0.5f, 0.01f) };
		mesh.uv = new Vector2[] { new Vector2(0, 0), new Vector2(1, 0), new Vector2(1, 1), new Vector2(0, 1) };
		mesh.triangles = new int[] { 2, 1, 0, 3, 2, 0 };
		mesh.RecalculateNormals();
		screen.AddComponent<MeshFilter>().mesh = mesh;
		
		var mat = new Material(Shader.Find("UI/Default"));
		mat.color = Color.white;
		mat.SetFloat("_ColorMask", 14.0f); // No idea what this means but it looks fine
		screen.AddComponent<MeshRenderer>().material = mat;
		
		player = screen.AddComponent<BiVideoPlayer>();
		player.OnError.AddListener((string msg) => {
			showPlayer = false;
			screen.SetActive(false);
			TryRefreshWindow();
			throw new System.Exception(msg);
		});
		player.OnReady.AddListener(() => {
			Plugin.Trace("Video prepared! Show player!");
			var scale = new Vector2(plat_settings.height / player.Height * player.Width, plat_settings.height);
			Utils.SetTransform(screen, plat_settings.pos * 1.667f, scale * 1.667f, plat_settings.rotation);
			
			showPlayer = true;
			playing = false;
			
			player.SetPanning(-1.0f);
			player.SetVolume(Settings.Instance.SongVolume);
			
			TryRefreshWindow();
			
			OnTimeChanged();
		});
		
#if CHROMPER_13
		atsc.TimeChanged += OnTimeChanged;
#else
		atsc.OnTimeChanged += OnTimeChanged;
#endif
		Settings.NotifyBySettingName("SongSpeed", UpdateSongSpeed);
		
		if (Plugin.enableUI) {
			var mapEditorUI = Object.FindAnyObjectByType<MapEditorUI>();
			MakeWindow(mapEditorUI);
		}
		
		Plugin.map_config!.Load();
		LoadVideo();
	}
	
	public string LoadVideo() {
		Plugin.Trace("LoadVideo");
		var cinema_info = Plugin.map_config!.cinema_video;
		
		TryRefreshWindow();
		
		if (!Plugin.map_config.config_exists) {
			Plugin.Trace("No config, no video");
			screen!.SetActive(false);
			return "";
		}
		
		plat_settings = PlatformSettings.GetPlatformSettings(platform);
		if (cinema_info.HasKey("screenPosition")) {
			ReadInVector3(ref plat_settings.pos, cinema_info["screenPosition"].AsObject);
		}
		if (cinema_info.HasKey("screenRotation")) {
			plat_settings.rotation = cinema_info["screenRotation"].AsObject.ReadVector3();
		}
		if (cinema_info.HasKey("screenHeight")) {
			plat_settings.height = cinema_info["screenHeight"].AsFloat;
		}
		
		offset = ((cinema_info["offset"] as JSONNumber) ?? 0) / 1000.0f;
		
		if (!Plugin.map_config!.video_downloaded) {
			showPlayer = false;
			screen!.SetActive(false);
			TryRefreshWindow();
			Plugin.Trace("Video file not downloaded");
			return Utils.Error("Video file not downloaded!");
		}
		
		screen!.SetActive(true);
		
		Plugin.Trace($"Load video: {Plugin.map_config!.video_file!}");
		player!.Open(Plugin.map_config!.video_file!);
		
		player!.SetMuted(true);
		
		return "";
	}
	
	private void ReadInVector3(ref Vector3 dest, JSONObject source) {
		if (source.HasKey("x")) dest.x = source["x"].AsFloat;
		if (source.HasKey("y")) dest.y = source["y"].AsFloat;
		if (source.HasKey("z")) dest.z = source["z"].AsFloat;
	}
	
	internal void MakeWindow(MapEditorUI mapEditorUI) {
		options_window = OptionsWindow.InitProxy(mapEditorUI);
	}
	
	public void ButtonPress() {
		Plugin.map_config!.Load();
		if (options_window != null) {
			ToggleWindow();
		}
		else {
			ToggleEnabled();
		}
	}
	
	internal void ToggleWindow() {
		(options_window as OptionsWindow)!.ToggleWindow();
	}
	
	public void TryRefreshWindow() {
		if (options_window != null) {
			RefreshWindow();
		}
	}
	
	internal void RefreshWindow() {
		(options_window as OptionsWindow)!.TriggerRefresh();
	}
	
	public void ToggleEnabled() {
		if (showPlayer) {
			player!.Pause();
			screen!.SetActive(false);
			showPlayer = false;
			TryRefreshWindow();
		}
		else {
			string err = LoadVideo();
			if (err != "") {
				PersistentUI.Instance.ShowDialogBox(err, null, PersistentUI.DialogBoxPresetType.Ok);
			}
		}
	}
	
	private void OnTimeChanged() {
		if (!showPlayer) return;
		
		var time = atsc!.CurrentSeconds + offset;
		
		// Causes lag when playing
		if (!playing) {
			player!.Seek(System.Math.Max(0, time));
		}
		
		if (atsc.IsPlaying && !playing && time >= 0) {
			player!.Play();
			playing = true;
		}
		
		if (!atsc.IsPlaying && playing) {
			player!.Pause();
			playing = false;
			player!.Seek(System.Math.Max(0, time));
		}
	}
	
	private void UpdateSongSpeed(object obj) {
		if (player == null) return;
		
		player!.SetRate(((float)obj) / 10.0f);
	}
}

}
