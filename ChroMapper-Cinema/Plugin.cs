using System.Linq;

using UnityEngine;
using UnityEngine.SceneManagement;

namespace ChroMapper_Cinema {

[Plugin("Cinema")]
public class Plugin {
	public static CinemaController? controller;
	public static MapConfig? map_config;
	public static ExtensionButton? main_button = null;
	public static bool enableUI = false;
	
	[Init]
	private void Init() {
		controller = new CinemaController();
		map_config = new MapConfig();
		
		try {
			var assembly = System.Reflection.Assembly.Load("ChroMapper-PropEdit");
			Debug.Log("PropEdit found, enabling settings window");
			enableUI = true;
		}
		catch (System.Exception) { }
		
#if CHROMPER_13
		LoadInitialMap.PlatformLoadedEvent += (p) => PlatformLoaded(p.gameObject);
#else
		SceneManager.sceneLoaded += SceneLoaded;
#endif
		
		main_button = ExtensionButtons.AddButton(
			Utils.LoadSprite("ChroMapper_Cinema.Resources.Icon.png"),
			"Cinema",
			controller.ButtonPress);
		
		Debug.Log("Cinema Plugin has loaded!");
	}
	
#if !CHROMPER_13
	private void SceneLoaded(Scene scene, LoadSceneMode mode) {
		if (scene.buildIndex == 3) {
			var context = Resources.FindObjectsOfTypeAll<BeatmapRuntimeContext>().FirstOrDefault();
			
			context.OnEnvironmentLoaded += (d) => PlatformLoaded(d.gameObject);
		}
	}
#endif
	
	private void PlatformLoaded(GameObject platform) {
		controller!.Init(platform);
	}
	
	[Exit]
	private void Exit() {
		
	}
	
	// For extra debug logging that shouldn't be included in releases
	public static void Trace(object message) {
//#if EXTRA_LOGGING
		var st = new System.Diagnostics.StackTrace(true);
		var caller = st.GetFrame(1);
		Debug.Log($"{caller.GetFileName()}:{caller.GetFileLineNumber()} {message}");
//#endif
	}
}

}
