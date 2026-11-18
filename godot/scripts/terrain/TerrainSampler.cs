using System;

namespace TransportSandbox.Terrain;

/// <summary>Generator v1. Immutable settings; all sampling is double precision and worker-safe.</summary>
public sealed class TerrainSampler
{
	public readonly uint Seed;
	public readonly double MountainHeight, HorizontalScale, SeaLevel;

	public TerrainSampler(string seed, double mountainHeight, double horizontalScale, double seaLevel)
	{
		uint hash = 2166136261;
		foreach (char code in seed)
			hash = unchecked((hash ^ code) * 16777619);
		Seed = hash;
		MountainHeight = mountainHeight;
		HorizontalScale = Math.Max(0.1, horizontalScale);
		SeaLevel = seaLevel;
	}

	public uint Hash(int x, int z, int salt = 0)
	{
		uint h = Seed ^ unchecked((uint)x * 374761393) ^ unchecked((uint)z * 668265263)
			^ unchecked((uint)salt * 1274126177);
		h = unchecked((h ^ (h >> 13)) * 1274126177);
		return h ^ (h >> 16);
	}

	private double Gradient(int x, int z, double px, double pz, int salt) => (Hash(x, z, salt) & 7) switch
	{
		0 => px,
		1 => -px,
		2 => pz,
		3 => -pz,
		4 => (px + pz) * Math.Sqrt(0.5),
		5 => (px - pz) * Math.Sqrt(0.5),
		6 => (-px + pz) * Math.Sqrt(0.5),
		_ => (-px - pz) * Math.Sqrt(0.5)
	};

	public static double Mix(double a, double b, double t) => a + (b - a) * t;
	public static double Smooth(double a, double b, double x)
	{
		double t = Math.Clamp((x - a) / (b - a), 0, 1);
		return t * t * (3 - 2 * t);
	}

	public double Noise(double x, double z, int salt = 0)
	{
		int ix = (int)Math.Floor(x), iz = (int)Math.Floor(z);
		double dx = x - ix, dz = z - iz;
		double fx = dx * dx * dx * (dx * (dx * 6 - 15) + 10);
		double fz = dz * dz * dz * (dz * (dz * 6 - 15) + 10);
		return Mix(Mix(Gradient(ix, iz, dx, dz, salt), Gradient(ix + 1, iz, dx - 1, dz, salt), fx),
			Mix(Gradient(ix, iz + 1, dx, dz - 1, salt), Gradient(ix + 1, iz + 1, dx - 1, dz - 1, salt), fx), fz) * 1.65;
	}

	public double Fbm(double x, double z, int salt, int octaves = 4)
	{
		double result = 0, amplitude = 1, total = 0;
		for (int i = 0; i < octaves; i++)
		{
			result += Noise(x, z, salt + i * 101) * amplitude;
			total += amplitude;
			double nextX = (x * 0.8 - z * 0.6) * 2.03 + 17.1;
			z = (x * 0.6 + z * 0.8) * 2.03 - 9.2;
			x = nextX;
			amplitude *= 0.49;
		}
		return result / total;
	}

	public double Height(double worldX, double worldZ)
	{
		double x = worldX / HorizontalScale, z = worldZ / HorizontalScale;
		double wx = x + Fbm(x / 4200, z / 4200, 11, 2) * 950;
		double wz = z + Fbm(x / 4200, z / 4200, 29, 2) * 950;
		double continent = Fbm(wx / 15000 + 5.7, wz / 15000 - 2.8, 43, 3);
		double land = Smooth(-0.34, 0.02, continent);
		double belt = Smooth(0.08, 0.56, Fbm(wx / 6500 + 9, wz / 6500 + 3, 61, 3) + 0.24);
		double ridge = 1 - Math.Abs(Fbm(wx / 1700 + 0.7, wz / 1700 + 4.9, 83, 5));
		double mountain = MountainHeight * belt * Math.Pow(ridge, 3.4);
		double foothills = 120 + Fbm(wx / 1600, wz / 1600, 109) * 190;
		double detail = Fbm(x / 210, z / 210, 131) * (5 + belt * 22);
		double elevation = Mix(-190 + continent * 220, foothills + mountain + detail, land);
		double riverX = Fbm(z / 3800 + 31.2, 7.4, 157, 3) * 1800 + Noise(z / 900, 2.8, 163) * 95;
		double distance = Math.Abs(x - riverX);
		double floodplain = 4 + Smooth(45, 270, distance) * 24 + Noise(x / 350, z / 350, 179) * 2;
		elevation = Mix(Math.Min(elevation, floodplain), elevation, Smooth(30, 1100, distance));
		return Mix(elevation, Math.Min(elevation, -7), 1 - Smooth(18, 48, distance));
	}

	public (double R, double G, double B) Color(double x, double z, double height, double slope)
	{
		double moisture = Fbm(x / 1700, z / 1700, 211, 2) * 0.5 + 0.5;
		double variation = Noise(x / 85, z / 85, 223) * 0.025;
		double r = Mix(0.19, 0.09, moisture) + variation;
		double g = Mix(0.245, 0.19, moisture) + variation;
		double b = Mix(0.085, 0.075, moisture) + variation;
		double alpine = Smooth(650, 1250, height);
		r = Mix(r, 0.25, alpine);
		g = Mix(g, 0.27, alpine);
		b = Mix(b, 0.19, alpine);
		double rock = Smooth(0.35, 0.85, slope);
		r = Mix(r, 0.29 + variation, rock);
		g = Mix(g, 0.28 + variation, rock);
		b = Mix(b, 0.255 + variation, rock);
		double shore = 1 - Smooth(SeaLevel + 1, SeaLevel + 9, height);
		r = Mix(r, 0.38, shore);
		g = Mix(g, 0.33, shore);
		b = Mix(b, 0.22, shore);
		double snow = Smooth(1280, 1640, height + Noise(x / 170, z / 170, 241) * 100) * (1 - Smooth(0.65, 1.3, slope));
		return (Mix(r, 0.83, snow), Mix(g, 0.86, snow), Mix(b, 0.88, snow));
	}
}
