async function loadParticles(options) {
  await loadFireworksPreset(tsParticles);

  await tsParticles.load(options);
}

const configs = { preset: "fireworks" };

loadParticles(configs);