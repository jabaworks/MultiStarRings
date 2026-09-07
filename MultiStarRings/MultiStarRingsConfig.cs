using System;
using System.Collections.Generic;
using UnityEngine;

namespace MultiStarRings
{
	[Serializable]
	public class MultiStarRingsConfig
	{
		public GlobalConfig Global = new GlobalConfig();
		public ShadowCasterList ShadowCasters = new ShadowCasterList();
		public RingShadowList RingShadows = new RingShadowList();
		public RingBrightnessList RingBrightness = new RingBrightnessList();
		public RingLightList RingLights = new RingLightList();
		public DebugConfig Debug = new DebugConfig();
	}

	[Serializable]
	public class GlobalConfig
	{
		public float shadowSoftness = 0.15f;
		public string shadowQuality = "High";
		public float updateInterval = 0.1f;
		public int maxShadowBodies = 8;
		public float brightnessCompressionExponent = 0.4f;
		public float brightnessCeiling = 5.0f;

		public float anisotropy = 0.72f;
		public float scatteringPower = 2.5f;
		public float scatteringStrength = 1.8f;
		public float ambientScatter = 0.15f;
	}

	[Serializable]
	public class ShadowCasterItem
	{
		public string name = "";
		public float radius = 1000000f;
		public float shadowIntensity = 1.0f;
		public float softness = 0.15f;
		public bool enabled = true;
	}

	[Serializable]
	public class ShadowCasterList
	{
		public List<ShadowCasterItem> Item =
			new List<ShadowCasterItem>();
	}

	[Serializable]
	public class RingShadowItem
	{
		public string ringBody = "";
		public string shadowCasters = "";
		public float shadowIntensity = 1.0f;
		public float softness = 0.15f;
		public bool enabled = true;
	}

	[Serializable]
	public class RingShadowList
	{
		public List<RingShadowItem> Item =
			new List<RingShadowItem>();
	}

	[Serializable]
	public class RingLightItem
	{
		public string ringBody = "";
		public string stars = "";
		public bool enabled = true;
	}

	[Serializable]
	public class RingLightList
	{
		public List<RingLightItem> Item =
			new List<RingLightItem>();
	}

	[Serializable]
	public class RingBrightnessItem
	{
		public string ringBody = "";
		public float compressionExponent = 0.4f;
		public float ceiling = 5.0f;
		public float glowBoost = 1.0f;
		public bool enabled = true;
		public bool UseDefaultShader = false;

		// Per-ring scattering/anisotropy overrides. float.MinValue means
		// "not set in this entry" - falls back to GlobalConfig's value.
		public float anisotropy = float.MinValue;
		public float scatteringPower = float.MinValue;
		public float scatteringStrength = float.MinValue;
		public float ambientScatter = float.MinValue;
	}

	[Serializable]
	public class RingBrightnessList
	{
		public List<RingBrightnessItem> Item =
			new List<RingBrightnessItem>();
	}

	[Serializable]
	public class DebugConfig
	{
		public bool enabled = true;
		public string logLevel = "Info";
		public bool drawDebugLines = false;
	}

	public static class ConfigLoader
	{
		private static MultiStarRingsConfig _config;
		private static bool _loaded = false;

		private const string RootNodeName =
			"MultiStarRings";

		public static MultiStarRingsConfig LoadConfig()
		{
			if (_loaded && _config != null)
				return _config;

			try
			{
				_config =
					new MultiStarRingsConfig();

				ConfigNode[] nodes =
					GameDatabase.Instance != null
						? GameDatabase.Instance.GetConfigNodes(
							RootNodeName)
						: new ConfigNode[0];

				if (nodes == null ||
					nodes.Length == 0)
				{
					Debug.LogWarning(
						"[MultiStarRings] No '" +
						RootNodeName +
						"' nodes found in GameData, using defaults");

					_loaded = true;
					return _config;
				}

				foreach (ConfigNode node in nodes)
				{
					if (node.HasNode("Global"))
					{
						_config.Global =
							ParseGlobal(
								node.GetNode("Global"));
					}

					if (node.HasNode("ShadowCasters"))
					{
						_config.ShadowCasters.Item.AddRange(
							ParseShadowCasters(
								node.GetNode("ShadowCasters")).Item);
					}

					if (node.HasNode("RingShadows"))
					{
						_config.RingShadows.Item.AddRange(
							ParseRingShadows(
								node.GetNode("RingShadows")).Item);
					}

					if (node.HasNode("RingBrightness"))
					{
						_config.RingBrightness.Item.AddRange(
							ParseRingBrightness(
								node.GetNode("RingBrightness")).Item);
					}

					if (node.HasNode("RingLights"))
					{
						_config.RingLights.Item.AddRange(
							ParseRingLights(
								node.GetNode("RingLights")).Item);
					}

					if (node.HasNode("Debug"))
					{
						_config.Debug =
							ParseDebug(
								node.GetNode("Debug"));
					}
				}

				_loaded = true;

				Debug.Log(
					"[MultiStarRings] Config loaded and merged from " +
					nodes.Length +
					" node(s) across GameData (" +
					_config.RingBrightness.Item.Count +
					" RingBrightness, " +
					_config.ShadowCasters.Item.Count +
					" ShadowCasters, " +
					_config.RingShadows.Item.Count +
					" RingShadows, " +
					_config.RingLights.Item.Count +
					" RingLights entries)");

				return _config;
			}
			catch (Exception ex)
			{
				Debug.LogError(
					"[MultiStarRings] Failed to load config: " +
					ex.Message);

				_config =
					new MultiStarRingsConfig();

				_loaded = true;

				return _config;
			}
		}

		public static void ApplyEditedNode(
			ConfigNode editedRoot)
		{
			if (editedRoot == null)
				return;

			MultiStarRingsConfig baseConfig =
				LoadConfig();

			if (editedRoot.HasNode("Global"))
			{
				baseConfig.Global =
					ParseGlobal(
						editedRoot.GetNode("Global"));
			}

			if (editedRoot.HasNode("ShadowCasters"))
			{
				ShadowCasterList edited =
					ParseShadowCasters(
						editedRoot.GetNode("ShadowCasters"));

				foreach (ShadowCasterItem editedItem
						 in edited.Item)
				{
					int index =
						baseConfig.ShadowCasters.Item.FindIndex(
							x =>
								x != null &&
								x.name == editedItem.name);

					if (index >= 0)
					{
						baseConfig.ShadowCasters.Item[index] =
							editedItem;
					}
					else
					{
						baseConfig.ShadowCasters.Item.Add(
							editedItem);
					}
				}
			}

			if (editedRoot.HasNode("RingShadows"))
			{
				RingShadowList edited =
					ParseRingShadows(
						editedRoot.GetNode("RingShadows"));

				foreach (RingShadowItem editedItem
						 in edited.Item)
				{
					int index =
						baseConfig.RingShadows.Item.FindIndex(
							x =>
								x != null &&
								x.ringBody == editedItem.ringBody);

					if (index >= 0)
					{
						baseConfig.RingShadows.Item[index] =
							editedItem;
					}
					else
					{
						baseConfig.RingShadows.Item.Add(
							editedItem);
					}
				}
			}

			if (editedRoot.HasNode("RingBrightness"))
			{
				RingBrightnessList edited =
					ParseRingBrightness(
						editedRoot.GetNode("RingBrightness"));

				foreach (RingBrightnessItem editedItem
						 in edited.Item)
				{
					int index =
						baseConfig.RingBrightness.Item.FindIndex(
							x =>
								x != null &&
								x.ringBody == editedItem.ringBody);

					if (index >= 0)
					{
						baseConfig.RingBrightness.Item[index] =
							editedItem;
					}
					else
					{
						baseConfig.RingBrightness.Item.Add(
							editedItem);
					}
				}
			}

			if (editedRoot.HasNode("RingLights"))
			{
				RingLightList edited =
					ParseRingLights(
						editedRoot.GetNode("RingLights"));

				foreach (RingLightItem editedItem
						 in edited.Item)
				{
					int index =
						baseConfig.RingLights.Item.FindIndex(
							x =>
								x != null &&
								x.ringBody == editedItem.ringBody);

					if (index >= 0)
					{
						baseConfig.RingLights.Item[index] =
							editedItem;
					}
					else
					{
						baseConfig.RingLights.Item.Add(
							editedItem);
					}
				}
			}

			if (editedRoot.HasNode("Debug"))
			{
				baseConfig.Debug =
					ParseDebug(
						editedRoot.GetNode("Debug"));
			}

			_config = baseConfig;
			_loaded = true;
		}

		public static void Reset()
		{
			_config = null;
			_loaded = false;
		}

		private static GlobalConfig ParseGlobal(
			ConfigNode node)
		{
			GlobalConfig c =
				new GlobalConfig();

			c.shadowSoftness =
				node.GetFloat(
					"shadowSoftness",
					0.15f);

			c.shadowQuality =
				node.GetString(
					"shadowQuality",
					"High");

			c.updateInterval =
				node.GetFloat(
					"updateInterval",
					0.1f);

			c.maxShadowBodies =
				node.GetInt(
					"maxShadowBodies",
					8);

			c.brightnessCompressionExponent =
				node.GetFloat(
					"brightnessCompressionExponent",
					0.4f);

			c.brightnessCeiling =
				node.GetFloat(
					"brightnessCeiling",
					5.0f);

			c.anisotropy =
				node.GetFloat(
					"anisotropy",
					0.72f);

			c.scatteringPower =
				node.GetFloat(
					"scatteringPower",
					2.5f);

			c.scatteringStrength =
				node.GetFloat(
					"scatteringStrength",
					1.8f);

			c.ambientScatter =
				node.GetFloat(
					"ambientScatter",
					0.15f);

			return c;
		}

		private static ShadowCasterList ParseShadowCasters(
			ConfigNode node)
		{
			ShadowCasterList list =
				new ShadowCasterList();

			foreach (ConfigNode itemNode
					 in node.GetNodes("Item"))
			{
				ShadowCasterItem item =
					new ShadowCasterItem
					{
						name =
							itemNode.GetString(
								"name",
								""),

						radius =
							itemNode.GetFloat(
								"radius",
								1000000f),

						shadowIntensity =
							itemNode.GetFloat(
								"shadowIntensity",
								1.0f),

						softness =
							itemNode.GetFloat(
								"softness",
								0.15f),

						enabled =
							itemNode.GetBool(
								"enabled",
								true)
					};

				if (!string.IsNullOrEmpty(item.name))
					list.Item.Add(item);
			}

			return list;
		}

		private static RingShadowList ParseRingShadows(
			ConfigNode node)
		{
			RingShadowList list =
				new RingShadowList();

			foreach (ConfigNode itemNode
					 in node.GetNodes("Item"))
			{
				RingShadowItem item =
					new RingShadowItem
					{
						ringBody =
							itemNode.GetString(
								"ringBody",
								""),

						shadowCasters =
							itemNode.GetString(
								"shadowCasters",
								""),

						shadowIntensity =
							itemNode.GetFloat(
								"shadowIntensity",
								1.0f),

						softness =
							itemNode.GetFloat(
								"softness",
								0.15f),

						enabled =
							itemNode.GetBool(
								"enabled",
								true)
					};

				if (!string.IsNullOrEmpty(item.ringBody))
					list.Item.Add(item);
			}

			return list;
		}

		private static RingLightList ParseRingLights(
			ConfigNode node)
		{
			RingLightList list =
				new RingLightList();

			foreach (ConfigNode itemNode
					 in node.GetNodes("Item"))
			{
				RingLightItem item =
					new RingLightItem
					{
						ringBody =
							itemNode.GetString(
								"ringBody",
								""),

						stars =
							itemNode.GetString(
								"stars",
								""),

						enabled =
							itemNode.GetBool(
								"enabled",
								true)
					};

				if (!string.IsNullOrEmpty(item.ringBody))
					list.Item.Add(item);
			}

			return list;
		}

		private static RingBrightnessList ParseRingBrightness(
			ConfigNode node)
		{
			RingBrightnessList list =
				new RingBrightnessList();

			foreach (ConfigNode itemNode
					 in node.GetNodes("Item"))
			{
				RingBrightnessItem item =
					new RingBrightnessItem
					{
						ringBody =
							itemNode.GetString(
								"ringBody",
								""),

						compressionExponent =
							itemNode.GetFloat(
								"compressionExponent",
								0.4f),

						ceiling =
							itemNode.GetFloat(
								"ceiling",
								5.0f),

						glowBoost =
							itemNode.GetFloat(
								"glowBoost",
								1.0f),

						enabled =
							itemNode.GetBool(
								"enabled",
								true),

						UseDefaultShader =
							itemNode.GetBool(
								"UseDefaultShader",
								false),

						anisotropy =
							itemNode.GetFloat(
								"anisotropy",
								float.MinValue),

						scatteringPower =
							itemNode.GetFloat(
								"scatteringPower",
								float.MinValue),

						scatteringStrength =
							itemNode.GetFloat(
								"scatteringStrength",
								float.MinValue),

						ambientScatter =
							itemNode.GetFloat(
								"ambientScatter",
								float.MinValue)
					};

				if (!string.IsNullOrEmpty(item.ringBody))
					list.Item.Add(item);
			}

			return list;
		}

		private static DebugConfig ParseDebug(
			ConfigNode node)
		{
			DebugConfig c =
				new DebugConfig();

			c.enabled =
				node.GetBool(
					"enabled",
					true);

			c.logLevel =
				node.GetString(
					"logLevel",
					"Info");

			c.drawDebugLines =
				node.GetBool(
					"drawDebugLines",
					false);

			return c;
		}
	}

	public static class ConfigNodeExtensions
	{
		public static float GetFloat(
			this ConfigNode node,
			string name,
			float defaultValue = 0f)
		{
			string val =
				node.GetValue(name);

			if (string.IsNullOrEmpty(val))
				return defaultValue;

			return float.TryParse(
				val,
				out float result)
				? result
				: defaultValue;
		}

		public static string GetString(
			this ConfigNode node,
			string name,
			string defaultValue = "")
		{
			string val =
				node.GetValue(name);

			return string.IsNullOrEmpty(val)
				? defaultValue
				: val;
		}

		public static int GetInt(
			this ConfigNode node,
			string name,
			int defaultValue = 0)
		{
			string val =
				node.GetValue(name);

			if (string.IsNullOrEmpty(val))
				return defaultValue;

			return int.TryParse(
				val,
				out int result)
				? result
				: defaultValue;
		}

		public static bool GetBool(
			this ConfigNode node,
			string name,
			bool defaultValue = false)
		{
			string val =
				node.GetValue(name);

			if (string.IsNullOrEmpty(val))
				return defaultValue;

			return bool.TryParse(
				val,
				out bool result)
				? result
				: defaultValue;
		}
	}
}