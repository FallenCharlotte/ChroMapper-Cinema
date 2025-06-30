using System.IO;
using System.Reflection;
using UnityEngine;
using UnityEngine.Video;
using SimpleJSON;

namespace ChroMapper_Cinema {

public class Cinema {
	public JSONObject cinema_info = new JSONObject();
	
	internal object? options_window = null;
	
	private AudioTimeSyncController? atsc = null;
	
	public bool enabled = false;
	private GameObject? parent;
	private GameObject? screen;
	private VideoPlayer? player;
	private string platform = "";
	private PlatformSettings plat_settings;
	
	private float offset;
	private bool playing;
	
	public Cinema() {
		
	}
	
	public void Init(AudioTimeSyncController atsc, GameObject platform) {
		this.parent = platform;
		this.atsc = atsc;
		
		this.platform = platform.name.Replace("(Clone)", "");
		Debug.Log($"Platform: {this.platform}");
		
		screen = new GameObject("Cinema Screen");
		screen.transform.SetParent(parent.transform);
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
		screen.AddComponent<MeshRenderer>().material = mat;
		
		player = screen.AddComponent<VideoPlayer>();
		player.errorReceived += (VideoPlayer p, string msg) => {
			enabled = false;
			screen.SetActive(false);
			if (options_window != null) {
				UpdateToggleButton();
			}
			throw new System.Exception(msg);
		};
		player.prepareCompleted += (VideoPlayer p) => {
			var scale = new Vector2(plat_settings.height / p.height * p.width, plat_settings.height);
			Utils.SetTransform(screen, plat_settings.pos * 1.667f, scale * 1.667f, plat_settings.rotation);
			Debug.Log("Cinema prepared: " + (p.isPrepared ? "true" : "false"));
			enabled = true;
			playing = false;
			
			if (options_window != null) {
				UpdateToggleButton();
			}
			
			Update();
		};
		player.seekCompleted += AfterSeek;
		player.playOnAwake = true;
		player.audioOutputMode = VideoAudioOutputMode.None;
		
		LoadVideo();
		
		atsc.TimeChanged += Update;
		Settings.NotifyBySettingName("SongSpeed", UpdateSongSpeed);
	}
	
	public string LoadVideo() {
		var cinema_info = Plugin.map_config!.cinema_video;
		
		if (!Plugin.map_config.config_exists) {
			return "";
		}
		
		plat_settings = PlatformSettings.GetPlatformSettings(platform);
		if (cinema_info.HasKey("screenPosition")) {
			plat_settings.pos = cinema_info["screenPosition"].AsObject.ReadVector3();
		}
		if (cinema_info.HasKey("screenRotation")) {
			plat_settings.rotation = cinema_info["screenRotation"].AsObject.ReadVector3();
		}
		if (cinema_info.HasKey("screenHeight")) {
			plat_settings.height = cinema_info["screenHeight"].AsFloat;
		}
		
		offset = ((cinema_info["offset"] as JSONNumber) ?? 0) / 1000.0f;
		
		if (!Plugin.map_config!.video_downloaded) {
			return Utils.Error("Video file not downloaded!");
		}
		
		screen!.SetActive(true);
		
		player!.url = Plugin.map_config!.video_file!;
		player!.Prepare();
		
		return "";
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
	
	public void ToggleEnabled() {
		if (enabled) {
			player!.Stop();
			screen!.SetActive(false);
			enabled = false;
			if (options_window != null) {
				UpdateToggleButton();
			}
		}
		else {
			string err = LoadVideo();
			if (err != "") {
				PersistentUI.Instance.ShowDialogBox(err, null, PersistentUI.DialogBoxPresetType.Ok);
			}
		}
	}
	
	internal void UpdateToggleButton() {
		(options_window as OptionsWindow)!.toggle_visibility!.SetImage(Utils.LoadSprite(enabled
			? "ChroMapper_Cinema.Resources.eye.png"
			: "ChroMapper_Cinema.Resources.eye-slash.png"));
	}
	
	private void Update() {
		if (!enabled) return;
		
		var time = atsc!.CurrentSeconds + offset;
		
		// Causes lag when playing
		if (!playing) {
			player!.time = time;
		}
		
		if (atsc.IsPlaying && !playing && time >= 0) {
			player!.Play();
			playing = true;
		}
		
		if (!atsc.IsPlaying && playing) {
			player!.Pause();
			playing = false;
		}
	}
	
	private void UpdateSongSpeed(object obj) {
		if (!enabled) return;
		
		player!.playbackSpeed = ((float)obj) / 10.0f;
	}
	
	private void AfterSeek(VideoPlayer player) {
		if (!playing) {
			player.StepForward();
		}
	}
}

}
