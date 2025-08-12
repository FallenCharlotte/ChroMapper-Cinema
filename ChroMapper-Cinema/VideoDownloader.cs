using System;
using System.Collections.Generic;
using System.IO;

using UnityEngine;

namespace ChroMapper_Cinema {

public class VideoDownloader {
	public string? ytdlp_path;
	public string local_path;
	public static readonly string TOOLS_DIR = "Plugins/Tools";
	
	public static string PlatformFilename() {
		return Application.platform switch {
			RuntimePlatform.LinuxEditor => "yt-dlp",
			RuntimePlatform.LinuxPlayer => "yt-dlp",
			RuntimePlatform.OSXEditor => "yt-dlp_macos",
			RuntimePlatform.OSXPlayer => "yt-dlp_macos",
			RuntimePlatform.WindowsEditor => "yt-dlp.exe",
			RuntimePlatform.WindowsPlayer => "yt-dlp.exe",
			_ => throw new System.Exception("Unsupported platform!"),
		};
	}
	
	private VideoDownloader() {
		local_path = Path.Combine(TOOLS_DIR, PlatformFilename());
		
		// Check if it's in PATH
		try {
			System.Diagnostics.Process.Start("yt-dlp",  "--version");
			ytdlp_path = "yt-dlp";
			Debug.Log("Using system yt-dlp");
			return;
		}
		catch(System.Exception) { }
		
		// Check local folder
		if (File.Exists(local_path)) {
			ytdlp_path = local_path;
			Debug.Log("Using local " + PlatformFilename());
		}
	}
	
	public static void DownloadVideo(string url, string folder, string? filename = null) {
		var downloader = new VideoDownloader();
		
		if (downloader.ytdlp_path == null) {
			PersistentUI.Instance.ShowDialogBox("Could not find yt-dlp!", null, PersistentUI.DialogBoxPresetType.Ok);
			return;
		}
		
		var out_arg = (filename == null)
			? "-o \"%(title)s.%(ext)s\""
			: $"-o \"{filename}\"";
		
		var args = $"-P \"{folder}\" -O \"%(title)s\" --no-simulate {out_arg} \"{url}\"";
		
		if (Application.platform == RuntimePlatform.LinuxEditor || Application.platform == RuntimePlatform.LinuxPlayer) {
			args += " --merge-output-format mkv --recode-video webm --postprocessor-args 'VideoConvertor:-vcodec vp8 -acodec libvorbis'";
		}
		else {
			args += " --recode-video mp4";
		}
		
		Debug.Log($"{downloader.ytdlp_path!} {args}");
		
		var dl = new System.Diagnostics.Process();
		dl.StartInfo.FileName = downloader.ytdlp_path!;
		dl.StartInfo.Arguments = args;
		dl.StartInfo.UseShellExecute = false;
		dl.StartInfo.RedirectStandardOutput = true;
		dl.StartInfo.RedirectStandardError = true;
		dl.StartInfo.EnvironmentVariables["TMP"] = (new DirectoryInfo(TOOLS_DIR)).FullName;
		dl.OutputDataReceived += (object _, System.Diagnostics.DataReceivedEventArgs outLine) => {
			if (outLine.Data == "") return;
			if (filename == null) {
				Plugin.map_config!["videoFile"] = outLine.Data + ".mp4";
				(Plugin.controller!.options_window as OptionsWindow)!.Refresh();
			}
		};
		dl.ErrorDataReceived += (object _, System.Diagnostics.DataReceivedEventArgs outLine) => {
			if (outLine.Data == "") return;
			Debug.LogError($"[yt-dlp] {outLine.Data}");
		};
		dl.Exited += (object _, System.EventArgs _) => {
			Debug.Log($"yt-dlp returned {dl.ExitCode}");
			Plugin.map_config!.TryVideo();
			Plugin.controller!.LoadVideo();
		};
		dl.EnableRaisingEvents = true;
		dl.Start();
		dl.BeginOutputReadLine();
		dl.BeginErrorReadLine();
	}
};

}
