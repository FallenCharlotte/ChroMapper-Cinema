using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;

using UnityEngine;

namespace ChroMapper_Cinema {

public class VideoDownloader {
	public string? ytdlp_path;
	public static readonly string TOOLS_DIR = "Plugins/Tools";
	public static readonly string BS_LIBS_DIR = "Libs";
	
	public static string PlatformFilename() {
		return Application.platform switch {
			RuntimePlatform.LinuxEditor => "yt-dlp_linux",
			RuntimePlatform.LinuxPlayer => "yt-dlp_linux",
			RuntimePlatform.OSXEditor => "yt-dlp_macos",
			RuntimePlatform.OSXPlayer => "yt-dlp_macos",
			RuntimePlatform.WindowsEditor => "yt-dlp.exe",
			RuntimePlatform.WindowsPlayer => "yt-dlp.exe",
			_ => throw new System.Exception("Unsupported platform!"),
		};
	}
	
	private VideoDownloader() {
		var bs_path = Path.Combine(Settings.Instance.BeatSaberInstallation, BS_LIBS_DIR, PlatformFilename());
		var local_path = Path.Combine(Directory.GetCurrentDirectory(), TOOLS_DIR, PlatformFilename());
		
		// Check local folder
		if (File.Exists(local_path)) try {
			Run(local_path, "--version", (line) => {
				Plugin.Trace($"Using local {PlatformFilename()}: {line}");
			});
			ytdlp_path = local_path;
			return;
		} catch(System.Exception) { }
		
		// Check Beat Saber folder
		if (File.Exists(bs_path)) try {
			Run(bs_path, "--version", (line) => {
				Plugin.Trace("Using Beat Saber {PlatformFilename()}: {line}");
			});
			ytdlp_path = bs_path;
			return;
		} catch(System.Exception) { }
		
		// Check if it's in PATH
		try {
			Run("yt-dlp", "--version", (line) => {
				Plugin.Trace($"Using system yt-dlp: {line}");
			});
			ytdlp_path = "yt-dlp";
			return;
		} catch(System.Exception) { }
	}
	
	public static void DownloadVideo(string url, string folder, string? filename = null) {
		var downloader = new VideoDownloader();
		
		if (downloader.ytdlp_path == null) {
			PersistentUI.Instance.ShowDialogBox("Could not find yt-dlp!", null, PersistentUI.DialogBoxPresetType.Ok);
			return;
		}
		
		var out_arg = String.IsNullOrWhiteSpace(filename)
			? "-o \"%(title)s.%(ext)s\""
			: $"-o \"{filename}\"";
		
		if (String.IsNullOrWhiteSpace(filename)) {
			Plugin.Trace($"Getting filename: \"{downloader.ytdlp_path!}\" {out_arg} -O \"%(title)s\" \"{url}\"");
			Run(downloader.ytdlp_path!, $"{out_arg} -O \"%(title)s\" \"{url}\"", (line) => {
				Plugin.map_config!["videoFile"] = line + ".mp4";
				Plugin.Trace($"videoFile: {line}.mp4");
				Plugin.controller!.RefreshWindow();
			});
		};
		
		var args = $"-P \"{folder}\" -f bestvideo[vcodec*=avc1]+bestaudio[acodec*=mp4] --no-cache-dir --recode-video mp4 --no-part --no-mtime {out_arg} \"{url}\"";
		
		Plugin.Trace($"Downloading: \"{downloader.ytdlp_path!}\" {args}");
		
		var download_label = (Plugin.controller!.options_window as OptionsWindow)!.download_label!;
		
		Run(
			downloader.ytdlp_path!,
			args,
			(line) => {
				if (line.Contains("[download]")) {
					download_label.text = "Downloading... " + Regex.Match(line, @"[\d\.]+%");
				}
			},
			(line) => {
				Debug.LogError($"[yt-dlp] {line}");
			},
			(proc) => {
				if (proc.ExitCode == 0) {
					Plugin.Trace($"Download successful!");
					download_label.text = "Done!";
					Plugin.map_config!.TryVideo();
					Plugin.controller!.AutoLoad();
				}
				else {
					download_label.text = "Download failed!";
					Debug.LogError("Download failed!");
				}
			}
		);
		
		download_label.text = "Downloading...";
	}
	
	private static void Run(string cmd, string args, Action<string>? outputCallback = null, Action<string>? errorCallback = null, Action<System.Diagnostics.Process>? exitCallback = null) {
		var p = new System.Diagnostics.Process();
		p.StartInfo.FileName = cmd;
		p.StartInfo.Arguments = args;
		p.StartInfo.UseShellExecute = (outputCallback == null && errorCallback == null);
		p.StartInfo.RedirectStandardOutput = (outputCallback != null);
		p.StartInfo.RedirectStandardError = (errorCallback != null);
		p.EnableRaisingEvents = (exitCallback != null);
		if (outputCallback != null) p.OutputDataReceived += (_, line) => {
			if (String.IsNullOrWhiteSpace(line.Data)) return;
			Plugin.controller!.mtr?.Run(() => outputCallback(line.Data));
		};
		if (errorCallback != null) p.ErrorDataReceived += (_, line) => {
			if (String.IsNullOrWhiteSpace(line.Data)) return;
			Plugin.controller!.mtr?.Run(() => errorCallback(line.Data));
		};
		if (exitCallback != null) p.Exited += (sender, _) => {
			Plugin.controller!.mtr?.Run(() => exitCallback((System.Diagnostics.Process)sender));
		};
		p.Start();
		if (outputCallback != null) p.BeginOutputReadLine();
		if (errorCallback != null) p.BeginErrorReadLine();
	}
};

}
