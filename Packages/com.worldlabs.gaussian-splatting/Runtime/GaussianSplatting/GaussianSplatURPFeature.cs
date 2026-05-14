// SPDX-License-Identifier: MIT
#if GS_ENABLE_URP

using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

namespace GaussianSplatting.Runtime
{
    // Note: I have no idea what is the purpose of ScriptableRendererFeature vs ScriptableRenderPass, which one of those
    // is supposed to do resource management vs logic, etc. etc. Code below "seems to work" but I'm just fumbling along,
    // without understanding any of it.
    //
    // ReSharper disable once InconsistentNaming
    class GaussianSplatURPFeature : ScriptableRendererFeature
    {
        class GSRenderPass : ScriptableRenderPass
        {
            class PassData
            {
                public Camera camera;
                public TextureHandle splatTarget;
                public TextureHandle cameraColor;
            }

            RTHandle m_RenderTarget;
            internal ScriptableRenderer m_Renderer = null;
            internal CommandBuffer m_Cmb = null;

            public void Dispose()
            {
                m_RenderTarget?.Release();
            }

            public override void OnCameraSetup(CommandBuffer cmd, ref RenderingData renderingData)
            {
                RenderTextureDescriptor rtDesc = renderingData.cameraData.cameraTargetDescriptor;
                rtDesc.depthBufferBits = 0;
                rtDesc.msaaSamples = 1;
                rtDesc.graphicsFormat = GraphicsFormat.R16G16B16A16_SFloat;
                RenderingUtils.ReAllocateIfNeeded(ref m_RenderTarget, rtDesc, FilterMode.Point, TextureWrapMode.Clamp, name: "_GaussianSplatRT");
                cmd.SetGlobalTexture(m_RenderTarget.name, m_RenderTarget.nameID);

                ConfigureTarget(m_RenderTarget);
                ConfigureClear(ClearFlag.Color, new Color(0,0,0,0));
            }

            public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
            {
                if (m_Cmb == null)
                    return;

                // add sorting, view calc and drawing commands for each splat object
                Material matComposite = GaussianSplatRenderSystem.instance.SortAndRenderSplats(renderingData.cameraData.camera, m_Cmb);

                // compose
                m_Cmb.BeginSample(GaussianSplatRenderSystem.s_ProfCompose);
                Blitter.BlitCameraTexture(m_Cmb, m_RenderTarget, m_Renderer.cameraColorTargetHandle, RenderBufferLoadAction.Load, RenderBufferStoreAction.Store, matComposite, 0);
                m_Cmb.EndSample(GaussianSplatRenderSystem.s_ProfCompose);
                context.ExecuteCommandBuffer(m_Cmb);
            }

            public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
            {
                UniversalCameraData cameraData = frameData.Get<UniversalCameraData>();
                UniversalResourceData resourceData = frameData.Get<UniversalResourceData>();

                TextureDesc splatDesc = new TextureDesc(cameraData.cameraTargetDescriptor);
                splatDesc.name = "_GaussianSplatRT";
                splatDesc.depthBufferBits = DepthBits.None;
                splatDesc.format = GraphicsFormat.R16G16B16A16_SFloat;
                splatDesc.filterMode = FilterMode.Point;
                splatDesc.wrapMode = TextureWrapMode.Clamp;
                splatDesc.msaaSamples = MSAASamples.None;
                splatDesc.clearBuffer = true;
                splatDesc.clearColor = new Color(0, 0, 0, 0);

                TextureHandle splatTarget = renderGraph.CreateTexture(splatDesc);
                TextureHandle cameraColor = resourceData.activeColorTexture;

                using (IUnsafeRenderGraphBuilder builder = renderGraph.AddUnsafePass<PassData>("Render Gaussian Splats", out var passData))
                {
                    passData.camera = cameraData.camera;
                    passData.splatTarget = splatTarget;
                    passData.cameraColor = cameraColor;

                    builder.AllowPassCulling(false);
                    builder.AllowGlobalStateModification(true);
                    builder.UseTexture(passData.splatTarget, AccessFlags.ReadWrite);
                    builder.UseTexture(passData.cameraColor, AccessFlags.ReadWrite);

                    builder.SetRenderFunc((PassData data, UnsafeGraphContext rgContext) =>
                    {
                        CommandBuffer cmd = CommandBufferHelpers.GetNativeCommandBuffer(rgContext.cmd);

                        cmd.SetRenderTarget(data.splatTarget);
                        cmd.ClearRenderTarget(false, true, new Color(0, 0, 0, 0));

                        Material matComposite = GaussianSplatRenderSystem.instance.SortAndRenderSplats(data.camera, cmd);
                        if (matComposite == null)
                            return;

                        cmd.SetGlobalTexture(GaussianSplatRenderer.Props.GaussianSplatRT, data.splatTarget);

                        cmd.BeginSample(GaussianSplatRenderSystem.s_ProfCompose);
                        Blitter.BlitCameraTexture(cmd, data.splatTarget, data.cameraColor, RenderBufferLoadAction.Load, RenderBufferStoreAction.Store, matComposite, 0);
                        cmd.EndSample(GaussianSplatRenderSystem.s_ProfCompose);
                    });
                }
            }
        }

        GSRenderPass m_Pass;
        bool m_HasCamera;

        public override void Create()
        {
            m_Pass = new GSRenderPass
            {
                renderPassEvent = RenderPassEvent.BeforeRenderingTransparents
            };
        }

        public override void OnCameraPreCull(ScriptableRenderer renderer, in CameraData cameraData)
        {
            m_HasCamera = false;
            var system = GaussianSplatRenderSystem.instance;
            if (!system.GatherSplatsForCamera(cameraData.camera))
                return;

            CommandBuffer cmb = system.InitialClearCmdBuffer(cameraData.camera);
            m_Pass.m_Cmb = cmb;
            m_HasCamera = true;
        }

        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
        {
            if (!m_HasCamera)
                return;
            m_Pass.m_Renderer = renderer;
            renderer.EnqueuePass(m_Pass);
        }

        protected override void Dispose(bool disposing)
        {
            m_Pass?.Dispose();
            m_Pass = null;
        }
    }
}

#endif // #if GS_ENABLE_URP
