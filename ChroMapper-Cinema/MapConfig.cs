using System.IO;
using SimpleJSON;

namespace ChroMapper_Cinema {

public class MapConfig {
	public JSONObject cinema_video;
	public string cinema_file;
	public string map_dir;
	
	public void Load() {
#if CHROMPER_11
		map_dir = BeatSaberSongContainer.Instance.Song.Directory;
#else
		map_dir = BeatSaberSongContainer.Instance.Info.Directory;
#endif
		cinema_file = Path.Combine(map_dir, "cinema-video.json");
		if (!File.Exists(cinema_file)) {
			cinema_video = new JSONObject();
			return;
		}
		
		var reader = new StreamReader(cinema_file);
		cinema_video = JSONNode.Parse(reader.ReadToEnd()).AsObject;
		reader.Close();
	}
	
	public void Save() {
		var writer = new StreamWriter(cinema_file);
		writer.Write(cinema_video.ToString());
		writer.Close();
		
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
