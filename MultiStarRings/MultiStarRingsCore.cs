using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Kopernicus.Components;
using UnityEngine;

namespace MultiStarRings
{
	public static class MultiStarRingsCore
	{
		private const int MaxLights = 4;
		private const double NegligibleFluxRatio = 0.01;
		private static double? _referenceFlux;
		private static float _lastLog = -999f;
		private const float LogInterval = 5f;

		private static readonly int SunPositionsId = Shader.PropertyToID("sunPositions");
		private static readonly int SunRadiiId = Shader.PropertyToID("sunRadii");
		private static readonly int SunWeightsId = Shader.PropertyToID("sunWeights");
		private static readonly int SunLuminositiesId = Shader.PropertyToID("sunLuminosities");
		private static readonly int SunFluxRatiosId = Shader.PropertyToID("sunFluxRatios");
		private static readonly int SunReferenceDistancesId = Shader.PropertyToID("sunReferenceDistances");
		private static readonly int ReferenceFluxId = Shader.PropertyToID("referenceFlux");
		private static readonly int SunColorsId = Shader.PropertyToID("sunColors");
		private static readonly int NumActiveLightsId = Shader.PropertyToID("numActiveLights");

		private const int MaxKeyframesPerLight = 20;
		private static readonly int StarKeyframeDistancesId = Shader.PropertyToID("starKeyframeDistances");
		private static readonly int StarKeyframeValuesId = Shader.PropertyToID("starKeyframeValues");
		private static readonly int StarKeyframeCountsId = Shader.PropertyToID("starKeyframeCounts");
		private static readonly int StarKeyframeInTangentsId = Shader.PropertyToID("starKeyframeInTangents");
		private static readonly int StarKeyframeOutTangentsId = Shader.PropertyToID("starKeyframeOutTangents");
		private static readonly int RingScaleFactorId = Shader.PropertyToID("_RingScaleFactor");
		private static readonly int RefBodyPosId = Shader.PropertyToID("_RefBodyPos");

		private static readonly int ShadowPositionsId = Shader.PropertyToID("shadowPositions");
		private static readonly int ShadowRadiiId = Shader.PropertyToID("shadowRadii");
		private static readonly int ShadowIntensitiesId = Shader.PropertyToID("shadowIntensities");
		private static readonly int ShadowSoftnessId = Shader.PropertyToID("shadowSoftness");
		private static readonly int NumShadowsId = Shader.PropertyToID("numShadows");
		private static readonly int ShadowQualityId = Shader.PropertyToID("shadowQuality");
		private static readonly int BrightnessCompressionExponentId = Shader.PropertyToID("brightnessCompressionExponent");
		private static readonly int BrightnessCeilingId = Shader.PropertyToID("brightnessCeiling");
		private static readonly int GlowBoostId = Shader.PropertyToID("glowBoost");
		private static readonly int AnisotropyId = Shader.PropertyToID("_Anisotropy");
		private static readonly int ScatteringPowerId = Shader.PropertyToID("_ScatteringPower");
		private static readonly int ScatteringStrengthId = Shader.PropertyToID("_ScatteringStrength");
		private static readonly int AmbientScatterId = Shader.PropertyToID("_AmbientScatter");

		private static AssetBundle _bundle;
		private static Shader _multiStarShader;
		private static bool _loadAttempted;

		private static readonly Dictionary<int, Material> _swappedMaterials =
			new Dictionary<int, Material>();

		private static readonly Dictionary<int, Material> _originalMaterials =
			new Dictionary<int, Material>();

		// Caches the reflected FieldInfo/PropertyInfo used to pull the
		// ring texture off a Kopernicus Ring instance, keyed by Type,
		// so GetRingTexture() only pays the reflection lookup cost once
		// per Ring type instead of once per ring per tick. A cached
		// null means "this type has none of the known texture members" -
		// still cached so we don't re-walk it every call either.
		private static readonly Dictionary<Type, MemberInfo> _texMemberCache =
			new Dictionary<Type, MemberInfo>();

		private static bool _shadowSystemInitialized = false;

		private static bool _cleanupHooked = false;

		// Reused every UpdateRings() call instead of being allocated
		// fresh per ring per tick. Safe because rings are processed
		// sequentially on the main thread and each buffer is fully
		// overwritten (or cleared) before use.
		private static readonly List<(KopernicusStar star, double flux)> _contributorsBuffer =
			new List<(KopernicusStar star, double flux)>();

		private static readonly List<(KopernicusStar star, double flux)> _activeBuffer =
			new List<(KopernicusStar star, double flux)>();

		private static readonly Vector4[] _positionsBuffer = new Vector4[MaxLights];
		private static readonly float[] _radiiBuffer = new float[MaxLights];
		private static readonly float[] _luminositiesBuffer = new float[MaxLights];
		private static readonly float[] _fluxRatiosBuffer = new float[MaxLights];
		private static readonly float[] _referenceDistancesBuffer = new float[MaxLights];
		private static readonly Vector4[] _colorsBuffer = new Vector4[MaxLights];

		private static readonly float[] _starKeyframeDistancesBuffer =
			new float[MaxLights * MaxKeyframesPerLight];
		private static readonly float[] _starKeyframeValuesBuffer =
			new float[MaxLights * MaxKeyframesPerLight];
		private static readonly float[] _starKeyframeInTangentsBuffer =
			new float[MaxLights * MaxKeyframesPerLight];
		private static readonly float[] _starKeyframeOutTangentsBuffer =
			new float[MaxLights * MaxKeyframesPerLight];
		private static readonly float[] _starKeyframeCountsBuffer =
			new float[MaxLights];

		private static readonly Vector4[] _shadowPositionsBuffer = new Vector4[8];
		private static readonly float[] _shadowRadiiBuffer = new float[8];
		private static readonly float[] _shadowIntensitiesBuffer = new float[8];

		// Tracks which Ring instance IDs were actually seen this update,
		// so stale _swappedMaterials/_originalMaterials entries left
		// behind by a Ring that was destroyed/recreated mid-session
		// (not on a scene load) can be pruned instead of leaking forever.
		private static readonly HashSet<int> _liveRingInstanceIdsBuffer = new HashSet<int>();

		private static void EnsureCleanupHooked()
		{
			if (_cleanupHooked)
				return;

			_cleanupHooked = true;

			GameEvents.onGameSceneLoadRequested.Add(OnSceneLoadRequested);
		}

		private static void OnSceneLoadRequested(GameScenes scene)
		{
			ClearMaterialCaches();

			// These are also per-game-session state and must not
			// survive a scene/save load, same as the material caches
			// above. Previously only the material caches were reset
			// here, which left stale flux calibration, shadow-caster
			// CelestialBody references, and a stale merged config
			// behind when switching saves or reloading.
			_referenceFlux = null;
			_shadowSystemInitialized = false;
			NBodyShadowSystem.Reset();
			ConfigLoader.Reset();
		}

		public static void ClearMaterialCaches()
		{
			foreach (Material mat in _swappedMaterials.Values)
			{
				if (mat != null)
					UnityEngine.Object.Destroy(mat);
			}

			_swappedMaterials.Clear();
			_originalMaterials.Clear();

			Debug.Log("[MultiStarRings] Cleared material caches on scene change");
		}

		// Loads a fresh copy of the asset bundle from disk and, only if
		// that succeeds, swaps it in and rebuilds every ring's material
		// against it. The old bundle/shader/materials are left completely
		// untouched until the new one is confirmed good, so a failed
		// reload (bad rebuild, file locked, etc.) leaves rings rendering
		// exactly as they were instead of going magenta with no way back
		// short of a restart.
		public static bool ReloadShaderBundle()
		{
			if (!TryLoadShaderBundle(out AssetBundle newBundle, out Shader newShader))
			{
				Debug.LogError(
					"[MultiStarRings] Shader bundle reload failed - keeping previous shader.");

				return false;
			}

			AssetBundle oldBundle = _bundle;

			_bundle = newBundle;
			_multiStarShader = newShader;
			_loadAttempted = true;

			// Every swapped material still points at the old bundle's
			// shader - destroy them so UpdateRings() rebuilds them all
			// against newShader on its next tick, then only now release
			// the old bundle. unloadAllLoadedObjects=false: the shader
			// object itself was already replaced by newShader above and
			// nothing references the old one anymore once the swapped
			// materials above are gone, so there's nothing left worth
			// keeping alive - but passing false here (rather than true)
			// avoids Unity forcibly tearing down anything still mid-use
			// this frame.
			ClearMaterialCaches();

			if (oldBundle != null)
			{
				oldBundle.Unload(false);
			}

			Debug.Log(
				"[MultiStarRings] Shader bundle reloaded successfully.");

			return true;
		}

		private static double GetReferenceFlux(List<KopernicusStar> stars)
		{
			if (_referenceFlux.HasValue)
				return _referenceFlux.Value;

			CelestialBody homeBody = FlightGlobals.GetHomeBody();

			if (homeBody == null ||
				homeBody.referenceBody == null)
			{
				return double.NaN;
			}

			CelestialBody homeStarBody =
				homeBody.referenceBody;

			KopernicusStar homeStar =
				stars.FirstOrDefault(
					s =>
						s != null &&
						s.sun != null &&
						s.sun.name == homeStarBody.name);

			if (homeStar == null ||
				homeStar.shifter == null ||
				homeStar.shifter.solarLuminosity <= 0.0)
			{
				return double.NaN;
			}

			Vector3d delta =
				LiveBodyLookup(homeBody).position -
				homeStar.sun.position;

			double sqrMagnitude =
				delta.sqrMagnitude;

			if (sqrMagnitude <= 0.0)
				return double.NaN;

			double flux =
				homeStar.shifter.solarLuminosity /
				sqrMagnitude;

			_referenceFlux = flux;

			Debug.Log(
				$"[MultiStarRings] Calibrated reference flux: {flux:E4}");

			return flux;
		}

		private static void EnsureShaderLoaded()
		{
			if (_loadAttempted)
				return;

			_loadAttempted = true;

			if (TryLoadShaderBundle(out AssetBundle bundle, out Shader shader))
			{
				_bundle = bundle;
				_multiStarShader = shader;
			}
		}

		// Loads the asset bundle from disk and pulls the ring shader out
		// of it, without touching _bundle/_multiStarShader - so a failed
		// attempt (bad file, missing shader, etc.) never disturbs whatever
		// is already loaded and currently in use. Caller decides what to
		// do with the result.
		private static bool TryLoadShaderBundle(
			out AssetBundle bundle,
			out Shader shader)
		{
			bundle = null;
			shader = null;

			try
			{
				string path =
					KSPUtil.ApplicationRootPath +
					"GameData/MultiStarRings/multistarrings.unity3d";

				AssetBundle loadedBundle =
					AssetBundle.LoadFromFile(path);

				if (loadedBundle == null)
				{
					Debug.LogError(
						$"[MultiStarRings] Failed to load asset bundle at {path}");

					return false;
				}

				Shader[] shaders =
					loadedBundle.LoadAllAssets<Shader>();

				if (shaders == null ||
					shaders.Length == 0)
				{
					Debug.LogError(
						"[MultiStarRings] Asset bundle contains no Shader assets.");

					loadedBundle.Unload(true);

					return false;
				}

				Shader foundShader =
					shaders.FirstOrDefault(
						s =>
							s != null &&
							s.name ==
							"MultiStarRings/RingsMultiStar");

				if (foundShader == null)
				{
					Debug.LogError(
						$"[MultiStarRings] Could not find shader 'MultiStarRings/RingsMultiStar' in bundle. Found: {string.Join(", ", shaders.Select(s => s != null ? s.name : "<null>"))}");

					loadedBundle.Unload(true);

					return false;
				}

				Debug.Log(
					$"[MultiStarRings] Loaded shader '{foundShader.name}'");

				bundle = loadedBundle;
				shader = foundShader;

				return true;
			}
			catch (Exception ex)
			{
				Debug.LogError(
					$"[MultiStarRings] Exception loading shader bundle: {ex}");

				return false;
			}
		}

		private static CelestialBody LiveBodyLookup(
			CelestialBody body)
		{
			if (FlightGlobals.Bodies != null)
			{
				CelestialBody live =
					FlightGlobals.Bodies.FirstOrDefault(
						b =>
							b != null &&
							b.name == body.name);

				if (live != null)
					return live;
			}

			return body;
		}

		private static readonly string[] PossibleTextureFieldNames =
		{
			"texture",
			"ringTexture",
			"mainTexture",
			"ringTex",
			"Texture"
		};

		// Resolves (once per Ring subtype, then cached forever) which
		// reflected member actually holds the ring texture. Returns
		// null - and caches that null - if the type has none of the
		// known members, so a type that doesn't match is only walked
		// once instead of on every call.
		private static MemberInfo ResolveTextureMember(Type ringType)
		{
			if (_texMemberCache.TryGetValue(ringType, out MemberInfo cached))
				return cached;

			MemberInfo found = null;

			foreach (string fieldName in PossibleTextureFieldNames)
			{
				FieldInfo field =
					ringType.GetField(
						fieldName,
						BindingFlags.Public |
						BindingFlags.NonPublic |
						BindingFlags.Instance);

				if (field != null)
				{
					found = field;
					break;
				}

				PropertyInfo prop =
					ringType.GetProperty(
						fieldName,
						BindingFlags.Public |
						BindingFlags.NonPublic |
						BindingFlags.Instance);

				if (prop != null)
				{
					found = prop;
					break;
				}
			}

			_texMemberCache[ringType] = found;

			return found;
		}

		// Cheap per-tick lookup: no reflection walk, just GetValue()
		// on the cached member (or a fast property read as fallback).
		// Safe/intended to be called every UpdateRings() tick per ring,
		// so a streamed/on-demand texture that gets swapped out from
		// under us (leaving the material holding a destroyed-object
		// reference) is picked up again instead of staying stuck on
		// whatever texture happened to be bound the first tick.
		private static Texture GetRingTexture(
			Ring ring,
			Material original)
		{
			MemberInfo member =
				ResolveTextureMember(ring.GetType());

			Texture tex = member switch
			{
				FieldInfo fi => fi.GetValue(ring) as Texture,
				PropertyInfo pi => pi.GetValue(ring, null) as Texture,
				_ => null
			};

			if (tex != null)
				return tex;

			if (original != null)
			{
				if (original.HasProperty("_MainTex"))
				{
					Texture origTex =
						original.GetTexture(
							"_MainTex");

					if (origTex != null)
						return origTex;
				}

				if (original.mainTexture != null)
					return original.mainTexture;
			}

			return null;
		}

		public static void UpdateRings()
		{
			EnsureShaderLoaded();
			EnsureCleanupHooked();

			if (_multiStarShader == null)
				return;

			if (!_shadowSystemInitialized)
			{
				NBodyShadowSystem.Initialize();
				_shadowSystemInitialized = true;
			}

			NBodyShadowSystem.Update();

			bool shouldLog =
				Time.time - _lastLog >= LogInterval;

			if (shouldLog)
				_lastLog = Time.time;

			try
			{
				List<KopernicusStar> stars =
					KopernicusStar.Stars;

				if (stars == null ||
					stars.Count == 0)
				{
					return;
				}

				double referenceFlux =
					GetReferenceFlux(stars);

				bool haveReference =
					!double.IsNaN(referenceFlux) &&
					referenceFlux > 0.0;

				Ring[] rings =
					UnityEngine.Object.FindObjectsOfType<Ring>();

				_liveRingInstanceIdsBuffer.Clear();

				foreach (Ring ring in rings)
				{
					if (ring == null ||
						ring.referenceBody == null ||
						ring.ringMr == null)
					{
						continue;
					}

					CelestialBody refBody =
						ring.referenceBody;

					var config =
						ConfigLoader.LoadConfig();

					CelestialBody liveRefBody =
						LiveBodyLookup(refBody);

					Vector3d refPosition =
						liveRefBody.position;

					Vector3 refScaledPosition =
						ScaledSpace.LocalToScaledSpace(
							refPosition);

					// Only match enabled entries here. Previously a
					// disabled RingBrightness entry (enabled=false)
					// was still found and its UseDefaultShader /
					// compressionExponent / ceiling values were
					// applied below - so "disabling" an override
					// didn't actually disable it everywhere.
					var brightnessConfig =
						config != null &&
						config.RingBrightness != null &&
						config.RingBrightness.Item != null
							? config.RingBrightness.Item.Find(
								r =>
									r != null &&
									r.ringBody == refBody.name &&
									r.enabled)
							: null;

					bool useDefaultShader =
						brightnessConfig != null &&
						brightnessConfig.UseDefaultShader;

					int instanceID =
						ring.GetInstanceID();

					_liveRingInstanceIdsBuffer.Add(instanceID);

					Material originalMaterial;

					if (!_originalMaterials.TryGetValue(
							instanceID,
							out originalMaterial) ||
						originalMaterial == null)
					{
						Material currentMaterial =
							ring.ringMr.sharedMaterial;

						if (currentMaterial != null &&
							currentMaterial.shader ==
							_multiStarShader)
						{
							Debug.LogWarning(
								$"[MultiStarRings] DIAG: ring for '{refBody.name}' " +
								$"(instanceID={instanceID}, innerRadius={TryGetRingFloat(ring, "innerRadius")}) " +
								"already has our shader bound on first sight - " +
								"originalMaterial capture will fail and this ring will bail every tick.");
						}
						else if (currentMaterial == null)
						{
							Debug.LogWarning(
								$"[MultiStarRings] DIAG: ring for '{refBody.name}' " +
								$"(instanceID={instanceID}, innerRadius={TryGetRingFloat(ring, "innerRadius")}) " +
								"has a null sharedMaterial on first sight - " +
								"originalMaterial capture will fail and this ring will bail every tick.");
						}

						if (currentMaterial != null &&
							currentMaterial.shader !=
							_multiStarShader)
						{
							originalMaterial =
								currentMaterial;

							_originalMaterials[instanceID] =
								originalMaterial;
						}
					}

					if (useDefaultShader)
					{
						if (originalMaterial != null &&
							ring.ringMr.sharedMaterial !=
							originalMaterial)
						{
							ring.ringMr.sharedMaterial =
								originalMaterial;
						}

						Material swapped;

						if (_swappedMaterials.TryGetValue(
								instanceID,
								out swapped))
						{
							if (swapped != null)
							{
								UnityEngine.Object.Destroy(
									swapped);
							}

							_swappedMaterials.Remove(
								instanceID);
						}

						continue;
					}

					if (originalMaterial == null)
					{
						if (shouldLog)
						{
							Debug.LogWarning(
								$"[MultiStarRings] Ring for '{refBody.name}' " +
								$"(instanceID={instanceID}, innerRadius={TryGetRingFloat(ring, "innerRadius")}) " +
								"has no original material (Kopernicus never assigned one - " +
								"seen on very thin rings) - building material directly from " +
								"the Ring component's own texture/color instead of skipping it.");
						}
					}

					Material mat;

					if (!_swappedMaterials.TryGetValue(
							instanceID,
							out mat) ||
						mat == null ||
						mat.shader != _multiStarShader)
					{
						mat =
							new Material(
								_multiStarShader);

						// originalMaterial may be null here (Kopernicus
						// never built one for this ring) - GetRingTexture
						// still works because it reads the texture off
						// the Ring component itself via reflection first,
						// only falling back to originalMaterial._MainTex
						// as a second option.
						Texture mainTex =
							GetRingTexture(
								ring,
								originalMaterial);

						if (mainTex != null)
						{
							mat.SetTexture(
								"_MainTex",
								mainTex);

							if (originalMaterial != null &&
								originalMaterial.HasProperty(
									"_MainTex"))
							{
								mat.SetTextureScale(
									"_MainTex",
									originalMaterial.GetTextureScale(
										"_MainTex"));

								mat.SetTextureOffset(
									"_MainTex",
									originalMaterial.GetTextureOffset(
										"_MainTex"));
							}
						}
						else if (shouldLog)
						{
							Debug.LogWarning(
								$"[MultiStarRings] No texture found for ring on {refBody.name}");
						}

						if (originalMaterial != null &&
							originalMaterial.HasProperty(
								"_Color"))
						{
							mat.SetColor(
								"_Color",
								originalMaterial.GetColor(
									"_Color"));
						}
						else
						{
							mat.SetColor(
								"_Color",
								Color.white);
						}

						ring.ringMr.sharedMaterial =
							mat;

						_swappedMaterials[instanceID] =
							mat;

						if (shouldLog)
						{
							Debug.Log(
								$"[MultiStarRings] Swapped material for '{refBody.name}' (texture: {(mainTex != null ? mainTex.name : "null")})");
						}
					}
					else if (ring.ringMr.sharedMaterial != mat)
					{
						ring.ringMr.sharedMaterial =
							mat;
					}

					if (mat == null)
						continue;

					// Refreshed every tick (cheap - ResolveTextureMember
					// is cached per Ring type, so this is just a GetValue()
					// plus an equality check most frames). Some texture
					// pipelines (on-demand/streamed 8K ring textures) can
					// destroy and replace the Texture object Kopernicus
					// handed us at first-build time. A destroyed Unity
					// object reference doesn't compare equal to null in
					// the normal sense, so a swapped material that only
					// ever set _MainTex once could keep pointing at a
					// dead texture indefinitely, rendering as flat/empty.
					// Re-resolving and only calling SetTexture when the
					// bound texture actually changed keeps this cheap
					// while fixing that case.
					Texture currentMainTex =
						GetRingTexture(
							ring,
							originalMaterial);

					if (currentMainTex != null &&
						mat.GetTexture("_MainTex") != currentMainTex)
					{
						mat.SetTexture(
							"_MainTex",
							currentMainTex);

						if (shouldLog)
						{
							Debug.Log(
								$"[MultiStarRings] Refreshed _MainTex for '{refBody.name}' (texture: {currentMainTex.name})");
						}
					}

					// If a RingLights entry exists (and is enabled) for
					// this ring body, only the star names it lists are
					// allowed to illuminate it - no automatic pickup of
					// whichever star happens to have the highest flux,
					// which previously let stars from unrelated systems
					// light rings they have no business touching. With
					// no entry, fall back to the old automatic behavior.
					var ringLightConfig =
						config != null &&
						config.RingLights != null &&
						config.RingLights.Item != null
							? config.RingLights.Item.Find(
								r =>
									r != null &&
									r.ringBody == refBody.name &&
									r.enabled)
							: null;

					HashSet<string> allowedStarNames = null;

					if (ringLightConfig != null &&
						!string.IsNullOrEmpty(ringLightConfig.stars))
					{
						allowedStarNames =
							new HashSet<string>(
								ringLightConfig.stars
									.Split(
										new[] { ',' },
										StringSplitOptions.RemoveEmptyEntries)
									.Select(s => s.Trim())
									.Where(s => !string.IsNullOrEmpty(s)));
					}

					List<(KopernicusStar star, double flux)> contributors =
						_contributorsBuffer;

					contributors.Clear();

					foreach (KopernicusStar star in stars)
					{
						if (star == null ||
							star.sun == null)
						{
							continue;
						}

						if (allowedStarNames != null &&
							!allowedStarNames.Contains(star.sun.name))
						{
							continue;
						}

						if (star.shifter == null ||
							!star.shifter.givesOffLight ||
							star.shifter.solarLuminosity <= 0.0)
						{
							continue;
						}

						Vector3d delta =
							refPosition -
							star.sun.position;

						double sqrMagnitude =
							delta.sqrMagnitude;

						if (sqrMagnitude <= 0.0)
						{
							double approxDist =
								refBody.Radius > 0.0
									? refBody.Radius * 0.1
									: 1000.0;

							sqrMagnitude =
								approxDist *
								approxDist;
						}

						double flux =
							star.shifter.solarLuminosity /
							sqrMagnitude;

						contributors.Add(
							(star, flux));
					}

					if (contributors.Count == 0)
						continue;

					contributors.Sort(
						(a, b) =>
							b.flux.CompareTo(a.flux));

					double strongestFlux =
						contributors[0].flux;

					List<(KopernicusStar star, double flux)> active =
						_activeBuffer;

					active.Clear();

					// Explicit RingLights entries still can't exceed
					// MaxLights - the shader-side arrays are fixed at
					// that size - but within that cap every allowed
					// star is used rather than only the strongest four
					// as in automatic mode. Flux still determines each
					// star's contribution strength via fluxRatios below.
					for (int c = 0;
						 c < contributors.Count &&
						 c < MaxLights;
						 c++)
					{
						active.Add(contributors[c]);
					}

					Vector4[] positions = _positionsBuffer;
					float[] radii = _radiiBuffer;
					float[] luminosities = _luminositiesBuffer;
					float[] fluxRatios = _fluxRatiosBuffer;
					float[] referenceDistances = _referenceDistancesBuffer;
					Vector4[] colors = _colorsBuffer;

					float[] starKeyframeDistances = _starKeyframeDistancesBuffer;
					float[] starKeyframeValues = _starKeyframeValuesBuffer;
					float[] starKeyframeInTangents = _starKeyframeInTangentsBuffer;
					float[] starKeyframeOutTangents = _starKeyframeOutTangentsBuffer;
					float[] starKeyframeCounts = _starKeyframeCountsBuffer;

					// These buffers are reused across rings/ticks, so any
					// slots not written below (e.g. active.Count < MaxLights,
					// or a light with fewer keyframes than a previous
					// occupant of the same slot) must be explicitly zeroed
					// rather than left holding stale data from a prior ring.
					Array.Clear(positions, 0, positions.Length);
					Array.Clear(radii, 0, radii.Length);
					Array.Clear(luminosities, 0, luminosities.Length);
					Array.Clear(fluxRatios, 0, fluxRatios.Length);
					Array.Clear(referenceDistances, 0, referenceDistances.Length);
					Array.Clear(colors, 0, colors.Length);
					Array.Clear(starKeyframeDistances, 0, starKeyframeDistances.Length);
					Array.Clear(starKeyframeValues, 0, starKeyframeValues.Length);
					Array.Clear(starKeyframeInTangents, 0, starKeyframeInTangents.Length);
					Array.Clear(starKeyframeOutTangents, 0, starKeyframeOutTangents.Length);
					Array.Clear(starKeyframeCounts, 0, starKeyframeCounts.Length);

					mat.SetVector(
						RefBodyPosId,
						new Vector4(
							refScaledPosition.x,
							refScaledPosition.y,
							refScaledPosition.z,
							0f));

					for (int i = 0;
						 i < active.Count;
						 i++)
					{
						KopernicusStar star =
							active[i].star;

						if (star.shifter != null &&
							star.shifter.intensityCurve != null &&
							star.shifter.intensityCurve.Curve != null)
						{
							Keyframe[] keys =
								star.shifter.intensityCurve.Curve.keys;

							int count =
								Math.Min(
									keys.Length,
									MaxKeyframesPerLight);

							starKeyframeCounts[i] =
								count;

							for (int k = 0;
								 k < count;
								 k++)
							{
								int index =
									i *
									MaxKeyframesPerLight +
									k;

								starKeyframeDistances[index] =
									keys[k].time;

								starKeyframeValues[index] =
									keys[k].value;

								starKeyframeInTangents[index] =
									keys[k].inTangent;

								starKeyframeOutTangents[index] =
									keys[k].outTangent;
							}
						}
						else
						{
							starKeyframeCounts[i] =
								0;
						}

						Vector3 starScaledPosition =
							ScaledSpace.LocalToScaledSpace(
								star.sun.position);

						Vector3 relativeScaledPosition =
							starScaledPosition -
							refScaledPosition;

						positions[i] =
							new Vector4(
								relativeScaledPosition.x,
								relativeScaledPosition.y,
								relativeScaledPosition.z,
								0f);

						radii[i] =
							ToScaledRadius(
								star.sun.position,
								star.sun.Radius);

						luminosities[i] =
							(float)star.shifter.solarLuminosity;

						double normalizedFlux;

						if (haveReference)
						{
							normalizedFlux =
								active[i].flux /
								referenceFlux;
						}
						else if (strongestFlux > 0.0)
						{
							normalizedFlux =
								active[i].flux /
								strongestFlux;
						}
						else
						{
							normalizedFlux =
								0.0;
						}

						fluxRatios[i] =
							(float)Math.Max(
								0.0,
								normalizedFlux);

						Vector3 relativeScaledForDistance =
							starScaledPosition -
							refScaledPosition;

						float referenceDistanceScaled =
							relativeScaledForDistance.magnitude;

						if (referenceDistanceScaled <=
							1e-8f)
						{
							double starRadius =
								Math.Max(
									star.sun.Radius,
									refBody.Radius);

							referenceDistanceScaled =
								(float)(starRadius /
								ScaledSpace.ScaleFactor);
						}

						referenceDistances[i] =
							Math.Max(
								referenceDistanceScaled,
								1e-8f);

						Color starColor =
							star.shifter.sunlightColor;

						colors[i] =
							new Vector4(
								starColor.r,
								starColor.g,
								starColor.b,
								1f);
					}

					mat.SetVectorArray(
						SunPositionsId,
						positions);

					mat.SetFloatArray(
						SunRadiiId,
						radii);

					// sunWeights / sunLuminosities are declared in
					// the shader's cbuffer but never read by it -
					// uploading them was wasted work every update.
					// luminosities[] is still computed above since
					// the debug log below reads luminosities[0].

					mat.SetFloatArray(
						SunFluxRatiosId,
						fluxRatios);

					mat.SetFloatArray(
						SunReferenceDistancesId,
						referenceDistances);

					mat.SetFloat(
						ReferenceFluxId,
						(float)(
							haveReference
								? referenceFlux
								: strongestFlux));

					mat.SetVectorArray(
						SunColorsId,
						colors);

					mat.SetInt(
						NumActiveLightsId,
						active.Count);

					mat.SetFloatArray(
						StarKeyframeDistancesId,
						starKeyframeDistances);

					mat.SetFloatArray(
						StarKeyframeValuesId,
						starKeyframeValues);

					mat.SetFloatArray(
						StarKeyframeCountsId,
						starKeyframeCounts);

					mat.SetFloatArray(
						StarKeyframeInTangentsId,
						starKeyframeInTangents);

					mat.SetFloatArray(
						StarKeyframeOutTangentsId,
						starKeyframeOutTangents);

					mat.SetFloat(
						RingScaleFactorId,
						(float)ScaledSpace.ScaleFactor);

					List<NBodyShadowData> shadowCasters =
						NBodyShadowSystem.GetShadowCastersForRing(
							refBody.name);

					int maxShadows =
						NBodyShadowSystem.GetMaxShadowBodies();

					int shadowCount =
						Math.Min(
							shadowCasters.Count,
							maxShadows);

					Vector4[] shadowPositions = _shadowPositionsBuffer;
					float[] shadowRadii = _shadowRadiiBuffer;
					float[] shadowIntensities = _shadowIntensitiesBuffer;

					Array.Clear(shadowPositions, 0, shadowPositions.Length);
					Array.Clear(shadowRadii, 0, shadowRadii.Length);
					Array.Clear(shadowIntensities, 0, shadowIntensities.Length);

					for (int i = 0;
						 i < shadowCount;
						 i++)
					{
						NBodyShadowData shadow =
							shadowCasters[i];

						Vector3 shadowScaledPosition =
							ScaledSpace.LocalToScaledSpace(
								shadow.position);

						Vector3 relativeScaledPosition =
							shadowScaledPosition -
							refScaledPosition;

						shadowPositions[i] =
							new Vector4(
								relativeScaledPosition.x,
								relativeScaledPosition.y,
								relativeScaledPosition.z,
								0f);

						shadowRadii[i] =
							ToScaledRadius(
								shadow.position,
								shadow.radius);

						shadowIntensities[i] =
							shadow.shadowIntensity;

						if (shouldLog && i == 0)
						{
							Vector3d physicalDelta =
								shadow.position -
								refPosition;

							Debug.Log(
								$"[MultiStarRings] Shadow caster '{shadow.name}' relative position: {physicalDelta.magnitude:N0}m, scaledRelative={relativeScaledPosition}, scaledRadius={shadowRadii[i]:E4}");
						}
					}

					mat.SetVectorArray(
						ShadowPositionsId,
						shadowPositions);

					mat.SetFloatArray(
						ShadowRadiiId,
						shadowRadii);

					mat.SetFloatArray(
						ShadowIntensitiesId,
						shadowIntensities);

					mat.SetInt(
						NumShadowsId,
						shadowCount);

					float softness =
						NBodyShadowSystem.GetShadowSoftness();

					if (config != null &&
						config.RingShadows != null &&
						config.RingShadows.Item != null)
					{
						var ringConfig =
							config.RingShadows.Item.Find(
								r =>
									r.ringBody ==
									refBody.name);

						if (ringConfig != null &&
							ringConfig.enabled)
						{
							softness =
								ringConfig.softness;
						}
					}

					mat.SetFloat(
						ShadowSoftnessId,
						softness);

					string quality =
						config != null &&
						config.Global != null
							? config.Global.shadowQuality
							: "High";

					int qualityValue =
						quality.ToLower() switch
						{
							"low" => 0,
							"medium" => 1,
							"high" => 2,
							"ultra" => 3,
							_ => 2
						};

					mat.SetInt(
						ShadowQualityId,
						qualityValue);

					// Per-body illumination multiplier. Independent of
					// compression - applies whenever a RingBrightness
					// entry is enabled for this body, defaulting to 1.0 (no
					// change) when there's no override.
					float glowBoost =
						brightnessConfig != null
							? brightnessConfig.glowBoost
							: 1.0f;

					mat.SetFloat(
						GlowBoostId,
						glowBoost);

					float compressionExponent =
						brightnessConfig != null
							? brightnessConfig.compressionExponent
							: config != null &&
							  config.Global != null
								? config.Global.brightnessCompressionExponent
								: 0.4f;

					float ceiling =
						brightnessConfig != null
							? brightnessConfig.ceiling
							: config != null &&
							  config.Global != null
								? config.Global.brightnessCeiling
								: 5.0f;

					mat.SetFloat(
						BrightnessCompressionExponentId,
						compressionExponent);

					mat.SetFloat(
						BrightnessCeilingId,
						ceiling);

					// Scattering/anisotropy knobs default to the shader's
					// own baked-in values (matching the Properties block)
					// when no Global config is loaded, so behavior is
					// unchanged unless the user actually sets these. A
					// per-ring RingBrightness override (if present and
					// not left at the "unset" sentinel) wins over Global.
					float anisotropy =
						brightnessConfig != null &&
						brightnessConfig.anisotropy != float.MinValue
							? brightnessConfig.anisotropy
							: config != null &&
							  config.Global != null
								? config.Global.anisotropy
								: 0.72f;

					float scatteringPower =
						brightnessConfig != null &&
						brightnessConfig.scatteringPower != float.MinValue
							? brightnessConfig.scatteringPower
							: config != null &&
							  config.Global != null
								? config.Global.scatteringPower
								: 2.5f;

					float scatteringStrength =
						brightnessConfig != null &&
						brightnessConfig.scatteringStrength != float.MinValue
							? brightnessConfig.scatteringStrength
							: config != null &&
							  config.Global != null
								? config.Global.scatteringStrength
								: 1.8f;

					float ambientScatter =
						brightnessConfig != null &&
						brightnessConfig.ambientScatter != float.MinValue
							? brightnessConfig.ambientScatter
							: config != null &&
							  config.Global != null
								? config.Global.ambientScatter
								: 0.15f;

					mat.SetFloat(
						AnisotropyId,
						anisotropy);

					mat.SetFloat(
						ScatteringPowerId,
						scatteringPower);

					mat.SetFloat(
						ScatteringStrengthId,
						scatteringStrength);

					mat.SetFloat(
						AmbientScatterId,
						ambientScatter);

					if (shouldLog)
					{
						Debug.Log(
							$"[MultiStarRings] Ring '{refBody.name}': {active.Count} lights, {shadowCount} shadows");

						for (int i = 0;
							 i < active.Count;
							 i++)
						{
							Debug.Log(
								$"[MultiStarRings] Light {i}: name={active[i].star.sun.name}, posScaled={positions[i]}, referenceDistanceScaled={referenceDistances[i]:E4}, fluxRatio={fluxRatios[i]:E4}");
						}

						if (shadowCount > 0)
						{
							Debug.Log(
								$"[MultiStarRings] Shadow 0: posScaled={shadowPositions[0]}, radiusScaled={shadowRadii[0]:E4}");
						}

						float sentCeiling =
							mat.HasProperty(
								BrightnessCeilingId)
								? mat.GetFloat(
									BrightnessCeilingId)
								: -1f;

						float sentCompExp =
							mat.HasProperty(
								BrightnessCompressionExponentId)
								? mat.GetFloat(
									BrightnessCompressionExponentId)
								: -1f;

						float sentRefFlux =
							mat.HasProperty(
								ReferenceFluxId)
								? mat.GetFloat(
									ReferenceFluxId)
								: -1f;

						float sentGlowBoost =
							mat.HasProperty(
								GlowBoostId)
								? mat.GetFloat(
									GlowBoostId)
								: -1f;

						float sentBrightness =
							mat.HasProperty(
								"_Brightness")
								? mat.GetFloat(
									"_Brightness")
								: -999f;

						float sentAlbedo =
							mat.HasProperty(
								"_AlbedoStrength")
								? mat.GetFloat(
									"_AlbedoStrength")
								: -999f;

						float? innerR =
							TryGetRingFloat(
								ring,
								"innerRadius");

						float? outerR =
							TryGetRingFloat(
								ring,
								"outerRadius");

						Debug.Log(
							$"[MultiStarRings] MATERIAL: glowBoost={sentGlowBoost:E4}, ceiling={sentCeiling:E4}, compExp={sentCompExp:E4}, referenceFlux={sentRefFlux:E4}, strongestFlux={strongestFlux:E4}, haveReference={haveReference}, _Brightness={sentBrightness}, _AlbedoStrength={sentAlbedo}");

						Debug.Log(
							$"[MultiStarRings] RING GEOM: innerRadius={(innerR.HasValue ? innerR.Value.ToString("N0") : "?")}, outerRadius={(outerR.HasValue ? outerR.Value.ToString("N0") : "?")}, luminosity0={(active.Count > 0 ? luminosities[0].ToString("E4") : "n/a")}");
					}
				}

				PruneStaleMaterialCacheEntries();
			}
			catch (Exception ex)
			{
				Debug.LogError(
					$"[MultiStarRings] Exception updating rings: {ex}");
			}
		}

		// Removes _swappedMaterials/_originalMaterials entries for Ring
		// instance IDs that weren't seen in this update's FindObjectsOfType
		// scan - i.e. the Ring was destroyed (and possibly recreated with
		// a new instance ID) mid-session, outside of a scene load. Without
		// this, those entries (and the Material a swapped one points to)
		// would sit in the dictionaries and never get cleaned up, since
		// ClearMaterialCaches() only runs on scene load.
		private static void PruneStaleMaterialCacheEntries()
		{
			List<int> staleIds = null;

			foreach (int instanceID in _originalMaterials.Keys)
			{
				if (!_liveRingInstanceIdsBuffer.Contains(instanceID))
				{
					(staleIds ??= new List<int>()).Add(instanceID);
				}
			}

			if (staleIds == null)
				return;

			foreach (int instanceID in staleIds)
			{
				_originalMaterials.Remove(instanceID);

				if (_swappedMaterials.TryGetValue(instanceID, out Material swapped))
				{
					if (swapped != null)
					{
						UnityEngine.Object.Destroy(swapped);
					}

					_swappedMaterials.Remove(instanceID);
				}
			}

			Debug.Log(
				$"[MultiStarRings] Pruned {staleIds.Count} stale ring material cache entr{(staleIds.Count == 1 ? "y" : "ies")}");
		}

		private static float ToScaledRadius(
			Vector3d center,
			double radius)
		{
			if (radius <= 0.0)
				return 0f;

			return (float)(
				radius /
				ScaledSpace.ScaleFactor);
		}

		private static float? TryGetRingFloat(
			object obj,
			string name)
		{
			try
			{
				var type =
					obj.GetType();

				var field =
					type.GetField(
						name,
						BindingFlags.Public |
						BindingFlags.Instance |
						BindingFlags.NonPublic);

				if (field != null)
				{
					return Convert.ToSingle(
						field.GetValue(obj));
				}

				var prop =
					type.GetProperty(
						name,
						BindingFlags.Public |
						BindingFlags.Instance |
						BindingFlags.NonPublic);

				if (prop != null)
				{
					return Convert.ToSingle(
						prop.GetValue(
							obj,
							null));
				}
			}
			catch
			{
			}

			return null;
		}
	}
}