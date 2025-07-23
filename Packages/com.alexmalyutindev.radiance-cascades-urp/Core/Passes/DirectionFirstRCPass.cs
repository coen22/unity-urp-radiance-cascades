using System;
using InternalBridge;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace AlexMalyutinDev.RadianceCascades
{
    public class DirectionFirstRCPass : ScriptableRenderPass, IDisposable
    {
        private readonly RadianceCascadesDirectionFirstCS _compute;
        private RTHandle _cascade0;
        private RTHandle _radianceSH;
        private RTHandle _intermediateBuffer;
        private RTHandle _intermediateBuffer2;

        private readonly Material _blitMaterial;
        private readonly RadianceCascadesRenderingData _renderingData;

        public DirectionFirstRCPass(
            RadianceCascadeResources resources,
            RadianceCascadesRenderingData renderingData
        )
        {
            profilingSampler = new ProfilingSampler("RadianceCascades.DirectionFirst");
            _compute = new RadianceCascadesDirectionFirstCS(resources.RadianceCascadesDirectionalFirstCS);
            // TODO: Make proper C# wrapper for Blit/Combine shader!
            _blitMaterial = resources.BlitMaterial;
            _renderingData = renderingData;
        }

        public override void Configure(CommandBuffer cmd, RenderTextureDescriptor cameraTextureDescriptor)
        {
            // 512 => 512 / 8 = 64 probes in row
            // TODO: Allocate texture with dimension (screen.width, screen.height) * 2 
            int cascadeWidth = 2048; // cameraTextureDescriptor.width; // 2048; // 
            int cascadeHeight = 1024; // cameraTextureDescriptor.height; // 1024; // 
            var desc = new RenderTextureDescriptor(cascadeWidth, cascadeHeight)
            {
                colorFormat = RenderTextureFormat.ARGBFloat,
                sRGB = false,
                enableRandomWrite = true,
            };
            RenderingUtils.ReAllocateIfNeeded(ref _cascade0, desc, name: "RadianceCascades");

            desc = new RenderTextureDescriptor(cascadeWidth / 2, cascadeHeight / 2)
            {
                colorFormat = RenderTextureFormat.ARGBFloat,
                sRGB = false,
                enableRandomWrite = true,
            };
            RenderingUtils.ReAllocateIfNeeded(ref _radianceSH, desc, name: "RadianceSH");

            desc = new RenderTextureDescriptor(cameraTextureDescriptor.width / 2, cameraTextureDescriptor.height / 2)
            {
                colorFormat = RenderTextureFormat.ARGBFloat,
                sRGB = false,
            };
            RenderingUtils.ReAllocateIfNeeded(ref _intermediateBuffer, desc, name: "RadianceBuffer");
        }

        public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
        {
            var radianceCascades = VolumeManager.instance.stack.GetComponent<RadianceCascades>();

            var renderer = renderingData.cameraData.renderer;
            var colorBuffer = renderer.cameraColorTargetHandle;
            var depthBuffer = renderer.cameraDepthTargetHandle;

            var cmd = CommandBufferPool.Get();

            using (new ProfilingScope(cmd, profilingSampler))
            {
                context.ExecuteCommandBuffer(cmd);
                cmd.Clear();

                _compute.RenderMerge(
                    cmd,
                    ref renderingData.cameraData,
                    depthBuffer,
                    _renderingData.MinMaxDepth,
                    _renderingData.VarianceDepth,
                    _renderingData.BlurredColorBuffer,
                    radianceCascades.RayScale.value,
                    ref _cascade0
                );

                if (!radianceCascades.UseSH.value)
                {
                    cmd.BeginSample("RadianceCascade.Combine");
                    {
                        cmd.SetRenderTarget(_intermediateBuffer);
                        cmd.SetGlobalTexture(ShaderIds.GBuffer3, renderingData.cameraData.renderer.GetGBuffer(3));
                        cmd.SetGlobalTexture(ShaderIds.MinMaxDepth, _renderingData.MinMaxDepth);
                        BlitUtils.BlitTexture(cmd, _cascade0, _blitMaterial, 2);

                        cmd.SetRenderTarget(
                            colorBuffer,
                            RenderBufferLoadAction.Load,
                            RenderBufferStoreAction.Store,
                            depthBuffer,
                            RenderBufferLoadAction.Load,
                            RenderBufferStoreAction.Store
                        );
                        BlitUtils.BlitTexture(cmd, _intermediateBuffer, _blitMaterial, 3);
                    }
                    cmd.EndSample("RadianceCascade.Combine");
                }
                else
                {
                    // TODO: Combine into SH.
                    _compute.CombineSH(
                        cmd,
                        ref renderingData.cameraData,
                        _cascade0,
                        _renderingData.MinMaxDepth,
                        _renderingData.VarianceDepth,
                        _radianceSH
                    );

                    cmd.BeginSample("RadianceCascade.BlitSH");
                    cmd.SetRenderTarget(
                        colorBuffer,
                        RenderBufferLoadAction.Load,
                        RenderBufferStoreAction.Store,
                        depthBuffer,
                        RenderBufferLoadAction.Load,
                        RenderBufferStoreAction.Store
                    );
                    cmd.SetGlobalMatrix("_ViewToWorld", renderingData.cameraData.GetViewMatrix().inverse);
                    cmd.SetGlobalTexture("_MinMaxDepth", _renderingData.MinMaxDepth);
                    BlitUtils.BlitTexture(cmd, _radianceSH, _blitMaterial, 4);
                    cmd.EndSample("RadianceCascade.BlitSH");
                }
            }

            context.ExecuteCommandBuffer(cmd);
            cmd.Clear();
            CommandBufferPool.Release(cmd);
        }

#if UNITY_6000_1_OR_NEWER
        private class PassData
        {
            public RenderingData renderingData;
            public DirectionFirstRCPass pass;
            public TextureHandle color;
            public TextureHandle depth;
        }

        internal void ExecutePass(in RasterGraphContext ctx, ref RenderingData renderingData, TextureHandle color, TextureHandle depth)
        {
            var cmd = ctx.cmd;
            var radianceCascades = VolumeManager.instance.stack.GetComponent<RadianceCascades>();

            var colorBuffer = color.rt;
            var depthBuffer = depth.rt;

            using (new ProfilingScope(cmd, profilingSampler))
            {
                _compute.RenderMerge(
                    cmd,
                    ref renderingData.cameraData,
                    depthBuffer,
                    _renderingData.MinMaxDepth,
                    _renderingData.VarianceDepth,
                    _renderingData.BlurredColorBuffer,
                    radianceCascades.RayScale.value,
                    ref _cascade0
                );

                if (!radianceCascades.UseSH.value)
                {
                    cmd.BeginSample("RadianceCascade.Combine");
                    {
                        cmd.SetRenderTarget(_intermediateBuffer);
                        cmd.SetGlobalTexture(ShaderIds.GBuffer3, renderingData.cameraData.renderer.GetGBuffer(3));
                        cmd.SetGlobalTexture(ShaderIds.MinMaxDepth, _renderingData.MinMaxDepth);
                        BlitUtils.BlitTexture(cmd, _cascade0, _blitMaterial, 2);

                        cmd.SetRenderTarget(
                            colorBuffer,
                            RenderBufferLoadAction.Load,
                            RenderBufferStoreAction.Store,
                            depthBuffer,
                            RenderBufferLoadAction.Load,
                            RenderBufferStoreAction.Store
                        );
                        BlitUtils.BlitTexture(cmd, _intermediateBuffer, _blitMaterial, 3);
                    }
                    cmd.EndSample("RadianceCascade.Combine");
                }
                else
                {
                    _compute.CombineSH(
                        cmd,
                        ref renderingData.cameraData,
                        _cascade0,
                        _renderingData.MinMaxDepth,
                        _renderingData.VarianceDepth,
                        _radianceSH
                    );

                    cmd.BeginSample("RadianceCascade.BlitSH");
                    cmd.SetRenderTarget(
                        colorBuffer,
                        RenderBufferLoadAction.Load,
                        RenderBufferStoreAction.Store,
                        depthBuffer,
                        RenderBufferLoadAction.Load,
                        RenderBufferStoreAction.Store
                    );
                    cmd.SetGlobalMatrix("_ViewToWorld", renderingData.cameraData.GetViewMatrix().inverse);
                    cmd.SetGlobalTexture("_MinMaxDepth", _renderingData.MinMaxDepth);
                    BlitUtils.BlitTexture(cmd, _radianceSH, _blitMaterial, 4);
                    cmd.EndSample("RadianceCascade.BlitSH");
                }
            }
        }

        public void RecordRenderGraph(RenderGraph renderGraph, in RenderingData renderingData)
        {
            using var builder = renderGraph.AddRasterRenderPass<PassData>(nameof(DirectionFirstRCPass), out var passData);
            passData.renderingData = renderingData;
            passData.pass = this;
            passData.color = renderGraph.ImportTexture(renderingData.cameraData.renderer.cameraColorTargetHandle);
            passData.depth = renderGraph.ImportTexture(renderingData.cameraData.renderer.cameraDepthTargetHandle);

            builder.ReadWriteTexture(passData.color);
            builder.ReadWriteTexture(passData.depth);

            builder.SetRenderFunc(static (PassData data, RasterGraphContext ctx) =>
            {
                data.pass.ExecutePass(ctx, ref data.renderingData, data.color, data.depth);
            });
        }
#endif

        public void Dispose()
        {
            _cascade0?.Release();
        }
    }
}
