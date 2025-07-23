using System;
using AlexMalyutinDev.RadianceCascades;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
#if UNITY_6000_1_OR_NEWER
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
#endif

public class RC2dPass : ScriptableRenderPass, IDisposable
{
    private const int CascadesCount = 5;
    private static readonly string[] CascadeNames = GenNames("_Cascade", CascadesCount);
    private static readonly Vector2Int[] Resolutions =
    {
        new(32 * 16, 32 * 9), // 256x144 probes0
        new(32 * 10, 32 * 6), // 160x96 probes0
        new(32 * 7, 32 * 4), // 112x64 probes0
        new(32 * 4, 32 * 3), // 64x48 probes0
        new(32 * 3, 32 * 2), // 48x32 probes0
    };

    private readonly ProfilingSampler _profilingSampler;
    private readonly Material _blit;
    private readonly SimpleRadianceCascadesCS _radianceCascadeCs;
    private readonly bool _showDebugPreview;

    private readonly RTHandle[] _cascades = new RTHandle[CascadesCount];


    public RC2dPass(
        RadianceCascadeResources resources,
        bool showDebugView
    )
    {
        _profilingSampler = new ProfilingSampler(nameof(RC2dPass));
        _radianceCascadeCs = new SimpleRadianceCascadesCS(resources.RadianceCascades);
        _showDebugPreview = showDebugView;
        _blit = resources.BlitMaterial;

        // BUG: Configuring with Depth and Color buffer dependency will cause to additional
        // resolve of this buffers before RadianceCascadesPass
        // ConfigureInput(ScriptableRenderPassInput.Depth | ScriptableRenderPassInput.Color);
    }

    [Obsolete("Use RecordRenderGraph", true)]
    public override void Configure(CommandBuffer cmd, RenderTextureDescriptor cameraTextureDescriptor)
    {
        // TODO: Resolution settings?
        var desc = new RenderTextureDescriptor(
            Resolutions[0].x,
            Resolutions[0].y
        )
        {
            colorFormat = RenderTextureFormat.ARGBHalf,
            enableRandomWrite = true,
        };

        for (int i = 0; i < _cascades.Length; i++)
        {
            RenderingUtils.ReAllocateIfNeeded(
                ref _cascades[i],
                desc,
                filterMode: FilterMode.Point,
                wrapMode: TextureWrapMode.Clamp,
                name: CascadeNames[i]
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

        using (new ProfilingScope(cmd, _profilingSampler))
        {
            RenderCascades(renderingData, cmd, colorTexture, depthTexture);
            context.ExecuteCommandBuffer(cmd);
            cmd.Clear();
        }

        context.ExecuteCommandBuffer(cmd);
        cmd.Clear();

        CommandBufferPool.Release(cmd);
    }

    private void RenderCascades(
        RenderingData renderingData,
        CommandBuffer cmd,
        RTHandle colorTexture,
        RTHandle depthTexture
    )
    {
        var sampleKey = "RenderCascades";
        cmd.BeginSample(sampleKey);
        {
            for (int level = 0; level < _cascades.Length; level++)
            {
                // TODO: Use Hi-Z Depth
                _radianceCascadeCs.RenderCascade(
                    cmd,
                    colorTexture,
                    depthTexture,
                    2 << level,
                    level,
                    _cascades[level]
                );
            }
        }
        cmd.EndSample(sampleKey);

        if (_showDebugPreview)
        {
            PreviewCascades(cmd, _cascades);
        }

        sampleKey = "MergeCascades";
        cmd.BeginSample(sampleKey);
        {
            for (int level = _cascades.Length - 1; level > 0; level--)
            {
                var lowerLevel = level - 1;
                _radianceCascadeCs.MergeCascades(
                    cmd,
                    _cascades[lowerLevel],
                    _cascades[level],
                    lowerLevel
                );
            }
        }
        cmd.EndSample(sampleKey);

        if (_showDebugPreview)
        {
            PreviewCascades(cmd, _cascades, 1.0f);
        }

        sampleKey = "Combine";
        cmd.BeginSample(sampleKey);
        {
            cmd.SetRenderTarget(
                colorTexture,
                RenderBufferLoadAction.Load,
                RenderBufferStoreAction.Store,
                depthTexture,
                RenderBufferLoadAction.Load,
                RenderBufferStoreAction.Store
            );
            // TODO: Do blit into intermediate buffer with bilinear filter, then blit onto the screen
            BlitUtils.BlitTexture(cmd, _cascades[0], _blit, 0);
        }
        cmd.EndSample(sampleKey);
    }

    private void PreviewCascades(CommandBuffer cmd, RTHandle[] rtHandles, float offset = 0.0f)
    {
        cmd.BeginSample("Preview");

        const float scale = 1f / 8f;
        for (int i = 0; i < rtHandles.Length; i++)
        {
            Blitter.BlitQuad(
                cmd,
                rtHandles[i],
                new Vector4(1, 1f, 0, 0),
                new Vector4(scale, scale, scale * offset, 1.0f - scale * (i + 1)),
                0,
                false
            );
        }

        cmd.EndSample("Preview");
    }

#if UNITY_6000_1_OR_NEWER
    private class PassData
    {
        public RenderingData renderingData;
        public RC2dPass pass;
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

        using (new ProfilingScope(cmd, _profilingSampler))
        {
            RenderCascadesRG(ctx, renderingData, cmd, color, depth, cascades);
        }
    }

    private static void RenderCascadesRG(
        in RasterGraphContext ctx,
        in RenderingData renderingData,
        CommandBuffer cmd,
        TextureHandle color,
        TextureHandle depth,
        TextureHandle[] cascades
    )
    {
        var colorRT = color.rt;
        var depthRT = depth.rt;

        for (int level = 0; level < cascades.Length; level++)
        {
            var target = cascades[level].rt;
            _radianceCascadeCs.RenderCascade(cmd, colorRT, depthRT, 2 << level, level, target);
        }

        for (int level = cascades.Length - 1; level > 0; level--)
        {
            var lower = cascades[level - 1].rt;
            var upper = cascades[level].rt;
            _radianceCascadeCs.MergeCascades(cmd, lower, upper, level - 1);
        }

        cmd.SetRenderTarget(
            colorRT,
            RenderBufferLoadAction.Load,
            RenderBufferStoreAction.Store,
            depthRT,
            RenderBufferLoadAction.Load,
            RenderBufferStoreAction.Store
        );
        RenderGraphUtils.BlitTexture(ctx, cascades[0], _blit, 0);
    }

    public void RecordRenderGraph(RenderGraph renderGraph, in RenderingData renderingData)
    {
        using var builder = renderGraph.AddRasterRenderPass<PassData>(nameof(RC2dPass), out var passData);
        passData.renderingData = renderingData;
        passData.pass = this;

        passData.color = renderGraph.ImportTexture(renderingData.cameraData.renderer.cameraColorTargetHandle);
        passData.depth = renderGraph.ImportTexture(renderingData.cameraData.renderer.cameraDepthTargetHandle);

        passData.cascades = new TextureHandle[CascadesCount];
        for (int i = 0; i < CascadesCount; i++)
        {
            var desc = new TextureDesc(Resolutions[i].x, Resolutions[i].y)
            {
                colorFormat = GraphicsFormat.R16G16B16A16_SFloat,
                name = CascadeNames[i],
                enableRandomWrite = true
            };
            passData.cascades[i] = renderGraph.CreateTexture(desc);
            builder.UseTexture(passData.cascades[i]);
        }

        builder.UseTexture(passData.color);
        builder.UseTexture(passData.depth);

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
