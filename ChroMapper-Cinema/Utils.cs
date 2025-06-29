using System.IO;
using System.Reflection;
using UnityEngine;
using UnityEngine.Video;
using SimpleJSON;

namespace ChroMapper_Cinema {

class Utils {
	public static Sprite LoadSprite(string asset) {
		Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(asset);
		byte[] data = new byte[stream.Length];
		stream.Read(data, 0, (int)stream.Length);
		
		Texture2D texture2D = new Texture2D(256, 256);
		texture2D.LoadImage(data);
		
		return Sprite.Create(texture2D, new Rect(0, 0, texture2D.width, texture2D.height), new Vector2(0, 0), 100.0f);
	}
	
	public static Transform SetTransform(GameObject obj, Vector3 pos, Vector3 scale, Vector3? rotation = null)
	{
		var trans = obj.GetComponent<Transform>();
		if (trans == null) {
			trans = obj.AddComponent<Transform>();
		}
		trans.localPosition = pos;
		trans.localScale = scale;
		if (rotation != null) {
			trans.localEulerAngles = (Vector3)rotation;
		}
		
		return trans;
	}
	
	public static string Error(string msg) {
		Debug.Log(msg);
		return msg;
	}
}

}
