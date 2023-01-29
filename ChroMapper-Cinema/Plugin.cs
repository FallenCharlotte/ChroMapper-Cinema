using UnityEngine;
using UnityEngine.SceneManagement;

namespace ChroMapper_Cinema {

[Plugin("Cinema")]
public class Plugin {
	public static Cinema controller;
	
	[Init]
	private void Init() {
		Debug.Log("Cinema Plugin has loaded!");
		controller = new Cinema();
		
		LoadInitialMap.PlatformLoadedEvent += PlatformLoaded;
	}
	
	private void PlatformLoaded(PlatformDescriptor descriptor) {
		var atsc = Object.FindObjectOfType<AudioTimeSyncController>();
		controller.Init(atsc, descriptor.gameObject);
	}
	
	[Exit]
	private void Exit() {
		
	}
}

}
