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
	}
	
	private void SceneLoaded(Scene scene, LoadSceneMode mode) {
		if (scene.buildIndex == 3) {
			// Map Edit
			var parent = GameObject.Find("Editor/Rotating");
			var atsc = BeatmapObjectContainerCollection.GetCollectionForType(BeatmapObject.ObjectType.Note).AudioTimeSyncController;
			
			controller.Init(parent, atsc);
		}
	}
	
	[Exit]
	private void Exit() {
		
	}
}

}
