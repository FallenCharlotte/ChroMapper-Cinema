using System.IO;
using UnityEngine;
using SimpleJSON;

namespace ChroMapper_Cinema {

public class MapConfig {
	public JSONObject cinema_video = new JSONObject();
	public string? cinema_file = null;
	public string? map_dir = null;
	public string? video_dir = null;
	public bool config_exists = false;
	public string? video_file = null;
	public bool video_downloaded = false;
	
	public void Load() {
		map_dir = BeatSaberSongContainer.Instance.Info.Directory;
		cinema_file = Path.Combine(map_dir, "cinema-video.json");
		
		var mapFolderName = new DirectoryInfo(map_dir!).Name;
		var parentFolder = Directory.GetParent(map_dir!).FullName;
		
		if (parentFolder == Settings.Instance.CustomWIPSongsFolder) {
			video_dir = Path.Combine(parentFolder, "CinemaWIPVideos", mapFolderName);
		}
		else {
			video_dir = map_dir;
		}
		
		if (!File.Exists(cinema_file)) {
			cinema_video = new JSONObject();
			config_exists = false;
			cinema_file = null;
			return;
		}
		
		config_exists = true;
		
		var reader = new StreamReader(cinema_file);
		cinema_video = JSONNode.Parse(reader.ReadToEnd()).AsObject;
		reader.Close();
		
		TryVideo();
	}
	
	public void Save() {
		var writer = new StreamWriter(cinema_file);
		writer.Write(cinema_video.ToString(4));
		writer.Close();
		
		config_exists = true;
		
		TryVideo();
		Plugin.controller!.AutoLoad();
	}
	
	public void TryVideo() {
		if (map_dir == null || !cinema_video.HasKey("videoFile")) {
			video_file = null;
			video_downloaded = false;
			return;
		}
		
		video_file = Path.Combine(video_dir, (string)cinema_video["videoFile"]);
		
		Plugin.Trace($"{video_file} eixsts? {File.Exists(video_file)}");
		if (!File.Exists(video_file)) do {
			var old_location = Path.Combine(map_dir, (string)cinema_video["videoFile"]);
			if (File.Exists(old_location)) {
				Debug.LogWarning("Cinema video file found in old location!");
				video_file = old_location;
				break;
			}
			var theater_location = Path.Combine(Settings.Instance.BeatSaberInstallation, "Beat Saber_Data", "CustomLevels", "TheaterOSTVideos", (string)cinema_video["videoFile"]);
			if (File.Exists(theater_location)) {
				video_file = theater_location;
				break;
			}
		} while (false);
		
		video_downloaded = File.Exists(video_file);
		Plugin.Trace($"video_downloaded: {video_downloaded}");
	}
	
	public JSONNode? this[string key] {
		get {
			return GetNode(cinema_video, key);
		}
		set {
			if (value is JSONNode n) {
				SetNode(cinema_video, key, value);
			}
			else {
				RemoveNode(cinema_video, key);
			}
			Save();
		}
	}
	
	private static JSONNode? GetNode(JSONNode root, string name) {
		string[] path = name.Split('.');
		foreach (string node in path) {
			if (!(root?.HasKey(node) ?? false)) {
				return null;
			}
			root = root[node];
		}
		return root;
	}
	
	private static JSONNode? SetNode(JSONNode root, string name, JSONNode? o) {
		if (o == null) {
			RemoveNode(root, name);
			return o;
		}
		string[] path = name.Split('.');
		for (int i = 0; i < path.Length - 1; ++i) {
			root = root[path[i]];
		}
		root[path[path.Length-1]] = o;
		return o;
	}
	
	private static void RemoveNode(JSONNode root, string name) {
		string[] path = name.Split('.');
		for (int i = 0; i < path.Length - 1; ++i) {
			if (!(root?.HasKey(path[i]) ?? false)) {
				return;
			}
			root = root[path[i]];
		}
		root?.Remove(path[path.Length - 1]);
	}
};

}
