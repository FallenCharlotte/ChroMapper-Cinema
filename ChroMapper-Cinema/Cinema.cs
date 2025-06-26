using System.IO;
using System.Reflection;
using UnityEngine;
using UnityEngine.Video;
using SimpleJSON;

namespace ChroMapper_Cinema {

public class Cinema {
	public ExtensionButton main_button;
	private AudioTimeSyncController atsc;
	
	private bool enabled;
	private GameObject parent;
	private GameObject screen;
	private VideoPlayer player;
	private string platform = Options.default_key;
	private PlatformSettings plat_settings;
	
	private float offset;
	private bool playing;
	
	public Cinema() {
		main_button = ExtensionButtons.AddButton(
			Utils.LoadSprite("ChroMapper_Cinema.Resources.Icon.png"),
			"Cinema",
			ToggleEnabled);
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
		mesh.vertices = new Vector3[] { Utils.V3(-0.5f, -0.5f, 0.01f), Utils.V3(0.5f, -0.5f, 0.01f), Utils.V3(0.5f, 0.5f, 0.01f), Utils.V3(-0.5f, 0.5f, 0.01f) };
		mesh.uv = new Vector2[] { Utils.V2(0, 0), Utils.V2(1, 0), Utils.V2(1, 1), Utils.V2(0, 1) };
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
			throw new System.Exception(msg);
		};
		player.prepareCompleted += (VideoPlayer p) => {
			var scale = new Vector2(plat_settings.height / p.height * p.width, plat_settings.height);
			Utils.SetTransform(screen, plat_settings.pos, scale, plat_settings.rotation);
			Debug.Log("Cinema prepared: " + (p.isPrepared ? "true" : "false"));
			enabled = true;
			playing = false;
			
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
#if CHROMPER_11
		var map_dir = BeatSaberSongContainer.Instance.Song.Directory;
#else
		var map_dir = BeatSaberSongContainer.Instance.Info.Directory;
#endif
		var cinema_file = Path.Combine(map_dir, "cinema-video.json");
		if (!File.Exists(cinema_file)) {
			screen.SetActive(false);
			enabled = false;
			return Utils.Error("No cinema-video.json!");
		}
		
		StreamReader reader = new StreamReader(cinema_file);
		var cinema_info = JSONNode.Parse(reader.ReadToEnd()).AsObject;
		reader.Close();
		
		Options.instance.LoadSettings();
		plat_settings = Options.instance.GetPlatformSettings(platform);
		if (cinema_info.HasKey("screenPosition")) {
			plat_settings.pos = Utils.JSONV3(cinema_info["screenPosition"].AsObject);
		}
		if (cinema_info.HasKey("screenRotation")) {
			plat_settings.rotation = Utils.JSONV3(cinema_info["screenRotation"].AsObject);
		}
		if (cinema_info.HasKey("screenHeight")) {
			plat_settings.height = cinema_info["screenHeight"].AsFloat;
		}
		
		offset = ((int)cinema_info["offset"]) / 1000.0f;
		
		var mapFolderName = new DirectoryInfo(map_dir).Name;
		var wipDir = Directory.GetParent(map_dir).FullName;
		var videoPath = Path.Combine(wipDir, "CinemaWIPVideos", mapFolderName, (string)cinema_info["videoFile"]);
		
		if (!File.Exists(videoPath)) {
			videoPath = Path.Combine(map_dir, (string)cinema_info["videoFile"]);
		}
		
		if (!File.Exists(videoPath)) {
			return Utils.Error("Video file not downloaded!");
		}
		
		screen.SetActive(true);
		
		player.url = videoPath;
		player.Prepare();
		
		return "";
	}
	
	public void ToggleEnabled() {
		if (enabled) {
			player.Stop();
			screen.SetActive(false);
			enabled = false;
		}
		else {
			string err = LoadVideo();
			if (err != "") {
				PersistentUI.Instance.ShowDialogBox(err, null, PersistentUI.DialogBoxPresetType.Ok);
			}
		}
	}
	
	private void Update() {
		if (!enabled) return;
		
		var time = atsc.CurrentSeconds + offset;
		
		// Causes lag when playing
		if (!playing) {
			player.time = time;
		}
		
		if (atsc.IsPlaying && !playing && time >= 0) {
			player.Play();
			playing = true;
		}
		
		if (!atsc.IsPlaying && playing) {
			player.Pause();
			playing = false;
		}
	}
	
	private void UpdateSongSpeed(object obj) {
		if (!enabled) return;
		
		player.playbackSpeed = ((float)obj) / 10.0f;
	}
	
	private void AfterSeek(VideoPlayer player) {
		if (!playing) {
			player.StepForward();
		}
	}
}

}
