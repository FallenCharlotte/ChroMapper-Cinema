using System.IO;
using UnityEngine;
using UnityEngine.Video;
using SimpleJSON;

namespace ChroMapper_Cinema.UserInterface {

public class UI {
	//public ExtensionButton main_button;
	private bool enabled;
	private GameObject screen;
	private VideoPlayer player;
	
	private float offset;
	
	public UI() {
		/*
		main_button = ExtensionButtons.AddButton(
			LoadSprite("ChroMapper_Cinema.Resources.Icon.png"),
			"Cinema",
			ToggleWindow);
			* */
	}
	
	public void AddWindow(GameObject parent) {
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
		
		screen = AddChild(parent, "Cinema Screen");
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
			p.Play();
			Debug.Log(offset);
		};
		player.playOnAwake = false;
		player.audioOutputMode = VideoAudioOutputMode.None;
		player.url = Path.Combine(map_dir, (string)cinema_info["videoFile"]);
		player.isLooping = true;
		player.Prepare();
	}
	
	
	
	private GameObject AddChild(GameObject parent, string name, params System.Type[] components) {
		var obj = new GameObject(name, components);
		obj.transform.SetParent(parent.transform);
		return obj;
	}
	
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
