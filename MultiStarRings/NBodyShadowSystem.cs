using System;
using System.Collections.Generic;
using System.Linq;
using Kopernicus.Components;
using UnityEngine;

namespace MultiStarRings
{
    public class NBodyShadowData
    {
        public string name;
        public Vector3d position;
        public double radius;
        public float shadowIntensity;
        public float softness;
        public bool enabled;
        public CelestialBody body;

        public static NBodyShadowData FromConfig(ShadowCasterItem config, CelestialBody body)
        {
            return new NBodyShadowData
            {
                name = config.name,
                radius = config.radius,
                shadowIntensity = config.shadowIntensity,
                softness = config.softness,
                enabled = config.enabled,
                body = body,
                position = body != null ? body.position : Vector3d.zero
            };
        }

        public static NBodyShadowData FromBody(CelestialBody body)
        {
            return new NBodyShadowData
            {
                name = body.name,
                radius = body.Radius,
                shadowIntensity = 1.0f,
                softness = 0.15f,
                enabled = true,
                body = body,
                position = body.position
            };
        }
    }

    public static class NBodyShadowSystem
    {
        private static MultiStarRingsConfig _config;
        private static Dictionary<string, NBodyShadowData> _shadowCasters = new Dictionary<string, NBodyShadowData>();
        private static Dictionary<string, List<string>> _ringShadowMappings = new Dictionary<string, List<string>>();
        private static bool _initialized = false;
        private static float _lastUpdate = 0f;

        // Cached results for GetShadowCastersForRing so it can return a
        // reference instead of allocating a new List every call, every
        // ring, every tick. Rebuilt whenever _shadowCasters/_ringShadowMappings
        // change (i.e. once per Initialize()/Reset()), not on the hot path.
        private static readonly List<NBodyShadowData> _allCastersCache = new List<NBodyShadowData>();
        private static readonly Dictionary<string, List<NBodyShadowData>> _resolvedRingCastersCache =
            new Dictionary<string, List<NBodyShadowData>>();

        public static void Reset()
        {
            _config = null;
            _shadowCasters.Clear();
            _ringShadowMappings.Clear();
            _allCastersCache.Clear();
            _resolvedRingCastersCache.Clear();
            _initialized = false;
            _lastUpdate = 0f;
        }

        private static void RebuildCasterCaches()
        {
            _allCastersCache.Clear();
            _allCastersCache.AddRange(_shadowCasters.Values);

            _resolvedRingCastersCache.Clear();

            foreach (var kvp in _ringShadowMappings)
            {
                var resolved = new List<NBodyShadowData>();

                foreach (var name in kvp.Value)
                {
                    if (_shadowCasters.TryGetValue(name, out NBodyShadowData data))
                    {
                        resolved.Add(data);
                    }
                }

                _resolvedRingCastersCache[kvp.Key] = resolved;
            }
        }

        public static void Initialize()
        {
            if (_initialized)
                return;

            _config = ConfigLoader.LoadConfig();
            
            if (_config == null)
            {
                Debug.LogWarning("[NBodyShadowSystem] No config found, using auto-detection");
                AutoDetectShadowCasters();
                RebuildCasterCaches();
                _initialized = true;
                return;
            }

            LoadShadowCasters();
            LoadRingMappings();
            RebuildCasterCaches();
            _initialized = true;
            
            LogInfo($"Initialized with {_shadowCasters.Count} shadow casters");
        }

        private static void LoadShadowCasters()
        {
            foreach (var config in _config.ShadowCasters.Item)
            {
                if (!config.enabled) continue;
                
                CelestialBody body = FindBody(config.name);
                if (body == null)
                {
                    LogWarning($"Shadow caster '{config.name}' not found");
                    continue;
                }

                var data = NBodyShadowData.FromConfig(config, body);
                _shadowCasters[config.name] = data;
                LogInfo($"Loaded shadow caster: {config.name} (radius: {config.radius:N0}m)");
            }
        }

        private static void AutoDetectShadowCasters()
        {
            if (FlightGlobals.Bodies == null)
                return;

            foreach (var body in FlightGlobals.Bodies)
            {
                if (body == null) continue;

                // Prefer bodies that actually have a Ring component (Kopernicus)
                bool hasRings = body.GetComponentInChildren<Kopernicus.Components.Ring>() != null;

                // Also treat very large bodies (gas giants / stars) as potential shadow casters
                bool isLarge = body.Radius > 1000000;

                if (hasRings || isLarge)
                {
                    if (!_shadowCasters.ContainsKey(body.name))
                    {
                        var data = NBodyShadowData.FromBody(body);
                        _shadowCasters[body.name] = data;
                        LogInfo($"Auto-detected shadow caster: {body.name} (rings={hasRings}, large={isLarge})");
                    }
                }
            }
        }

        private static void LoadRingMappings()
        {
            foreach (var config in _config.RingShadows.Item)
            {
                if (!config.enabled) continue;
                
                var casters = config.shadowCasters.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
                var list = new List<string>();
                
                foreach (var caster in casters)
                {
                    string trimmed = caster.Trim();
                    if (_shadowCasters.ContainsKey(trimmed))
                    {
                        list.Add(trimmed);
                        LogInfo($"Ring '{config.ringBody}' casts shadow from '{trimmed}'");
                    }
                    else
                    {
                        LogWarning($"Shadow caster '{trimmed}' not found for ring '{config.ringBody}'");
                    }
                }
                
                _ringShadowMappings[config.ringBody] = list;
            }
        }

        private static CelestialBody FindBody(string name)
        {
            if (FlightGlobals.Bodies == null)
                return null;
            
            return FlightGlobals.Bodies.FirstOrDefault(b => b != null && b.name == name);
        }

        public static void Update()
        {
            if (!_initialized)
                Initialize();

            float now = Time.time;
            if (now - _lastUpdate < _config.Global.updateInterval)
                return;
            
            _lastUpdate = now;

            // Update shadow caster positions
            foreach (var kvp in _shadowCasters)
            {
                if (kvp.Value.body != null)
                {
                    kvp.Value.position = kvp.Value.body.position;
                }
            }
        }

        public static List<NBodyShadowData> GetShadowCastersForRing(string ringBodyName)
        {
            if (!_initialized)
                Initialize();

            if (_resolvedRingCastersCache.TryGetValue(ringBodyName, out List<NBodyShadowData> resolved))
            {
                return resolved;
            }

            return _allCastersCache;
        }

        public static bool IsBodyShadowCaster(string name)
        {
            return _shadowCasters.ContainsKey(name);
        }

        public static float GetShadowSoftness()
        {
            return _config?.Global.shadowSoftness ?? 0.15f;
        }

        public static int GetMaxShadowBodies()
        {
            int configured = _config?.Global.maxShadowBodies ?? 8;
            return Mathf.Clamp(configured, 0, 8);
        }

        private static void LogInfo(string message)
        {
            if (_config?.Debug.enabled == true && _config.Debug.logLevel == "Info")
                Debug.Log($"[NBodyShadowSystem] {message}");
        }

        private static void LogWarning(string message)
        {
            if (_config?.Debug.enabled == true && (_config.Debug.logLevel == "Warning" || _config.Debug.logLevel == "Info"))
                Debug.LogWarning($"[NBodyShadowSystem] {message}");
        }

        private static void LogVerbose(string message)
        {
            if (_config?.Debug.enabled == true && _config.Debug.logLevel == "Verbose")
                Debug.Log($"[NBodyShadowSystem] {message}");
        }
    }
}
