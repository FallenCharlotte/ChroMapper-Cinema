using UnityEngine;
using UnityEngine.SceneManagement;

namespace ChroMapper_Cinema {

[Plugin("Cinema")]
public class Plugin {
	public static Cinema controller;
	
	[Init]
	private void Init() {
		Debug.Log("Cinema Plugin has loaded!");
		SceneManager.sceneLoaded += SceneLoaded;
		controller = new Cinema();
		
		LoadInitialMap.PlatformLoadedEvent += PlatformLoaded;
	}
	
	private void SceneLoaded(Scene scene, LoadSceneMode mode) {
		if (scene.buildIndex == 3) {
			// Map Edit
			var atsc = BeatmapObjectContainerCollection.GetCollectionForType(BeatmapObject.ObjectType.Note).AudioTimeSyncController;
			
			controller.Init(atsc);
		}
	}
	
	private void PlatformLoaded(PlatformDescriptor descriptor) {
		controller.UpatePlatform(descriptor.gameObject);
	}
	
	[Exit]
	private void Exit() {
		
	}
}

}
