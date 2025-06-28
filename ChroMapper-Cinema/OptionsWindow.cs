using System.Linq;
using System.Linq.Expressions;
using UnityEngine;
using UnityEngine.Events;
using SimpleJSON;

using ChroMapper_PropEdit.Components;
using ChroMapper_PropEdit.UserInterface;
using ChroMapper_PropEdit.Utils;

namespace ChroMapper_Cinema {

internal class OptionsWindow : UIWindow {
	public static OptionsWindow InitProxy(MapEditorUI mapEditorUI) {
		return UIWindow.Create<OptionsWindow>(mapEditorUI);
	}
	
	public override void Init(MapEditorUI mapEditorUI) {
		base.Init(mapEditorUI, "Cinema Settings");
		
		Debug.Log("Cinema Settings Window!");
		
		MakeTextbox("Video ID", "videoID", "The YouTube video ID from the part after the &v= in the URL");
		MakeTextbox("Video URL", "videoUrl", "Use this parameter instead of videoID if you want to use a video hoster other than YouTube.");
		MakeTextbox("Title", "title", "The title of the video. Will be shown to the user.");
		MakeTextbox("Author", "author", "The name of the video's uploader. Will be shown to the user.");
		MakeTextbox("Video File", "videoFile", "Name of the video file on the local file system.");
		MakeParsed<int>("Duration", "duration", "Video duration in seconds. Will be shown to the user, but has no other function than that.");
		MakeParsed<int>("Offset", "offset", "Video duration in seconds. The offset in milliseconds to align the video with the map.");
	}
	
	private GameObject MakeLine(string name, Vector2? size = null, string tooltip = "") {
		return current_panel!.transform.Find(name)?.gameObject
			?? UI.AddField(current_panel!, name, size, tooltip);
	}
	
	public Textbox MakeTextbox(string label, string key, string tooltip = "") {
		var line = MakeLine(label, null, tooltip);
		var input = Textbox.Create(line, false);
		UI.AttachTransform(input.gameObject, new Vector2(0, 0), new Vector2(0, 0), new Vector2(0.5f, 0), new Vector2(1, 1));
		
		var value = (string)Plugin.map_config[key];
		Textbox.Setter setter = (string v) => {
			Plugin.map_config[key] = v;
		};
		
		return input.Set(value, false, setter);
	}
	public Textbox MakeParsed<T>(string label, string key, string tooltip = "") where T : struct {
		var line = MakeLine(label, null, tooltip);
		var input = Textbox.Create(line, false);
		UI.AttachTransform(input.gameObject, new Vector2(0, 0), new Vector2(0, 0), new Vector2(0.5f, 0), new Vector2(1, 1));
		
		var value = (Data.GetNode(Plugin.map_config.cinema_video, key) is JSONNode n)
			? Data.CreateConvertFunc<JSONNode, T>()(n)
			: (T?)null;
		UnityAction<T?> setter = (T? v) => {
			if (v is T value) {
				Data.SetNode(Plugin.map_config.cinema_video, key, Data.CreateConvertFunc<T, SimpleJSON.JSONNode>()(value));
			}
			else {
				Data.RemoveNode(Plugin.map_config.cinema_video, key);
			}
			Plugin.map_config.Save();
		};
		
		return UI.UpdateParsed<T>(input, value, false, setter);
	}
	
	public override void ToggleWindow() {
		//Refresh();
		window!.Toggle();
	}
};

}
