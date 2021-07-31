using SharpDX.DXGI;
using System.Threading;
using System;
using System.Drawing;

namespace HelloWindow
{
    
    using SharpDX;
    using SharpDX.Direct3D12;
    using SharpDX.Windows;
    public class HelloWindow : IDisposable
    {
        private RenderForm render_form_ = null;
        private Device device_;
        private CommandQueue cmdqe_;
        private SwapChain3 swapChain_;
        private int frameIdx_ = 0;
        private DescriptorHeap rtvHeap_;
        private int rtvDescriptorSize_;
        const int FrameCount = 2;
        private Resource[] renderTargets  = new Resource[FrameCount];
        private CommandAllocator commandAllocator_;

        private GraphicsCommandList cmdList_;
        private Fence fence_;
        private int fenceVal_;
        private AutoResetEvent fenceEvent_;

        // public HelloWindow()
        // {
        //     render_form_ = new RenderForm("hello");
        //     render_form_.ClientSize = new Size(width_, heigh_);
        //     render_form_.AllowUserResizing = false;
        // }

        public void Initialize(RenderForm form)
        {
            LoadPipeLine(form);
            LoadAssets();
        }

        private void LoadPipeLine(RenderForm form)
        {
            int width = form.ClientSize.Width;
            int height = form.ClientSize.Height;
#if DEBUG
            DebugInterface.Get().EnableDebugLayer();
#endif
            //init device
            device_ = new Device(null,SharpDX.Direct3D.FeatureLevel.Level_12_0);
            using (var factory = new Factory4())
            {
                CommandQueueDescription queueDesc = new CommandQueueDescription(CommandListType.Direct);
                cmdqe_ = device_.CreateCommandQueue(queueDesc);

                SwapChainDescription swapChainDesc = new SwapChainDescription()
                {
                    BufferCount = FrameCount,
                    ModeDescription = new ModeDescription(width, height, new Rational(60, 1), Format.R8G8B8A8_UNorm),
                    Usage = Usage.RenderTargetOutput,
                    SwapEffect = SwapEffect.FlipDiscard,
                    OutputHandle = form.Handle,
                    SampleDescription = new SampleDescription(1, 0),
                    IsWindowed = true
                };
                
                SwapChain tmpSC = new SwapChain(factory,cmdqe_,swapChainDesc);
                swapChain_ = tmpSC.QueryInterface<SwapChain3>();
                tmpSC.Dispose();
                frameIdx_ = swapChain_.CurrentBackBufferIndex;
            }

            DescriptorHeapDescription rtvHeapDesc = new DescriptorHeapDescription()
            {
                DescriptorCount = FrameCount,
                Flags = DescriptorHeapFlags.None,
                Type = DescriptorHeapType.RenderTargetView
            };

            rtvHeap_ = device_.CreateDescriptorHeap(rtvHeapDesc);
            rtvDescriptorSize_ = device_.GetDescriptorHandleIncrementSize(DescriptorHeapType.RenderTargetView);

            CpuDescriptorHandle rtvHandle = rtvHeap_.CPUDescriptorHandleForHeapStart;
            for (int i = 0; i < FrameCount; i++)
            {
                renderTargets[i] = swapChain_.GetBackBuffer<Resource>(i);
                device_.CreateRenderTargetView(renderTargets[i], null, rtvHandle);
                rtvHandle += rtvDescriptorSize_;
            }

            commandAllocator_ = device_.CreateCommandAllocator(CommandListType.Direct);
        }

        private void LoadAssets()
        {
            cmdList_ = device_.CreateCommandList(CommandListType.Direct, commandAllocator_, null);
            
            cmdList_.Close();

            fence_ = device_.CreateFence(0, FenceFlags.None);
            fenceVal_ = 1;
            fenceEvent_ = new AutoResetEvent(false);
        }


        private void PopulateCmdList()
        {
            commandAllocator_.Reset();
            cmdList_.Reset(commandAllocator_, null);
            cmdList_.ResourceBarrierTransition(renderTargets[frameIdx_],ResourceStates.Present,ResourceStates.RenderTarget);

            var rtvHandle = rtvHeap_.CPUDescriptorHandleForHeapStart;
            rtvHandle += frameIdx_ * rtvDescriptorSize_;
            cmdList_.ClearRenderTargetView(rtvHandle,new Color4(0,0.2F,0.2F,1.0f),0,null);
            
            cmdList_.ResourceBarrierTransition(renderTargets[frameIdx_],ResourceStates.RenderTarget,ResourceStates.Present);
            cmdList_.Close();
        }

        private void WaitForPreviousFrame()
        {
            int fence = fenceVal_;
            cmdqe_.Signal(this.fence_,fence);
            fenceVal_++;
            if (this.fence_.CompletedValue < fence)
            {
                this.fence_.SetEventOnCompletion(fence,fenceEvent_.SafeWaitHandle.DangerousGetHandle());
                fenceEvent_.WaitOne();
            }

            frameIdx_ = swapChain_.CurrentBackBufferIndex;
        }
        public void Run()
        {
            PopulateCmdList();
            cmdqe_.ExecuteCommandList(cmdList_);
            swapChain_.Present(1, 0);
            WaitForPreviousFrame();
        }
        

        public void Dispose()
        {
            foreach (var target in renderTargets)
            {
                target?.Dispose();
            }
            commandAllocator_.Dispose();
            cmdqe_.Dispose();
            rtvHeap_.Dispose();
            cmdList_.Dispose();
            fence_.Dispose();
            swapChain_.Dispose();
            device_.Dispose();
        }
    }
}