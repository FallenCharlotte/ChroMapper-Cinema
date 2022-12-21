using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using SimpleJSON;

namespace ChroMapper_Cinema {

struct PlatformSettings {
	public static readonly Vector3 DEFAULT_POS = new Vector3(0, 12.4f, 67.8f);
	public const float DEFAULT_HEIGHT = 25.0f;
	public static readonly Vector3 DEFAULT_ROT = new Vector3(-8, 0, 0);
	
	public Vector3 pos;
	public Vector3 rotation;
	public float height;
	
	public PlatformSettings(Vector3? p = null, Vector3? r = null, float h = DEFAULT_HEIGHT) {
		pos = p ?? DEFAULT_POS;
		rotation = r ?? DEFAULT_ROT;
		height = h;
	}
}

class Options {
	public static readonly string settings_file = UnityEngine.Application.persistentDataPath + "/Cinema.json";
	public static readonly string default_key = "_default";
	
	public static Options instance {
		get {
			if (_instance == null) {
				_instance = new Options();
			}
			return _instance;
		}
	}
	private static Options _instance;
	
	public PlatformSettings GetPlatformSettings(string platform) {
		var key = (plat_settings.ContainsKey(platform))
			? platform
			: default_key;
		
		Debug.Log($"Using Cinema platform settings '{key}'");
		
		return plat_settings[key];
	}
	
	public void LoadSettings() {
		plat_settings = new Dictionary<string, PlatformSettings>();
		
		if (File.Exists(settings_file)) {
			Debug.Log("Reading Cinema settings file");
			 using (var reader = new StreamReader(settings_file)) {
				var o = JSON.Parse(reader.ReadToEnd());
				foreach (var platform in o.AsObject["platforms"]) {
					var pos = platform.Value["position"].AsObject;
					var rot = platform.Value["rotation"].AsObject;
					var height = platform.Value["height"].AsFloat;
					plat_settings.Add(platform.Key, new PlatformSettings(Utils.JSONV3(pos), Utils.JSONV3(rot), height));
				}
			}
		}
		
		if (!plat_settings.ContainsKey(default_key)) {
			plat_settings.Add(default_key, new PlatformSettings(null, null));
		}
	}
	
	public void SaveSettings() {
		JSONObject root = new JSONObject();
		JSONObject platforms = new JSONObject();
		foreach (var platform in plat_settings) {
			var plat = new JSONObject();
			var p = Utils.V3JSON(platform.Value.pos);
			p.Inline = true;
			plat.Add("position", p);
			var s = Utils.V3JSON(platform.Value.rotation);
			s.Inline = true;
			plat.Add("rotation", s);
			plat.Add("height", platform.Value.height);
			platforms.Add(platform.Key, plat);
		}
		root.Add("platforms", platforms);
		File.WriteAllText(settings_file, root.ToString(4));
	}
	
	private Options() {
		LoadSettings();
		SaveSettings();
	}
	
	private Dictionary<string, PlatformSettings> plat_settings;
};

}
