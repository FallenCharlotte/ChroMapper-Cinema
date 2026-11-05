# ChroMapper-Cinema
Cinema Viewer in ChroMapper

On Linux, converts mp4 to vp8 because Unity is bad :( This makes downloads very slow.

## Dependencies:

Downloading videos, particularly from youtube, requires a full yt-dlp+ffmpeg+deno setup. The easiest way to get this set up is with a package manager:

- **Windows:** If you have a beat saber installation with cinema set up, it should find and load the libraries from there. If not, use [winget](https://learn.microsoft.com/en-us/windows/package-manager/winget/): install command:
```cmd
winget install --source winget --id yt-dlp.yt-dlp
```

- **MacOS:** [Homebrew](https://brew.sh/): install command:
```sh
brew install yt-dlp
```

- **Linux:** You should know this

## Done:

- [x] Play/Pause/Seek
- [x] Playback speed
- [x] Toggle button
- [x] Add video / edit offset (better sync tool TODO)
- [x] Ffmpeg duct-tape for native linux playback

## Maybe TODO?

- [ ] Cinema's environment changes
