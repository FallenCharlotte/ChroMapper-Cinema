using System.IO;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;
using SimpleJSON;

using ChroMapper_PropEdit.Components;
using ChroMapper_PropEdit.Enums;
using ChroMapper_PropEdit.UserInterface;
using ChroMapper_PropEdit.Utils;

namespace ChroMapper_Cinema {

internal class OptionsWindow : UIWindow {
	public UIButton? toggle_visibility;
	
	public static OptionsWindow InitProxy(MapEditorUI mapEditorUI) {
		return UIWindow.Create<OptionsWindow>(mapEditorUI);
	}
	
	public override void Init(MapEditorUI mapEditorUI) {
		base.Init(mapEditorUI, "Cinema Settings");
		
		Debug.Log("Cinema Settings Window!");
		
		{
			toggle_visibility = UI.AddButton(window!.title!, Utils.LoadSprite("ChroMapper_Cinema.Resources.eye.png"), Plugin.controller!.ToggleEnabled);
			UI.AttachTransform(toggle_visibility.gameObject, pos: new Vector2(-25, -14), size: new Vector2(30, 30), anchor_min: new Vector2(1, 1), anchor_max: new Vector2(1, 1));
			var tooltip = toggle_visibility.gameObject.AddComponent<Tooltip>();
			tooltip.TooltipOverride = "Toggle Screen Visibility";
		}
		
		Refresh();
	}
	
	public void Refresh() {
		// TODO: Clear window when needed
		
		MakeTextbox("Video ID", "videoID", "The YouTube video ID from the part after the &v= in the URL");
		MakeTextbox("Video URL", "videoUrl", "Use this parameter instead of videoID if you want to use a video hoster other than YouTube.");
		{
			var line = MakeLine("");
			var download = UI.AddButton(line, "Download", () => {
				var url = (Plugin.map_config!.cinema_video.HasKey("videoUrl"))
					? (string)Plugin.map_config!["videoUrl"]
					: ("https://www.youtube.com/watch?v=" + (string)Plugin.map_config!["videoID"]);
				var filename = Plugin.map_config.cinema_video.HasKey("videoFile")
					? (string)Plugin.map_config["videoFile"]
					: null;
				VideoDownloader.DownloadVideo(url, Plugin.map_config!.video_dir!, filename);
			});
			UI.AttachTransform(download.gameObject, new Vector2(0, 0), new Vector2(0, 0), new Vector2(0.5f, 0), new Vector2(1, 1));
		}
		MakeTextbox("Title", "title", "The title of the video. Will be shown to the user.");
		MakeTextbox("Author", "author", "The name of the video's uploader. Will be shown to the user.");
		MakeTextbox("Video File", "videoFile", "Name of the video file on the local file system.");
		MakeParsed<int>("Duration", "duration", "Video duration in seconds. Will be shown to the user, but has no other function than that.");
		MakeParsed<int>("Offset", "offset", "Video duration in seconds. The offset in milliseconds to align the video with the map.");
		MakeCheckbox("Config By Mapper", "configByMapper", false, "Whether the config was created by the mapper.");
		
		MakeLine("");
		
		AddExpando("cinemaAdvanced", "Advanced Settings", false); {
			// TODO: Dropdown
			MakeTextbox("Environment", "environmentName", "Force a specific environment that is only used if the user has Cinema installed and the video downloaded.");
			MakeParsed<float>("Playback Speed", "playbackSpeed", "Adjust the playback speed of the video.");
			MakeCheckbox("Loop", "loop", false, "Whether the video should loop if it ends before the map does.");
			MakeParsed<float>("End Video At", "endVideoAt", "Video time, in seconds, to end at. The video will fade out for one second prior.");
			MakeVector3("Screen Position", "screenPosition", "Custom screen position. This setting prevents the user from overriding the environment.");
			MakeVector3("Screen Rotation", "screenRotation", "Rotates the screen.");
			MakeParsed<float>("Screen Height", "screenHeight", "Determines the size of the screen. There is no setting for the width, since that is calculated automatically by the height and the aspect ratio of the video.");
			MakeParsed<float>("Screen Curvature", "screenCurvature", "Force a specific curvature of the screen in degrees. (0-180)");
			MakeParsed<int>("Screen Subsurfaces", "screenSubsurfaces", "Sets how many sub-surfaces the curved screen uses, changing the smoothness of the curvature. (1-256)");
			MakeDropdown("Allow Custom Platform", "allowCustomPlatform", MapSettings.OptionBool, true, "When set to false, will prevent the CustomPlatforms mod from loading a custom platform for this map if the video is playing.");
			MakeCheckbox("Disable Default Modifications", "disableDefaultModifications", false, "If set to true, will disable any environment modifications Cinema does by default for the selected environment.");
			MakeCheckbox("Force Environment Modifications", "forceEnvironmentModifications", false, "Set this to true to have your environment modifications applied even if no video is defined or downloaded by the user.");
			MakeCheckbox("Merge Prop Groups", "mergePropGroups", false, "If this is set to true, all cloned lights will be merged with existing prop groups, based on the specified z-position.");
			MakeDropdown("Transparency", "transparency", MapSettings.OptionBool, true, "Override the user's choice and force transparency to be enabled or disabled.");
			MakeParsed<float>("Bloom", "bloom", "Sets the amount of bloom (glow) that appears around the video player during brightly colored parts of the video.");
		} panels.Pop();
		
		AddExpando("cinemaColorCorrect", "Color Correction", false); {
			MakeParsed<float>("Brightness", "colorCorrection.brightness", "Valid range: 0-2");
			MakeParsed<float>("Contrast", "colorCorrection.contrast", "Valid range: 0-5");
			MakeParsed<float>("Saturation", "colorCorrection.saturation", "Valid range: 0-5");
			MakeParsed<float>("Exposure", "colorCorrection.exposure", "Valid range: 0-5");
			MakeParsed<float>("Gamma", "colorCorrection.gamma", "Valid range: 0-5");
			MakeParsed<float>("Hue", "colorCorrection.hue", "Valid range: -360 to +360 (in degrees)");
		} panels.Pop();
		
		AddExpando("cinemaVignette", "Vignette", false); {
			MakeDropdown("Type", "vignette.type", VignetteTypes, true, "Changes how the radius and softness parameters behave.");
			MakeParsed<float>("Radius", "vignette.radius", "Valid range: 0 to 1.\nIf the type is \"elliptical\", the screen is only really elliptical if the radius is set to 0. Values above that simply round the corners of the screen to varying degrees.");
			MakeParsed<float>("Softness", "vignette.softness", "Valid range: 0 to 1. Defines the sharpness of the cutout.");
		}
		
		// TODO: Additional Screens
		
		// TODO: Environment
	}
	
	private GameObject MakeLine(string name, Vector2? size = null, string tooltip = "") {
		return UI.AddField(current_panel!, name, size, tooltip);
	}
	
	private Toggle MakeCheckbox(string label, string key, bool _default, string tooltip = "") {
		var line = MakeLine(label, null, tooltip);
		var value = (Data.GetNode(Plugin.map_config!.cinema_video, key) is JSONNode n)
			? Data.CreateConvertFunc<JSONNode, bool>()(n)
			: _default;
		
		UnityAction<bool> setter = (v) => {
			if (v == _default) {
				Data.SetNode(Plugin.map_config.cinema_video, key, null);
			}
			else {
				Data.SetNode(Plugin.map_config.cinema_video, key, v);
			}
			Plugin.map_config.Save();
		};
		
		return UI.AddCheckbox(line, value, setter);
	}
	
	private UIDropdown MakeDropdown<T>(string label, string key, Map<T?> type, bool nullable = false, string tooltip = "") {
		var line = MakeLine(label, null, tooltip);
		
		var value = (Data.GetNode(Plugin.map_config!.cinema_video, key) is JSONNode n)
			? Data.CreateConvertFunc<JSONNode, T>()(n)
			: default!;
		
		UnityAction<T?> setter = Setter<T?>(key);
		
		return UI.AddDropdown(line, value, setter, type, nullable);
	}
	
	private Textbox MakeTextbox(string label, string key, string tooltip = "") {
		var line = MakeLine(label, null, tooltip);
		var input = Textbox.Create(line, false);
		UI.AttachTransform(input.gameObject, new Vector2(0, 0), new Vector2(0, 0), new Vector2(0.5f, 0), new Vector2(1, 1));
		
		var value = (string)Plugin.map_config![key];
		Textbox.Setter setter = (string? v) => {
			Plugin.map_config[key] = v;
		};
		
		return input.Set(value, false, setter);
	}
	
	private Textbox MakeParsed<T>(string label, string key, string tooltip = "") where T : struct {
		var line = MakeLine(label, null, tooltip);
		var input = Textbox.Create(line, false);
		UI.AttachTransform(input.gameObject, new Vector2(0, 0), new Vector2(0, 0), new Vector2(0.5f, 0), new Vector2(1, 1));
		
		var value = (Data.GetNode(Plugin.map_config!.cinema_video, key) is JSONNode n)
			? Data.CreateConvertFunc<JSONNode, T>()(n)
			: (T?)null;
		UnityAction<T?> setter = Setter<T?>(key);
		
		return UI.UpdateParsed<T>(input, value, false, setter);
	}
	
	private UnityAction<T?> Setter<T>(string key) {
		return (T? v) => {
			if (v is T value) {
				Data.SetNode(Plugin.map_config!.cinema_video, key, Data.CreateConvertFunc<T, SimpleJSON.JSONNode>()(value));
			}
			else {
				Data.RemoveNode(Plugin.map_config!.cinema_video, key);
			}
			Plugin.map_config.Save();
		};
	}
	
	private void MakeVector3(string name, string key, string tooltip = "") {
		panels.Push(UI.AddChild(current_panel!, name).AddComponent<Collapsible>().Init(name, false, tooltip, false).panel!);
		
		current_panel!.GetComponent<VerticalLayoutGroup>().padding = new RectOffset(5, 5, 0, 5);
		
		MakeParsed<float>("X", $"{key}.x");
		MakeParsed<float>("Y", $"{key}.y");
		MakeParsed<float>("Z", $"{key}.z");
		
		panels.Pop();
	}
	
	public override void ToggleWindow() {
		if (window!.gameObject.activeSelf) {
			Refresh();
		}
		window!.Toggle();
	}
	
	public static readonly Map<string?> VignetteTypes = new Map<string?> {
		{ "rectangular", "Rectangular" },
		{ "elliptical", "Elliptical" },
	};
};

}
