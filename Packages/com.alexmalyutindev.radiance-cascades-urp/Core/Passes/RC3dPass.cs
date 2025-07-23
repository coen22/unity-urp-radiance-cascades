using System;
using AlexMalyutinDev.RadianceCascades;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
#if UNITY_6000_1_OR_NEWER
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
#endif

public class RadianceCascades3dPass : ScriptableRenderPass, IDisposable
{
    private const int CascadesCount = 5;
    private static readonly string[] Cascade3dNames = GenNames("_Cascade", CascadesCount);

    private readonly ProfilingSampler _profilingSampler;

    private readonly RadianceCascadesRenderingData _radianceCascadesRenderingData;
    private readonly Material _blitMaterial;
    private readonly RadianceCascadeCubeMapCS _radianceCascade;

    private readonly RTHandle[] _cascades = new RTHandle[CascadesCount];

    public RadianceCascades3dPass(
        RadianceCascadeResources resources,
        RadianceCascadesRenderingData radianceCascadesRenderingData
    )
    {
        _profilingSampler = new ProfilingSampler(nameof(RC2dPass));
        _radianceCascade = new RadianceCascadeCubeMapCS(resources.RadianceCascades3d);
        _radianceCascadesRenderingData = radianceCascadesRenderingData;
        _blitMaterial = resources.BlitMaterial;
    }

    [Obsolete("Use RecordRenderGraph", true)]
    public override void Configure(CommandBuffer cmd, RenderTextureDescriptor cameraTextureDescriptor)
    {
        const int scale = 4;
        var decs = new RenderTextureDescriptor(
            (2 << CascadesCount) * 2 * scale,
            (1 << CascadesCount) * 3 * scale
        )
        {
            colorFormat = RenderTextureFormat.ARGBHalf,
            enableRandomWrite = true,
            mipCount = 0,
            depthBufferBits = 0,
            depthStencilFormat = GraphicsFormat.None,
        };
        for (int i = 0; i < _cascades.Length; i++)
        {
            RenderingUtils.ReAllocateIfNeeded(
                ref _cascades[i],
                decs,
                name: Cascade3dNames[i],
                filterMode: FilterMode.Point,
                wrapMode: TextureWrapMode.Clamp
            );
        }
    }

    [Obsolete("Use RecordRenderGraph", true)]
    public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
    {
        var cmd = CommandBufferPool.Get();

        var colorTexture = renderingData.cameraData.renderer.cameraColorTargetHandle;
        var depthTexture = renderingData.cameraData.renderer.cameraDepthTargetHandle;

        var colorTextureRT = colorTexture.rt;
        if (colorTextureRT == null)
        {
            return;
        }

        using (new ProfilingScope((CommandBuffer)cmd, _profilingSampler))
        {
            context.ExecuteCommandBuffer(cmd);
            cmd.Clear();

            Render(cmd, ref renderingData, colorTexture, depthTexture);
        }

        context.ExecuteCommandBuffer(cmd);
        cmd.Clear();

        CommandBufferPool.Release(cmd);
    }

    private void Render(
        CommandBuffer cmd,
        ref RenderingData renderingData,
        RTHandle colorTexture,
        RTHandle depthTexture
    )
    {
        var sampleKey = "RenderCascades";
        cmd.BeginSample(sampleKey);
        {
            for (int level = 0; level < _cascades.Length; level++)
            {
                _radianceCascade.RenderCascade(
                    cmd,
                    ref renderingData,
                    _radianceCascadesRenderingData,
                    colorTexture,
                    depthTexture,
                    2 << level,
                    level,
                    _cascades[level]
                );
            }
        }
        cmd.EndSample(sampleKey);

        sampleKey = "MergeCascades";
        cmd.BeginSample(sampleKey);
        {
            for (int level = _cascades.Length - 1; level > 0; level--)
            {
                _radianceCascade.MergeCascades(
                    cmd,
                    _cascades[level - 1],
                    _cascades[level],
                    level - 1
                );
            }
        }
        cmd.EndSample(sampleKey);

        sampleKey = "Combine";
        cmd.BeginSample(sampleKey);
        cmd.SetRenderTarget(
            colorTexture,
            RenderBufferLoadAction.Load,
            RenderBufferStoreAction.Store,
            depthTexture,
            RenderBufferLoadAction.Load,
            RenderBufferStoreAction.Store
        );
        cmd.SetGlobalTexture("_GBuffer0", colorTexture);
        BlitUtils.BlitTexture(cmd, _cascades[0], _blitMaterial, 1);
        cmd.EndSample(sampleKey);
    }

#if UNITY_6000_1_OR_NEWER
    private class PassData
    {
        public RenderingData renderingData;
        public RadianceCascades3dPass pass;
        public TextureHandle color;
        public TextureHandle depth;
        public TextureHandle[] cascades;
    }

    internal void ExecutePass(in RasterGraphContext ctx, ref RenderingData renderingData, TextureHandle color, TextureHandle depth, TextureHandle[] cascades)
    {
        var cmd = ctx.cmd;
        var colorTextureRT = color.rt;
        if (colorTextureRT == null)
            return;

        using (new ProfilingScope((CommandBuffer)cmd, _profilingSampler))
        {
            RenderRG(ctx, cmd, ref renderingData, color, depth, cascades);
        }
    }

    private void RenderRG(
        in RasterGraphContext ctx,
        CommandBuffer cmd,
        ref RenderingData renderingData,
        TextureHandle color,
        TextureHandle depth,
        TextureHandle[] cascades)
    {
        var colorRT = color.rt;
        var depthRT = depth.rt;

        for (int level = 0; level < cascades.Length; level++)
        {
            var target = cascades[level].rt;
            _radianceCascade.RenderCascade(cmd, ref renderingData, _radianceCascadesRenderingData, colorRT, depthRT, 2 << level, level, target);
        }

        for (int level = cascades.Length - 1; level > 0; level--)
        {
            var lower = cascades[level - 1].rt;
            var upper = cascades[level].rt;
            _radianceCascade.MergeCascades(cmd, lower, upper, level - 1);
        }

        cmd.SetRenderTarget(
            colorRT,
            RenderBufferLoadAction.Load,
            RenderBufferStoreAction.Store,
            depthRT,
            RenderBufferLoadAction.Load,
            RenderBufferStoreAction.Store
        );
        cmd.SetGlobalTexture("_GBuffer0", colorRT);
        RenderGraphUtils.BlitTexture(ctx, cascades[0], _blitMaterial, 1);
    }

    public void RecordRenderGraph(RenderGraph renderGraph, in RenderingData renderingData)
    {
        using var builder = renderGraph.AddRasterRenderPass<PassData>(nameof(RadianceCascades3dPass), out var passData);
        passData.renderingData = renderingData;
        passData.pass = this;

        passData.color = renderGraph.ImportTexture(renderingData.cameraData.renderer.cameraColorTargetHandle);
        passData.depth = renderGraph.ImportTexture(renderingData.cameraData.renderer.cameraDepthTargetHandle);

        passData.cascades = new TextureHandle[CascadesCount];
        const int scale = 4;
        int width = (2 << CascadesCount) * 2 * scale;
        int height = (1 << CascadesCount) * 3 * scale;
        for (int i = 0; i < CascadesCount; i++)
        {
            var desc = new TextureDesc(width, height)
            {
                colorFormat = GraphicsFormat.R16G16B16A16_SFloat,
                name = Cascade3dNames[i],
                enableRandomWrite = true
            };
            passData.cascades[i] = renderGraph.CreateTexture(desc);
            builder.UseTexture(passData.cascades[i]);
        }

        builder.ReadWriteTexture(passData.color);
        builder.ReadWriteTexture(passData.depth);

        builder.SetRenderFunc(static (PassData data, RasterGraphContext ctx) =>
        {
            data.pass.ExecutePass(ctx, ref data.renderingData, data.color, data.depth, data.cascades);
        });
    }
#endif


    public void Dispose()
    {
        for (int i = 0; i < _cascades.Length; i++)
        {
            _cascades[i]?.Release();
        }
    }

    private static string[] GenNames(string name, int n)
    {
        var names = new string[n];
        for (int i = 0; i < n; i++)
        {
            names[i] = name + i;
        }

        return names;
    }
}
