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
#if DEBUG || _DEBUG 
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
                    //the window we render to
                    OutputHandle = form.Handle,
                    //no multi sample
                    SampleDescription = new SampleDescription(1, 0),//The default sampler mode, with no anti-aliasing, has a count of 1 and a quality level of 0
                    IsWindowed = true
                };
                
                Console.WriteLine("{0} Adapters!",factory.Adapters.Length);
                foreach (var adapter in factory.Adapters)
                {
                    var desc = adapter.Description;
                    Console.WriteLine("Adapter: " + desc.Description);
                    foreach (var otpt in adapter.Outputs)
                    {
                        Console.WriteLine("\tOutput: " + otpt.Description.DeviceName);
                        // Console.WriteLine("\t\tOutput Parma: " + otpt.GetDisplayModeList(Format.B8G8R8A8_UNorm,0));
                        var lst = otpt.GetDisplayModeList(Format.B8G8R8A8_UNorm, 0);
                        Console.WriteLine(lst.Length);
                        for (int i = 0; i < lst.Length; i++)
                        {
                            Console.WriteLine("{0} {1} {2}", lst[i].RefreshRate, lst[i].Width, lst[i].Height);
                        }

                    }
                }
                
                
                //?????????GPU cmdqueue??????
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
            //Gets the size of the handle increment for the given type of descriptor heap
            rtvDescriptorSize_ = device_.GetDescriptorHandleIncrementSize(DescriptorHeapType.RenderTargetView);

            FeatureDataMultisampleQualityLevels lv = new FeatureDataMultisampleQualityLevels()
            {
                Format = Format.R8G8B8A8_UNorm,
                SampleCount = 4,
                Flags = MultisampleQualityLevelFlags.None,
                QualityLevelCount = 0
            };
            device_.CheckFeatureSupport(Feature.MultisampleQualityLevels, ref lv);
            Console.WriteLine(lv.QualityLevelCount); // must > 0

            //??????????????????????????????
            CpuDescriptorHandle rtvHandle = rtvHeap_.CPUDescriptorHandleForHeapStart;
            for (int i = 0; i < FrameCount; i++)
            {
                //?????????????????????????????????
                renderTargets[i] = swapChain_.GetBackBuffer<Resource>(i);
                device_.CreateRenderTargetView(renderTargets[i], null, rtvHandle);
                //???????????????????????????????????????????????????????????????????????????????????????
                rtvHandle += rtvDescriptorSize_;
            }

            commandAllocator_ = device_.CreateCommandAllocator(CommandListType.Direct);
        }

        private void LoadAssets()
        {
            //?????????Direct????????????Bundle??????????????????
            //????????????????????????????????????
            cmdList_ = device_.CreateCommandList(CommandListType.Direct, commandAllocator_, null);
            
            cmdList_.Close();

            fence_ = device_.CreateFence(0, FenceFlags.None);
            fenceVal_ = 1;
            fenceEvent_ = new AutoResetEvent(false);
        }


        private void PopulateCmdList()
        {
            //??????????????????????????????????????????Allocator???
            //???cmdlist????????????????????????????????????????????????????????????????????????????????????????????????GPU????????????queue???????????????????????????????????????,???????????????Fence
            commandAllocator_.Reset();
            cmdList_.Reset(commandAllocator_, null);
            //RenderTarget?????????GPU???
            //???:???????????????
            
            cmdList_.ResourceBarrierTransition(renderTargets[frameIdx_],ResourceStates.Present,ResourceStates.RenderTarget);

            var rtvHandle = rtvHeap_.CPUDescriptorHandleForHeapStart;
            rtvHandle += frameIdx_ * rtvDescriptorSize_;
            cmdList_.ClearRenderTargetView(rtvHandle,new Color4(0,0.2F,0.2F,1.0f),0,null);
            
            cmdList_.ResourceBarrierTransition(renderTargets[frameIdx_],ResourceStates.RenderTarget,ResourceStates.Present);
            //cmdlist????????????????????????????????????????????????
            cmdList_.Close();
        }

        private void WaitForPreviousFrame()
        {
            int fence = fenceVal_;
            //this->fence_????????????0???fenceVal????????????1???CPU???????????????????????????ringbuffer,??????GPU???1?????????????????????
            cmdqe_.Signal(this.fence_,fence);
            //CPU???????????????+1????????????????????????
            fenceVal_++;
            //CPU?????????GPU???????????????GPU?????????????????????fence_.CompletedValue????????????0
            if (this.fence_.CompletedValue < fence)
            {
                this.fence_.SetEventOnCompletion(fence,fenceEvent_.SafeWaitHandle.DangerousGetHandle());
                //??????GPU??????
                fenceEvent_.WaitOne();
            }

            //GPU?????????????????????fence????????????????????????1??????fenceVal???????????????2 
            frameIdx_ = swapChain_.CurrentBackBufferIndex;
        }
        public void Run()
        {
            PopulateCmdList();
            //??????????????????????????????
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