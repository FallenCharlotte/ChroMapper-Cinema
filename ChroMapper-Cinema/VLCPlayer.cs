// https://code.videolan.org/videolan/vlc-unity/-/blob/master/Assets/VLCUnity/Demos/Scripts/VLCPlayerExample.cs

using UnityEngine;
using LibVLCSharp;

public class VLCPlayer : MonoBehaviour {
	public static LibVLC? libvlc;
	public MediaPlayer mediaPlayer;
	
	public Renderer? screen;
	
	Texture2D? _vlcTexture = null;
	public RenderTexture? texture = null;
	
	public string Path {
		get { return mediaPlayer.Media?.Mrl ?? ""; }
		set { mediaPlayer.Media = new Media(value); }
	}
	
	public float Time {
		get { return mediaPlayer.Time / 1000.0f; }
		set { mediaPlayer.SetTime((long)(value * 1000)); }
	}
	
	public void Play() {
		mediaPlayer.Play();
	}
	
	public void Pause() {
		mediaPlayer.Pause();
	}
	
	public VLCPlayer() {
		if (libvlc == null) {
			Core.Initialize();
			
			libvlc = new LibVLC();
			
			libvlc.Log += (s, e) => {
				try {
					Debug.Log(e.FormattedLog);
				}
				catch (System.Exception ex) {
					Debug.LogError("Error logging error! " + ex.ToString());
				}
			};
		}
		
		mediaPlayer = new MediaPlayer(libvlc);
	}
	
	private void Update() {
		if (screen == null) return;
		
		uint height = 0;
		uint width = 0;
		mediaPlayer.Size(0, ref width, ref height);
		
		if (_vlcTexture == null || _vlcTexture.width != width || _vlcTexture.height != height)
		{
			ResizeOutputTextures(width, height);
		}
		
		if (UpdateTexture())
		{
			//Copy the vlc texture into the output texture, automatically flipped over
			var flip = new Vector2(1, 1);
			Graphics.Blit(_vlcTexture, texture, flip, Vector2.zero); //If you wanted to do post processing outside of VLC you could use a shader here.
		}
	}
	
	private void ResizeOutputTextures(uint px, uint py) {
		if (px != 0 && py != 0)
		{
			DestroyTextures();
			
			var ptr = mediaPlayer.GetTexture(px, py, out bool updated);
			if (updated && ptr != System.IntPtr.Zero) {
				_vlcTexture = Texture2D.CreateExternalTexture((int) px, (int) py, TextureFormat.RGBA32, false, true, ptr);
			}
			
			if (_vlcTexture != null)
			{
				texture = new RenderTexture(_vlcTexture.width, _vlcTexture.height, 0, RenderTextureFormat.ARGB32);
				
				if (screen != null)
					screen.material.mainTexture = texture;
			}
		}
	}
	
	private void DestroyTextures() {
		if (texture != null) {
			if (RenderTexture.active == texture)
				RenderTexture.active = null;
			texture.Release();
			DestroyImmediate(texture);
			texture = null;
		}
		
		if (_vlcTexture != null)
		{
			DestroyImmediate(_vlcTexture);
			_vlcTexture = null;
		}
	}
	
	private bool UpdateTexture() {
		if (_vlcTexture == null) return false;
		
		var ptr = mediaPlayer.GetTexture((uint)_vlcTexture.width, (uint)_vlcTexture.height, out bool updated);
		
		if (updated && ptr != System.IntPtr.Zero)
		{
			_vlcTexture.UpdateExternalTexture(ptr);
			return true;
		}
		return false;
	}
}
