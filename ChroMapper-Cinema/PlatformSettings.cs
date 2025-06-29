using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using SimpleJSON;

namespace ChroMapper_Cinema {

struct PlatformSettings {
	public static readonly Vector3 DEFAULT_POS = new Vector3(0, 12.4f, 67.8f);
	public const float DEFAULT_HEIGHT = 25.0f;
	public static readonly Vector3 DEFAULT_ROT = new Vector3(-8, 0, 0);
	
	public Vector3 pos;
	public Vector3 rotation;
	public float height;
	public float curvature;
	
	public static PlatformSettings GetPlatformSettings(string platform) {
		// From https://github.com/qqrz997/BeatSaberCinema/blob/master/BeatSaberCinema/Screen/Placement.cs#L81
		return platform switch {
			"LinkinParkEnvironment" => new PlatformSettings(new Vector3(0f, 6.2f, 52.7f), Vector3.zero, 16f, 0f),
			"BTSEnvironment" => new PlatformSettings(new Vector3(0, 12.4f, 80f), new Vector3(-7, 0, 0), 25f),
			"OriginsEnvironment" => new PlatformSettings(new Vector3(0, 12.4f, 66.7f), new Vector3(-7, 0, 0), 25f),
			"KaleidoscopeEnvironment" => new PlatformSettings(new Vector3(0f, -0.5f, 35f), Vector3.zero, 12f),
			"InterscopeEnvironment" => new PlatformSettings(new Vector3(0f, 6.3f, 37f), Vector3.zero, 12.5f),
			"CrabRaveEnvironment" => new PlatformSettings(new Vector3(0f, 5.46f, 40f), new Vector3(-5f, 0f, 0f), 13f),
			"MonstercatEnvironment" => new PlatformSettings(new Vector3(0f, 5.46f, 40f), new Vector3(-5f, 0f, 0f), 13f),
			"SkrillexEnvironment" => new PlatformSettings(new Vector3(0f, 1.5f, 40f), Vector3.zero, 12f),
			"WeaveEnvironment" => new PlatformSettings(new Vector3(0f, 1.5f, 21f), Vector3.zero, 4.3f, 0f),
			"PyroEnvironment" => new PlatformSettings(new Vector3(0f, 12f, 60f), Vector3.zero, 24f, 0f),
			"EDMEnvironment" => new PlatformSettings(new Vector3(0f, 1.5f, 25f), Vector3.zero, 8f),
			"LizzoEnvironment" => new PlatformSettings(new Vector3(0f, 8f, 63f), Vector3.zero, 16f),
			"Dragons2Environment" => new PlatformSettings(new Vector3(0f, 5.8f, 67f), Vector3.zero, 33f),
			_ => new PlatformSettings(DEFAULT_POS, DEFAULT_ROT, DEFAULT_HEIGHT)
		};
	}
	
	private PlatformSettings(Vector3 p, Vector3 r, float h, float c = 0) {
		pos = p;
		rotation = r;
		height = h;
		curvature = c;
	}
};

}
