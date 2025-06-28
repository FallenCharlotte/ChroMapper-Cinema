using UnityEngine;
using UnityEngine.SceneManagement;

namespace ChroMapper_Cinema {

[Plugin("Cinema")]
public class Plugin {
	public static Cinema controller;
	public static MapConfig map_config;
	public static ExtensionButton main_button = null;
	public static bool enableUI = false;
	
	[Init]
	private void Init() {
		controller = new Cinema();
		map_config = new MapConfig();
		
		try {
			var assembly = System.Reflection.Assembly.Load("ChroMapper-PropEdit");
			Debug.Log("PropEdit found, enabling settings window");
			enableUI = true;
		}
		catch (System.Exception) { }
		
		main_button = ExtensionButtons.AddButton(
			Utils.LoadSprite("ChroMapper_Cinema.Resources.Icon.png"),
			"Cinema",
			controller.ButtonPress);
		
		LoadInitialMap.PlatformLoadedEvent += PlatformLoaded;
		SceneManager.sceneLoaded += SceneLoaded;
		
		Debug.Log("Cinema Plugin has loaded!");
	}
	
	private void PlatformLoaded(PlatformDescriptor descriptor) {
		var atsc = Object.FindObjectOfType<AudioTimeSyncController>();
		controller.Init(atsc, descriptor.gameObject);
	}
	
	private void SceneLoaded(Scene scene, LoadSceneMode mode) {
		if (scene.buildIndex == 3) {
			map_config.Load();
			
			if (enableUI) {
				var mapEditorUI = Object.FindObjectOfType<MapEditorUI>();
				controller.MakeWindow(mapEditorUI);
			}
		}
	}
	
	[Exit]
	private void Exit() {
		
	}
}

}
