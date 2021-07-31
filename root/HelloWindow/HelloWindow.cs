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
                
                
                //需要向GPU cmdqueue更新
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

            //把指针移动到数组开头
            CpuDescriptorHandle rtvHandle = rtvHeap_.CPUDescriptorHandleForHeapStart;
            for (int i = 0; i < FrameCount; i++)
            {
                //对数组的每个元素初始化
                renderTargets[i] = swapChain_.GetBackBuffer<Resource>(i);
                device_.CreateRenderTargetView(renderTargets[i], null, rtvHandle);
                //移动指针到下一个结构体，所以指针需要移动一个结构体字节的量
                rtvHandle += rtvDescriptorSize_;
            }

            commandAllocator_ = device_.CreateCommandAllocator(CommandListType.Direct);
        }

        private void LoadAssets()
        {
            //普通用Direct，虽然有Bundle但是没必要用
            //第二个参数传入内存分配器
            cmdList_ = device_.CreateCommandList(CommandListType.Direct, commandAllocator_, null);
            
            cmdList_.Close();

            fence_ = device_.CreateFence(0, FenceFlags.None);
            fenceVal_ = 1;
            fenceEvent_ = new AutoResetEvent(false);
        }


        private void PopulateCmdList()
        {
            //命令相关的结构体内存都存放在Allocator上
            //把cmdlist清空以复用内存，看龙书。一定要在确定上一帧执行完毕以后才能清空，GPU里的命令queue可能有指针引用着这里的内存,需要用围栏Fence
            commandAllocator_.Reset();
            cmdList_.Reset(commandAllocator_, null);
            //RenderTarget状态：GPU写
            //读:着色器资源
            
            cmdList_.ResourceBarrierTransition(renderTargets[frameIdx_],ResourceStates.Present,ResourceStates.RenderTarget);

            var rtvHandle = rtvHeap_.CPUDescriptorHandleForHeapStart;
            rtvHandle += frameIdx_ * rtvDescriptorSize_;
            cmdList_.ClearRenderTargetView(rtvHandle,new Color4(0,0.2F,0.2F,1.0f),0,null);
            
            cmdList_.ResourceBarrierTransition(renderTargets[frameIdx_],ResourceStates.RenderTarget,ResourceStates.Present);
            //cmdlist停止记录命令，准备提交到命令队列
            cmdList_.Close();
        }

        private void WaitForPreviousFrame()
        {
            int fence = fenceVal_;
            //this->fence_初始值为0，fenceVal初始值为1，CPU推送一条围栏命令到ringbuffer,要求GPU在1的时候给个信号
            cmdqe_.Signal(this.fence_,fence);
            //CPU端把围栏值+1，以备下一次围栏
            fenceVal_++;
            //CPU端等待GPU信号，如果GPU没有完成信号，fence_.CompletedValue仍然等于0
            if (this.fence_.CompletedValue < fence)
            {
                this.fence_.SetEventOnCompletion(fence,fenceEvent_.SafeWaitHandle.DangerousGetHandle());
                //等待GPU信号
                fenceEvent_.WaitOne();
            }

            //GPU执行完毕，此时fence结构体的值已经是1，而fenceVal的值已经是2 
            frameIdx_ = swapChain_.CurrentBackBufferIndex;
        }
        public void Run()
        {
            PopulateCmdList();
            //只有到这一步才会执行
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