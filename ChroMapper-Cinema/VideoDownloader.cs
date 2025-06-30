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
	
	public void DownloadYTDLP() {
		if (!Directory.Exists(TOOLS_DIR)) {
			Directory.CreateDirectory(TOOLS_DIR);
		}
		var client = new System.Net.WebClient();
		var url = "https://github.com/yt-dlp/yt-dlp/releases/latest/download/" + PlatformFilename();
		Debug.Log($"Download yt-dlp: {url}");
		client.DownloadFile(url, local_path);
		ytdlp_path = local_path;
	}
	
	public static void DownloadVideo(string url, string folder, string? filename = null) {
		var downloader = new VideoDownloader();
		
		if (downloader.ytdlp_path == null) {
			downloader.DownloadYTDLP();
		}
		if (downloader.ytdlp_path == null) {
			Debug.LogError("Could not download yt-dlp");
			return;
		}
		
		var out_arg = (filename == null)
			? "-o \"%(title)s.%(ext)s\""
			: $"-o \"{filename}\"";
		
		var args = $"-P \"{folder}\" {out_arg} \"{url}\"";
		
		if (Application.platform == RuntimePlatform.LinuxEditor || Application.platform == RuntimePlatform.LinuxPlayer) {
			args += "--merge-output-format mkv --recode-video webm --postprocessor-args 'VideoConvertor:-vcodec vp8 -acodec libvorbis'";
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
			Debug.Log($"[yt-dlp] {outLine.Data}");
			if (outLine.Data.StartsWith(DEST_PREFIX) && filename == null) {
				var info = new FileInfo(outLine.Data.Remove(0, DEST_PREFIX.Length));
				Plugin.map_config!["videoFile"] = info.Name;
				(Plugin.controller!.options_window as OptionsWindow)!.Refresh();
			}
			if (outLine.Data.StartsWith(MERGE_PREFIX) && filename == null) {
				var info = new FileInfo(outLine.Data.Remove(0, MERGE_PREFIX.Length));
				Plugin.map_config!["videoFile"] = info.Name;
				(Plugin.controller!.options_window as OptionsWindow)!.Refresh();
			}
		};
		dl.ErrorDataReceived += (object _, System.Diagnostics.DataReceivedEventArgs outLine) => {
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
	
	private static readonly string DEST_PREFIX = "[download] Destination: ";
	private static readonly string MERGE_PREFIX = "[Merger] Merging formats into ";
};

}
