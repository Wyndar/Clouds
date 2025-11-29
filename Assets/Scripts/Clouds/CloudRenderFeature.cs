using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

public class CloudRenderFeature : ScriptableRendererFeature {
    class CloudRenderPass : ScriptableRenderPass {
        readonly string profilerTag = "Cloud Render";
        RTHandle cameraColorTarget;
        RTHandle tempColorTarget;

        public CloudRenderPass () {
            renderPassEvent = RenderPassEvent.AfterRenderingSkybox;
        }

        public void Setup (RTHandle colorTarget) {
            cameraColorTarget = colorTarget;
        }

        public override void OnCameraSetup (CommandBuffer cmd, ref RenderingData renderingData) {
            var descriptor = renderingData.cameraData.cameraTargetDescriptor;
            descriptor.depthBufferBits = 0;
            descriptor.msaaSamples = 1;
            RenderingUtils.ReAllocateIfNeeded (ref tempColorTarget, descriptor, FilterMode.Bilinear, TextureWrapMode.Clamp, name: "_CloudsTempTexture");
        }

        public override void Execute (ScriptableRenderContext context, ref RenderingData renderingData) {
            if (cameraColorTarget == null) {
                return;
            }

            var camera = renderingData.cameraData.camera;
            var cloudMaster = camera.GetComponent<CloudMaster> ();
            if (cloudMaster == null) {
                return;
            }

            if (!cloudMaster.TryPrepareMaterial (out var material)) {
                return;
            }

            var cmd = CommandBufferPool.Get (profilerTag);
            using (new ProfilingScope (cmd, new ProfilingSampler (profilerTag))) {
                cmd.SetRenderTarget (tempColorTarget);
                cmd.SetGlobalTexture ("_MainTex", cameraColorTarget);
                cmd.DrawMesh (RenderingUtils.fullscreenMesh, Matrix4x4.identity, material);

                Blitter.BlitCameraTexture (cmd, tempColorTarget, cameraColorTarget);
            }

            context.ExecuteCommandBuffer (cmd);
            CommandBufferPool.Release (cmd);
        }

        public override void OnCameraCleanup (CommandBuffer cmd) {
            cameraColorTarget = null;
        }

        public void Dispose () {
            tempColorTarget?.Release ();
        }
    }

    CloudRenderPass cloudPass;

    public override void Create () {
        cloudPass = new CloudRenderPass ();
    }

    public override void AddRenderPasses (ScriptableRenderer renderer, ref RenderingData renderingData) {
        if (cloudPass == null) {
            return;
        }

        cloudPass.Setup (renderer.cameraColorTargetHandle);
        renderer.EnqueuePass (cloudPass);
    }

    protected override void Dispose (bool disposing) {
        if (disposing) {
            cloudPass?.Dispose ();
        }
    }
}
