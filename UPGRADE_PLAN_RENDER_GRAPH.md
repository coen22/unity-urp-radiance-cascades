# TODO: Upgrade to URP 17 Render Graph API

The project currently uses custom ScriptableRenderPass classes that rely on the
legacy API. Upgrading to URP 17 requires migrating these passes to the new
Render Graph framework. Below is a high level plan of work required to perform
this upgrade.

## General steps
- [ ] Update the URP package in `Packages/manifest.json` to `com.unity.render-pipelines.universal` version `17.x` if not already using the latest patch.
- [ ] Review release notes and migration guides from Unity for URP 17 and the
  Render Graph API.
- [ ] Clean up obsolete API usage warnings (e.g. `Configure` and `Execute` methods
  in `ScriptableRenderPass`). These will be replaced with Render Graph render
  pass implementations.

## Pass migration
- [ ] Convert `RC2dPass`, `RC3dPass` and `DirectionFirstRCPass` to Render Graph
  passes. Each should implement a `RecordRenderGraph` method or equivalent.
- [ ] Replace direct calls to `CommandBuffer` setup with Render Graph resources
  (e.g. `textureHandle`, `RenderGraphBuilder`).
- [ ] Ensure render targets (camera color, depth, intermediate buffers) are
  declared via Render Graph `ReadWriteTexture`/`CreateTexture` descriptors.
- [ ] Expose `_GBuffer0` and any other buffers to the passes using Render Graph
  `ReadTexture`/`WriteTexture` semantics rather than manual `SetGlobalTexture`.
- [ ] Replace `Blitter.BlitTexture` and `BlitUtils.BlitTexture` usages with
  `RenderGraphUtils` alternatives if available.

## Pipeline integration
- [ ] Update the renderer feature registration to use `AddRenderPasses` that
  enqueues Render Graph passes.
- [ ] Verify the pass ordering so that radiance cascades are combined with the
  direct and forward lighting passes correctly.

## Testing
- [ ] Test scenes with forward and deferred rendering to ensure direct light is
  preserved when applying radiance cascades.
- [ ] Validate that build warnings related to obsolete API usage are resolved.
- [ ] Profile the render graph implementation for any regressions.
