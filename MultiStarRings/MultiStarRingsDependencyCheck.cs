using System;
using System.Linq;
using UnityEngine;

namespace MultiStarRings
{
    [KSPAddon(KSPAddon.Startup.MainMenu, true)]
    public class MultiStarRingsDependencyCheck : MonoBehaviour
    {
        // Lowest Kopernicus version this mod has been tested against.
        private static readonly Version MinKopernicusVersion =
            new Version(1, 12, 1, 0);

        private void Start()
        {
            CheckKopernicus();
        }

        private void CheckKopernicus()
        {
            var kopernicusAssembly =
                AssemblyLoader.loadedAssemblies
                    .FirstOrDefault(a => a.name == "Kopernicus");

            if (kopernicusAssembly == null)
            {
                Warn(
                    "Kopernicus was not found. MultiStarRings requires " +
                    "Kopernicus to function and will not do anything " +
                    "without it."
                );
                return;
            }

            Version installedVersion =
                kopernicusAssembly.assembly.GetName().Version;

            if (installedVersion < MinKopernicusVersion)
            {
                Warn(
                    $"Kopernicus {installedVersion} was found, but " +
                    $"MultiStarRings expects at least " +
                    $"{MinKopernicusVersion}. Rings may not behave " +
                    $"correctly. Please update Kopernicus."
                );
            }
        }

        private void Warn(string message)
        {
            Debug.LogWarning($"[MultiStarRings] {message}");

            ScreenMessages.PostScreenMessage(
                $"[MultiStarRings] {message}",
                10f,
                ScreenMessageStyle.UPPER_CENTER
            );
        }
    }
}
