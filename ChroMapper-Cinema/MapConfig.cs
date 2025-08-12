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
#if CHROMPER_11
		map_dir = BeatSaberSongContainer.Instance.Song.Directory;
#else
		map_dir = BeatSaberSongContainer.Instance.Info.Directory;
#endif
		cinema_file = Path.Combine(map_dir, "cinema-video.json");
		
		var mapFolderName = new DirectoryInfo(map_dir!).Name;
		var wipDir = Directory.GetParent(map_dir!).FullName;
		
		video_dir = Path.Combine(wipDir, "CinemaWIPVideos", mapFolderName);
		
		if (!File.Exists(cinema_file)) {
			cinema_video = new JSONObject();
			config_exists = false;
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
		
		TryVideo();
	}
	
	public void TryVideo() {
		if (map_dir == null || !cinema_video.HasKey("videoFile")) {
			video_file = null;
			video_downloaded = false;
			return;
		}
		
		video_file = Path.Combine(video_dir, (string)cinema_video["videoFile"]);
		
		switch (Application.platform) {
		case RuntimePlatform.LinuxEditor:
		case RuntimePlatform.LinuxPlayer:
			video_file += ".webm";
			break;
		}
		
		if (!File.Exists(video_file)) {
			var old_location = Path.Combine(map_dir, (string)cinema_video["videoFile"]);
			if (File.Exists(old_location)) {
				Debug.LogWarning("Cinema video file found in old location!");
				video_file = old_location;
			}
		}
		
		video_downloaded = File.Exists(video_file);
	}
	
	public JSONNode this[string key] {
		get {
			return cinema_video[key];
		}
		set {
			cinema_video[key] = value;
			Save();
		}
	}
};

}
