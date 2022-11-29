using System.IO;
using UnityEngine;
using UnityEngine.Video;
using SimpleJSON;

namespace ChroMapper_Cinema {

public class Cinema {
	//public ExtensionButton main_button;
	private AudioTimeSyncController atsc;
	
	private bool enabled;
	private GameObject parent;
	private GameObject screen;
	private VideoPlayer player;
	
	private float offset;
	private bool playing;
	
	public Cinema() {
		/*
		main_button = ExtensionButtons.AddButton(
			LoadSprite("ChroMapper_Cinema.Resources.Icon.png"),
			"Cinema",
			ToggleWindow);
			* */
	}
	
	public void Init(AudioTimeSyncController atsc) {
		this.atsc = atsc;
		
		var map_dir = BeatSaberSongContainer.Instance.Song.Directory;
		var cinema_file = Path.Combine(map_dir, "cinema-video.json");
		if (!File.Exists(cinema_file)) {
			enabled = false;
			return;
		}
		
		StreamReader reader = new StreamReader(cinema_file);
		var cinema_info = (JSONObject)JSONNode.Parse(reader.ReadToEnd());
		reader.Close();
		
		offset = ((int)cinema_info["offset"]) / 1000.0f;
		
		screen = new GameObject("Cinema Screen");
		if (parent) {
			screen.transform.SetParent(parent.transform);
		}
		SetTransform(screen, V3(0, 16, 48), V3(16, 9, 1));
		
		
		var mesh = new Mesh();
		mesh.name = "Scripted_Plane_New_Mesh";
		mesh.vertices = new Vector3[] { V3(-1, -1, 0.01f), V3(1, -1, 0.01f), V3(1, 1, 0.01f), V3(-1, 1, 0.01f) };
		mesh.uv = new Vector2[] { V2(0, 0), V2(1, 0), V2(1, 1), V2(0, 1) };
		mesh.triangles = new int[] { 2, 1, 0, 3, 2, 0 };
		mesh.RecalculateNormals();
		screen.AddComponent<MeshFilter>().mesh = mesh;
		
		var mat = new Material(Shader.Find("UI/Default"));
		mat.color = Color.white;
		screen.AddComponent<MeshRenderer>().material = mat;
		
		player = screen.AddComponent<VideoPlayer>();
		player.errorReceived += (VideoPlayer p, string msg) => {
			enabled = false;
			throw new System.Exception(msg);
		};
		player.prepareCompleted += (VideoPlayer p) => {
			Debug.Log("Cinema prepared: " + (p.isPrepared ? "true" : "false"));
			enabled = true;
			playing = false;
			p.StepForward();
		};
		player.seekCompleted += AfterSeek;
		player.playOnAwake = true;
		player.audioOutputMode = VideoAudioOutputMode.None;
		player.url = Path.Combine(map_dir, (string)cinema_info["videoFile"]);
		player.Prepare();
		
		atsc.TimeChanged += Update;
		Settings.NotifyBySettingName("SongSpeed", UpdateSongSpeed);
	}
	
	public void UpatePlatform(GameObject parent) {
		this.parent = parent;
		
		if (screen) {
			screen.transform.SetParent(parent.transform);
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
	
	// Unity helpers
	
	private Transform SetTransform(GameObject obj, Vector3 pos, Vector3 scale)
	{
		var trans = obj.GetComponent<Transform>();
		if (trans == null) {
			trans = obj.AddComponent<Transform>();
		}
		trans.localPosition = pos;
		trans.localScale = scale;
		
		return trans;
	}
	
	private Vector2 V2(float x, float y) {
		return new Vector2(x, y);
	}
	private Vector3 V3(float x, float y, float z) {
		return new Vector3(x, y, z);
	}
}

}
