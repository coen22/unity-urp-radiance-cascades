using AlexMalyutinDev.RadianceCascades.BlurredColorBuffer;
using AlexMalyutinDev.RadianceCascades.MinMaxDepth;
using AlexMalyutinDev.RadianceCascades.SmoothedDepth;
using AlexMalyutinDev.RadianceCascades.VarianceDepth;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
#if UNITY_6000_1_OR_NEWER
using UnityEngine.Rendering.RenderGraphModule;
#endif

namespace AlexMalyutinDev.RadianceCascades
{
    public class RadianceCascadesFeature : ScriptableRendererFeature
    {
        public RadianceCascadeResources Resources;

        public bool showDebugView;

        private RC2dPass _rc2dPass;
        private RadianceCascades3dPass _radianceCascadesPass3d;
        private DirectionFirstRCPass _directionFirstRcPass;
        private VoxelizationPass _voxelizationPass;

        private MinMaxDepthPass _minMaxDepthPass;
        private SmoothedDepthPass _smoothedDepthPass;
        private VarianceDepthPass _varianceDepthPass;
        private BlurredColorBufferPass _blurredColorBufferPass;

        private RadianceCascadesRenderingData _radianceCascadesRenderingData;

        public override void Create()
        {
            _radianceCascadesRenderingData = new RadianceCascadesRenderingData();

            _rc2dPass = new RC2dPass(Resources, showDebugView)
            {
                renderPassEvent = RenderPassEvent.AfterRenderingDeferredLights
            };

            _voxelizationPass = new VoxelizationPass(Resources, _radianceCascadesRenderingData)
            {
                renderPassEvent = RenderPassEvent.AfterRenderingShadows,
            };
            _radianceCascadesPass3d = new RadianceCascades3dPass(Resources, _radianceCascadesRenderingData)
            {
                renderPassEvent = RenderPassEvent.AfterRenderingDeferredLights
            };

            // Direction First Passes
            _minMaxDepthPass = new MinMaxDepthPass(Resources.MinMaxDepthMaterial, _radianceCascadesRenderingData)
            {
                renderPassEvent = RenderPassEvent.AfterRenderingGbuffer
            };
            _smoothedDepthPass = new SmoothedDepthPass(Resources.SmoothedDepthMaterial, _radianceCascadesRenderingData)
            {
                renderPassEvent = RenderPassEvent.AfterRenderingGbuffer
            };
            _varianceDepthPass = new VarianceDepthPass(Resources.VarianceDepthMaterial, _radianceCascadesRenderingData)
            {
                renderPassEvent = RenderPassEvent.AfterRenderingGbuffer
            };
            _blurredColorBufferPass = new BlurredColorBufferPass(
                Resources.BlurredColorBufferMaterial,
                _radianceCascadesRenderingData
            )
            {
                renderPassEvent = RenderPassEvent.AfterRenderingDeferredLights
            };
            _directionFirstRcPass = new DirectionFirstRCPass(Resources, _radianceCascadesRenderingData)
            {
                renderPassEvent = RenderPassEvent.AfterRenderingDeferredLights,
            };
        }

#if UNITY_6000_1_OR_NEWER
        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            ref var renderingData = ref frameData.Get<RenderingData>();
            if (renderingData.cameraData.isPreviewCamera)
                return;

            var volume = VolumeManager.instance.stack.GetComponent<RadianceCascades>();
            var renderType = volume.RenderingType.value;
            if (!volume.active || renderType == RenderingType.None)
                return;

            _radianceCascadesRenderingData.Cascade0Size = new Vector2Int(2048 / 8, 1024 / 8);

            if (renderType == RenderingType.Simple2dProbes)
            {
                _rc2dPass.RecordRenderGraph(renderGraph, renderingData);
            }
            else if (renderType == RenderingType.CubeMapProbes)
            {
                _radianceCascadesPass3d.RecordRenderGraph(renderGraph, renderingData);
            }
            else if (renderType == RenderingType.DirectionFirstProbes)
            {
                _directionFirstRcPass.RecordRenderGraph(renderGraph, renderingData);
            }
        }
#endif

        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
        {
            if (renderingData.cameraData.isPreviewCamera)
            {
                return;
            }

            var volume = VolumeManager.instance.stack.GetComponent<RadianceCascades>();
            var renderType = volume.RenderingType.value;
            if (!volume.active || renderType == RenderingType.None)
            {
                return;
            }

            _radianceCascadesRenderingData.Cascade0Size = new Vector2Int(2048 / 8, 1024 / 8);

            if (renderType == RenderingType.Simple2dProbes)
            {
                renderer.EnqueuePass(_rc2dPass);
            }
            else if (renderType == RenderingType.CubeMapProbes)
            {
                renderer.EnqueuePass(_voxelizationPass);
                renderer.EnqueuePass(_radianceCascadesPass3d);
            }
            else if (renderType == RenderingType.DirectionFirstProbes)
            {
                renderer.EnqueuePass(_minMaxDepthPass);
                renderer.EnqueuePass(_varianceDepthPass);
                renderer.EnqueuePass(_blurredColorBufferPass);
                renderer.EnqueuePass(_directionFirstRcPass);
            }
        }


        protected override void Dispose(bool disposing)
        {
            _rc2dPass?.Dispose();
            _radianceCascadesPass3d?.Dispose();
            _voxelizationPass?.Dispose();
            _minMaxDepthPass?.Dispose();
        }
    }
}
