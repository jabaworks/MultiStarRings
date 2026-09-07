using System;
using UnityEngine;

namespace MultiStarRings
{
    [KSPAddon(KSPAddon.Startup.Flight, false)]
    public class MultiStarRingsUpdate : MonoBehaviour
    {
        private float _updateTimer = 0f;
        private float _updateInterval = 0.1f; // Update 10 times per second
        
        private void Start()
        {
            Debug.Log("[MultiStarRings] Update component started");
            _updateInterval = ConfigLoader.LoadConfig()?.Global.updateInterval ?? 0.1f;
            
            // Initial update
            try
            {
                MultiStarRingsCore.UpdateRings();
            }
            catch (Exception ex)
            {
                Debug.LogError($"[MultiStarRings] Initial update failed: {ex}");
            }
        }
        
        private void Update()
        {
            _updateTimer += Time.deltaTime;
            
            if (_updateTimer >= _updateInterval)
            {
                _updateTimer = 0f;
                
                try
                {
                    MultiStarRingsCore.UpdateRings();
                }
                catch (Exception ex)
                {
                    Debug.LogError($"[MultiStarRings] Update failed: {ex}");
                }
            }
        }
        
        private void OnDestroy()
        {
            Debug.Log("[MultiStarRings] Update component destroyed");
        }
    }
    
    [KSPAddon(KSPAddon.Startup.TrackingStation, false)]
    public class MultiStarRingsUpdateTracking : MultiStarRingsUpdate
    {
    }
    
    [KSPAddon(KSPAddon.Startup.SpaceCentre, false)]
    public class MultiStarRingsUpdateSpaceCenter : MultiStarRingsUpdate
    {
    }
}
