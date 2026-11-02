# Chakra Vortex Particle Rig Design

## Goal

Create a Unity particle-effect rig that represents the seven main human chakras as colored spinning vortices aligned over a human form.

The effect should read correctly from both front and side views:

- From the front, each chakra appears as a glowing colored center over the body.
- From the side, each chakra appears roughly conical.
- Back-facing cones should appear to collect energy into the body.
- Front-facing cones should appear to emit energy outward from the body.
- Crown emits/receives vertically above the head.
- Root emits/receives vertically below the pelvis/base.

## Visual Direction

The rig uses seven chakra anchors:

1. Crown — white/violet
2. Third eye — purple
3. Throat — blue
4. Heart — green
5. Solar plexus/navel — yellow
6. Sacral — orange
7. Root — red

Each anchor has:

- A small colored core glow at the body center.
- A front cone particle system, moving away from the body.
- A back cone particle system, moving toward the body.
- A rotating ring/vortex cue at the cone mouth.

The five torso/head anchors use paired front/back cones. Crown and root use vertical cone orientation so they still read like chakra funnels from the side.

## Unity Implementation Shape

Create a prefab or scene object named `ChakraVortexRig`.

Hierarchy:

```text
ChakraVortexRig
  Crown
    CoreGlow
    VerticalVortex
  ThirdEye
    CoreGlow
    FrontEmissionCone
    BackCollectionCone
  Throat
    CoreGlow
    FrontEmissionCone
    BackCollectionCone
  Heart
    CoreGlow
    FrontEmissionCone
    BackCollectionCone
  SolarPlexus
    CoreGlow
    FrontEmissionCone
    BackCollectionCone
  Sacral
    CoreGlow
    FrontEmissionCone
    BackCollectionCone
  Root
    CoreGlow
    VerticalVortex
```

Particle behavior:

- Cones use Shape = Cone.
- Front cones emit outward from the body.
- Back cones move inward toward the body.
- Particles are translucent, additive-looking, and color-matched to the chakra.
- Cone systems rotate slowly to communicate vortex motion.
- Optional ring particles at the wide mouth reinforce the spinning funnel.

## Placement Assumption

The initial rig is a standalone object in the active scene, sized for preview rather than bound to a tracked avatar. It can later be attached to a humanoid/avatar root or positioned in front of the XR camera.

## Success Criteria

- The Unity scene contains a visible chakra vortex rig.
- The visual clearly uses conical vortex forms, not just dots or spheres.
- Rear vortices imply collection into the body.
- Front vortices imply emission away from the body.
- The effect is made from regular Unity particle systems/materials so it can be inspected and adjusted in the Editor.

## Out of Scope for First Pass

- Runtime attachment to a real tracked body skeleton.
- Hand-authored chakra symbol textures.
- Audio reactivity.
- Voice-command spawning.
- Quest performance optimization beyond using modest particle counts.
