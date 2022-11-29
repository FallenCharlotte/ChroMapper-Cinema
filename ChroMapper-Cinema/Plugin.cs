using UnityEngine;
using UnityEngine.SceneManagement;

using ChroMapper_Cinema.UserInterface;

namespace ChroMapper_Cinema {

[Plugin("Cinema")]
public class Plugin {
	public static UI ui;
	
	[Init]
	private void Init() {
		Debug.Log("Cinema Plugin has loaded!");
		SceneManager.sceneLoaded += SceneLoaded;
		ui = new UI();
	}
	
	private void SceneLoaded(Scene scene, LoadSceneMode mode) {
		if (scene.buildIndex == 3) {
			// Map Edit
			var parent = GameObject.Find("Editor/Rotating");
			ui.AddWindow(parent);
		}
	}
	
	[Exit]
	private void Exit() {
		
	}
}

}
