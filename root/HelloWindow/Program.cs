using System;
using SharpDX.Windows;

namespace HelloWindow
{
    class Program
    {
        [STAThread]
        //https://zhuanlan.zhihu.com/p/135313476
        //和muduo的事件循环类似，事件被push到队列，由自己线程来处理
        static void Main(string[] args)
        {
            var form = new RenderForm("hello")
            {
                Width = 1280, Height = 800
            };
            form.Show();
            
            using (HelloWindow win = new HelloWindow())
            {
                win.Initialize(form);
                using (var loop = new RenderLoop(form))
                {
                    while (loop.NextFrame())
                    {
                        win.Run();
                    }
                }
                
            }
        }
    }
}