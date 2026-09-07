# MultiStarRings
**Open the config manager with  Ctrl+R**

Replaces KSP's stock ring lighting with a shader built for Kopernicus systems that have more than one star. Instead of lighting a ring from one generic source, it works out which stars actually contribute the most light at that ring's body (or whichever ones you configure) and feeds the strongest ones into the shader, with proper attenuation, star colour, scattering, and occultation shadows.

## Requirements

- Kopernicus
- A GPU that can run Shader Model 3.0+

## Install

Drop the contents of the release zip into `GameData/`. That's it - it works with no config at all, using automatic shadow-caster detection.

## Configuration

Everything is standard ConfigNode syntax under a `MultiStarRings` node. `Global`/`Debug` settings live only in the mod's own `MultiStarRings.cfg`; everything else (`ShadowCasters`, `RingShadows`, `RingBrightness`, `RingLights`) can be added by any patch-style cfg and gets merged automatically.

There's also an in-game debug UI for browsing/editing configs and generating new patch files without hand-writing the ConfigNode syntax.

Here's an example from my own unreleased planet pack - Aeolus is a gas giant with a few moons.

```
MultiStarRings
{
	RingBrightness
	{
		Item
		{
			ringBody = Aeolus
			compressionExponent = 0.4
			anisotropy = 0.4
			scatteringPower = 1
			scatteringStrength = 1.8
			ambientScatter = 0.15
			ceiling = 1
			enabled = true
			UseDefaultShader = false
			glowBoost = 400 // multiplies the resulting illumination by whatever
			                // you put here - used here because Aeolus is far
			                // enough out that the light would otherwise be too dim
		}
	}
	RingLights
	{
		Item
		{
			ringBody = Aeolus
			stars = Sun // you can set which stars illuminate a ring by hand
			enabled = true
		}
	}
	ShadowCasters
	{
		Item
		{
			name = Aeolus
			radius = 8000000
			shadowIntensity = 1.0 // radius/intensity/softness are the shadow-caster properties
			softness = 0.15
			enabled = true
		}
		Item
		{
			name = Craton
			radius = 700000
			shadowIntensity = 1.0
			softness = 0.15
			enabled = true
		}
		Item
		{
			name = Agni
			radius = 190000
			shadowIntensity = 1.0
			softness = 0.15
			enabled = true
		}
		Item
		{
			name = Ejecta
			radius = 190000
			shadowIntensity = 1.0
			softness = 0.15
			enabled = true
		}
		Item
		{
			name = Erro
			radius = 375000
			shadowIntensity = 1.0
			softness = 0.15
			enabled = true
		}
		Item
		{
			name = Collos
			radius = 460000
			shadowIntensity = 1.0
			softness = 0.15
			enabled = true
		}
	}
	RingShadows
	{
		Item
		{
			ringBody = Aeolus
			shadowCasters = Aeolus, Craton, Agni, Ejecta, Collos, Erro
			shadowIntensity = 1.0
			softness = 0.15
			enabled = true
		}
	}
}
```

## License

GPL-3.0-only. See [license.md](license.md).
