using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEngine;

namespace MultiStarRings
{
	[KSPAddon(KSPAddon.Startup.Flight, false)]
	public class MultiStarRingsUIFlight : MultiStarRingsUIBase
	{
	}

	[KSPAddon(KSPAddon.Startup.TrackingStation, false)]
	public class MultiStarRingsUITrackingStation : MultiStarRingsUIBase
	{
	}

	[KSPAddon(KSPAddon.Startup.SpaceCentre, false)]
	public class MultiStarRingsUISpaceCentre : MultiStarRingsUIBase
	{
	}

	public class MultiStarRingsUIBase : MonoBehaviour
	{
		private class ConfigFile
		{
			public string FullPath;
			public string DisplayPath;
			public string OriginalText;
			public List<ConfigBlock> Blocks =
				new List<ConfigBlock>();
		}

		private class ConfigBlock
		{
			public int Start;
			public int End;
			public ConfigNode Node;
		}

		private Rect _windowRect =
			new Rect(120, 60, 640, 700);

		private Vector2 _scroll;

		private readonly List<ConfigFile> _files =
			new List<ConfigFile>();

		private int _fileIndex;
		private int _sectionIndex;
		private int _itemIndex;

		private bool _visible;

		private GUIStyle _windowStyle;
		private GUIStyle _titleStyle;
		private GUIStyle _labelStyle;
		private GUIStyle _fieldStyle;
		private GUIStyle _buttonStyle;
		private GUIStyle _smallButtonStyle;
		private GUIStyle _centerStyle;
		private GUIStyle _boxStyle;

		private bool _stylesReady;

		private string _status = "";
		private float _statusTimer;

		// "Create a new config" panel state.
		private bool _showCreatePanel;
		private string _newConfigFolder = "";
		private string _newConfigName = "NewConfig";

		private void Awake()
		{
			MultiStarRingsUIBase[] existing =
				UnityEngine.Object.FindObjectsOfType<MultiStarRingsUIBase>();

			if (existing != null &&
				existing.Length > 1)
			{
				Destroy(gameObject);
				return;
			}

			DontDestroyOnLoad(gameObject);
			ScanFiles();
		}

		private void Update()
		{
			if (Input.GetKey(KeyCode.LeftControl) &&
				Input.GetKeyDown(KeyCode.R))
			{
				_visible = !_visible;

				if (_visible)
				{
					ScanFiles();
				}
			}

			if (_statusTimer > 0f)
			{
				_statusTimer -= Time.deltaTime;

				if (_statusTimer <= 0f)
				{
					_status = "";
				}
			}
		}

		private void OnGUI()
		{
			if (!_visible)
			{
				return;
			}

			EnsureStyles();

			_windowRect =
				GUI.Window(
					241931,
					_windowRect,
					DrawWindow,
					"MultiStarRings Config Editor",
					_windowStyle);
		}

		private void EnsureStyles()
		{
			if (_stylesReady)
			{
				return;
			}

			_windowStyle =
				new GUIStyle(GUI.skin.window)
				{
					fontSize = 14
				};

			_titleStyle =
				new GUIStyle(GUI.skin.label)
				{
					fontSize = 15,
					fontStyle = FontStyle.Bold,
					alignment = TextAnchor.MiddleCenter
				};

			_labelStyle =
				new GUIStyle(GUI.skin.label)
				{
					fontSize = 12
				};

			_fieldStyle =
				new GUIStyle(GUI.skin.textField)
				{
					fontSize = 12
				};

			_buttonStyle =
				new GUIStyle(GUI.skin.button)
				{
					fontSize = 12
				};

			_smallButtonStyle =
				new GUIStyle(GUI.skin.button)
				{
					fontSize = 11
				};

			_centerStyle =
				new GUIStyle(GUI.skin.label)
				{
					fontSize = 11,
					alignment = TextAnchor.MiddleCenter,
					wordWrap = false
				};

			_boxStyle =
				new GUIStyle(GUI.skin.box);

			_stylesReady = true;
		}

		private void DrawWindow(int id)
		{
			GUILayout.BeginVertical();

			GUILayout.BeginHorizontal();

			GUILayout.FlexibleSpace();

			GUILayout.Label(
				"MultiStarRings",
				_titleStyle,
				GUILayout.Width(300));

			GUILayout.FlexibleSpace();

			if (GUILayout.Button(
					"X",
					_smallButtonStyle,
					GUILayout.Width(28)))
			{
				_visible = false;
			}

			GUILayout.EndHorizontal();

			GUILayout.Space(4);

			DrawFileSelector();

			GUILayout.Space(2);

			DrawSectionSelector();

			DrawItemSelector();

			GUILayout.Space(4);

			GUILayout.BeginHorizontal();

			if (GUILayout.Button(
					"Apply",
					_buttonStyle,
					GUILayout.Height(30)))
			{
				ApplyChanges();
			}

			if (GUILayout.Button(
					"Save",
					_buttonStyle,
					GUILayout.Height(30)))
			{
				SaveCurrentFile();
			}

			if (GUILayout.Button(
					"Reload Shader",
					_buttonStyle,
					GUILayout.Height(30)))
			{
				ReloadShader();
			}

			GUILayout.EndHorizontal();

			GUILayout.Space(4);

			if (GUILayout.Button(
					_showCreatePanel
						? "▼ New Config..."
						: "▶ New Config...",
					_smallButtonStyle,
					GUILayout.Height(22)))
			{
				_showCreatePanel = !_showCreatePanel;
			}

			if (_showCreatePanel)
			{
				DrawCreateConfigPanel();
			}

			GUILayout.Space(5);

			if (_files.Count == 0)
			{
				GUILayout.Label(
					"No MultiStarRings configs found.",
					_labelStyle);

				GUILayout.EndVertical();

				GUI.DragWindow(
					new Rect(
						0,
						0,
						10000,
						24));

				return;
			}

			_scroll =
				GUILayout.BeginScrollView(
					_scroll,
					false,
					true,
					GUILayout.Height(520));

			DrawCurrentSection();

			GUILayout.EndScrollView();

			if (!string.IsNullOrEmpty(_status))
			{
				GUILayout.Space(3);

				GUILayout.Label(
					_status,
					_centerStyle);
			}

			GUILayout.EndVertical();

			GUI.DragWindow(
				new Rect(
					0,
					0,
					10000,
					24));
		}

		private void DrawCreateConfigPanel()
		{
			GUILayout.BeginVertical(
				_boxStyle);

			GUILayout.Label(
				"Folder (absolute path)",
				_labelStyle);

			_newConfigFolder =
				GUILayout.TextField(
					_newConfigFolder,
					_fieldStyle);

			GUILayout.Label(
				"File name (without .cfg)",
				_labelStyle);

			_newConfigName =
				GUILayout.TextField(
					_newConfigName,
					_fieldStyle);

			GUILayout.Space(4);

			if (GUILayout.Button(
					"Create",
					_buttonStyle,
					GUILayout.Height(28)))
			{
				CreateNewConfig(
					_newConfigFolder,
					_newConfigName);
			}

			GUILayout.EndVertical();

			GUILayout.Space(4);
		}

		private void CreateNewConfig(
			string folder,
			string name)
		{
			if (string.IsNullOrWhiteSpace(folder))
			{
				SetStatus(
					"Enter a folder path first.");

				return;
			}

			if (string.IsNullOrWhiteSpace(name))
			{
				SetStatus(
					"Enter a file name first.");

				return;
			}

			foreach (char c in Path.GetInvalidFileNameChars())
			{
				if (name.IndexOf(c) >= 0)
				{
					SetStatus(
						"File name has invalid characters.");

					return;
				}
			}

			try
			{
				if (!Directory.Exists(folder))
				{
					Directory.CreateDirectory(folder);
				}

				string fileName =
					name.EndsWith(
						".cfg",
						StringComparison.OrdinalIgnoreCase)
						? name
						: name + ".cfg";

				string fullPath =
					Path.Combine(
						folder,
						fileName);

				if (File.Exists(fullPath))
				{
					SetStatus(
						"A file with that name already exists there.");

					return;
				}

				File.WriteAllText(
					fullPath,
					BuildConfigTemplate());

				_showCreatePanel = false;

				ScanFiles();

				for (int i = 0; i < _files.Count; i++)
				{
					if (string.Equals(
							_files[i].FullPath,
							fullPath,
							StringComparison.OrdinalIgnoreCase))
					{
						_fileIndex = i;
						_sectionIndex = 0;
						_itemIndex = 0;
						break;
					}
				}

				SetStatus(
					"Created " +
					fileName +
					" - configure it below, then Save.");
			}
			catch (Exception ex)
			{
				Debug.LogError(
					"[MultiStarRings] Create config failed: " +
					ex);

				SetStatus(
					"Create failed: " +
					ex.Message);
			}
		}

		private string BuildConfigTemplate()
		{
			ConfigNode root =
				new ConfigNode("MultiStarRings");

			root.AddNode(new ConfigNode("ShadowCasters"));
			root.AddNode(new ConfigNode("RingShadows"));
			root.AddNode(new ConfigNode("RingBrightness"));
			root.AddNode(new ConfigNode("RingLights"));

			return root.ToString();
		}

		private void DrawFileSelector()
		{
			ConfigFile file =
				CurrentFile();

			GUILayout.BeginHorizontal();

			bool canCycle =
				_files.Count > 1;

			GUI.enabled = canCycle;

			if (GUILayout.Button(
					"←",
					_buttonStyle,
					GUILayout.Width(155)))
			{
				PreviousFile();
			}

			GUI.enabled = true;

			GUILayout.BeginVertical();

			GUILayout.Label(
				"Config File",
				_smallButtonStyle,
				GUILayout.Height(20));

			GUILayout.Label(
				file != null
					? file.DisplayPath
					: "None",
				_centerStyle,
				GUILayout.Height(20));

			GUILayout.Label(
				_files.Count > 0
					? (_fileIndex + 1) +
					  " / " +
					  _files.Count
					: "0 / 0",
				_centerStyle,
				GUILayout.Height(17));

			GUILayout.EndVertical();

			GUI.enabled = canCycle;

			if (GUILayout.Button(
					"→",
					_buttonStyle,
					GUILayout.Width(155)))
			{
				NextFile();
			}

			GUI.enabled = true;

			GUILayout.EndHorizontal();
		}

		private void DrawSectionSelector()
		{
			ConfigNode root =
				CurrentRootNode();

			if (root == null ||
				root.nodes == null ||
				root.nodes.Count == 0)
			{
				return;
			}

			int count =
				root.nodes.Count;

			if (_sectionIndex < 0)
			{
				_sectionIndex = 0;
			}

			if (_sectionIndex >= count)
			{
				_sectionIndex = count - 1;
			}

			GUILayout.BeginHorizontal();

			bool canCycle =
				count > 1;

			GUI.enabled = canCycle;

			if (GUILayout.Button(
					"←",
					_buttonStyle,
					GUILayout.Width(155)))
			{
				_sectionIndex--;

				if (_sectionIndex < 0)
				{
					_sectionIndex = count - 1;
				}

				_itemIndex = 0;
				_scroll = Vector2.zero;
			}

			GUI.enabled = true;

			GUILayout.BeginVertical();

			GUILayout.Label(
				"Section",
				_smallButtonStyle,
				GUILayout.Height(20));

			GUILayout.Label(
				root.nodes[_sectionIndex].name,
				_titleStyle,
				GUILayout.Height(20));

			GUILayout.Label(
				(_sectionIndex + 1) +
				" / " +
				count,
				_centerStyle,
				GUILayout.Height(17));

			GUILayout.EndVertical();

			GUI.enabled = canCycle;

			if (GUILayout.Button(
					"→",
					_buttonStyle,
					GUILayout.Width(155)))
			{
				_sectionIndex++;

				if (_sectionIndex >= count)
				{
					_sectionIndex = 0;
				}

				_itemIndex = 0;
				_scroll = Vector2.zero;
			}

			GUI.enabled = true;

			GUILayout.EndHorizontal();
		}

		private void DrawItemSelector()
		{
			ConfigNode section =
				CurrentSection();

			if (section == null ||
				section.nodes == null ||
				section.nodes.Count == 0)
			{
				return;
			}

			int count =
				section.nodes.Count;

			if (_itemIndex < 0)
			{
				_itemIndex = 0;
			}

			if (_itemIndex >= count)
			{
				_itemIndex = count - 1;
			}

			GUILayout.BeginHorizontal();

			bool canCycle =
				count > 1;

			GUI.enabled = canCycle;

			if (GUILayout.Button(
					"←",
					_buttonStyle,
					GUILayout.Width(155)))
			{
				_itemIndex--;

				if (_itemIndex < 0)
				{
					_itemIndex = count - 1;
				}

				_scroll = Vector2.zero;
			}

			GUI.enabled = true;

			GUILayout.BeginVertical();

			GUILayout.Label(
				"Item",
				_smallButtonStyle,
				GUILayout.Height(20));

			GUILayout.Label(
				section.nodes[_itemIndex].name,
				_titleStyle,
				GUILayout.Height(20));

			GUILayout.Label(
				(_itemIndex + 1) +
				" / " +
				count,
				_centerStyle,
				GUILayout.Height(17));

			GUILayout.EndVertical();

			GUI.enabled = canCycle;

			if (GUILayout.Button(
					"→",
					_buttonStyle,
					GUILayout.Width(155)))
			{
				_itemIndex++;

				if (_itemIndex >= count)
				{
					_itemIndex = 0;
				}

				_scroll = Vector2.zero;
			}

			GUI.enabled = true;

			GUILayout.EndHorizontal();
		}

		private void DrawCurrentSection()
		{
			ConfigNode section =
				CurrentSection();

			if (section == null)
			{
				ConfigNode root =
					CurrentRootNode();

				if (root == null)
				{
					return;
				}

				DrawNodeValues(root);
				return;
			}

			DrawAddEntryButton(section);

			if (section.nodes != null &&
				section.nodes.Count > 0)
			{
				ConfigNode item =
					CurrentItem();

				if (item != null)
				{
					DrawNodeValues(item);
				}

				return;
			}

			DrawNodeValues(section);
		}

		private void DrawAddEntryButton(ConfigNode categoryNode)
		{
			(string key, string value)[] defaults =
				GetListDefaultsForCategory(
					categoryNode.name);

			if (defaults == null)
			{
				return;
			}

			if (GUILayout.Button(
					"+ Add New Entry",
					_buttonStyle,
					GUILayout.Height(26)))
			{
				ConfigNode newItem =
					new ConfigNode("Item");

				FillDefaults(
					newItem,
					defaults);

				categoryNode.AddNode(
					newItem);

				SetStatus(
					"Added new " +
					categoryNode.name +
					" entry with default values - rename it, then Apply and Save.");
			}

			GUILayout.Space(4);
		}

		private void DrawNodeValues(ConfigNode node)
		{
			if (node == null)
			{
				return;
			}

			if (node.values != null)
			{
				foreach (ConfigNode.Value value
						 in node.values)
				{
					DrawValue(
						node,
						value.name,
						value.value);
				}
			}

			if (node.nodes != null &&
				node.nodes.Count > 0)
			{
				GUILayout.Space(8);

				GUILayout.Label(
					"Nested Nodes",
					_sectionLabelStyle());

				foreach (ConfigNode child
						 in node.nodes)
				{
					DrawNestedNode(
						child,
						0);
				}
			}
		}

		private void DrawNestedNode(
			ConfigNode node,
			int depth)
		{
			if (node == null)
			{
				return;
			}

			GUILayout.BeginVertical(
				_boxStyle);

			GUILayout.Label(
				node.name,
				_titleStyle);

			if (node.values != null)
			{
				foreach (ConfigNode.Value value
						 in node.values)
				{
					DrawValue(
						node,
						value.name,
						value.value);
				}
			}

			if (node.nodes != null &&
				node.nodes.Count > 0)
			{
				foreach (ConfigNode child
						 in node.nodes)
				{
					DrawNestedNode(
						child,
						depth + 1);
				}
			}

			GUILayout.EndVertical();

			GUILayout.Space(3);
		}

		private void DrawValue(
			ConfigNode owner,
			string name,
			string value)
		{
			GUILayout.BeginHorizontal();

			GUILayout.Label(
				name,
				_labelStyle,
				GUILayout.Width(190));

			string original =
				value ?? "";

			string edited =
				GUILayout.TextField(
					original,
					_fieldStyle);

			if (edited != original)
			{
				owner.SetValue(
					name,
					edited,
					true);
			}

			GUILayout.EndHorizontal();
		}

		private GUIStyle _sectionLabelStyle()
		{
			GUIStyle style =
				new GUIStyle(
					GUI.skin.button)
				{
					fontSize = 12,
					fontStyle = FontStyle.Bold
				};

			return style;
		}

		private void ApplyConfigDefaults(
			ConfigNode parsedRoot,
			bool isMainConfigFile)
		{
			if (parsedRoot == null)
			{
				return;
			}

			ConfigNode msr =
				parsedRoot.nodes != null &&
				parsedRoot.nodes.Count > 0
					? parsedRoot.nodes[0]
					: parsedRoot;

			if (msr == null)
			{
				return;
			}

			if (isMainConfigFile)
			{
				FillDefaults(
					EnsureNode(msr, "Global"),
					GlobalDefaults());

				FillDefaults(
					EnsureNode(msr, "Debug"),
					DebugDefaults());

				return;
			}
			FillListDefaults(
				EnsureNode(msr, "ShadowCasters"),
				ShadowCasterDefaults());

			FillListDefaults(
				EnsureNode(msr, "RingShadows"),
				RingShadowDefaults());

			FillListDefaults(
				EnsureNode(msr, "RingBrightness"),
				RingBrightnessDefaults());

			FillListDefaults(
				EnsureNode(msr, "RingLights"),
				RingLightDefaults());
		}

		private ConfigNode EnsureNode(
			ConfigNode parent,
			string name)
		{
			ConfigNode node =
				parent.GetNode(name);

			if (node == null)
			{
				node = new ConfigNode(name);
				parent.AddNode(node);
			}

			return node;
		}

		private void FillListDefaults(
			ConfigNode listNode,
			(string key, string value)[] defaults)
		{
			if (listNode == null ||
				listNode.nodes == null)
			{
				return;
			}

			foreach (ConfigNode item in listNode.nodes)
			{
				FillDefaults(
					item,
					defaults);
			}
		}

		private void FillDefaults(
			ConfigNode node,
			(string key, string value)[] defaults)
		{
			if (node == null ||
				defaults == null)
			{
				return;
			}

			foreach (var d in defaults)
			{
				if (!node.HasValue(d.key))
				{
					node.AddValue(
						d.key,
						d.value);
				}
			}
		}

		private (string key, string value)[] GetListDefaultsForCategory(
			string categoryName)
		{
			switch (categoryName)
			{
				case "ShadowCasters":
					return ShadowCasterDefaults();

				case "RingShadows":
					return RingShadowDefaults();

				case "RingBrightness":
					return RingBrightnessDefaults();

				case "RingLights":
					return RingLightDefaults();

				default:
					return null;
			}
		}

		// These mirror the field defaults declared on GlobalConfig,
		// DebugConfig, ShadowCasterItem, RingShadowItem and
		// RingBrightnessItem in MultiStarRingsConfig.cs. Kept in sync
		// with those manually since ConfigNode has no reflection-based
		// default-instance helper here.
		private static (string key, string value)[] GlobalDefaults() =>
			new[]
			{
				("shadowSoftness", "0.15"),
				("shadowQuality", "High"),
				("updateInterval", "0.1"),
				("maxShadowBodies", "8"),
				("brightnessCompressionExponent", "0.4"),
				("brightnessCeiling", "5"),
			};

		private static (string key, string value)[] DebugDefaults() =>
			new[]
			{
				("enabled", "True"),
				("logLevel", "Info"),
				("drawDebugLines", "False"),
			};

		private static (string key, string value)[] ShadowCasterDefaults() =>
			new[]
			{
				("name", "NewShadowCaster"),
				("radius", "1000000"),
				("shadowIntensity", "1"),
				("softness", "0.15"),
				("enabled", "True"),
			};

		private static (string key, string value)[] RingShadowDefaults() =>
			new[]
			{
				("ringBody", "NewRingBody"),
				("shadowCasters", ""),
				("shadowIntensity", "1"),
				("softness", "0.15"),
				("enabled", "True"),
			};

		private static (string key, string value)[] RingBrightnessDefaults() =>
			new[]
			{
				("ringBody", "NewRingBody"),
				("compressionExponent", "0.4"),
				("ceiling", "5"),
				("glowBoost", "1"),
				("enabled", "True"),
				("UseDefaultShader", "False"),
			};

		private static (string key, string value)[] RingLightDefaults() =>
			new[]
			{
				("ringBody", "NewRingBody"),
				("stars", ""),
				("enabled", "True"),
			};

		private ConfigFile CurrentFile()
		{
			if (_files.Count == 0)
			{
				return null;
			}

			if (_fileIndex < 0)
			{
				_fileIndex = 0;
			}

			if (_fileIndex >= _files.Count)
			{
				_fileIndex =
					_files.Count - 1;
			}

			return _files[_fileIndex];
		}

		private ConfigNode CurrentRootNode()
		{
			ConfigFile file =
				CurrentFile();

			if (file == null ||
				file.Blocks.Count == 0)
			{
				return null;
			}

			if (_fileIndex < 0)
			{
				_fileIndex = 0;
			}

			ConfigBlock block =
				file.Blocks[0];

			return
				block.Node.nodes != null &&
				block.Node.nodes.Count > 0
					? block.Node.nodes[0]
					: block.Node;
		}

		private ConfigNode CurrentSection()
		{
			ConfigNode root =
				CurrentRootNode();

			if (root == null ||
				root.nodes == null ||
				root.nodes.Count == 0)
			{
				return null;
			}

			if (_sectionIndex < 0)
			{
				_sectionIndex = 0;
			}

			if (_sectionIndex >= root.nodes.Count)
			{
				_sectionIndex =
					root.nodes.Count - 1;
			}

			return root.nodes[_sectionIndex];
		}

		private ConfigNode CurrentItem()
		{
			ConfigNode section =
				CurrentSection();

			if (section == null ||
				section.nodes == null ||
				section.nodes.Count == 0)
			{
				return null;
			}

			if (_itemIndex < 0)
			{
				_itemIndex = 0;
			}

			if (_itemIndex >= section.nodes.Count)
			{
				_itemIndex =
					section.nodes.Count - 1;
			}

			return section.nodes[_itemIndex];
		}

		private void PreviousFile()
		{
			if (_files.Count <= 1)
			{
				return;
			}

			_fileIndex--;

			if (_fileIndex < 0)
			{
				_fileIndex =
					_files.Count - 1;
			}

			_sectionIndex = 0;
			_itemIndex = 0;
			_scroll = Vector2.zero;
		}

		private void NextFile()
		{
			if (_files.Count <= 1)
			{
				return;
			}

			_fileIndex++;

			if (_fileIndex >= _files.Count)
			{
				_fileIndex = 0;
			}

			_sectionIndex = 0;
			_itemIndex = 0;
			_scroll = Vector2.zero;
		}

		private void ScanFiles()
		{
			_files.Clear();

			string gameData =
				KSPUtil.ApplicationRootPath +
				"GameData";

			if (string.IsNullOrEmpty(_newConfigFolder))
			{
				_newConfigFolder = gameData;
			}

			if (!Directory.Exists(gameData))
			{
				SetStatus(
					"GameData not found.");

				return;
			}

			string[] paths;

			try
			{
				paths =
					Directory.GetFiles(
						gameData,
						"*.cfg",
						SearchOption.AllDirectories);
			}
			catch (Exception ex)
			{
				Debug.LogError(
					"[MultiStarRings] Config scan failed: " +
					ex);

				SetStatus(
					"Config scan failed.");

				return;
			}

			foreach (string path in paths)
			{
				string text;

				try
				{
					text =
						File.ReadAllText(path);
				}
				catch
				{
					continue;
				}

				List<Tuple<int, int>> ranges =
					FindRootRanges(
						text,
						"MultiStarRings");

				if (ranges.Count == 0)
				{
					continue;
				}

				ConfigFile file =
					new ConfigFile
					{
						FullPath = path,
						DisplayPath =
							MakeDisplayPath(
								gameData,
								path),
						OriginalText = text
					};

				foreach (Tuple<int, int> range
						 in ranges)
				{
					int start =
						range.Item1;

					int end =
						range.Item2;

					string block =
						text.Substring(
							start,
							end - start);

					try
					{
						ConfigNode node =
							ConfigNode.Parse(block);
						bool isMainConfigFile =
							string.Equals(
								Path.GetFileName(path),
								"MultiStarRings.cfg",
								StringComparison.OrdinalIgnoreCase);

						ApplyConfigDefaults(
							node,
							isMainConfigFile);

						file.Blocks.Add(
							new ConfigBlock
							{
								Start = start,
								End = end,
								Node = node
							});
					}
					catch (Exception ex)
					{
						Debug.LogError(
							"[MultiStarRings] Could not parse " +
							file.DisplayPath +
							": " +
							ex.Message);
					}
				}

				if (file.Blocks.Count > 0)
				{
					_files.Add(file);

					Debug.Log(
						"[MultiStarRings] UI found: " +
						file.DisplayPath);
				}
			}

			_files.Sort(
				delegate (
					ConfigFile a,
					ConfigFile b)
				{
					return string.Compare(
						a.DisplayPath,
						b.DisplayPath,
						StringComparison.OrdinalIgnoreCase);
				});

			if (_files.Count == 0)
			{
				_fileIndex = 0;
				_sectionIndex = 0;
				_itemIndex = 0;

				SetStatus(
					"No MultiStarRings configs found.");

				return;
			}

			if (_fileIndex >= _files.Count)
			{
				_fileIndex = 0;
			}

			_sectionIndex = 0;
			_itemIndex = 0;
			_scroll = Vector2.zero;

			SetStatus(
				"Found " +
				_files.Count +
				" config file(s).");
		}

		private List<Tuple<int, int>> FindRootRanges(
			string text,
			string nodeName)
		{
			List<Tuple<int, int>> result =
				new List<Tuple<int, int>>();

			if (string.IsNullOrEmpty(text))
			{
				return result;
			}

			int search =
				0;

			while (search < text.Length)
			{
				int index =
					text.IndexOf(
						nodeName,
						search,
						StringComparison.Ordinal);

				if (index < 0)
				{
					break;
				}

				int lineStart =
					index;

				while (lineStart > 0 &&
					   text[lineStart - 1] != '\n' &&
					   text[lineStart - 1] != '\r')
				{
					lineStart--;
				}

				string prefix =
					text.Substring(
						lineStart,
						index - lineStart);

				if (prefix.Trim().Length != 0)
				{
					search =
						index +
						nodeName.Length;

					continue;
				}

				int afterName =
					index +
					nodeName.Length;

				while (afterName < text.Length &&
					   char.IsWhiteSpace(
						   text[afterName]))
				{
					afterName++;
				}

				if (afterName >= text.Length ||
					text[afterName] != '{')
				{
					search =
						index +
						nodeName.Length;

					continue;
				}

				int depth = 0;
				int close = -1;

				for (int i = afterName;
					 i < text.Length;
					 i++)
				{
					char c =
						text[i];

					if (c == '{')
					{
						depth++;
					}
					else if (c == '}')
					{
						depth--;

						if (depth == 0)
						{
							close = i;
							break;
						}
					}
				}

				if (close < 0)
				{
					break;
				}

				result.Add(
					Tuple.Create(
						lineStart,
						close + 1));

				search =
					close + 1;
			}

			return result;
		}

		private string MakeDisplayPath(
			string gameData,
			string path)
		{
			string prefix =
				gameData.TrimEnd(
					Path.DirectorySeparatorChar,
					Path.AltDirectorySeparatorChar);

			if (path.StartsWith(
					prefix,
					StringComparison.OrdinalIgnoreCase))
			{
				string result =
					path.Substring(
						prefix.Length);

				return result.TrimStart(
					Path.DirectorySeparatorChar,
					Path.AltDirectorySeparatorChar)
					.Replace(
						'\\',
						'/');
			}

			return path.Replace(
				'\\',
				'/');
		}

		private void ApplyChanges()
		{
			try
			{
				ConfigNode editedNode =
					CurrentRootNode();

				if (editedNode != null)
				{
					ConfigLoader.ApplyEditedNode(
						editedNode);
				}

				SetStatus(
					"Applied.");
			}
			catch (Exception ex)
			{
				Debug.LogError(
					"[MultiStarRings] Apply failed: " +
					ex);

				SetStatus(
					"Apply failed.");
			}
		}

		private void ReloadShader()
		{
			try
			{
				bool success =
					MultiStarRingsCore.ReloadShaderBundle();

				SetStatus(
					success
						? "Shader reloaded."
						: "Shader reload failed - check log.");
			}
			catch (Exception ex)
			{
				Debug.LogError(
					"[MultiStarRings] Shader reload failed: " +
					ex);

				SetStatus(
					"Shader reload failed.");
			}
		}

		private void SaveCurrentFile()
		{
			ConfigFile file =
				CurrentFile();

			if (file == null ||
				file.Blocks.Count == 0)
			{
				SetStatus(
					"No config selected.");

				return;
			}

			try
			{
				string text =
					file.OriginalText;

				List<ConfigBlock> blocks =
					file.Blocks;

				List<Tuple<int, int, string>> replacements =
					new List<Tuple<int, int, string>>();

				foreach (ConfigBlock block
						 in blocks)
				{
					ConfigNode nodeToWrite =
						block.Node.nodes != null &&
						block.Node.nodes.Count > 0
							? block.Node.nodes[0]
							: block.Node;

					replacements.Add(
						Tuple.Create(
							block.Start,
							block.End,
							nodeToWrite.ToString()));
				}

				replacements.Sort(
					delegate (
						Tuple<int, int, string> a,
						Tuple<int, int, string> b)
					{
						return b.Item1.CompareTo(
							a.Item1);
					});

				foreach (Tuple<int, int, string> replacement
						 in replacements)
				{
					text =
						text.Substring(
							0,
							replacement.Item1) +
						replacement.Item3 +
						text.Substring(
							replacement.Item2);
				}

				string temporaryPath =
					file.FullPath +
					".msr_tmp";

				File.WriteAllText(
					temporaryPath,
					text);

				if (File.Exists(file.FullPath))
				{
					File.Replace(
						temporaryPath,
						file.FullPath,
						null);
				}
				else
				{
					File.Move(
						temporaryPath,
						file.FullPath);
				}

				file.OriginalText =
					text;

				ScanFiles();

				SetStatus(
					"Saved: " +
					file.DisplayPath);
			}
			catch (Exception ex)
			{
				Debug.LogError(
					"[MultiStarRings] Save failed: " +
					ex);

				SetStatus(
					"Save failed.");
			}
		}

		private void SetStatus(
			string text)
		{
			_status = text;
			_statusTimer = 4f;
		}
	}
}
